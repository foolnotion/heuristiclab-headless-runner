# HeuristicLab headless runner

A headless GP/GPC harness for `heal-research/HeuristicLab`, built for
reproducing/comparing against [operon](https://github.com/heal-research/operon)
on the Feynman-comparison experiment (see `foolnotion/operon-publications`,
`experiments/feynman-comparison/`).

Extracted out of a local `heal-research/HeuristicLab` checkout (where it
started life as a handful of commits on top of upstream `main`) into its
own private repo, since it doesn't belong in the upstream HeuristicLab
project.

## Layout

- `HeuristicLab.HeadlessRunner/` — runs a single GP/GPC training job
  (`--train`/`--test` CSVs in, result CSV rows out), used for the bulk of
  the comparison runs.
- `HeuristicLab.DataExporter/` — exports HeuristicLab's own bundled
  problem-instance generators (Feynman/Aircraft-lift/etc.) to CSV.
- `data/` — driver scripts (`run_grid.sh`, `run_full_grid.sh`,
  `run_pilot.sh`, `gen_draws.py`, `summarize_results.py`) that wire the
  above two into a parallel grid run over problems x noise x variant x
  seeds.

## Building

Drop `HeuristicLab.HeadlessRunner/` and `HeuristicLab.DataExporter/` in
as sibling directories of a built `heal-research/HeuristicLab` checkout
(both `.csproj` files reference sibling projects' compiled DLLs via
`..\bin\*.dll` `HintPath`s, i.e. they expect to sit next to that
checkout's own `HeuristicLab.HeadlessRunner`/`HeuristicLab.DataExporter`
slots and its `bin\Release\` output), then build with MSBuild:

```
MSBuild.exe HeuristicLab.HeadlessRunner/HeadlessRunner.csproj -p:Configuration=Release
```

## Runtime env overrides (HeuristicLab.HeadlessRunner)

- `HL_POPSIZE` — population size (default 1000)
- `HL_GENS` — max generations (default 20 for GPC, 200 for GP)
- `HL_MUTATION_PROB` — mutation probability (default 0.15; set to `0`
  for a crossover-only ablation)
- `HL_SELECTOR=random` — swap the default `TournamentSelector(GroupSize=5)`
  for `RandomSelector` (uniform, fitness-independent parent choice), for
  a selection-pressure ablation
- `HL_INTERPRETER=default` — use the managed linear tree interpreter
  instead of the native one
- `HL_DEBUG=1` — verbose console diagnostics
