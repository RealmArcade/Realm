using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Realm.WasmBindgen;

[Generator]
public partial class WasmLinkerGenerator : IIncrementalGenerator
{
	public void Initialize(IncrementalGeneratorInitializationContext context)
	{
		var compilationProvider = context.CompilationProvider;

		var witFileProvider = context.AdditionalTextsProvider
			.Where(file => Path.GetFileName(file.Path).Equals("game.wit", StringComparison.OrdinalIgnoreCase))
			.Select((file, cancellationToken) => file.GetText(cancellationToken)?.ToString() ?? "");

		var combined = compilationProvider.Combine(witFileProvider.Collect()).Combine(context.AnalyzerConfigOptionsProvider);

		context.RegisterSourceOutput(combined, static (context, input) => Execute(context, input.Left.Left, input.Left.Right, input.Right));
	}

	private const string DefaultHostAssembly = "Realm.Client";

	private const string DefaultHostNamespace = "Realm.Client.Core";

	private const string DefaultHostType = "WasmRuntime";

	private static void Execute(SourceProductionContext context, Compilation compilation, ImmutableArray<string> witFiles, AnalyzerConfigOptionsProvider options)
	{
		var gameApiSymbol = compilation.GetTypeByMetadataName("Realm.MapAPI.IGameAPI");
		if (gameApiSymbol == null)
			return;

		var host = ResolveHostType(options);
		var entityInterfaces = WasmEntityDiscovery.DiscoverEntityInterfaces(gameApiSymbol);
		string staticWit = witFiles.FirstOrDefault() ?? "";
		string manualFunctions = ExtractManualFunctions(staticWit);

		string witContent = WasmWitDocumentWriter.GenerateWitContent(gameApiSymbol, entityInterfaces, manualFunctions);
		string mapApiWitPath = FindMapApiWitPath(compilation);
		WriteWitContent(mapApiWitPath, witContent);

		string? repoRoot = FindRepoRoot(compilation);
		string ecsComponents = !string.IsNullOrEmpty(repoRoot) ? GenerateEcsComponents(repoRoot) : "";
		Compilation lldbCompilation = compilation;
		if (!string.IsNullOrEmpty(ecsComponents))
		{
			try
			{
				var parseOptions = compilation.SyntaxTrees.FirstOrDefault()?.Options as CSharpParseOptions;
				var tree = CSharpSyntaxTree.ParseText(ecsComponents, parseOptions);
				lldbCompilation = compilation.AddSyntaxTrees(tree);
			}
			catch
			{
				lldbCompilation = compilation;
			}
		}

		if (!string.IsNullOrEmpty(repoRoot))
		{
			WasmEntryPointEmitter.GenerateWasmEntryPoint(repoRoot, gameApiSymbol);
			string templateWitPath = Path.Combine(repoRoot, "MapTemplate", "wit", "game.g.wit");
			WriteWitContent(templateWitPath, witContent);
			GenerateLldbScript(repoRoot, lldbCompilation);
			GenerateNatvisFile(repoRoot, lldbCompilation);
		}

		GenerateOutputSources(context, compilation.AssemblyName ?? "", host, gameApiSymbol, entityInterfaces, manualFunctions, repoRoot, ecsComponents);
	}

	private static string ExtractManualFunctions(string staticWit)
	{
		if (string.IsNullOrEmpty(staticWit))
			return "";

		var match = GameApiInterfaceRegex().Match(staticWit);
		return match.Success ? match.Groups[1].Value.Trim() : "";
	}

	private static string FindMapApiWitPath(Compilation compilation)
	{
		var syntaxTree = compilation.SyntaxTrees.FirstOrDefault(t => !string.IsNullOrEmpty(t.FilePath));
		if (syntaxTree == null)
			throw new Exception(@"[WasmLinkerGenerator] Error: unable to determine path for Realm.MapAPI\wit\game.g.wit");

		string? dir = Path.GetDirectoryName(syntaxTree.FilePath);
		while (dir != null)
		{
			string candidate = Path.Combine(dir, "Realm.MapAPI", "wit", "game.g.wit");
			if (File.Exists(candidate) || Directory.Exists(Path.GetDirectoryName(candidate)))
				return candidate;

			if (Path.GetFileName(dir) == "Realm.MapAPI")
			{
				string candidateInProject = Path.Combine(dir, "wit", "game.g.wit");
				if (File.Exists(candidateInProject) || Directory.Exists(Path.GetDirectoryName(candidateInProject)))
					return candidateInProject;
			}

			dir = Path.GetDirectoryName(dir);
		}

		throw new Exception(@"[WasmLinkerGenerator] Error: unable to determine path for Realm.MapAPI\wit\game.g.wit");
	}

