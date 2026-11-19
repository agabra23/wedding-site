#!/usr/bin/env bash
# Starts the API (:5080) and the Vite dev server (:5173). Ctrl+C stops both.
set -euo pipefail
cd "$(dirname "$0")/.."

# These env vars would override user-secrets and point at the wrong database.
unset CosmosConnection PartiesContainer

trap 'kill 0' EXIT INT TERM

(cd api/WeddingApi && dotnet run --urls http://localhost:5080) &
npm run dev:only &

wait
