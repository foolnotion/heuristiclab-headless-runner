using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

using HeuristicLab.Algorithms.GeneticAlgorithm;
using HeuristicLab.Analysis;
using HeuristicLab.Common;
using HeuristicLab.Core;
using HeuristicLab.Data;
using HeuristicLab.Encodings.SymbolicExpressionTreeEncoding;
using HeuristicLab.Optimization;
using HeuristicLab.Problems.DataAnalysis;
using HeuristicLab.Problems.DataAnalysis.Symbolic;
using HeuristicLab.Problems.DataAnalysis.Symbolic.Regression;
using HeuristicLab.Random;
using HeuristicLab.Selection;
using HeuristicLab.SequentialEngine;

namespace HeuristicLab.HeadlessRunner {
  internal static class Program {
    private static int Main(string[] args) {
      if (args.Length > 0 && args[0] == "--mode" && args.Length > 1 && args[1] == "ptc2sample") {
        var sampleOpts = ParseSampleArgs(args);
        if (sampleOpts == null) return 1;
        try {
          RunPtc2Sample(sampleOpts);
          return 0;
        } catch (Exception ex) {
          Console.Error.WriteLine("ERROR: " + ex);
          return 1;
        }
      }

      var opts = ParseArgs(args);
      if (opts == null) return 1;

      try {
        Run(opts);
        return 0;
      } catch (Exception ex) {
        Console.Error.WriteLine("ERROR: " + ex);
        return 1;
      }
    }

    private class Options {
      public string TrainCsv;
      public string TestCsv;
      public string Target;
      public string Variant = "GP"; // GP or GPC
      public int Seed = 1;
      public string Output;
      public string Problem = "problem";
      public string Noise = "0";
      public string ModelOutput;
      public string FormulaOutput;
      public string GenStatsOutput;
    }

    private static Options ParseArgs(string[] args) {
      var o = new Options();
      for (int i = 0; i < args.Length; i++) {
        switch (args[i]) {
          case "--train": o.TrainCsv = args[++i]; break;
          case "--test": o.TestCsv = args[++i]; break;
          case "--target": o.Target = args[++i]; break;
          case "--variant": o.Variant = args[++i]; break;
          case "--seed": o.Seed = int.Parse(args[++i], CultureInfo.InvariantCulture); break;
          case "--output": o.Output = args[++i]; break;
          case "--problem": o.Problem = args[++i]; break;
          case "--noise": o.Noise = args[++i]; break;
          case "--model-output": o.ModelOutput = args[++i]; break;
          case "--formula-output": o.FormulaOutput = args[++i]; break;
          case "--gen-stats-output": o.GenStatsOutput = args[++i]; break;
          default:
            Console.Error.WriteLine("Unknown argument: " + args[i]);
            return null;
        }
      }
      if (o.TrainCsv == null || o.TestCsv == null || o.Target == null || o.Output == null) {
        Console.Error.WriteLine("Usage: HeuristicLab.HeadlessRunner --train <csv> --test <csv> --target <col> --variant GP|GPC --seed <int> --output <csv> [--problem <name>] [--noise 0|1] [--model-output <hl-file>] [--formula-output <csv>] [--gen-stats-output <csv>]");
        return null;
      }
      return o;
    }

    private class SampleOptions {
      public int Count = 20000;
      public int Seed = 1;
      public int MaxLength = 50;
      public int MaxDepth = 20;
      public string LengthsOutput;
      public string SymbolsOutput;
      public string ReferenceCsv;
      public string Target;
    }

    private static SampleOptions ParseSampleArgs(string[] args) {
      var o = new SampleOptions();
      for (int i = 2; i < args.Length; i++) {
        switch (args[i]) {
          case "--count": o.Count = int.Parse(args[++i], CultureInfo.InvariantCulture); break;
          case "--seed": o.Seed = int.Parse(args[++i], CultureInfo.InvariantCulture); break;
          case "--max-length": o.MaxLength = int.Parse(args[++i], CultureInfo.InvariantCulture); break;
          case "--max-depth": o.MaxDepth = int.Parse(args[++i], CultureInfo.InvariantCulture); break;
          case "--lengths-output": o.LengthsOutput = args[++i]; break;
          case "--symbols-output": o.SymbolsOutput = args[++i]; break;
          case "--reference-csv": o.ReferenceCsv = args[++i]; break;
          case "--target": o.Target = args[++i]; break;
          default:
            Console.Error.WriteLine("Unknown argument: " + args[i]);
            return null;
        }
      }
      if (o.LengthsOutput == null || o.SymbolsOutput == null || o.ReferenceCsv == null || o.Target == null) {
        Console.Error.WriteLine("Usage: HeuristicLab.HeadlessRunner --mode ptc2sample --count <n> --seed <int> [--max-length <n>] [--max-depth <n>] --reference-csv <csv> --target <col> --lengths-output <csv> --symbols-output <csv>");
        return null;
      }
      return o;
    }

