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
  // every generation, replacing each one with a freshly PTC2-created individual (re-generated,
  // not a new crossover event), optimized+evaluated with the real evaluator's own LM step,
  // retried up to MaxRetries times if the replacement is itself degenerate. Tests the retention
  // side of the degenerate-mass-inflation finding directly, independent of any theory about why
  // the degenerate mass forms in the first place (Intervention A, scaling the LM budget, refuted
  // the "flat budget" causal story but left the phenomenon itself -- and this retention-side
  // question -- untouched).
  //
  // Calls SymbolicRegressionParameterOptimizationEvaluator.OptimizeParameters directly (the same
  // real, unmodified static helper ScaledParameterOptimizationEvaluator already uses) rather than
  // the evaluator instance's own Evaluate() method -- calling Evaluate() as a bare method outside
  // the normal operator-graph execution throws a NullReferenceException (its RandomParameter has
  // no bound ExecutionContext when invoked this way, and its 6-arg Evaluate overload reads
  // RandomParameter.ActualValue directly with no null/context guard). That exception was getting
  // silently swallowed somewhere in HL's engine/analyzer execution machinery -- the run "completed"
  // with Generations executed=0 and no visible error at all, which is what took a while to track
  // down. OptimizeParameters is a pure static helper with no such dependency.
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

        for (int i = 0; i < trees.Length; i++) {
          if (qualities[i].Value <= 0) {
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
            PurgeCount++;
          }
        }
      }
      return base.Apply();
    }
  }
}
