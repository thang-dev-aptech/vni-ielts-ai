#!/usr/bin/env bash
#
# F1.5 — one command from a clean checkout to a running local stack.
#
# Before this, getting a working environment was five separate manual steps
# scattered across CLAUDE.md, docs/README.md and tribal knowledge: check the
# Node/pnpm version, install, generate the API client (undocumented as a
# prerequisite — see F1.2), start Mongo and MinIO, and know how to tell they
# were actually ready rather than merely started. Getting the order wrong
# produced a confusing failure at a step that had nothing to do with the
# real cause.
#
# toolchain → install → generate → start dependencies → readiness, in that
# order, each step depending on the one before it having actually finished.
#
# Usage: scripts/bootstrap.sh
#
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"

echo "bootstrap: 1/5 — toolchain versions..."
node scripts/check-toolchain-versions.mjs

echo "bootstrap: 2/5 — installing dependencies (frozen lockfile)..."
pnpm install --frozen-lockfile

echo "bootstrap: 3/5 — generating the API client from contracts/openapi..."
pnpm run generate:api-client

echo "bootstrap: 4/5 — starting MongoDB + MinIO..."
docker compose -f infra/docker/compose.yaml up -d

echo "bootstrap: 5/5 — waiting for both to report healthy..."
deadline=$((SECONDS + 90))
while true; do
  # Compose's own healthcheck is the source of truth here, not "the port
  # answers" — Mongo is not ready until its replica set has an elected
  # PRIMARY (see compose.yaml's healthcheck), which a bare TCP or HTTP probe
  # would not know to wait for.
  statuses="$(docker compose -f infra/docker/compose.yaml ps --format '{{.Service}} {{.Health}}' 2>/dev/null || true)"

  mongo_ok=false
  minio_ok=false
  grep -q '^mongo healthy$' <<<"$statuses" && mongo_ok=true
  grep -q '^minio healthy$' <<<"$statuses" && minio_ok=true

  if [ "$mongo_ok" = true ] && [ "$minio_ok" = true ]; then
    break
  fi

  if [ "$SECONDS" -ge "$deadline" ]; then
    echo "bootstrap: MongoDB/MinIO did not both report healthy within 90s." >&2
    echo "$statuses" >&2
    docker compose -f infra/docker/compose.yaml logs --tail 30 >&2 || true
    exit 1
  fi

  sleep 2
done

echo ""
echo "bootstrap: ready."
echo "  pnpm api        — the backend API (http://localhost:5099)"
echo "  pnpm dev        — the learner web app"
echo "  pnpm dev:admin  — the admin CMS"
echo "  pnpm check      — the same checks CI runs"
