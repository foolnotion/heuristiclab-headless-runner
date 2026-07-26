#!/bin/bash
# Parallel grid runner: <problem> x {noise0,noise1} x {GP,GPC} x <seeds>
# Usage: run_grid.sh <problem> <data_dir> <output_csv> [seeds] [parallelism]
set -e
PROBLEM="$1"
DATA="$2"
OUT="$3"
SEEDS="${4:-30}"
PAR="${5:-16}"

EXE="/c/Users/Bogdan/source/repos/HeuristicLab/HeuristicLab.HeadlessRunner/bin/Release/HeuristicLab.HeadlessRunner.exe"
TMPDIR=$(mktemp -d)

jobs_file="$TMPDIR/jobs.txt"
> "$jobs_file"
for noise in 0 1; do
  for variant in GP GPC; do
    for seed in $(seq 1 "$SEEDS"); do
      echo "$noise $variant $seed" >> "$jobs_file"
    done
  done
done

run_one() {
  noise="$1"; variant="$2"; seed="$3"
  jobout="$TMPDIR/out_${PROBLEM}_${noise}_${variant}_${seed}.csv"
  "$EXE" \
    --train "$DATA/seed${seed}_noise${noise}_train.csv" \
    --test "$DATA/seed${seed}_noise${noise}_test.csv" \
    --target y \
    --variant "$variant" \
    --seed "$seed" \
    --output "$jobout" \
    --problem "$PROBLEM" \
    --noise "$noise" >/dev/null
}
export -f run_one
export EXE TMPDIR PROBLEM DATA

cat "$jobs_file" | xargs -P "$PAR" -L 1 bash -c 'run_one "$@"' _

# Merge per-job outputs (each has its own header line) into one combined CSV.
first=1
> "$OUT"
for f in "$TMPDIR"/out_*.csv; do
  if [ "$first" = "1" ]; then
    cat "$f" >> "$OUT"
    first=0
  else
    tail -n +2 "$f" >> "$OUT"
  fi
done

rm -rf "$TMPDIR"
echo "Grid run complete: $OUT ($(($(wc -l < "$OUT") - 1)) rows)"
