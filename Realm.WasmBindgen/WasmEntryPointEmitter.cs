using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;

namespace Realm.WasmBindgen;

internal static class WasmEntryPointEmitter
{
	internal static void GenerateWasmEntryPoint(string repoRoot, INamedTypeSymbol gameApiSymbol)
	{
		string templatePath = Path.Combine(repoRoot, "MapTemplate", "WasmEntryPoint.template.cs");
		if (!File.Exists(templatePath)) return;

		string templateContent = File.ReadAllText(templatePath);
		var stringBuilder = new StringBuilder();

		var events = gameApiSymbol.GetMembers()
			.OfType<IEventSymbol>()
			.Where(e => e.Name.StartsWith("On"))
			.OrderBy(e => e.Name, StringComparer.Ordinal);

		foreach (var eventSymbol in events)
		{
			EmitWasmEntryPointEventHandler(stringBuilder, eventSymbol);
		}

		string handlersCode = stringBuilder.ToString().TrimEnd();
		string generatedCode = templateContent.Contains("{{GENERATED_EVENT_HANDLERS}}")
			? templateContent.Replace("{{GENERATED_EVENT_HANDLERS}}", handlersCode)
			: templateContent + "\n" + handlersCode + "\n";

		string targetPath = Path.Combine(repoRoot, "MapTemplate", "WasmEntryPoint.g.cs");
		try
		{
			if (File.Exists(targetPath))
			{
				string existing = File.ReadAllText(targetPath);
				if (string.Equals(existing.Replace("\r\n", "\n"), generatedCode.Replace("\r\n", "\n"), StringComparison.Ordinal))
				{
					return;
				}

				var attributes = File.GetAttributes(targetPath);
				if ((attributes & FileAttributes.ReadOnly) != 0)
				{
					File.SetAttributes(targetPath, attributes & ~FileAttributes.ReadOnly);
				}
			}

			File.WriteAllText(targetPath, generatedCode, Encoding.UTF8);
		}
		catch
		{
		}
	}

	internal static void EmitWasmEntryPointEventHandler(StringBuilder stringBuilder, IEventSymbol eventSymbol)
	{
		var delegateMethod = ((INamedTypeSymbol)eventSymbol.Type).DelegateInvokeMethod;
		if (delegateMethod == null) return;

		string entryPointName = WitIdentifierFormat.ToKebabCase(eventSymbol.Name);
		string triggerMethodName = "TriggerOn" + (eventSymbol.Name.StartsWith("On") ? eventSymbol.Name.Substring(2) : eventSymbol.Name);
		var parameters = delegateMethod.Parameters;

		var methodParameters = new List<string>();
		var bodyStatements = new List<string>();
		var invokeArguments = new List<string>();

		var parameterNames = GenerateWasmEntryPointParameterNames(parameters);

		for (int parameterIndex = 0; parameterIndex < parameters.Length; parameterIndex++)
		{
			var parameter = parameters[parameterIndex];
			ITypeSymbol parameterType = parameter.Type;
			string parameterName = parameterNames[parameterIndex];

			if (parameterType.SpecialType == SpecialType.System_String)
			{
				methodParameters.Add($"nint {parameterName}Ptr, int {parameterName}Len");
				bodyStatements.Add($"        string {parameterName} = ReadString({parameterName}Ptr, {parameterName}Len);");
				invokeArguments.Add(parameterName);
			}
			else if (parameterType.ToDisplayString() == "System.Numerics.Vector3" || WasmTypeClassifier.IsNullableVector3(parameterType))
			{
				methodParameters.Add($"float {parameterName}X, float {parameterName}Y, float {parameterName}Z");
				invokeArguments.Add($"new Vector3({parameterName}X, {parameterName}Y, {parameterName}Z)");
			}
			else if (parameterType.ToDisplayString() == "System.Numerics.Vector2")
			{
				methodParameters.Add($"float {parameterName}X, float {parameterName}Y");
				invokeArguments.Add($"new Vector2({parameterName}X, {parameterName}Y)");
			}
			else if (WasmEntityDiscovery.IsEntityInterface(parameterType, out _) || WasmEntityDiscovery.IsEntityIdStruct(parameterType, out _))
			{
				methodParameters.Add($"int {parameterName}");
				invokeArguments.Add(parameterName);
			}
			else if (parameterType.SpecialType == SpecialType.System_Boolean)
			{
				methodParameters.Add($"int {parameterName}");
				invokeArguments.Add($"{parameterName} != 0");
			}
			else if (parameterType.TypeKind == TypeKind.Enum)
			{
				methodParameters.Add($"int {parameterName}");
				invokeArguments.Add($"({GetCleanSymbolTypeName(parameterType)}){parameterName}");
			}
			else
			{
				string primitiveTypeName = GetCleanSymbolTypeName(parameterType);
				methodParameters.Add($"{primitiveTypeName} {parameterName}");
				invokeArguments.Add(parameterName);
			}
		}

		stringBuilder.AppendLine($"    [UnmanagedCallersOnly(EntryPoint = \"{entryPointName}\")]");
		stringBuilder.AppendLine($"    public static void {eventSymbol.Name}({string.Join(", ", methodParameters)})");
		stringBuilder.AppendLine("    {");
		foreach (var statement in bodyStatements)
		{
			stringBuilder.AppendLine(statement);
		}
		stringBuilder.AppendLine($"        if (_gameApi is GameAPI_WasmModule api) api.{triggerMethodName}({string.Join(", ", invokeArguments)});");
		stringBuilder.AppendLine("    }");
		stringBuilder.AppendLine();
	}

