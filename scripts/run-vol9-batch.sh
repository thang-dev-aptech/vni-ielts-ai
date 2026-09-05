#!/usr/bin/env bash
# Parse VOL 9 Tests 2–8 (Reading + Listening .docx, keys in their KEY folders)
# through Vni.Ielts.ExamImporter and assemble fixtures/exams/vol9-test-N.json.
#
# Same shape as run-cambridge-batch.sh: resumable via `.ok` markers, nothing
# published, the seeder loads fixtures on the next API boot. Test 1 already
# ships from the earlier hand-built package and is not touched.
#
# usage: scripts/run-vol9-batch.sh [test ...]     default: 2 3 4 5 6 7 8
set -u

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
IMPORTER="$ROOT/backend/tools/Vni.Ielts.ExamImporter/bin/Release/net10.0/Vni.Ielts.ExamImporter.dll"
VOL9="$ROOT/Đề IELTS/Đề CAM/Đề thi thật (Chỉ L và R) VOL 9 - REAL IELTS-20260819T082203Z-1-001/VOL 9 - REAL IELTS"
PACKAGES="$ROOT/_workspace/vol9/packages"
LOGS="$ROOT/_workspace/vol9/batch"
READING_MODEL="${READING_MODEL:-gpt-5.4}"
LISTENING_MODEL="${LISTENING_MODEL:-gpt-5.4}"

mkdir -p "$PACKAGES" "$LOGS"
[ -f "$IMPORTER" ] || { echo "importer not built: dotnet build backend/tools/Vni.Ielts.ExamImporter -c Release" >&2; exit 2; }

TESTS=("$@")
[ ${#TESTS[@]} -eq 0 ] && TESTS=(2 3 4 5 6 7 8)

# The source files are named by hand and not quite uniformly ("TEST 1 -R",
# "KET TEST 2-R"), so resolve each by glob rather than by formula.
find_one() {
  local dir="$1" pattern="$2"
  local hit
  hit=$(find "$dir" -maxdepth 1 -iname "$pattern" | head -n 1)
  [ -n "$hit" ] && echo "$hit"
}

parse_one() {
  local test="$1" module="$2"
  local slug="vol9-t${test}-${module}"
  local out="$PACKAGES/${slug}.json" ok="$PACKAGES/${slug}.ok"
  local letter dir src key
  case "$module" in
    reading)   letter=R; dir="$VOL9/READING";   key=$(find_one "$dir/KEY - EXPLAINATION" "KE*TEST ${test}*-R.docx") ;;
    listening) letter=L; dir="$VOL9/LISTENING"; key=$(find_one "$dir/KEY - TRANSCRIPT"   "KEY TEST ${test}*-L.docx") ;;
  esac
  src=$(find_one "$dir" "TEST ${test}*-${letter}.docx")

  if [ -f "$ok" ]; then echo "[skip] $slug already validated"; return 0; fi
  if [ -z "$src" ] || [ -z "$key" ]; then
    echo "[miss] $slug: source or key not found (src='${src:-}', key='${key:-}')" | tee -a "$LOGS/$slug.log"
    return 1
  fi

  local model_env=()
  [ "$module" = "reading" ] && model_env=(Ai__OpenAi__Model="$READING_MODEL")
  [ "$module" = "listening" ] && model_env=(Ai__OpenAi__Model="$LISTENING_MODEL")

  echo "[run ] $slug ($(date +%H:%M:%S))"
  if env ${model_env[@]+"${model_env[@]}"} dotnet "$IMPORTER" \
      --source "$src" --key "$key" --out "$out" \
      --keep-module "$module" --title "VOL 9 — Test $test (Reading + Listening)" --rights-cleared \
      --sandbox "$dir" \
      >"$LOGS/$slug.log" 2>&1; then
    touch "$ok"
    echo "[ ok ] $slug ($(date +%H:%M:%S)) $(grep '^section' "$LOGS/$slug.log" | tr '\n' ' ')"
    return 0
  fi
  echo "[FAIL] $slug ($(date +%H:%M:%S)) — see _workspace/vol9/batch/$slug.log"
  grep -vE "^info: System.Net" "$LOGS/$slug.log" | tail -4 | sed 's/^/       /' | cut -c1-200
  return 1
}

assemble_one() {
  local test="$1"
  [ -f "$PACKAGES/vol9-t${test}-reading.ok" ] && [ -f "$PACKAGES/vol9-t${test}-listening.ok" ] \
    || { echo "[hold] vol9 test $test: not both modules ready"; return 1; }
  if python3 "$ROOT/scripts/assemble-vol9-fixture.py" --test "$test" \
      --reading "$PACKAGES/vol9-t${test}-reading.json" \
      --listening "$PACKAGES/vol9-t${test}-listening.json" >"$LOGS/vol9-t${test}-assemble.log" 2>&1; then
    echo "[fix ] $(grep '^wrote' "$LOGS/vol9-t${test}-assemble.log")"
  else
    echo "[WARN] vol9 test $test assembled with findings — see _workspace/vol9/batch/vol9-t${test}-assemble.log"
    tail -4 "$LOGS/vol9-t${test}-assemble.log" | sed 's/^/       /'
  fi
}

for test in "${TESTS[@]}"; do
  parse_one "$test" reading
  parse_one "$test" listening
  assemble_one "$test"
done
echo "batch finished $(date +%H:%M:%S)"
