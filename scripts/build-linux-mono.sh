#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
RUNNER_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
HL_ROOT="${1:-${RUNNER_ROOT}/../HeuristicLab}"

if [ ! -d "${HL_ROOT}/.git" ]; then
  echo "HeuristicLab checkout not found: ${HL_ROOT}" >&2
  echo "Usage: scripts/build-linux-mono.sh [path-to-HeuristicLab]" >&2
  exit 1
fi

if ! command -v mono >/dev/null 2>&1 || ! command -v xbuild >/dev/null 2>&1 || ! command -v msbuild >/dev/null 2>&1; then
  echo "mono, xbuild, and msbuild are required. Use: nix develop" >&2
  exit 1
fi

build_xbuild() {
  local project="$1"
  xbuild "$project" \
    /p:Configuration=Release \
    /p:Platform="AnyCPU" \
    /p:SolutionDir="${HL_ROOT}/" \
    /p:RestorePackages=false \
    /verbosity:minimal
}

mkdir -p \
  "${RUNNER_ROOT}/HeuristicLab.HeadlessRunner/Properties" \
  "${RUNNER_ROOT}/HeuristicLab.DataExporter/Properties"

if [ ! -f "${RUNNER_ROOT}/HeuristicLab.HeadlessRunner/Properties/AssemblyInfo.cs" ]; then
  cat > "${RUNNER_ROOT}/HeuristicLab.HeadlessRunner/Properties/AssemblyInfo.cs" <<'CS'
using System.Reflection;
using System.Runtime.InteropServices;

[assembly: AssemblyTitle("HeuristicLab.HeadlessRunner")]
[assembly: AssemblyProduct("HeuristicLab.HeadlessRunner")]
[assembly: ComVisible(false)]
[assembly: Guid("b7b1b7b1-0001-4c1a-9c1a-000000000001")]
CS
fi

if [ ! -f "${RUNNER_ROOT}/HeuristicLab.DataExporter/Properties/AssemblyInfo.cs" ]; then
  cat > "${RUNNER_ROOT}/HeuristicLab.DataExporter/Properties/AssemblyInfo.cs" <<'CS'
using System.Reflection;
using System.Runtime.InteropServices;

[assembly: AssemblyTitle("HeuristicLab.DataExporter")]
[assembly: AssemblyProduct("HeuristicLab.DataExporter")]
[assembly: ComVisible(false)]
[assembly: Guid("b7b1b7b1-0002-4c1a-9c1a-000000000002")]
CS
fi

msbuild "${HL_ROOT}/HeuristicLab.ExtLibs/HeuristicLab.Attic/1.0.0/HeuristicLab.Attic.csproj" \
  /restore \
  /p:Configuration=Release \
  /p:Platform="AnyCPU" \
  /p:SolutionDir="${HL_ROOT}/" \
  /verbosity:minimal

mkdir -p "${HL_ROOT}/bin"

if [ ! -f "${HL_ROOT}/bin/HEAL.Attic.dll" ]; then
  cp "${HOME}/.nuget/packages/heal.attic/1.5.0/lib/net461/HEAL.Attic.dll" "${HL_ROOT}/bin/HEAL.Attic.dll"
fi

if [ ! -f "${HL_ROOT}/bin/Google.Protobuf.dll" ]; then
  cp "${HOME}/.nuget/packages/google.protobuf/3.6.1/lib/net45/Google.Protobuf.dll" "${HL_ROOT}/bin/Google.Protobuf.dll"
fi

