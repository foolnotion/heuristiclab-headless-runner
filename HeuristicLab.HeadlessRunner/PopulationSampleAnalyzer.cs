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

namespace HeuristicLab.HeadlessRunner {
  // Dumps (length, quality) for every individual in the population, at a configurable set of
  // generations -- for measuring correlation(length, fitness) across the full population at
  // late/equilibrium generations, not just population-level summary stats (best/avg/worst).
  // Added to ga.Analyzer alongside HL's own default analyzers; uses the same ScopeTreeLookupParameter
  // mechanism those already use to reach every individual in the population, so no HeuristicLab
  // source changes are needed at all for this one.
  [Item("PopulationSampleAnalyzer", "Dumps (length, quality) for every individual at configured generations.")]
  [StorableType("A1B2C3D4-E5F6-47A8-9B0C-1D2E3F4A5B6C")]
  public sealed class PopulationSampleAnalyzer : SingleSuccessorOperator, IAnalyzer {
    public bool EnabledByDefault => true;

    public ScopeTreeLookupParameter<ISymbolicExpressionTree> SymbolicExpressionTreeParameter {
      get { return (ScopeTreeLookupParameter<ISymbolicExpressionTree>)Parameters["SymbolicExpressionTree"]; }
    }
    public ScopeTreeLookupParameter<DoubleValue> QualityParameter {
      get { return (ScopeTreeLookupParameter<DoubleValue>)Parameters["Quality"]; }
    }
    public LookupParameter<IntValue> GenerationsParameter {
      get { return (LookupParameter<IntValue>)Parameters["Generations"]; }
    }

    // Static, not-instance, so Program.cs can configure/read it without threading a reference
    // through the operator graph. Null by default (zero-cost / opt-in), matching the NoOpLog/
    // KernelLog pattern used for the crossover instrumentation.
    public static HashSet<int> TargetGenerations = null;
    public static List<Tuple<int, int, int, int, double>> Log = null; // (generation, index, length, depth, quality)

    [StorableConstructor]
    private PopulationSampleAnalyzer(StorableConstructorFlag _) : base(_) { }
    private PopulationSampleAnalyzer(PopulationSampleAnalyzer original, Cloner cloner) : base(original, cloner) { }
    public PopulationSampleAnalyzer() : base() {
      Parameters.Add(new ScopeTreeLookupParameter<ISymbolicExpressionTree>("SymbolicExpressionTree", "The tree of each individual in the population."));
      Parameters.Add(new ScopeTreeLookupParameter<DoubleValue>("Quality", "The quality of each individual in the population."));
      Parameters.Add(new LookupParameter<IntValue>("Generations", "The current generation count."));
    }

    public override IDeepCloneable Clone(Cloner cloner) => new PopulationSampleAnalyzer(this, cloner);

    public override IOperation Apply() {
      if (Log != null && TargetGenerations != null) {
        int generation = GenerationsParameter.ActualValue.Value;
        if (TargetGenerations.Contains(generation)) {
          var trees = SymbolicExpressionTreeParameter.ActualValue;
          var qualities = QualityParameter.ActualValue;
          for (int i = 0; i < trees.Length; i++)
            Log.Add(Tuple.Create(generation, i, trees[i].Length, trees[i].Depth, qualities[i].Value));
        }
      }
      return base.Apply();
    }
  }
}
