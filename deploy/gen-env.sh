#!/usr/bin/env bash
# Fill empty/placeholder secrets in .env. Does not change hostnames.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")" && pwd)"
ENV_FILE="$ROOT/.env"

if [[ ! -f "$ENV_FILE" ]]; then
  cp "$ROOT/.env.example" "$ENV_FILE"
  echo "Created $ENV_FILE from .env.example"
fi

rand() { openssl rand -hex 32; }

set_if_placeholder() {
  local key="$1"
  local current
  current="$(grep -E "^${key}=" "$ENV_FILE" | head -n1 | cut -d= -f2- || true)"
  if [[ -z "$current" || "$current" == change-me* ]]; then
    local value
    value="$(rand)"
    if grep -qE "^${key}=" "$ENV_FILE"; then
      grep -vE "^${key}=" "$ENV_FILE" > "$ENV_FILE.tmp"
      echo "${key}=${value}" >> "$ENV_FILE.tmp"
      mv "$ENV_FILE.tmp" "$ENV_FILE"
    else
      echo "${key}=${value}" >> "$ENV_FILE"
    fi
    echo "Set $key"
  fi
}

set_if_placeholder POSTGRES_PASSWORD
set_if_placeholder AUTH_SIGNING_KEY
set_if_placeholder AUTH_ADMIN_SIGNING_KEY
set_if_placeholder HUB_ADMIN_SIGNING_KEY
set_if_placeholder CONTENT_JWT_KEY
set_if_placeholder MINIO_ROOT_PASSWORD

echo "Edit hostnames (AUTH_HOST, HUB_HOST, CONTENT_HOST) and ACME_EMAIL in .env before ./up.sh"
