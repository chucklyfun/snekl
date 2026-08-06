# Auth ring architecture

Snekl separates **authentication servers** (identity, login, server subscriptions) from **content servers** (forums, posts, moderation). Users maintain a **Decentralized Identifier (DID)** that is stable across auth servers and content — the canonical author identity.

## Layers

```
┌─────────────────────────────────────────────────────────────┐
│  Client (React) — holds did:key private key (optional)       │
└───────────────┬─────────────────────────────┬───────────────┘
                │                             │
                ▼                             ▼
┌───────────────────────────┐   ┌─────────────────────────────┐
│  Auth servers (ring)       │   │  Content servers             │
│  did:web, login, VCs       │   │  forums, posts, trust JWTs   │
└───────────────────────────┘   └─────────────────────────────┘
         mutual trust                      trusts many issuers
```

## DIDs in Snekl

| Actor | DID method | Example | Purpose |
|-------|-----------|---------|---------|
| **User** | `did:key` | `did:key:z6Mk…` | Stable author identity; derived from Ed25519 public key |
| **Auth server** | `did:web` | `did:web:auth.example.com` | Issuer identity; DID document at `/.well-known/did.json` |

The user's **`did:key`** replaces the opaque PersonCluster GUID as the portable identity. An internal GUID may still exist for database relations, but **content servers and federation use the DID**.

### Why did:key for users

- No registry required — the identifier *is* the public key
- User can generate and hold the key client-side (self-sovereign)
- Same DID works on any auth server, trusted or not
- Standard W3C DID Core + did:key method

### Why did:web for auth servers

- Natural fit for hosted auth services
- DID document served at `/.well-known/did.json`
- Contains Ed25519 verification key for signing Verifiable Credentials

## Person cluster (one human, one author)

A **PersonCluster** binds local auth credentials to a user's **`did:key`**:

| Field | Purpose |
|-------|---------|
| `Did` | Canonical W3C DID — stored on posts as `AuthorDid` |
| `CanonicalUsername` | Same everywhere, e.g. `alice` |
| `PrimaryAuthServerUrl` | Default source for disambiguation tag |
| `DisambiguationTag` | Optional override when username collides |

Each auth server holds one or more **AuthIdentity** rows linked to the same DID:

```
did:key:z6Mk… (alice)
├── auth-alpha:  sub=uuid-a  (password login)
├── auth-beta:   sub=uuid-b  (vouched or imported)
└── auth-gamma:  sub=uuid-c  (identity credential import)
```

Content servers resolve any trusted `(iss, sub)` JWT to the same **`AuthorDid`**. All posts and presentation data use the DID — **same author** regardless of which auth server issued the token.

## Three ways to link identity across auth servers

| Path | Trust required? | Mechanism |
|------|-----------------|-----------|
| **Partner trust + vouch** | Mutual partner trust | Signed vouch between ring partners (existing) |
| **Identity credential import** | None (crypto only) | User exports VC from auth A, imports on auth B |
| **Client-held did:key** | None | User registers with existing DID + ownership proof |

### Identity credential (untrusted servers)

When auth servers do **not** mutually trust each other, the user carries a **Verifiable Credential**:

1. User logs into auth-alpha, calls `POST /api/me/export`.
2. Auth-alpha issues a `SneklIdentityCredential` signed with its `did:web` verification key.
3. User presents the credential JSON to auth-beta via `POST /api/auth/import`.
4. Auth-beta resolves auth-alpha's `did:web` document, verifies the signature, and creates a local identity bound to the **same `did:key`**.
5. User sets a new password on beta — beta does not delegate authentication to alpha.

This separates **identity continuity** (same DID) from **auth federation** (ring login, which still requires mutual trust).

### Client-provided did:key

On register, the client may supply:

```json
{
  "username": "alice",
  "did": "did:key:z6Mk…",
  "keyOwnershipProof": "z…"
}
```

The proof is an Ed25519 signature over `snekl-register:{username}:{did}`. The server verifies against the DID's embedded public key before accepting registration.

If no DID is supplied, the server generates a `did:key` and returns the private key once in the register response (dev convenience; production clients should generate locally).

## Mutual partner trust

Auth servers A and B are partners only when **both** have an `AuthPartnerTrust` row.

- Vouched provisioning works only between mutual partners.
- Ring login extends sessions only to mutual partners in the same connected component.
- Partners exchange signing keys for vouch validation.

Ring login and partner vouches remain **inside trusted groups**. DIDs and identity credentials work **everywhere**.

## Ring login

1. User logs into auth-alpha with username/password.
2. Auth-alpha creates a local session and a **RingSession** id.
3. Auth-alpha sends signed vouches (including `personDid`) to each mutual partner.
4. Partners validate trust + signature and issue their own access tokens for the linked identity.
5. Client receives `ringSessionId` and `sessions[]` for all partners.

## Username and disambiguation

- Login username is `alice` on each auth server the user owns.
- Display defaults to `alice` when no collision exists on the current server.
- When two clusters share `alice` on the same server, show `alice · {tag}`.
- Tag defaults to short name of **primary auth server**; user may customize.

## Content server integration

Content servers:

1. Configure **TrustedAuthIssuer** (JWKS URL per auth server).
2. Validate JWT `iss` + signature; read `did` claim.
3. Map `(iss, sub)` → `AuthorDid` via `AuthorIdentityMap`.
4. Store posts with `AuthorDid` and optional legacy `AuthorPersonClusterId`.
5. Federation payloads include `authorDid` and `canonicalUsername`.

## API summary (auth server)

| Method | Path | Description |
|--------|------|-------------|
| POST | `/api/auth/register` | Create identity with new or client-provided `did:key` |
| POST | `/api/auth/import` | Register via exported identity credential |
| POST | `/api/auth/login` | Local login + ring sessions |
| POST | `/api/me/export` | Export signed identity credential |
| GET | `/api/me` | Cluster profile + linked identities |
| GET | `/.well-known/did.json` | Auth server DID document |
| GET | `/.well-known/jwks.json` | JWT verification keys |
| GET | `/api/did/resolve/{did}` | Resolve `did:key` to DID document |

## Related documents

- Root [README.md](../README.md) — running content stack
- [W3C DID Core](https://www.w3.org/TR/did-1.1/)
- [did:key method](https://w3c-ccg.github.io/did-key-spec/)
