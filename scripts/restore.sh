#!/usr/bin/env bash
#
# Restores an encrypted backup produced by scripts/backup.sh.
#
# ── The two things this refuses to do ────────────────────────────────────────
#
# <b>It never drops anything on its own.</b> `mongorestore --drop` is one word
# away and it is the word that turns a recovery into a second outage: run
# against the wrong URI, it deletes the production data somebody was trying to
# protect. Dropping is the operator's decision, made with --drop, typed out.
#
# <b>It never guesses the namespace.</b> Restoring the whole archive when the
# operator meant one collection is the same mistake wearing a different hat.
# --ns-include narrows it; without it, the archive goes back exactly where it
# came from, which is what a disaster recovery actually wants.
#
# Usage:
#   VNI_BACKUP_KEY_FILE=/etc/vni/backup.key scripts/restore.sh ARCHIVE [options]
#
#     --uri URI          where to restore to (default: the local stack)
#     --ns-include GLOB   restore only these namespaces, e.g. 'vni_ielts.*'
#     --ns-from A --ns-to B   rename while restoring, to verify beside live data
#     --drop              drop each collection before restoring it
#     --oplog-replay      replay the oplog captured during the dump
#
set -euo pipefail

if [[ $# -lt 1 ]]; then
  sed -n '2,25p' "$0" | sed 's/^# \{0,1\}//' >&2
  exit 2
fi

ARCHIVE="$1"; shift

MONGO_URI="${VNI_BACKUP_MONGO_URI:-mongodb://localhost:27018/?directConnection=true}"
NS_INCLUDE=""
NS_FROM=""
NS_TO=""
DROP=""
OPLOG=""

while [[ $# -gt 0 ]]; do
  case "$1" in
    --uri)          MONGO_URI="$2"; shift 2 ;;
    --ns-include)   NS_INCLUDE="$2"; shift 2 ;;
    --ns-from)      NS_FROM="$2"; shift 2 ;;
    --ns-to)        NS_TO="$2"; shift 2 ;;
    --drop)         DROP="--drop"; shift ;;
    --oplog-replay) OPLOG="--oplogReplay"; shift ;;
    *) echo "restore: unknown option $1" >&2; exit 2 ;;
  esac
done

if [[ -z "${VNI_BACKUP_KEY_FILE:-}" ]]; then
  echo "restore: VNI_BACKUP_KEY_FILE is not set." >&2
  exit 2
fi

if [[ ! -r "$ARCHIVE" ]]; then
  echo "restore: cannot read $ARCHIVE" >&2
  exit 2
fi

# Overridable for the same reason as in backup.sh — a host with Docker and no
# database tools. The archive arrives on stdin either way.
#
#   VNI_MONGORESTORE="docker run --rm -i --network host mongo:7 mongorestore"
read -r -a MONGORESTORE <<< "${VNI_MONGORESTORE:-mongorestore}"

command -v gpg >/dev/null 2>&1 || { echo "restore: gpg is not on PATH." >&2; exit 2; }
command -v "${MONGORESTORE[0]}" >/dev/null 2>&1 \
  || { echo "restore: ${MONGORESTORE[0]} is not on PATH." >&2; exit 2; }

# ── Integrity before decryption ──────────────────────────────────────────────
#
# A truncated transfer and a wrong key both fail to decrypt, and they need
# opposite responses: fetch it again, or find the right key. Checking the
# checksum first makes the message say which.
if [[ -r "$ARCHIVE.sha256" ]]; then
  if command -v shasum >/dev/null 2>&1; then
    shasum -a 256 -c "$ARCHIVE.sha256" >/dev/null 2>&1 \
      || { echo "restore: $ARCHIVE does not match its checksum — the file is damaged, not the key." >&2; exit 1; }
  elif command -v sha256sum >/dev/null 2>&1; then
    sha256sum -c "$ARCHIVE.sha256" >/dev/null 2>&1 \
      || { echo "restore: $ARCHIVE does not match its checksum — the file is damaged, not the key." >&2; exit 1; }
  fi
fi

ARGS=(--uri="$MONGO_URI" --gzip --archive --quiet)
[[ -n "$NS_INCLUDE" ]] && ARGS+=(--nsInclude "$NS_INCLUDE")
[[ -n "$NS_FROM"    ]] && ARGS+=(--nsFrom "$NS_FROM")
[[ -n "$NS_TO"      ]] && ARGS+=(--nsTo "$NS_TO")
[[ -n "$DROP"       ]] && ARGS+=("$DROP")
[[ -n "$OPLOG"      ]] && ARGS+=("$OPLOG")

# Decrypt straight into mongorestore. The plaintext archive never exists as a
# file, so a failed restore leaves no readable copy of every learner's answers
# in a temp directory.
gpg --batch --quiet --decrypt \
    --passphrase-file "$VNI_BACKUP_KEY_FILE" \
    "$ARCHIVE" \
| "${MONGORESTORE[@]}" "${ARGS[@]}"
