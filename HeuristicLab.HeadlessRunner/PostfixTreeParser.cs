using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using HeuristicLab.Encodings.SymbolicExpressionTreeEncoding;
using HeuristicLab.Problems.DataAnalysis.Symbolic;

namespace HeuristicLab.HeadlessRunner {
  // Inverse of Program.FormatPostfix: reconstructs an HL tree from one ';'-separated postfix token
  // line (same format used by --postfix-output). Standard RPN/postfix tree construction via an
  // explicit stack -- push a leaf for "C"/"V" tokens, pop N operands and push a new function node
  // for a bare op token (N implied by the fixed arity table below).
  //
  // "C<value>" tokens are reconstructed as NumberTreeNode, not ConstantTreeNode, even though the
  // dump collapsed both original leaf types into one "C" token. This is a deliberate choice, not an
  // oversight: TreeToAutoDiffTermConverter.ConvertToAutoDiff (HeuristicLab.Problems.DataAnalysis.
  // Symbolic/3.4/Converters/TreeToAutoDiffTermConverter.cs:158-166) treats Number nodes as the real
  // optimizable AutoDiff parameters (seeded from NumberTreeNode.Value) and Constant nodes as FIXED
  // ("constants are fixed in autodiff" -- returns ConstantTreeNode.Value straight through, never
  // touched by the LM optimizer). Reconstructing "C" tokens as ConstantTreeNode would (a) require a
  // hacky per-node Constant-symbol clone just to hold an independent value at all, since
  // ConstantTreeNode.Value is a read-only pass-through to its Symbol's single shared Value field, and
  // (b) produce individuals whose numeric literals never get tuned by the real GPC evaluator during
  // the seeded run -- clearly not what a byte-identical-population comparison wants. NumberTreeNode
  // supports an independent per-node Value directly (no cloning needed) and is the type the real
  // evaluator actually optimizes, so every "C<value>" token becomes a Number leaf with that literal
  // value, regardless of whether it was originally a Number or a (permanently-zero) Constant leaf.
  public static class PostfixTreeParser {
    private static readonly Dictionary<string, Type> FunctionTokens = new Dictionary<string, Type> {
      { "add", typeof(Addition) },
      { "sub", typeof(Subtraction) },
      { "mul", typeof(Multiplication) },
      { "div", typeof(Division) },
      { "sin", typeof(Sine) },
      { "cos", typeof(Cosine) },
      { "tan", typeof(Tangent) },
      { "tanh", typeof(HyperbolicTangent) },
      { "exp", typeof(Exponential) },
      { "log", typeof(Logarithm) },
      { "sqrt", typeof(SquareRoot) },
      { "square", typeof(Square) },
      { "cbrt", typeof(CubeRoot) },
      { "abs", typeof(Absolute) },
    };
    private static readonly HashSet<string> BinaryTokens = new HashSet<string> { "add", "sub", "mul", "div" };

    // Builds the full tree (RootSymbol -> StartSymbol -> parsed content), matching
    // ProbabilisticTreeCreator.Create's own wrapper wiring exactly, so the result is
    // structurally identical to a real PTC2-created tree.
    public static ISymbolicExpressionTree Parse(string line, ISymbolicExpressionGrammar grammar) {
      var variableSymbol = (Variable)grammar.Symbols.First(s => s is Variable);

      var stack = new Stack<ISymbolicExpressionTreeNode>();
      var tokens = line.Split(';');
      foreach (var token in tokens) {
        if (token.Length == 0) throw new InvalidOperationException("Empty token in postfix line: " + line);
        char kind = token[0];
        if (kind == 'C') {
          double value = double.Parse(token.Substring(1), CultureInfo.InvariantCulture);
          stack.Push(new NumberTreeNode(value));
        } else if (kind == 'V') {
          int colon = token.IndexOf(':');
          if (colon < 0) throw new InvalidOperationException("Malformed Variable token (missing ':<weight>'): " + token);
          string varName = token.Substring(1, colon - 1);
          double weight = double.Parse(token.Substring(colon + 1), CultureInfo.InvariantCulture);
          var varNode = (VariableTreeNode)variableSymbol.CreateTreeNode();
          varNode.VariableName = varName;
          varNode.Weight = weight;
          stack.Push(varNode);
        } else {
          if (!FunctionTokens.TryGetValue(token, out var symbolType))
            throw new InvalidOperationException("Unmapped postfix function token: " + token);
          var symbol = grammar.Symbols.First(s => s.GetType() == symbolType);
          var node = symbol.CreateTreeNode();
          if (BinaryTokens.Contains(token)) {
            if (stack.Count < 2) throw new InvalidOperationException($"Stack underflow on binary token '{token}' in line: {line}");
            var right = stack.Pop();
            var left = stack.Pop();
            node.AddSubtree(left);
            node.AddSubtree(right);
          } else {
            if (stack.Count < 1) throw new InvalidOperationException($"Stack underflow on unary token '{token}' in line: {line}");
            var child = stack.Pop();
            node.AddSubtree(child);
          }
          stack.Push(node);
        }
      }
      if (stack.Count != 1) throw new InvalidOperationException($"Postfix line did not reduce to a single root (stack size {stack.Count}): {line}");
      var contentRoot = stack.Pop();

      var tree = new SymbolicExpressionTree();
      var rootNode = (SymbolicExpressionTreeTopLevelNode)grammar.ProgramRootSymbol.CreateTreeNode();
      rootNode.SetGrammar(grammar.CreateExpressionTreeGrammar());
      var startNode = (SymbolicExpressionTreeTopLevelNode)grammar.StartSymbol.CreateTreeNode();
      startNode.SetGrammar(grammar.CreateExpressionTreeGrammar());
      rootNode.AddSubtree(startNode);
      startNode.AddSubtree(contentRoot);
      tree.Root = rootNode;
      return tree;
    }

    public static List<ISymbolicExpressionTree> ParseFile(string path, ISymbolicExpressionGrammar grammar) {
      var trees = new List<ISymbolicExpressionTree>();
      foreach (var line in File.ReadAllLines(path)) {
        if (string.IsNullOrWhiteSpace(line)) continue;
        trees.Add(Parse(line, grammar));
      }
      return trees;
    }
  }
}
