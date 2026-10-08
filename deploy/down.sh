#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")" && pwd)"
cd "$ROOT"

# Always include the search profile so OpenSearch is stopped after a target switch.
docker compose --profile search down "$@"
echo "Snekl stack stopped."
