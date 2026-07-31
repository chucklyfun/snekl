# Auth ring architecture

Snekl separates **authentication servers** (identity, login, server subscriptions) from **content servers** (forums, posts, moderation). Users can maintain accounts on multiple auth servers that **mutually trust** each other, log in once across the ring, and appear as **one author** on content.

## Layers

```
┌─────────────────────────────────────────────────────────────┐
│  Client (React)                                              │
└───────────────┬─────────────────────────────┬───────────────┘
                │                             │
                ▼                             ▼
┌───────────────────────────┐   ┌─────────────────────────────┐
│  Auth servers (ring)       │   │  Content servers             │
│  login, PersonCluster,     │   │  forums, posts, local mods   │
│  settings replicas         │   │  trust auth JWTs             │
└───────────────────────────┘   └─────────────────────────────┘
         mutual trust                      trusts many issuers
```

## Person cluster (one human, one author)

A **PersonCluster** is the stable author identity used on content servers and in federation.

| Field | Purpose |
|-------|---------|
| `Id` | Global GUID — stored on posts as `AuthorPersonClusterId` |
| `CanonicalUsername` | Same everywhere, e.g. `alice` |
| `PrimaryAuthServerUrl` | Default source for disambiguation tag |
| `DisambiguationTag` | Optional override when username collides |

Each auth server holds one or more **AuthIdentity** rows linked to the same cluster:

```
PersonCluster alice (pc-uuid)
├── auth-alpha:  sub=uuid-a  (password login)
├── auth-beta:   sub=uuid-b  (vouched from alpha)
└── auth-gamma:  sub=uuid-c  (vouched)
```

Content servers resolve any trusted `(iss, sub)` JWT to the same `PersonClusterId`. All posts and presentation data use the cluster — **same author** regardless of which auth server issued the token.

## Mutual partner trust

Auth servers A and B are partners only when **both** have an `AuthPartnerTrust` row.

- Vouched provisioning works only between mutual partners.
- Ring login extends sessions only to mutual partners in the same connected component.
- Partners exchange JWKS URLs for token verification.

## Ring login

1. User logs into auth-alpha with username/password.
2. Auth-alpha creates a local session and a **RingSession** id.
3. Auth-alpha sends signed assertions to each mutual partner.
4. Partners validate trust + signature and issue their own access tokens for the linked identity.
5. Client receives `ringSessionId` and `sessions[]` for all partners.

If auth-alpha goes offline, the user logs into auth-beta with beta's password — same PersonCluster, same content author id.

## Vouched provisioning

While logged into auth-alpha, user requests partner auth-beta:

1. Auth-alpha sends signed vouch: cluster id, username, subject id, profile snapshot.
2. Auth-beta verifies mutual trust + signature.
3. Auth-beta creates AuthIdentity linked to the **same** PersonCluster (same cluster id from vouch).
4. User may never set a password on beta, or set one later for recovery.

Creating on a partner means that server **trusts auth-alpha's authentication** for identity binding, not that the user skips security entirely.

## Username and disambiguation

- Login username is `alice` on each auth server the user owns.
- Display defaults to `alice` when no collision exists on the current server.
- When two clusters share `alice` on the same server, show `alice · {tag}`.
- Tag defaults to short name of **primary auth server**; user may customize (e.g. `codes` → `alice · codes`).

## Settings replicas

Each auth server stores a **UserSettingsReplica** (profile JSON, content-server memberships). Updates can sync to mutual partners. Content servers may still apply **local presentation overrides** per node.

## Content server integration

Content servers:

1. Configure **TrustedAuthIssuer** (JWKS URL per auth server).
2. Validate JWT `iss` + signature.
3. Map `(iss, sub)` → `PersonClusterId` via `AuthorIdentityMap`.
4. Store posts with `AuthorPersonClusterId`.
5. Federation payloads include `authorClusterId` and `canonicalUsername`.

Legacy local `User` + password on content nodes remains for development; production path uses external auth tokens.

## API summary (auth server)

| Method | Path | Description |
|--------|------|-------------|
| POST | `/api/auth/register` | Create cluster + identity + password |
| POST | `/api/auth/login` | Local login + ring sessions |
| POST | `/api/partners/trust` | Establish mutual trust (admin) |
| POST | `/api/partners/provision` | Vouch user onto partner (authenticated) |
| GET | `/api/me` | Cluster profile + linked identities |
| GET | `/.well-known/jwks.json` | Public keys for content servers |

## Related documents

- Root [README.md](../README.md) — running content stack
- Federation inbox — content server ↔ content server sync (separate from auth ring)
