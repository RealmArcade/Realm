using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Realm.WasmBindgen;

public sealed class ComponentFieldLayout
{
	public string Name { get; init; } = "";
	public int Offset { get; init; }
	public string TypeName { get; init; } = "";
}

public sealed class ComponentLayout
{
	public string Name { get; init; } = "";
	public string FullName { get; init; } = "";
	public int Size { get; init; }
	public List<ComponentFieldLayout> Fields { get; init; } = new();
	public Dictionary<string, string> PropertyToField { get; init; } = new(StringComparer.Ordinal);
}

public sealed class HandleFieldSpec
{
	public string Name { get; init; } = "";
	public string Kind { get; init; } = "host";
	public string TypeName { get; init; } = "int";
	public string? ComponentName { get; init; }
	public int ComponentId { get; init; } = -1;
	public int SubOffset { get; init; }
	public int IndexOffset { get; init; }
	public bool RequiresAlive { get; init; }
	public bool Invert { get; init; }
	public int MinValue { get; init; }
	public int MaxValue { get; init; } = int.MaxValue;
	public object? ConstantValue { get; init; }
	public List<string> TagNames { get; init; } = new();
	public List<int> TagIds { get; init; } = new();
	public object? DefaultValue { get; init; }
}

public sealed class HandleTypeLayout
{
	public string Name { get; init; } = "";
	public string Label { get; init; } = "";
	public string ValueField { get; init; } = "Value";
	public int MinValue { get; init; }
	public int MaxValue { get; init; } = int.MaxValue;
	public List<HandleFieldSpec> Fields { get; init; } = new();
	public List<string> SummaryFields { get; init; } = new();
}

public static class WasmDebugTypeDiscovery
{
	private static readonly Regex ExtensionHeaderRegex = new(@"extension\s*\(\s*([A-Za-z_][\w\.]*)\s+([A-Za-z_]\w*)\s*\)", RegexOptions.Compiled);

	private static readonly string[] PreferredSummaryFields =
	{
		"Health", "MaxHealth", "Position", "Gold", "Wood", "CurrentPopulation", "MaxPopulation", "ResourceAmount"
	};

	public static Dictionary<string, ComponentLayout> DiscoverComponentLayouts(Compilation compilation, string repoRoot)
	{
		var layouts = new Dictionary<string, ComponentLayout>(StringComparer.Ordinal);
		string componentsRoot = Path.Combine(repoRoot, "Realm.Ecs", "Components");
		if (!Directory.Exists(componentsRoot)) return layouts;

		foreach (string filePath in Directory.GetFiles(componentsRoot, "*.cs", SearchOption.AllDirectories))
		{
			string source = File.ReadAllText(filePath, Encoding.UTF8);
			SyntaxTree tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
			CompilationUnitSyntax root = tree.GetCompilationUnitRoot();

			foreach (TypeDeclarationSyntax structDeclaration in root.DescendantNodes().OfType<TypeDeclarationSyntax>().Where(IsStructLike))
			{
				string typeName = structDeclaration.Identifier.Text;
				string namespaceName = GetNamespace(structDeclaration);
				string fullName = string.IsNullOrEmpty(namespaceName) ? typeName : $"{namespaceName}.{typeName}";
				INamedTypeSymbol? symbol = compilation.GetTypeByMetadataName(fullName);
				if (symbol == null) continue;

				List<(string Name, int Offset, string TypeName)> fields = WasmLldbScriptWriter.CalculateStructLayout(symbol, out int size);
				var propertyToField = new Dictionary<string, string>(StringComparer.Ordinal);
				foreach (PropertyDeclarationSyntax property in structDeclaration.Members.OfType<PropertyDeclarationSyntax>())
				{
					string? expression = GetGetterExpression(property);
					if (string.IsNullOrEmpty(expression)) continue;

					string? fieldName = fields
						.Select(field => field.Name)
						.FirstOrDefault(name => Regex.IsMatch(expression, $@"\b{Regex.Escape(name)}\b"));
					if (fieldName != null) propertyToField[property.Identifier.Text] = fieldName;
				}

				layouts[typeName] = new ComponentLayout
				{
					Name = typeName,
					FullName = fullName,
					Size = size,
					Fields = fields.Select(field => new ComponentFieldLayout { Name = field.Name, Offset = field.Offset, TypeName = field.TypeName }).ToList(),
					PropertyToField = propertyToField
				};
			}
		}

		return layouts;
	}