    // Grammar wiring is two-level: GroupSymbol (e.g. "Trigonometric Functions") gates whether its
    // members are reachable in the grammar's allowed-child rules at all; each individual member
    // symbol also has its own Enabled flag. Both must be true for a function to actually appear
    // in generated trees. ConfigureAsDefaultRegressionGrammar() leaves Arithmetic Functions,
    // Exponential/Logarithmic Functions, Real Valued Symbols and Terminals groups enabled, and
    // disables the Trigonometric/Power/Special/Conditional/TimeSeries groups wholesale, plus
    // Average/Absolute/HyperbolicTangent/Constant individually. We re-enable the specific
    // groups/symbols the paper's function set needs on top of that baseline.
    private static TypeCoherentExpressionGrammar BuildGrammar() {
      var grammar = new TypeCoherentExpressionGrammar();
      grammar.ConfigureAsDefaultRegressionGrammar();

      grammar.Symbols.First(s => s is HeuristicLab.Problems.DataAnalysis.Symbolic.Subtraction).Enabled = false;

      grammar.Symbols.First(s => s.Name == TypeCoherentExpressionGrammar.TrigonometricFunctionsName).Enabled = true;
      grammar.Symbols.First(s => s is HeuristicLab.Problems.DataAnalysis.Symbolic.Tangent).Enabled = false;
      grammar.Symbols.First(s => s is HeuristicLab.Problems.DataAnalysis.Symbolic.HyperbolicTangent).Enabled = true;

      grammar.Symbols.First(s => s.Name == TypeCoherentExpressionGrammar.PowerFunctionsName).Enabled = true;
      grammar.Symbols.First(s => s is HeuristicLab.Problems.DataAnalysis.Symbolic.Power).Enabled = false;
      grammar.Symbols.First(s => s is HeuristicLab.Problems.DataAnalysis.Symbolic.Root).Enabled = false;
      grammar.Symbols.First(s => s is HeuristicLab.Problems.DataAnalysis.Symbolic.Cube).Enabled = false;
      grammar.Symbols.First(s => s is HeuristicLab.Problems.DataAnalysis.Symbolic.CubeRoot).Enabled = false;

      grammar.Symbols.First(s => s is HeuristicLab.Problems.DataAnalysis.Symbolic.Constant).Enabled = true;
      return grammar;
    }

