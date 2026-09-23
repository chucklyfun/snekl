# Hub feed architecture (v1)

Named federations use **official hubs** as the home-feed index and federation config host. The member client is **portable** (SPA, later mobile/desktop)—it is not “the hub you opened.” Content **origins** remain canonical for threads and replies. Hubs do **not** receive full post replication for peer local DBs; they receive **feed cards** and lossy **score patches**.

Related: [Auth ring](architecture-auth-ring.md). Replaces the “broadcast every post to every peer inbox for a unified local timeline” model for the user-facing home feed.

## Goals

- Portable member client: one SPA/app talks to Auth + any federations and content origins the account lists
- Ranked home feed: within a federation the hub ranks; across federations the client merges
- Consistent display: cards deep-link to origin **data**; the client is not bound to a hub hostname
- No user propagation when a server joins a federation
- No per-upvote traffic to the hub
- Forum-first membership; server visitor is implicit
- Hub-owned federation registry (nodes acknowledge; no full peer-mesh sync)
- Auth as a **separate webservice**; the client remembers one Auth URL (typed at login)

## Non-goals (v1)

- Federation-wide roles or ACLs
- Client re-ranking **inside** a single federation’s hub feed (hub owns that slice)
- Hub thumbnail caching (hotlink origin media)
- Realtime notifications (later; can reuse card/score channels)
- Cross-federation account sync (SPA must not send other-federation data to a home hub)
- Picking a multi-region LB product (soft constraints only; see [Hub scale](#hub-scale))
- Embedding Account.Api inside the Hub process

## Deployment and entrance

| Surface | Host |
|---------|------|
| Member client (home, discover, thread UI, login form) | Portable SPA (`:5173` in dev) or later native apps — not hosted on the hub |
| Auth webservice (login/register/session + account lists) | **Separate** Account service (`:5100`) |
| Federation config document + feed index | Hub (or any HTTPS host that serves the config contract) |
| Thread/reply/vote/moderation **API** + media | Origin content server |
| Auth operator UI | `{authHost}/admin/` (`:5100/admin/`) — Account identity plus an auth-only operator password |
| Federation invite send | `{hubHost}/admin/` (`:5200/admin/`) — Account identity plus a hub-only operator password; origin checks `RequestFederation` |
| Federation invite accept + node/issuers/Super Users | `{contentHost}/admin/` (`:5001/admin/`) — Account identity plus a **content-node** operator password |

Admin is always `{thatHost}/admin/` on Auth, Hub, or content. Same JS bundle; `GET /admin/bootstrap` returns `{ kind, name, publicBaseUrl, did, authLoginUrl }`. Operator JWT stays per origin (two passwords: Account, then host operator). Dashboard + redoable checklist, not a one-shot wizard.

**Local ports (dev):** Auth `:5100`, Hub `:5200`, Content `:5001`/`:5002`, Vite member SPA `:5173`. Admin UIs are served from each API’s `wwwroot/admin` (watched by `vite.admin.config.ts` when using `dotnet run … -- dev`).

**Auth URL:** configured on Auth/Hub/content (`Auth:BaseUrl`, `Hub:AuthBaseUrl`, `AuthIssuers`). The client stores one Auth URL after login (`VITE_AUTH_URL` is first-run only).

**Federations / content origins:** URLs on the **account** (`/api/me/federations`, `/api/me/content-servers`). Paste a URL to add. Zero hubs is valid. In-app content routes are `/s/{serverDid}/…`.

**Federation client contract:** generic HTTPS + optional Account JWT. The host serves a **config document** (`snekl-federation/1`: `name`, `baseUrl`, `auth: { issuer, jwks }`, `mapVersion`, `servers[]`, optional `feed`), not a Snekl-only hub session. Snekl.Hub is one implementation; a static JSON file could be another. Auth fetches a user-supplied federation URL **anonymously** (do not forward the member JWT).

**Same public hostname (later):** optional edge proxy can expose the SPA + `/api/hub` and `/api/auth` under one name without merging binaries.

**Even a single content node** can run without a hub; members add origins on their account. Joining a federation is optional.

**Click-through:** the client calls the card’s `originBaseUrl` APIs. `originPostUrl` is the canonical resource identity / share target, not a requirement to load origin HTML.

**Clients:** v1 ships the portable SPA. Later: native apps, desktop tools, and third-party clients talking to **auth + federation config + origin** APIs.

## Actors and joins

| Layer | Who joins | Effect | Roles |
|-------|-----------|--------|-------|
| **Federation** | Content servers (admin) | Named network; register with official hub(s); appear in hub registry | None (no federation ACL) |
| **Server** | Implicit on first forum join | Durable **visitor** row on that origin (not anonymous default) | Sparse: stewards / server admins |
| **Forum** | Explicit user action | Primary membership; feed eligibility for private forums | Primary permissions |

When server S joins federation F: a member with `RequestFederation` on S sends an invite from the **hub** admin. S’s **site admin** accepts the pending invite on the origin. The hub registry then lists S as active; S acknowledges the map. Existing users are **not** copied. Remotes hitting S remain visitors until they forum-join (which creates visitor + forum member on S).

A content server **may belong to multiple federations**. It pushes cards/scores to each federation’s hub and tracks each hub’s `mapVersion` independently.

**Known server moderators** may be listed for trust/UX; that is directory metadata, not a federation permission system.

## Federation configuration (hub-owned)

**Today (peer mesh):** each content node stores its own `PeerNodes` edges. There is no single federation map; “who’s in the network” drifts unless every node is updated alike. Feed broadcast walks local peers.

**v1 (hub registry):**

| Concern | Source of truth | Node responsibility |
|---------|-----------------|---------------------|
| Membership of F | Hub registry | Register / heartbeat / leave |
| Directory + policy snapshot | Hub (`mapVersion`) | Pull or receive signed map; **ack** and store `mapVersion` |
| Card ingest eligibility | Hub | Push only if registered; include `mapVersion` on pushes |
| Peer inbox mesh for home feed | Retired | Not required |

Hub admin manages central federation configuration (servers in F, known stewards, discover listing). Each node acknowledges that map rather than maintaining a full replica of every other node’s peer list for sync.

Light ack protocol: hub publishes `mapVersion`; stale nodes refresh before ingest; hub may reject card pushes with a “map outdated” signal.

## Trust split

| Concern | Owner |
|---------|--------|
| Identity / login | Account auth webservice (ring semantics per [auth ring](architecture-auth-ring.md)); login **UI** on the portable client |
| Forum/server ACL, votes, replies, moderation | Origin content server |
| Ranked feed **within** federation F | Hub F |
| Ranked feed **across** federations | Client, using each hub’s scores + local adjustments |
| Thread UI chrome | Client calling origin APIs |

## Multi-federation client

Users may browse **multiple federations** in one client install. Federation memberships live on the **account**, like content-server subscriptions—not in a device-only list and not on hub A as “this user also uses B.”

| Rule | Meaning |
|------|---------|
| Per-federation data | Never send federation A’s memberships, cards, or activity to federation B |
| Account-local federation list | Enabled federations are URLs on `/api/me/federations` (paste to add; zero is valid) |
| Cross-fed home | Client fetches each enabled hub’s `GET /feed` (or config `feed`) and merges (see [Ranking](#ranking)) |
| Within one fed | Hub response is already ranked; client does not re-rank that slice unless applying **personalization overlays** |
| Auth fetch of config | Account loads a user-supplied federation URL **without** forwarding the member JWT |

## Ranking

### Within a federation (hub)

Hub uses `PostRanking.HotScore(score, createdAt)` (same formula as content nodes) over cards the user may see (joined forums + eligible public). Score patches are lossy/batched (see below).

### Across federations (SPA)

- Compare **hot scores** from each hub (same formula ⇒ roughly comparable).
- Allow **client-side adjustments**, for example:
  - Friend / followed-author activity
  - Forum membership boosts
  - Forum interest (e.g. prefer art-tagged or media-heavy posts within a forum)
- Support **multiple named feeds** in the client with different ranking profiles (e.g. “Home”, “Art-heavy”, “Friends”, “New only”)—not a single global sort forever.

Hubs never learn the cross-fed adjustment inputs from other federations.

## Feed card

Origins push cards to each hub for federations they belong to. Cards are **presentation snapshots**, not canonical posts.

### Schema (`HubFeedCard`)

```json
{
  "schemaVersion": 1,
  "postId": "uuid",
  "forumId": "uuid",
  "forumSlug": "string",
  "forumName": "string",
  "serverDid": "did:web:…",
  "originBaseUrl": "https://origin.example",
  "originPostUrl": "https://origin.example/f/{forumSlug}/…",
  "title": "string | null",
  "excerpt": "string",
  "imagePreviewUrl": "string | null",
  "authorDid": "did:key:…",
  "authorDisplayName": "string",
  "createdAt": "ISO-8601",
  "editedAt": "ISO-8601 | null",
  "visibility": "public | members",
  "score": 0,
  "commentCount": 0,
  "mapVersion": 1,
  "event": "upsert | delete",
  "eventAt": "ISO-8601"
}
```

| Field | Notes |
|-------|--------|
| `imagePreviewUrl` | Absolute URL on origin (or origin media); client **hotlinks**; hub does not re-host |
| `originPostUrl` | Canonical resource / share URL; client loads thread via origin API |
| `excerpt` | Short preview only; full body stays on origin |
| `visibility` | `public` = eligible for discovery lane; `members` = only users with forum membership |
| `score` / `commentCount` | Optional on structural upsert; authoritative score channel is the patch API |
| `mapVersion` | Hub registry version the origin believes is current |
| `event: delete` | Tombstone; hub drops card from indexes |

Signed like today’s federation envelopes (origin node DID / key) so the hub only accepts cards from registered federation servers.

### Score patch (separate, lossy)

```json
{
  "schemaVersion": 1,
  "postId": "uuid",
  "serverDid": "did:web:…",
  "score": 42,
  "commentCount": 7,
  "patchedAt": "ISO-8601"
}
```

Origins **do not** push on every upvote. v1 default:

- Flush when `|Δscore| >= K` (e.g. 5) **or** at most every T minutes while the thread is “active”
- Always flush on create card upsert (initial score)
- Delete/tombstone is immediate via card event

Hub recomputes hot rank with `PostRanking.HotScore(score, createdAt)`.

## Hub HTTP API (v1)

### Origin → hub (server-to-server)

| Method | Path | Purpose |
|--------|------|---------|
| `POST` | `/api/hub/cards` | Upsert or tombstone a feed card (signed) |
| `POST` | `/api/hub/scores` | Batch score patches (signed); array body OK |

### Federation membership

| Method | Path | Purpose |
|--------|------|---------|
| `POST` | `/api/hub/servers` | Hub-initiated invite. Requires Account JWT; hub forwards it to the origin `POST /api/federation/invites`. Origin must grant the caller `RequestFederation`. Stores a pending member until the origin accepts. |
| `GET` | `/api/hub/join-requests` | Pending hub-sent invites (Account JWT; optional `?serverDid=`) |
| `POST` | `/api/hub/join-requests/accept` | Origin site admin completes membership (`serverDid`, `acceptedBy: origin`, `originInviteId`) |
| `POST` | `/api/hub/join-requests/decline` | Decline or cancel a pending invite |
| `DELETE` | `/api/hub/servers/{serverDid}` | Leave federation |
| `GET` | `/api/hub/map` | Fetch current federation map + `mapVersion` (active members only) |
| `POST` | `/api/hub/map/ack` | Node acknowledges `mapVersion` |

Rate-limit and dedup by `(serverDid, postId, eventAt)` / patch idempotency keys (Redis dedup, same idea as federation inbox dedup).

### Client → hub (user)

| Method | Path | Purpose |
|--------|------|---------|
| `GET` | `/api/hub/feed?sort=hot\|new&cursor=&limit=` | User’s ranked aggregate feed **for this federation** |
| `GET` | `/api/hub/feed/discover?…` | Optional: public-only slice |
| `POST` | `/api/hub/memberships/sync` | Auth-backed sync of forum memberships **in this federation** |
| `GET` | `/api/hub/servers` | Federation server directory (+ optional known stewards) |

`GET /feed` returns card DTOs for rendering; it does not proxy the full thread.

### Membership

- Source of truth for “user U joined forum F on server S”: **origin** (forum member) + hub index of that fact for feed filtering **within that federation**.
- On forum join: origin creates **server visitor** (if needed) + forum member; notifies that federation’s hub (`membership.upsert`).
- On leave: `membership.delete`; if last forum on that server, v1 **keeps** server visitor (revisit later).
- Hub includes `visibility: members` cards only when membership is present; `public` cards may appear in discover / mixed feed per product rules.

Private content on the hub means **indexed previews the user is allowed to see**, not a second ACL engine. Opening the post still hits origin, which enforces permissions again.

## Redis layout (hub)

Postgres (or hub DB): federation registry, memberships, durable card registry (survive Redis flush). Redis: serving and ranking.

| Key | Type | Purpose |
|-----|------|---------|
| `hub:card:{serverDid}:{postId}` | HASH/JSON | Card payload for render |
| `hub:z:forum:{serverDid}:{forumId}:hot` | ZSET | Per-forum hot (score = hot rank) |
| `hub:z:forum:{serverDid}:{forumId}:new` | ZSET | Per-forum new (score = createdAt unix) |
| `hub:z:public:hot` / `:new` | ZSET | Public discovery indexes |
| `hub:z:user:{userDid}:hot` / `:new` | ZSET | Optional materialized per-user feed |
| `hub:dedup:card:{…}` / `hub:dedup:score:{…}` | STRING | Idempotency TTLs |
| `hub:map:version` | STRING | Current federation map version |

**Read path (materialized user ZSET):** `ZREVRANGE` + `HMGET` cards — no origin fan-out.

**Read path (v1 lighter):** union of user’s forum ZSETs + public set, merge top-N in hub memory, then hydrate cards. Prefer materialized user sets once membership volume justifies it.

Score patch: update card hash fields + `ZADD` affected ZSETs with new `HotScore`. No card body rewrite required.

## Hub scale

v1: **one logical hub hostname** per federation; horizontal replicas later behind ordinary LB.

Soft constraints (decide now, implement when needed):

1. Hub API process is **stateless** — feed/registry state in shared Redis + DB  
2. Card/score ingest is **idempotent** (multi-replica safe)  
3. Origins push to the **hub URL** (DNS/LB), not a single machine name  
4. Sticky sessions not required for feed/auth cookie strategies that use shared session store  

Defer: multi-region active-active, separate ingest vs read pools, multiple independent authoritative hubs for one F (product choice later).

## Origin responsibilities

On thread create / title-or-preview edit / soft-delete / visibility change:

1. Persist locally (canonical).
2. `POST /api/hub/cards` to **each** configured official hub for federations this server belongs to.

On upvote / downvote:

1. Persist locally only.
2. Enqueue score patch; flush on threshold or timer to each relevant hub.

Replies always on origin. Crosspost (later) = new origin post with pointer metadata; that origin pushes its own card.

## What to retire / demote

| Current behavior | v1 fate |
|------------------|---------|
| `BroadcastPost*` to all **peers’** `/api/federation/inbox` for home-timeline copies | **Retire** for feed purposes |
| Storing federated post/forum clones so “local DB has everyone’s posts” | **Demote**; not required for home feed |
| Client `aggregateForums` / `getRecentThreads(servers…)` as primary **single-federation** home feed | **Replace** with `GET /api/hub/feed` |
| Client merge across **federations** | **Keep / extend** — only cross-fed boundary; plus personalization profiles |
| `PostAggregationService` multi-server merge on a content node | **Demote** to debug/fallback; hub is canonical ranker inside F |
| Peer subscribe as “sync graph” | **Retarget**: hub registration + map ack — not post replication |
| Federation page copy about broadcast-to-peers unified feed | **Update** to hub-card + portable client model |
| Standalone content node as full member site | **Keep** — account can subscribe to origins with zero hubs |

Keep for now (orthogonal):

- Origin node identity (`GET /api/node`), DID verify
- Signed envelopes / validators (reuse for hub card auth)
- Redis on content nodes (cache, SignalR, dedup)
- Forum + server permission model

## End-to-end flows

### Post

```
User → origin API (via portable client): create thread
origin → DB
origin → each hub: POST /api/hub/cards (upsert)
hub → Redis card + ZADDs (+ user ZSETs if membership)
client → each enabled hub: GET /api/hub/feed
client → merge / adjust ranks → render
client → origin API: open thread
```

### Upvote

```
User → origin API: upvote
origin → DB
(no hub call)
… later …
origin → each hub: POST /api/hub/scores
hub → update ZSET scores
```

### Join forum on new server

```
User → origin S: join forum
origin → visitor row + forum member
origin → hub(s) for S’s federations: membership.upsert
hub → include that forum’s cards in user’s feed for that F
```

### Federation map change

```
Hub admin: add/remove server in registry → mapVersion++
Nodes: GET /api/hub/map → POST /api/hub/map/ack
Nodes push cards with current mapVersion
```

## Open items (explicitly deferred)

- Exact K/T for score flush
- Whether discover is mixed into `/feed` or separate
- Leave-last-forum visitor cleanup
- Concrete client ranking-profile schema (friends / membership / interest weights)
- Notification channel reusing cards/scores
- Migration: purge or ignore existing federated clone rows
- Third-party client API stability guarantees

## Implementation order (suggested)

1. Portable SPA + Auth/Hub/content APIs; auth remains Account.Api; each host serves `/admin/` (done for skeleton)
2. Hub registry + map/ack; hub sends invite (Account JWT + origin `RequestFederation`); origin site-admin accepts
3. Card + score ingest, Redis keys, per-federation feed
4. Membership sync + private card filtering
5. Origin: push cards on write to all member hubs; stop peer broadcast for posts
6. SPA: home feed from account federation list; thread routes via origin API
7. SPA: cross-fed merge + pluggable ranking profiles
8. Score batch worker on origin; tune K/T
9. Docs/UI: Federation = config document + hub registry; Servers/Forums = user-facing joins 
