# syntax=docker/dockerfile:1

# ── The admin CMS image ─────────────────────────────────────────────────
#
# Everything here follows apps/web/Dockerfile for the same reasons: a pnpm
# workspace build from the repo root, runtime config generated at container
# start rather than baked in, nginx-unprivileged for non-root without a
# hand-rolled recipe. → F2.6, packages/auth/src/runtimeConfig.ts
#
# Build from the repo root:
#   docker build -f apps/admin/Dockerfile -t vni-ielts-admin:<tag> .

FROM node:24-bookworm-slim@sha256:ba849c60be29959425b8734d57b8b4b7d56f98edd9504c9af091d5281095a71e AS build
WORKDIR /repo

COPY . .

RUN corepack enable && pnpm install --frozen-lockfile
RUN pnpm --filter @vni/admin build

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

# --chown, and it has to be explicit — see apps/web/Dockerfile for why:
# the base image's html directory is root-owned even though nginx itself
# runs as uid 101, so without this the entrypoint's config rewrite fails
# and the container never starts.
COPY --from=build --chown=nginx:nginx /repo/apps/admin/dist /usr/share/nginx/html
COPY apps/admin/nginx.conf /etc/nginx/conf.d/default.conf
COPY apps/admin/docker-entrypoint.d/40-vni-runtime-config.sh /docker-entrypoint.d/40-vni-runtime-config.sh

EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=3s --start-period=5s --retries=3 \
    CMD wget -q -O /dev/null http://127.0.0.1:8080/ || exit 1