	private static void WriteWitContent(string mapApiWitPath, string witContent)
	{
		string? dir = Path.GetDirectoryName(mapApiWitPath);
		if (dir != null && Directory.Exists(dir))
		{
			if (!File.Exists(mapApiWitPath) || File.ReadAllText(mapApiWitPath, Encoding.UTF8) != witContent)
			{
				File.WriteAllText(mapApiWitPath, witContent, Encoding.UTF8);
			}
		}
	}

	private static void GenerateOutputSources(SourceProductionContext context, string assemblyName, (string Assembly, string Namespace, string Type) host, INamedTypeSymbol gameApiSymbol, HashSet<INamedTypeSymbol> entityInterfaces, string manualFunctions, string? repoRoot, string ecsComponents)
	{
		if (assemblyName == host.Assembly)
		{
			context.AddSource("WasmRuntime.g.cs",
				SourceText.From(WasmHostBindingEmitter.GenerateHostBindings(gameApiSymbol, entityInterfaces, host.Namespace, host.Type), Encoding.UTF8));

			context.AddSource("WasmRuntime.AutoEvents.g.cs",
				SourceText.From(WasmAutoEventEmitter.GenerateAutoEvents(gameApiSymbol, host.Namespace, host.Type), Encoding.UTF8));

			context.AddSource("GeneratedWit.g.cs",
				SourceText.From(GenerateWitConstant(gameApiSymbol, entityInterfaces, manualFunctions, host.Namespace), Encoding.UTF8));
		}
		else if (assemblyName == "Realm.MapAPI")
		{
			context.AddSource("WasmWrappers.g.cs",
				SourceText.From(WasmGuestProxyEmitter.GenerateWasmWrappers(gameApiSymbol, entityInterfaces), Encoding.UTF8));

			if (!string.IsNullOrEmpty(ecsComponents))
			{
				context.AddSource("EcsComponents.g.cs",
					SourceText.From(ecsComponents, Encoding.UTF8));
			}
		}
	}

	private static (string Assembly, string Namespace, string Type) ResolveHostType(AnalyzerConfigOptionsProvider options)
	{
		return (
			ReadOption(options, "build_property.RealmWasmHostAssembly", DefaultHostAssembly),
			ReadOption(options, "build_property.RealmWasmHostNamespace", DefaultHostNamespace),
			ReadOption(options, "build_property.RealmWasmHostType", DefaultHostType));
	}

	private static string ReadOption(AnalyzerConfigOptionsProvider options, string key, string fallback)
	{
		return options.GlobalOptions.TryGetValue(key, out string? value) && !string.IsNullOrWhiteSpace(value) ? value : fallback;
	}




	private static string GenerateWitConstant(INamedTypeSymbol gameApiSymbol, HashSet<INamedTypeSymbol> entityInterfaces, string manualFunctions, string hostNamespace)
	{
		string witBody = WasmWitDocumentWriter.GenerateWitContent(gameApiSymbol, entityInterfaces, manualFunctions);

		var wit = new StringBuilder();
		wit.AppendLine("// Do not edit file directly.");
		wit.AppendLine("// This file is auto-generated by the custom WasmLinkerGenerator tool.");
		wit.AppendLine("// Generator source: file:///D:/git/Realm/Realm.WasmBindgen/WasmLinkerGenerator.cs");
		wit.AppendLine();
		wit.AppendLine($"namespace {hostNamespace};");
		wit.AppendLine();
		wit.AppendLine("public static class GeneratedWit");
		wit.AppendLine("{");
		wit.AppendLine("    public const string Content = \"\"\"");
		wit.Append(witBody);
		wit.AppendLine("\"\"\";");
		wit.AppendLine("}");

		return wit.ToString();
	}



	private static string? FindRepoRoot(Compilation compilation)
	{
		var syntaxTree = compilation.SyntaxTrees.FirstOrDefault(t => !string.IsNullOrEmpty(t.FilePath));
		if (syntaxTree == null) return null;

		string? dir = Path.GetDirectoryName(syntaxTree.FilePath);
		while (dir != null)
		{
			if (Directory.Exists(Path.Combine(dir, "MapTemplate")) && Directory.Exists(Path.Combine(dir, "Realm.MapAPI")))
			{
				return dir;
			}

			dir = Path.GetDirectoryName(dir);
		}

		return null;
	}

