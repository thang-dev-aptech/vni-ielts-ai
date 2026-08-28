#!/usr/bin/env bash
#
# F3.3 — applies the PBM storage/PITR configuration and reports what the
# recovery point actually is.
#
# ── Why PBM rather than extending scripts/backup.sh ─────────────────────────
#
# [QUYẾT ĐỊNH kỹ thuật] 28/08/2026 — Percona Backup for MongoDB, evaluated
# before writing any oplog-tailing code of our own, which is the order the
# Foundation checklist asks for.
#
# `scripts/backup.sh` is not replaced and is not wrong. `mongodump --oplog`
# gives an encrypted, consistent, self-contained artifact, and its gpg
# encryption is CLIENT-side, so it does not depend on the storage backend
# having a KMS. What it cannot give is a recovery point between two runs: the
# oplog it captures covers only the dump's own duration. Daily runs therefore
# mean losing up to a day, and `docs/development/nfr.md` already named the
# consequence — an incident at 11am loses a 9am sitting.
#
# Continuous oplog capture is the missing piece, and hand-rolling it is a
# well-known way to lose data quietly: resumption after an agent restart,
# rollover, and the ordering guarantees around a primary step-down are all
# easy to get subtly wrong and hard to notice until a restore is attempted.
# PBM is open source, speaks S3-compatible storage, and names no cloud — it
# is a tool, not a vendor commitment.
#
# The cost of being wrong: PBM is another moving part to operate, and its
# restore path is all-or-nothing at instance granularity. Both are accepted
# because the alternative is bespoke oplog code on the recovery path.
#
# Usage:
#   scripts/pbm-setup.sh              # apply config, enable PITR, report
#   VNI_PBM_SSE=AES256 scripts/pbm-setup.sh   # if the bucket has a KMS
#
# Environment (all optional; defaults are the local MinIO stack):
#   VNI_PBM_CONTAINER   agent container name              (default: vni-pbm)
#   VNI_PBM_URI         mongodb URI the agent uses
#   VNI_PBM_ENDPOINT    S3 endpoint URL
#   VNI_PBM_BUCKET      bucket name
#   VNI_PBM_ACCESS_KEY / VNI_PBM_SECRET_KEY
#   VNI_PBM_SSE         server-side encryption algorithm, e.g. AES256
#
set -euo pipefail

# <b>Git Bash rewrites arguments that look like absolute POSIX paths.</b> On
# Windows, `docker exec … pbm config --file /tmp/pbm-config.yaml` arrives at
# the container as `C:/Users/…/Temp/pbm-config.yaml` and fails with "no such
# file or directory" naming a path that was never asked for — observed here,
# not anticipated. The path is meaningful inside the container only, so the
# rewrite is always wrong. Ignored on Linux and macOS, where the variable
# means nothing. → F1 (clean checkout and toolchain across platforms)
export MSYS_NO_PATHCONV=1
export MSYS2_ARG_CONV_EXCL='*'

CONTAINER="${VNI_PBM_CONTAINER:-vni-pbm}"
URI="${VNI_PBM_URI:-mongodb://localhost:27017/?replicaSet=rs0}"
ENDPOINT="${VNI_PBM_ENDPOINT:-http://vni-minio:9000}"
BUCKET="${VNI_PBM_BUCKET:-vni-backups}"
ACCESS_KEY="${VNI_PBM_ACCESS_KEY:-vni-local}"
SECRET_KEY="${VNI_PBM_SECRET_KEY:-vni-local-dev-only}"
OPLOG_SPAN_MIN="${VNI_PBM_OPLOG_SPAN_MIN:-1}"

pbm() {
  docker exec -e PBM_MONGODB_URI="$URI" "$CONTAINER" pbm "$@"
}

if ! docker inspect "$CONTAINER" >/dev/null 2>&1; then
  echo "pbm-setup: no container named '$CONTAINER'." >&2
  echo "pbm-setup: start the stack first — docker compose -f infra/docker/compose.yaml up -d" >&2
  exit 2
fi

# Built here rather than shipped as a file with production values in it: the
# committed infra/docker/pbm-config.yaml carries only local placeholders, and
# a real deployment passes its own through the environment. → CLAUDE.md rule 6
CONFIG="$(mktemp)"
trap 'rm -f "$CONFIG"' EXIT

{
  echo "storage:"
  echo "  type: s3"
  echo "  s3:"
  echo "    endpointUrl: ${ENDPOINT}"
  echo "    bucket: ${BUCKET}"
  echo "    prefix: pbm"
  echo "    region: us-east-1"
  echo "    forcePathStyle: true"
  echo "    credentials:"
  echo "      access-key-id: ${ACCESS_KEY}"
  echo "      secret-access-key: ${SECRET_KEY}"
  if [ -n "${VNI_PBM_SSE:-}" ]; then
    echo "    serverSideEncryption:"
    echo "      sseAlgorithm: ${VNI_PBM_SSE}"
  fi
  echo "pitr:"
  echo "  enabled: true"
  echo "  oplogSpanMin: ${OPLOG_SPAN_MIN}"
} > "$CONFIG"

echo "pbm-setup: applying storage configuration to '$CONTAINER'..."

# Piped in rather than `docker cp`'d. `docker cp` takes a LOCAL path and a
# CONTAINER path in one argument list, and on Git Bash exactly one of them
# needs rewriting — there is no setting of MSYS_NO_PATHCONV that is right for
# both, which is how `C:\tmp\tmp.jFR8GXuo1u` came to be looked for. A redirect
# is resolved by this shell (so the local path is correct) and the container
# path is a quoted argument to `sh` (so nothing rewrites it).
#
# Written under the agent's own $HOME, not /tmp: the image already holds a
# root-owned /tmp/pbm-config.yaml, /tmp carries the sticky bit, and the agent
# runs as uid 1001 — so writing there fails with "Permission denied" on a
# path that looks perfectly writable. $HOME is resolved by the container's
# shell, so this stays correct if the image's user ever changes.
docker exec -i "$CONTAINER" sh -c 'cat > "$HOME/pbm-config.yaml"' < "$CONFIG"

# `pbm config --file` echoes the whole config back with the credentials
# masked; sending it to /dev/null keeps even the masked form out of CI logs.
# $HOME expands inside the container, matching where it was just written.
docker exec -e PBM_MONGODB_URI="$URI" "$CONTAINER" \
  sh -c 'pbm config --file "$HOME/pbm-config.yaml"' >/dev/null

# Set explicitly as well as in the file: applying a config file does not always
# re-arm the PITR routine on an agent that was already running.
pbm config --set pitr.enabled=true >/dev/null
pbm config --set "pitr.oplogSpanMin=${OPLOG_SPAN_MIN}" >/dev/null

echo "pbm-setup: waiting for the PITR routine to report ON..."
for _ in $(seq 1 20); do
  if pbm status 2>/dev/null | grep -q 'Status \[ON\]'; then
    break
  fi
  sleep 2
done

if ! pbm status 2>/dev/null | grep -q 'Status \[ON\]'; then
  echo "pbm-setup: PITR did not come on. Agent status follows:" >&2
  pbm status >&2 || true
  exit 1
fi

echo
echo "pbm-setup: OK — continuous oplog capture is on."
echo "pbm-setup: recovery point is at most ${OPLOG_SPAN_MIN} minute(s) of writes."
if [ -z "${VNI_PBM_SSE:-}" ]; then
  echo "pbm-setup: NOTE — no server-side encryption requested. Backups are"
  echo "pbm-setup:        encrypted at rest only if the bucket itself is."
fi
echo
pbm status
