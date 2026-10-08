# Snekl — Federated Nest Social Network

Snekl is an open-source, nest-style social network built as a **network of independent nodes**. Each node runs its own C# web service and PostgreSQL database. Nodes connect to each other through a lightweight HTTP federation protocol to share nests and threaded discussions.

## Architecture

Snekl separates **auth servers** (identity ring) from **content servers** (nests and posts). See [docs/architecture-auth-ring.md](docs/architecture-auth-ring.md) for the full auth ring design.

```
┌─────────────────┐         HTTP federation          ┌─────────────────┐
│   Node Alpha    │ ◄──────────────────────────────► │   Node Beta     │
│  ASP.NET Core   │    POST /api/federation/inbox     │  ASP.NET Core   │
│  PostgreSQL     │    GET  /api/node                 │  PostgreSQL     │
└────────┬────────┘                                   └────────┬────────┘
         │                                                     │
         └──────────────────┬──────────────────────────────────┘
                            │
                     ┌──────▼──────┐
                     │ React (Vite) │
                     │   Frontend   │
                     └─────────────┘

┌──────────────────┐  ring login / vouch   ┌──────────────────┐
│   Auth Alpha     │ ◄──────────────────► │   Auth Beta      │
│  Snekl.Account   │  mutual partner trust │  Snekl.Account   │
└──────────────────┘                       └──────────────────┘
         │ JWT (PersonCluster)                      │
         └──────────────┬───────────────────────────┘
                        ▼
              Content servers trust issuers
```

### Components

| Layer | Technology | Purpose |
|-------|-----------|---------|
| **Hub** | ASP.NET Core 8 | Federation registry, feed index API, optional SPA host (`:5200` local) |
| **Account API** | ASP.NET Core 8 | Auth webservice: login, PersonCluster, partner trust (`:5100`) |
| **API** | ASP.NET Core 8, EF Core | Content origins: REST, JWT auth, federation inbox |
| **Domain** | C# class library | Users, nests, threaded posts, peer nodes |
| **Billing** | C# class library | Stripe subscriptions and commission escrow ledger (`Snekl.Billing`). Not called by Auth or commissions yet |
| **Database** | PostgreSQL | Independent per-node / hub / auth storage |
| **Frontend** | React 19, TypeScript, Vite | Member SPA (login UI calls auth; feed will use hub) |

### Federation protocol

Each node exposes:

- `GET /api/node` — node identity (name, URL, ID)
- `POST /api/federation/peers` — subscribe to another node
- `POST /api/federation/inbox` — receive federated content
- `DELETE /api/federation/peers/{id}` — unsubscribe

**Today:** when a user creates a nest or post locally, the node broadcasts an envelope to subscribed peers. Federated content is stored locally with origin metadata and shown alongside local content.

**Planned:** named federations use official hubs that host the member SPA and hub APIs, own the federation registry, and index feed cards. **Auth stays a separate webservice** (login UI in the Hub SPA calls it over HTTP). Origins stay API-canonical for threads. The SPA may merge feeds across federations without leaking that data between hubs. See [hub feed architecture](docs/architecture-hub-feed.md). Many customer content servers can share one content database and process pool; see [virtual content servers](docs/architecture-virtual-servers.md).

## Quick start (local development)

### One command (recommended)