	internal static List<string> GenerateWasmEntryPointParameterNames(ImmutableArray<IParameterSymbol> parameters)
	{
		var baseNames = new List<string>(parameters.Length);
		for (int parameterIndex = 0; parameterIndex < parameters.Length; parameterIndex++)
		{
			var parameter = parameters[parameterIndex];
			if (HasAuthoredSymbolParameterName(parameter))
			{
				baseNames.Add(parameter.Name);
			}
			else
			{
				baseNames.Add(GetDefaultBaseSymbolParameterName(parameter.Type));
			}
		}

		var counts = new Dictionary<string, int>(StringComparer.Ordinal);
		foreach (var baseName in baseNames)
		{
			counts[baseName] = counts.TryGetValue(baseName, out int current) ? current + 1 : 1;
		}

		var seenIndices = new Dictionary<string, int>(StringComparer.Ordinal);
		var resolvedNames = new List<string>(parameters.Length);

		for (int parameterIndex = 0; parameterIndex < parameters.Length; parameterIndex++)
		{
			string baseName = baseNames[parameterIndex];
			int totalCount = counts[baseName];

			if (totalCount == 1)
			{
				resolvedNames.Add(baseName);
			}
			else
			{
				int occurrenceIndex = seenIndices.TryGetValue(baseName, out int currentSeen) ? currentSeen + 1 : 1;
				seenIndices[baseName] = occurrenceIndex;

				if (baseName.EndsWith("Id", StringComparison.Ordinal) && totalCount == 2)
				{
					resolvedNames.Add(occurrenceIndex == 1 ? $"source{char.ToUpperInvariant(baseName[0])}{baseName.Substring(1)}" : $"target{char.ToUpperInvariant(baseName[0])}{baseName.Substring(1)}");
				}
				else
				{
					resolvedNames.Add($"{baseName}{occurrenceIndex}");
				}
			}
		}

		return resolvedNames;
	}

	internal static bool HasAuthoredSymbolParameterName(IParameterSymbol parameter)
	{
		if (string.IsNullOrWhiteSpace(parameter.Name)) return false;
		if (string.Equals(parameter.Name, "obj", StringComparison.OrdinalIgnoreCase)) return false;
		if (parameter.Name.StartsWith("arg", StringComparison.OrdinalIgnoreCase) &&
		    int.TryParse(parameter.Name.Substring(3), out _))
		{
			return false;
		}

		return true;
	}

	internal static string GetDefaultBaseSymbolParameterName(ITypeSymbol type)
	{
		if (WasmEntityDiscovery.IsEntityInterface(type, out var unwrapped))
		{
			string name = WitIdentifierFormat.CleanInterfaceName(unwrapped);
			return $"{char.ToLowerInvariant(name[0])}{name.Substring(1)}Id";
		}

		if (type.SpecialType == SpecialType.System_String)
		{
			return "text";
		}

		if (type.ToDisplayString() == "System.Numerics.Vector3" || type.ToDisplayString() == "System.Numerics.Vector2")
		{
			return "position";
		}

		if (type.SpecialType == SpecialType.System_Single || type.SpecialType == SpecialType.System_Double)
		{
			return "amount";
		}

		if (type.SpecialType == SpecialType.System_Boolean)
		{
			return "flag";
		}

		if (type.SpecialType == SpecialType.System_Int32 || type.SpecialType == SpecialType.System_Int64 || type.SpecialType == SpecialType.System_Int16 || type.SpecialType == SpecialType.System_Byte)
		{
			return "value";
		}

		if (type.TypeKind == TypeKind.Enum)
		{
			string enumName = type.Name;
			return $"{char.ToLowerInvariant(enumName[0])}{enumName.Substring(1)}";
		}

		return "param";
	}

	internal static string GetCleanSymbolTypeName(ITypeSymbol type)
	{
		if (type.SpecialType == SpecialType.System_Int32) return "int";
		if (type.SpecialType == SpecialType.System_Single) return "float";
		if (type.SpecialType == SpecialType.System_Boolean) return "bool";
		if (type.SpecialType == SpecialType.System_Double) return "double";
		if (type.SpecialType == SpecialType.System_Byte) return "byte";
		if (type.SpecialType == SpecialType.System_Int64) return "long";
		if (type.SpecialType == SpecialType.System_Int16) return "short";
		if (type.SpecialType == SpecialType.System_String) return "string";
		return type.Name;
	}
}
