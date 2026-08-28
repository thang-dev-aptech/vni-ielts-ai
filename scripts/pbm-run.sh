#!/usr/bin/env bash
#
# F3.5 — the portable backup runner.
#
# ── The problem ─────────────────────────────────────────────────────────────
#
# Every backup script written for F3.3/F3.4 reaches the database the same way:
# `docker exec vni-pbm pbm …`. That works on the laptop it was written on and
# nowhere else. It assumes a Docker daemon, a socket this process may talk to,
# and a container with a particular NAME — none of which a scheduler has.
# A Kubernetes CronJob, a systemd timer on the database host, or a Nomad
# periodic job would each need the commands rewritten, and a backup command
# rewritten under time pressure is a backup command nobody has drilled.
#
# ── The fix: one command surface, two transports ────────────────────────────
#
# The operation is the same everywhere; only *how the pbm binary is reached*
# differs. So that is the only thing that varies:
#
#   direct  — `pbm` is on PATH. This is the scheduler case: a container built
#             from percona/percona-backup-mongodb, a pod, or the database host
#             itself. Nothing Docker-specific is involved.
#   docker  — shell out to `docker exec <container> pbm`. The developer case.
#
# Detected automatically, overridable with VNI_PBM_MODE. Every other script in
# this directory can therefore stay a thin wrapper around one contract instead
# of each growing its own copy of the connection logic.
#
# ── This does NOT schedule anything, deliberately ───────────────────────────
#
# No cron entry, no timer unit, no CronJob manifest is installed by this
# repository. The platform is not chosen (that is a Production Ready item),
# and a schedule quietly committed here would become an RPO promise nobody
# made — `G-11`. What this provides is a command a scheduler can call and a
# configuration contract it can fill in. Claiming more would be claiming a
# production schedule exists when it does not.
#
# ── Configuration contract ──────────────────────────────────────────────────
#
#   VNI_PBM_MODE         direct | docker        (default: auto-detect)
#   VNI_PBM_CONTAINER    container name, docker mode only   (default: vni-pbm)
#   VNI_PBM_URI          MongoDB URI            (default: local replica set)
#   VNI_PBM_MAX_PITR_LAG_SECONDS      alert threshold       (default: 300)
#   VNI_PBM_MAX_BACKUP_AGE_SECONDS    alert threshold       (default: 93600)
#   VNI_PBM_KEEP_DAILY / _WEEKLY / _MONTHLY   retention     (default: 7/5/12)
#
# Exit code is the whole interface: 0 succeeded, non-zero did not. Every
# scheduler on the list above reads that, and none of them needs anything else.
#
# Usage:
#   scripts/pbm-run.sh backup       # take a full backup
#   scripts/pbm-run.sh alert        # fail if backups are stale
#   scripts/pbm-run.sh retention    # report what retention would delete
#   scripts/pbm-run.sh retention --apply
#   scripts/pbm-run.sh status
#   scripts/pbm-run.sh pbm <args…>  # anything else, passed through
#
set -uo pipefail

# <b>Deliberately NOT setting MSYS_NO_PATHCONV here.</b> The sibling scripts
# that pass CONTAINER-side paths set it for themselves. Setting it globally in
# this dispatcher breaks the opposite case: `$HERE` is a POSIX path from Git
# Bash, and handing it unconverted to a native Windows `node` produced
# `Cannot find module 'C:\c\Users\...'` — a real path with an extra `c\`
# glued on. One process cannot want both conversions, so each scope picks the
# one it needs. → F1, and the same trap `pbm-setup.sh` documents.
HERE="$(cd "$(dirname "$0")" && pwd)"

CONTAINER="${VNI_PBM_CONTAINER:-vni-pbm}"
URI="${VNI_PBM_URI:-mongodb://localhost:27017/?replicaSet=rs0}"

# ── Transport ───────────────────────────────────────────────────────────────
resolve_mode() {
  if [ -n "${VNI_PBM_MODE:-}" ]; then
    echo "$VNI_PBM_MODE"
    return
  fi
  # `pbm` on PATH means this IS the backup container or host — no Docker hop.
  if command -v pbm >/dev/null 2>&1; then
    echo direct
  else
    echo docker
  fi
}

MODE="$(resolve_mode)"

pbm_exec() {
  case "$MODE" in
    direct)
      PBM_MONGODB_URI="$URI" pbm "$@"
      ;;
    docker)
      docker exec -e PBM_MONGODB_URI="$URI" "$CONTAINER" pbm "$@"
      ;;
    *)
      echo "pbm-run: unknown VNI_PBM_MODE '$MODE' (expected 'direct' or 'docker')." >&2
      return 2
      ;;
  esac
}

# Exported so the sibling scripts use the transport resolved here rather than
# each re-deciding — one contract, not four.
export VNI_PBM_MODE="$MODE"

command="${1:-}"
[ "$#" -gt 0 ] && shift

case "$command" in
  backup)
    echo "pbm-run: [$MODE] taking a full backup..."
    pbm_exec backup --wait "$@"
    ;;

  status)
    pbm_exec status "$@"
    ;;

  alert)
    exec "$HERE/pbm-alert.sh" "$@"
    ;;

  retention)
    if ! command -v node >/dev/null 2>&1; then
      echo "pbm-run: retention needs Node.js on PATH (the selection logic is tested JS)." >&2
      exit 2
    fi
    exec node "$HERE/pbm-retention.mjs" "$@"
    ;;

  pbm)
    pbm_exec "$@"
    ;;

  ''|-h|--help|help)
    sed -n '2,55p' "$0" | sed 's/^# \{0,1\}//'
    exit 0
    ;;

  *)
    echo "pbm-run: unknown command '$command'. Try: backup | alert | retention | status | pbm" >&2
    exit 2
    ;;
esac
