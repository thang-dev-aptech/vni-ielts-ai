#!/usr/bin/env bash
#
# F4.1 — telemetry really reaches a real collector.
#
# ── Why this exists next to the in-process tests ────────────────────────────
#
# `TelemetryExportTests` asserts the SDK side: that the activity source
# produces spans, that database work nests inside the request, that a log
# written during a span carries that span's ids. All true and all checkable
# without a collector.
#
# None of it proves the OTLP wire path. Encoding, transport and a receiver
# accepting the payload are a separate set of ways to be broken, and a fake
# receiver written beside the code that produces the spans proves only that
# the two agree with each other. So this runs the upstream collector binary
# and asserts on what IT saw.
#
# <b>All three signals, because they fail independently.</b> Traces export on
# a batch timer, logs on their own processor, metrics on a periodic reader
# whose default interval is 60 seconds — so a check that waits ten seconds
# and declares success has really only tested traces.
#
# Provider-neutral: the collector's only exporter is `debug`. Nothing leaves
# the machine and no observability vendor is chosen. → F4.1
#
# Usage:
#   docker compose -f infra/docker/compose.yaml up -d     # needs MongoDB
#   scripts/otel-smoke.sh
#
set -euo pipefail

# <b>Two path worlds in one script, and only one setting can be right at a
# time.</b> `docker run -v` needs the HOST side in Windows form; `dotnet`
# needs a path it can actually open. Exporting MSYS_NO_PATHCONV globally
# breaks the second (the build silently failed with the csproj path unmangled
# into `/c/Users/...`), so nothing is exported here: `$ROOT` stays POSIX for
# dotnet, and `$ROOT_MOUNT` is the Windows form used only for the mount.
# → the same trap documented in pbm-run.sh and pbm-setup.sh
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
ROOT_MOUNT="$(cd "$ROOT" && { pwd -W 2>/dev/null || pwd; })"
COLLECTOR="vni-otel-smoke"
API_PORT="${VNI_OTEL_SMOKE_PORT:-5399}"
MONGO="${VNI_OTEL_SMOKE_MONGO:-mongodb://localhost:27018/?directConnection=true}"

# The metric reader's default export interval is 60s, so anything less than
# that is a test that cannot see metrics. → OTEL_METRIC_EXPORT_INTERVAL below
METRIC_WAIT="${VNI_OTEL_METRIC_WAIT:-25}"

API_PID=""
WORKER_PID=""

cleanup() {
  local code=$?
  [ -n "$API_PID" ] && kill "$API_PID" >/dev/null 2>&1 || true
  [ -n "$WORKER_PID" ] && kill "$WORKER_PID" >/dev/null 2>&1 || true
  docker rm -f "$COLLECTOR" >/dev/null 2>&1 || true
  exit "$code"
}
trap cleanup EXIT

echo "otel-smoke: starting the upstream collector..."
docker rm -f "$COLLECTOR" >/dev/null 2>&1 || true
MSYS_NO_PATHCONV=1 docker run -d --name "$COLLECTOR" \
  -p 127.0.0.1:4317:4317 -p 127.0.0.1:4318:4318 \
  -v "$ROOT_MOUNT/infra/docker/otel-collector-test.yaml:/etc/otelcol-contrib/config.yaml:ro" \
  otel/opentelemetry-collector-contrib:0.116.1 >/dev/null

for _ in $(seq 1 30); do
  curl -sf -o /dev/null -X POST "http://localhost:4318/v1/traces" \
    -H 'Content-Type: application/json' -d '{}' && break
  sleep 1
done

echo "otel-smoke: building the API..."
dotnet build "$ROOT/backend/src/Vni.Ielts.Api/Vni.Ielts.Api.csproj" -v:q >/dev/null

echo "otel-smoke: starting the API with OTLP export enabled..."
# <b>The built DLL, not `dotnet run`.</b> `dotnet run` launches the
# application as a CHILD process, so the pid this script holds is the
# launcher — killing it in `cleanup` leaves the real API running, holding a
# lock on `Vni.Ielts.Infrastructure.dll`. Every later build then fails with
# `MSB3027 ... locked by: Vni.Ielts.Api`. Observed here; a leaked process
# would do the same to CI. Running the DLL directly makes `$API_PID` the
# process that actually needs killing.
ASPNETCORE_ENVIRONMENT=Development \
OTEL_METRIC_EXPORT_INTERVAL=5000 \
dotnet "$ROOT/backend/src/Vni.Ielts.Api/bin/Debug/net10.0/Vni.Ielts.Api.dll" \
  --urls "http://127.0.0.1:${API_PORT}" \
  --Otel:Endpoint=http://localhost:4317 \
  --Mongo:ConnectionString="$MONGO" \
  --Mongo:Database="vni_otel_smoke" \
  --Jwt:SigningKey=kkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkk \
  > /tmp/vni-otel-smoke-api.log 2>&1 &
