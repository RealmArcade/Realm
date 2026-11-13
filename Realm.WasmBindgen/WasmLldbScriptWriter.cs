using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;

namespace Realm.WasmBindgen;

public static class WasmLldbScriptWriter
{
	public static void GenerateLldbScript(string repoRoot, Compilation compilation)
	{
		string targetPath = Path.Combine(repoRoot, "MapTemplate", "realm_lldb.py");
		string templatePath = Path.Combine(repoRoot, "Realm.WasmBindgen", "realm_lldb.template.py");

		string templateContent = File.Exists(templatePath)
			? File.ReadAllText(templatePath)
			: "";

		if (string.IsNullOrEmpty(templateContent)) return;

		var sharedMemorySymbol = compilation.GetTypeByMetadataName("Realm.Ecs.EcsSharedMemory");
		var constants = new List<(string Name, int Value)>();
		if (sharedMemorySymbol != null)
		{
			foreach (var member in sharedMemorySymbol.GetMembers())
			{
				if (member is IFieldSymbol fieldSymbol && fieldSymbol.IsConst && fieldSymbol.HasConstantValue && fieldSymbol.ConstantValue is int intVal)
				{
					constants.Add((fieldSymbol.Name, intVal));
				}
			}
		}

		var structSizes = new Dictionary<string, int>();
		var structSymbols = new[]
		{
			("POSITION", "Realm.MapAPI.Position"),
			("HEALTH", "Realm.MapAPI.Health"),
			("MANA", "Realm.MapAPI.Mana"),
			("ATTACK", "Realm.MapAPI.Attack"),
			("ARMOR", "Realm.MapAPI.Resistances"),
			("MOVEMENT_STATS", "Realm.MapAPI.MovementStats"),
			("UNIT_OWNER_PLAYER", "Realm.MapAPI.UnitOwnerPlayer"),
			("LEVEL", "Realm.MapAPI.Level"),
			("EXPERIENCE", "Realm.MapAPI.Experience"),
			("MODEL_SCALE", "Realm.MapAPI.ModelScale"),
			("ROTATION_Y", "Realm.MapAPI.RotationY"),
			("PLAYER_RESOURCES", "Realm.MapAPI.PlayerResources"),
			("SCRIPT_PLAYER", "Realm.MapAPI.ScriptPlayer"),
			("PLAYER_POPULATION", "Realm.MapAPI.PlayerPopulation"),
		};

		foreach (var (prefix, typeName) in structSymbols)
		{
			var sym = compilation.GetTypeByMetadataName(typeName) ??
			          compilation.GetTypeByMetadataName($"Realm.MapAPI.Components.{typeName.Substring("Realm.MapAPI.".Length)}");
			if (sym != null)
			{
				CalculateStructLayout(sym, out int size);
				structSizes[prefix] = size;
			}
			else
			{
				structSizes[prefix] = 0;
			}
		}

		var componentIdsSymbol = compilation.GetTypeByMetadataName("Realm.Ecs.ComponentIds");
		int GetCompId(string name, int fallback)
		{
			if (componentIdsSymbol != null)
			{
				var member = componentIdsSymbol.GetMembers(name).OfType<IFieldSymbol>().FirstOrDefault();
				if (member != null && member.IsConst && member.HasConstantValue && member.ConstantValue is int val)
				{
					return val;
				}
			}
			return fallback;
		}

		var playerFields = new List<(string Name, int CompId, string ArrayOffsetName, string ElemSizeName, int SubOffset, string TypeName)>
		{
			("Id", -1, "0", "0", 0, "int"),
			("Gold", GetCompId("PlayerResources", 69), "PLAYER_RESOURCES_OFFSET", "PLAYER_RESOURCES_SIZE", 0, "int"),
			("Wood", GetCompId("PlayerResources", 69), "PLAYER_RESOURCES_OFFSET", "PLAYER_RESOURCES_SIZE", 4, "int"),
			("Stone", GetCompId("PlayerResources", 69), "PLAYER_RESOURCES_OFFSET", "PLAYER_RESOURCES_SIZE", 8, "int"),
			("Ore", GetCompId("PlayerResources", 69), "PLAYER_RESOURCES_OFFSET", "PLAYER_RESOURCES_SIZE", 12, "int"),
			("IsActive", GetCompId("ScriptPlayer", 31), "SCRIPT_PLAYER_OFFSET", "SCRIPT_PLAYER_SIZE", 0, "bool"),
			("IsComputer", GetCompId("ScriptPlayer", 31), "SCRIPT_PLAYER_OFFSET", "SCRIPT_PLAYER_SIZE", 1, "bool"),
			("Team", GetCompId("ScriptPlayer", 31), "SCRIPT_PLAYER_OFFSET", "SCRIPT_PLAYER_SIZE", 4, "int"),
			("CurrentPopulation", GetCompId("PlayerPopulation", 24), "PLAYER_POPULATION_OFFSET", "PLAYER_POPULATION_SIZE", 0, "int"),
			("MaxPopulation", GetCompId("PlayerPopulation", 24), "PLAYER_POPULATION_OFFSET", "PLAYER_POPULATION_SIZE", 4, "int"),
		};

		var entityFields = new List<(string Name, int CompId, string ArrayOffsetName, string ElemSizeName, int SubOffset, string TypeName)>
		{
			("Id", -1, "0", "0", 0, "int"),
			("Health", GetCompId("Health", 1), "HEALTH_OFFSET", "HEALTH_SIZE", 0, "float"),
			("MaxHealth", GetCompId("Health", 1), "HEALTH_OFFSET", "HEALTH_SIZE", 4, "float"),
			("Mana", GetCompId("Mana", 3), "MANA_OFFSET", "MANA_SIZE", 0, "float"),
			("MaxMana", GetCompId("Mana", 3), "MANA_OFFSET", "MANA_SIZE", 4, "float"),
			("ManaRegen", GetCompId("Mana", 3), "MANA_OFFSET", "MANA_SIZE", 8, "float"),
			("Damage", GetCompId("Attack", 43), "ATTACK_OFFSET", "ATTACK_SIZE", 0, "float"),
			("Range", GetCompId("Attack", 43), "ATTACK_OFFSET", "ATTACK_SIZE", 4, "float"),
			("AttackSpeed", GetCompId("Attack", 43), "ATTACK_OFFSET", "ATTACK_SIZE", 8, "float"),
			("Armor", GetCompId("Resistances", 48), "ARMOR_OFFSET", "ARMOR_SIZE", 0, "float"),
			("MagicResistance", GetCompId("Resistances", 48), "ARMOR_OFFSET", "ARMOR_SIZE", 4, "float"),
			("Speed", GetCompId("MovementStats", 57), "MOVEMENT_STATS_OFFSET", "MOVEMENT_STATS_SIZE", 0, "float"),
			("Position", GetCompId("Position", 0), "POSITION_OFFSET", "POSITION_SIZE", 0, "Vector3"),
			("Player", GetCompId("UnitOwnerPlayer", 37), "UNIT_OWNER_PLAYER_OFFSET", "UNIT_OWNER_PLAYER_SIZE", 0, "int"),
			("Level", GetCompId("Level", 106), "LEVEL_OFFSET", "LEVEL_SIZE", 0, "int"),
			("Experience", GetCompId("Experience", 104), "EXPERIENCE_OFFSET", "EXPERIENCE_SIZE", 0, "float"),
			("Scale", GetCompId("ModelScale", 108), "MODEL_SCALE_OFFSET", "MODEL_SCALE_SIZE", 0, "float"),
			("RotationY", GetCompId("RotationY", 112), "ROTATION_Y_OFFSET", "ROTATION_Y_SIZE", 0, "float"),
		};

		var offsetsBuilder = new StringBuilder();
		offsetsBuilder.AppendLine("# Constants auto-discovered from EcsSharedMemory");
		foreach (var (name, value) in constants)
		{
			offsetsBuilder.AppendLine($"{name} = {value}");
		}

		offsetsBuilder.AppendLine();
		offsetsBuilder.AppendLine("# Element sizes auto-calculated from struct layouts");
		foreach (var (prefix, size) in structSizes)
		{
			offsetsBuilder.AppendLine($"{prefix}_SIZE = {size}");
		}

		offsetsBuilder.AppendLine();
		offsetsBuilder.AppendLine("# Fields auto-discovered for PlayerId (name, comp_id, array_offset, elem_size, sub_offset, type_name)");
		offsetsBuilder.AppendLine("PLAYER_FIELDS = [");
		foreach (var f in playerFields)
		{
			offsetsBuilder.AppendLine($"    (\"{f.Name}\", {f.CompId}, {f.ArrayOffsetName}, {f.ElemSizeName}, {f.SubOffset}, \"{f.TypeName}\"),");
		}
		offsetsBuilder.AppendLine("]");

		offsetsBuilder.AppendLine();
		offsetsBuilder.AppendLine("# Fields auto-discovered for UnitId (name, comp_id, array_offset, elem_size, sub_offset, type_name)");
		offsetsBuilder.AppendLine("ENTITY_FIELDS = [");
		foreach (var f in entityFields)
		{
			offsetsBuilder.AppendLine($"    (\"{f.Name}\", {f.CompId}, {f.ArrayOffsetName}, {f.ElemSizeName}, {f.SubOffset}, \"{f.TypeName}\"),");
		}
		offsetsBuilder.AppendLine("]");

		offsetsBuilder.AppendLine();
		offsetsBuilder.AppendLine("PLAYER_OFFSETS = {name: (comp_id, array_offset, elem_size, sub_offset, type_name) for name, comp_id, array_offset, elem_size, sub_offset, type_name in PLAYER_FIELDS}");
		offsetsBuilder.AppendLine("ENTITY_OFFSETS = {name: (comp_id, array_offset, elem_size, sub_offset, type_name) for name, comp_id, array_offset, elem_size, sub_offset, type_name in ENTITY_FIELDS}");

		string scriptContent = templateContent.Replace("# {{OFFSETS}}", offsetsBuilder.ToString().TrimEnd())
			.Replace("{{OFFSETS}}", offsetsBuilder.ToString().TrimEnd());

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