cd "${HL_ROOT}"
build_xbuild "HeuristicLab.ExtLibs/HeuristicLab.ALGLIB/3.17.0/ALGLIB-3.17.0/ALGLIB-3.17.0.csproj"
build_xbuild "HeuristicLab.ExtLibs/HeuristicLab.AutoDiff/1.0/AutoDiff-1.0/AutoDiff-1.0.csproj"
build_xbuild "HeuristicLab.Common/3.3/HeuristicLab.Common-3.3.csproj"
build_xbuild "HeuristicLab.Collections/3.3/HeuristicLab.Collections-3.3.csproj"
build_xbuild "HeuristicLab.Core/3.3/HeuristicLab.Core-3.3.csproj"
build_xbuild "HeuristicLab.Data/3.3/HeuristicLab.Data-3.3.csproj"
build_xbuild "HeuristicLab.Parameters/3.3/HeuristicLab.Parameters-3.3.csproj"
build_xbuild "HeuristicLab.Random/3.3/HeuristicLab.Random-3.3.csproj"
build_xbuild "HeuristicLab.Operators/3.3/HeuristicLab.Operators-3.3.csproj"
build_xbuild "HeuristicLab.Analysis/3.3/HeuristicLab.Analysis-3.3.csproj"
build_xbuild "HeuristicLab.Optimization/3.3/HeuristicLab.Optimization-3.3.csproj"
build_xbuild "HeuristicLab.Optimization.Operators/3.3/HeuristicLab.Optimization.Operators-3.3.csproj"
build_xbuild "HeuristicLab.Selection/3.3/HeuristicLab.Selection-3.3.csproj"
build_xbuild "HeuristicLab.Encodings.RealVectorEncoding/3.3/HeuristicLab.Encodings.RealVectorEncoding-3.3.csproj"
build_xbuild "HeuristicLab.Encodings.SymbolicExpressionTreeEncoding/3.4/HeuristicLab.Encodings.SymbolicExpressionTreeEncoding-3.4.csproj"
build_xbuild "HeuristicLab.Problems.Instances/3.3/HeuristicLab.Problems.Instances-3.3.csproj"
build_xbuild "HeuristicLab.Problems.DataAnalysis/3.4/HeuristicLab.Problems.DataAnalysis-3.4.csproj"
build_xbuild "HeuristicLab.Problems.Instances.DataAnalysis/3.3/HeuristicLab.Problems.Instances.DataAnalysis-3.3.csproj"
build_xbuild "HeuristicLab.ExtLibs/HeuristicLab.NativeInterpreter/0.2/HeuristicLab.NativeInterpreter-0.2/HeuristicLab.NativeInterpreter-0.2.csproj"
build_xbuild "HeuristicLab.Problems.DataAnalysis.Symbolic/3.4/HeuristicLab.Problems.DataAnalysis.Symbolic-3.4.csproj"
build_xbuild "HeuristicLab.Problems.DataAnalysis.Symbolic.Regression/3.4/HeuristicLab.Problems.DataAnalysis.Symbolic.Regression-3.4.csproj"
build_xbuild "HeuristicLab.SequentialEngine/3.3/HeuristicLab.SequentialEngine-3.3.csproj"
build_xbuild "HeuristicLab.Algorithms.GeneticAlgorithm/3.3/HeuristicLab.Algorithms.GeneticAlgorithm-3.3.csproj"
build_xbuild "HeuristicLab.Algorithms.OffspringSelectionGeneticAlgorithm/3.3/HeuristicLab.Algorithms.OffspringSelectionGeneticAlgorithm-3.3.csproj"

cd "${RUNNER_ROOT}"
ln -sfn "${HL_ROOT}/bin" bin
xbuild "HeuristicLab.DataExporter/HeuristicLab.DataExporter.csproj" /p:Configuration=Release /p:Platform="AnyCPU" /p:RestorePackages=false /verbosity:minimal
xbuild "HeuristicLab.HeadlessRunner/HeadlessRunner.csproj" /p:Configuration=Release /p:Platform="AnyCPU" /p:RestorePackages=false /verbosity:minimal

cat <<EOF

Built HeuristicLab.HeadlessRunner:
  ${RUNNER_ROOT}/HeuristicLab.HeadlessRunner/bin/Release/HeuristicLab.HeadlessRunner.exe

Linux runs must use the managed interpreter:
  HL_INTERPRETER=default mono ${RUNNER_ROOT}/HeuristicLab.HeadlessRunner/bin/Release/HeuristicLab.HeadlessRunner.exe ...
EOF
