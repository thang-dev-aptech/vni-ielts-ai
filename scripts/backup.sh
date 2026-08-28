#!/usr/bin/env bash
#
# Takes an encrypted, point-in-time-consistent backup of the whole MongoDB
# instance.
#
# ── Why a logical dump rather than a volume snapshot ──────────────────────────
#
# [QUYẾT ĐỊNH kỹ thuật] 28/08/2026 — `mongodump --oplog`, not a filesystem
# snapshot of the data volume.
#
# A snapshot of a running MongoDB is only safe if the filesystem can take an
# atomic one AND the journal is on the same volume. Docker's named volumes on
# the machines this product will run on give neither guarantee, so a snapshot
# taken while a write is in flight restores into a database that has to be
# repaired — and the repair is discovered during the incident, not before it.
#
# `--oplog` records the operations that happened *while the dump ran*, so the
# restore can replay them to a single consistent instant. That requires a
# replica set, which this product already runs everywhere for exactly this
# family of reasons. → infra/docker/compose.yaml
#
# The cost of being wrong: a logical dump is slower than a snapshot and scales
# with data size rather than disk size. At this product's scale — exam packages
# measured in megabytes, learner answers in kilobytes — that is minutes, and the
# recovery objectives in docs/development/nfr.md are set with that in mind. If
# the collection ever outgrows it, the replacement is a managed continuous
# backup, not a snapshot.
#
# ── Why the whole instance ───────────────────────────────────────────────────
#
# `--oplog` cannot be combined with `--db`: a point-in-time position is a
# property of the instance, not of one database. Backing up everything is also
# the honest default — a restore that quietly omitted a collection somebody
# added later is the kind of gap found only when it matters.
#
# Usage:
#   VNI_BACKUP_KEY_FILE=/etc/vni/backup.key scripts/backup.sh [output-dir]
#
set -euo pipefail

MONGO_URI="${VNI_BACKUP_MONGO_URI:-mongodb://localhost:27018/?directConnection=true}"
OUT_DIR="${1:-${VNI_BACKUP_DIR:-./backups}}"

# ── The key ──────────────────────────────────────────────────────────────────
#
# Refused rather than defaulted, and this is the one place in the script worth
# arguing about.
#
# A backup contains every learner's email address, every answer they wrote and
# every band they were given. Under Vietnam's PDPL that is personal data with
# the same obligations wherever it sits, and a backup file is the copy most
# likely to end up somewhere nobody is thinking about — a laptop, an S3 bucket
# with a permissive policy, an engineer's home directory.
#
# So there is no unencrypted mode. A script that fell back to writing plaintext
# when a key was missing would do it on the day somebody was in a hurry.
# → docs/security/privacy-vietnam-pdpl.md
if [[ -z "${VNI_BACKUP_KEY_FILE:-}" ]]; then
  echo "backup: VNI_BACKUP_KEY_FILE is not set." >&2
  echo "        There is no unencrypted mode. A backup carries every learner's" >&2
  echo "        answers and email address; PDPL obligations follow the copy." >&2
  exit 2
fi

if [[ ! -r "$VNI_BACKUP_KEY_FILE" ]]; then
  echo "backup: cannot read $VNI_BACKUP_KEY_FILE" >&2
  exit 2
fi

if [[ ! -s "$VNI_BACKUP_KEY_FILE" ]]; then
  echo "backup: $VNI_BACKUP_KEY_FILE is empty. An empty passphrase encrypts nothing." >&2
  exit 2
fi

# A key file the whole machine can read is a key file that is not a secret.
# Checked rather than fixed: silently chmod-ing somebody else's file is a
# surprise, and the surprise would hide how it got that way.
if [[ -n "$(find "$VNI_BACKUP_KEY_FILE" -perm +044 2>/dev/null || find "$VNI_BACKUP_KEY_FILE" -perm /044 2>/dev/null)" ]]; then
  echo "backup: $VNI_BACKUP_KEY_FILE is readable by group or other. chmod 600 it." >&2
  exit 2
fi

# ── Where mongodump comes from ───────────────────────────────────────────────
#
# <b>Overridable, because a backup host is often a host with Docker and nothing
# else.</b> Installing the MongoDB database tools on a machine whose only job is
# to hold an archive is one more thing to patch; running the same image the
# database runs is not.
#
#   VNI_MONGODUMP="docker run --rm --network host mongo:7 mongodump" scripts/backup.sh
#
# The archive streams over stdout either way, so the pipeline below does not
# care which one it is.
read -r -a MONGODUMP <<< "${VNI_MONGODUMP:-mongodump}"

command -v gpg >/dev/null 2>&1 || { echo "backup: gpg is not on PATH." >&2; exit 2; }
command -v "${MONGODUMP[0]}" >/dev/null 2>&1 \
  || { echo "backup: ${MONGODUMP[0]} is not on PATH." >&2; exit 2; }

mkdir -p "$OUT_DIR"

STAMP="$(date -u +%Y%m%dT%H%M%SZ)"
ARCHIVE="$OUT_DIR/vni-$STAMP.archive.gz.gpg"

# ── The pipeline ─────────────────────────────────────────────────────────────
#
# Straight through, never landing on disk in the clear. Writing a plaintext
# archive and encrypting it afterwards leaves a window — and, on a crash, a
# plaintext file nobody deletes.
#
# `--compress-algo none` because the stream is already gzipped by mongodump;
# compressing ciphertext-bound data twice costs CPU and saves nothing.
#
# The S2K settings are deliberate. gpg's defaults are old, and while a key file
# should hold high-entropy bytes rather than a password, a passphrase somebody
# typed is what will actually be in there one day.
"${MONGODUMP[@]}" \
  --uri="$MONGO_URI" \
  --oplog \
  --gzip \
  --archive \
  --quiet \
| gpg --batch --yes --quiet \
      --symmetric \
      --cipher-algo AES256 \
      --digest-algo SHA512 \
      --s2k-mode 3 \
      --s2k-digest-algo SHA512 \
      --s2k-count 65011712 \
      --compress-algo none \
      --passphrase-file "$VNI_BACKUP_KEY_FILE" \
      --output "$ARCHIVE"

chmod 600 "$ARCHIVE"

# A checksum sidecar, so a restore can tell a truncated transfer from a wrong
# key. Both fail; only one of them means the backup itself is gone.
if command -v shasum >/dev/null 2>&1; then
  shasum -a 256 "$ARCHIVE" > "$ARCHIVE.sha256"
elif command -v sha256sum >/dev/null 2>&1; then
  sha256sum "$ARCHIVE" > "$ARCHIVE.sha256"
fi

echo "$ARCHIVE"
