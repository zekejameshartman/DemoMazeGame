#!/usr/bin/env bash
set -euo pipefail

if [[ -z "${BASH_VERSION:-}" ]]; then
  echo "Error: this script must be run with bash" >&2
  echo "Run: ./summarize_ai_sessions.sh" >&2
  exit 2
fi

# Summarize AI session logs (logs/sessions/*.json) into readable tables.
#
# This script:
# - Selects up to N most-recent sessions *per model* (newest first)
# - Aggregates results by (model + goal-coordinates setting)
# - Prints a text table suitable for pasting into reports

usage() {
  cat <<'EOF'
Usage:
  ./summarize_ai_sessions.sh [options]

Options:
  -n, --runs-per-model N   Max most-recent runs to include per model (default: 20)
  -d, --dir DIR            Sessions directory (default: auto-detect)
      --no-totals          Do not print the per-model totals table
      --tsv                Output TSV (no alignment) instead of a formatted table
  -h, --help               Show this help

Notes:
  - Only session logs are used; files ending in "_api.json" are ignored.
  - "Most recent" is based on the session log filename prefix (yyyy-MM-dd_HHmmss_fff).
  - Requires: jq
  - Optional: column (for pretty alignment)
EOF
}

RUNS_PER_MODEL=20
SESS_DIR="logs/sessions"
DIR_SET=0
PRINT_TOTALS=1
OUTPUT_TSV=0

while [[ $# -gt 0 ]]; do
  case "$1" in
    -n|--runs-per-model)
      RUNS_PER_MODEL="${2:-}"
      shift 2
      ;;
    -d|--dir)
      SESS_DIR="${2:-}"
      DIR_SET=1
      shift 2
      ;;
    --no-totals)
      PRINT_TOTALS=0
      shift
      ;;
    --tsv)
      OUTPUT_TSV=1
      shift
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      echo "Unknown arg: $1" >&2
      echo >&2
      usage >&2
      exit 2
      ;;
  esac
done

# Resolve default sessions directory relative to this script.
# This avoids surprises when running from arbitrary working directories.
SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]:-$0}")" && pwd -P)"

if (( DIR_SET == 0 )); then
  # Try common locations (repo layouts have varied over time).
  if [[ -d "$SCRIPT_DIR/logs/sessions" ]]; then
    SESS_DIR="$SCRIPT_DIR/logs/sessions"
  elif [[ -d "$SCRIPT_DIR/DempMazeGame/logs/sessions" ]]; then
    SESS_DIR="$SCRIPT_DIR/DempMazeGame/logs/sessions"
  else
    SESS_DIR="$SCRIPT_DIR/logs/sessions"
  fi
else
  # If a relative path was passed, first try CWD, then script-relative.
  if [[ ! -d "$SESS_DIR" && -d "$SCRIPT_DIR/$SESS_DIR" ]]; then
    SESS_DIR="$SCRIPT_DIR/$SESS_DIR"
  fi
fi

if ! command -v jq >/dev/null 2>&1; then
  echo "Error: jq is required (https://stedolan.github.io/jq/)" >&2
  exit 1
fi

if [[ ! "$RUNS_PER_MODEL" =~ ^[0-9]+$ ]] || [[ "$RUNS_PER_MODEL" -le 0 ]]; then
  echo "Error: --runs-per-model must be a positive integer" >&2
  exit 2
fi

shopt -s nullglob

if [[ ! -d "$SESS_DIR" ]]; then
  echo "No such directory: $SESS_DIR" >&2
  echo "Tip: pass --dir 'DempMazeGame/logs/sessions' (relative to repo root)" >&2
  exit 2
fi

