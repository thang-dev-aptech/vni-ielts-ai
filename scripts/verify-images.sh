#!/usr/bin/env bash
#
# F2.6 — builds every OCI image this repo produces, tagged by commit SHA
# rather than a mutable `latest`, and proves the two properties the
# checklist actually asks for instead of assuming a Dockerfile that builds
# means a Dockerfile that is correct:
#
#   1. non-root — every image's own process runs as a non-root uid, not just
#      "the base image supports it".
#   2. the SAME learner/admin image serves a DIFFERENT runtime config when
#      started with different environment variables — the whole point of
#      F2.6's runtime-config work. A rebuild-per-environment regression
#      would pass every unit test in packages/auth and still be broken;
#      this is the only check that would catch it.
#
# Provider-neutral: builds and inspects images locally, never pushes
# anywhere. Which registry a deployment uses is an operational decision for
# whoever owns that deployment, not this script.
#
# Usage: scripts/verify-images.sh
# Requires Docker. Set VNI_REQUIRE_DOCKER=1 to fail instead of skip when it
# is unavailable — the same convention VNI_REQUIRE_MONGO/VNI_REQUIRE_MINIO
# already use in the backend test suites, so CI cannot silently skip this.

set -euo pipefail
cd "$(dirname "$0")/.."

if ! docker info >/dev/null 2>&1; then
  if [ "${VNI_REQUIRE_DOCKER:-0}" = "1" ]; then
    echo "Docker is required (VNI_REQUIRE_DOCKER=1) but not reachable." >&2
    exit 1
  fi
  echo "Docker is not available — skipping image verification."
  exit 0
fi

SHA="$(git rev-parse HEAD)"
FAILURES=0
CONTAINERS=()

cleanup() {
  local status=$?
  for c in "${CONTAINERS[@]:-}"; do
    [ -n "$c" ] && docker rm -f "$c" >/dev/null 2>&1 || true
  done
  exit "$status"
}
trap cleanup EXIT

fail() {
  echo "FAIL: $1" >&2
  FAILURES=$((FAILURES + 1))
}

pass() {
  echo "ok — $1"
}

build() {
  local name="$1" dockerfile="$2" context="$3"
  echo "── building $name:$SHA ($dockerfile) ──"
  docker build -f "$dockerfile" -t "$name:$SHA" "$context" >/dev/null
}

# Runs `id -u` inside a fresh container from $1's own entrypoint image and
# asserts it printed something other than 0 (root). `--entrypoint id -u`
# replaces this image's real entrypoint for the one call — nothing here
# depends on what the image normally does when it starts.
assert_non_root() {
  local image="$1"
  local uid
  uid="$(docker run --rm --entrypoint id "$image:$SHA" -u)"
  if [ "$uid" = "0" ]; then
    fail "$image:$SHA runs as uid 0 (root)"
  else
    pass "$image:$SHA runs as uid $uid, not root"
  fi
}

# The claim this whole runtime-config mechanism exists to make true: one
# built image, two different container environments, two different served
# configs — no rebuild in between. Runs both containers from the SAME image
# reference simultaneously specifically so a rebuild between them could not
# accidentally make this pass for the wrong reason.
assert_runtime_config_not_baked_in() {
  local image="$1" port_a="$2" port_b="$3"
  local name_a="vni-verify-${image}-a" name_b="vni-verify-${image}-b"
  CONTAINERS+=("$name_a" "$name_b")

  docker run -d --name "$name_a" -p "127.0.0.1:${port_a}:8080" \
    -e API_BASE_URL="https://api.a.invalid" -e ENVIRONMENT_NAME="env-a" \
    "$image:$SHA" >/dev/null
  docker run -d --name "$name_b" -p "127.0.0.1:${port_b}:8080" \
    -e API_BASE_URL="https://api.b.invalid" -e ENVIRONMENT_NAME="env-b" \
    "$image:$SHA" >/dev/null

  local ready=0
  for _ in $(seq 1 20); do
    if curl -sf "http://127.0.0.1:${port_a}/env-config.js" >/dev/null 2>&1 \
      && curl -sf "http://127.0.0.1:${port_b}/env-config.js" >/dev/null 2>&1; then
      ready=1
      break
    fi
    sleep 0.5
  done
  if [ "$ready" != "1" ]; then
    fail "$image:$SHA — env-config.js never became reachable on either container"
    return
  fi

  local config_a config_b
  config_a="$(curl -sf "http://127.0.0.1:${port_a}/env-config.js")"
  config_b="$(curl -sf "http://127.0.0.1:${port_b}/env-config.js")"

  if [ "$config_a" = "$config_b" ]; then
    fail "$image:$SHA served identical env-config.js from two different environments"
  elif ! grep -q "api.a.invalid" <<<"$config_a" || ! grep -q "env-a" <<<"$config_a"; then
    fail "$image:$SHA container A did not reflect its own environment"
  elif ! grep -q "api.b.invalid" <<<"$config_b" || ! grep -q "env-b" <<<"$config_b"; then
    fail "$image:$SHA container B did not reflect its own environment"
  else
    pass "$image:$SHA — same image, two containers, two different served configs"
  fi
}

build vni-ielts-api backend/Dockerfile backend
build vni-ielts-worker backend/Dockerfile.worker backend
build vni-ielts-web apps/web/Dockerfile .
build vni-ielts-admin apps/admin/Dockerfile .

assert_non_root vni-ielts-api
assert_non_root vni-ielts-worker
assert_non_root vni-ielts-web
assert_non_root vni-ielts-admin

assert_runtime_config_not_baked_in vni-ielts-web 18190 18191
assert_runtime_config_not_baked_in vni-ielts-admin 18192 18193

echo
if [ "$FAILURES" -gt 0 ]; then
  echo "$FAILURES check(s) failed."
  exit 1
fi
echo "All image checks passed. Tagged with $SHA:"
for name in vni-ielts-api vni-ielts-worker vni-ielts-web vni-ielts-admin; do
  echo "  $name:$SHA"
done
