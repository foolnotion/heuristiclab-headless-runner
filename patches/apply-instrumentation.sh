#!/bin/bash
# Applies patches/subtree-crossover-instrumentation.patch to a sibling heal-research/HeuristicLab
# checkout, and rebuilds the affected project so the harness can link against the instrumented
# fields (SubtreeCrossover.NoOpLog / .KernelLog / .DonorLog, SymbolicExpressionTreeManipulator
# .LengthLog). Never commit the patched state to heal-research/HeuristicLab -- run
# revert-instrumentation.sh when done.
#
# Usage: patches/apply-instrumentation.sh [path-to-HeuristicLab-checkout]
# Defaults to ../HeuristicLab relative to this repo (the layout this harness assumes throughout).
set -e
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
HL_DIR="${1:-$HERE/../../HeuristicLab}"
HL_DIR="$(cd "$HL_DIR" && pwd)"

if git -C "$HL_DIR" diff --quiet \
    HeuristicLab.Encodings.SymbolicExpressionTreeEncoding/3.4/Crossovers/SubtreeCrossover.cs \
    HeuristicLab.Encodings.SymbolicExpressionTreeEncoding/3.4/Manipulators/SymbolicExpressionTreeManipulator.cs \
    2>/dev/null; then
  echo "Applying instrumentation patch to $HL_DIR ..."
  git -C "$HL_DIR" apply "$HERE/subtree-crossover-instrumentation.patch"
else
  echo "SubtreeCrossover.cs/SymbolicExpressionTreeManipulator.cs already have local changes in $HL_DIR -- assuming instrumentation is already applied, skipping patch."
fi

MSBUILD="/c/Program Files/Microsoft Visual Studio/18/Community/MSBuild/Current/Bin/MSBuild.exe"
HL_DIR_WIN="$(cygpath -w "$HL_DIR")"
"$MSBUILD" "$HL_DIR/HeuristicLab.Encodings.SymbolicExpressionTreeEncoding/3.4/HeuristicLab.Encodings.SymbolicExpressionTreeEncoding-3.4.csproj" \
  -p:Configuration=Release "-p:SolutionDir=${HL_DIR_WIN}\\" -m -v:minimal

echo "Instrumentation applied and rebuilt. Remember to run revert-instrumentation.sh when done."
