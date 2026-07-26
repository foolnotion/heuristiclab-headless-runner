using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using HeuristicLab.Algorithms.GeneticAlgorithm;
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
          default:
            Console.Error.WriteLine("Unknown argument: " + args[i]);
            return null;
        }
      }
      if (o.TrainCsv == null || o.TestCsv == null || o.Target == null || o.Output == null) {
        Console.Error.WriteLine("Usage: HeuristicLab.HeadlessRunner --train <csv> --test <csv> --target <col> --variant GP|GPC --seed <int> --output <csv> [--problem <name>] [--noise 0|1]");
        return null;
      }
      return o;
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

      // Grammar wiring is two-level: GroupSymbol (e.g. "Trigonometric Functions") gates whether its
      // members are reachable in the grammar's allowed-child rules at all; each individual member
      // symbol also has its own Enabled flag. Both must be true for a function to actually appear
      // in generated trees. ConfigureAsDefaultRegressionGrammar() leaves Arithmetic Functions,
      // Exponential/Logarithmic Functions, Real Valued Symbols and Terminals groups enabled, and
      // disables the Trigonometric/Power/Special/Conditional/TimeSeries groups wholesale, plus
      // Average/Absolute/HyperbolicTangent/Constant individually. We re-enable the specific
      // groups/symbols the paper's function set needs on top of that baseline.
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
      ga.MutationProbability.Value = 0.15;

      var tournament = ga.SelectorParameter.ValidValues.OfType<TournamentSelector>().First();
      tournament.GroupSizeParameter.Value = new IntValue(5);
      ga.Selector = tournament;

      var subtreeCx = ga.CrossoverParameter.ValidValues.OfType<SubtreeCrossover>().First();
      ga.Crossover = subtreeCx;

      var multiMut = ga.MutatorParameter.ValidValues.OfType<MultiSymbolicExpressionTreeManipulator>().First();
      var allowedMutators = new HashSet<Type> {
        typeof(ReplaceBranchManipulation),
        typeof(FullTreeShaker),
        typeof(OnePointShaker),
        typeof(ChangeNodeTypeManipulation),
      };
      foreach (var op in multiMut.Operators.ToList())
        multiMut.Operators.SetItemCheckedState(op, allowedMutators.Contains(op.GetType()));
      ga.Mutator = multiMut;

      ga.Engine = new SequentialEngine.SequentialEngine();

      var sw = System.Diagnostics.Stopwatch.StartNew();
      ga.Prepare();
      ga.Start();
      sw.Stop();

      var bestSolution = (ISymbolicRegressionSolution)ga.Results["Best training solution"].Value;
      double trainNmse = bestSolution.TrainingNormalizedMeanSquaredError * 100.0;
      double testNmse = bestSolution.TestNormalizedMeanSquaredError * 100.0;

      if (Environment.GetEnvironmentVariable("HL_DEBUG") == "1") {
        var tree = bestSolution.Model.SymbolicExpressionTree;
        Console.WriteLine("Best tree infix: " + new InfixExpressionFormatter().Format(tree));
        Console.WriteLine("Best tree length=" + tree.Length + " depth=" + tree.Depth);
        Console.WriteLine("Best training solution quality: " + ((HeuristicLab.Data.DoubleValue)ga.Results["Best training solution quality"].Value).Value);
        Console.WriteLine("Best training solution generation: " + ga.Results["Best training solution generation"].Value);
        if (ga.Results.ContainsKey("Generations"))
          Console.WriteLine("Generations executed: " + ga.Results["Generations"].Value);
        if (ga.Results.ContainsKey("Evaluated Solutions"))
          Console.WriteLine("Evaluated Solutions: " + ga.Results["Evaluated Solutions"].Value);
        var targetTrain = problemData.TargetVariableTrainingValues.ToArray();
        var mean = targetTrain.Average();
        var variance = targetTrain.Select(v => (v - mean) * (v - mean)).Average();
        Console.WriteLine($"Train target mean={mean} variance={variance}");
      }

      bool writeHeader = !File.Exists(o.Output);
      using (var w = new StreamWriter(o.Output, append: true)) {
        if (writeHeader)
          w.WriteLine("problem,noise,variant,seed,train_nmse_pct,test_nmse_pct,generations,elapsed_seconds");
        w.WriteLine(string.Join(",",
          o.Problem, o.Noise, o.Variant, o.Seed.ToString(CultureInfo.InvariantCulture),
          trainNmse.ToString("R", CultureInfo.InvariantCulture),
          testNmse.ToString("R", CultureInfo.InvariantCulture),
          ga.MaximumGenerations.Value.ToString(CultureInfo.InvariantCulture),
          sw.Elapsed.TotalSeconds.ToString("F2", CultureInfo.InvariantCulture)));
      }

      Console.WriteLine($"{o.Problem} noise={o.Noise} {o.Variant} seed={o.Seed}: train NMSE%={trainNmse:F4} test NMSE%={testNmse:F4} ({sw.Elapsed.TotalSeconds:F1}s)");
    }
  }
}
