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

This repo's `.csproj` files reference sibling HeuristicLab projects'
compiled DLLs via `..\bin\*.dll` `HintPath`s (unchanged from when this
lived inside a `heal-research/HeuristicLab` checkout, where `..\bin`
was that checkout's own build output directory). To build standalone:

1. Build a `heal-research/HeuristicLab` checkout so its `bin/` directory
   is populated.
2. Make this repo's `bin/` a directory junction/symlink pointing at that
   checkout's `bin/` — e.g. on Windows:
   `New-Item -ItemType Junction -Path bin -Target <path-to-HeuristicLab>\bin`
3. Each project's `Properties/AssemblyInfo.cs` isn't tracked (gitignored,
   like the `bin`/`obj` output dirs) — create a minimal one per project
   if missing (`AssemblyTitle`/`AssemblyProduct`/a `Guid` attribute is
   enough; MSBuild only needs the file to exist and compile).
4. Build with MSBuild:

```
MSBuild.exe HeuristicLab.HeadlessRunner/HeadlessRunner.csproj -p:Configuration=Release
```

## GPC evaluator

GPC runs use `SymbolicRegressionParameterOptimizationEvaluator` (ALGLIB
lsfit + AutoDiff Levenberg-Marquardt, `HeuristicLab.Problems.DataAnalysis.Symbolic.Regression`),
**not** the newer `ParameterOptimizationEvaluator` (native-interpreter
LM). This matches the paper's actual saved `.hl` files — confirmed via
`SymbolicRegressionParameterOptimizationEvaluator`'s `AfterDeserialization`
backward-compat shim, which renames a legacy `ConstantOptimizationIterations`
parameter to today's `ParameterOptimizationIterations`, matching the
`.hl` dump's actual saved parameter names exactly (also
`Count Function and Gradient Evaluations`, same capitalization) — the
class is marked `[Obsolete("Use ParameterOptimizationEvaluator instead")]`
in current HL, i.e. it's the file's *older* evaluator, later superseded
by the native one; the `.hl` files predate that switch. Its Quality
metric is Pearson R² (maximized), not raw MSE (minimized) like the GP
variant's evaluator — `--gen-stats-output`'s `fitness_*` columns convert
accordingly (`NMSE% = (1 - R²) * 100`, exact given `ApplyLinearScaling=true`).
Noticeably slower than the native evaluator (~40-50s per GPC run at
`PopulationSize=1000`/20 generations vs. ~10-15s) since it's a managed
ALGLIB+AutoDiff implementation rather than a native C++ one — factor
this into batch-run time estimates.

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
- `--crossover-noop-output <csv>` — export one row per `SubtreeCrossover.Cross()`
  call (`problem, noise, variant, seed, generation, parent0_length,
  is_noop`), for measuring how often crossover silently returns parent0
  unchanged (no donor branch fit the size budget). **Requires the
  instrumentation patch** — see `patches/` below.
- `--crossover-kernel-output <csv>` — export one row per
  `SubtreeCrossover.Cross()` call (`problem, noise, variant, seed,
  generation, parent_length, removed_length`), for building an empirical
  crossover node-selection kernel (excision-side only — parent0's total
  length and the excised subtree's length; the donor side is filtered by
  a per-event budget and isn't the same distribution, see the
  operon-publications README for why). **Requires the instrumentation
  patch** — see `patches/` below.
- `HL_EVAL_FREE=1` — swap in `PlaceholderEvaluator` (see
  `PlaceholderEvaluator.cs`) instead of the real GP/GPC evaluator: skips
  the LM constant-optimization / real fitness computation entirely,
  assigning cheap uniform-random Quality instead, so `Elites=1` can still
  break ties without a stable-sort artifact. Isolates crossover/
  reinsertion structural dynamics from fitness-driven dynamics at a
  fraction of the per-generation cost (~15-20x faster than the real GPC
  evaluator) — useful for many-seed equilibrium-variance studies where
  the real LM cost makes that many full runs impractical. `fitness_*`
  columns in `--gen-stats-output` are meaningless noise in this mode;
  only the `length_*` columns are informative. No HL source patch
  needed — this one's entirely in `PlaceholderEvaluator.cs`.

### Instrumentation patch (`SubtreeCrossover.NoOpLog` / `.KernelLog`)

`--crossover-noop-output` and `--crossover-kernel-output` need two
fields on `SubtreeCrossover` (`public static List<Tuple<int,bool>>
NoOpLog` and `public static List<Tuple<int,int>> KernelLog`, both null
by default so they're zero-cost when unused) that aren't upstreamed —
nothing gets committed to `heal-research/HeuristicLab` from this
project. Rather than hand-editing `SubtreeCrossover.cs` from memory each
time, apply/revert it as a scripted patch:

```
patches/apply-instrumentation.sh [path-to-HeuristicLab-checkout]   # defaults to ../HeuristicLab
patches/revert-instrumentation.sh [path-to-HeuristicLab-checkout]
```

Both scripts apply/revert `patches/subtree-crossover-instrumentation.patch`
against the target checkout and rebuild
`HeuristicLab.Encodings.SymbolicExpressionTreeEncoding` in place. Always
run `revert-instrumentation.sh` once you're done capturing
`--crossover-noop-output`/`--crossover-kernel-output` data — the checkout
should go back to a clean, uninstrumented `git status` before doing
anything else with it. If the patch no longer applies cleanly (upstream
`SubtreeCrossover.cs` changed), re-derive it: make the same edit by hand
once, `git diff` it, and overwrite `patches/subtree-crossover-instrumentation.patch`
with the new diff.

## PTC2 sampler mode

`--mode ptc2sample --count <n> --seed <int> [--max-length <n>]
[--max-depth <n>] --reference-csv <csv> --target <col> --lengths-output
<csv> --symbols-output <csv>` invokes HL's `ProbabilisticTreeCreator`
(PTC2) directly, with no GA/selection/crossover/mutation involved at
all — just raw creator output, for isolating PTC2's own length
distribution and symbol frequencies. `--reference-csv`/`--target` supply
a real dataset so the grammar's `Variable` terminal is configured with
actual variable names (`grammar.ConfigureVariableSymbols(problemData)`)
the same way a real GP/GPC run's `Problem` wires them in — a bare
grammar with no `ProblemData` attached has zero configured variable
names and can never select `Variable` at all. Outputs: `lengths-output`
(one `length` column, raw node count including both `RootSymbol`/
`StartSymbol` wrapper nodes, uncorrected) and `symbols-output` (`symbol,
count, fraction`, one row per distinct symbol name across all sampled
trees).
