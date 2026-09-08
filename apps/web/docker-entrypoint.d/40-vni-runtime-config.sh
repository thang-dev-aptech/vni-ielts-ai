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

# SUPPORT_ZALO_URL is the one value here that gets rendered to a visitor who is
# not signed in — it is the "ask a human" link on the forgot-password page,
# which exists because registration no longer collects an email and there is no
# self-service reset. A link on that page is exactly what a phishing lure wants
# to be: the visitor is already locked out, already looking for help, and about
# to type a password somewhere.
#
# So this is refused rather than escaped. `escape()` above stops a value from
# breaking the JS string literal; it does nothing about a value that is a
# perfectly well-formed `javascript:` or `data:` URL, which on an href is
# script execution on our own origin, or an `http://` one that hands the
# conversation to whoever is on the network.
#
# Refuse loudly and keep the container down. The alternative — drop the value
# and boot — leaves an operator who mistyped the scheme with a page that
# silently offers no support channel at all, and nothing anywhere saying why.
# A container that will not start is a fault someone reads; a missing link is
# one nobody notices until a learner cannot get back in.
support_zalo_url="${SUPPORT_ZALO_URL:-}"
if [ -n "$support_zalo_url" ]; then
  case "$support_zalo_url" in
    https://?*) ;;
    *)
      echo "40-vni-runtime-config.sh: SUPPORT_ZALO_URL must be an https:// URL; refusing to start." >&2
      exit 1
      ;;
  esac
fi

cat > /usr/share/nginx/html/env-config.js <<EOF
window.__VNI_RUNTIME_CONFIG__ = {
  apiBaseUrl: "$(escape "${API_BASE_URL:-}")",
  environment: "$(escape "${ENVIRONMENT_NAME:-}")",
  telemetryEndpoint: "$(escape "${TELEMETRY_ENDPOINT:-}")",
  supportZaloUrl: $([ -n "$support_zalo_url" ] && printf '"%s"' "$(escape "$support_zalo_url")" || printf 'null')
};
EOF
