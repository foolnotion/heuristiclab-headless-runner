#!/bin/bash
# Reverts SubtreeCrossover.cs back to its committed state in the sibling heal-research/HeuristicLab
# checkout and rebuilds, undoing apply-instrumentation.sh. Run this once you're done capturing
# --crossover-noop-output / --crossover-kernel-output data -- nothing from patches/ should ever be
# left applied (let alone committed) in heal-research/HeuristicLab.
#
# Usage: patches/revert-instrumentation.sh [path-to-HeuristicLab-checkout]
set -e
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
HL_DIR="${1:-$HERE/../../HeuristicLab}"
HL_DIR="$(cd "$HL_DIR" && pwd)"

git -C "$HL_DIR" checkout -- HeuristicLab.Encodings.SymbolicExpressionTreeEncoding/3.4/Crossovers/SubtreeCrossover.cs

MSBUILD="/c/Program Files/Microsoft Visual Studio/18/Community/MSBuild/Current/Bin/MSBuild.exe"
HL_DIR_WIN="$(cygpath -w "$HL_DIR")"
"$MSBUILD" "$HL_DIR/HeuristicLab.Encodings.SymbolicExpressionTreeEncoding/3.4/HeuristicLab.Encodings.SymbolicExpressionTreeEncoding-3.4.csproj" \
  -p:Configuration=Release "-p:SolutionDir=${HL_DIR_WIN}\\" -m -v:minimal

echo "Instrumentation reverted; $HL_DIR is back to a clean, uninstrumented checkout."
git -C "$HL_DIR" status --short