	private static void GenerateLldbScript(string repoRoot, Compilation compilation)
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

		var playerFields = new List<(string Name, int CompId, string ArrayOffsetName, string ElemSizeName, int SubOffset, string TypeName)>
		{
			("Gold", 68, "PLAYER_RESOURCES_OFFSET", "PLAYER_RESOURCES_SIZE", 0, "int"),
			("Wood", 68, "PLAYER_RESOURCES_OFFSET", "PLAYER_RESOURCES_SIZE", 4, "int"),
			("Stone", 68, "PLAYER_RESOURCES_OFFSET", "PLAYER_RESOURCES_SIZE", 8, "int"),
			("Ore", 68, "PLAYER_RESOURCES_OFFSET", "PLAYER_RESOURCES_SIZE", 12, "int"),
			("IsActive", 31, "SCRIPT_PLAYER_OFFSET", "SCRIPT_PLAYER_SIZE", 0, "bool"),
			("IsComputer", 31, "SCRIPT_PLAYER_OFFSET", "SCRIPT_PLAYER_SIZE", 1, "bool"),
			("Team", 31, "SCRIPT_PLAYER_OFFSET", "SCRIPT_PLAYER_SIZE", 4, "int"),
			("CurrentPopulation", 24, "PLAYER_POPULATION_OFFSET", "PLAYER_POPULATION_SIZE", 0, "int"),
			("MaxPopulation", 24, "PLAYER_POPULATION_OFFSET", "PLAYER_POPULATION_SIZE", 4, "int"),
		};

