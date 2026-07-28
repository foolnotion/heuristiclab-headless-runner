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
  // SymbolicRegressionParameterOptimizationEvaluator is sealed, so this doesn't subclass it --
  // it reimplements only the thin InstrumentedApply()/Evaluate() wiring, delegating the actual
  // LM optimization to the real (unmodified) SymbolicRegressionParameterOptimizationEvaluator
  // .OptimizeParameters static helper, just with a per-individual iteration budget scaled by
  // the tree's own optimizable-parameter count (k) instead of a flat constant -- mirroring
  // operon's own maxfev = iterations * (n_params + 1) convention (optimizer.hpp:200, traced
  // earlier in this investigation). BaseIterations defaults to 10 to match the real evaluator's
  // own default, so BaseIterations=10 with a 1-parameter tree gives maxIterations=20, matching
  // operon's convention shape exactly.
  //
  // k is obtained via a throwaway TreeToAutoDiffTermConverter.TryConvertToAutoDiff call (the same
  // public static method OptimizeParameters itself calls internally) purely to read
  // initialParameters.Length before deciding the iteration budget -- the actual optimization
  // still runs through the real, unmodified ALGLIB+AutoDiff call, so there's no risk of the LM
  // logic itself drifting from the real evaluator.
  [Item("Scaled Parameter Optimization Evaluator", "Like SymbolicRegressionParameterOptimizationEvaluator, but LM iteration budget scales with parameter count instead of being flat.")]
  [StorableType("B2C3D4E5-F6A7-48B9-9C1D-2E3F4A5B6C7D")]
  public sealed class ScaledParameterOptimizationEvaluator : SymbolicRegressionSingleObjectiveEvaluator {
    public override bool Maximization => true;

    public int BaseIterations = 10;

    [StorableConstructor]
    private ScaledParameterOptimizationEvaluator(StorableConstructorFlag _) : base(_) { }
    private ScaledParameterOptimizationEvaluator(ScaledParameterOptimizationEvaluator original, Cloner cloner)
      : base(original, cloner) {
      BaseIterations = original.BaseIterations;
    }
    public ScaledParameterOptimizationEvaluator() : base() { }

    public override IDeepCloneable Clone(Cloner cloner) => new ScaledParameterOptimizationEvaluator(this, cloner);

    private int GetScaledIterations(ISymbolicExpressionTree tree, bool updateVariableWeights, bool applyLinearScaling) {
      List<TreeToAutoDiffTermConverter.DataForVariable> parameters;
      double[] initialParameters;
      TreeToAutoDiffTermConverter.ParametricFunction func;
      TreeToAutoDiffTermConverter.ParametricFunctionGradient funcGrad;
      if (!TreeToAutoDiffTermConverter.TryConvertToAutoDiff(tree, updateVariableWeights, applyLinearScaling, out parameters, out initialParameters, out func, out funcGrad))
        return BaseIterations; // not AutoDiff-compatible; OptimizeParameters will itself throw -- match default budget for that (moot) case
      int k = parameters.Count;
      return BaseIterations * (k + 1);
    }

    public override IOperation InstrumentedApply() {
      var tree = SymbolicExpressionTreeParameter.ActualValue;
      var rows = GenerateRowsToEvaluate();
      bool applyLinearScaling = ApplyLinearScalingParameter.ActualValue.Value;
      int maxIterations = GetScaledIterations(tree, updateVariableWeights: true, applyLinearScaling: applyLinearScaling);

      var counter = new SymbolicRegressionParameterOptimizationEvaluator.EvaluationsCounter();
      double quality = SymbolicRegressionParameterOptimizationEvaluator.OptimizeParameters(
        SymbolicDataAnalysisTreeInterpreterParameter.ActualValue, tree, ProblemDataParameter.ActualValue, rows,
        applyLinearScaling, maxIterations, updateVariableWeights: true,
        lowerEstimationLimit: EstimationLimitsParameter.ActualValue.Lower,
        upperEstimationLimit: EstimationLimitsParameter.ActualValue.Upper,
        updateParametersInTree: true, counter: counter);

      QualityParameter.ActualValue = new DoubleValue(quality);
      return base.InstrumentedApply();
    }

    public override double Evaluate(IExecutionContext context, ISymbolicExpressionTree tree, IRegressionProblemData problemData, IEnumerable<int> rows) {
      SymbolicDataAnalysisTreeInterpreterParameter.ExecutionContext = context;
      EstimationLimitsParameter.ExecutionContext = context;
      ApplyLinearScalingParameter.ExecutionContext = context;

      double r2 = SymbolicRegressionSingleObjectivePearsonRSquaredEvaluator.Calculate(
        tree, problemData, rows, SymbolicDataAnalysisTreeInterpreterParameter.ActualValue,
        ApplyLinearScalingParameter.ActualValue.Value,
        EstimationLimitsParameter.ActualValue.Lower, EstimationLimitsParameter.ActualValue.Upper);

      SymbolicDataAnalysisTreeInterpreterParameter.ExecutionContext = null;
      EstimationLimitsParameter.ExecutionContext = null;
      ApplyLinearScalingParameter.ExecutionContext = null;
      return r2;
    }

    public override double Evaluate(
      ISymbolicExpressionTree tree,
      IRegressionProblemData problemData,
      IEnumerable<int> rows,
      ISymbolicDataAnalysisExpressionTreeInterpreter interpreter,
      bool applyLinearScaling = true,
      double lowerEstimationLimit = double.MinValue,
      double upperEstimationLimit = double.MaxValue) {
      int maxIterations = GetScaledIterations(tree, updateVariableWeights: true, applyLinearScaling: applyLinearScaling);
      var counter = new SymbolicRegressionParameterOptimizationEvaluator.EvaluationsCounter();
      return SymbolicRegressionParameterOptimizationEvaluator.OptimizeParameters(
        interpreter, tree, problemData, rows, applyLinearScaling, maxIterations,
        updateVariableWeights: true, lowerEstimationLimit: lowerEstimationLimit,
        upperEstimationLimit: upperEstimationLimit, updateParametersInTree: true, counter: counter);
    }
  }
}
