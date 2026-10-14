using System.Globalization;
using System.Text.RegularExpressions;

namespace Realm.Ecs.Services;

/// <summary>
/// Compiles and evaluates dynamic mathematical expressions against entity stats and runtime contexts.
/// </summary>
public static partial class FormulaEvaluator
{
	private static readonly Dictionary<string, CompiledFormula> Cache = new(StringComparer.OrdinalIgnoreCase);

	public static CompiledFormula Compile(string expression)
	{
		if (string.IsNullOrWhiteSpace(expression))
		{
			return new CompiledFormula(new ConstantNode(0f));
		}

		if (Cache.TryGetValue(expression, out var cached))
		{
			return cached;
		}

		var tokens = Tokenize(expression);
		int index = 0;
		var root = ParseExpression(tokens, ref index);
		var compiled = new CompiledFormula(root);
		Cache[expression] = compiled;
		return compiled;
	}

	public static float Evaluate(string expression, in FormulaContext context)
	{
		var compiled = Compile(expression);
		return compiled.Evaluate(in context);
	}

	[GeneratedRegex(@"([0-9]+(?:\.[0-9]+)?)|([a-zA-Z_][a-zA-Z0-9_\.]*)|(\+|-|\*|\/|\^|\(|\)|,)")]
	private static partial Regex TokenizerRegex();

	private static List<string> Tokenize(string expression)
	{
		var tokens = new List<string>();
		var matches = TokenizerRegex().Matches(expression);
		foreach (Match match in matches)
		{
			if (match.Success)
			{
				tokens.Add(match.Value);
			}
		}

		return tokens;
	}

	private static IFormulaNode ParseExpression(List<string> tokens, ref int index)
	{
		var left = ParseTerm(tokens, ref index);

		while (index < tokens.Count)
		{
			string op = tokens[index];
			if (op == "+" || op == "-")
			{
				index++;
				var right = ParseTerm(tokens, ref index);
				left = new BinaryOpNode(left, op[0], right);
			}
			else
			{
				break;
			}
		}

		return left;
	}

	private static IFormulaNode ParseTerm(List<string> tokens, ref int index)
	{
		var left = ParsePower(tokens, ref index);

		while (index < tokens.Count)
		{
			string op = tokens[index];
			if (op == "*" || op == "/")
			{
				index++;
				var right = ParsePower(tokens, ref index);
				left = new BinaryOpNode(left, op[0], right);
			}
			else
			{
				break;
			}
		}

		return left;
	}

	private static IFormulaNode ParsePower(List<string> tokens, ref int index)
	{
		var left = ParseFactor(tokens, ref index);

		while (index < tokens.Count && tokens[index] == "^")
		{
			index++;
			var right = ParseFactor(tokens, ref index);
			left = new BinaryOpNode(left, '^', right);
		}

		return left;
	}

	private static IFormulaNode ParseFactor(List<string> tokens, ref int index)
	{
		if (index >= tokens.Count) return new ConstantNode(0f);

		string token = tokens[index];

		if (token == "-")
		{
			index++;
			var operand = ParseFactor(tokens, ref index);
			return new BinaryOpNode(new ConstantNode(0f), '-', operand);
		}

		if (token == "+")
		{
			index++;
			return ParseFactor(tokens, ref index);
		}

		if (token == "(")
		{
			index++;
			var expr = ParseExpression(tokens, ref index);
			if (index < tokens.Count && tokens[index] == ")") index++;
			return expr;
		}

		if (float.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out float constantVal))
		{
			index++;
			return new ConstantNode(constantVal);
		}

		if (index + 1 < tokens.Count && tokens[index + 1] == "(")
		{
			return ParseFunctionNode(tokens, ref index, token);
		}

		index++;
		return new VariableNode(token);
	}

	private static FunctionNode ParseFunctionNode(List<string> tokens, ref int index, string token)
	{
		string funcName = token.ToLowerInvariant();
		index += 2;
		var args = new List<IFormulaNode>();

		if (index < tokens.Count && tokens[index] != ")")
		{
			while (index < tokens.Count)
			{
				args.Add(ParseExpression(tokens, ref index));

				if (index >= tokens.Count || tokens[index] != ",")
				{
					break;
				}

				index++;
			}
		}

		if (index < tokens.Count && tokens[index] == ")")
		{
			index++;
		}

		return new FunctionNode(funcName, args.ToArray());
	}
}