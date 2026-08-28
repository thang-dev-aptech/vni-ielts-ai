#!/usr/bin/env bash
#
# F3.4 — the point-in-time restore drill.
#
# ── What this proves that scripts/restore-drill.sh does not ─────────────────
#
# `restore-drill.sh` drills the `backup.sh` path: dump, destroy, restore,
# compare. It restores into the SAME instance, scoped by namespace.
#
# This drills the PBM path, and the difference is the whole point of F3.4:
#
#   1. it restores to an ARBITRARY INSTANT, not to whenever a dump happened —
#      which is the only way to answer "recover to just before the incident";
#   2. it restores into a SEPARATE, ISOLATED INSTANCE and never writes to the
#      source, because PBM restores at instance granularity and a drill that
#      overwrote production to prove production is recoverable would be the
#      most expensive possible way to be wrong;
#   3. it compares document count, a content checksum, AND the point-in-time
#      invariant itself — that writes before the target survived and writes
#      after it did not. A count alone passes even when the restore landed on
#      the wrong instant, which is exactly the failure worth catching.
#
# It also measures RTO, because an untimed drill proves the mechanism works
# and says nothing about whether it works fast enough.
#
# ── Why it is safe to run on a live machine ─────────────────────────────────
#
# It writes only to a UUID-named throwaway database on the source, and reads
# everything else. The restore happens in a container this script creates and
# removes. The source instance is never restored into, never dropped, and is
# verified untouched at the end.
#
# Usage:
#   docker compose -f infra/docker/compose.yaml up -d
#   scripts/pbm-setup.sh
#   scripts/pitr-drill.sh
#
set -euo pipefail

export MSYS_NO_PATHCONV=1
export MSYS2_ARG_CONV_EXCL='*'

SOURCE_MONGO="${VNI_DRILL_SOURCE:-vni-mongo}"
SOURCE_PBM="${VNI_PBM_CONTAINER:-vni-pbm}"
NETWORK="${VNI_DRILL_NETWORK:-vni-ielts_default}"
URI="${VNI_PBM_URI:-mongodb://localhost:27017/?replicaSet=rs0}"
RTO_BUDGET_SECONDS="${VNI_DRILL_RTO_BUDGET:-3600}"

DB="vni_pitr_drill_$(date +%s)"
TARGET_MONGO="vni-mongo-pitrdrill"
TARGET_PBM="vni-pbm-pitrdrill"

cleanup() {
  local code=$?
  echo
  echo "pitr-drill: tearing down the isolated target..."
  docker rm -f "$TARGET_MONGO" "$TARGET_PBM" >/dev/null 2>&1 || true
  # The throwaway database on the SOURCE, and only that.
  docker exec "$SOURCE_MONGO" mongosh --quiet --eval \
    "db.getSiblingDB('$DB').dropDatabase()" >/dev/null 2>&1 || true
  exit "$code"
}
trap cleanup EXIT

src() { docker exec "$SOURCE_MONGO" mongosh --quiet --eval "$1"; }
pbm_src() { docker exec -e PBM_MONGODB_URI="$URI" "$SOURCE_PBM" pbm "$@"; }
pbm_tgt() { docker exec -e PBM_MONGODB_URI="$URI" "$TARGET_PBM" pbm "$@"; }

for c in "$SOURCE_MONGO" "$SOURCE_PBM"; do
  docker inspect "$c" >/dev/null 2>&1 || {
    echo "pitr-drill: no container '$c'. Start the stack and run scripts/pbm-setup.sh." >&2
    exit 2
  }
done

pbm_src status 2>/dev/null | grep -q 'Status \[ON\]' || {
  echo "pitr-drill: PITR is not ON. Run scripts/pbm-setup.sh first." >&2
  exit 2
}

# ── 1 · A base backup, so PITR has something to replay onto ──────────────────
echo "pitr-drill: taking a base backup..."
pbm_src backup --wait >/dev/null