    // Direct ProbabilisticTreeCreator (PTC2) invocation -- no GA, no selection/crossover/mutation,
    // no Problem/Evaluator wiring at all. Matches operon's standalone-sampler ablation: isolates
    // PTC2's own length distribution and symbol frequencies from any GA-loop confound.
    private static void RunPtc2Sample(SampleOptions o) {
      var grammar = BuildGrammar();

      // TypeCoherentExpressionGrammar's Variable terminal isn't usable until it's told which
      // variable names exist -- that wiring normally happens implicitly via
      // SymbolicDataAnalysisProblem.OnProblemDataChanged -> grammar.ConfigureVariableSymbols(problemData)
      // when a real GP/GPC run attaches ProblemData. A bare grammar with no ProblemData attached
      // has zero configured variable names, so PTC2 can never select the Variable symbol at all --
      // reproduced here explicitly against one of the experiment's own reference CSVs (train+test
      // rows, minus the target column) so the sampled grammar matches what a real run actually uses.
      var (refHeader, refRows) = ReadCsv(o.ReferenceCsv);
      var refVariableNames = refHeader;
      var refColumns = new List<System.Collections.IList>();
      for (int c = 0; c < refVariableNames.Count; c++) {
        var col = new List<double>(refRows.Count);
        foreach (var r in refRows) col.Add(r[c]);
        refColumns.Add(col);
      }
      var refDataset = new Dataset(refVariableNames, refColumns);
      var refAllowedInputVariables = refVariableNames.Where(v => v != o.Target).ToList();
      var refProblemData = new RegressionProblemData(refDataset, refAllowedInputVariables, o.Target);
      grammar.ConfigureVariableSymbols(refProblemData);

      var random = new MersenneTwister((uint)o.Seed);

      var symbolCounts = new Dictionary<string, long>();
      var lengths = new List<int>(o.Count);

      for (int i = 0; i < o.Count; i++) {
        var tree = ProbabilisticTreeCreator.Create(random, grammar, o.MaxLength, o.MaxDepth);
        lengths.Add(tree.Length);
        foreach (var node in tree.Root.IterateNodesPrefix()) {
          var name = node.Symbol.Name;
          symbolCounts.TryGetValue(name, out var count);
          symbolCounts[name] = count + 1;
        }
        if (Environment.GetEnvironmentVariable("HL_DEBUG") == "1" && i < 3) {
          Console.WriteLine($"sample {i}: length={tree.Length} depth={tree.Depth} formula={new InfixExpressionFormatter().Format(tree)}");
        }
      }

      using (var lw = new StreamWriter(o.LengthsOutput, append: false)) {
        lw.WriteLine("length");
        foreach (var len in lengths)
          lw.WriteLine(len.ToString(CultureInfo.InvariantCulture));
      }

      long totalNodes = symbolCounts.Values.Sum();
      using (var sw2 = new StreamWriter(o.SymbolsOutput, append: false)) {
        sw2.WriteLine("symbol,count,fraction");
        foreach (var kvp in symbolCounts.OrderBy(kvp => kvp.Key, StringComparer.Ordinal))
          sw2.WriteLine(string.Join(",", kvp.Key, kvp.Value.ToString(CultureInfo.InvariantCulture), (kvp.Value / (double)totalNodes).ToString("R", CultureInfo.InvariantCulture)));
      }

      Console.WriteLine($"[verify] sampled {o.Count} trees via ProbabilisticTreeCreator.Create directly (no GA/selection/crossover/mutation)");
      Console.WriteLine($"[verify] maxLength={o.MaxLength} maxDepth={o.MaxDepth} seed={o.Seed}");
      Console.WriteLine($"length min={lengths.Min()} median={Median(lengths):F2} mean={lengths.Average():F2} max={lengths.Max()}");
    }

    private static double Median(List<int> values) {
      var sorted = values.OrderBy(v => v).ToList();
      int n = sorted.Count;
      return n % 2 == 1 ? sorted[n / 2] : (sorted[n / 2 - 1] + sorted[n / 2]) / 2.0;
    }

    private static (List<string> header, List<double[]> rows) ReadCsv(string path) {
      var lines = File.ReadAllLines(path);
      var header = lines[0].Split(',').Select(s => s.Trim()).ToList();
      var rows = new List<double[]>();
      for (int i = 1; i < lines.Length; i++) {
        if (string.IsNullOrWhiteSpace(lines[i])) continue;
        var parts = lines[i].Split(',');
        var row = new double[parts.Length];
        for (int j = 0; j < parts.Length; j++)
          row[j] = double.Parse(parts[j], CultureInfo.InvariantCulture);
        rows.Add(row);
      }
      return (header, rows);
    }

