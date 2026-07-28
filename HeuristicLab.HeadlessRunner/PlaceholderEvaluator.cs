using System.Collections.Generic;
using HEAL.Attic;
using HeuristicLab.Common;
using HeuristicLab.Core;
using HeuristicLab.Data;
using HeuristicLab.Encodings.SymbolicExpressionTreeEncoding;
using HeuristicLab.Problems.DataAnalysis;
using HeuristicLab.Problems.DataAnalysis.Symbolic;
using HeuristicLab.Problems.DataAnalysis.Symbolic.Regression;

namespace HeuristicLab.HeadlessRunner {
  // Evaluation-free stand-in for SymbolicRegressionParameterOptimizationEvaluator: skips the
  // LM constant-optimization step (and any real fitness computation) entirely, assigning a cheap
  // uniform-random Quality instead. Used via HL_EVAL_FREE=1 to isolate crossover/reinsertion
  // structural dynamics from fitness-driven dynamics at a fraction of the per-generation cost --
  // fitness becomes pure noise, so it can still break ties for Elites=1 without introducing a
  // stable-sort artifact the way a fixed constant would.
  [Item("Placeholder Evaluator (evaluation-free)", "Assigns a cheap uniform-random Quality instead of actually evaluating the tree.")]
  [StorableType("F3D8B6C1-8B2A-4B1E-9C5C-6E2A9B5B7C10")]
  public sealed class PlaceholderEvaluator : SymbolicRegressionSingleObjectiveEvaluator {
    public override bool Maximization => false;

    [StorableConstructor]
    private PlaceholderEvaluator(StorableConstructorFlag _) : base(_) { }
    private PlaceholderEvaluator(PlaceholderEvaluator original, Cloner cloner) : base(original, cloner) { }
    public PlaceholderEvaluator() : base() { }

    public override IDeepCloneable Clone(Cloner cloner) => new PlaceholderEvaluator(this, cloner);

    public override IOperation InstrumentedApply() {
      QualityParameter.ActualValue = new DoubleValue(RandomParameter.ActualValue.NextDouble());
      return base.InstrumentedApply();
    }

    public override double Evaluate(IExecutionContext context, ISymbolicExpressionTree tree, IRegressionProblemData problemData, IEnumerable<int> rows) {
      RandomParameter.ExecutionContext = context;
      double q = RandomParameter.ActualValue.NextDouble();
      RandomParameter.ExecutionContext = null;
      return q;
    }

    public override double Evaluate(
      ISymbolicExpressionTree tree,
      IRegressionProblemData problemData,
      IEnumerable<int> rows,
      ISymbolicDataAnalysisExpressionTreeInterpreter interpreter,
      bool applyLinearScaling = true,
      double lowerEstimationLimit = double.MinValue,
      double upperEstimationLimit = double.MaxValue) {
      return 0.0;
    }
  }
}