	public static Dictionary<string, int> DiscoverComponentIds(Compilation compilation)
	{
		var ids = new Dictionary<string, int>(StringComparer.Ordinal);
		INamedTypeSymbol? componentIds = compilation.GetTypeByMetadataName("Realm.Ecs.ComponentIds");
		if (componentIds == null) return ids;

		foreach (IFieldSymbol field in componentIds.GetMembers().OfType<IFieldSymbol>())
		{
			if (field.IsConst && field.HasConstantValue && field.ConstantValue is int value)
			{
				ids[field.Name] = value;
			}
		}

		return ids;
	}

	public static Dictionary<string, int> DiscoverComponentOffsets(Compilation compilation, string repoRoot, Dictionary<string, ComponentLayout> layouts, Dictionary<string, int> componentIds)
	{
		var offsets = new Dictionary<string, int>(StringComparer.Ordinal);
		INamedTypeSymbol? sharedMemory = compilation.GetTypeByMetadataName("Realm.Ecs.EcsSharedMemory");
		if (sharedMemory == null) return offsets;

		var constants = new Dictionary<string, int>(StringComparer.Ordinal);
		foreach (IFieldSymbol field in sharedMemory.GetMembers().OfType<IFieldSymbol>())
		{
			if (field.IsConst && field.HasConstantValue && field.ConstantValue is int value)
			{
				constants[field.Name] = value;
			}
		}

		string sharedMemoryPath = Path.Combine(repoRoot, "Realm.Ecs", "EcsWorld.SharedMemory.cs");
		if (!File.Exists(sharedMemoryPath)) return offsets;

		SyntaxTree tree = CSharpSyntaxTree.ParseText(File.ReadAllText(sharedMemoryPath, Encoding.UTF8), new CSharpParseOptions(LanguageVersion.Preview));
		CompilationUnitSyntax root = tree.GetCompilationUnitRoot();
		int currentOffset = constants.TryGetValue("COMPONENTS_START_OFFSET", out int startOffset) ? startOffset : 0;

		foreach (InvocationExpressionSyntax invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
		{
			if (invocation.Expression is not GenericNameSyntax genericName || genericName.Identifier.Text != "BindComponent") continue;
			if (invocation.ArgumentList.Arguments.Count < 2) continue;

			string? componentName = GetComponentIdName(invocation.ArgumentList.Arguments[0].Expression);
			if (componentName == null || !layouts.TryGetValue(componentName, out ComponentLayout? layout)) continue;

			int count = EvaluateIntExpression(invocation.ArgumentList.Arguments[1].Expression, constants);
			offsets[componentName] = currentOffset;
			currentOffset += count * layout.Size;
		}

		foreach (ComponentLayout layout in layouts.Values)
		{
			if (offsets.ContainsKey(layout.Name)) continue;

			string constantName = ToUpperSnakeCase(layout.Name) + "_OFFSET";
			if (constants.TryGetValue(constantName, out int offset))
			{
				offsets[layout.Name] = offset;
			}
		}

		return offsets;
	}

	public static Dictionary<string, HandleTypeLayout> DiscoverHandleTypes(Compilation compilation, string repoRoot, Dictionary<string, ComponentLayout> layouts, Dictionary<string, int> componentIds, Dictionary<string, int> sharedConstants)
	{
		var handles = new Dictionary<string, HandleTypeLayout>(StringComparer.Ordinal);
		string sharedRoot = Path.Combine(repoRoot, "Realm.Ecs", "Shared");
		if (!Directory.Exists(sharedRoot)) return handles;

		var propertiesByHandle = new Dictionary<string, List<PropertyDeclarationSyntax>>(StringComparer.Ordinal);

		foreach (string filePath in Directory.GetFiles(sharedRoot, "*.cs", SearchOption.TopDirectoryOnly))
		{
			string source = File.ReadAllText(filePath, Encoding.UTF8);
			SyntaxTree tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
			CompilationUnitSyntax root = tree.GetCompilationUnitRoot();

			foreach (TypeDeclarationSyntax structDeclaration in root.DescendantNodes().OfType<TypeDeclarationSyntax>().Where(IsStructLike))
			{
				if (!IsHandleStruct(structDeclaration)) continue;

				string typeName = structDeclaration.Identifier.Text;
				propertiesByHandle[typeName] = structDeclaration.Members.OfType<PropertyDeclarationSyntax>().ToList();
			}
		}

		foreach (string filePath in Directory.GetFiles(Path.Combine(repoRoot, "Realm.MapAPI"), "*.cs", SearchOption.TopDirectoryOnly))
		{
			string source = File.ReadAllText(filePath, Encoding.UTF8);
			if (!source.Contains("extension(", StringComparison.Ordinal)) continue;

			string transformed = TransformExtensionBlocks(source);
			SyntaxTree tree = CSharpSyntaxTree.ParseText(transformed, new CSharpParseOptions(LanguageVersion.Preview));
			CompilationUnitSyntax root = tree.GetCompilationUnitRoot();

			foreach (ClassDeclarationSyntax extensionClass in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
			{
				if (!extensionClass.Identifier.Text.StartsWith("__RealmExtension_", StringComparison.Ordinal)) continue;

				string typeName = extensionClass.Identifier.Text.Substring("__RealmExtension_".Length);
				if (!propertiesByHandle.TryGetValue(typeName, out List<PropertyDeclarationSyntax>? properties))
				{
					properties = new List<PropertyDeclarationSyntax>();
					propertiesByHandle[typeName] = properties;
				}

				properties.AddRange(extensionClass.Members.OfType<PropertyDeclarationSyntax>());
			}
		}

		foreach ((string typeName, List<PropertyDeclarationSyntax> properties) in propertiesByHandle)
		{
			var fields = new List<HandleFieldSpec>
			{
				new() { Name = "Value", Kind = "value", TypeName = "int" }
			};

			var seenNames = new HashSet<string>(StringComparer.Ordinal) { "Value" };
			foreach (PropertyDeclarationSyntax property in properties)
			{
				string propertyName = property.Identifier.Text;
				if (!seenNames.Add(propertyName)) continue;

				HandleFieldSpec? spec = ParseProperty(property, typeName, layouts, componentIds, sharedConstants);
				if (spec != null) fields.Add(spec);
			}

			HandleFieldSpec? validField = fields.FirstOrDefault(field => field.Kind == "valid");
			handles[typeName] = new HandleTypeLayout
			{
				Name = typeName,
				Label = typeName.EndsWith("Id", StringComparison.Ordinal) ? typeName.Substring(0, typeName.Length - 2) : typeName,
				ValueField = "Value",
				MinValue = validField?.MinValue ?? 0,
				MaxValue = validField?.MaxValue ?? int.MaxValue,
				Fields = fields,
				SummaryFields = SelectSummaryFields(fields)
			};
		}

		return handles;
	}

	private static bool IsStructLike(TypeDeclarationSyntax declaration)
	{
		if (declaration is StructDeclarationSyntax) return true;
		return declaration is RecordDeclarationSyntax record && record.ClassOrStructKeyword.IsKind(SyntaxKind.StructKeyword);
	}

	private static bool IsHandleStruct(TypeDeclarationSyntax structDeclaration)
	{
		if (structDeclaration.ParameterList != null)
		{
			foreach (ParameterSyntax parameter in structDeclaration.ParameterList.Parameters)
			{
				if (parameter.Identifier.Text == "Value" && parameter.Type?.ToString() == "int") return true;
			}
		}

		foreach (MemberDeclarationSyntax member in structDeclaration.Members)
		{
			if (member is FieldDeclarationSyntax fieldDeclaration)
			{
				foreach (VariableDeclaratorSyntax variable in fieldDeclaration.Declaration.Variables)
				{
					if (variable.Identifier.Text == "Value" && fieldDeclaration.Declaration.Type.ToString() == "int") return true;
				}
			}

			if (member is PropertyDeclarationSyntax property && property.Identifier.Text == "Value" && property.Type.ToString() == "int") return true;
		}

		return false;
	}

	private static HandleFieldSpec? ParseProperty(PropertyDeclarationSyntax property, string handleTypeName, Dictionary<string, ComponentLayout> layouts, Dictionary<string, int> componentIds, Dictionary<string, int> sharedConstants)
	{
		string propertyName = property.Identifier.Text;
		string? expression = GetGetterExpression(property);
		if (string.IsNullOrEmpty(expression)) return Host(propertyName);

		if (propertyName is "Value" or "Id" or "UniqueId" or "Index")
		{
			return new HandleFieldSpec { Name = propertyName, Kind = "value", TypeName = "int" };
		}

		if (propertyName == "IsValid")
		{
			(int min, int max) = ParseValidRange(expression, handleTypeName, sharedConstants);
			return new HandleFieldSpec { Name = propertyName, Kind = "valid", TypeName = "bool", MinValue = min, MaxValue = max };
		}

		if (propertyName == "IsDead")
		{
			return new HandleFieldSpec { Name = propertyName, Kind = "alive", TypeName = "bool", Invert = true, DefaultValue = false };
		}

		Match componentMatch = Regex.Match(expression, @"GetComponent<([A-Za-z_][\w\.]*)>\s*\(\s*ComponentIds\.([A-Za-z_]\w*)\s*,\s*([^)]*)\)\s*->\s*([A-Za-z_]\w*)\s*(\()?");
		if (componentMatch.Success)
		{
			string componentTypeName = componentMatch.Groups[1].Value;
			string componentIdName = componentMatch.Groups[2].Value;
			string indexExpression = componentMatch.Groups[3].Value;
			string memberName = componentMatch.Groups[4].Value;
			bool isMethodCall = componentMatch.Groups[5].Success;

			if (isMethodCall) return Host(propertyName);
			if (!layouts.TryGetValue(componentTypeName, out ComponentLayout? layout)) return Host(propertyName);
			if (!componentIds.TryGetValue(componentIdName, out int componentId)) return Host(propertyName);

			string? fieldName = ResolveComponentField(layout, memberName);
			if (fieldName == null) return Host(propertyName);

			ComponentFieldLayout? field = layout.Fields.FirstOrDefault(candidate => candidate.Name == fieldName);
			if (field == null) return Host(propertyName);

			int indexOffset = Regex.IsMatch(indexExpression, @"\+\s*1\b") ? 1 : 0;
			bool requiresAlive = expression.Contains("Alive[", StringComparison.Ordinal);
			string propertyType = property.Type.ToString();
			string fieldType = propertyType == "bool" && field.TypeName is "int" or "bool" ? "bool" : field.TypeName;
			object? defaultValue = ParseDefaultValue(expression, fieldType);

			return new HandleFieldSpec
			{
				Name = propertyName,
				Kind = "component",
				TypeName = fieldType,
				ComponentName = componentTypeName,
				ComponentId = componentId,
				SubOffset = field.Offset,
				IndexOffset = indexOffset,
				RequiresAlive = requiresAlive,
				DefaultValue = defaultValue
			};
		}

		List<string> tagNames = Regex.Matches(expression, @"HasComponent\s*\([^,]+,\s*ComponentIds\.([A-Za-z_]\w*)\s*\)")
			.Select(match => match.Groups[1].Value)
			.Distinct(StringComparer.Ordinal)
			.ToList();
		if (tagNames.Count > 0)
		{
			var tagIds = new List<int>();
			foreach (string tagName in tagNames)
			{
				if (componentIds.TryGetValue(tagName, out int tagId)) tagIds.Add(tagId);
			}

			return new HandleFieldSpec
			{
				Name = propertyName,
				Kind = tagNames.Count > 1 ? "tag_any" : "tag",
				TypeName = "bool",
				ComponentName = tagNames[0],
				ComponentId = tagIds.Count > 0 ? tagIds[0] : -1,
				TagNames = tagNames,
				TagIds = tagIds,
				RequiresAlive = expression.Contains("Alive[", StringComparison.Ordinal),
				DefaultValue = false
			};
		}

		if (expression.Contains("Alive[", StringComparison.Ordinal))
		{
			return new HandleFieldSpec { Name = propertyName, Kind = "alive", TypeName = "bool", Invert = false, DefaultValue = false };
		}

		if (TryParseConstant(expression, out object? constantValue, out string constantType))
		{
			return new HandleFieldSpec { Name = propertyName, Kind = "constant", TypeName = constantType, ConstantValue = constantValue };
		}

		return Host(propertyName);
	}

	private static HandleFieldSpec Host(string propertyName)
	{
		return new HandleFieldSpec { Name = propertyName, Kind = "host", TypeName = "string" };
	}

	private static string? ResolveComponentField(ComponentLayout layout, string memberName)
	{
		if (layout.Fields.Any(field => field.Name == memberName)) return memberName;
		if (layout.PropertyToField.TryGetValue(memberName, out string? fieldName)) return fieldName;
		return null;
	}

	private static (int Min, int Max) ParseValidRange(string expression, string handleTypeName, Dictionary<string, int> sharedConstants)
	{
		int min = expression.Contains("Value > 0", StringComparison.Ordinal) ? 1 : 0;
		int max = handleTypeName switch
		{
			"PlayerId" => sharedConstants.TryGetValue("MAX_PLAYERS", out int maxPlayers) ? maxPlayers : 25,
			"UnitId" or "LootItemId" => sharedConstants.TryGetValue("MAX_ENTITIES", out int maxEntities) ? maxEntities : 8192,
			_ => int.MaxValue
		};

		return (min, max);
	}

	private static object? ParseDefaultValue(string expression, string typeName)
	{
		Match conditional = Regex.Match(expression, @"\?\s*[^:]+:\s*([^;\)]+)");
		if (!conditional.Success) return DefaultForType(typeName);

		string falseBranch = conditional.Groups[1].Value.Trim();
		if (TryParseConstant(falseBranch, out object? value, out _)) return value;
		return DefaultForType(typeName);
	}

	private static object DefaultForType(string typeName)
	{
		return typeName switch
		{
			"float" => 0f,
			"bool" => false,
			_ => 0
		};
	}

	private static bool TryParseConstant(string expression, out object? value, out string typeName)
	{
		string trimmed = expression.Trim();
		if (trimmed.EndsWith("f", StringComparison.OrdinalIgnoreCase) &&
		    float.TryParse(trimmed.Substring(0, trimmed.Length - 1), NumberStyles.Float, CultureInfo.InvariantCulture, out float floatValue))
		{
			value = floatValue;
			typeName = "float";
			return true;
		}

		if (trimmed is "true" or "false")
		{
			value = trimmed == "true";
			typeName = "bool";
			return true;
		}

		if (int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out int intValue))
		{
			value = intValue;
			typeName = "int";
			return true;
		}

		value = null;
		typeName = "int";
		return false;
	}

	private static List<string> SelectSummaryFields(List<HandleFieldSpec> fields)
	{
		var selected = new List<string>();
		foreach (string preferred in PreferredSummaryFields)
		{
			HandleFieldSpec? field = fields.FirstOrDefault(candidate => candidate.Name == preferred && candidate.Kind is "component" or "value");
			if (field != null && !selected.Contains(field.Name)) selected.Add(field.Name);
			if (selected.Count >= 4) return selected;
		}

		foreach (HandleFieldSpec field in fields)
		{
			if (field.Kind is not ("component" or "value")) continue;
			if (field.Name is "Value" or "Id" or "UniqueId" or "Index") continue;
			if (selected.Contains(field.Name)) continue;
			selected.Add(field.Name);
			if (selected.Count >= 4) break;
		}

		return selected;
	}

	private static string? GetGetterExpression(PropertyDeclarationSyntax property)
	{
		if (property.ExpressionBody != null) return property.ExpressionBody.Expression.ToString();

		AccessorDeclarationSyntax? getter = property.AccessorList?.Accessors.FirstOrDefault(accessor => accessor.IsKind(SyntaxKind.GetAccessorDeclaration));
		if (getter == null) return null;
		if (getter.ExpressionBody != null) return getter.ExpressionBody.Expression.ToString();

		ReturnStatementSyntax? returnStatement = getter.Body?.Statements.OfType<ReturnStatementSyntax>().FirstOrDefault();
		return returnStatement?.Expression?.ToString();
	}

	private static string GetNamespace(SyntaxNode node)
	{
		var names = new List<string>();
		SyntaxNode? current = node.Parent;
		while (current != null)
		{
			if (current is BaseNamespaceDeclarationSyntax namespaceDeclaration)
			{
				names.Insert(0, namespaceDeclaration.Name.ToString());
			}
			current = current.Parent;
		}

		return string.Join(".", names);
	}

	private static string? GetComponentIdName(ExpressionSyntax expression)
	{
		if (expression is MemberAccessExpressionSyntax memberAccess && memberAccess.Expression is IdentifierNameSyntax identifier && identifier.Identifier.Text == "ComponentIds")
		{
			return memberAccess.Name.Identifier.Text;
		}

		return null;
	}

	private static int EvaluateIntExpression(ExpressionSyntax expression, Dictionary<string, int> constants)
	{
		if (expression is LiteralExpressionSyntax literal && literal.Token.Value is int literalValue) return literalValue;
		if (expression is IdentifierNameSyntax identifier && constants.TryGetValue(identifier.Identifier.Text, out int constantValue)) return constantValue;
		if (expression is BinaryExpressionSyntax binary && binary.IsKind(SyntaxKind.AddExpression))
		{
			return EvaluateIntExpression(binary.Left, constants) + EvaluateIntExpression(binary.Right, constants);
		}

		return 0;
	}

	private static string ToUpperSnakeCase(string name)
	{
		var builder = new StringBuilder();
		for (int index = 0; index < name.Length; index++)
		{
			char character = name[index];
			if (index > 0 && char.IsUpper(character) && (char.IsLower(name[index - 1]) || (index + 1 < name.Length && char.IsLower(name[index + 1]))))
			{
				builder.Append('_');
			}
			builder.Append(char.ToUpperInvariant(character));
		}

		return builder.ToString();
	}

	private static string TransformExtensionBlocks(string source)
	{
		var builder = new StringBuilder(source);
		MatchCollection matches = ExtensionHeaderRegex.Matches(source);
		for (int index = matches.Count - 1; index >= 0; index--)
		{
			Match match = matches[index];
			string typeName = match.Groups[1].Value;
			string replacement = $"class __RealmExtension_{typeName}";
			builder.Remove(match.Index, match.Length);
			builder.Insert(match.Index, replacement);
		}

		return builder.ToString();
	}
}
