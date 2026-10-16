using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Realm.LocalizationGenerator;

public static partial class Program
{
	private static readonly string[] ExcludedDirectoryNames = { "obj", "bin", ".godot", ".git", "node_modules", "Maps", "MapTemplate", "vscode_embedded", "wasi_sdk_embedded" };

	private static readonly HashSet<string> LocalizationManagerMethodNames = new() { "Translate", "TranslateKey" };

	private static readonly HashSet<string> AutoTranslatedPropertyNames = new() { "Text", "TooltipText", "PlaceholderText", "Title" };

	private static IEnumerable<string> EnumerateConstantStrings(ExpressionSyntax expression, Func<ExpressionSyntax, string?> constantResolver)
	{
		switch (expression)
		{
			case ParenthesizedExpressionSyntax parenthesized:
				foreach (string text in EnumerateConstantStrings(parenthesized.Expression, constantResolver)) yield return text;
				break;
			case ConditionalExpressionSyntax conditional:
				foreach (string text in EnumerateConstantStrings(conditional.WhenTrue, constantResolver)) yield return text;
				foreach (string text in EnumerateConstantStrings(conditional.WhenFalse, constantResolver)) yield return text;
				break;
			case LiteralExpressionSyntax or InterpolatedStringExpressionSyntax or BinaryExpressionSyntax:
				if (TryGetConstantString(expression, constantResolver, out string? value) && value != null) yield return value;
				break;
		}
	}

	private static Dictionary<string, HashSet<int>> BuildTranslatingParameterMap(IReadOnlyList<SyntaxTree> syntaxTrees)
	{
		var map = new Dictionary<string, HashSet<int>>(StringComparer.Ordinal);

		var callables = syntaxTrees
			.SelectMany(tree => tree.GetRoot().DescendantNodes())
			.Select(node => node switch
			{
				MethodDeclarationSyntax method => (Name: method.Identifier.ValueText, Parameters: method.ParameterList.Parameters, Body: (SyntaxNode?)method),
				LocalFunctionStatementSyntax local => (Name: local.Identifier.ValueText, Parameters: local.ParameterList.Parameters, Body: local),
				_ => (Name: "", Parameters: default(SeparatedSyntaxList<ParameterSyntax>), Body: null)
			})
			.Where(callable => callable.Body != null && callable.Parameters.Count > 0)
			.ToList();

		bool changed = true;
		for (int iteration = 0; changed && iteration < 6; iteration++)
		{
			changed = false;
			foreach (var callable in callables)
			{
				for (int index = 0; index < callable.Parameters.Count; index++)
				{
					var parameter = callable.Parameters[index];
					if (parameter.Type?.ToString().TrimEnd('?') != "string") continue;
					if (map.TryGetValue(callable.Name, out var known) && known.Contains(index)) continue;
					if (!ParameterReachesTranslation(callable.Body!, parameter.Identifier.ValueText, map)) continue;

					if (!map.TryGetValue(callable.Name, out known)) map[callable.Name] = known = new HashSet<int>();
					known.Add(index);
					changed = true;
				}
			}
		}

		return map;
	}

	private static bool ParameterReachesTranslation(SyntaxNode body, string parameterName, Dictionary<string, HashSet<int>> map)
	{
		bool IsParameter(ExpressionSyntax expression) => expression is IdentifierNameSyntax identifier && identifier.Identifier.ValueText == parameterName;

		foreach (var assignment in body.DescendantNodes().OfType<AssignmentExpressionSyntax>())
		{
			string? propertyName = assignment.Left switch
			{
				IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
				MemberAccessExpressionSyntax member => member.Name.Identifier.ValueText,
				_ => null
			};
			if (propertyName != null && AutoTranslatedPropertyNames.Contains(propertyName) && IsParameter(assignment.Right)) return true;
		}

		foreach (var invocation in body.DescendantNodes().OfType<InvocationExpressionSyntax>())
		{
			var arguments = invocation.ArgumentList.Arguments;
			if (TryClassifyInvocation(invocation, out _) && arguments.Count > 0 && IsParameter(arguments[0].Expression)) return true;

			string? calleeName = invocation.Expression switch
			{
				IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
				MemberAccessExpressionSyntax member => member.Name.Identifier.ValueText,
				_ => null
			};
			if (calleeName == null || !map.TryGetValue(calleeName, out var indices)) continue;
			if (indices.Any(index => index < arguments.Count && IsParameter(arguments[index].Expression))) return true;
		}

		return false;
	}

