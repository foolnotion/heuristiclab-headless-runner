using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
  // Records the population visible in HL's normal analyzer phase. This is a
  // generation-end snapshot, not an offspring-boundary measurement: callers
  // must not infer feasible-parent -> feasible-offspring transitions from it.
  [Item("ShapeDynamicsAnalyzer", "Summarizes live constrained-population dynamics once per generation.")]
  [StorableType("E5F6A7B8-C9D0-41E2-B3F4-A5B6C7D8E9F0")]
  public sealed class ShapeDynamicsAnalyzer : SingleSuccessorOperator, IAnalyzer {
    public bool EnabledByDefault => false;
    public ScopeTreeLookupParameter<ISymbolicExpressionTree> SymbolicExpressionTreeParameter {
      get { return (ScopeTreeLookupParameter<ISymbolicExpressionTree>)Parameters["SymbolicExpressionTree"]; }
    }
    public LookupParameter<IntValue> GenerationsParameter {
      get { return (LookupParameter<IntValue>)Parameters["Generations"]; }
    }

    public static ShapeConstrainedRegressionProblemData ProblemData;
    public static IBoundsEstimator BoundsEstimator;
    public static List<string> Log;

    [StorableConstructor]
    private ShapeDynamicsAnalyzer(StorableConstructorFlag _) : base(_) { }
    private ShapeDynamicsAnalyzer(ShapeDynamicsAnalyzer original, Cloner cloner) : base(original, cloner) { }
    public ShapeDynamicsAnalyzer() {
      Parameters.Add(new ScopeTreeLookupParameter<ISymbolicExpressionTree>("SymbolicExpressionTree", "Population trees."));
      Parameters.Add(new LookupParameter<IntValue>("Generations", "Current generation."));
    }
    public override IDeepCloneable Clone(Cloner cloner) => new ShapeDynamicsAnalyzer(this, cloner);

    public override IOperation Apply() {
      if (Log == null) return base.Apply();
      var trees = SymbolicExpressionTreeParameter.ActualValue;
      var constraints = ProblemData.ShapeConstraints.EnabledConstraints.ToArray();
      int feasible = 0, infeasible = 0, uncertified = 0;
      double violationSum = 0.0;
      var feasibleLengths = new List<int>();
      var infeasibleLengths = new List<int>();
      var feasibleDepths = new List<int>();
      var infeasibleDepths = new List<int>();
      foreach (var tree in trees) {
        try {
          var violations = IntervalUtil.GetConstraintViolations(constraints, BoundsEstimator,
            ProblemData.VariableRanges, tree).ToArray();
          if (violations.Any(v => double.IsNaN(v) || double.IsInfinity(v))) {
            uncertified++;
            infeasibleLengths.Add(tree.Length);
            infeasibleDepths.Add(tree.Depth);
          } else {
            var violation = violations.Sum();
            violationSum += violation;
            if (violation == 0.0) {
              feasible++;
              feasibleLengths.Add(tree.Length);
              feasibleDepths.Add(tree.Depth);
            } else {
              infeasible++;
              infeasibleLengths.Add(tree.Length);
              infeasibleDepths.Add(tree.Depth);
            }
          }
        } catch (Exception) {
          uncertified++;
          infeasibleLengths.Add(tree.Length);
          infeasibleDepths.Add(tree.Depth);
        }
      }
      Func<List<int>, string> mean = xs => xs.Count == 0 ? "" : xs.Average().ToString("R", CultureInfo.InvariantCulture);
      Log.Add(string.Join(",",
        GenerationsParameter.ActualValue.Value.ToString(CultureInfo.InvariantCulture),
        trees.Length.ToString(CultureInfo.InvariantCulture),
        feasible.ToString(CultureInfo.InvariantCulture),
        infeasible.ToString(CultureInfo.InvariantCulture),
        uncertified.ToString(CultureInfo.InvariantCulture),
        violationSum.ToString("R", CultureInfo.InvariantCulture),
        mean(feasibleLengths), mean(infeasibleLengths), mean(feasibleDepths), mean(infeasibleDepths)));
      return base.Apply();
    }
  }
}
