using System;
using HEAL.Attic;
using HeuristicLab.Common;
using HeuristicLab.Core;
using HeuristicLab.Data;
using HeuristicLab.Encodings.SymbolicExpressionTreeEncoding;
using HeuristicLab.Operators;
using HeuristicLab.Optimization;
using HeuristicLab.Parameters;
using HeuristicLab.Problems.DataAnalysis;
using HeuristicLab.Problems.DataAnalysis.Symbolic;
using HeuristicLab.Problems.DataAnalysis.Symbolic.Regression;

namespace HeuristicLab.HeadlessRunner {
  // Intervention B: purges individuals with Quality<=0 (degenerate/constant-predictor-equivalent)
  // every generation. Tests the retention side of the degenerate-mass-inflation finding directly,
  // independent of any theory about why the degenerate mass forms in the first place (Intervention
  // A, scaling the LM budget, refuted the "flat budget" causal story but left the phenomenon
  // itself -- and this retention-side question -- untouched).
  //
  // Replacement mechanism: PARENT-SELECTION SWAP (size-neutral by construction). The first version
  // of this analyzer replaced each purged slot with a freshly PTC2-created tree (mean ~27 raw
  // nodes), which injected continuously-large material into ~11.5% of the population every
  // generation -- a confound independent of anything about degenerate-mass retention, and very
  // likely why that version's equilibrium nearly doubled (8.45 -> 16.29) rather than testing what
  // it was meant to test. This version instead promotes a uniformly-random *surviving* individual
  // (Quality>0, sampled from the pre-purge population so replacements can't chain within a single
  // generation) into each purged slot -- a deep clone of that survivor's tree/quality, not a
  // shared reference. This keeps the replacement's size distribution identical to the population's
  // own current surviving distribution, by construction, with no new tree creation or evaluator
  // call at all. Falls back to a fresh PTC2 individual (optimized via
  // SymbolicRegressionParameterOptimizationEvaluator.OptimizeParameters, the same real static
  // helper ScaledParameterOptimizationEvaluator uses) only in the degenerate edge case where the
  // entire population is degenerate and there is no survivor to promote -- calling the evaluator's
  // own Evaluate() method directly (rather than this static helper) throws a silently-swallowed
  // NullReferenceException outside the normal operator-graph execution context (its RandomParameter
  // has no bound ExecutionContext), so OptimizeParameters is used there too.
  //
  // Added to ga.Analyzer via the same ScopeTreeLookupParameter mechanism PopulationSampleAnalyzer
  // uses (mutating the existing tree/quality objects in place -- ISymbolicExpressionTree.Root and
  // DoubleValue.Value are both settable, so no scope-tree surgery is needed) -- no HeuristicLab
  // source changes needed for this one either.
  [Item("PurgeDegenerateAnalyzer", "Replaces individuals with Quality<=0 with a fresh PTC2 individual each generation.")]
  [StorableType("C3D4E5F6-A7B8-49C0-9D2E-3F4A5B6C7D8E")]
  public sealed class PurgeDegenerateAnalyzer : SingleSuccessorOperator, IAnalyzer {
    public bool EnabledByDefault => true;

    public ScopeTreeLookupParameter<ISymbolicExpressionTree> SymbolicExpressionTreeParameter {
      get { return (ScopeTreeLookupParameter<ISymbolicExpressionTree>)Parameters["SymbolicExpressionTree"]; }
    }
    public ScopeTreeLookupParameter<DoubleValue> QualityParameter {
      get { return (ScopeTreeLookupParameter<DoubleValue>)Parameters["Quality"]; }
    }