	public static int Main(string[] args)
	{
		if (args.Length < 2)
		{
			Console.Error.WriteLine("Usage: Realm.LocalizationGenerator <solution.slnx> <output en.json>");
			return 1;
		}

		string solutionPath = Path.GetFullPath(args[0]);
		string outputPath = Path.GetFullPath(args[1]);

		if (!File.Exists(solutionPath))
		{
			Console.Error.WriteLine($"Solution not found: {solutionPath}");
			return 1;
		}

		var catalog = new SortedDictionary<string, string>(StringComparer.Ordinal);

		var projectDirectories = EnumerateProjectDirectories(solutionPath).ToList();

		var syntaxTrees = projectDirectories
			.SelectMany(projectDirectory => EnumerateFiles(projectDirectory, "*.cs"))
			.Select(sourceFile => CSharpSyntaxTree.ParseText(File.ReadAllText(sourceFile), path: sourceFile))
			.ToList();

		var constantResolver = BuildConstantResolver(syntaxTrees);
		var translatingParameters = BuildTranslatingParameterMap(syntaxTrees);

		foreach (var tree in syntaxTrees)
		{
			foreach (var entry in ExtractFromCSharp(tree, constantResolver, translatingParameters)) AddEntry(catalog, entry.Key, entry.Value);
		}

		foreach (string projectDirectory in projectDirectories)
		{
			foreach (string scriptFile in EnumerateFiles(projectDirectory, "*.js"))
			{
				foreach (var entry in ExtractFromJavaScript(scriptFile)) AddEntry(catalog, entry.Key, entry.Value);
			}

			foreach (string sceneFile in EnumerateFiles(projectDirectory, "*.tscn"))
			{
				foreach (var entry in ExtractFromScene(sceneFile)) AddEntry(catalog, entry.Key, entry.Value);
			}
		}

		string? outputDirectory = Path.GetDirectoryName(outputPath);
		if (!string.IsNullOrEmpty(outputDirectory)) Directory.CreateDirectory(outputDirectory);

		if (File.Exists(outputPath)) File.Delete(outputPath);

		var options = new JsonSerializerOptions
		{
			WriteIndented = true,
			Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
		};
		File.WriteAllText(outputPath, JsonSerializer.Serialize(catalog, options), new UTF8Encoding(false));

		Console.WriteLine($"Generated {catalog.Count} localization keys -> {outputPath}");
		return 0;
	}

	private static void AddEntry(SortedDictionary<string, string> catalog, string key, string value)
	{
		if (string.IsNullOrEmpty(key)) return;
		if (!catalog.TryGetValue(key, out var existing) || (existing == key && value != key))
		{
			catalog[key] = value;
		}
	}

	private static IEnumerable<string> EnumerateProjectDirectories(string solutionPath)
	{
		string solutionDirectory = Path.GetDirectoryName(solutionPath)!;
		var document = XDocument.Load(solutionPath);
		return document.Descendants("Project")
			.Select(project => (string?)project.Attribute("Path"))
			.Where(path => !string.IsNullOrEmpty(path))
			.Select(path => Path.GetDirectoryName(Path.GetFullPath(Path.Combine(solutionDirectory, path!.Replace('/', Path.DirectorySeparatorChar))))!)
			.Where(Directory.Exists)
			.Distinct(StringComparer.OrdinalIgnoreCase);
	}

	private static IEnumerable<string> EnumerateFiles(string directory, string pattern)
	{
		foreach (string file in Directory.EnumerateFiles(directory, pattern)) yield return file;

		foreach (string subDirectory in Directory.EnumerateDirectories(directory))
		{
			string name = Path.GetFileName(subDirectory);
			if (ExcludedDirectoryNames.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;
			foreach (string file in EnumerateFiles(subDirectory, pattern)) yield return file;
		}
	}

	private static Func<ExpressionSyntax, string?> BuildConstantResolver(IEnumerable<SyntaxTree> syntaxTrees)
	{
		var qualifiedConstants = new Dictionary<string, string>(StringComparer.Ordinal);
		var simpleConstants = new Dictionary<string, string?>(StringComparer.Ordinal);

		foreach (var tree in syntaxTrees)
		{
			foreach (var field in tree.GetRoot().DescendantNodes().OfType<FieldDeclarationSyntax>())
			{
				bool isConstantLike = field.Modifiers.Any(SyntaxKind.ConstKeyword)
					|| (field.Modifiers.Any(SyntaxKind.StaticKeyword) && field.Modifiers.Any(SyntaxKind.ReadOnlyKeyword));
				if (!isConstantLike) continue;
				if (field.Declaration.Type.ToString() != "string") continue;
				if (field.Parent is not TypeDeclarationSyntax owner) continue;

				foreach (var variable in field.Declaration.Variables)
				{
					string? constantValue = variable.Initializer?.Value switch
					{
						LiteralExpressionSyntax literal when literal.IsKind(SyntaxKind.StringLiteralExpression) => literal.Token.ValueText,
						InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: "ConvertFromUtf32" }, ArgumentList.Arguments: [{ Expression: LiteralExpressionSyntax { Token.Value: int codePoint } }] } => char.ConvertFromUtf32(codePoint),
						_ => null
					};
					if (constantValue == null) continue;

					string name = variable.Identifier.ValueText;
					qualifiedConstants[$"{owner.Identifier.ValueText}.{name}"] = constantValue;
					simpleConstants[name] = simpleConstants.ContainsKey(name) ? null : constantValue;
				}
			}
		}

