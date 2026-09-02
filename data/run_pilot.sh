#!/usr/bin/env bash
# Public pilot: aircraft_lift x {noise0,noise1} x {GP,GPC} x 30 seeds.
set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
RUNNER_ROOT="$(cd "${HERE}/.." && pwd)"
EXE="${HL_RUNNER_EXE:-${RUNNER_ROOT}/HeuristicLab.HeadlessRunner/bin/Release/HeuristicLab.HeadlessRunner.exe}"
DATA="${1:-${RUNNER_ROOT}/data/aircraft_lift}"
OUT="${2:-${RUNNER_ROOT}/data/pilot_results.csv}"
SEEDS="${SEEDS:-30}"

if [ ! -f "${EXE}" ]; then
  echo "HeuristicLab runner executable not found: ${EXE}" >&2
  echo "Build it with scripts/build-linux-mono.sh or set HL_RUNNER_EXE." >&2
  exit 1
fi
if [ ! -f "${DATA}/seed1_noise0_train.csv" ]; then
  python3 "${HERE}/gen_draws.py" aircraft_lift "${DATA}" --seeds "${SEEDS}"
fi

rm -f "$OUT"
for noise in 0 1; do
  for variant in GP GPC; do
    for seed in $(seq 1 "$SEEDS"); do
      mono "$EXE" \
        --train "$DATA/seed${seed}_noise${noise}_train.csv" \
        --test "$DATA/seed${seed}_noise${noise}_test.csv" \
        --target __target__ \
        --variant "$variant" \
        --seed "$seed" \
        --output "$OUT" \
        --problem aircraft_lift \
        --noise "$noise"
    done
  done
done

echo "Pilot run complete: $OUT"
