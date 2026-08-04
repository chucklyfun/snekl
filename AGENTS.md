# AGENTS.md

## Cursor Cloud specific instructions

### Product scope

Snekl lives under `src/` (`Snekl.Modern.sln`) and `client/snekl-web/`.

### Starting the stack

See [README.md](README.md) for the canonical commands. Preferred local flow:

```bash
dotnet run --project src/Snekl.Server --no-launch-profile -- dev
```

That starts Redis + PostgreSQL via `docker-compose.deps.yml`, then Auth (`:5100`), Content (`:5001`), and the Vite frontend (`:5173`).

Manual equivalent when orchestrating separately:

- Deps: `docker compose -f docker-compose.deps.yml up -d` (creates DBs `snekl` and `snekl_auth`)
- Auth: `dotnet run --project src/Snekl.Account.Api --no-launch-profile` with `DOTNET_URLS=http://localhost:5100` and `ConnectionStrings__Default` pointing at `snekl_auth`
- Content: `dotnet run --project src/Snekl.Api --no-launch-profile` with `DOTNET_URLS=http://localhost:5001` and DB `snekl`
- Frontend: `npm run dev -- --host 0.0.0.0 --port 5173` in `client/snekl-web` (copy `.env.example` → `.env` if missing)

Docker must already be running (`dockerd`). In this Cloud VM, if the docker socket is root-only, `sudo chmod 666 /var/run/docker.sock` (or membership in the `docker` group) is needed so `dotnet run --project src/Snekl.Server -- deps/dev` can talk to the daemon.

Ensure `PATH` includes `$HOME/.dotnet` (dotnet SDK is installed via the official install script into `~/.dotnet`).

Node/npm come from nvm (`~/.nvm/versions/node/v22.22.2/bin`). That directory is prepended in `~/.bashrc` so `npm`/`node` win over `/exec-daemon/node`. New shells should already have them; in a bare session run `source ~/.bashrc` or `export PATH="$HOME/.nvm/versions/node/v22.22.2/bin:$PATH"`.

### Lint / test / build

| Area | Command | Notes |
|------|---------|-------|
| Frontend lint | `npm run lint` in `client/snekl-web` | oxlint; may report non-blocking warnings |
| Frontend build | `npm run build` in `client/snekl-web` | `tsc -b && vite build` |
| Frontend unit tests | `npm test` in `client/snekl-web` | Vitest (`src/**/*.test.ts`) |
| .NET build | `dotnet build Snekl.Modern.sln` | |
| .NET tests | `dotnet test Snekl.Modern.sln` | xUnit; integration tests need Docker (Testcontainers Postgres) |

### Gotchas

- Development APIs only wipe databases when `Snekl:ResetDb` is true (pass `--reset-db` to the orchestrator). Without it, accounts and content persist across restarts.
- Content JWT validation trusts issuers via JWKS (`AuthIssuers` in `src/Snekl.Api/appsettings.json`). Signup/login go through Auth (`:5100`); content APIs expect that JWT.
- Redis is optional functionally but started by `dev`/`deps`. Without Redis, caching falls back to in-memory and rate limiting is disabled.
- Registration requires a `did:key` ownership proof (browser generates Ed25519 keys via `@noble/ed25519`).
- Running Snekl APIs lock Debug build output. Check recent activity before stopping orphaned processes:
  - `dotnet run --project src/Snekl.Server --no-launch-profile -- status`
  - `dotnet run --project src/Snekl.Server --no-launch-profile -- stop --force` (only when you are sure nothing needs the API)
- Optional: tag agent HTTP calls with header `X-Snekl-Dev-Client: cursor-agent` so activity shows up under "Last cursor" in `status`.