# ── 2 · Known data BEFORE the target instant ────────────────────────────────
echo "pitr-drill: writing 500 documents that must survive..."
src "
  const d = db.getSiblingDB('$DB');
  const docs = [];
  for (let i = 0; i < 500; i++) docs.push({ _id: i, era: 'before', payload: 'row-' + i });
  d.rows.insertMany(docs);
" >/dev/null

BEFORE_COUNT=$(src "db.getSiblingDB('$DB').rows.countDocuments({})" | tr -d '\r\n ')
# A content checksum, not just a count: a restore that produced the right
# NUMBER of wrong documents passes a count check.
BEFORE_SUM=$(src "
  const d = db.getSiblingDB('$DB');
  const all = d.rows.find({}, {_id:1, payload:1}).sort({_id:1}).toArray();
  print(all.map(r => r._id + ':' + r.payload).join('|').length + '/' +
        all.reduce((a, r) => (a + r._id) % 1000000007, 7));
" | tr -d '\r\n ')

echo "pitr-drill:   count=$BEFORE_COUNT checksum=$BEFORE_SUM"

# ── 3 · The target instant ──────────────────────────────────────────────────
sleep 2
TARGET_TIME=$(docker exec "$SOURCE_MONGO" date -u +%Y-%m-%dT%H:%M:%S | tr -d '\r\n')
echo "pitr-drill: target instant T = $TARGET_TIME"
sleep 2

# ── 4 · Data AFTER the target instant, which must NOT come back ─────────────
echo "pitr-drill: writing 500 documents that must NOT survive..."
src "
  const d = db.getSiblingDB('$DB');
  const docs = [];
  for (let i = 1000; i < 1500; i++) docs.push({ _id: i, era: 'after', payload: 'row-' + i });
  d.rows.insertMany(docs);
" >/dev/null

# ── 5 · Wait for the oplog slice covering T to reach storage ────────────────
# PITR uploads a slice every oplogSpanMin; a restore to T is impossible until
# the slice containing T has landed.
echo "pitr-drill: waiting for the oplog slice covering T..."
for _ in $(seq 1 40); do
  END="$(pbm_src status 2>/dev/null | grep -oE '[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}' | tail -1 || true)"
  if [ -n "$END" ] && [ "$END" \> "$TARGET_TIME" ]; then break; fi
  sleep 5
done
[ -n "${END:-}" ] && [ "$END" \> "$TARGET_TIME" ] || {
  echo "pitr-drill: PITR coverage never reached T ($TARGET_TIME); last end=${END:-none}." >&2
  exit 1
}
echo "pitr-drill:   coverage reaches $END"

# ── 6 · An isolated target instance. The source is never restored into. ─────
echo "pitr-drill: starting an isolated restore target..."
docker rm -f "$TARGET_MONGO" "$TARGET_PBM" >/dev/null 2>&1 || true
docker run -d --name "$TARGET_MONGO" --network "$NETWORK" \
  mongo:7 --replSet rs0 --bind_ip_all --port 27017 >/dev/null
sleep 8
docker exec "$TARGET_MONGO" mongosh --quiet --eval \
  'rs.initiate({_id:"rs0",members:[{_id:0,host:"localhost:27017"}]})' >/dev/null
for _ in $(seq 1 20); do
  [ "$(docker exec "$TARGET_MONGO" mongosh --quiet --eval 'db.hello().isWritablePrimary' | tr -d '\r\n ')" = "true" ] && break
  sleep 2
done

# The target's own agent, PITR OFF so it never writes into the SOURCE's
# oplog chain in shared storage.
docker run -d --name "$TARGET_PBM" --network "container:$TARGET_MONGO" \
  -e PBM_MONGODB_URI="$URI" \
  percona/percona-backup-mongodb:2.12.0 pbm-agent >/dev/null
sleep 10

docker exec -i "$TARGET_PBM" sh -c 'cat > "$HOME/c.yaml"' <<EOF
storage:
  type: s3
  s3:
    endpointUrl: ${VNI_PBM_ENDPOINT:-http://vni-minio:9000}
    bucket: ${VNI_PBM_BUCKET:-vni-backups}
    prefix: pbm
    region: us-east-1
    forcePathStyle: true
    credentials:
      access-key-id: ${VNI_PBM_ACCESS_KEY:-vni-local}
      secret-access-key: ${VNI_PBM_SECRET_KEY:-vni-local-dev-only}
pitr:
  enabled: false
EOF
docker exec -e PBM_MONGODB_URI="$URI" "$TARGET_PBM" \
  sh -c 'pbm config --file "$HOME/c.yaml"' >/dev/null
sleep 6

# ── 7 · Restore to T, timed ─────────────────────────────────────────────────
echo "pitr-drill: restoring to $TARGET_TIME ..."
STARTED=$(date +%s)
pbm_tgt restore --time="$TARGET_TIME" --wait >/dev/null
FINISHED=$(date +%s)
RTO=$((FINISHED - STARTED))
echo "pitr-drill:   restore completed in ${RTO}s"

# ── 8 · Compare ─────────────────────────────────────────────────────────────
tgt() { docker exec "$TARGET_MONGO" mongosh --quiet --eval "$1"; }

AFTER_COUNT=$(tgt "db.getSiblingDB('$DB').rows.countDocuments({})" | tr -d '\r\n ')
AFTER_SUM=$(tgt "
  const d = db.getSiblingDB('$DB');
  const all = d.rows.find({}, {_id:1, payload:1}).sort({_id:1}).toArray();
  print(all.map(r => r._id + ':' + r.payload).join('|').length + '/' +
        all.reduce((a, r) => (a + r._id) % 1000000007, 7));
" | tr -d '\r\n ')
LEAKED=$(tgt "db.getSiblingDB('$DB').rows.countDocuments({era:'after'})" | tr -d '\r\n ')

FAIL=0

echo
echo "pitr-drill: ── comparison ──"
printf 'pitr-drill:   count     source-at-T=%s  restored=%s\n' "$BEFORE_COUNT" "$AFTER_COUNT"
printf 'pitr-drill:   checksum  source-at-T=%s  restored=%s\n' "$BEFORE_SUM" "$AFTER_SUM"
printf 'pitr-drill:   post-T documents present in restore: %s (must be 0)\n' "$LEAKED"

[ "$AFTER_COUNT" = "$BEFORE_COUNT" ] || { echo "pitr-drill: FAIL — document count differs." >&2; FAIL=1; }
[ "$AFTER_SUM" = "$BEFORE_SUM" ] || { echo "pitr-drill: FAIL — checksum differs; the right NUMBER of wrong documents came back." >&2; FAIL=1; }
[ "$LEAKED" = "0" ] || { echo "pitr-drill: FAIL — writes made AFTER T came back; the restore landed on the wrong instant." >&2; FAIL=1; }

# The source must be exactly as it was: still holding both eras.
SOURCE_TOTAL=$(src "db.getSiblingDB('$DB').rows.countDocuments({})" | tr -d '\r\n ')
[ "$SOURCE_TOTAL" = "1000" ] || { echo "pitr-drill: FAIL — the SOURCE changed (expected 1000, found $SOURCE_TOTAL)." >&2; FAIL=1; }
printf 'pitr-drill:   source untouched: %s documents still present (both eras)\n' "$SOURCE_TOTAL"

if [ "$RTO" -gt "$RTO_BUDGET_SECONDS" ]; then
  echo "pitr-drill: FAIL — RTO ${RTO}s exceeds the ${RTO_BUDGET_SECONDS}s budget." >&2
  FAIL=1
fi

echo
if [ "$FAIL" -ne 0 ]; then
  echo "pitr-drill: DRILL FAILED." >&2
  exit 1
fi

echo "pitr-drill: OK — restored to an isolated instance at T."
echo "pitr-drill:   RTO ${RTO}s of a ${RTO_BUDGET_SECONDS}s budget."
echo "pitr-drill:   count and checksum match; no post-T write came back; source untouched."