    // Static configuration, set once from Program.cs before Start() -- mirrors the
    // NoOpLog/KernelLog/TargetGenerations pattern used by the other harness-side instrumentation.
    // Uses its own IRandom (seeded from the CLI --seed) rather than a scope-bound "Random"
    // LookupParameter -- reproducibility of the replacement draws isn't a concern for this
    // ablation the way it would be for the main run's own RNG stream.
    public static bool Enabled = false;
    public static IRandom Random;
    public static ISymbolicExpressionGrammar Grammar;
    public static int MaxLength;
    public static int MaxDepth;
    public static IRegressionProblemData ProblemData;
    public static ISymbolicDataAnalysisExpressionTreeInterpreter Interpreter;
    public static bool ApplyLinearScaling;
    public static double LowerEstimationLimit = double.MinValue;
    public static double UpperEstimationLimit = double.MaxValue;
    public static int BaseIterations = 10; // matches the real evaluator's own default
    public static int MaxRetries = 20;
    public static long PurgeCount = 0;
    public static long FellBackToStillDegenerateCount = 0;
    public static long ApplyCallCount = 0;
    public static long IndividualsSeenCount = 0;
    // Incremented only in the all-degenerate edge case (no survivor to promote), where this
    // analyzer falls back to the old fresh-PTC2 replacement mechanism for that generation.
    public static long NoSurvivorFallbackCount = 0;

    [StorableConstructor]
    private PurgeDegenerateAnalyzer(StorableConstructorFlag _) : base(_) { }
    private PurgeDegenerateAnalyzer(PurgeDegenerateAnalyzer original, Cloner cloner) : base(original, cloner) { }
    public PurgeDegenerateAnalyzer() : base() {
      Parameters.Add(new ScopeTreeLookupParameter<ISymbolicExpressionTree>("SymbolicExpressionTree", "The tree of each individual in the population."));
      Parameters.Add(new ScopeTreeLookupParameter<DoubleValue>("Quality", "The quality of each individual in the population."));
    }

    public override IDeepCloneable Clone(Cloner cloner) => new PurgeDegenerateAnalyzer(this, cloner);

    public override IOperation Apply() {
      if (Enabled) {
        var trees = SymbolicExpressionTreeParameter.ActualValue;
        var qualities = QualityParameter.ActualValue;
        ApplyCallCount++;
        IndividualsSeenCount += trees.Length;
        var rows = ProblemData.TrainingIndices;

        // Snapshot survivor indices (Quality>0) from the pre-purge population, so that a slot
        // filled this generation can't itself be sampled as a donor for another slot in the same
        // pass (would let replacements chain within one generation).
        var survivorIndices = new System.Collections.Generic.List<int>();
        for (int i = 0; i < trees.Length; i++)
          if (qualities[i].Value > 0) survivorIndices.Add(i);

        for (int i = 0; i < trees.Length; i++) {
          if (qualities[i].Value <= 0) {
            if (survivorIndices.Count > 0) {
              int donor = survivorIndices[Random.Next(survivorIndices.Count)];
              var clonedRoot = (ISymbolicExpressionTreeNode)trees[donor].Root.Clone();
              trees[i].Root = clonedRoot;
              qualities[i].Value = qualities[donor].Value;
            } else {
              // Edge case: entire population is degenerate, nothing to promote. Fall back to a
              // fresh PTC2 individual (same mechanism as the original PTC2-replacement version).
              NoSurvivorFallbackCount++;
              double newQuality = double.NegativeInfinity;
              ISymbolicExpressionTreeNode newRoot = null;
              for (int attempt = 0; attempt < MaxRetries; attempt++) {
                var candidate = ProbabilisticTreeCreator.Create(Random, Grammar, MaxLength, MaxDepth);
                var counter = new SymbolicRegressionParameterOptimizationEvaluator.EvaluationsCounter();
                double q = SymbolicRegressionParameterOptimizationEvaluator.OptimizeParameters(
                  Interpreter, candidate, ProblemData, rows, ApplyLinearScaling, BaseIterations,
                  updateVariableWeights: true, lowerEstimationLimit: LowerEstimationLimit,
                  upperEstimationLimit: UpperEstimationLimit, updateParametersInTree: true, counter: counter);
                newRoot = candidate.Root;
                newQuality = q;
                if (q > 0) break;
              }
              if (newQuality <= 0) FellBackToStillDegenerateCount++;
              trees[i].Root = newRoot;
              qualities[i].Value = newQuality;
            }
            PurgeCount++;
          }
        }
      }
      return base.Apply();
    }
  }
}
