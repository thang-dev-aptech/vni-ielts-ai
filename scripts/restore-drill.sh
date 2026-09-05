#!/usr/bin/env bash
#
# The restore drill.
#
# ── Why a drill exists at all ────────────────────────────────────────────────
#
# <b>A backup nobody has restored is a hypothesis.</b> The failures that matter
# are never "the dump did not run" — that one is loud and somebody notices the
# same week. They are the quiet ones: the archive decrypts to nothing because a
# pipe swallowed an error, the key on the backup host is not the key the archive
# was made with, `--oplog` was silently ignored because the node is not in a
# replica set, or the restore succeeds and puts the data somewhere nobody looks.
# Every one of those looks exactly like a working backup until the day it is
# needed.
#
# So this does the whole cycle for real: write known data, back the instance up,
# <b>destroy the data</b>, restore it from the encrypted archive, and compare
# what came back against what went in.
#
# ── Why it is safe to run on a live machine ──────────────────────────────────
#
# The only database it destroys is one it created seconds earlier, named with a
# UUID. The backup covers the whole instance, as production's does — but the
# restore is scoped with --ns-include to that one throwaway namespace, so
# nothing else is touched even if the archive contains it.
#
# Usage:  scripts/restore-drill.sh
#
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
MONGO_URI="${VNI_BACKUP_MONGO_URI:-mongodb://localhost:27018/?directConnection=true}"

command -v gpg >/dev/null 2>&1 || { echo "drill: gpg is not on PATH." >&2; exit 2; }

# The drill does not check for mongodump/mongorestore itself: backup.sh and
# restore.sh own that check, and they accept VNI_MONGODUMP / VNI_MONGORESTORE
# overrides the drill must not second-guess. Checking here as well would refuse
# a setup those two would have handled.

# ── Talking to the database ──────────────────────────────────────────────────
#
# <b>An override rather than a hard dependency on a working host `mongosh`.</b>
# The drill needs a shell only to seed and to read back; the backup itself uses
# `mongodump`, which is a static binary and always works. On the machine this
# was written on, Homebrew's `mongosh` was broken by an unrelated node upgrade —
# and a drill that cannot run because of somebody's package manager is a drill
# that stops being run.
#
#   VNI_MONGOSH="docker exec -i vni-mongo mongosh" \
#   VNI_MONGOSH_URI="mongodb://localhost:27017/?directConnection=true" \
#   scripts/restore-drill.sh
#
# The two URIs are separate on purpose: `mongodump` runs on the host and reaches
# the container through the published port, while a `mongosh` running *inside*
# the container reaches it on the internal one. One variable for both would work
# in exactly one of the two setups.
read -r -a MONGOSH <<< "${VNI_MONGOSH:-mongosh}"
SHELL_URI="${VNI_MONGOSH_URI:-$MONGO_URI}"

"${MONGOSH[@]}" "$SHELL_URI" --quiet --eval 'db.adminCommand({ping:1}).ok' >/dev/null 2>&1 \
  || { echo "drill: ${MONGOSH[*]} cannot reach $SHELL_URI." >&2; exit 2; }

WORK="$(mktemp -d)"
DB="vni_restore_drill_$(uuidgen 2>/dev/null | tr -d - | tr 'A-Z' 'a-z' || date +%s)"

cleanup() {
  "${MONGOSH[@]}" "$SHELL_URI" --quiet --eval "db.getSiblingDB('$DB').dropDatabase()" >/dev/null 2>&1 || true
  rm -rf "$WORK"
}
trap cleanup EXIT

echo "drill: database $DB"

# A throwaway key, generated here. The drill must never need the production one
# — a rehearsal that requires the real secret is a rehearsal nobody runs.
KEY="$WORK/drill.key"
head -c 64 /dev/urandom | base64 > "$KEY"
chmod 600 "$KEY"

WRONG_KEY="$WORK/wrong.key"
head -c 64 /dev/urandom | base64 > "$WRONG_KEY"
chmod 600 "$WRONG_KEY"

export VNI_BACKUP_KEY_FILE="$KEY"
export VNI_BACKUP_MONGO_URI="$MONGO_URI"

