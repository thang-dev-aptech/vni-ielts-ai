#!/usr/bin/env bash
# Parse every prepared Cambridge paper through Vni.Ielts.ExamImporter, then
# assemble each test into fixtures/exams/<book>-test-N.json.
#
# Idempotent: a paper whose package already validated (a `.ok` marker beside
# it) is skipped, so a batch that dies on quota or a hung stream is restarted
# with the same command and carries on from where it stopped. Nothing here
# publishes — the seeder picks fixtures up on the next API boot.
#
# Model per module is a runtime override, not a config edit: Reading output
# is long enough that deepseek-v4-pro truncated mid-JSON and then hung the
# SSE stream for hours (Cam 17 T3, 2026-09-03); flash finishes. Listening is
# short and stays on the configured model.
#
# usage: scripts/run-cambridge-batch.sh [book ...]     default: cam17 cam16 cam19 cam18 cam21
set -u

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
IMPORTER="$ROOT/backend/tools/Vni.Ielts.ExamImporter/bin/Release/net10.0/Vni.Ielts.ExamImporter.dll"
PREPARED="$ROOT/_workspace/cambridge/prepared"
PACKAGES="$ROOT/_workspace/cambridge/packages"
LOGS="$ROOT/_workspace/cambridge/batch"
READING_MODEL="${READING_MODEL:-gpt-5.4}"
# Listening too: deepseek-v4-pro opened the stream and then sat silent for
# the full 18-minute deadline on Cam 16 T4 and VOL 9 T5 (2026-09-04 02:19–02:29).
LISTENING_MODEL="${LISTENING_MODEL:-gpt-5.4}"

mkdir -p "$PACKAGES" "$LOGS"

if [ ! -f "$IMPORTER" ]; then
  echo "importer not built: dotnet build backend/tools/Vni.Ielts.ExamImporter -c Release" >&2
  exit 2
fi

BOOKS=("$@")
[ ${#BOOKS[@]} -eq 0 ] && BOOKS=(cam17 cam16 cam19 cam18 cam21)

book_number() { echo "${1#cam}"; }

# Books whose Listening cannot ship because no audio exists on disk.
has_audio() {
  case "$1" in
    cam20|cam21) return 1 ;;
    *) return 0 ;;
  esac
}

parse_one() {
  local book="$1" test="$2" module="$3"
  local slug="${book}-t${test}-${module}"
  local out="$PACKAGES/${slug}.json"
  local ok="$PACKAGES/${slug}.ok"
  local src="$PREPARED/$book/test-$test/paper-${module}.txt"
  local key="$PREPARED/$book/test-$test/key-${module}.txt"
  local title="Cambridge IELTS $(book_number "$book") — Test $test"

  if [ -f "$ok" ]; then
    echo "[skip] $slug already validated"
    return 0
  fi
  if [ ! -f "$src" ] || [ ! -f "$key" ]; then
    echo "[miss] $slug: paper or key text not prepared" | tee -a "$LOGS/$slug.log"
    return 1
  fi

  local model_env=()
  [ "$module" = "reading" ] && model_env=(Ai__OpenAi__Model="$READING_MODEL")
  [ "$module" = "listening" ] && model_env=(Ai__OpenAi__Model="$LISTENING_MODEL")

  echo "[run ] $slug ($(date +%H:%M:%S))"
  # ${arr[@]+"${arr[@]}"}: an empty array is "unbound" to bash 3.2's `set -u`.
  if env ${model_env[@]+"${model_env[@]}"} dotnet "$IMPORTER" \
      --source "$src" --key "$key" --out "$out" \
      --keep-module "$module" --title "$title" --rights-cleared \
      --sandbox "$PREPARED/$book/test-$test" \
      >"$LOGS/$slug.log" 2>&1; then
    touch "$ok"
    echo "[ ok ] $slug ($(date +%H:%M:%S)) $(grep '^section' "$LOGS/$slug.log" | tr '\n' ' ')"
    return 0
  fi

  echo "[FAIL] $slug ($(date +%H:%M:%S)) — see _workspace/cambridge/batch/$slug.log"
  tail -5 "$LOGS/$slug.log" | sed 's/^/       /'
  return 1
}

assemble_one() {
  local book="$1" test="$2"
  local reading="$PACKAGES/${book}-t${test}-reading.ok"
  local listening="$PACKAGES/${book}-t${test}-listening.ok"
  local args=(--book "$book" --test "$test" --reading "$PACKAGES/${book}-t${test}-reading.json")

  [ -f "$reading" ] || { echo "[hold] $book test $test: reading not ready, not assembled"; return 1; }
  if has_audio "$book"; then
    [ -f "$listening" ] || { echo "[hold] $book test $test: listening not ready, not assembled"; return 1; }
    args+=(--listening "$PACKAGES/${book}-t${test}-listening.json")
  fi

  if python3 "$ROOT/scripts/assemble-cambridge-fixture.py" "${args[@]}" >"$LOGS/${book}-t${test}-assemble.log" 2>&1; then
    echo "[fix ] $(grep '^wrote' "$LOGS/${book}-t${test}-assemble.log")"
  else
    echo "[WARN] $book test $test assembled with findings — see _workspace/cambridge/batch/${book}-t${test}-assemble.log"
    tail -4 "$LOGS/${book}-t${test}-assemble.log" | sed 's/^/       /'
  fi
}

for book in "${BOOKS[@]}"; do
  for test in 1 2 3 4; do
    [ -d "$PREPARED/$book/test-$test" ] || continue
    parse_one "$book" "$test" reading
    has_audio "$book" && parse_one "$book" "$test" listening
    assemble_one "$book" "$test"
  done
done

echo "batch finished $(date +%H:%M:%S)"
