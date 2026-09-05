# syntax=docker/dockerfile:1

# ── The learner web app image ───────────────────────────────────────────
#
# <b>F2.6 — written 2026-08-28. There was no Dockerfile for either frontend
# at all</b>, and the API base URL was a Vite build-time env var
# (`VITE_API_BASE`, inlined into the JS bundle at `vite build`) — so the same
# built image could never move between environments without a rebuild, and a
# rebuild can drift from the bundle that was actually tested.
# → packages/auth/src/runtimeConfig.ts, docker-entrypoint.d/
#
# <b>Build context is the repo root, not this directory.</b> This app is a
# pnpm workspace member — it depends on @vni/auth, @vni/design-system,
# @vni/ui and the generated @vni/api-client, all siblings under packages/ —
# so a `pnpm install` here needs the whole workspace, not just apps/web/.
# Build from the repo root:
#   docker build -f apps/web/Dockerfile -t vni-ielts-web:<tag> .

FROM node:24-bookworm-slim@sha256:ba849c60be29959425b8734d57b8b4b7d56f98edd9504c9af091d5281095a71e AS build
WORKDIR /repo

COPY . .

RUN corepack enable && pnpm install --frozen-lockfile
RUN pnpm run generate:api-client
RUN pnpm --filter @vni/web build

# <b>nginx-unprivileged, not root nginx plus a manual USER switch.</b> The
# stock nginx image binds port 80, which needs root; getting off that safely
# means either `chown`-ing every path nginx writes to (its pid file, its temp
# dirs) or trusting someone else's recipe for it. nginx-unprivileged is the
# nginx project's own build for exactly this — non-root by default, listening
# on 8080, already owning everything it needs to.
FROM nginxinc/nginx-unprivileged:1.29-alpine@sha256:0c79d56aee561a1d81c63f00eee5fb5fe29279560cdc55e91425133104c7fbe6 AS runtime

# <b>Patch the base, do not merely pin it.</b> F4.5 pins by digest so a build is
# reproducible; this line exists because a pin alone is a promise to ship the
# same known-vulnerable packages forever. Measured on 2026-08-29 with
# `trivy image --severity HIGH,CRITICAL --ignore-unfixed`:
#
#   nginx-unprivileged:1.27-alpine   33 (HIGH 31, CRITICAL 2)  <- what CI failed on
#   nginx-unprivileged:1.29-alpine   12 (HIGH 12, CRITICAL 0)  <- newest tag, still red
#   1.29-alpine + apk upgrade         0
#
# So moving the pin forward was necessary and not sufficient: the upstream image
# is rebuilt on its own schedule, and Alpine publishes package fixes between
# those rebuilds. openssl was 3.3.3-r0 against a 3.3.7-r0 fix.
#
# The cost, stated plainly: `apk upgrade` resolves at build time, so two builds
# of this Dockerfile from the same digest can differ. That is a deliberate trade
# — the digest still fixes the base layer and the SBOM records exactly what
# shipped, which is what F4.5 needs; what it gives up is the pretence that a
# months-old pin is safe merely because it is exact.
USER root
RUN apk --no-cache upgrade
USER nginx

# <b>--chown, and it has to be explicit.</b> The base image's
# /usr/share/nginx/html is root-owned (mode 755) even though the process
# itself runs as uid 101 — confirmed by running the base image directly, not
# assumed. Without this, the container boots as non-root correctly and then
# fails at the one thing this Dockerfile exists to make work: the entrypoint
# script cannot overwrite env-config.js, and nginx never starts at all.
COPY --from=build --chown=nginx:nginx /repo/apps/web/dist /usr/share/nginx/html
COPY apps/web/nginx.conf /etc/nginx/conf.d/default.conf

# <b>Runs through nginx's own /docker-entrypoint.d/ convention</b> — every
# executable *.sh there is sourced, in name order, by the image's own
# docker-entrypoint.sh before nginx starts. Not a custom ENTRYPOINT: that
# would mean re-implementing what the base image already does correctly
# (signal handling, the same script-sourcing contract) for no reason.
COPY apps/web/docker-entrypoint.d/40-vni-runtime-config.sh /docker-entrypoint.d/40-vni-runtime-config.sh

EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=3s --start-period=5s --retries=3 \
    CMD wget -q -O /dev/null http://127.0.0.1:8080/ || exit 1