From the repository root, with [Docker](https://docs.docker.com/get-docker/) running:

```bash
./snekl dev
```

On Windows: `.\snekl dev`. Equivalent: `dotnet run --project src/Snekl.Server --no-launch-profile -- dev`.

This starts **Redis + PostgreSQL + MinIO + OpenSearch** in Docker, then launches locally:

- **Auth:** http://localhost:5100
- **Hub:** http://localhost:5200
- **Content:** http://localhost:5001 (beta `:5002`)
- **Faker extension:** http://localhost:5300
- **Frontend:** http://localhost:5173

Faker is an out-of-process extension. Open its admin; **Connect**, create bots, and fill **Alpha** (`:5001`) and **Beta** (`:5002`) separately (you must be an admin on that node). Then start keep-posting to exercise SignalR on every filled node.
- **MinIO API / console:** http://localhost:9000 / http://localhost:9001 (bucket `snekl-media`, user/pass `snekl` / `sneklsecret`)
- **OpenSearch:** http://localhost:9200 (security plugin disabled in dev; alpha/beta use `snekl-posts-alpha` / `snekl-posts-beta`)

Other commands:

```bash
./snekl up            # full Docker stack
./snekl down          # stop full stack
./snekl deps          # Redis + Postgres + MinIO + OpenSearch only
./snekl status        # running APIs and recent request activity
./snekl stop          # stop local APIs (skips rebuild if snekl.exe is locked)
./snekl pack          # Linux host tarball (Caddy + Auth/Hub/Content images)
./snekl android       # member SPA debug APK
./snekl all           # dev stack plus sibling extensions and landing repos
./snekl dev --errors-only   # same stack; servers log errors only, so the port list stays on screen
./snekl dev --no-frontend
```

### Android app

The member SPA also ships as an Android app (`dev.snekl.app`) under `client/snekl-web/android`. It is the same client: sign in with an Auth server URL, then federations and content origins load from that account. Requires a JDK 17+ and the Android SDK (`ANDROID_HOME`, platform 36).

```bash
./snekl android
```

On Windows: `.\snekl android`. The debug APK is written to `client/snekl-web/android/app/build/outputs/apk/debug/app-debug.apk`. `npm run android` in `client/snekl-web` still opens Android Studio when that IDE is installed.

On a phone or emulator, `localhost` is the device. At login, set Auth server URL to the computer running Auth (emulator: `http://10.0.2.2:5100`). The shell allows HTTP so a local stack works, and it does not auto-attach loopback content/hub ports.

### Deploy to a Linux host

Same-server production uses Caddy on 80/443 in front of Auth, Hub, and Content. See [deploy/README.md](deploy/README.md).

```bash
./snekl pack
# copy artifacts/snekl-linux-deploy.tar.gz to the host, then:
# tar -xzf snekl-linux-deploy.tar.gz && cd snekl-linux-deploy
# cp .env.example .env && ./gen-env.sh   # then edit hostnames
# ./up.sh                # 2 GB target: Redis on, OpenSearch off
```

### Manual setup

### Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download)
- [Node.js 20+](https://nodejs.org/)
- PostgreSQL 16 (or use Docker Compose below)

### 1. Start PostgreSQL

```bash
docker run -d --name snekl-pg \
  -e POSTGRES_USER=snekl \
  -e POSTGRES_PASSWORD=snekl \
  -e POSTGRES_DB=snekl \
  -p 5432:5432 postgres:16-alpine
```

### 2. Run the API

```bash
dotnet run --project src/Snekl.Api
```

API listens on `http://localhost:5000` (or the port shown in console output).

### 3. Run the React frontend

```bash
cd client/snekl-web
cp .env.example .env
npm install
npm run dev
```

Open `http://localhost:5173`.

### Tests

```bash
# .NET unit + integration (integration tests pull Postgres via Testcontainers; Docker required)
dotnet test Snekl.Modern.sln

# Frontend unit tests
cd client/snekl-web && npm test
```

### 4. Run two federated nodes (Docker)

```bash
docker compose up --build
```

- **Auth Alpha:** http://localhost:5100
- **Auth Beta:** http://localhost:5101
- **Content Alpha:** http://localhost:5001
- **Content Beta:** http://localhost:5002

Point the React app at Auth via `VITE_AUTH_URL` in `client/snekl-web/.env` (first-run only; after login the client remembers the URL you typed):

```bash
VITE_AUTH_URL=http://localhost:5100
VITE_ALLOW_DEV_PASSWORD_RESET=true
```

Register and log in through the auth server; the React client generates a `did:key` in the browser and sends an ownership proof on signup. Federations and content origins are URLs on your account (paste them on **Servers**).

Operator UIs use the same React visuals as the member app, labeled **Admin**, at `{host}/server/`:

- **Auth admin:** `http://localhost:5100/server/`
- **Hub admin:** `http://localhost:5200/server/`
- **Content admin:** `http://localhost:5001/server/`

Sign in with a regular Account, then an admin password that lives **only on that host**. The first Account user to open admin claims the server and chooses that password; later operators are invited and set their own password on first login. Hub operators send federation invites; the origin’s site admin accepts them under **Incoming hub requests**. The requester’s member JWT is checked on the content node for `RequestFederation`.

### 5. Run auth server locally

```bash
dotnet run --project src/Snekl.Account.Api
```

Auth API listens on `http://localhost:5100`.

## API endpoints

| Method | Path | Description |
|--------|------|-------------|
| POST | `/api/auth/register` | Create account |
| POST | `/api/auth/login` | Get JWT token |
| GET | `/api/auth/me` | Current user with flair, status, annotations |
| PUT | `/api/users/me/flair` | Update personal flair and name color |
| PUT | `/api/users/me/status` | Set temporary status (with expiry) |
| DELETE | `/api/users/me/status` | Clear active status |
| GET | `/api/users/status/presets` | Status presets (live, at event, safe, etc.) |
| GET | `/api/users/{username}` | Public profile with presentation |
| GET | `/api/lists` | Curated highlight/warn lists |
| POST | `/api/lists` | Create a curated list |
| POST | `/api/lists/{id}/entries` | Add user to list with label + description |
| POST | `/api/lists/{id}/subscribe` | Subscribe to a list |
| GET | `/api/forums` | List nests |
| POST | `/api/forums` | Create nest (auth required) |
| GET | `/api/forums/{slug}` | Nest details |
| GET | `/api/posts/forum/{forumId}` | List threads |
| GET | `/api/posts/{postId}` | Thread with replies |
| POST | `/api/posts` | Create post/reply (auth required) |
| GET | `/api/node` | Node metadata |
| GET | `/api/federation/peers` | List peer nodes |
| POST | `/api/federation/peers` | Subscribe to peer |
| POST | `/api/federation/inbox` | Federation inbox (inter-node) |
| GET | `/api/identity/clusters/{id}` | PersonCluster reference (external auth) |
| POST | `/api/identity/map` | Map `(iss, sub)` → PersonCluster |
| GET/POST | `/api/identity/issuers` | Trusted auth issuers |
| GET | `/api/extensions` | Active extension installs on this node |
| GET | `/api/extensions/install-options` | Resources the current admin can authorize |
| POST | `/api/extensions/installs` | Clip permissions to the authorizer and create an install |
| GET | `/api/extensions/installs/{id}` | Install details (installer JWT; Auth uses this during consent) |

### Auth server endpoints

| Method | Path | Description |
|--------|------|-------------|
| POST | `/api/auth/register` | Create identity with new or client-provided `did:key` |
| POST | `/api/auth/import` | Register on this server via exported identity credential |
| POST | `/api/auth/login` | Local login + ring sessions |
| GET | `/api/me` | Profile + linked identities (includes DID) |
| POST | `/api/me/export` | Export signed identity credential for untrusted-server linking |
| PUT | `/api/me/profile` | Update display name, bio, tag |
| POST | `/api/partners/trust` | Establish mutual partner trust |
| POST | `/api/partners/provision` | Accept vouched identity |
| GET | `/.well-known/did.json` | Auth server DID document (`did:web`) |
| GET | `/api/oauth/apps/{clientId}` | Public app metadata |
| POST | `/api/oauth/apps` | Register an extension (auth required) |
| POST | `/api/oauth/authorize` | Admin consent → authorization code |
| POST | `/api/oauth/token` | Code/refresh → install or bot token |
| POST | `/api/oauth/bots` | Create an app-owned bot (install token) |

## Project structure

```
src/
  Snekl.Domain/          # Entities and DTOs
  Snekl.Infrastructure/  # EF Core, services, federation client
  Snekl.Api/             # ASP.NET Core REST API (content)
  Snekl.Server/          # Dev stack orchestrator CLI (`dotnet run --project src/Snekl.Server`)
  Snekl.Account.Domain/  # Auth ring entities and DTOs
  Snekl.Account.Infrastructure/
  Snekl.Account.Api/     # Auth ring REST API
  Snekl.Faker/           # Dev extension: bot personas + fake nest traffic
docs/
  architecture-auth-ring.md
client/
  snekl-web/             # React frontend
docker/
  Dockerfile.api         # Multi-stage API container
docker-compose.yml       # Two-node demo cluster
Snekl.Modern.sln         # Solution entrypoint
```

## User presentation

- **Personal flair** — emoji, optional icon, username color/style
- **Temporary status** — live, at event, supporting cause, safe, preparing (with avatar overlays and auto-expiry)
- **Curated lists** — community-managed highlight, verify, and warning lists
- **Label-based annotations** — flair badges with labels like Verified or Celebrity, hover descriptions, username color/style
- **Avatar overlays** — rings, corner badges, pulse for live streaming

Subscribe to lists you trust on the **Lists** page; annotations appear on posts and profiles when you browse while logged in.

## Redis (high-volume interactions)

Redis is optional but recommended for production and included in `docker compose`. When `ConnectionStrings:Redis` is set, Snekl uses it for:

| Use | Benefit |
|-----|---------|
| **JWKS / issuer keys** | Shared cache across API replicas; avoids HTTP fetch per JWT |
| **Shadow user lookup** | Caches external `(iss, sub) → UserId` mapping |
| **User presentation** | 60s cache for flair, status, list annotations on reads |
| **Rate limiting** | Login, register, posting, federation inbox |
| **Federation dedup** | Prevents duplicate inbox processing |
| **Vouch replay protection** | One-time partner vouch signatures |
| **DID document cache** | Caches remote `did:web` resolution |

Without Redis, services fall back to in-memory distributed cache (single-node only) and rate limiting is disabled.

Local Redis: `docker run -d -p 6379:6379 redis:7-alpine`

## Roadmap

### Done

- [x] Soft delete + edit history
- [x] Signed federation envelopes (node key pairs)
- [x] Moderation tools per nest
- [x] Upvotes + hot/new ranking
- [x] Multi-server subscribe (auth) + client-aggregated home feed
- [x] Real-time updates via SignalR
- [x] Extension host (OAuth install grants, app-owned bots, Extensions menu, Faker)
- [x] Origin full-text search (`GET /api/search`) via OpenSearch when configured, with Postgres `ILIKE` fallback
- [x] Hub-wide search (`GET /api/hub/search`) over visible feed cards; the SPA merges hub hits with origin FTS
- [x] Official **hub** (`:5200`) — federation config, hub-owned registry, portable SPA (Auth `:5100` + content URLs). Origins enqueue signed cards, scores, and memberships in `HubOutbox` and deliver with retries. Hub ingest verifies envelopes (`Hub:RequireSignatures`, default true) against the active registry and origin `did:web`. Public `GET /api/hub/feed` ranks from stored upvote count + `createdAt` (Redis ZSETs when available). Hub start and hub admin **Refresh public feed** pull last-updated public threads from active origins (`sort=updated`). SPA registry refresh calls `POST /api/forums/hub-memberships/sync` so members-only cards can appear in that user’s hub feed.
- [x] Bot tokens on Content: deny-by-default for writes that are not mapped to the install grant (forum/server/post/media/federation-invite). Reads stay permission-filtered. Install tokens still cannot act as the authorizing user.

### Next

- Host **admin** (`:5100/server/`, `:5200/server/`, `:5001/server/`) — Account identity plus a per-host operator password (first user claims the server)
- Richer extension placements (beyond the Extensions menu)

### Later / deferred

- ActivityPub compatibility layer
- Mobile-friendly PWA
- Notifications (can reuse hub card/score channels)

## License

MIT — see [LICENSE](LICENSE).
