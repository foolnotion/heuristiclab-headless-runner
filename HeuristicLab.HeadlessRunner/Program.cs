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
      public string CrossoverNoopOutput;
      public string CrossoverKernelOutput;
      public string CrossoverDonorOutput;
      public string PopulationSampleOutput;
      public string PopulationSampleGenerations; // comma-separated, e.g. "500,600,700,800,900"
      public string MutationTraceOutput;
      public string CrossoverJoinedOutput;
      public int CrossoverJoinedMinGeneration = 0;
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
          case "--crossover-noop-output": o.CrossoverNoopOutput = args[++i]; break;
          case "--crossover-kernel-output": o.CrossoverKernelOutput = args[++i]; break;
          case "--crossover-donor-output": o.CrossoverDonorOutput = args[++i]; break;
          case "--population-sample-output": o.PopulationSampleOutput = args[++i]; break;
          case "--population-sample-generations": o.PopulationSampleGenerations = args[++i]; break;
          case "--mutation-trace-output": o.MutationTraceOutput = args[++i]; break;
          case "--crossover-joined-output": o.CrossoverJoinedOutput = args[++i]; break;
          case "--crossover-joined-min-generation": o.CrossoverJoinedMinGeneration = int.Parse(args[++i], CultureInfo.InvariantCulture); break;
          default:
            Console.Error.WriteLine("Unknown argument: " + args[i]);
            return null;
        }
      }
      if (o.TrainCsv == null || o.TestCsv == null || o.Target == null || o.Output == null) {
        Console.Error.WriteLine("Usage: HeuristicLab.HeadlessRunner --train <csv> --test <csv> --target <col> --variant GP|GPC --seed <int> --output <csv> [--problem <name>] [--noise 0|1] [--model-output <hl-file>] [--formula-output <csv>] [--gen-stats-output <csv>] [--crossover-noop-output <csv>] [--crossover-kernel-output <csv>] [--crossover-donor-output <csv>] [--population-sample-output <csv> --population-sample-generations <csv-list>]");
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
      public string TreesOutput;
      public string PostfixOutput;
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
          case "--trees-output": o.TreesOutput = args[++i]; break;
          case "--postfix-output": o.PostfixOutput = args[++i]; break;
          case "--reference-csv": o.ReferenceCsv = args[++i]; break;
          case "--target": o.Target = args[++i]; break;
          default:
            Console.Error.WriteLine("Unknown argument: " + args[i]);
            return null;
        }
      }
      if (o.ReferenceCsv == null || o.Target == null || (o.LengthsOutput == null && o.SymbolsOutput == null && o.TreesOutput == null && o.PostfixOutput == null)) {
        Console.Error.WriteLine("Usage: HeuristicLab.HeadlessRunner --mode ptc2sample --count <n> --seed <int> [--max-length <n>] [--max-depth <n>] --reference-csv <csv> --target <col> [--lengths-output <csv>] [--symbols-output <csv>] [--trees-output <txt>] [--postfix-output <txt>]");
        return null;
      }
      return o;
    }

    // Maps this experiment's enabled function symbols to operon's bare lowercase token names, for
    // the --postfix-output dump. Includes a few tokens (sub/tan/cbrt/abs) this experiment's grammar
    // never actually enables, in case PTC2 ever places one, rather than hitting an unmapped symbol.
    private static readonly Dictionary<Type, string> PostfixFunctionTokens = new Dictionary<Type, string> {
      { typeof(HeuristicLab.Problems.DataAnalysis.Symbolic.Addition), "add" },
      { typeof(HeuristicLab.Problems.DataAnalysis.Symbolic.Subtraction), "sub" },
      { typeof(HeuristicLab.Problems.DataAnalysis.Symbolic.Multiplication), "mul" },
      { typeof(HeuristicLab.Problems.DataAnalysis.Symbolic.Division), "div" },
      { typeof(HeuristicLab.Problems.DataAnalysis.Symbolic.Sine), "sin" },
      { typeof(HeuristicLab.Problems.DataAnalysis.Symbolic.Cosine), "cos" },
      { typeof(HeuristicLab.Problems.DataAnalysis.Symbolic.Tangent), "tan" },
      { typeof(HeuristicLab.Problems.DataAnalysis.Symbolic.HyperbolicTangent), "tanh" },
      { typeof(HeuristicLab.Problems.DataAnalysis.Symbolic.Exponential), "exp" },
      { typeof(HeuristicLab.Problems.DataAnalysis.Symbolic.Logarithm), "log" },
      { typeof(HeuristicLab.Problems.DataAnalysis.Symbolic.SquareRoot), "sqrt" },
      { typeof(HeuristicLab.Problems.DataAnalysis.Symbolic.Square), "square" },
      { typeof(HeuristicLab.Problems.DataAnalysis.Symbolic.CubeRoot), "cbrt" },
      { typeof(HeuristicLab.Problems.DataAnalysis.Symbolic.Absolute), "abs" },
    };

    // Lossless postfix token dump for byte-comparable-population seeding on operon's side: skips the
    // ProgramRootSymbol/StartSymbol wrapper nodes (exactly 1 each per tree, not mathematically
    // meaningful), emits real content nodes only, postfix (children before parent). Constant and
    // Number both collapse to a single "C<value>" leaf token (operon has one constant-leaf concept);
    // Variable becomes "V<name>:<weight>" -- same explicit-scalar treatment as Constant/Number,
    // since VariableTreeNodeBase.Weight is a real per-node value assigned at creation time
    // (ResetLocalParameters draws it from Normal(WeightMu, WeightSigma), unlike Constant's Value,
    // which is never locally reset -- see below), and dropping it would silently change the
    // function the tree computes (weight is rarely exactly 1.0). One token per leaf either way, so
    // this doesn't add a node the way an explicit "weight;name;mul" expansion would. Constant's
    // value is always the grammar's shared, never-locally-reset default (0.0 here, since no
    // evaluator has run) -- flagged explicitly in the header comment emitted by RunPtc2Sample below
    // rather than silently treated as a real fitted value.
    private static string FormatPostfix(ISymbolicExpressionTree tree, out int tokenCount) {
      var tokens = new List<string>();
      foreach (var node in tree.Root.IterateNodesPostfix()) {
        var name = node.Symbol.Name;
        if (name == "ProgramRootSymbol" || name == "StartSymbol") continue;
        if (node is HeuristicLab.Problems.DataAnalysis.Symbolic.ConstantTreeNode ct) {
          tokens.Add("C" + ct.Value.ToString("R", CultureInfo.InvariantCulture));
        } else if (node is HeuristicLab.Problems.DataAnalysis.Symbolic.NumberTreeNode nt) {
          tokens.Add("C" + nt.Value.ToString("R", CultureInfo.InvariantCulture));
        } else if (node is HeuristicLab.Problems.DataAnalysis.Symbolic.VariableTreeNode vt) {
          tokens.Add("V" + vt.VariableName + ":" + vt.Weight.ToString("R", CultureInfo.InvariantCulture));
        } else if (PostfixFunctionTokens.TryGetValue(node.Symbol.GetType(), out var token)) {
          tokens.Add(token);
        } else {
          throw new InvalidOperationException($"Unmapped symbol in postfix dump: {node.Symbol.GetType().Name} ({name})");
        }
      }
      tokenCount = tokens.Count;
      return string.Join(";", tokens);
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

      // HL_GRAMMAR=addmul: minimal NaN-safe grammar restricted to Addition/Multiplication only
      // (plus Variable/Constant terminals), for testing whether the degenerate-fitness length
      // skew is specific to domain-violating functions (div/log/sqrt, which can all produce
      // NaN/undefined results) or a more general property of overparameterized local search.
      // Disables every function symbol this method otherwise enables (including the ones
      // ConfigureAsDefaultRegressionGrammar() itself leaves on -- Division, Exponential,
      // Logarithm -- not just the ones added above), leaving only Addition and Multiplication
      // reachable.
      if (Environment.GetEnvironmentVariable("HL_GRAMMAR") == "addmul") {
        grammar.Symbols.First(s => s.Name == TypeCoherentExpressionGrammar.TrigonometricFunctionsName).Enabled = false;
        grammar.Symbols.First(s => s.Name == TypeCoherentExpressionGrammar.PowerFunctionsName).Enabled = false;
        grammar.Symbols.First(s => s is HeuristicLab.Problems.DataAnalysis.Symbolic.Division).Enabled = false;
        grammar.Symbols.First(s => s is HeuristicLab.Problems.DataAnalysis.Symbolic.Exponential).Enabled = false;
        grammar.Symbols.First(s => s is HeuristicLab.Problems.DataAnalysis.Symbolic.Logarithm).Enabled = false;
        grammar.Symbols.First(s => s is HeuristicLab.Problems.DataAnalysis.Symbolic.Square).Enabled = false;
        grammar.Symbols.First(s => s is HeuristicLab.Problems.DataAnalysis.Symbolic.SquareRoot).Enabled = false;
      }

      // HL_DISABLE_NUMBER=1: disables the plain-literal Number terminal (MinValue=-20/MaxValue=20,
      // enabled by default), leaving Constant/Variable as the only two terminal types -- matching
      // operon's terminal set (constant, variable; no separate "plain literal" symbol). The paper's
      // real GPC config has 3 active terminals (Number, Constant, Variable), which was found to fully
      // explain the measured terminal-frequency dilution vs. operon (predicted 2/3 ratio ~matches the
      // measured ~0.68-0.70). This toggle lets that be eliminated directly for a matched-terminal-set
      // rerun, same pattern as the existing HL_GRAMMAR=addmul override above.
      if (Environment.GetEnvironmentVariable("HL_DISABLE_NUMBER") == "1") {
        grammar.Symbols.First(s => s is HeuristicLab.Problems.DataAnalysis.Symbolic.Number).Enabled = false;
      }
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
      var formulas = o.TreesOutput != null ? new List<string>(o.Count) : null;
      var postfixLines = o.PostfixOutput != null ? new List<string>(o.Count) : null;
      var postfixTokenCounts = o.PostfixOutput != null ? new List<int>(o.Count) : null;
      int attempts = 0, failures = 0;
      var formatter = new InfixExpressionFormatter();

      for (int i = 0; i < o.Count; i++) {
        ISymbolicExpressionTree tree = null;
        while (tree == null) {
          attempts++;
          try {
            tree = ProbabilisticTreeCreator.Create(random, grammar, o.MaxLength, o.MaxDepth);
          } catch (Exception ex) {
            failures++;
            if (Environment.GetEnvironmentVariable("HL_DEBUG") == "1")
              Console.WriteLine($"[warn] tree creation attempt {attempts} failed: {ex.GetType().Name}: {ex.Message}");
          }
        }
        lengths.Add(tree.Length);
        foreach (var node in tree.Root.IterateNodesPrefix()) {
          var name = node.Symbol.Name;
          symbolCounts.TryGetValue(name, out var count);
          symbolCounts[name] = count + 1;
        }
        if (formulas != null) formulas.Add(formatter.Format(tree));
        if (postfixLines != null) {
          var line = FormatPostfix(tree, out var tokenCount);
          postfixLines.Add(line);
          postfixTokenCounts.Add(tokenCount);
        }
        if (Environment.GetEnvironmentVariable("HL_DEBUG") == "1" && i < 3) {
          Console.WriteLine($"sample {i}: length={tree.Length} depth={tree.Depth} formula={formatter.Format(tree)}");
        }
      }

      if (o.LengthsOutput != null) {
        using (var lw = new StreamWriter(o.LengthsOutput, append: false)) {
          lw.WriteLine("length");
          foreach (var len in lengths)
            lw.WriteLine(len.ToString(CultureInfo.InvariantCulture));
        }
      }

      if (o.SymbolsOutput != null) {
        long totalNodes = symbolCounts.Values.Sum();
        using (var sw2 = new StreamWriter(o.SymbolsOutput, append: false)) {
          sw2.WriteLine("symbol,count,fraction");
          foreach (var kvp in symbolCounts.OrderBy(kvp => kvp.Key, StringComparer.Ordinal))
            sw2.WriteLine(string.Join(",", kvp.Key, kvp.Value.ToString(CultureInfo.InvariantCulture), (kvp.Value / (double)totalNodes).ToString("R", CultureInfo.InvariantCulture)));
        }
      }

      if (formulas != null) {
        using (var tw = new StreamWriter(o.TreesOutput, append: false)) {
          foreach (var f in formulas)
            tw.WriteLine(f);
        }
      }

      if (postfixLines != null) {
        using (var pw = new StreamWriter(o.PostfixOutput, append: false)) {
          foreach (var l in postfixLines)
            pw.WriteLine(l);
        }
      }

      Console.WriteLine($"[verify] sampled {o.Count} trees via ProbabilisticTreeCreator.Create directly (no GA/selection/crossover/mutation)");
      Console.WriteLine($"[verify] attempts={attempts} failures={failures} (out of {o.Count} requested trees)");
      Console.WriteLine($"[verify] maxLength={o.MaxLength} maxDepth={o.MaxDepth} seed={o.Seed}");
      Console.WriteLine($"[verify] Number.Enabled={grammar.Symbols.First(s => s is HeuristicLab.Problems.DataAnalysis.Symbolic.Number).Enabled} Constant.Enabled={grammar.Symbols.First(s => s is HeuristicLab.Problems.DataAnalysis.Symbolic.Constant).Enabled} (HL_DISABLE_NUMBER={Environment.GetEnvironmentVariable("HL_DISABLE_NUMBER") ?? "unset"})");
      Console.WriteLine($"length min={lengths.Min()} median={Median(lengths):F2} mean={lengths.Average():F2} max={lengths.Max()}");

      if (postfixTokenCounts != null) {
        Console.WriteLine("[verify] postfix token count vs. (tree.Length - 2) for first 5 trees (must match exactly -- wrapper nodes excluded on both sides):");
        for (int i = 0; i < Math.Min(5, lengths.Count); i++) {
          int expected = lengths[i] - 2;
          bool ok = expected == postfixTokenCounts[i];
          Console.WriteLine($"  tree {i}: tree.Length={lengths[i]} tree.Length-2={expected} postfixTokens={postfixTokenCounts[i]} match={ok}");
        }
        int mismatches = 0;
        for (int i = 0; i < lengths.Count; i++)
          if (lengths[i] - 2 != postfixTokenCounts[i]) mismatches++;
        Console.WriteLine($"[verify] postfix token count mismatches across all {lengths.Count} trees: {mismatches}");
      }
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

      bool evalFree = Environment.GetEnvironmentVariable("HL_EVAL_FREE") == "1";

      ISymbolicRegressionSingleObjectiveEvaluator evaluator;
      if (evalFree) {
        // Skips the real evaluation/LM step entirely -- Quality becomes cheap uniform-random
        // noise, for isolating crossover/reinsertion structural dynamics from fitness-driven
        // dynamics at a fraction of the per-generation cost. See PlaceholderEvaluator.cs.
        evaluator = new PlaceholderEvaluator();
      } else if (isGpc && Environment.GetEnvironmentVariable("HL_LM_SCALE") == "1") {
        // Scales the LM iteration budget by parameter count (maxIterations = 10*(k+1)), mirroring
        // operon's own maxfev = iterations*(n_params+1) convention, to test whether HL's flat
        // maxIterations=10 (regardless of k) is what pushes larger trees toward the degenerate
        // quality=0 floor more often. See ScaledParameterOptimizationEvaluator.cs.
        evaluator = new ScaledParameterOptimizationEvaluator();
      } else if (isGpc) {
        // The paper's actual .hl files used SymbolicRegressionParameterOptimizationEvaluator
        // (ALGLIB+AutoDiff Levenberg-Marquardt) -- confirmed via its AfterDeserialization backward-
        // compat shim, which renames a legacy "ConstantOptimizationIterations" parameter to today's
        // "ParameterOptimizationIterations", matching the .hl dump's actual saved parameter names
        // exactly (also "Count Function and Gradient Evaluations", same capitalization). The newer
        // ParameterOptimizationEvaluator (native-interpreter LM) is marked [Obsolete("Use
        // ParameterOptimizationEvaluator instead")] on the OLD class -- i.e. it's each *older* HL
        // build's evaluator that was later superseded by the native one; the .hl files predate that
        // switch. Default-constructed parameters already match the .hl config (Iterations=10,
        // Probability=1, RowsPercentage=1, UpdateVariableWeights=true) -- no explicit overrides needed.
        var poe = new SymbolicRegressionParameterOptimizationEvaluator();
        evaluator = poe;
      } else {
        var mse = new SymbolicRegressionSingleObjectiveMeanSquaredErrorEvaluator();
        evaluator = mse;
      }

      // SymbolicRegressionSingleObjectiveProblem sets ApplyLinearScalingParameter = true by default in its ctor.
      // It also hardcodes Maximization = true in the ctor regardless of the evaluator passed in, which is only
      // correct for the default (maximizing) evaluator. Our GP evaluator (MSE) minimizes (Maximization =>
      // false) while our GPC evaluator (SymbolicRegressionParameterOptimizationEvaluator, Pearson R^2) maximizes
      // (Maximization => true) -- this line reads the correct direction from whichever evaluator was
      // selected above rather than hardcoding either, since the two variants now differ.
      // HL_SEED_POPULATION=<path>: bypasses the normal PTC2-based SolutionCreator entirely for
      // generation 0, injecting a pre-parsed population from a --postfix-output-format file instead
      // (see PostfixTreeParser.cs/SeededTreeCreator.cs) -- for byte-identical-population comparisons
      // against another engine seeded from the same file. Everything else in the operator graph
      // (Selector/Crossover/Mutator/Evaluator/Elites) is untouched; only initial population creation
      // is replaced.
      string seedPopulationPath = Environment.GetEnvironmentVariable("HL_SEED_POPULATION");
      ISymbolicDataAnalysisSolutionCreator treeCreator;
      List<ISymbolicExpressionTree> seedTrees = null;
      if (seedPopulationPath != null) {
        seedTrees = PostfixTreeParser.ParseFile(seedPopulationPath, grammar);
        SeededTreeCreator.SeedPopulation = seedTrees;
        SeededTreeCreator.NextIndex = 0;
        treeCreator = new SeededTreeCreator();

        Console.WriteLine($"[verify] HL_SEED_POPULATION={seedPopulationPath}: parsed {seedTrees.Count} trees");
        for (int i = 0; i < Math.Min(5, seedTrees.Count); i++) {
          int contentTokens = File.ReadLines(seedPopulationPath).Skip(i).First().Split(';').Length;
          Console.WriteLine($"  tree {i}: tree.Length={seedTrees[i].Length} (tree.Length-2={seedTrees[i].Length - 2}) sourceLineTokenCount={contentTokens} match={seedTrees[i].Length - 2 == contentTokens}");
        }
      } else {
        treeCreator = new SymbolicDataAnalysisExpressionTreeCreator();
      }

      var problem = new SymbolicRegressionSingleObjectiveProblem(problemData, evaluator, treeCreator);
      problem.Maximization.Value = evaluator.Maximization;
      // Default is SymbolicDataAnalysisExpressionTreeLinearInterpreter (plain managed tree-walking);
      // NativeInterpreter wraps hl-native-interpreter.dll (native C++, already a dependency via
      // ParameterOptimizationEvaluator/GPC) and is faster than either managed interpreter.
      problem.SymbolicExpressionTreeInterpreter = Environment.GetEnvironmentVariable("HL_INTERPRETER") == "default"
        ? (HeuristicLab.Problems.DataAnalysis.Symbolic.ISymbolicDataAnalysisExpressionTreeInterpreter)new SymbolicDataAnalysisExpressionTreeLinearInterpreter()
        : new HeuristicLab.Problems.DataAnalysis.Symbolic.NativeInterpreter();
      problem.SymbolicExpressionTreeGrammar = grammar;
      problem.MaximumSymbolicExpressionTreeLength.Value = Environment.GetEnvironmentVariable("HL_MAXLENGTH") != null ? int.Parse(Environment.GetEnvironmentVariable("HL_MAXLENGTH"), CultureInfo.InvariantCulture) : 50;
      problem.MaximumSymbolicExpressionTreeDepth.Value = Environment.GetEnvironmentVariable("HL_MAXDEPTH") != null ? int.Parse(Environment.GetEnvironmentVariable("HL_MAXDEPTH"), CultureInfo.InvariantCulture) : 20;

      var ga = new GeneticAlgorithm { Problem = problem };
      ga.Seed.Value = o.Seed;
      ga.SetSeedRandomly.Value = false;
      ga.PopulationSize.Value = Environment.GetEnvironmentVariable("HL_POPSIZE") != null ? int.Parse(Environment.GetEnvironmentVariable("HL_POPSIZE")) : 1000;
      if (seedTrees != null && seedTrees.Count != ga.PopulationSize.Value)
        throw new InvalidOperationException($"HL_SEED_POPULATION file has {seedTrees.Count} trees but PopulationSize={ga.PopulationSize.Value} -- these must match exactly.");
      ga.MaximumGenerations.Value = Environment.GetEnvironmentVariable("HL_GENS") != null ? int.Parse(Environment.GetEnvironmentVariable("HL_GENS")) : (isGpc ? 20 : 200);
      ga.Elites.Value = Environment.GetEnvironmentVariable("HL_ELITES") != null ? int.Parse(Environment.GetEnvironmentVariable("HL_ELITES")) : 1;
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
      // HL_MUTATOR_SET=<comma-list>: restricts the enabled mutator subset to exactly the given
      // tokens, for isolating each operator's structural contribution one at a time (e.g. the
      // cumulative single-generation ablation: onepoint -> +changetype -> +fulltree -> +replace ->
      // +remove). Unset keeps the existing default (all 5, the full set used everywhere else in
      // this investigation).
      var mutatorTokenMap = new Dictionary<string, Type> {
        { "onepoint", typeof(OnePointShaker) },
        { "changetype", typeof(ChangeNodeTypeManipulation) },
        { "fulltree", typeof(FullTreeShaker) },
        { "replace", typeof(ReplaceBranchManipulation) },
        { "remove", typeof(RemoveBranchManipulation) },
      };
      HashSet<Type> allowedMutators;
      string mutatorSetEnv = Environment.GetEnvironmentVariable("HL_MUTATOR_SET");
      if (mutatorSetEnv != null) {
        allowedMutators = new HashSet<Type>();
        foreach (var tok in mutatorSetEnv.Split(',')) {
          if (tok.Length == 0) continue;
          if (!mutatorTokenMap.TryGetValue(tok.Trim(), out var t))
            throw new InvalidOperationException($"Unknown HL_MUTATOR_SET token '{tok}' -- valid tokens: {string.Join(",", mutatorTokenMap.Keys)}");
          allowedMutators.Add(t);
        }
      } else {
        allowedMutators = new HashSet<Type>(mutatorTokenMap.Values);
      }
      foreach (var op in multiMut.Operators.ToList())
        multiMut.Operators.SetItemCheckedState(op, allowedMutators.Contains(op.GetType()));
      ga.Mutator = multiMut;
      Console.WriteLine($"[verify] enabled mutators = {string.Join(",", multiMut.Operators.CheckedItems.Select(x => x.Value.GetType().Name))} (HL_MUTATOR_SET={mutatorSetEnv ?? "unset (default: all 5)"})");

      // TEMPORARY diagnostic, not a permanent feature: HL_MUTATION_TRACE=1 enables
      // SymbolicExpressionTreeManipulator.LengthLog (see the ad-hoc, uncommitted patch to that file)
      // to directly test each manipulator's structural size-neutrality -- same tree object, length
      // read immediately before/after the Manipulate() call, zero RNG-order confound.
      bool mutationTrace = Environment.GetEnvironmentVariable("HL_MUTATION_TRACE") == "1";
      if (mutationTrace) HeuristicLab.Encodings.SymbolicExpressionTreeEncoding.SymbolicExpressionTreeManipulator.LengthLog = new List<Tuple<string, int, int>>();

      // Adds a per-individual (length, quality) population sample at configured generations,
      // via HL's own ScopeTreeLookupParameter mechanism (the same one BestAverageWorstQualityAnalyzer/
      // MinAverageMaxSymbolicExpressionTreeLengthAnalyzer already use to reach every individual) --
      // no HeuristicLab source changes needed for this one, entirely in PopulationSampleAnalyzer.cs.
      if (o.PopulationSampleOutput != null) {
        var sampleAnalyzer = new PopulationSampleAnalyzer();
        ga.Analyzer.Operators.Add(sampleAnalyzer);
        ga.Analyzer.Operators.SetItemCheckedState(sampleAnalyzer, true);
        PopulationSampleAnalyzer.TargetGenerations = new HashSet<int>(
          (o.PopulationSampleGenerations ?? "").Split(',').Where(s => s.Length > 0).Select(int.Parse));
        PopulationSampleAnalyzer.Log = new List<Tuple<int, int, int, int, int, double>>();
      }

      // Intervention B: purges Quality<=0 (degenerate) individuals every generation, replacing
      // each with a freshly PTC2-created individual (re-evaluated with this run's real
      // evaluator, retried up to MaxRetries times if the replacement is itself degenerate) --
      // testing the retention side of the degenerate-mass-inflation finding directly, independent
      // of Intervention A's (refuted) LM-budget-scaling theory. No HL source changes needed.
      if (Environment.GetEnvironmentVariable("HL_PURGE_DEGENERATE") == "1") {
        var purgeAnalyzer = new PurgeDegenerateAnalyzer();
        ga.Analyzer.Operators.Add(purgeAnalyzer);
        ga.Analyzer.Operators.SetItemCheckedState(purgeAnalyzer, true);
        PurgeDegenerateAnalyzer.Enabled = true;
        PurgeDegenerateAnalyzer.Random = new MersenneTwister((uint)o.Seed);
        PurgeDegenerateAnalyzer.Grammar = grammar;
        PurgeDegenerateAnalyzer.MaxLength = problem.MaximumSymbolicExpressionTreeLength.Value;
        PurgeDegenerateAnalyzer.MaxDepth = problem.MaximumSymbolicExpressionTreeDepth.Value;
        PurgeDegenerateAnalyzer.ProblemData = problemData;
        PurgeDegenerateAnalyzer.Interpreter = problem.SymbolicExpressionTreeInterpreter;
        PurgeDegenerateAnalyzer.ApplyLinearScaling = problem.ApplyLinearScalingParameter.Value.Value;
        PurgeDegenerateAnalyzer.LowerEstimationLimit = problem.EstimationLimits.Lower;
        PurgeDegenerateAnalyzer.UpperEstimationLimit = problem.EstimationLimits.Upper;
        PurgeDegenerateAnalyzer.PurgeCount = 0;
        PurgeDegenerateAnalyzer.FellBackToStillDegenerateCount = 0;
        PurgeDegenerateAnalyzer.NoSurvivorFallbackCount = 0;
      }

      ga.Engine = new SequentialEngine.SequentialEngine();

      // TEMPORARY debug instrumentation (SubtreeCrossover.NoOpLog is a local-only field on this
      // checkout's HeuristicLab.Encodings.SymbolicExpressionTreeEncoding build, not committed to
      // heal-research/HeuristicLab): captures (parent0 length, is-noop) for every Cross() call
      // this run, only when --crossover-noop-output was requested.
      if (o.CrossoverNoopOutput != null)
        SubtreeCrossover.NoOpLog = new List<Tuple<int, bool>>();
      // Same instrumentation family: (parent0 length, removed-subtree length) for the excision side
      // of every Cross() call, for building an empirical crossover node-selection kernel.
      if (o.CrossoverKernelOutput != null)
        SubtreeCrossover.KernelLog = new List<Tuple<int, int>>();
      // Same instrumentation family: (parent1/donor length, inserted-branch length) for the donor
      // side of every Cross() call, (-1,-1) for no-op calls -- for the donor-side kernel-symmetry
      // check (does donor tree size correlate with the excised donor branch size).
      if (o.CrossoverDonorOutput != null)
        SubtreeCrossover.DonorLog = new List<Tuple<int, int>>();
      // Same instrumentation family: (parent0 length, removed length, parent1/donor length,
      // inserted length) for BOTH sides of every Cross() call in one row -- unlike KernelLog/
      // DonorLog, which need reconstructing via NoOpLog to join correctly since the donor CSV
      // writer skips no-op rows. insertedLength = -1 for no-ops.
      if (o.CrossoverJoinedOutput != null)
        SubtreeCrossover.JoinedLog = new List<Tuple<int, int, int, int, int, int>>();

      // Read back from the live algorithm object right before Start() -- not the intended
      // config value -- so a silent fallback-to-default or a parse failure earlier would show up here.
      Console.WriteLine($"[verify] ga.Elites.Value (read from algorithm object) = {ga.Elites.Value.ToString(CultureInfo.InvariantCulture)}");
      Console.WriteLine($"[verify] ga.MutationProbability.Value (read from algorithm object) = {ga.MutationProbability.Value.ToString(CultureInfo.InvariantCulture)}");
      Console.WriteLine($"[verify] ga.Selector (read from algorithm object) = {ga.Selector.GetType().Name}");
      Console.WriteLine($"[verify] ga.Problem.Evaluator (read from algorithm object) = {problem.Evaluator.GetType().Name}, Maximization = {problem.Maximization.Value}");
      Console.WriteLine($"[verify] Number.Enabled (read from grammar) = {grammar.Symbols.First(s => s is HeuristicLab.Problems.DataAnalysis.Symbolic.Number).Enabled} (HL_DISABLE_NUMBER={Environment.GetEnvironmentVariable("HL_DISABLE_NUMBER") ?? "unset"})");

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

      // Train target variance, used to convert the per-generation "Qualities" series into NMSE%
      // comparable to trainNmse/testNmse above and to operon's probe output. GP's evaluator
      // (SymbolicRegressionSingleObjectiveMeanSquaredErrorEvaluator) reports raw MSE as Quality
      // (minimized); GPC's evaluator (SymbolicRegressionParameterOptimizationEvaluator) reports
      // Pearson R^2 as Quality (maximized) -- with ApplyLinearScaling=true (the model's own optimal
      // affine correction already applied), R^2 = 1 - NMSE exactly, so NMSE% = (1 - R^2) * 100.
      // ToNmsePercent below picks the right conversion from evaluator.Maximization.
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
        // reading Results after the run.
        double ToNmsePercent(double quality) =>
          evaluator.Maximization
            ? (1.0 - quality) * 100.0   // GPC: Quality is Pearson R^2 (maximized); NMSE% = (1 - R^2) * 100
            : quality / targetTrainVariance * 100.0; // GP: Quality is raw MSE (minimized)

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
              ToNmsePercent(bestSeries[g]).ToString("R", CultureInfo.InvariantCulture),
              ToNmsePercent(avgSeries[g]).ToString("R", CultureInfo.InvariantCulture),
              ToNmsePercent(worstSeries[g]).ToString("R", CultureInfo.InvariantCulture),
              minLenSeries[g].ToString("R", CultureInfo.InvariantCulture),
              avgLenSeries[g].ToString("R", CultureInfo.InvariantCulture),
              maxLenSeries[g].ToString("R", CultureInfo.InvariantCulture)));
          }
        }
      }

      if (o.CrossoverNoopOutput != null) {
        // Generation is inferred from call index, not tracked live: with CrossoverProbability=1.0
        // (never skipped) and the plain GeneticAlgorithm's fixed offspring count of
        // PopulationSize-Elites children per generation (GeneticAlgorithm.cs: selector selects
        // 2*(PopulationSize-Elites) parents, ChildrenCreator pairs them 1:1 into children, each
        // gets exactly one Cross() call), calls per generation is constant and known ahead of time.
        int callsPerGeneration = ga.PopulationSize.Value - ga.Elites.Value;
        var log = SubtreeCrossover.NoOpLog;
        bool writeNoopHeader = !File.Exists(o.CrossoverNoopOutput);
        using (var nw = new StreamWriter(o.CrossoverNoopOutput, append: true)) {
          if (writeNoopHeader)
            nw.WriteLine("problem,noise,variant,seed,generation,parent0_length,is_noop");
          for (int i = 0; i < log.Count; i++) {
            int generation = i / callsPerGeneration;
            nw.WriteLine(string.Join(",",
              o.Problem, o.Noise, o.Variant, o.Seed.ToString(CultureInfo.InvariantCulture),
              generation.ToString(CultureInfo.InvariantCulture),
              log[i].Item1.ToString(CultureInfo.InvariantCulture),
              log[i].Item2 ? "1" : "0"));
          }
        }
        Console.WriteLine($"[verify] crossover calls logged = {log.Count} (expected {callsPerGeneration} x {ga.MaximumGenerations.Value} generations = {callsPerGeneration * ga.MaximumGenerations.Value})");
      }

      if (o.CrossoverKernelOutput != null) {
        // Same generation-inference reasoning as --crossover-noop-output above.
        int callsPerGeneration = ga.PopulationSize.Value - ga.Elites.Value;
        var kernelLog = SubtreeCrossover.KernelLog;
        bool writeKernelHeader = !File.Exists(o.CrossoverKernelOutput);
        using (var kw = new StreamWriter(o.CrossoverKernelOutput, append: true)) {
          if (writeKernelHeader)
            kw.WriteLine("problem,noise,variant,seed,generation,parent_length,removed_length");
          for (int i = 0; i < kernelLog.Count; i++) {
            int generation = i / callsPerGeneration;
            kw.WriteLine(string.Join(",",
              o.Problem, o.Noise, o.Variant, o.Seed.ToString(CultureInfo.InvariantCulture),
              generation.ToString(CultureInfo.InvariantCulture),
              kernelLog[i].Item1.ToString(CultureInfo.InvariantCulture),
              kernelLog[i].Item2.ToString(CultureInfo.InvariantCulture)));
          }
        }
        Console.WriteLine($"[verify] crossover kernel events logged = {kernelLog.Count} (expected {callsPerGeneration} x {ga.MaximumGenerations.Value} generations = {callsPerGeneration * ga.MaximumGenerations.Value})");
      }

      if (o.CrossoverDonorOutput != null) {
        // Same generation-inference reasoning as --crossover-noop-output above. Row index i still
        // lines up 1:1 with call index (DonorLog logs a (-1,-1) sentinel for no-op calls rather
        // than skipping them), so generation math is unaffected -- only the (-1,-1) rows themselves
        // are skipped when writing, since there's no real donor-branch event to report for them.
        int callsPerGeneration = ga.PopulationSize.Value - ga.Elites.Value;
        var donorLog = SubtreeCrossover.DonorLog;
        bool writeDonorHeader = !File.Exists(o.CrossoverDonorOutput);
        int noopSkipped = 0;
        using (var dw = new StreamWriter(o.CrossoverDonorOutput, append: true)) {
          if (writeDonorHeader)
            dw.WriteLine("problem,noise,variant,seed,generation,donor_length,inserted_length");
          for (int i = 0; i < donorLog.Count; i++) {
            if (donorLog[i].Item1 < 0) { noopSkipped++; continue; }
            int generation = i / callsPerGeneration;
            dw.WriteLine(string.Join(",",
              o.Problem, o.Noise, o.Variant, o.Seed.ToString(CultureInfo.InvariantCulture),
              generation.ToString(CultureInfo.InvariantCulture),
              donorLog[i].Item1.ToString(CultureInfo.InvariantCulture),
              donorLog[i].Item2.ToString(CultureInfo.InvariantCulture)));
          }
        }
        Console.WriteLine($"[verify] crossover donor events logged = {donorLog.Count - noopSkipped} (of {donorLog.Count} total calls, {noopSkipped} were no-ops and skipped; expected {callsPerGeneration} x {ga.MaximumGenerations.Value} generations = {callsPerGeneration * ga.MaximumGenerations.Value} total calls)");
      }

      if (o.CrossoverJoinedOutput != null) {
        // Both excision and donor side in one row per call, always (including no-ops, with
        // inserted_length=-1) -- avoids the join-via-NoOpLog reconstruction --crossover-kernel-output
        // / --crossover-donor-output need when both sides are wanted together. Filtered to
        // generation >= CrossoverJoinedMinGeneration (skip the burn-in transient) since this is
        // typically used for equilibrium-region kernel dumps over many generations.
        int callsPerGeneration = ga.PopulationSize.Value - ga.Elites.Value;
        var joinedLog = SubtreeCrossover.JoinedLog;
        bool writeJoinedHeader = !File.Exists(o.CrossoverJoinedOutput);
        int written = 0;
        using (var jw = new StreamWriter(o.CrossoverJoinedOutput, append: true)) {
          if (writeJoinedHeader)
            jw.WriteLine("problem,noise,variant,seed,generation,parent_length,removed_length,donor_length,inserted_length,parent_depth,donor_depth");
          for (int i = 0; i < joinedLog.Count; i++) {
            int generation = i / callsPerGeneration;
            if (generation < o.CrossoverJoinedMinGeneration) continue;
            jw.WriteLine(string.Join(",",
              o.Problem, o.Noise, o.Variant, o.Seed.ToString(CultureInfo.InvariantCulture),
              generation.ToString(CultureInfo.InvariantCulture),
              joinedLog[i].Item1.ToString(CultureInfo.InvariantCulture),
              joinedLog[i].Item2.ToString(CultureInfo.InvariantCulture),
              joinedLog[i].Item3.ToString(CultureInfo.InvariantCulture),
              joinedLog[i].Item4.ToString(CultureInfo.InvariantCulture),
              joinedLog[i].Item5.ToString(CultureInfo.InvariantCulture),
              joinedLog[i].Item6.ToString(CultureInfo.InvariantCulture)));
            written++;
          }
        }
        Console.WriteLine($"[verify] crossover joined events logged = {written} (of {joinedLog.Count} total calls, filtered to generation >= {o.CrossoverJoinedMinGeneration})");
      }

      if (o.PopulationSampleOutput != null) {
        var sampleLog = PopulationSampleAnalyzer.Log;
        bool writeSampleHeader = !File.Exists(o.PopulationSampleOutput);
        using (var psw = new StreamWriter(o.PopulationSampleOutput, append: true)) {
          if (writeSampleHeader)
            psw.WriteLine("problem,noise,variant,seed,generation,individual_index,raw_length,bare_length,raw_depth,bare_depth,terminal_count,quality");
          foreach (var row in sampleLog) {
            int generation = row.Item1, index = row.Item2, rawLength = row.Item3, rawDepth = row.Item4, terminalCount = row.Item5;
            double quality = row.Item6;
            psw.WriteLine(string.Join(",",
              o.Problem, o.Noise, o.Variant, o.Seed.ToString(CultureInfo.InvariantCulture),
              generation.ToString(CultureInfo.InvariantCulture),
              index.ToString(CultureInfo.InvariantCulture),
              rawLength.ToString(CultureInfo.InvariantCulture),
              (rawLength - 2).ToString(CultureInfo.InvariantCulture),
              rawDepth.ToString(CultureInfo.InvariantCulture),
              (rawDepth - 2).ToString(CultureInfo.InvariantCulture),
              terminalCount.ToString(CultureInfo.InvariantCulture),
              quality.ToString("R", CultureInfo.InvariantCulture)));
          }
        }
        Console.WriteLine($"[verify] population samples logged = {sampleLog.Count} across {PopulationSampleAnalyzer.TargetGenerations.Count} target generations (expect {ga.PopulationSize.Value} individuals x {PopulationSampleAnalyzer.TargetGenerations.Count} generations = {ga.PopulationSize.Value * PopulationSampleAnalyzer.TargetGenerations.Count}, if all target generations were reached)");
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

      if (PurgeDegenerateAnalyzer.Enabled)
        Console.WriteLine($"[verify] degenerate purges = {PurgeDegenerateAnalyzer.PurgeCount} (parent-swap replacement; no-survivor PTC2 fallback {PurgeDegenerateAnalyzer.NoSurvivorFallbackCount} times, of which still-degenerate after fallback {PurgeDegenerateAnalyzer.FellBackToStillDegenerateCount} times; Apply() called {PurgeDegenerateAnalyzer.ApplyCallCount} times, saw {PurgeDegenerateAnalyzer.IndividualsSeenCount} individual-slots total)");

      if (mutationTrace) {
        var log = HeuristicLab.Encodings.SymbolicExpressionTreeEncoding.SymbolicExpressionTreeManipulator.LengthLog;
        Console.WriteLine($"[verify] mutation trace: {log.Count} manipulator invocations total");
        foreach (var grp in log.GroupBy(t => t.Item1)) {
          int total = grp.Count();
          var changedList = grp.Where(t => t.Item2 != t.Item3).ToList();
          int changed = changedList.Count;
          Console.WriteLine($"  {grp.Key}: {total} invocations, {changed} changed tree length (size-neutral in {total - changed}/{total})");
          if (changed > 0) {
            double meanDelta = changedList.Average(t => (double)(t.Item3 - t.Item2));
            double meanAbsDelta = changedList.Average(t => (double)Math.Abs(t.Item3 - t.Item2));
            Console.WriteLine($"    among changed: mean delta (after-before) = {meanDelta.ToString("F3", CultureInfo.InvariantCulture)}, mean |delta| = {meanAbsDelta.ToString("F3", CultureInfo.InvariantCulture)}");
          }
          foreach (var t in grp.Where(t => t.Item2 != t.Item3).Take(3))
            Console.WriteLine($"    example: lengthBefore={t.Item2} lengthAfter={t.Item3}");
        }

        if (o.MutationTraceOutput != null) {
          bool writeTraceHeader = !File.Exists(o.MutationTraceOutput);
          using (var mtw = new StreamWriter(o.MutationTraceOutput, append: true)) {
            if (writeTraceHeader)
              mtw.WriteLine("problem,noise,variant,seed,manipulator,length_before,length_after");
            foreach (var t in log)
              mtw.WriteLine(string.Join(",", o.Problem, o.Noise, o.Variant, o.Seed.ToString(CultureInfo.InvariantCulture), t.Item1, t.Item2.ToString(CultureInfo.InvariantCulture), t.Item3.ToString(CultureInfo.InvariantCulture)));
          }
        }
      }

      Console.WriteLine($"{o.Problem} noise={o.Noise} {o.Variant} seed={o.Seed}: train NMSE%={trainNmse:F4} test NMSE%={testNmse:F4} ({sw.Elapsed.TotalSeconds:F1}s)");
    }
  }
}