# ── 1 · Data whose shape is worth checking, not just counting ────────────────
#
# Counting documents proves almost nothing: a restore that dropped every field
# but `_id` counts the same. These carry the types that actually get lost when a
# dump/restore path is wrong — a date that comes back as a string, binary that
# comes back base64-encoded, a decimal that comes back as a double and quietly
# changes a band.
"${MONGOSH[@]}" "$SHELL_URI" --quiet --eval "
  // Named 'target', not 'db'. A 'const db' in the same scope shadows mongosh's
  // own 'db' before it is assigned, and the error it produces names neither.
  const target = db.getSiblingDB('$DB');
  target.sittings.insertMany([
    { _id: 'a', band: NumberDecimal('7.5'), at: ISODate('2026-08-28T01:02:03Z'),
      audio: BinData(0, 'T2dnUwACAAAAAAAAAAA='), answers: { 'r-1': 'river depth', 'r-2': null } },
    { _id: 'b', band: NumberDecimal('6.0'), at: ISODate('2026-01-01T00:00:00Z'),
      audio: BinData(0, 'AAECAwQ='), answers: { 'l-3': 'A|C' } },
  ]);
  target.learners.insertOne({ _id: 'u1', email: 'hoc.vien@example.com', diacritics: 'Nguyễn Thị Hoà' });
" >/dev/null

# A fingerprint of the content, computed by the database itself. Comparing this
# before and after is what makes the drill about fidelity rather than about
# whether the collections exist.
fingerprint() {
  "${MONGOSH[@]}" "$SHELL_URI" --quiet --eval "
    const target = db.getSiblingDB('$DB');
    const out = [];
    for (const name of target.getCollectionNames().sort()) {
      // .toArray(): a mongosh cursor is not a synchronous iterable, and
      // for..of over one throws a TypeError that names neither the cursor nor
      // the collection.
      for (const doc of target.getCollection(name).find().sort({_id: 1}).toArray()) {
        out.push(name + ':' + EJSON.stringify(doc));
      }
    }
    print(out.join('\n'));
  "
}

BEFORE="$(fingerprint)"
[[ -n "$BEFORE" ]] || { echo "drill: seeded nothing. Is $SHELL_URI writable?" >&2; exit 1; }

# ── 2 · Back the instance up ────────────────────────────────────────────────
ARCHIVE="$("$ROOT/scripts/backup.sh" "$WORK")"
echo "drill: archive $(basename "$ARCHIVE") ($(wc -c < "$ARCHIVE" | tr -d ' ') bytes)"

# It must not be readable without the key. Checked, because "encrypted" is a
# claim about a file and this is the file.
if gpg --batch --quiet --decrypt --passphrase-file "$WRONG_KEY" "$ARCHIVE" >/dev/null 2>&1; then
  echo "drill: FAILED — the archive decrypted with a key that never encrypted it." >&2
  exit 1
fi
echo "drill: archive refuses the wrong key"

# ── 3 · Destroy the data ────────────────────────────────────────────────────
"${MONGOSH[@]}" "$SHELL_URI" --quiet --eval "db.getSiblingDB('$DB').dropDatabase()" >/dev/null

GONE="$(fingerprint)"
[[ -z "$GONE" ]] || { echo "drill: the drop did not drop. Nothing after this proves anything." >&2; exit 1; }
echo "drill: data destroyed"

# ── 4 · Restore, scoped to the throwaway namespace ──────────────────────────
"$ROOT/scripts/restore.sh" "$ARCHIVE" --ns-include "$DB.*"

AFTER="$(fingerprint)"

# ── 5 · Compare ─────────────────────────────────────────────────────────────
if [[ "$BEFORE" != "$AFTER" ]]; then
  echo "drill: FAILED — what came back is not what went in." >&2
  diff <(printf '%s\n' "$BEFORE") <(printf '%s\n' "$AFTER") >&2 || true
  exit 1
fi

echo "drill: restored $(printf '%s\n' "$AFTER" | wc -l | tr -d ' ') documents, byte-identical"
echo "drill: PASSED"
