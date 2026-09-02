# HeuristicLab headless runner

A command-line harness for running GP and GPC symbolic-regression experiments with [HeuristicLab](https://github.com/heal-research/HeuristicLab). It reads training and test CSV files and writes machine-readable per-run result rows.

## Scope

The repository contains the runner source, a Nix development shell, build scripts, input-generation scripts, and optional instrumentation patches. It does not contain build outputs, credentials, third-party private data, unpublished results, or paper-author source code.

The bundled public benchmark utilities generate synthetic Feynman-style regression data. A reproduction record should pin:

- this repository revision;
- the upstream HeuristicLab revision;
- the input-generation command and parameters;
- the runner command and environment;
- a checksummed machine-readable result artifact; and
- the metric, aggregation, and exact result scope being tested.

## Layout

- `HeuristicLab.HeadlessRunner/`: executes one GP or GPC training job.
- `HeuristicLab.DataExporter/`: exports selected bundled HeuristicLab problem instances to CSV.
- `data/`: public input-generation, grid-run, pilot-run, and result-summary scripts.
- `patches/`: optional instrumentation patches for a local HeuristicLab checkout.
- `scripts/build-linux-mono.sh`: builds the required HeuristicLab dependencies and both executables under Mono.

## Build on Linux

Clone HeuristicLab beside this repository, then build in the supplied Nix shell:

```text
git clone https://github.com/heal-research/HeuristicLab.git ../HeuristicLab
nix develop --command scripts/build-linux-mono.sh ../HeuristicLab
```

The script builds the dependency subset used by the runner and produces:

```text
HeuristicLab.HeadlessRunner/bin/Release/HeuristicLab.HeadlessRunner.exe
HeuristicLab.DataExporter/bin/Release/HeuristicLab.DataExporter.exe
```

The build uses Mono's managed interpreter. On Linux, run the executable through `mono` and set `HL_INTERPRETER=default`.

## Run one experiment

```text
HL_INTERPRETER=default mono HeuristicLab.HeadlessRunner/bin/Release/HeuristicLab.HeadlessRunner.exe \
  --train train.csv \
  --test test.csv \
  --target y \
  --variant GP \
  --seed 1 \
  --output result.csv \
  --problem example \
  --noise 0
```

`--variant` accepts `GP` or `GPC`. The output CSV includes the problem label, noise label, variant, seed, train and test NMSE percentages, executed generations, and elapsed time.

## Public synthetic-data workflow

Generate 30 deterministic train/test draws for the bundled `aircraft_lift` specification:

```text
python3 data/gen_draws.py aircraft_lift data/aircraft_lift --seeds 30
```

Run a pilot over clean and noisy data, both variants, and all seeds:

```text
HL_INTERPRETER=default data/run_pilot.sh data/aircraft_lift data/pilot_results.csv
python3 data/summarize_results.py data/pilot_results.csv data/pilot_summary.csv
```

Run the same grid for another generated data directory:

```text
HL_INTERPRETER=default data/run_grid.sh aircraft_lift data/aircraft_lift data/grid_results.csv 30 16
python3 data/summarize_results.py data/grid_results.csv data/grid_summary.csv
```

Set `HL_RUNNER_EXE` to use an executable outside the default build path.

## Shape-constrained GP

The runner supports HeuristicLab's constrained single-objective GP evaluator with a JSON configuration file:

```text
--shape-constraints-config constraints.json
```

The configuration defines input domains and output or derivative constraints. The runner uses `LinearScalingGrammar` when this evaluator is enabled. Add `--shape-soft-constraints` to use soft scoring; otherwise the evaluator applies its hard gate. `--shape-penalty-factor <value>` sets the soft-mode penalty multiplier.

Optional diagnostics:

- `--shape-dynamics-output <csv>` writes generation-end feasibility summaries.
- `--constraint-diagnostics-output <csv>` writes per-tree constraint diagnostics. Use it for setup validation rather than bulk runs.

## Runtime configuration

Environment variables configure common experiment settings:

- `HL_POPSIZE`: population size; default `1000`.
- `HL_GENS`: maximum generations; default `200` for GP and `20` for GPC.
- `HL_MUTATION_PROB`: mutation probability; default `0.15`.
- `HL_MAXLENGTH`, `HL_MAXDEPTH`: tree-size limits; defaults `50`, `20`.
- `HL_ELITES`: number of preserved elites; default `1`.
- `HL_SELECTOR=random`: use random rather than tournament selection.
- `HL_INTERPRETER=default`: use the managed interpreter on Linux.
- `HL_DEBUG=1`: enable diagnostic logging.

## Optional instrumentation

The patches under `patches/` add local logging hooks to a HeuristicLab checkout for crossover and mutation diagnostics. They are not required for normal runs.

Apply and later revert them explicitly:

```text
patches/apply-instrumentation.sh ../HeuristicLab
patches/revert-instrumentation.sh ../HeuristicLab
```

Do not publish results obtained with local patches without recording the exact patch revision and explaining what it changes.