API_PID=$!

for _ in $(seq 1 90); do
  curl -sf "http://127.0.0.1:${API_PORT}/health/ready" >/dev/null 2>&1 && break
  sleep 1
done

if ! curl -sf "http://127.0.0.1:${API_PORT}/health/ready" >/dev/null 2>&1; then
  echo "otel-smoke: the API never became ready. Log:" >&2
  tail -30 /tmp/vni-otel-smoke-api.log >&2
  exit 1
fi

echo "otel-smoke: driving a request that touches MongoDB..."
# <b>The status code is deliberately ignored.</b> What this needs is a request
# that reaches the pipeline and touches the database — a 201 on the first run
# and a 400 ("email already registered") on every run after it are equally
# good for that. `|| true` because the assertion is on what the COLLECTOR saw,
# not on what the API answered, and under `set -e` a duplicate-email 400 (or
# curl's own exit 23 writing a discarded body) would otherwise end the script
# before a single assertion ran.
curl -s -o /dev/null -X POST "http://127.0.0.1:${API_PORT}/api/v1/auth/register" \
  -H 'Content-Type: application/json' \
  -d '{"email":"otel-smoke@example.com","password":"Password123!Aa","displayName":"Smoke"}' \
  || true

# <b>The worker too, because the phase gate asks for both and they are wired
# separately.</b> They share `AddVniTelemetry` but register it in two different
# Program.cs files under two different service names, so "the API exports"
# proves nothing about the worker — and the worker is where the queue metrics
# and the marking spans come from.
echo "otel-smoke: starting the worker with OTLP export enabled..."
dotnet build "$ROOT/backend/src/Vni.Ielts.Worker/Vni.Ielts.Worker.csproj" -v:q >/dev/null

DOTNET_ENVIRONMENT=Development \
ASPNETCORE_HTTP_PORTS="${VNI_OTEL_SMOKE_WORKER_PORT:-5398}" \
OTEL_METRIC_EXPORT_INTERVAL=5000 \
dotnet "$ROOT/backend/src/Vni.Ielts.Worker/bin/Debug/net10.0/Vni.Ielts.Worker.dll" \
  --Otel:Endpoint=http://localhost:4317 \
  --Mongo:ConnectionString="$MONGO" \
  --Mongo:Database="vni_otel_smoke" \
  --Jwt:SigningKey=kkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkk \
  > /tmp/vni-otel-smoke-worker.log 2>&1 &
WORKER_PID=$!

echo "otel-smoke: waiting ${METRIC_WAIT}s for all three signals to export..."
sleep "$METRIC_WAIT"

LOG="$(docker logs "$COLLECTOR" 2>&1)"
FAIL=0

check() {
  local what="$1" pattern="$2"
  local count
  count="$(printf '%s' "$LOG" | grep -c -- "$pattern" || true)"
  if [ "$count" -gt 0 ]; then
    echo "otel-smoke:   ok — $what ($count)"
  else
    echo "otel-smoke:   FAIL — no $what reached the collector" >&2
    FAIL=1
  fi
}

echo
echo "otel-smoke: ── what the collector actually received ──"
check "telemetry identifying itself as vni-api" "service.name: Str(vni-api)"
check "trace batches"   "ResourceSpans #"
check "metric batches"  "ResourceMetrics #"
check "log batches"     "ResourceLog #"
check "a server span for the real request" "POST /api/v1/auth/register"
check "telemetry identifying itself as vni-worker" "service.name: Str(vni-worker)"

echo
if [ "$FAIL" -ne 0 ]; then
  echo "otel-smoke: FAILED — telemetry did not reach the collector." >&2
  exit 1
fi

echo "otel-smoke: OK — traces, metrics and logs all arrived over OTLP, from vni-api and vni-worker."
