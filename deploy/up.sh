#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")" && pwd)"
cd "$ROOT"

usage() {
  cat <<'EOF'
Start Snekl on this host (Caddy on :80 / :443).

Usage:
  ./up.sh [--target 2gb|search] [docker compose up args...]

Targets:
  2gb     Default. ~2 GB RAM: Redis (64mb cap), no OpenSearch.
  search  ~4 GB+: Redis (128mb) plus OpenSearch. Origin FTS.

Examples:
  ./up.sh
  ./up.sh --target 2gb
  ./up.sh --target search
EOF
}

TARGET=""
PASSTHRU=()
while [[ $# -gt 0 ]]; do
  case "$1" in
    -h|--help)
      usage
      exit 0
      ;;
    --target)
      if [[ $# -lt 2 ]]; then
        echo "--target requires 2gb or search" >&2
        exit 1
      fi
      TARGET="$2"
      shift 2
      ;;
    *)
      PASSTHRU+=("$1")
      shift
      ;;
  esac
done

if [[ ! -f .env ]]; then
  echo "Copy .env.example to .env, set hostnames and secrets, then re-run." >&2
  echo "  cp .env.example .env && ./gen-env.sh" >&2
  exit 1
fi

if [[ -z "$TARGET" ]]; then
  TARGET="$(grep -E '^SNEKL_TARGET=' .env 2>/dev/null | tail -n1 | cut -d= -f2- || true)"
fi
TARGET="${TARGET:-2gb}"

TARGET_FILE="$ROOT/targets/${TARGET}.env"
if [[ ! -f "$TARGET_FILE" ]]; then
  echo "Unknown target '$TARGET'. Available: 2gb, search" >&2
  exit 1
fi

if [[ -f images.tar ]]; then
  echo "Loading Snekl images..."
  docker load -i images.tar
fi

set -a
# shellcheck disable=SC1091
source "$ROOT/.env"
# shellcheck disable=SC1090
source "$TARGET_FILE"
set +a

if [[ "$TARGET" == "2gb" ]]; then
  export OPENSEARCH_URL=""
  export COMPOSE_PROFILES=""
fi

if [[ "$TARGET" == "search" ]]; then
  export COMPOSE_PROFILES=search
  export OPENSEARCH_URL="${OPENSEARCH_URL:-http://opensearch:9200}"
fi

PROFILE_ARGS=()
if [[ "$TARGET" == "search" ]]; then
  PROFILE_ARGS+=(--profile search)
fi

echo "Starting Snekl target '$TARGET' (Caddy on :80 / :443)..."
if [[ "$TARGET" != "2gb" ]]; then
  echo "Hint: run ./down.sh before switching targets so leftover OpenSearch is stopped."
fi
COMPOSE=(docker compose "${PROFILE_ARGS[@]}" --env-file .env --env-file "$TARGET_FILE" up -d)
if [[ ${#PASSTHRU[@]} -gt 0 ]]; then
  "${COMPOSE[@]}" "${PASSTHRU[@]}"
else
  "${COMPOSE[@]}"
fi

echo
echo "Target:        $TARGET"
echo "Member SPA:    https://${HUB_HOST}/"
echo "Auth admin:    https://${AUTH_HOST}/admin/"
echo "Hub admin:     https://${HUB_HOST}/admin/"
echo "Origin admin:  https://${CONTENT_HOST}/admin/"
if [[ "$TARGET" == "2gb" ]]; then
  echo "Search:        Postgres fallback (OpenSearch not running)"
else
  echo "Search:        OpenSearch at $OPENSEARCH_URL"
fi
