#!/bin/bash
# Pilot run: aircraft_lift x {noise0,noise1} x {GP,GPC} x 30 seeds
set -e
EXE="/c/Users/Bogdan/source/repos/HeuristicLab/HeuristicLab.HeadlessRunner/bin/Release/HeuristicLab.HeadlessRunner.exe"
DATA="/c/Users/Bogdan/source/repos/HeuristicLab/data/aircraft_lift"
OUT="/c/Users/Bogdan/source/repos/HeuristicLab/data/pilot_results.csv"

rm -f "$OUT"

for noise in 0 1; do
  for variant in GP GPC; do
    for seed in $(seq 1 30); do
      "$EXE" \
        --train "$DATA/seed${seed}_noise${noise}_train.csv" \
        --test "$DATA/seed${seed}_noise${noise}_test.csv" \
        --target y \
        --variant $variant \
        --seed $seed \
        --output "$OUT" \
        --problem aircraft_lift \
        --noise $noise
    done
  done
done

echo "Pilot run complete: $OUT"