session_files=()
for f in "$SESS_DIR"/*.json; do
  [[ "$f" == *_api.json ]] && continue
  session_files+=("$f")
done

if ((${#session_files[@]} == 0)); then
  echo "No session logs found in: $SESS_DIR" >&2
  echo "Expected files like: logs/sessions/2026-01-22_101530_123_abcd_model.json" >&2
  exit 0
fi

# Sort descending by filename (timestamp prefix sorts lexicographically)
mapfile -t sorted_files < <(printf '%s\n' "${session_files[@]}" | LC_ALL=C sort -r)

declare -A per_model_taken
selected_files=()

for f in "${sorted_files[@]}"; do
  # Skip unreadable/invalid files.
  if ! model_name=$(jq -r '.model.name // empty' "$f" 2>/dev/null); then
    echo "Warning: skipping invalid JSON: $f" >&2
    continue
  fi
  if [[ -z "$model_name" ]]; then
    model_name="UNKNOWN"
  fi

  taken=${per_model_taken["$model_name"]:-0}
  if (( taken < RUNS_PER_MODEL )); then
    selected_files+=("$f")
    per_model_taken["$model_name"]=$((taken + 1))
  fi
done

if ((${#selected_files[@]} == 0)); then
  echo "No valid session logs found in: $SESS_DIR" >&2
  exit 0
fi

# Emit one TSV line per selected session (one jq invocation per file keeps this robust
# even if the file list is large).
tmp_rows="$(mktemp)"
tmp_by_settings="$(mktemp)"
tmp_by_model="$(mktemp)"
trap 'rm -f "$tmp_rows" "$tmp_by_settings" "$tmp_by_model"' EXIT

for f in "${selected_files[@]}"; do
  jq -r '
    def yn($b): if ($b // false) then "Y" else "N" end;
    [
      (.model.name // "UNKNOWN"),
      yn(.settings.showGoalCoordinates),
      yn(.outcome.won),
      yn(.outcome.stoppedByUser),
      yn(.outcome.reachedMaxMoves),
      yn(.outcome.errorOccurred),
      ((.metrics.totalMoves // 0) | tostring),
      ((.metrics.wallCollisions // 0) | tostring),
      ((.tokenUsage.totalTokens // 0) | tostring),
      ((.cost.totalCostUsd // 0) | tostring)
    ] | @tsv
  ' "$f" >> "$tmp_rows" || {
    echo "Warning: skipping unreadable JSON: $f" >&2
  }
done

awk -F'\t' -v OFS='\t' '
  function b2i(v) { return (v=="Y" ? 1 : 0) }
  function f2(v) { return sprintf("%.2f", v) }
  function f3(v) { return sprintf("%.3f", v) }
  function f1(v) { return sprintf("%.1f", v) }
  function pct(a,b) { return (b>0 ? f1(100.0*a/b) : "0.0") }

  {
    model=$1
    goal=$2

    won=b2i($3)
    stopped=b2i($4)
    reachedMax=b2i($5)
    err=b2i($6)

    totalMoves=$7+0
    collisions=$8+0
    totalTok=$9+0
    cost=$10+0

    key=model OFS goal

    n[key]++
    wins[key]+=won
    stops[key]+=stopped
    maxes[key]+=reachedMax
    errs[key]+=err

    sumMoves[key]+=totalMoves
    sumColl[key]+=collisions
    sumTok[key]+=totalTok
    sumCost[key]+=cost

    mk=model
    mn[mk]++
    mwins[mk]+=won
    mstop[mk]+=stopped
    mmax[mk]+=reachedMax
    merr[mk]+=err
    msumMoves[mk]+=totalMoves
    msumColl[mk]+=collisions
    msumTok[mk]+=totalTok
    msumCost[mk]+=cost
  }

  END {
    # Detailed table header (keep it narrow to avoid wrapping)
    print "Model","Goal","Runs","Won","Lost","Win%","AvgMv","Coll/M","Tok/M","Cost$","$/Win","Stop","Max","Err"

    for (k in n) {
      split(k, a, OFS)
      model=a[1]
      goal=a[2]

      runs=n[k]
      won=wins[k]
      lost=runs-won

      avgMoves = (runs>0 ? sumMoves[k]/runs : 0)
      collPerMove = (sumMoves[k]>0 ? sumColl[k]/sumMoves[k] : 0)
      tokPerMove = (sumMoves[k]>0 ? sumTok[k]/sumMoves[k] : 0)

      print model,goal,
            runs,won,lost,pct(won,runs),
            f1(avgMoves),f3(collPerMove),f1(tokPerMove),
            f2(sumCost[k]),
            (won>0 ? f2(sumCost[k]/won) : "-"),
            stops[k],maxes[k],errs[k]
    }

    print "---MODEL_TOTALS---"
    print "Model","Runs","Won","Lost","Win%","AvgMv","Coll/M","Tok/M","Cost$","$/Win","Stop","Max","Err"

    for (m in mn) {
      runs=mn[m]
      won=mwins[m]
      lost=runs-won

      avgMoves = (runs>0 ? msumMoves[m]/runs : 0)
      collPerMove = (msumMoves[m]>0 ? msumColl[m]/msumMoves[m] : 0)
      tokPerMove = (msumMoves[m]>0 ? msumTok[m]/msumMoves[m] : 0)

      print m,
            runs,won,lost,pct(won,runs),
            f1(avgMoves),f3(collPerMove),f1(tokPerMove),
            f2(msumCost[m]),
            (won>0 ? f2(msumCost[m]/won) : "-"),
            mstop[m],mmax[m],merr[m]
    }
  }
' "$tmp_rows" | {
  in_totals=0
  while IFS= read -r line; do
    if [[ "$line" == "---MODEL_TOTALS---" ]]; then
      in_totals=1
      continue
    fi
    if (( in_totals == 0 )); then
      printf '%s\n' "$line" >> "$tmp_by_settings"
    else
      printf '%s\n' "$line" >> "$tmp_by_model"
    fi
  done
}

selected_total=${#selected_files[@]}
model_count=${#per_model_taken[@]}

echo "Using up to $RUNS_PER_MODEL most-recent sessions per model"
echo "Selected sessions: $selected_total across $model_count model(s) from $SESS_DIR"
echo

print_table() {
  local tsv_file="$1"
  shift
  local sort_args=("$@")

  # Keep header as the first line; sort only the rows.
  local sorted
  sorted="$(
    {
      IFS= read -r header || true
      if [[ -n "${header:-}" ]]; then
        printf '%s\n' "$header"
      fi
      if [[ ${#sort_args[@]} -gt 0 ]]; then
        LC_ALL=C sort "${sort_args[@]}"
      else
        cat
      fi
    } < "$tsv_file"
  )"

  if (( OUTPUT_TSV == 1 )); then
    printf '%s\n' "$sorted"
    return
  fi

  if command -v column >/dev/null 2>&1; then
    printf '%s\n' "$sorted" | column -t -s $'\t'
  else
    printf '%s\n' "$sorted"
  fi
}

echo "Detailed (model + goal coordinates setting)"
print_table "$tmp_by_settings" -t $'\t' -k1,1 -k2,2

if (( PRINT_TOTALS == 1 )); then
  echo
  echo "Model totals (across included settings)"
  print_table "$tmp_by_model" -t $'\t' -k1,1
fi
