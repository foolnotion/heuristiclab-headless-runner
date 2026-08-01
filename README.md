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
- `HL_MAXLENGTH` / `HL_MAXDEPTH` — override
  `MaximumSymbolicExpressionTreeLength`/`MaximumSymbolicExpressionTreeDepth`
  (defaults 50/20). Set far above anything the population could reach
  (e.g. `100000`/`1000`) for a donor-side-budget-unbound ablation,
  mirroring Operon's `--maxlength`/`--maxdepth` unbound sweep — these
  feed directly into `SubtreeCrossover`'s `maxInsertedBranchLength`/
  `maxInsertedBranchDepth` donor-branch-selection gate (see
  `SubtreeCrossover.cs:121`/`:122`), not just the initial-population
  PTC2 budget.
- `HL_ELITES` — number of individuals `BestSelector` carries forward
  unconditionally each generation (default 1). Set to `0` to disable
  elitism entirely — every generation's whole population, including
  what would have been the elite, gets replaced by newly-generated
  offspring — for testing how much of the equilibrium-length effect
  comes from *having* an elite at all (as opposed to the elite's own
  size specifically, already tested separately via elite-tenure).
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
- `--crossover-donor-output <csv>` — export one row per **actual**
  `SubtreeCrossover.Cross()` swap (`problem, noise, variant, seed,
  generation, donor_length, inserted_length`; no-op calls, where no
  donor branch fit the size budget, are logged internally with a
  `(-1,-1)` sentinel to keep row-index-based generation inference
  aligned with `--crossover-kernel-output`/`--crossover-noop-output`,
  but are filtered out of the CSV itself since there's no real donor
  event to report), for the donor side of the crossover kernel:
  `donor_length` is parent1's *total* tree length (not just the excised
  branch), `inserted_length` is the length of the branch actually
  grafted into parent0 — for checking whether donor-tree size
  correlates with the donor branch it contributes (operon's own
  reinserter shows ~zero correlation there). **Requires the
  instrumentation patch** — see `patches/` below.
- `--crossover-joined-output <csv> [--crossover-joined-min-generation <n>]`
  — export one row per `SubtreeCrossover.Cross()` call (`problem, noise,
  variant, seed, generation, parent_length, removed_length,
  donor_length, inserted_length, parent_depth, donor_depth`) with
  **both** the excision and donor side together in the same row, always
  (including no-ops, with `inserted_length=-1` — unlike
  `--crossover-donor-output`, no rows are filtered out, so there's no
  need to reconstruct alignment via `--crossover-noop-output` when both
  sides are wanted jointly). `parent_depth`/`donor_depth` are
  `parent0.Depth`/`parent1.Depth` (whole-tree, wrapper-inclusive, same
  convention as `raw_depth` elsewhere) at the moment of the call, added
  for testing whether tree shape (length-vs-depth) explains the
  removed/L ratio's decay under the unbound ablation.
  `--crossover-joined-min-generation` filters to `generation >= n`
  (default 0), for skipping the burn-in transient on equilibrium-region
  kernel dumps over many generations. **Requires the instrumentation
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
- `--population-sample-output <csv> --population-sample-generations <comma-list>`
  — dumps `(length, quality)` for every individual in the population at
  the given generations (e.g. `--population-sample-generations
  500,600,700,800,900`), for measuring `correlation(length, fitness)`
  across the full population rather than just population-level
  best/avg/worst. Adds a new `PopulationSampleAnalyzer` (see
  `PopulationSampleAnalyzer.cs`) to `ga.Analyzer`, using the same
  `ScopeTreeLookupParameter` mechanism HL's own
  `BestAverageWorstQualityAnalyzer`/`MinAverageMaxSymbolicExpressionTreeLengthAnalyzer`
  already use to reach every individual in the population. **No HL
  source patch needed** — entirely new code in `PopulationSampleAnalyzer.cs`,
  unlike the crossover instrumentation below. Output:
  `problem, noise, variant, seed, generation, length, quality` (one row
  per sampled individual per generation).
