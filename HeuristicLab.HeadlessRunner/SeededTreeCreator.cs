using System;
using System.Collections.Generic;
using HEAL.Attic;
using HeuristicLab.Common;
using HeuristicLab.Core;
using HeuristicLab.Encodings.SymbolicExpressionTreeEncoding;
using HeuristicLab.Problems.DataAnalysis.Symbolic;

namespace HeuristicLab.HeadlessRunner {
  // Swaps in for the real SolutionCreator (SymbolicDataAnalysisExpressionTreeCreator/PTC2) so
  // generation 0's population is loaded verbatim from a pre-parsed set of trees instead of randomly
  // created -- everything else in the algorithm's operator graph (Selector, Crossover, Mutator,
  // Evaluator, BestSelector-based elitism) is untouched, since none of them call through this
  // creator: SolutionsCreator (initial population) is the only caller of
  // ISymbolicExpressionTreeCreator.CreateTree in this codebase -- manipulators like
  // ReplaceBranchManipulation call the static ProbabilisticTreeCreator.PTC2 directly instead, so
  // they aren't affected by this override at all.
  //
  // SeedPopulation/nextIndex are static, not instance fields, matching the NoOpLog/KernelLog/
  // PopulationSampleAnalyzer.Log pattern already used in this codebase for state that needs to cross
  // the operator-graph boundary from Program.cs -- null/zero by default (opt-in, zero-cost when
  // HL_SEED_POPULATION is unset), and safe against the algorithm cloning its operator graph before
  // Start() since the actual tree instances live in a list keyed by a shared static index, not on
  // this operator's own (possibly-cloned) instance state.
  [Item("SeededTreeCreator", "Serves pre-parsed trees from a static list instead of creating new ones, for byte-identical-population seeding.")]
  [StorableType("6F3A9C1E-2B4D-4E7A-8F1C-3D5B7E9A0C2F")]
  public sealed class SeededTreeCreator : SymbolicDataAnalysisExpressionTreeCreator {
    public static List<ISymbolicExpressionTree> SeedPopulation = null;
    public static int NextIndex = 0;

    [StorableConstructor]
    private SeededTreeCreator(StorableConstructorFlag _) : base(_) { }
    private SeededTreeCreator(SeededTreeCreator original, Cloner cloner) : base(original, cloner) { }
    public SeededTreeCreator() : base() { }

    public override IDeepCloneable Clone(Cloner cloner) => new SeededTreeCreator(this, cloner);

    public override ISymbolicExpressionTree CreateTree(IRandom random, ISymbolicExpressionGrammar grammar, int maxTreeLength, int maxTreeDepth) {
      if (SeedPopulation == null)
        throw new InvalidOperationException("SeededTreeCreator.SeedPopulation was not set before Start().");
      if (NextIndex >= SeedPopulation.Count)
        throw new InvalidOperationException($"SeededTreeCreator: requested tree {NextIndex + 1}, but the seed population only has {SeedPopulation.Count} trees (PopulationSize must match the seed file's line count exactly).");
      return SeedPopulation[NextIndex++];
    }
  }
}