		return expression => expression switch
		{
			IdentifierNameSyntax identifier => simpleConstants.GetValueOrDefault(identifier.Identifier.ValueText),
			MemberAccessExpressionSyntax member when member.Expression is IdentifierNameSyntax owner =>
				qualifiedConstants.GetValueOrDefault($"{owner.Identifier.ValueText}.{member.Name.Identifier.ValueText}"),
			_ => null
		};
	}

	private static IEnumerable<KeyValuePair<string, string>> ExtractFromCSharp(SyntaxTree tree, Func<ExpressionSyntax, string?> constantResolver, Dictionary<string, HashSet<int>> translatingParameters)
	{
		string sourceFile = tree.FilePath;
		foreach (var wrapperInvocation in tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>())
		{
			string? calleeName = wrapperInvocation.Expression switch
			{
				IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
				MemberAccessExpressionSyntax member => member.Name.Identifier.ValueText,
				_ => null
			};
			if (calleeName == null || !translatingParameters.TryGetValue(calleeName, out var indices)) continue;

			var wrapperArguments = wrapperInvocation.ArgumentList.Arguments;
			foreach (int index in indices.Where(candidate => candidate < wrapperArguments.Count))
			{
				foreach (string text in EnumerateConstantStrings(wrapperArguments[index].Expression, constantResolver))
				{
					if (!string.IsNullOrWhiteSpace(text)) yield return new KeyValuePair<string, string>(text, text);
				}
			}
		}

		foreach (var assignment in tree.GetRoot().DescendantNodes().OfType<AssignmentExpressionSyntax>())
		{
			if (!assignment.IsKind(SyntaxKind.SimpleAssignmentExpression)) continue;

			string? propertyName = assignment.Left switch
			{
				IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
				MemberAccessExpressionSyntax member => member.Name.Identifier.ValueText,
				_ => null
			};
			if (propertyName == null || !AutoTranslatedPropertyNames.Contains(propertyName)) continue;

			foreach (string text in EnumerateConstantStrings(assignment.Right, constantResolver))
			{
				if (!string.IsNullOrWhiteSpace(text)) yield return new KeyValuePair<string, string>(text, text);
			}
		}

		foreach (var invocation in tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>())
		{
			if (!TryClassifyInvocation(invocation, out bool hasFallbackArgument)) continue;

			var arguments = invocation.ArgumentList.Arguments;
			if (arguments.Count == 0) continue;

			if (!TryGetConstantString(arguments[0].Expression, constantResolver, out string? key) || string.IsNullOrEmpty(key))
			{
				int line = tree.GetLineSpan(invocation.Span).StartLinePosition.Line + 1;
				Console.WriteLine($"warning: non-literal localization key skipped at {sourceFile}:{line}");
				continue;
			}

			string value = key;
			if (hasFallbackArgument && arguments.Count > 1 && TryGetConstantString(arguments[1].Expression, constantResolver, out string? fallback) && !string.IsNullOrEmpty(fallback))
			{
				value = fallback;
			}

			yield return new KeyValuePair<string, string>(key, value);
		}
	}

	private static bool TryClassifyInvocation(InvocationExpressionSyntax invocation, out bool hasFallbackArgument)
	{
		hasFallbackArgument = false;
		switch (invocation.Expression)
		{
			case IdentifierNameSyntax { Identifier.ValueText: "Tr" }:
				return true;
			case MemberAccessExpressionSyntax member:
				string methodName = member.Name.Identifier.ValueText;
				string receiver = member.Expression.ToString();
				if (methodName == "Tr") return true;
				if (methodName == "Translate" && receiver.EndsWith("TranslationServer", StringComparison.Ordinal)) return true;
				if (LocalizationManagerMethodNames.Contains(methodName) && receiver.EndsWith("LocalizationManager", StringComparison.Ordinal))
				{
					hasFallbackArgument = true;
					return true;
				}
				return false;
			default:
				return false;
		}
	}

	private static bool TryGetConstantString(ExpressionSyntax expression, Func<ExpressionSyntax, string?> constantResolver, out string? value)
	{
		switch (expression)
		{
			case LiteralExpressionSyntax literal when literal.IsKind(SyntaxKind.StringLiteralExpression):
				value = literal.Token.ValueText;
				return true;
			case ParenthesizedExpressionSyntax parenthesized:
				return TryGetConstantString(parenthesized.Expression, constantResolver, out value);
			case BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.AddExpression):
				if (TryGetConstantString(binary.Left, constantResolver, out string? left) && TryGetConstantString(binary.Right, constantResolver, out string? right))
				{
					value = left + right;
					return true;
				}
				break;
			case IdentifierNameSyntax or MemberAccessExpressionSyntax:
				value = constantResolver(expression);
				return value != null;
			case InterpolatedStringExpressionSyntax interpolated:
				var builder = new StringBuilder();
				foreach (var content in interpolated.Contents)
				{
					if (content is InterpolatedStringTextSyntax text)
					{
						builder.Append(text.TextToken.ValueText);
					}
					else if (content is InterpolationSyntax { AlignmentClause: null, FormatClause: null } hole && constantResolver(hole.Expression) is { } resolved)
					{
						builder.Append(resolved);
					}
					else
					{
						value = null;
						return false;
					}
				}

				value = builder.ToString();
				return true;
		}

		value = null;
		return false;
	}

	private static IEnumerable<KeyValuePair<string, string>> ExtractFromScene(string sceneFile)
	{
		foreach (Match match in SceneTextPropertyRegex().Matches(File.ReadAllText(sceneFile)))
		{
			string text = SceneEscapeRegex().Replace(match.Groups["value"].Value, escape => escape.Groups[1].Value switch
			{
				"n" => "\n",
				"t" => "\t",
				var other => other
			});

			if (!string.IsNullOrWhiteSpace(text)) yield return new KeyValuePair<string, string>(text, text);
		}
	}

	[GeneratedRegex(@"^(?:text|tooltip_text|placeholder_text|title) = ""(?<value>(?:[^""\\]|\\.)*)""\s*$", RegexOptions.Multiline | RegexOptions.Singleline)]
	private static partial Regex SceneTextPropertyRegex();

	[GeneratedRegex(@"\\(.)", RegexOptions.Singleline)]
	private static partial Regex SceneEscapeRegex();

	private static IEnumerable<KeyValuePair<string, string>> ExtractFromJavaScript(string scriptFile)
	{
		string content = File.ReadAllText(scriptFile);
		if (!content.Contains("function t(key, fallback)", StringComparison.Ordinal)) yield break;

		foreach (Match match in JavaScriptTranslationCallRegex().Matches(content))
		{
			string key = UnescapeJavaScriptString(match.Groups["key"].Value);
			string value = match.Groups["fallback"].Success ? UnescapeJavaScriptString(match.Groups["fallback"].Value) : key;
			yield return new KeyValuePair<string, string>(key, string.IsNullOrEmpty(value) ? key : value);
		}
	}

	private static string UnescapeJavaScriptString(string raw)
	{
		return JavaScriptEscapeRegex().Replace(raw, match =>
		{
			string escape = match.Groups[1].Value;
			return escape[0] switch
			{
				'n' => "\n",
				'r' => "\r",
				't' => "\t",
				'u' when escape.Length == 5 => ((char)Convert.ToInt32(escape.Substring(1), 16)).ToString(),
				'x' when escape.Length == 3 => ((char)Convert.ToInt32(escape.Substring(1), 16)).ToString(),
				_ => escape
			};
		});
	}

	[GeneratedRegex(@"(?<![\w$.])t\(\s*(?:'(?<key>(?:[^'\\\r\n]|\\.)*)'|""(?<key>(?:[^""\\\r\n]|\\.)*)""|`(?<key>(?:[^`\\$]|\\.)*)`)(?:\s*,\s*(?:'(?<fallback>(?:[^'\\\r\n]|\\.)*)'|""(?<fallback>(?:[^""\\\r\n]|\\.)*)""|`(?<fallback>(?:[^`\\$]|\\.)*)`))?")]
	private static partial Regex JavaScriptTranslationCallRegex();

	[GeneratedRegex(@"\\(u[0-9a-fA-F]{4}|x[0-9a-fA-F]{2}|.)", RegexOptions.Singleline)]
	private static partial Regex JavaScriptEscapeRegex();
}
