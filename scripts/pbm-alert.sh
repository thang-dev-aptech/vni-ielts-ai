#!/usr/bin/env bash
#
# F3.4 — is the backup actually still happening?
#
# ── The failure this exists to catch ────────────────────────────────────────
#
# <b>A backup system that stops is silent.</b> Nothing errors when the PITR
# routine dies: the API serves traffic, the worker marks papers, every health
# check stays green, and the recovery point quietly slides from one minute to
# however long ago the agent stopped. It is discovered during the incident,
# which is the one moment it cannot be fixed.
#
# So staleness is asserted rather than assumed, and it is asserted against a
# clock: PITR coverage must reach within a threshold of NOW, and there must be
# a completed snapshot within a second, longer threshold.
#
# ── Local, and deliberately not a SaaS ──────────────────────────────────────
#
# This exits non-zero and prints one line per problem. That is the whole
# interface, and it is enough for cron/systemd `OnFailure=`, a Kubernetes
# CronJob, or a human. Choosing an alerting vendor is not this queue's call —
# the contract is an exit code, which every one of them can read.
#
# Usage:
#   scripts/pbm-alert.sh
#   VNI_PBM_MAX_PITR_LAG_SECONDS=300 VNI_PBM_MAX_BACKUP_AGE_SECONDS=93600 scripts/pbm-alert.sh
#
set -uo pipefail

export MSYS_NO_PATHCONV=1
export MSYS2_ARG_CONV_EXCL='*'

CONTAINER="${VNI_PBM_CONTAINER:-vni-pbm}"
URI="${VNI_PBM_URI:-mongodb://localhost:27017/?replicaSet=rs0}"

# <b>Five minutes, matching the RPO the mechanism is configured for.</b> The
# oplog span is one minute, so coverage more than five minutes behind means
# several slices in a row did not land — not jitter.
MAX_PITR_LAG="${VNI_PBM_MAX_PITR_LAG_SECONDS:-300}"

# 26 hours, not 24: a daily backup that starts at the same time each day would
# otherwise alarm on ordinary run-to-run drift.
MAX_BACKUP_AGE="${VNI_PBM_MAX_BACKUP_AGE_SECONDS:-93600}"

problems=0
note() { echo "pbm-alert: $*" >&2; problems=$((problems + 1)); }

# F3.5 — same transport contract as `pbm-run.sh`: `direct` when the pbm binary
# is on PATH (a scheduler pod, or the database host), `docker` otherwise. A
# staleness alarm that only runs on a laptop is not an alarm.
MODE="${VNI_PBM_MODE:-}"
if [ -z "$MODE" ]; then
  if command -v pbm >/dev/null 2>&1; then MODE=direct; else MODE=docker; fi
fi

if [ "$MODE" = docker ] && ! docker inspect "$CONTAINER" >/dev/null 2>&1; then
  note "CRITICAL — no container named '$CONTAINER'. Nothing is taking backups."
  exit 1
fi

if [ "$MODE" = direct ]; then
  LIST="$(PBM_MONGODB_URI="$URI" pbm list -o json 2>/dev/null || true)"
else
  LIST="$(docker exec -e PBM_MONGODB_URI="$URI" "$CONTAINER" pbm list -o json 2>/dev/null || true)"
fi

if [ -z "$LIST" ]; then
  note "CRITICAL — could not read backup status from '$CONTAINER'. Treating as no backups."
  exit 1
fi

NOW="$(date -u +%s)"

# ── PITR ────────────────────────────────────────────────────────────────────
PITR_ON="$(printf '%s' "$LIST" | grep -o '"on":[a-z]*' | head -1 | cut -d: -f2)"

if [ "$PITR_ON" != "true" ]; then
  note "CRITICAL — continuous PITR is OFF. The recovery point is now the last full backup."
else
  # The end of the newest PITR range, as a unix timestamp.
  PITR_END="$(printf '%s' "$LIST" \
    | grep -o '"end":[0-9]*' | cut -d: -f2 | sort -n | tail -1)"

  if [ -z "$PITR_END" ]; then
    note "CRITICAL — PITR reports ON but has no oplog coverage at all."
  else
    LAG=$((NOW - PITR_END))
    if [ "$LAG" -gt "$MAX_PITR_LAG" ]; then
      note "CRITICAL — PITR coverage is ${LAG}s behind (threshold ${MAX_PITR_LAG}s). Recent writes are not recoverable."
    else
      echo "pbm-alert: ok — PITR coverage is ${LAG}s behind (threshold ${MAX_PITR_LAG}s)."
    fi
  fi
fi

# ── Last completed snapshot ─────────────────────────────────────────────────
# `restoreTo` is a unix timestamp on each completed snapshot.
LAST_SNAP="$(printf '%s' "$LIST" | grep -o '"restoreTo":[0-9]*' | cut -d: -f2 | sort -n | tail -1)"

if [ -z "$LAST_SNAP" ]; then
  note "CRITICAL — there is no completed snapshot. PITR alone cannot restore without a base backup."
else
  AGE=$((NOW - LAST_SNAP))
  if [ "$AGE" -gt "$MAX_BACKUP_AGE" ]; then
    note "WARNING — the newest full backup is ${AGE}s old (threshold ${MAX_BACKUP_AGE}s)."
  else
    echo "pbm-alert: ok — newest full backup is ${AGE}s old (threshold ${MAX_BACKUP_AGE}s)."
  fi
fi

if [ "$problems" -ne 0 ]; then
  echo "pbm-alert: $problems problem(s). Backups are not in the state this product needs." >&2
  exit 1
fi

echo "pbm-alert: OK — backups are current."
