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

FROM nginxinc/nginx-unprivileged:1.27-alpine@sha256:65e3e85dbaed8ba248841d9d58a899b6197106c23cb0ff1a132b7bfe0547e4c0 AS runtime

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
