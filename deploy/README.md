# Linux host deployment (Caddy)

Same-server layout: **Caddy** on 80/443, then Auth, Hub, Content, PostgreSQL, Redis, and MinIO on a private Docker network. The member SPA is served from Hub (`https://hub…`). Admin UIs stay at `{host}/admin/` on each API.

## Host targets

Pick a target at install time. **`2gb` is the default** and the one to use on a 2 GB VPS.

| Target | RAM | Redis | OpenSearch | Origin search |
|--------|-----|-------|------------|---------------|
| **`2gb`** | ~2 GB | Yes, capped at 64 MB (`allkeys-lru`) | No | Postgres `ILIKE` |
| **`search`** | ~4 GB+ | Yes, 128 MB cap | Yes (~512 MB JVM) | OpenSearch |

Postgres is also sized down on `2gb` (128 MB `shared_buffers`, 50 connections). Run `./down.sh` before switching targets so leftover OpenSearch is not left running.

## What you need on the host

- A Linux box with Docker Engine and Compose v2
- **2 GB RAM** (or more) for `--target 2gb`; **~4 GB** if you want `--target search`
- Ports **80** and **443** free
- Three DNS names pointing at the box, for example `auth.example.com`, `hub.example.com`, `origin.example.com`

## Build a package (from this repo)

On a machine with Docker (Windows or Linux):

```bash
./snekl pack
```

That writes `artifacts/snekl-linux-deploy.tar.gz` (Compose files, Caddyfile, targets, and `snekl/auth`, `snekl/hub`, `snekl/content` images). Copy the tarball to the host.

To run the 2 GB stack from the repo without packing:

```bash
cp deploy/.env.example deploy/.env
# edit deploy/.env
docker compose -f deploy/docker-compose.yml -f deploy/docker-compose.build.yml \
  --env-file deploy/.env --env-file deploy/targets/2gb.env up --build -d
```

## On the Linux host

```bash
tar -xzf snekl-linux-deploy.tar.gz
cd snekl-linux-deploy
cp .env.example .env
./gen-env.sh          # random secrets; then edit hostnames + ACME_EMAIL
# Point DNS at this machine, then:
./up.sh               # same as ./up.sh --target 2gb
```

Full-text search later, on a larger box:

```bash
./down.sh
./up.sh --target search
```

First boot claims Let’s Encrypt certs. Caddy logs show HTTP-01 challenges; the names must already resolve here.

Internal TLS (no public DNS): set `CADDYFILE=Caddyfile.internal` in `.env`. Browsers will warn until they trust Caddy’s local CA.

## After it is up

1. Open the SPA at `https://<HUB_HOST>/` and register through Auth.
2. Claim each admin (first Account user to succeed sets that host’s operator password):
   - `https://<AUTH_HOST>/admin/`
   - `https://<HUB_HOST>/admin/`
   - `https://<CONTENT_HOST>/admin/`
3. On Content admin, confirm the Auth issuer. From Hub admin, send a federation invite; accept it on Content.

Do not enable `Snekl:ResetDb` or `Auth:AllowDevPasswordReset` on this stack. Postgres, Redis, MinIO, and OpenSearch are not published to the internet.

## Secrets and URLs

`AUTH_SIGNING_KEY` is shared by Auth (member JWT) and Hub. Do not rotate it without planning a logout of every session. `Auth:BaseUrl`, `Hub:BaseUrl`, and `Node:BaseUrl` are public HTTPS names; changing them later rewrites `did:web` identity.

Mail: set `SMTP_HOST` (and related vars) or Auth will only log verification/reset messages.