    private static void Run(Options o) {
      var (trainHeader, trainRows) = ReadCsv(o.TrainCsv);
      var (testHeader, testRows) = ReadCsv(o.TestCsv);
      if (!trainHeader.SequenceEqual(testHeader))
        throw new InvalidOperationException("Train/test CSV column headers do not match.");
      if (!trainHeader.Contains(o.Target))
        throw new InvalidOperationException($"Target column '{o.Target}' not found in CSV header.");

      var variableNames = trainHeader;
      int nTrain = trainRows.Count;
      int nTest = testRows.Count;

      var columns = new List<System.Collections.IList>();
      for (int c = 0; c < variableNames.Count; c++) {
        var col = new List<double>(nTrain + nTest);
        foreach (var r in trainRows) col.Add(r[c]);
        foreach (var r in testRows) col.Add(r[c]);
        columns.Add(col);
      }

      var dataset = new Dataset(variableNames, columns);
      var allowedInputVariables = variableNames.Where(v => v != o.Target).ToList();
      var problemData = new RegressionProblemData(dataset, allowedInputVariables, o.Target);
      problemData.TrainingPartition.Start = 0;
      problemData.TrainingPartition.End = nTrain;
      problemData.TestPartition.Start = nTrain;
      problemData.TestPartition.End = nTrain + nTest;

      if (Environment.GetEnvironmentVariable("HL_DEBUG") == "1") {
        Console.WriteLine("Variables: " + string.Join(",", variableNames));
        Console.WriteLine("AllowedInputVariables: " + string.Join(",", problemData.AllowedInputVariables));
        Console.WriteLine("TargetVariable: " + problemData.TargetVariable);
        Console.WriteLine("Dataset.Rows: " + dataset.Rows);
        Console.WriteLine("TrainingPartition: " + problemData.TrainingPartition.Start + ".." + problemData.TrainingPartition.End);
        Console.WriteLine("TestPartition: " + problemData.TestPartition.Start + ".." + problemData.TestPartition.End);
        for (int r = 0; r < 3; r++) {
          var vals = variableNames.Select(v => dataset.GetDoubleValue(v, r));
          Console.WriteLine($"row {r}: " + string.Join(",", vals));
        }
      }

      var grammar = BuildGrammar();

      bool isGpc = string.Equals(o.Variant, "GPC", StringComparison.OrdinalIgnoreCase);

      ISymbolicRegressionSingleObjectiveEvaluator evaluator;
      if (isGpc) {
        var poe = new ParameterOptimizationEvaluator { Iterations = 10 };
        evaluator = poe;
      } else {
        var mse = new SymbolicRegressionSingleObjectiveMeanSquaredErrorEvaluator();
        evaluator = mse;
      }

      // SymbolicRegressionSingleObjectiveProblem sets ApplyLinearScalingParameter = true by default in its ctor.
      // It also hardcodes Maximization = true in the ctor regardless of the evaluator passed in, which is only
      // correct for the default (maximizing) evaluator. Our evaluators (MSE, ParameterOptimizationEvaluator)
      // both minimize (Maximization => false), so this must be corrected explicitly or the GA actively searches
      // for the worst possible fit.
      var problem = new SymbolicRegressionSingleObjectiveProblem(problemData, evaluator, new SymbolicDataAnalysisExpressionTreeCreator());
      problem.Maximization.Value = evaluator.Maximization;
      // Default is SymbolicDataAnalysisExpressionTreeLinearInterpreter (plain managed tree-walking);
      // NativeInterpreter wraps hl-native-interpreter.dll (native C++, already a dependency via
      // ParameterOptimizationEvaluator/GPC) and is faster than either managed interpreter.
      problem.SymbolicExpressionTreeInterpreter = Environment.GetEnvironmentVariable("HL_INTERPRETER") == "default"
        ? (HeuristicLab.Problems.DataAnalysis.Symbolic.ISymbolicDataAnalysisExpressionTreeInterpreter)new SymbolicDataAnalysisExpressionTreeLinearInterpreter()
        : new HeuristicLab.Problems.DataAnalysis.Symbolic.NativeInterpreter();
      problem.SymbolicExpressionTreeGrammar = grammar;
      problem.MaximumSymbolicExpressionTreeLength.Value = 50;
      problem.MaximumSymbolicExpressionTreeDepth.Value = 20;

      var ga = new GeneticAlgorithm { Problem = problem };
      ga.Seed.Value = o.Seed;
      ga.SetSeedRandomly.Value = false;
      ga.PopulationSize.Value = Environment.GetEnvironmentVariable("HL_POPSIZE") != null ? int.Parse(Environment.GetEnvironmentVariable("HL_POPSIZE")) : 1000;
      ga.MaximumGenerations.Value = Environment.GetEnvironmentVariable("HL_GENS") != null ? int.Parse(Environment.GetEnvironmentVariable("HL_GENS")) : (isGpc ? 20 : 200);
      ga.Elites.Value = 1;
      ga.MutationProbability.Value = Environment.GetEnvironmentVariable("HL_MUTATION_PROB") != null ? double.Parse(Environment.GetEnvironmentVariable("HL_MUTATION_PROB"), CultureInfo.InvariantCulture) : 0.15;

      // HL_SELECTOR=random swaps in RandomSelector (uniform, fitness-independent parent choice)
      // in place of the default TournamentSelector(GroupSize=5); anything else (including unset)
      // keeps the default.
      if (Environment.GetEnvironmentVariable("HL_SELECTOR") == "random") {
        var random = ga.SelectorParameter.ValidValues.OfType<RandomSelector>().First();
        ga.Selector = random;
      } else {
        var tournament = ga.SelectorParameter.ValidValues.OfType<TournamentSelector>().First();
        tournament.GroupSizeParameter.Value = new IntValue(5);
        ga.Selector = tournament;
      }

      var subtreeCx = ga.CrossoverParameter.ValidValues.OfType<SubtreeCrossover>().First();
      ga.Crossover = subtreeCx;

      var multiMut = ga.MutatorParameter.ValidValues.OfType<MultiSymbolicExpressionTreeManipulator>().First();
      var allowedMutators = new HashSet<Type> {
        typeof(ReplaceBranchManipulation),
        typeof(FullTreeShaker),
        typeof(OnePointShaker),
        typeof(ChangeNodeTypeManipulation),
        typeof(RemoveBranchManipulation),
      };
      foreach (var op in multiMut.Operators.ToList())
        multiMut.Operators.SetItemCheckedState(op, allowedMutators.Contains(op.GetType()));
      ga.Mutator = multiMut;

      ga.Engine = new SequentialEngine.SequentialEngine();

      // Read back from the live algorithm object right before Start() -- not the intended
      // config value -- so a silent fallback-to-default or a parse failure earlier would show up here.
      Console.WriteLine($"[verify] ga.MutationProbability.Value (read from algorithm object) = {ga.MutationProbability.Value.ToString(CultureInfo.InvariantCulture)}");
      Console.WriteLine($"[verify] ga.Selector (read from algorithm object) = {ga.Selector.GetType().Name}");

      var sw = System.Diagnostics.Stopwatch.StartNew();
      ga.Prepare();
      ga.Start();
      sw.Stop();

      var bestSolution = (ISymbolicRegressionSolution)ga.Results["Best training solution"].Value;
      double trainNmse = bestSolution.TrainingNormalizedMeanSquaredError * 100.0;
      double testNmse = bestSolution.TestNormalizedMeanSquaredError * 100.0;
      var bestTree = bestSolution.Model.SymbolicExpressionTree;
      int modelLength = bestTree.Length;
      int modelDepth = bestTree.Depth;
      // Formatted once and reused for all outputs below (HL_DEBUG console line, the CSV's model
      // column, and --formula-output) since InfixExpressionFormatter.Format is not free to call.
      string modelFormula = new InfixExpressionFormatter().Format(bestTree);
      string escapedModelFormula = "\"" + modelFormula.Replace("\"", "\"\"") + "\"";

      // Train target variance, used to convert the per-generation "Qualities" series (raw MSE, since
      // that's what both SymbolicRegressionSingleObjectiveMeanSquaredErrorEvaluator and
      // ParameterOptimizationEvaluator report as Quality) into NMSE% comparable to trainNmse/testNmse
      // above and to operon's probe output.
      var targetTrain = problemData.TargetVariableTrainingValues.ToArray();
      var targetTrainMean = targetTrain.Average();
      var targetTrainVariance = targetTrain.Select(v => (v - targetTrainMean) * (v - targetTrainMean)).Average();

      if (Environment.GetEnvironmentVariable("HL_DEBUG") == "1") {
        Console.WriteLine("Best tree infix: " + modelFormula);
        Console.WriteLine("Best tree length=" + bestTree.Length + " depth=" + bestTree.Depth);
        Console.WriteLine("Best training solution quality: " + ((HeuristicLab.Data.DoubleValue)ga.Results["Best training solution quality"].Value).Value);
        Console.WriteLine("Best training solution generation: " + ga.Results["Best training solution generation"].Value);
        if (ga.Results.ContainsKey("Generations"))
          Console.WriteLine("Generations executed: " + ga.Results["Generations"].Value);
        if (ga.Results.ContainsKey("Evaluated Solutions"))
          Console.WriteLine("Evaluated Solutions: " + ga.Results["Evaluated Solutions"].Value);
        Console.WriteLine($"Train target mean={targetTrainMean} variance={targetTrainVariance}");
      }

      bool writeHeader = !File.Exists(o.Output);
      using (var w = new StreamWriter(o.Output, append: true)) {
        if (writeHeader)
          w.WriteLine("problem,noise,variant,seed,train_nmse_pct,test_nmse_pct,generations,elapsed_seconds,model_length,model_depth,model");
        w.WriteLine(string.Join(",",
          o.Problem, o.Noise, o.Variant, o.Seed.ToString(CultureInfo.InvariantCulture),
          trainNmse.ToString("R", CultureInfo.InvariantCulture),
          testNmse.ToString("R", CultureInfo.InvariantCulture),
          ga.MaximumGenerations.Value.ToString(CultureInfo.InvariantCulture),
          sw.Elapsed.TotalSeconds.ToString("F2", CultureInfo.InvariantCulture),
          modelLength.ToString(CultureInfo.InvariantCulture),
          modelDepth.ToString(CultureInfo.InvariantCulture),
          escapedModelFormula));
      }

      if (o.FormulaOutput != null) {
        bool writeFormulaHeader = !File.Exists(o.FormulaOutput);
        using (var fw = new StreamWriter(o.FormulaOutput, append: true, Encoding.UTF8)) {
          if (writeFormulaHeader)
            fw.WriteLine("problem,noise,variant,seed,formula");
          fw.WriteLine(string.Join(",",
            o.Problem, o.Noise, o.Variant, o.Seed.ToString(CultureInfo.InvariantCulture),
            escapedModelFormula));
        }
      }

      if (o.GenStatsOutput != null) {
        // Both DataTables come from analyzers that HL's GeneticAlgorithm/SymbolicDataAnalysisProblem
        // already wire up and enable by default (BestAverageWorstQualityAnalyzer and
        // MinAverageMaxSymbolicExpressionTreeLengthAnalyzer) -- no custom instrumentation needed, just
        // reading Results after the run. Quality is raw MSE; converted to NMSE% via targetTrainVariance
        // so it's comparable to trainNmse/testNmse above and to operon's probe output.
        var qualities = (DataTable)ga.Results["Qualities"].Value;
        var bestSeries = qualities.Rows["CurrentBestQuality"].Values;
        var avgSeries = qualities.Rows["CurrentAverageQuality"].Values;
        var worstSeries = qualities.Rows["CurrentWorstQuality"].Values;

        var lengths = (DataTable)ga.Results["Symbolic expression tree length"].Value;
        var minLenSeries = lengths.Rows["Minimal symbolic expression tree length"].Values;
        var avgLenSeries = lengths.Rows["Average symbolic expression tree length"].Values;
        var maxLenSeries = lengths.Rows["Maximal symbolic expression tree length"].Values;

        int nGen = bestSeries.Count;
        bool writeGenStatsHeader = !File.Exists(o.GenStatsOutput);
        using (var gw = new StreamWriter(o.GenStatsOutput, append: true)) {
          if (writeGenStatsHeader)
            gw.WriteLine("problem,noise,variant,seed,generation,fitness_best,fitness_avg,fitness_worst,length_min,length_avg,length_max");
          for (int g = 0; g < nGen; g++) {
            gw.WriteLine(string.Join(",",
              o.Problem, o.Noise, o.Variant, o.Seed.ToString(CultureInfo.InvariantCulture),
              g.ToString(CultureInfo.InvariantCulture),
              (bestSeries[g] / targetTrainVariance * 100.0).ToString("R", CultureInfo.InvariantCulture),
              (avgSeries[g] / targetTrainVariance * 100.0).ToString("R", CultureInfo.InvariantCulture),
              (worstSeries[g] / targetTrainVariance * 100.0).ToString("R", CultureInfo.InvariantCulture),
              minLenSeries[g].ToString("R", CultureInfo.InvariantCulture),
              avgLenSeries[g].ToString("R", CultureInfo.InvariantCulture),
              maxLenSeries[g].ToString("R", CultureInfo.InvariantCulture)));
          }
        }
      }

      if (o.ModelOutput != null) {
        // Persist the whole run (algorithm + problem + results, including the best solution) using
        // HeuristicLab's native HEAL.Attic-based format, the same one ContentManager.Save uses for
        // .hl/.hl.gz files opened from the GUI.
        var dir = Path.GetDirectoryName(o.ModelOutput);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        bool compressed = o.ModelOutput.EndsWith(".gz", StringComparison.OrdinalIgnoreCase);
        ContentManager.Initialize(new PersistenceContentManager());
        ContentManager.Save(ga, o.ModelOutput, compressed);
      }

      Console.WriteLine($"{o.Problem} noise={o.Noise} {o.Variant} seed={o.Seed}: train NMSE%={trainNmse:F4} test NMSE%={testNmse:F4} ({sw.Elapsed.TotalSeconds:F1}s)");
    }
  }
}
