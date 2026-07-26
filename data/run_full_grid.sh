#!/bin/bash
# Full grid: 19 problems x {noise0,noise1} x {GP,GPC} x 30 seeds
set -e
ROOT="/c/Users/Bogdan/source/repos/HeuristicLab"
EXPORTER="$ROOT/HeuristicLab.DataExporter/bin/Release/HeuristicLab.DataExporter.exe"
DATA_ROOT="$ROOT/data/draws"
RESULTS_ROOT="$ROOT/data/results"
SEEDS="${1:-30}"
PAR="${2:-16}"

mkdir -p "$DATA_ROOT" "$RESULTS_ROOT"

# DataExporter key -> filesystem-safe folder name
declare -A PROBLEMS=(
  [aircraft_lift]=aircraft_lift
  [flow_psi]=flow_psi
  [fuel_flow]=fuel_flow
  [jackson_2_11]=jackson_2_11
  [wave_power]=wave_power
  ["I.6.20"]=I_6_20
  ["I.9.18"]=I_9_18
  ["I.15.3x"]=I_15_3x
  ["I.15.3t"]=I_15_3t
  ["I.30.5"]=I_30_5
  ["I.32.17"]=I_32_17
  ["I.41.16"]=I_41_16
  ["I.48.20"]=I_48_20
  ["II.6.15a"]=II_6_15a
  ["II.11.27"]=II_11_27
  ["II.11.28"]=II_11_28
  ["II.35.21"]=II_35_21
  ["III.9.52"]=III_9_52
  ["III.10.19"]=III_10_19
)

for key in "${!PROBLEMS[@]}"; do
  folder="${PROBLEMS[$key]}"
  echo "=== $key ($folder) ==="
  "$EXPORTER" --problem "$key" --out "$DATA_ROOT/$folder" --seeds "$SEEDS"
  "$ROOT/data/run_grid.sh" "$key" "$DATA_ROOT/$folder" "$RESULTS_ROOT/${folder}_results.csv" "$SEEDS" "$PAR"
done

# Merge all per-problem results into one combined CSV. Build under a name that
# does NOT match the "*_results.csv" glob below (it would otherwise match its
# own in-progress output and self-append catastrophically), then rename.
COMBINED="$ROOT/data/results/full_results.csv"
TMP_COMBINED="$ROOT/data/results/.combining.csv"
first=1
> "$TMP_COMBINED"
for f in "$RESULTS_ROOT"/*_results.csv; do
  if [ "$first" = "1" ]; then
    cat "$f" >> "$TMP_COMBINED"
    first=0
  else
    tail -n +2 "$f" >> "$TMP_COMBINED"
  fi
done
mv "$TMP_COMBINED" "$COMBINED"

echo "Full grid complete: $COMBINED ($(($(wc -l < "$COMBINED") - 1)) total rows)"
