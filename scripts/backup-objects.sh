#!/usr/bin/env bash
#
# Mirrors the object-storage buckets to a second location.
#
# ── Why this is separate from scripts/backup.sh ──────────────────────────────
#
# The database backup is a point-in-time archive; this is a mirror. They are
# different operations because the data is different in one way that matters:
# an exam package or a recording is written once and never modified, so the
# useful protection is a second copy in another failure domain rather than a
# history of a file that has no history.
#
# ── What a mirror does not protect against, and what does ────────────────────
#
# <b>A mirror faithfully copies a deletion.</b> `mc mirror --remove` propagates
# a delete; without it, a mirror silently accumulates objects the source has
# already dropped, which quietly breaks the retention window PDPL requires.
# Neither behaviour saves an operator who overwrote a good exam package with a
# broken one.
#
# What saves them is <b>versioning on the source bucket</b>, which is enabled for
# the three authored buckets in infra/docker/compose.yaml and deliberately not
# for the two holding learner audio and generated artefacts — a version history
# there would be a copy that outlives the deletion it was meant to honour.
#
# So: versioning for authorship mistakes, mirror for losing the storage.
#
# Usage:
#   scripts/backup-objects.sh SOURCE_ALIAS TARGET_ALIAS
#
# `mc alias set` both first. This never takes credentials on its command line —
# an argument is visible in `ps` to every user on the machine.
#
set -euo pipefail

if [[ $# -ne 2 ]]; then
  echo "usage: scripts/backup-objects.sh SOURCE_ALIAS TARGET_ALIAS" >&2
  echo "       configure both with 'mc alias set' first; never pass keys here." >&2
  exit 2
fi

SOURCE="$1"
TARGET="$2"

command -v mc >/dev/null 2>&1 || { echo "backup-objects: mc is not on PATH." >&2; exit 2; }

# Authored content, mirrored WITH --remove so the copy tracks the source. Safe
# here precisely because these buckets are versioned: a propagated delete is
# recoverable on the source side.
AUTHORED=(vni-exam-assets vni-packages vni-documents)

# Learner data, mirrored WITH --remove because retention has to reach the copy.
# A backup that keeps a recording the product has deleted is a PDPL problem
# wearing the costume of a safety measure.
RETAINED=(vni-audio-90d vni-artifacts-2y)

for bucket in "${AUTHORED[@]}" "${RETAINED[@]}"; do
  echo "backup-objects: $SOURCE/$bucket → $TARGET/$bucket"
  mc mirror --overwrite --remove --quiet "$SOURCE/$bucket" "$TARGET/$bucket"
done

echo "backup-objects: done"
