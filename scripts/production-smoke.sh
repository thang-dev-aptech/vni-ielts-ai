#!/usr/bin/env bash
#
# F0.3 — actually boots the Production configuration path, instead of trusting
# that it would.
#
# Before 2026-08-28 there was no Dockerfile at all, so `ASPNETCORE_ENVIRONMENT=Production`
# had never been exercised. The independent re-audit that day found the compose
# file that was supposed to smoke-test it could not boot either: two of its
# settings (`Cors:Origins`, `Email:ClientBaseUrl`) were plain HTTP, and
# `StartupConfiguration.ValidateOrThrow` correctly refuses that outside
# Development — so the container that was meant to prove the production path
# works crashed before ever reaching it. Fixed in
# infra/docker/compose.production.yaml alongside this script.
#
# What this proves, each run:
#   1. the API and worker images build from the current source;
#   2. the API boots under ASPNETCORE_ENVIRONMENT=Production, passes its own
#      startup validation, and becomes ready (real Mongo + MinIO behind it —
#      not mocked);
#   3. a representative authenticated-surface endpoint actually answers
#      through the full pipeline (routing, DI, rate limiting, CORS setup),
#      not just the health probe;
#   4. the worker's own /health/ready reports its polling loop actually
#      running — not merely that the container has not exited (F2.2).
#
# What it does NOT prove: this is a smoke harness against the local dev
# stack (Mongo/MinIO on localhost), not a deployment target. See the compose
# file's own header.
#
# Usage:
#   docker compose -f infra/docker/compose.yaml up -d      # Mongo + MinIO
#   scripts/production-smoke.sh
#
# CI usage — a fresh secret every run, nothing persisted:
#   VNI_JWT_SIGNING_KEY="$(openssl rand -base64 48)" scripts/production-smoke.sh
#
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
COMPOSE_FILE="$ROOT/infra/docker/compose.production.yaml"
API_URL="${VNI_SMOKE_API_URL:-http://localhost:18080}"
WORKER_URL="${VNI_SMOKE_WORKER_URL:-http://localhost:18081}"
READY_TIMEOUT_SECONDS="${VNI_SMOKE_READY_TIMEOUT_SECONDS:-120}"

# <b>A fresh, throwaway key every run, never a fixture value.</b> The
# placeholder in the compose file itself is not committed to a file anywhere
# — `Jwt__SigningKey` is a required, unset environment variable there — and
# this is the "CI generates a temporary secret" half of F0.3. It is 64 random
# bytes, well over the 32-byte floor `StartupConfiguration` enforces, and it
# exists for exactly the lifetime of this script's container run.
if [ -z "${VNI_JWT_SIGNING_KEY:-}" ]; then
  VNI_JWT_SIGNING_KEY="$(openssl rand -base64 48)"
fi
export VNI_JWT_SIGNING_KEY

cleanup() {
  local exit_code=$?
  if [ "$exit_code" -ne 0 ]; then
    echo "smoke: FAILED — dumping API logs before teardown:" >&2
    docker compose -f "$COMPOSE_FILE" logs api >&2 2>&1 || true
  fi
  echo "smoke: tearing down (always, success or failure)..."
  docker compose -f "$COMPOSE_FILE" down -v --remove-orphans >/dev/null 2>&1 || true
  exit "$exit_code"
}
trap cleanup EXIT

echo "smoke: building and starting API + worker under ASPNETCORE_ENVIRONMENT=Production..."
docker compose -f "$COMPOSE_FILE" up --build -d

echo "smoke: waiting up to ${READY_TIMEOUT_SECONDS}s for /health/ready..."
deadline=$((SECONDS + READY_TIMEOUT_SECONDS))
until curl -fsS "$API_URL/health/ready" >/dev/null 2>&1; do
  if [ "$SECONDS" -ge "$deadline" ]; then
    echo "smoke: the API never became ready within ${READY_TIMEOUT_SECONDS}s." >&2
    exit 1
  fi

  # <b>The container has to still be running, or waiting is pointless.</b> A
  # crash on the startup gate exits immediately — without this check, a
  # broken configuration would be reported as "timed out" instead of the far
  # more useful "the container exited", and the actual problem (Docker
  # already has it, in the logs) would only surface once the full timeout
  # had been burned for nothing.
  api_state="$(docker compose -f "$COMPOSE_FILE" ps --format json api 2>/dev/null \
    | grep -o '"State":"[^"]*"' | head -n1 | cut -d'"' -f4 || true)"
  if [ "$api_state" != "running" ] && [ -n "$api_state" ]; then
    echo "smoke: the API container exited (state: $api_state) instead of becoming ready." >&2
    exit 1
  fi

  sleep 2
done

echo "smoke: API /health/ready is answering. Waiting up to ${READY_TIMEOUT_SECONDS}s for the worker's own /health/ready..."
worker_deadline=$((SECONDS + READY_TIMEOUT_SECONDS))
until curl -fsS "$WORKER_URL/health/ready" >/dev/null 2>&1; do
  if [ "$SECONDS" -ge "$worker_deadline" ]; then
    echo "smoke: the worker's polling loop never reported ready within ${READY_TIMEOUT_SECONDS}s." >&2
    docker compose -f "$COMPOSE_FILE" logs worker >&2 || true
    exit 1
  fi
  sleep 2
done

echo "smoke: calling a representative endpoint (not just health)..."
status="$(curl -s -o /dev/null -w '%{http_code}' "$API_URL/api/v1/auth/sso/providers")"
if [ "$status" != "200" ]; then
  echo "smoke: /api/v1/auth/sso/providers answered $status, expected 200." >&2
  exit 1
fi

echo "smoke: OK — API + worker built and booted in Production mode, and answered a real request."
