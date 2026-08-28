#!/bin/sh
# F2.6 — regenerates env-config.js from this container's own environment,
# every time it starts. Nothing here is baked in at `docker build`: the same
# image, run with different values for these three variables, serves a
# different config without a rebuild. → packages/auth/src/runtimeConfig.ts
#
# Runs via nginx's own /docker-entrypoint.d/ convention (nginxinc/nginx-unprivileged
# ships the same docker-entrypoint.sh as the official nginx image): every
# executable *.sh here is sourced, in name order, before nginx starts.
set -eu

# A value coming from a container's own environment is operator-controlled,
# not attacker-controlled — but it can still legitimately contain a `"` or a
# `\` (an escaped character in a URL), either of which would otherwise break
# out of the JS string literal below and leave env-config.js emitting invalid
# JavaScript instead of a wrong-but-harmless value.
escape() {
  printf '%s' "$1" | sed 's/\\/\\\\/g; s/"/\\"/g'
}

cat > /usr/share/nginx/html/env-config.js <<EOF
window.__VNI_RUNTIME_CONFIG__ = {
  apiBaseUrl: "$(escape "${API_BASE_URL:-}")",
  environment: "$(escape "${ENVIRONMENT_NAME:-}")",
  telemetryEndpoint: "$(escape "${TELEMETRY_ENDPOINT:-}")"
};
EOF
