using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;

namespace Realm.WasmBindgen;

public static class WasmLldbScriptWriter
{
	public static void GenerateLldbScript(string repoRoot, Compilation compilation, string? targetPathOverride = null)
	{
		string targetPath = targetPathOverride ?? Path.Combine(repoRoot, "MapTemplate", "realm_lldb.py");
		string templatePath = Path.Combine(repoRoot, "Realm.WasmBindgen", "realm_lldb.template.py");

		string templateContent = File.Exists(templatePath)
			? File.ReadAllText(templatePath)
			: "";

		if (string.IsNullOrEmpty(templateContent)) return;

		Dictionary<string, int> sharedConstants = DiscoverSharedConstants(compilation);
		Dictionary<string, ComponentLayout> componentLayouts = WasmDebugTypeDiscovery.DiscoverComponentLayouts(compilation, repoRoot);
		Dictionary<string, int> componentIds = WasmDebugTypeDiscovery.DiscoverComponentIds(compilation);
		Dictionary<string, int> componentOffsets = WasmDebugTypeDiscovery.DiscoverComponentOffsets(compilation, repoRoot, componentLayouts, componentIds);
		Dictionary<string, HandleTypeLayout> handleTypes = WasmDebugTypeDiscovery.DiscoverHandleTypes(compilation, repoRoot, componentLayouts, componentIds, sharedConstants);
		Dictionary<string, List<string>> hostTypes = DiscoverHostTypes(compilation);

		var builder = new StringBuilder();
		builder.AppendLine("# Constants auto-discovered from EcsSharedMemory");
		foreach ((string name, int value) in sharedConstants.OrderBy(pair => pair.Key, StringComparer.Ordinal))
		{
			builder.AppendLine($"{name} = {value}");
		}

		builder.AppendLine();
		builder.AppendLine("# Component array offsets auto-discovered from EcsWorld.BindSharedMemory");
		builder.AppendLine("COMPONENT_OFFSETS = {");
		foreach ((string name, int offset) in componentOffsets.OrderBy(pair => pair.Key, StringComparer.Ordinal))
		{
			builder.AppendLine($"    \"{name}\": {offset},");
		}
		builder.AppendLine("}");

		builder.AppendLine();
		builder.AppendLine("# Component element sizes auto-calculated from struct layouts");
		builder.AppendLine("COMPONENT_SIZES = {");
		foreach (string componentName in componentOffsets.Keys.OrderBy(name => name, StringComparer.Ordinal))
		{
			if (!componentLayouts.TryGetValue(componentName, out ComponentLayout? layout)) continue;
			builder.AppendLine($"    \"{layout.Name}\": {layout.Size},");
		}
		builder.AppendLine("}");

		builder.AppendLine();
		builder.AppendLine("# Component identifiers auto-discovered from ComponentIds");
		builder.AppendLine("COMPONENT_IDS = {");
		foreach ((string name, int id) in componentIds.OrderBy(pair => pair.Key, StringComparer.Ordinal))
		{
			builder.AppendLine($"    \"{name}\": {id},");
		}
		builder.AppendLine("}");

		builder.AppendLine();
		builder.AppendLine("# Handle types and their readable properties auto-discovered from Realm.Ecs and Realm.MapAPI");
		builder.AppendLine("HANDLE_TYPES = {");
		foreach (HandleTypeLayout handleType in handleTypes.Values.OrderBy(handle => handle.Name, StringComparer.Ordinal))
		{
			builder.AppendLine($"    \"{handleType.Name}\": {{");
			builder.AppendLine($"        \"name\": \"{handleType.Name}\",");
			builder.AppendLine($"        \"label\": \"{handleType.Label}\",");
			builder.AppendLine($"        \"value_field\": \"{handleType.ValueField}\",");
			builder.AppendLine($"        \"min_value\": {handleType.MinValue},");
			builder.AppendLine($"        \"max_value\": {handleType.MaxValue},");
			builder.AppendLine("        \"fields\": [");
			foreach (HandleFieldSpec field in handleType.Fields)
			{
				builder.AppendLine($"            {FormatField(field)},");
			}
			builder.AppendLine("        ],");
			builder.AppendLine($"        \"summary_fields\": [{string.Join(", ", handleType.SummaryFields.Select(name => $"\"{name}\""))}],");
			builder.AppendLine("    },");
		}
		builder.AppendLine("}");

		builder.AppendLine();
		builder.AppendLine("# Host wrapper types whose properties cross the WASM boundary and cannot be inspected without running threads");
		builder.AppendLine("HOST_TYPES = {");
		foreach ((string typeName, List<string> propertyNames) in hostTypes.OrderBy(pair => pair.Key, StringComparer.Ordinal))
		{
			string label = typeName.EndsWith("_WasmModule", StringComparison.Ordinal) ? typeName.Substring(0, typeName.Length - "_WasmModule".Length) : typeName;
			builder.AppendLine($"    \"{typeName}\": {{");
			builder.AppendLine($"        \"name\": \"{typeName}\",");
			builder.AppendLine($"        \"label\": \"{label}\",");
			builder.AppendLine("        \"fields\": [");
			foreach (string propertyName in propertyNames)
			{
				builder.AppendLine($"            {{ \"name\": \"{propertyName}\" }},");
			}
			builder.AppendLine("        ],");
			builder.AppendLine("    },");
		}
		builder.AppendLine("}");

		string scriptContent = templateContent.Replace("# {{OFFSETS}}", builder.ToString().TrimEnd())
			.Replace("{{OFFSETS}}", builder.ToString().TrimEnd());

		try
		{
			if (!File.Exists(targetPath) || File.ReadAllText(targetPath, Encoding.UTF8) != scriptContent)
			{
				File.WriteAllText(targetPath, scriptContent, Encoding.UTF8);
			}
		}
		catch
		{
		}
	}

	private static Dictionary<string, List<string>> DiscoverHostTypes(Compilation compilation)
	{
		var hostTypes = new Dictionary<string, List<string>>(StringComparer.Ordinal);
		INamedTypeSymbol? gameApiSymbol = compilation.GetTypeByMetadataName("Realm.MapAPI.IGameAPI");
		if (gameApiSymbol == null) return hostTypes;

		List<string> gameApiProperties = gameApiSymbol.GetMembers()
			.OfType<IPropertySymbol>()
			.Select(property => property.Name)
			.Distinct(StringComparer.Ordinal)
			.OrderBy(name => name, StringComparer.Ordinal)
			.ToList();

		hostTypes["IGameAPI"] = gameApiProperties;
		hostTypes["GameAPI_WasmModule"] = gameApiProperties;

		foreach (INamedTypeSymbol entityInterface in WasmEntityDiscovery.DiscoverEntityInterfaces(gameApiSymbol))
		{
			List<string> entityProperties = entityInterface.GetMembers()
				.OfType<IPropertySymbol>()
				.Select(property => property.Name)
				.Distinct(StringComparer.Ordinal)
				.OrderBy(name => name, StringComparer.Ordinal)
				.ToList();

			hostTypes[entityInterface.Name] = entityProperties;
			hostTypes[WitIdentifierFormat.CleanInterfaceName(entityInterface) + "_WasmModule"] = entityProperties;
		}

		return hostTypes;
	}

	private static Dictionary<string, int> DiscoverSharedConstants(Compilation compilation)
	{
		var constants = new Dictionary<string, int>(StringComparer.Ordinal);
		INamedTypeSymbol? sharedMemorySymbol = compilation.GetTypeByMetadataName("Realm.Ecs.EcsSharedMemory");
		if (sharedMemorySymbol == null) return constants;

		foreach (IFieldSymbol fieldSymbol in sharedMemorySymbol.GetMembers().OfType<IFieldSymbol>())
		{
			if (fieldSymbol.IsConst && fieldSymbol.HasConstantValue && fieldSymbol.ConstantValue is int intValue)
			{
				constants[fieldSymbol.Name] = intValue;
			}
		}

		return constants;
	}

	private static string FormatField(HandleFieldSpec field)
	{
		var parts = new List<string>
		{
			$"\"name\": \"{field.Name}\"",
			$"\"kind\": \"{field.Kind}\"",
			$"\"type\": \"{field.TypeName}\""
		};

		if (field.ComponentName != null) parts.Add($"\"component\": \"{field.ComponentName}\"");
		if (field.ComponentId >= 0) parts.Add($"\"component_id\": {field.ComponentId}");
		if (field.SubOffset != 0) parts.Add($"\"sub_offset\": {field.SubOffset}");
		if (field.IndexOffset != 0) parts.Add($"\"index_offset\": {field.IndexOffset}");
		if (field.RequiresAlive) parts.Add("\"requires_alive\": True");
		if (field.Invert) parts.Add("\"invert\": True");
		if (field.Kind == "valid")
		{
			parts.Add($"\"min\": {field.MinValue}");
			parts.Add($"\"max\": {field.MaxValue}");
		}
		if (field.Kind == "constant") parts.Add($"\"constant\": {FormatConstant(field.ConstantValue, field.TypeName)}");
		if (field.TagNames.Count > 0) parts.Add($"\"tags\": [{string.Join(", ", field.TagNames.Select(name => $"\"{name}\""))}]");
		if (field.TagIds.Count > 0) parts.Add($"\"tag_ids\": [{string.Join(", ", field.TagIds)}]");
		if (field.DefaultValue != null) parts.Add($"\"default\": {FormatConstant(field.DefaultValue, field.TypeName)}");

		return "{ " + string.Join(", ", parts) + " }";
	}

	private static string FormatConstant(object? value, string typeName)
	{
		if (value == null) return "None";
		if (typeName == "bool") return value is true ? "True" : "False";
		if (typeName == "float") return Convert.ToSingle(value, CultureInfo.InvariantCulture).ToString("R", CultureInfo.InvariantCulture);
		return Convert.ToInt32(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);
	}

	public static List<(string Name, int Offset, string TypeName)> CalculateStructLayout(INamedTypeSymbol structSymbol, out int totalSize)
	{
		var fields = new List<(string Name, int Offset, string TypeName)>();
		int currentOffset = 0;
		int maxAlignment = 1;
		const int pack = 4;

		foreach (var member in structSymbol.GetMembers())
		{
			if (member is IFieldSymbol field && !field.IsStatic)
			{
				int fieldSize = field.IsFixedSizeBuffer ? field.FixedSize * 4 : GetFieldSize(field.Type);
				int fieldAlign = Math.Min(GetFieldAlignment(field.Type), pack);
				currentOffset = (currentOffset + (fieldAlign - 1)) & ~(fieldAlign - 1);
				maxAlignment = Math.Max(maxAlignment, fieldAlign);

				if (!field.Name.StartsWith("Reserved", StringComparison.OrdinalIgnoreCase) && !field.IsFixedSizeBuffer)
				{
					string pyTypeName = GetPyTypeName(field);
					fields.Add((field.Name, currentOffset, pyTypeName));
				}

				currentOffset += fieldSize;
			}
		}

		totalSize = (currentOffset + (maxAlignment - 1)) & ~(maxAlignment - 1);
		return fields;
	}

	private static int GetPrimitiveSize(ITypeSymbol type)
	{
		if (type.SpecialType == SpecialType.System_Boolean || type.SpecialType == SpecialType.System_Byte || type.SpecialType == SpecialType.System_SByte)
			return 1;
		if (type.SpecialType == SpecialType.System_Int16 || type.SpecialType == SpecialType.System_UInt16)
			return 2;
		if (type.SpecialType == SpecialType.System_Int32 || type.SpecialType == SpecialType.System_UInt32 || type.SpecialType == SpecialType.System_Single)
			return 4;
		if (type.SpecialType == SpecialType.System_Int64 || type.SpecialType == SpecialType.System_UInt64 || type.SpecialType == SpecialType.System_Double)
			return 8;
		return 0;
	}

	private static int GetFieldSize(ITypeSymbol type)
	{
		int primSize = GetPrimitiveSize(type);
		if (primSize > 0) return primSize;

		if (type.ToDisplayString() == "System.Numerics.Vector2")
			return 8;
		if (type.ToDisplayString() == "System.Numerics.Vector3")
			return 12;
		if (type.Name.Contains("FixedBuffer"))
			return 32;

		AttributeData? inlineArray = type.GetAttributes().FirstOrDefault(attribute => attribute.AttributeClass?.Name == "InlineArrayAttribute");
		if (inlineArray != null && inlineArray.ConstructorArguments.Length == 1 && inlineArray.ConstructorArguments[0].Value is int elementCount)
		{
			IFieldSymbol? elementField = type.GetMembers().OfType<IFieldSymbol>().FirstOrDefault();
			if (elementField != null) return GetFieldSize(elementField.Type) * elementCount;
		}

		return 4;
	}

	private static int GetFieldAlignment(ITypeSymbol type)
	{
		int primSize = GetPrimitiveSize(type);
		if (primSize > 0) return primSize;

		if (type.ToDisplayString() == "System.Numerics.Vector2" || type.ToDisplayString() == "System.Numerics.Vector3")
			return 4;
		return 4;
	}

	private static string GetPyTypeName(IFieldSymbol field)
	{
		var type = field.Type;
		if (type.SpecialType == SpecialType.System_Single) return "float";
		if (type.SpecialType == SpecialType.System_Boolean) return "bool";
		if (type.SpecialType == SpecialType.System_Byte)
		{
			if (field.Name.StartsWith("Is", StringComparison.Ordinal) || field.Name == "Alive" || field.Name == "Invulnerable")
				return "bool";
			return "int";
		}
		if (type.SpecialType == SpecialType.System_Int32) return "int";
		if (type.ToDisplayString() == "System.Numerics.Vector3") return "Vector3";
		return "float";
	}
}
