# Snekl — Federated Forum Social Network

Snekl is an open-source, forum-style social network built as a **network of independent nodes**. Each node runs its own C# web service and PostgreSQL database. Nodes connect to each other through a lightweight HTTP federation protocol to share forums and threaded discussions.

## Architecture

Snekl separates **auth servers** (identity ring) from **content servers** (forums and posts). See [docs/architecture-auth-ring.md](docs/architecture-auth-ring.md) for the full auth ring design.

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
| **API** | ASP.NET Core 8, EF Core | REST web services, JWT auth, federation inbox |
| **Account API** | ASP.NET Core 8 | Auth ring: login, PersonCluster, partner trust |
| **Domain** | C# class library | Users, forums, threaded posts, peer nodes |
| **Database** | PostgreSQL | Independent per-node storage |
| **Frontend** | React 19, TypeScript, Vite | Forum browsing, posting, federation management |
| **Legacy** | ServiceStack (.NET 4.6) | Original 2016 codebase (preserved in repo root) |

### Federation protocol

Each node exposes:

- `GET /api/node` — node identity (name, URL, ID)
- `POST /api/federation/peers` — subscribe to another node
- `POST /api/federation/inbox` — receive federated content
- `DELETE /api/federation/peers/{id}` — unsubscribe

When a user creates a forum or post locally, the node broadcasts an envelope to all subscribed peers. Federated content is stored locally with origin metadata and displayed alongside local content.

## Quick start (local development)

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

### 4. Run two federated nodes (Docker)

```bash
docker compose up --build
```

- **Auth Alpha:** http://localhost:5100
- **Auth Beta:** http://localhost:5101
- **Content Alpha:** http://localhost:5001
- **Content Beta:** http://localhost:5002

Point the React app at a content node via `VITE_API_URL` in `.env`. Register and log in via an auth server (`http://localhost:5100/api/auth/register`).

To connect content nodes running in Docker, subscribe using `http://host.docker.internal:5002` from the Alpha federation page (containers reach the host network this way).

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
| POST | `/api/lists/{id}/entries` | Add user to list with severity + tooltip |
| POST | `/api/lists/{id}/subscribe` | Subscribe to a list |
| GET | `/api/forums` | List forums |
| POST | `/api/forums` | Create forum (auth required) |
| GET | `/api/forums/{slug}` | Forum details |
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
| GET | `/.well-known/jwks.json` | JWKS for content server JWT trust |

## Project structure

```
src/
  Snekl.Domain/          # Entities and DTOs
  Snekl.Infrastructure/  # EF Core, services, federation client
  Snekl.Api/             # ASP.NET Core REST API (content)
  Snekl.Account.Domain/  # Auth ring entities and DTOs
  Snekl.Account.Infrastructure/
  Snekl.Account.Api/     # Auth ring REST API
docs/
  architecture-auth-ring.md
client/
  snekl-web/             # React frontend
docker/
  Dockerfile.api         # Multi-stage API container
docker-compose.yml       # Two-node demo cluster
Snekl.Core/              # Legacy domain (ServiceStack era)
Website/                 # Legacy web app (AngularJS)
```

## User presentation

- **Personal flair** — emoji, optional icon, username color/style
- **Temporary status** — live, at event, supporting cause, safe, preparing (with avatar overlays and auto-expiry)
- **Curated lists** — community-managed highlight, verify, and warning lists
- **Severity-based annotations** — username color/style, flair badges, hover tooltips
- **Avatar overlays** — rings, corner badges, pulse for live streaming

Subscribe to lists you trust on the **Lists** page; annotations appear on posts and profiles when you browse while logged in.

## Roadmap

- Signed federation envelopes (node key pairs)
- ActivityPub compatibility layer
- Moderation tools per forum
- Full-text search across local + federated content
- Real-time updates via SignalR
- Mobile-friendly PWA

## License

MIT — see [LICENSE](LICENSE).
