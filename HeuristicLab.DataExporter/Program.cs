using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using HeuristicLab.Problems.DataAnalysis;
using HeuristicLab.Problems.Instances.DataAnalysis;

namespace HeuristicLab.DataExporter {
  internal static class Program {
    // Feynman-family noise API uses sigma = targetSigma * sqrt(r/(1-r)), which is a
    // different convention than the paper's y' = y + N(0, 0.05*sigma_y). Solving
    // sqrt(r/(1-r)) = 0.05 for r gives r = 0.0025/1.0025 ~= 0.0024938.
    private const double FeynmanNoiseRatio = 0.0025 / 1.0025;

    // Physics-family classes (AircraftLift, FluidDynamics, RocketFuelFlow) already
    // implement y' = y + N(0, 0.05*sigma_y) directly and always generate both the
    // clean and noisy target columns (named "<target>" and "<target>_noise").
    private static readonly Dictionary<string, Func<int, bool, IRegressionProblemData>> Registry =
      new Dictionary<string, Func<int, bool, IRegressionProblemData>>(StringComparer.OrdinalIgnoreCase) {
        { "aircraft_lift", (seed, noisy) => new AircraftLift(seed).GenerateRegressionData() },
        { "flow_psi", (seed, noisy) => new FluidDynamics(seed).GenerateRegressionData() },
        { "fuel_flow", (seed, noisy) => new RocketFuelFlow(seed).GenerateRegressionData() },
        { "jackson_2_11", (seed, noisy) => new FeynmanBonus14(seed, 100, 100, noisy ? (double?)FeynmanNoiseRatio : null).GenerateRegressionData() },
        { "wave_power", (seed, noisy) => new FeynmanBonus4(seed, 100, 100, noisy ? (double?)FeynmanNoiseRatio : null).GenerateRegressionData() },
        { "I.6.20", (seed, noisy) => new Feynman2(seed, 100, 100, noisy ? (double?)FeynmanNoiseRatio : null).GenerateRegressionData() },
        { "I.9.18", (seed, noisy) => new Feynman5(seed, 100, 100, noisy ? (double?)FeynmanNoiseRatio : null).GenerateRegressionData() },
        { "I.15.3x", (seed, noisy) => new Feynman17(seed, 100, 100, noisy ? (double?)FeynmanNoiseRatio : null).GenerateRegressionData() },
        { "I.15.3t", (seed, noisy) => new Feynman18(seed, 100, 100, noisy ? (double?)FeynmanNoiseRatio : null).GenerateRegressionData() },
        { "I.30.5", (seed, noisy) => new Feynman31(seed, 100, 100, noisy ? (double?)FeynmanNoiseRatio : null).GenerateRegressionData() },
        { "I.32.17", (seed, noisy) => new Feynman33(seed, 100, 100, noisy ? (double?)FeynmanNoiseRatio : null).GenerateRegressionData() },
        { "I.41.16", (seed, noisy) => new Feynman44(seed, 100, 100, noisy ? (double?)FeynmanNoiseRatio : null).GenerateRegressionData() },
        { "I.48.20", (seed, noisy) => new Feynman50(seed, 100, 100, noisy ? (double?)FeynmanNoiseRatio : null).GenerateRegressionData() },
        { "II.6.15a", (seed, noisy) => new Feynman56(seed, 100, 100, noisy ? (double?)FeynmanNoiseRatio : null).GenerateRegressionData() },
        { "II.11.27", (seed, noisy) => new Feynman64(seed, 100, 100, noisy ? (double?)FeynmanNoiseRatio : null).GenerateRegressionData() },
        { "II.11.28", (seed, noisy) => new Feynman65(seed, 100, 100, noisy ? (double?)FeynmanNoiseRatio : null).GenerateRegressionData() },
        { "II.35.21", (seed, noisy) => new Feynman81(seed, 100, 100, noisy ? (double?)FeynmanNoiseRatio : null).GenerateRegressionData() },
        { "III.9.52", (seed, noisy) => new Feynman90(seed, 100, 100, noisy ? (double?)FeynmanNoiseRatio : null).GenerateRegressionData() },
        { "III.10.19", (seed, noisy) => new Feynman91(seed, 100, 100, noisy ? (double?)FeynmanNoiseRatio : null).GenerateRegressionData() },
      };

    private static int Main(string[] args) {
      string problem = null, outDir = null;
      int seeds = 30;
      for (int i = 0; i < args.Length; i++) {
        switch (args[i]) {
          case "--problem": problem = args[++i]; break;
          case "--out": outDir = args[++i]; break;
          case "--seeds": seeds = int.Parse(args[++i], CultureInfo.InvariantCulture); break;
          case "--list":
            foreach (var name in Registry.Keys) Console.WriteLine(name);
            return 0;
          default:
            Console.Error.WriteLine("Unknown argument: " + args[i]);
            return 1;
        }
      }
      if (problem == null || outDir == null) {
        Console.Error.WriteLine("Usage: HeuristicLab.DataExporter --problem <name> --out <dir> [--seeds N] | --list");
        return 1;
      }
      if (!Registry.TryGetValue(problem, out var factory)) {
        Console.Error.WriteLine("Unknown problem: " + problem + ". Use --list to see available names.");
        return 1;
      }

      Directory.CreateDirectory(outDir);
      for (int seed = 1; seed <= seeds; seed++) {
        foreach (var noisy in new[] { false, true }) {
          var pd = factory(seed, noisy);
          string targetVar = pd.TargetVariable;
          string noisyCol = targetVar + "_noise";
          if (noisy && pd.Dataset.VariableNames.Contains(noisyCol)) targetVar = noisyCol;

          var inputVars = pd.AllowedInputVariables.ToList();
          int noiseFlag = noisy ? 1 : 0;

          WriteCsv(Path.Combine(outDir, $"seed{seed}_noise{noiseFlag}_train.csv"), pd, inputVars, targetVar,
            Enumerable.Range(pd.TrainingPartition.Start, pd.TrainingPartition.End - pd.TrainingPartition.Start));
          WriteCsv(Path.Combine(outDir, $"seed{seed}_noise{noiseFlag}_test.csv"), pd, inputVars, targetVar,
            Enumerable.Range(pd.TestPartition.Start, pd.TestPartition.End - pd.TestPartition.Start));
        }
      }

      Console.WriteLine($"Exported {seeds} seeds x (noise0/noise1) x (train/test) for {problem} to {outDir}");
      return 0;
    }

    private static void WriteCsv(string path, IRegressionProblemData pd, List<string> inputVars, string targetVar, IEnumerable<int> rows) {
      using (var w = new StreamWriter(path)) {
        w.WriteLine(string.Join(",", inputVars) + ",__target__");
        foreach (var row in rows) {
          var vals = inputVars.Select(v => pd.Dataset.GetDoubleValue(v, row).ToString("R", CultureInfo.InvariantCulture));
          var y = pd.Dataset.GetDoubleValue(targetVar, row).ToString("R", CultureInfo.InvariantCulture);
          w.WriteLine(string.Join(",", vals) + "," + y);
        }
      }
    }
  }
}
