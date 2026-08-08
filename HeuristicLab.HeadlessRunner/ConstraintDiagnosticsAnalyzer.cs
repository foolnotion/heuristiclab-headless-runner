using System;
using System.Collections.Generic;
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
  // Read-only diagnostic for the upstream constrained evaluator. It runs in
  // the normal analyzer phase and recomputes the exact upstream score so an
  // operator-graph failure cannot be mistaken for an event-wiring failure.
  [Item("ConstraintDiagnosticsAnalyzer", "Records upstream constraint-evaluator outcomes for every population member.")]
  [StorableType("D4E5F6A7-B8C9-40D1-A2E3-F4A5B6C7D8E9")]
  public sealed class ConstraintDiagnosticsAnalyzer : SingleSuccessorOperator, IAnalyzer {
    public bool EnabledByDefault => false;
    public ScopeTreeLookupParameter<ISymbolicExpressionTree> SymbolicExpressionTreeParameter {
      get { return (ScopeTreeLookupParameter<ISymbolicExpressionTree>)Parameters["SymbolicExpressionTree"]; }
    }
    public ScopeTreeLookupParameter<DoubleValue> QualityParameter {
      get { return (ScopeTreeLookupParameter<DoubleValue>)Parameters["Quality"]; }
    }
    public LookupParameter<IntValue> GenerationsParameter {
      get { return (LookupParameter<IntValue>)Parameters["Generations"]; }
    }

    public static ShapeConstrainedRegressionProblemData ProblemData;
    public static ISymbolicDataAnalysisExpressionTreeInterpreter Interpreter;
    public static NMSESingleObjectiveConstraintsEvaluator Evaluator;
    public static double LowerEstimationLimit;
    public static double UpperEstimationLimit;
    public static List<string> Log;

    [StorableConstructor]
    private ConstraintDiagnosticsAnalyzer(StorableConstructorFlag _) : base(_) { }
    private ConstraintDiagnosticsAnalyzer(ConstraintDiagnosticsAnalyzer original, Cloner cloner) : base(original, cloner) { }
    public ConstraintDiagnosticsAnalyzer() {
      Parameters.Add(new ScopeTreeLookupParameter<ISymbolicExpressionTree>("SymbolicExpressionTree", "Population trees."));
      Parameters.Add(new ScopeTreeLookupParameter<DoubleValue>("Quality", "Stored population qualities."));
      Parameters.Add(new LookupParameter<IntValue>("Generations", "Current generation."));
    }
    public override IDeepCloneable Clone(Cloner cloner) => new ConstraintDiagnosticsAnalyzer(this, cloner);

    public override IOperation Apply() {
      if (Log == null) return base.Apply();
      var trees = SymbolicExpressionTreeParameter.ActualValue;
      var qualities = QualityParameter.ActualValue;
      int generation = GenerationsParameter.ActualValue.Value;
      var constraints = ProblemData.ShapeConstraints.EnabledConstraints;
      for (int i = 0; i < trees.Length; i++) {
        try {
          var violations = IntervalUtil.GetConstraintViolations(constraints, Evaluator.BoundsEstimator,
            ProblemData.VariableRanges, trees[i]).ToArray();
          var recomputed = NMSESingleObjectiveConstraintsEvaluator.Calculate(trees[i], ProblemData,
            ProblemData.TrainingIndices, Interpreter, LowerEstimationLimit, UpperEstimationLimit,
            Evaluator.BoundsEstimator, Evaluator.UseSoftConstraints, Evaluator.PenalityFactor);
          Log.Add(string.Join(",", generation, i, trees[i].Length,
            qualities[i].Value.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
            recomputed.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
            string.Join(";", violations.Select(v => v.ToString("R", System.Globalization.CultureInfo.InvariantCulture))), ""));
        } catch (Exception ex) {
          Log.Add(string.Join(",", generation, i, trees[i].Length,
            qualities[i].Value.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
            "", "", ex.GetType().Name + ":" + ex.Message.Replace(",", ";")));
        }
      }
      return base.Apply();
    }
  }
}