- `HL_LM_SCALE=1` — swap in `ScaledParameterOptimizationEvaluator` (see
  `ScaledParameterOptimizationEvaluator.cs`) instead of the real GPC
  evaluator: same ALGLIB+AutoDiff LM optimization
  (`SymbolicRegressionParameterOptimizationEvaluator.OptimizeParameters`,
  called directly and unmodified), but with the iteration budget scaled
  by the tree's own parameter count (`maxIterations = 10*(k+1)`) instead
  of the real evaluator's flat `maxIterations=10` — mirrors operon's own
  `maxfev = iterations*(n_params+1)` convention, for testing whether a
  flat LM budget disadvantages larger trees.
  `SymbolicRegressionParameterOptimizationEvaluator` is `sealed`, so this
  doesn't subclass it — it reimplements only the thin
  `InstrumentedApply()`/`Evaluate()` wiring and delegates the actual
  optimization to the real static helper, so there's no risk of the LM
  logic itself drifting from the real evaluator. No HL source patch
  needed for this one either.
- `HL_PURGE_DEGENERATE=1` — swap in `PurgeDegenerateAnalyzer` (see
  `PurgeDegenerateAnalyzer.cs`), added to `ga.Analyzer`: every
  generation, replaces any individual with `Quality<=0` with a freshly
  PTC2-created individual (optimized via the real
  `SymbolicRegressionParameterOptimizationEvaluator.OptimizeParameters`
  static helper, retried up to `MaxRetries=20` times if the replacement
  is itself degenerate), for testing whether purging degenerate
  individuals before they can persist collapses the population-length
  equilibrium. No HL source patch needed — uses the same
  `ScopeTreeLookupParameter` mechanism as `PopulationSampleAnalyzer`,
  mutating `ISymbolicExpressionTree.Root`/`DoubleValue.Value` in place
  (both settable, no scope-tree surgery needed). **Important**: call the
  evaluator's static `OptimizeParameters` helper directly, not the
  evaluator instance's own `Evaluate(tree, problemData, rows,
  interpreter, ...)` method — that overload reads `RandomParameter.ActualValue`
  with no null guard, which is `null` (and throws) when called as a bare
  method outside the normal operator-graph execution context. That
  exception gets silently swallowed somewhere in HL's engine/analyzer
  machinery rather than surfacing as an error — the symptom is a
  "successful"-looking run with `Generations executed: 0` regardless of
  `HL_GENS`, no error printed at all. If you add a new evaluator-calling
  operator like this, use the static helper (or explicitly set
  `RandomParameter.ExecutionContext` first) — don't call the instance
  `Evaluate()` overload directly from outside the operator graph.
- `HL_GRAMMAR=addmul` — restricts `BuildGrammar()`'s grammar to just
  `Addition`/`Multiplication` (plus `Variable`/`Constant`/`Number`
  terminals): disables the Trigonometric Functions and Power Functions
  symbol groups (group-level `Enabled=false` alone suffices regardless
  of the individual member symbols' own flags — see the two-level
  wiring note above `BuildGrammar()`) plus `Division`, `Exponential`,
  and `Logarithm` individually. A purely linear/polynomial grammar where
  nothing can ever produce NaN/undefined results, for isolating whether
  a length-dependent degenerate-fitness effect is specific to
  domain-violating functions (`div`/`log`/`sqrt`) or more fundamental to
  overparameterized local search in general. Verify the restriction
  actually took effect with `--mode ptc2sample --symbols-output <csv>`
  before trusting a real run — sampled trees should contain only
  `Addition`/`Multiplication`/`Constant`/`Number`/`Variable` plus the
  `ProgramRootSymbol`/`StartSymbol` wrapper nodes, nothing else.
- `HL_DISABLE_NUMBER=1` — disables the `Number` terminal (plain random
  numeric literal, `MinValue=-20`/`MaxValue=20`, enabled by default),
  leaving `Constant`/`Variable` as the only two terminal types — matches
  operon's terminal set (`constant`, `variable`; no separate
  "plain literal" symbol), for testing whether HL's 3-way terminal split
  (`Number`+`Constant`+`Variable`) vs. operon's 2-way one fully explains
  the measured terminal-frequency dilution between engines (predicted
  ratio ~2/3, matches the measured ~0.68-0.70 almost exactly). Verified
  live in both the real-run and `--mode ptc2sample` paths (`[verify]
  Number.Enabled=...`). **Note**: with `Number` disabled and no evaluator
  ever invoked (i.e. under `--mode ptc2sample`, which has no GA loop at
  all), every sampled `Constant` node prints as `0` — `ConstantTreeNode`
  has no `ResetLocalParameters` override (unlike `NumberTreeNode`, which
  draws a fresh uniform-random value per node at creation time), so its
  `Value` is just a pass-through read of the grammar's single shared
  `Constant` symbol instance, which starts at C#'s default `double` value
  (`0.0`) and is only ever written by the LM constant-optimization
  evaluator during a real run's fitness evaluation. This is expected
  behavior, not a bug — `Constant` is designed as an LM-optimizable
  placeholder, not a randomized literal — but it means a bare PTC2 sample
  can validate symbol-frequency/length distributions with `Number`
  disabled, not constant-value distributions (there's nothing to sample
  there without a real evaluator run).

- `HL_MUTATOR_SET=<comma-list>` — restricts the enabled mutator subset to
  exactly the given tokens (`onepoint`, `changetype`, `fulltree`,
  `replace`, `remove`, mapping to `OnePointShaker`,
  `ChangeNodeTypeManipulation`, `FullTreeShaker`,
  `ReplaceBranchManipulation`, `RemoveBranchManipulation`
  respectively), for isolating each operator's structural contribution
  one at a time (e.g. a cumulative single-generation ablation from a
  seeded population: `onepoint` -> `onepoint,changetype` ->
  `onepoint,changetype,fulltree` -> ... ). Unset keeps the existing
  default (all 5). Verified live (`[verify] enabled mutators = ...`).
- `HL_MUTATION_TRACE=1` — enables
  `SymbolicExpressionTreeManipulator.LengthLog` (see instrumentation
  patch below): logs `(manipulator type name, length before, length
  after)` for every manipulator invocation during the run, printing a
  per-type summary at the end (`N invocations, M changed tree length`).
  Tests structural size-neutrality of a manipulator directly — same
  tree object, length read immediately before/after its own
  `Manipulate()` call — as opposed to inferring it from population-level
  length differences across configs with the operator enabled vs. not,
  which can't distinguish "this operator changes size" from "this
  operator's own random-draw count differs, shifting subsequent
  individuals' crossover outcomes downstream, even though the operator
  itself never touches tree structure." Used to confirm
  `OnePointShaker`/`FullTreeShaker`/`ChangeNodeTypeManipulation` are all
  genuinely size-neutral in HL (0 length changes across 163+ live
  invocations each) despite `HL_MUTATOR_SET` ablation configs 1-4 (which
  only add these three) *not* coming back byte-identical to each other —
  the RNG-stream-shift explanation, not a hidden size-changing bug.

### Instrumentation patch (`SubtreeCrossover.NoOpLog` / `.KernelLog` / `.DonorLog`, `SymbolicExpressionTreeManipulator.LengthLog`)

`--crossover-noop-output`/`--crossover-kernel-output`/
`--crossover-donor-output` need three fields on `SubtreeCrossover`
(`NoOpLog`/`KernelLog`/`DonorLog`) and `HL_MUTATION_TRACE` needs one
field on `SymbolicExpressionTreeManipulator` (`LengthLog`), all null by
default so they're zero-cost when unused, that aren't upstreamed —
nothing gets committed to `heal-research/HeuristicLab` from this
project. Rather than hand-editing those files from memory each time,
apply/revert it as a scripted patch:

```
patches/apply-instrumentation.sh [path-to-HeuristicLab-checkout]   # defaults to ../HeuristicLab
patches/revert-instrumentation.sh [path-to-HeuristicLab-checkout]
```

Both scripts apply/revert `patches/subtree-crossover-instrumentation.patch`
(despite the name, it now covers both files above) against the target
checkout and rebuild `HeuristicLab.Encodings.SymbolicExpressionTreeEncoding`
in place. Always run `revert-instrumentation.sh` once you're done
capturing `--crossover-noop-output`/`--crossover-kernel-output`/
`--crossover-donor-output`/`HL_MUTATION_TRACE` data — the checkout
should go back to a clean, uninstrumented `git status` before doing
anything else with it. If the patch no longer applies cleanly (upstream
`SubtreeCrossover.cs`/`SymbolicExpressionTreeManipulator.cs` changed),
re-derive it: make the same edits by hand once, `git diff` both files
together, and overwrite `patches/subtree-crossover-instrumentation.patch`
with the new combined diff.

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