		var entityFields = new List<(string Name, int CompId, string ArrayOffsetName, string ElemSizeName, int SubOffset, string TypeName)>
		{
			("Health", 1, "HEALTH_OFFSET", "HEALTH_SIZE", 0, "float"),
			("MaxHealth", 1, "HEALTH_OFFSET", "HEALTH_SIZE", 4, "float"),
			("Mana", 3, "MANA_OFFSET", "MANA_SIZE", 0, "float"),
			("MaxMana", 3, "MANA_OFFSET", "MANA_SIZE", 4, "float"),
			("ManaRegen", 3, "MANA_OFFSET", "MANA_SIZE", 8, "float"),
			("Damage", 43, "ATTACK_OFFSET", "ATTACK_SIZE", 0, "float"),
			("Range", 43, "ATTACK_OFFSET", "ATTACK_SIZE", 4, "float"),
			("AttackSpeed", 43, "ATTACK_OFFSET", "ATTACK_SIZE", 8, "float"),
			("Armor", 48, "ARMOR_OFFSET", "ARMOR_SIZE", 0, "float"),
			("MagicResistance", 48, "ARMOR_OFFSET", "ARMOR_SIZE", 4, "float"),
			("Speed", 57, "MOVEMENT_STATS_OFFSET", "MOVEMENT_STATS_SIZE", 0, "float"),
			("Position", 0, "POSITION_OFFSET", "POSITION_SIZE", 0, "Vector3"),
			("Player", 37, "UNIT_OWNER_PLAYER_OFFSET", "UNIT_OWNER_PLAYER_SIZE", 0, "int"),
			("Level", 105, "LEVEL_OFFSET", "LEVEL_SIZE", 0, "int"),
			("Experience", 103, "EXPERIENCE_OFFSET", "EXPERIENCE_SIZE", 0, "float"),
			("Scale", 107, "MODEL_SCALE_OFFSET", "MODEL_SCALE_SIZE", 0, "float"),
			("RotationY", 111, "ROTATION_Y_OFFSET", "ROTATION_Y_SIZE", 0, "float"),
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

	private static List<(string Name, int Offset, string TypeName)> CalculateStructLayout(INamedTypeSymbol structSymbol, out int totalSize)
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

	private static void GenerateNatvisFile(string repoRoot, Compilation compilation)
	{
		string targetPath = Path.Combine(repoRoot, "realm.natvis");
		string templatePath = Path.Combine(repoRoot, "Realm.WasmBindgen", "realm.natvis.template.xml");

		string templateContent = File.Exists(templatePath)
			? File.ReadAllText(templatePath)
			: "";

		if (string.IsNullOrEmpty(templateContent)) return;

		var handleTypes = DiscoverHandleTypes(compilation);
		var sb = new StringBuilder();
		foreach (var handleType in handleTypes)
		{
			string typeName = handleType.Name;
			string ns = handleType.ContainingNamespace.ToDisplayString();
			string cleanName = typeName.EndsWith("Id", StringComparison.Ordinal)
				? typeName.Substring(0, typeName.Length - 2)
				: typeName;
			string cppNs = ns.Replace(".", "::");

			sb.AppendLine($"  <Type Name=\"{ns}.{typeName}\">");
			sb.AppendLine($"    <DisplayString>{cleanName} {{{{Value}}}}</DisplayString>");
			sb.AppendLine("    <Expand>");
			sb.AppendLine("      <Item Name=\"Value\">Value</Item>");
			sb.AppendLine("    </Expand>");
			sb.AppendLine("  </Type>");
			sb.AppendLine($"  <Type Name=\"{cppNs}::{typeName}\">");
			sb.AppendLine($"    <DisplayString>{cleanName} {{{{Value}}}}</DisplayString>");
			sb.AppendLine("    <Expand>");
			sb.AppendLine("      <Item Name=\"Value\">Value</Item>");
			sb.AppendLine("    </Expand>");
			sb.AppendLine("  </Type>");
		}

		string natvisContent = templateContent.Replace("{{TEMPLATE}}", sb.ToString().TrimEnd());
		try
		{
			if (!File.Exists(targetPath) || File.ReadAllText(targetPath, Encoding.UTF8) != natvisContent)
			{
				File.WriteAllText(targetPath, natvisContent, Encoding.UTF8);
			}
		}
		catch
		{
		}
	}

	private static List<INamedTypeSymbol> DiscoverHandleTypes(Compilation compilation)
	{
		var gameApiSymbol = compilation.GetTypeByMetadataName("Realm.MapAPI.IGameAPI");
		if (gameApiSymbol == null) return new List<INamedTypeSymbol>();

		return GetAllTypes(gameApiSymbol.ContainingAssembly)
			.Where(t => t.TypeKind == TypeKind.Struct && !t.IsStatic &&
						t.GetMembers().Any(m => (m is IPropertySymbol p && p.Name == "Value" && p.Type.SpecialType == SpecialType.System_Int32)
											 || (m is IFieldSymbol f && f.Name == "Value" && f.Type.SpecialType == SpecialType.System_Int32)))
			.OrderBy(t => t.Name)
			.ToList();
	}

	private static IEnumerable<INamedTypeSymbol> GetAllTypes(IAssemblySymbol assembly)
	{
		return GetAllTypes(assembly.GlobalNamespace);
	}

	private static IEnumerable<INamedTypeSymbol> GetAllTypes(INamespaceSymbol namespaceSymbol)
	{
		foreach (var member in namespaceSymbol.GetMembers())
		{
			if (member is INamespaceSymbol nestedNamespace)
			{
				foreach (var type in GetAllTypes(nestedNamespace))
				{
					yield return type;
				}
			}
			else if (member is INamedTypeSymbol type)
			{
				yield return type;
				foreach (var nestedType in type.GetTypeMembers())
				{
					yield return nestedType;
				}
			}
		}
	}

	private static string GenerateEcsComponents(string repoRoot)
	{
		var relativePaths = new[]
		{
			Path.Combine("Realm.Ecs", "Common", "ResourceId.cs"),
			Path.Combine("Realm.Ecs", "Components", "Combat", "SplashType.cs"),
			Path.Combine("Realm.Ecs", "Components", "Combat", "Attack.cs"),
			Path.Combine("Realm.Ecs", "Components", "Combat", "Resistances.cs"),
			Path.Combine("Realm.Ecs", "Components", "Core", "Position.cs"),
			Path.Combine("Realm.Ecs", "Components", "Core", "PreviousPosition.cs"),
			Path.Combine("Realm.Ecs", "Components", "Core", "Health.cs"),
			Path.Combine("Realm.Ecs", "Components", "Core", "Mana.cs"),
			Path.Combine("Realm.Ecs", "Components", "Core", "PlayerPopulation.cs"),
			Path.Combine("Realm.Ecs", "Components", "Core", "ScriptPlayer.cs"),
			Path.Combine("Realm.Ecs", "Components", "Core", "UnitOwnerPlayer.cs"),
			Path.Combine("Realm.Ecs", "Components", "Meta", "Experience.cs"),
			Path.Combine("Realm.Ecs", "Components", "Meta", "Level.cs"),
			Path.Combine("Realm.Ecs", "Components", "Meta", "ModelScale.cs"),
			Path.Combine("Realm.Ecs", "Components", "Meta", "RotationY.cs"),
			Path.Combine("Realm.Ecs", "Components", "Meta", "PreviousRotationY.cs"),
			Path.Combine("Realm.Ecs", "Components", "Movement", "MovementType.cs"),
			Path.Combine("Realm.Ecs", "Components", "Movement", "MovementStats.cs"),
			Path.Combine("Realm.Ecs", "Components", "Resources", "PlayerResources.cs"),
		};

		var sb = new StringBuilder();
		sb.AppendLine("// <auto-generated/>");
		sb.AppendLine("#nullable enable");
		sb.AppendLine("using System;");
		sb.AppendLine("using System.Collections.Generic;");
		sb.AppendLine("using System.Numerics;");
		sb.AppendLine("using System.Runtime.InteropServices;");
		sb.AppendLine("using Realm.Ecs;");
		sb.AppendLine("using Realm.MapAPI;");
		sb.AppendLine();
		sb.AppendLine("namespace Realm.MapAPI.Components;");
		sb.AppendLine();

		foreach (var relPath in relativePaths)
		{
			string fullPath = Path.Combine(repoRoot, relPath);
			if (!File.Exists(fullPath)) continue;

			string code = File.ReadAllText(fullPath, Encoding.UTF8);
			var tree = CSharpSyntaxTree.ParseText(code);
			var root = tree.GetRoot();

			foreach (var node in root.DescendantNodes())
			{
				if (node is BaseTypeDeclarationSyntax typeDecl)
				{
					if (typeDecl.Parent is BaseTypeDeclarationSyntax) continue;
					sb.AppendLine(FormatTypeForMapApi(typeDecl));
					sb.AppendLine();
				}
			}
		}

		return sb.ToString();
	}

	private static string FormatTypeForMapApi(BaseTypeDeclarationSyntax typeDecl)
	{
		string typeName = typeDecl.Identifier.Text;
		var sb = new StringBuilder();

		string typeDoc = ExtractDocComment(typeDecl);
		if (string.IsNullOrWhiteSpace(typeDoc))
		{
			typeDoc = $"/// <summary>\n/// Represents the {typeName} component in WebAssembly linear memory.\n/// </summary>";
		}
		sb.AppendLine(typeDoc);

		if (typeDecl is EnumDeclarationSyntax enumDecl)
		{
			string baseList = enumDecl.BaseList != null ? $" {enumDecl.BaseList.ToFullString().Trim()}" : "";
			sb.AppendLine($"public enum {typeName}{baseList}");
			sb.AppendLine("{");
			foreach (var member in enumDecl.Members)
			{
				string memberDoc = ExtractDocComment(member);
				if (string.IsNullOrWhiteSpace(memberDoc))
				{
					memberDoc = $"    /// <summary>{member.Identifier.Text} enumeration value.</summary>";
				}
				sb.AppendLine(memberDoc);
				string eqVal = member.EqualsValue != null ? $" {member.EqualsValue.ToFullString().Trim()}" : "";
				sb.AppendLine($"    {member.Identifier.Text}{eqVal},");
			}
			sb.AppendLine("}");
			return sb.ToString();
		}

		if (typeDecl is RecordDeclarationSyntax recordDecl)
		{
			if (!recordDecl.AttributeLists.Any(a => a.ToFullString().Contains("StructLayout")))
			{
				sb.AppendLine("[StructLayout(LayoutKind.Sequential, Pack = 4)]");
			}
			string cleanRecordText = CleanCode(recordDecl.ToFullString());
			sb.AppendLine(cleanRecordText.Trim());
			return sb.ToString();
		}

		if (typeDecl is StructDeclarationSyntax structDecl)
		{
			bool isUnsafe = structDecl.Modifiers.Any(SyntaxKind.UnsafeKeyword) ||
			                structDecl.Members.OfType<FieldDeclarationSyntax>()
				                .Any(f => f.Modifiers.Any(SyntaxKind.FixedKeyword));

			if (!structDecl.AttributeLists.Any(a => a.ToFullString().Contains("StructLayout")))
			{
				sb.AppendLine("[StructLayout(LayoutKind.Sequential, Pack = 4)]");
			}
			string unsafeKeyword = isUnsafe ? "unsafe " : "";
			sb.AppendLine($"public {unsafeKeyword}struct {typeName}");
			sb.AppendLine("{");

			foreach (var member in structDecl.Members)
			{
				string memberName = GetMemberName(member);
				string key = $"{typeName}.{memberName}";
				string memberDoc = ExtractDocComment(member);

				if (string.IsNullOrWhiteSpace(memberDoc))
				{
					if (WasmHostBindingEmitter.ComponentMemberDescriptions.TryGetValue(key, out var knownDoc))
					{
						memberDoc = $"    /// <summary>{knownDoc}</summary>";
					}
					else if (member is ConstructorDeclarationSyntax)
					{
						memberDoc = $"    /// <summary>Initializes a new instance of the <see cref=\"{typeName}\"/> struct.</summary>";
					}
					else if (member is PropertyDeclarationSyntax)
					{
						memberDoc = $"    /// <summary>Gets or sets the {memberName} property.</summary>";
					}
					else if (member is FieldDeclarationSyntax)
					{
						memberDoc = $"    /// <summary>The {memberName} field.</summary>";
					}
					else if (member is ConversionOperatorDeclarationSyntax)
					{
						memberDoc = $"    /// <summary>Converts the instance to or from its primitive representation.</summary>";
					}
					else if (member is MethodDeclarationSyntax)
					{
						memberDoc = $"    /// <summary>Executes the {memberName} operation.</summary>";
					}
				}

				if (!string.IsNullOrWhiteSpace(memberDoc))
				{
					sb.AppendLine(memberDoc);
				}

				var cleanMember = member.WithoutLeadingTrivia();
				string memberText = CleanCode(cleanMember.ToFullString()).Trim();
				sb.AppendLine($"    {memberText.Replace("\n", "\n    ")}");
				sb.AppendLine();
			}

			sb.AppendLine("}");
			return sb.ToString();
		}

		return CleanCode(typeDecl.ToFullString());
	}

	private static string CleanCode(string code)
	{
		code = code
			.Replace("Realm.Ecs.Common.", "")
			.Replace("Realm.Ecs.Components.Movement.", "")
			.Replace("Realm.Ecs.Components.Combat.", "")
			.Replace("Realm.Ecs.Components.Core.", "")
			.Replace("Realm.Ecs.Components.Meta.", "")
			.Replace("Realm.Ecs.Components.Resources.", "")
			.Replace("Realm.MapAPI.Components.", "")
			.Replace("Realm.MapAPI.", "");

		code = code.Replace("MovementType.Air", "global::Realm.MapAPI.Components.MovementType.Air");
		code = code.Replace("MovementType.Water", "global::Realm.MapAPI.Components.MovementType.Water");
		code = code.Replace("MovementType.Hover", "global::Realm.MapAPI.Components.MovementType.Hover");
		code = code.Replace("MovementType.Ground", "global::Realm.MapAPI.Components.MovementType.Ground");
		code = code.Replace("global::Realm.MapAPI.Components.global::Realm.MapAPI.Components.", "global::Realm.MapAPI.Components.");
		return code;
	}

	private static string ExtractDocComment(SyntaxNode node)
	{
		var leadingTrivia = node.GetLeadingTrivia();
		var docLines = new List<string>();
		foreach (var trivia in leadingTrivia)
		{
			if (trivia.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia) ||
			    trivia.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia))
			{
				string text = trivia.ToFullString().TrimEnd();
				if (!string.IsNullOrWhiteSpace(text))
				{
					docLines.Add(text);
				}
			}
		}
		return string.Join("\n", docLines);
	}

	private static string GetMemberName(MemberDeclarationSyntax member)
	{
		if (member is FieldDeclarationSyntax field && field.Declaration.Variables.Count > 0)
			return field.Declaration.Variables[0].Identifier.Text;
		if (member is PropertyDeclarationSyntax prop)
			return prop.Identifier.Text;
		if (member is MethodDeclarationSyntax method)
			return method.Identifier.Text;
		if (member is ConstructorDeclarationSyntax ctor)
			return ctor.Identifier.Text;
		return "";
	}

	[GeneratedRegex(@"interface\s+game-api\s*\{(.*?)\}", RegexOptions.Singleline)]
	private static partial Regex GameApiInterfaceRegex();
}