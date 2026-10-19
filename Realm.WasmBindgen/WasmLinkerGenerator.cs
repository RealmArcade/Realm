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

		var combined = compilationProvider.Combine(witFileProvider.Collect());

		context.RegisterSourceOutput(combined, Execute);
	}

	private static void Execute(SourceProductionContext context, (Compilation Left, ImmutableArray<string> Right) input)
	{
		var compilation = input.Left;
		var gameApiSymbol = compilation.GetTypeByMetadataName("Realm.MapAPI.IGameAPI");
		if (gameApiSymbol == null)
			return;

		var entityInterfaces = DiscoverEntityInterfaces(gameApiSymbol);
		string staticWit = input.Right.FirstOrDefault() ?? "";
		string manualFunctions = ExtractManualFunctions(staticWit);

		string witContent = GenerateWitContent(gameApiSymbol, entityInterfaces, manualFunctions);
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
			GenerateWasmEntryPoint(repoRoot, gameApiSymbol);
			string templateWitPath = Path.Combine(repoRoot, "MapTemplate", "wit", "game.g.wit");
			WriteWitContent(templateWitPath, witContent);
			GenerateLldbScript(repoRoot, lldbCompilation);
			GenerateNatvisFile(repoRoot, lldbCompilation);
		}

		GenerateOutputSources(context, compilation.AssemblyName ?? "", gameApiSymbol, entityInterfaces, manualFunctions, repoRoot, ecsComponents);
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

	private static void GenerateOutputSources(SourceProductionContext context, string assemblyName, INamedTypeSymbol gameApiSymbol, HashSet<INamedTypeSymbol> entityInterfaces, string manualFunctions, string? repoRoot, string ecsComponents)
	{
		if (assemblyName == "Realm.Client")
		{
			context.AddSource("WasmRuntime.g.cs",
				SourceText.From(GenerateHostBindings(gameApiSymbol, entityInterfaces), Encoding.UTF8));

			context.AddSource("WasmRuntime.AutoEvents.g.cs",
				SourceText.From(GenerateAutoEvents(gameApiSymbol), Encoding.UTF8));

			context.AddSource("GeneratedWit.g.cs",
				SourceText.From(GenerateWitConstant(gameApiSymbol, entityInterfaces, manualFunctions), Encoding.UTF8));
		}
		else if (assemblyName == "Realm.MapAPI")
		{
			context.AddSource("WasmWrappers.g.cs",
				SourceText.From(GenerateWasmWrappers(gameApiSymbol, entityInterfaces), Encoding.UTF8));

			if (!string.IsNullOrEmpty(ecsComponents))
			{
				context.AddSource("EcsComponents.g.cs",
					SourceText.From(ecsComponents, Encoding.UTF8));
			}
		}
	}

	// ── Helper Discovery and Symbol Checks ──────────────────────────────────────

	private static HashSet<INamedTypeSymbol> DiscoverEntityInterfaces(INamedTypeSymbol gameApiSymbol)
	{
		var discovered = new HashSet<INamedTypeSymbol>(SymbolComparer.Instance);
		var queue = new Queue<INamedTypeSymbol>();
		queue.Enqueue(gameApiSymbol);

		while (queue.Count > 0)
		{
			var current = queue.Dequeue();
			foreach (var member in current.GetMembers())
			{
				ITypeSymbol? type = null;
				if (member is IPropertySymbol prop)
				{
					type = prop.Type;
				}
				else if (member is IMethodSymbol method)
				{
					foreach (var p in method.Parameters)
					{
						ProcessType(p.Type, discovered, queue);
					}

					type = method.ReturnType;
				}

				if (type != null)
				{
					ProcessType(type, discovered, queue);
				}
			}
		}

		discovered.Remove(gameApiSymbol);
		return discovered;
	}

	private static void ProcessType(ITypeSymbol type, HashSet<INamedTypeSymbol> discovered, Queue<INamedTypeSymbol> queue)
	{
		if (IsCollection(type, out var elemType))
		{
			type = elemType;
		}

		if (IsEntityInterface(type, out var unwrapped))
		{
			if (unwrapped is INamedTypeSymbol named && !discovered.Contains(named))
			{
				discovered.Add(named);
				queue.Enqueue(named);
			}
		}
	}

	private static bool IsEntityInterface(ITypeSymbol type, out ITypeSymbol unwrappedType)
	{
		unwrappedType = type;
		if (type is INamedTypeSymbol named && named.IsGenericType && named.ConstructedFrom.SpecialType == SpecialType.System_Nullable_T)
		{
			unwrappedType = named.TypeArguments[0];
		}
		else if (type.NullableAnnotation == NullableAnnotation.Annotated)
		{
			unwrappedType = type.WithNullableAnnotation(NullableAnnotation.NotAnnotated);
		}

		if (unwrappedType.TypeKind != TypeKind.Interface)
			return false;

		string display = unwrappedType.ToDisplayString();
		if (!display.StartsWith("Realm.MapAPI."))
			return false;

		if (display == "Realm.MapAPI.IGameAPI" || display == "Realm.MapAPI.IMapScript" || display == "Realm.MapAPI.IPlayer")
			return false;

		return true;
	}

	private static bool IsEntityIdStruct(ITypeSymbol type, out string typeName)
	{
		typeName = string.Empty;
		if (type == null) return false;

		ITypeSymbol unwrapped = type;
		if (type is INamedTypeSymbol named && named.IsGenericType && named.ConstructedFrom.SpecialType == SpecialType.System_Nullable_T)
		{
			unwrapped = named.TypeArguments[0];
		}

		string fullName = unwrapped.ToDisplayString();
		if (fullName == "Realm.MapAPI.UnitId" || unwrapped.Name == "UnitId")
		{
			typeName = "UnitId";
			return true;
		}
		if (fullName == "Realm.MapAPI.PlayerId" || unwrapped.Name == "PlayerId")
		{
			typeName = "PlayerId";
			return true;
		}
		if (fullName == "Realm.MapAPI.LootItemId" || unwrapped.Name == "LootItemId")
		{
			typeName = "LootItemId";
			return true;
		}
		if (fullName == "Realm.MapAPI.ResourceNodeId" || unwrapped.Name == "ResourceNodeId")
		{
			typeName = "ResourceNodeId";
			return true;
		}
		return false;
	}

	private static bool IsCollection(ITypeSymbol type, out ITypeSymbol elementType)
	{
		elementType = null!;
		if (type == null || type.SpecialType == SpecialType.System_String)
			return false;

		if (type is IArrayTypeSymbol arrayType)
		{
			elementType = arrayType.ElementType;
			return true;
		}

		if (type is INamedTypeSymbol namedType)
		{
			if (namedType.IsGenericType)
			{
				string def = namedType.ConstructedFrom.ToDisplayString();
				if (def == "System.Collections.Generic.IEnumerable<T>" ||
				    def == "System.ReadOnlySpan<T>" ||
				    def == "System.Span<T>")
				{
					elementType = namedType.TypeArguments[0];
					return true;
				}
			}

			foreach (var iface in namedType.AllInterfaces)
			{
				if (iface.IsGenericType && iface.ConstructedFrom.ToDisplayString() == "System.Collections.Generic.IEnumerable<T>")
				{
					elementType = iface.TypeArguments[0];
					return true;
				}
			}
		}

		return false;
	}

	private static string CleanInterfaceName(ITypeSymbol type)
	{
		string name = type.Name;
		if (name.StartsWith("I") && name.Length > 1 && char.IsUpper(name[1]))
			return name.Substring(1);
		return name;
	}

	private static string FindCollectionMember(INamedTypeSymbol gameApiSymbol, ITypeSymbol entityType)
	{
		foreach (var member in gameApiSymbol.GetMembers())
		{
			ITypeSymbol? returnType = null;
			if (member is IPropertySymbol prop)
			{
				returnType = prop.Type;
			}
			else if (member is IMethodSymbol method && method.Parameters.Length == 0 && method.MethodKind == MethodKind.Ordinary)
			{
				returnType = method.ReturnType;
			}

			if (returnType != null && IsCollection(returnType, out var elemType) && SymbolEqualityComparer.Default.Equals(elemType, entityType))
			{
				return member is IMethodSymbol ? member.Name + "()" : member.Name;
			}
		}

		throw new InvalidOperationException($"Could not find collection member on IGameAPI returning a collection of {entityType.ToDisplayString()}");
	}

	private static string FindResolverMember(INamedTypeSymbol gameApiSymbol, ITypeSymbol entityType)
	{
		foreach (var member in gameApiSymbol.GetMembers())
		{
			if (member is IMethodSymbol method && method.Parameters.Length == 1 && method.Parameters[0].Type.SpecialType == SpecialType.System_Int32 && method.MethodKind == MethodKind.Ordinary)
			{
				ITypeSymbol ret = method.ReturnType;
				if (IsEntityInterface(ret, out var unwrapped) && SymbolEqualityComparer.Default.Equals(unwrapped, entityType))
				{
					return member.Name;
				}
			}
		}

		throw new InvalidOperationException($"Could not find resolver method on IGameAPI returning {entityType.ToDisplayString()} taking a single int parameter");
	}

	// ── Output 1: Host-side DefineFunction registrations ────────────────────────

	private static string GenerateHostBindings(INamedTypeSymbol gameApiSymbol, HashSet<INamedTypeSymbol> entityInterfaces)
	{
		var sb = new StringBuilder();
		var methodNames = new List<string>();
		var definedFunctions = new HashSet<string>();
		bool lastWasMultiLine = false;

		var bodySb = new StringBuilder();

		GenerateGameApiBindings(bodySb, gameApiSymbol, definedFunctions, methodNames, ref lastWasMultiLine);
		GenerateEntityInterfaceBindings(bodySb, entityInterfaces, gameApiSymbol, definedFunctions, methodNames, ref lastWasMultiLine);

		AppendHostBindingsHeader(sb, methodNames);
		sb.Append(bodySb.ToString());

		sb.AppendLine("    }");
		sb.AppendLine("}");
		return sb.ToString();
	}

	private static void AppendHostBindingsHeader(StringBuilder sb, List<string> methodNames)
	{
		sb.AppendLine("// <auto-generated/>");
		sb.AppendLine("#nullable enable");
		sb.AppendLine("using System;");
		sb.AppendLine("using System.Linq;");
		sb.AppendLine("using Wasmtime;");
		sb.AppendLine("using Realm.MapAPI;");
		sb.AppendLine("using System.Runtime.InteropServices;");
		sb.AppendLine();
		sb.AppendLine("namespace Realm.Client.Core;");
		sb.AppendLine();
		sb.AppendLine("partial class WasmRuntime");
		sb.AppendLine("{");
		sb.AppendLine($"    public const int TotalApiMethodCount = {methodNames.Count};");
		sb.AppendLine();
		sb.AppendLine("    public static readonly string[] ApiMethodNames = new string[]");
		sb.AppendLine("    {");
		for (int index = 0; index < methodNames.Count; index++)
		{
			sb.AppendLine($"        \"{methodNames[index]}\",");
		}
		sb.AppendLine("    };");
		sb.AppendLine();
		sb.AppendLine("    private partial void InitializeAutoBindings()");
		sb.AppendLine("    {");
		sb.AppendLine("        const string mod = \"custom:game/game-api\";");
		sb.AppendLine();
	}

	private static void GenerateGameApiBindings(StringBuilder sb, INamedTypeSymbol gameApiSymbol, HashSet<string> definedFunctions, List<string> methodNames, ref bool lastWasMultiLine)
	{
		var propertyAccessorMethods = CollectPropertyAccessorNames(gameApiSymbol);

		foreach (var member in gameApiSymbol.GetMembers())
		{
			if (member is IEventSymbol || !member.IsAbstract)
				continue;

			if (member is IPropertySymbol property)
			{
				EmitPropertyBindings(sb, property, ref lastWasMultiLine, "(_cachedApi ?? (IGameAPI?)Realm.Client.Core.GameHost.Instance)", definedFunctions, methodNames);
			}
			else if (member is IMethodSymbol method && method.MethodKind == MethodKind.Ordinary)
			{
				if (!propertyAccessorMethods.Contains(method.Name))
				{
					EmitMethodBinding(sb, method, ref lastWasMultiLine, "(_cachedApi ?? (IGameAPI?)Realm.Client.Core.GameHost.Instance)", gameApiSymbol, definedFunctions, methodNames);
				}
			}
		}
	}

	private static void GenerateEntityInterfaceBindings(StringBuilder sb, HashSet<INamedTypeSymbol> entityInterfaces, INamedTypeSymbol gameApiSymbol, HashSet<string> definedFunctions, List<string> methodNames, ref bool lastWasMultiLine)
	{
		foreach (var entityIface in entityInterfaces)
		{
			GenerateSingleEntityInterfaceBinding(sb, entityIface, gameApiSymbol, definedFunctions, methodNames, ref lastWasMultiLine);
		}
	}

	private static void GenerateSingleEntityInterfaceBinding(StringBuilder sb, INamedTypeSymbol entityIface, INamedTypeSymbol gameApiSymbol, HashSet<string> definedFunctions, List<string> methodNames, ref bool lastWasMultiLine)
	{
		string tKebab = ToKebabCase(CleanInterfaceName(entityIface));
		bool hasUniqueId = entityIface.GetMembers().Any(m => m is IPropertySymbol p && p.Name == "UniqueId");

		EmitEntityCountFunctionIfNeeded(sb, entityIface, gameApiSymbol, tKebab, hasUniqueId, definedFunctions, methodNames);

		var accessorMethods = CollectPropertyAccessorNames(entityIface);
		foreach (var member in entityIface.GetMembers())
		{
			ProcessEntityInterfaceMember(sb, member, entityIface, gameApiSymbol, tKebab, accessorMethods, definedFunctions, methodNames, ref lastWasMultiLine);
		}
	}

	private static void EmitEntityCountFunctionIfNeeded(StringBuilder sb, INamedTypeSymbol entityIface, INamedTypeSymbol gameApiSymbol, string tKebab, bool hasUniqueId, HashSet<string> definedFunctions, List<string> methodNames)
	{
		if (hasUniqueId) return;

		string countName = $"{tKebab}-count";
		if (definedFunctions.Add(countName))
		{
			int methodIndex = methodNames.Count;
			methodNames.Add(countName);
			sb.AppendLine($"        _linker.DefineFunction(mod, \"{countName}\", () => {{ using var _scope = BeginProfileApiCall({methodIndex}); return (_cachedApi ?? (IGameAPI?)Realm.Client.Core.GameHost.Instance)?.{FindCollectionMember(gameApiSymbol, entityIface)}?.Count() ?? 0; }});");
		}
	}

	private static void ProcessEntityInterfaceMember(StringBuilder sb, ISymbol member, INamedTypeSymbol entityIface, INamedTypeSymbol gameApiSymbol, string tKebab, HashSet<string> accessorMethods, HashSet<string> definedFunctions, List<string> methodNames, ref bool lastWasMultiLine)
	{
		if (!member.IsAbstract) return;

		if (member is IPropertySymbol property && property.Name != "UniqueId")
		{
			EmitEntityPropertyBindings(sb, property, tKebab, entityIface, gameApiSymbol, ref lastWasMultiLine, definedFunctions, methodNames);
		}
		else if (member is IMethodSymbol method && method.MethodKind == MethodKind.Ordinary)
		{
			if (!accessorMethods.Contains(method.Name))
			{
				EmitEntityMethodBinding(sb, method, tKebab, entityIface, gameApiSymbol, ref lastWasMultiLine, definedFunctions, methodNames);
			}
		}
	}

	private static void EmitEntityPropertyBindings(StringBuilder sb, IPropertySymbol property, string tKebab, INamedTypeSymbol entitySymbol, INamedTypeSymbol gameApiSymbol, ref bool lastWasMultiLine, HashSet<string> definedFunctions, List<string> methodNames)
	{
		string witBase = tKebab + "-" + ToKebabCase(property.Name);
		string resolver = FindResolverMember(gameApiSymbol, entitySymbol);

		if (property.Type.ToDisplayString() == "System.Numerics.Vector3")
		{
			EmitEntityVector3Property(sb, property, witBase, resolver, definedFunctions, methodNames);
			return;
		}

		var retKind = ClassifyReturn(property.Type);
		if (retKind == RetKind.Unsupported) return;

		EmitEntityStandardProperty(sb, property, witBase, resolver, gameApiSymbol, retKind, ref lastWasMultiLine, definedFunctions, methodNames);
		EmitEntityPropertySetter(sb, property, witBase, resolver, gameApiSymbol, definedFunctions, methodNames);
	}

	private static void EmitEntityVector3Property(StringBuilder sb, IPropertySymbol property, string witBase, string resolver, HashSet<string> definedFunctions, List<string> methodNames)
	{
		string nameX = $"{witBase}-x";
		if (definedFunctions.Add(nameX))
		{
			int idxX = methodNames.Count;
			methodNames.Add(nameX);
			sb.AppendLine($"        _linker.DefineFunction(mod, \"{nameX}\", (int id) => {{ using var _scope = BeginProfileApiCall({idxX}); var u = (_cachedApi ?? (IGameAPI?)Realm.Client.Core.GameHost.Instance)?.{resolver}(id); return u != null ? u.{property.Name}.X : 0f; }});");
		}

		string nameY = $"{witBase}-y";
		if (definedFunctions.Add(nameY))
		{
			int idxY = methodNames.Count;
			methodNames.Add(nameY);
			sb.AppendLine($"        _linker.DefineFunction(mod, \"{nameY}\", (int id) => {{ using var _scope = BeginProfileApiCall({idxY}); var u = (_cachedApi ?? (IGameAPI?)Realm.Client.Core.GameHost.Instance)?.{resolver}(id); return u != null ? u.{property.Name}.Y : 0f; }});");
		}

		string nameZ = $"{witBase}-z";
		if (definedFunctions.Add(nameZ))
		{
			int idxZ = methodNames.Count;
			methodNames.Add(nameZ);
			sb.AppendLine($"        _linker.DefineFunction(mod, \"{nameZ}\", (int id) => {{ using var _scope = BeginProfileApiCall({idxZ}); var u = (_cachedApi ?? (IGameAPI?)Realm.Client.Core.GameHost.Instance)?.{resolver}(id); return u != null ? u.{property.Name}.Z : 0f; }});");
		}

		if (property.SetMethod != null)
		{
			string setWitBase = "set-" + witBase;
			if (definedFunctions.Add(setWitBase))
			{
				int idxSet = methodNames.Count;
				methodNames.Add(setWitBase);
				sb.AppendLine($"        _linker.DefineFunction(mod, \"{setWitBase}\", (int id, float valX, float valY, float valZ) => {{ using var _scope = BeginProfileApiCall({idxSet}); var u = (_cachedApi ?? (IGameAPI?)Realm.Client.Core.GameHost.Instance)?.{resolver}(id); if (u != null) u.{property.Name} = new System.Numerics.Vector3(valX, valY, valZ); }});");
			}
		}
	}

	private static void EmitEntityStandardProperty(StringBuilder sb, IPropertySymbol property, string witBase, string resolver, INamedTypeSymbol gameApiSymbol, RetKind retKind, ref bool lastWasMultiLine, HashSet<string> definedFunctions, List<string> methodNames)
	{
		string expr = $"(_cachedApi ?? (IGameAPI?)Realm.Client.Core.GameHost.Instance)?.{resolver}(id)?.{property.Name}";
		string mapped = MapEntityPropertyExpression(property, gameApiSymbol, retKind, expr);

		if (!definedFunctions.Add(witBase)) return;

		int methodIndex = methodNames.Count;
		methodNames.Add(witBase);

		if (lastWasMultiLine) sb.AppendLine();
		if (retKind == RetKind.StringReturn)
		{
			sb.AppendLine($"        _linker.DefineFunction(mod, \"{witBase}\", (Caller caller, int id, int retArea) => {{ using var _scope = BeginProfileApiCall({methodIndex}); string s = {mapped}; WriteGuestString(caller, retArea, s); }});");
		}
		else
		{
			sb.AppendLine($"        _linker.DefineFunction(mod, \"{witBase}\", (int id) => {{ using var _scope = BeginProfileApiCall({methodIndex}); return {mapped}; }});");
		}

		lastWasMultiLine = false;
	}

	private static string MapEntityPropertyExpression(IPropertySymbol property, INamedTypeSymbol gameApiSymbol, RetKind retKind, string expr)
	{
		if (retKind == RetKind.EntityReturn || retKind == RetKind.EntityNullableReturn)
		{
			IsEntityInterface(property.Type, out var unwrapped);
			bool targetHasId = unwrapped.GetMembers().Any(m => m is IPropertySymbol p && p.Name == "UniqueId");
			if (targetHasId)
			{
				return $"{expr}?.UniqueId ?? 0";
			}
			else
			{
				string colName = FindCollectionMember(gameApiSymbol, unwrapped);
				return $"({expr} != null) ? ((_cachedApi ?? (IGameAPI?)Realm.Client.Core.GameHost.Instance)?.{colName}.ToList().IndexOf({expr}) ?? -1) : -1";
			}
		}

		return retKind switch
		{
			RetKind.BoolReturn => $"({expr} ?? false) ? 1 : 0",
			RetKind.DirectFloat => $"{expr} ?? 0f",
			RetKind.DirectInt => $"(int)({expr} ?? 0)",
			RetKind.StringReturn => $"{expr} ?? \"\"",
			_ => "0"
		};
	}

	private static void EmitEntityPropertySetter(StringBuilder sb, IPropertySymbol property, string witBase, string resolver, INamedTypeSymbol gameApiSymbol, HashSet<string> definedFunctions, List<string> methodNames)
	{
		if (property.SetMethod == null) return;

		string setWitBase = "set-" + witBase;
		if (!definedFunctions.Add(setWitBase)) return;

		int methodIndex = methodNames.Count;
		methodNames.Add(setWitBase);

		if (property.Type.SpecialType == SpecialType.System_Boolean)
			sb.AppendLine($"        _linker.DefineFunction(mod, \"{setWitBase}\", (int id, int val) => {{ using var _scope = BeginProfileApiCall({methodIndex}); var u = (_cachedApi ?? (IGameAPI?)Realm.Client.Core.GameHost.Instance)?.{resolver}(id); if (u != null) u.{property.Name} = val != 0; }});");
		else if (property.Type.SpecialType == SpecialType.System_Single)
			sb.AppendLine($"        _linker.DefineFunction(mod, \"{setWitBase}\", (int id, float val) => {{ using var _scope = BeginProfileApiCall({methodIndex}); var u = (_cachedApi ?? (IGameAPI?)Realm.Client.Core.GameHost.Instance)?.{resolver}(id); if (u != null) u.{property.Name} = val; }});");
		else if (property.Type.SpecialType == SpecialType.System_Int32)
			sb.AppendLine($"        _linker.DefineFunction(mod, \"{setWitBase}\", (int id, int val) => {{ using var _scope = BeginProfileApiCall({methodIndex}); var u = (_cachedApi ?? (IGameAPI?)Realm.Client.Core.GameHost.Instance)?.{resolver}(id); if (u != null) u.{property.Name} = val; }});");
		else if (property.Type.SpecialType == SpecialType.System_String)
			sb.AppendLine($"        _linker.DefineFunction(mod, \"{setWitBase}\", (Caller caller, int id, int valPtr, int valLen) => {{ using var _scope = BeginProfileApiCall({methodIndex}); var u = (_cachedApi ?? (IGameAPI?)Realm.Client.Core.GameHost.Instance)?.{resolver}(id); if (u != null) u.{property.Name} = ReadGuestString(caller, valPtr, valLen); }});");
		else if (IsEntityInterface(property.Type, out var unwrapped))
		{
			string resolverR = FindResolverMember(gameApiSymbol, unwrapped);
			sb.AppendLine($"        _linker.DefineFunction(mod, \"{setWitBase}\", (int id, int val) => {{ using var _scope = BeginProfileApiCall({methodIndex}); var u = (_cachedApi ?? (IGameAPI?)Realm.Client.Core.GameHost.Instance)?.{resolver}(id); if (u != null) u.{property.Name} = (_cachedApi ?? (IGameAPI?)Realm.Client.Core.GameHost.Instance)?.{resolverR}(val); }});");
		}
	}

	private static void EmitEntityMethodBinding(StringBuilder sb, IMethodSymbol method, string tKebab, INamedTypeSymbol entitySymbol, INamedTypeSymbol gameApiSymbol, ref bool lastWasMultiLine, HashSet<string> definedFunctions, List<string> methodNames)
	{
		var retKind = ClassifyReturn(method.ReturnType);
		if (!IsValidReturnType(retKind, method.ReturnType)) return;

		string witName = tKebab + "-" + ToKebabCase(method.Name);
		if (!definedFunctions.Add(witName)) return;

		string resolver = FindResolverMember(gameApiSymbol, entitySymbol);

		if (retKind == RetKind.StringListReturn)
		{
			EmitEntityStringListMethod(sb, witName, resolver, method, ref lastWasMultiLine, definedFunctions, methodNames);
			return;
		}

		var paramInfos = ExtractParamInfos(method);
		if (paramInfos == null) return;

		int methodIndex = methodNames.Count;
		methodNames.Add(witName);

		bool needsRetArea = retKind == RetKind.StringReturn;
		string lambdaParamsStr = BuildEntityLambdaParams(method, paramInfos, needsRetArea);

		if (lastWasMultiLine) sb.AppendLine();
		sb.AppendLine($"        _linker.DefineFunction(mod, \"{witName}\", ({lambdaParamsStr}) =>");
		sb.AppendLine("        {");
		sb.AppendLine($"            using var _scope = BeginProfileApiCall({methodIndex});");
		sb.AppendLine($"            var hostTarget = (_cachedApi ?? (IGameAPI?)Realm.Client.Core.GameHost.Instance)?.{resolver}(id);");

		string defaultReturn = GetDefaultReturnValue(method, retKind, needsRetArea);
		if (!string.IsNullOrEmpty(defaultReturn))
			sb.AppendLine($"            if (hostTarget == null) return {defaultReturn};");
		else
			sb.AppendLine($"            if (hostTarget == null) return;");

		EmitEntityMethodParameters(sb, paramInfos, gameApiSymbol);
		EmitEntityMethodCall(sb, method, retKind, gameApiSymbol, needsRetArea);

		if (!string.IsNullOrEmpty(defaultReturn))
			sb.AppendLine($"            return {defaultReturn};");

		sb.AppendLine("        });");
		lastWasMultiLine = true;
	}

	private static bool IsValidReturnType(RetKind retKind, ITypeSymbol returnType)
	{
		if (retKind == RetKind.EntityListReturn) return false;
		if (retKind != RetKind.Unsupported) return true;
		return returnType.ToDisplayString() == "System.Numerics.Vector3" || returnType.SpecialType == SpecialType.System_Void;
	}

	private static string GetDefaultReturnValue(IMethodSymbol method, RetKind retKind, bool needsRetArea)
	{
		if (method.ReturnType.SpecialType == SpecialType.System_Void || needsRetArea) return string.Empty;
		return retKind == RetKind.DirectFloat ? "0f" : "0";
	}

	private static List<ParamInfo>? ExtractParamInfos(IMethodSymbol method)
	{
		var paramInfos = new List<ParamInfo>();
		foreach (var param in method.Parameters)
		{
			var kind = ClassifyParam(param.Type);
			if (kind == PrmKind.Unsupported) return null;
			paramInfos.Add(new ParamInfo(param.Name, kind, param.Type));
		}

		return paramInfos;
	}

	private static string BuildEntityLambdaParams(IMethodSymbol method, List<ParamInfo> paramInfos, bool needsRetArea)
	{
		bool hasStringParam = method.Parameters.Any(p => p.Type.SpecialType == SpecialType.System_String);
		bool hasStringListParam = method.Parameters.Any(p => ClassifyParam(p.Type) == PrmKind.StringListParam);
		bool needsCaller = hasStringParam || hasStringListParam || needsRetArea;

		var lambdaParams = new List<string>();
		if (needsCaller) lambdaParams.Add("Caller caller");
		lambdaParams.Add("int id");
		lambdaParams.AddRange(paramInfos.Select(GetLambdaParamDefinition).Where(s => !string.IsNullOrEmpty(s)));
		if (needsRetArea) lambdaParams.Add("int retArea");

		return string.Join(", ", lambdaParams);
	}

	private static void EmitEntityStringListMethod(StringBuilder sb, string witName, string resolver, IMethodSymbol method, ref bool lastWasMultiLine, HashSet<string> definedFunctions, List<string> methodNames)
	{
		string countName = $"{witName}-count";
		if (definedFunctions.Add(countName))
		{
			int countIdx = methodNames.Count;
			methodNames.Add(countName);
			sb.AppendLine($"        _linker.DefineFunction(mod, \"{countName}\", (int id) =>");
			sb.AppendLine("        {");
			sb.AppendLine($"            using var _scope = BeginProfileApiCall({countIdx});");
			sb.AppendLine($"            var target = (_cachedApi ?? (IGameAPI?)Realm.Client.Core.GameHost.Instance)?.{resolver}(id);");
			sb.AppendLine($"            return target?.{method.Name}().Count() ?? 0;");
			sb.AppendLine("        });");
		}

		string getName = $"{witName}-get";
		if (definedFunctions.Add(getName))
		{
			int getIdx = methodNames.Count;
			methodNames.Add(getName);
			sb.AppendLine($"        _linker.DefineFunction(mod, \"{getName}\", (Caller caller, int id, int index, int retArea) =>");
			sb.AppendLine("        {");
			sb.AppendLine($"            using var _scope = BeginProfileApiCall({getIdx});");
			sb.AppendLine($"            var target = (_cachedApi ?? (IGameAPI?)Realm.Client.Core.GameHost.Instance)?.{resolver}(id);");
			sb.AppendLine($"            string s = target?.{method.Name}().ElementAtOrDefault(index) ?? \"\";");
			sb.AppendLine("            WriteGuestString(caller, retArea, s);");
			sb.AppendLine("        });");
		}
		lastWasMultiLine = true;
	}

	private static string GetLambdaParamDefinition(ParamInfo p)
	{
		return p.Kind switch
		{
			PrmKind.StringParam => $"int {p.Name}Ptr, int {p.Name}Len",
			PrmKind.StringListParam => $"int {p.Name}Ptr, int {p.Name}Len",
			PrmKind.DirectFloat => $"float {p.Name}",
			PrmKind.DirectInt or PrmKind.BoolParam => $"int {p.Name}",
			PrmKind.EntityParam => $"int {p.Name}RawId",
			PrmKind.Vector2Param => $"float {p.Name}X, float {p.Name}Y",
			PrmKind.Vector3Param => $"float {p.Name}X, float {p.Name}Y, float {p.Name}Z",
			PrmKind.Vector3NullableParam => $"float {p.Name}R, float {p.Name}G, float {p.Name}B, int {p.Name}HasColor",
			_ => ""
		};
	}

	private static void EmitEntityMethodParameters(StringBuilder sb, List<ParamInfo> paramInfos, INamedTypeSymbol gameApiSymbol)
	{
		foreach (var p in paramInfos)
		{
			if (p.Kind == PrmKind.StringParam)
				sb.AppendLine($"                string {p.Name} = ReadGuestString(caller, {p.Name}Ptr, {p.Name}Len);");
			else if (p.Kind == PrmKind.StringListParam)
				sb.AppendLine($"                string[] {p.Name} = ReadGuestStringList(caller, {p.Name}Ptr, {p.Name}Len);");
			else if (p.Kind == PrmKind.Vector3Param)
				sb.AppendLine($"                var {p.Name} = new System.Numerics.Vector3({p.Name}X, {p.Name}Y, {p.Name}Z);");
			else if (p.Kind == PrmKind.EntityParam)
			{
				if (IsEntityIdStruct(p.Type, out _))
				{
					sb.AppendLine($"                var {p.Name} = ({p.Type.ToDisplayString()}){p.Name}RawId;");
				}
				else if (IsEntityInterface(p.Type, out var unwrapped))
				{
					string res = FindResolverMember(gameApiSymbol, unwrapped);
					sb.AppendLine($"                var {p.Name} = (_cachedApi ?? (IGameAPI?)Realm.Client.Core.GameHost.Instance)?.{res}({p.Name}RawId);");
				}
			}
		}
	}

	private static void EmitEntityMethodCall(StringBuilder sb, IMethodSymbol method, RetKind retKind, INamedTypeSymbol gameApiSymbol, bool needsRetArea)
	{
		string callExpr = BuildEntityMethodCallExpression(method);

		if (method.ReturnType.SpecialType == SpecialType.System_Void)
		{
			sb.AppendLine($"                {callExpr};");
			return;
		}

		if (retKind == RetKind.StringReturn)
		{
			sb.AppendLine($"                string s = {callExpr} ?? \"\";");
			sb.AppendLine("                WriteGuestString(caller, retArea, s);");
			return;
		}

		string exprMapped = MapEntityMethodReturnExpression(method, retKind, gameApiSymbol, callExpr);
		sb.AppendLine($"                return {exprMapped};");
	}

	private static string BuildEntityMethodCallExpression(IMethodSymbol method)
	{
		var callArgs = new List<string>();
		foreach (var p in method.Parameters)
		{
			if (p.Type.SpecialType == SpecialType.System_Boolean)
				callArgs.Add($"{p.Name} != 0");
			else if (p.Type.TypeKind == TypeKind.Enum)
				callArgs.Add($"({p.Type.ToDisplayString()}){p.Name}");
			else
				callArgs.Add(p.Name);
		}

		return $"hostTarget.{method.Name}({string.Join(", ", callArgs)})";
	}

	private static string MapEntityMethodReturnExpression(IMethodSymbol method, RetKind retKind, INamedTypeSymbol gameApiSymbol, string callExpr)
	{
		if (retKind == RetKind.EntityReturn || retKind == RetKind.EntityNullableReturn)
		{
			IsEntityInterface(method.ReturnType, out var unwrapped);
			bool targetHasId = unwrapped.GetMembers().Any(m => m is IPropertySymbol p && p.Name == "UniqueId");
			if (targetHasId)
			{
				return $"({callExpr})?.UniqueId ?? 0";
			}

			string colName = FindCollectionMember(gameApiSymbol, unwrapped);
			return $"({callExpr} != null) ? ((_cachedApi ?? (IGameAPI?)Realm.Client.Core.GameHost.Instance)?.{colName}.ToList().IndexOf({callExpr}) ?? -1) : -1";
		}

		return retKind switch
		{
			RetKind.BoolReturn => $"({callExpr}) ? 1 : 0",
			_ => callExpr
		};
	}

	private static void EmitPropertyBindings(StringBuilder sb, IPropertySymbol property, ref bool lastWasMultiLine, string targetInstance, HashSet<string> definedFunctions, List<string> methodNames)
	{
		var retKind = ClassifyReturn(property.Type);
		if (retKind == RetKind.Unsupported || RetKindToWit(retKind) == null)
			return;

		string witBase = ToKebabCase(property.Name);

		EmitPropertyGetter(sb, property, witBase, retKind, targetInstance, ref lastWasMultiLine, definedFunctions, methodNames);
		EmitPropertySetter(sb, property, witBase, targetInstance, definedFunctions, methodNames);
	}

	private static void EmitPropertyGetter(StringBuilder sb, IPropertySymbol property, string witBase, RetKind retKind, string targetInstance, ref bool lastWasMultiLine, HashSet<string> definedFunctions, List<string> methodNames)
	{
		if (property.GetMethod == null) return;

		string getWitBase = $"get-{witBase}";
		if (!definedFunctions.Add(getWitBase)) return;

		int methodIndex = methodNames.Count;
		methodNames.Add(getWitBase);

		if (lastWasMultiLine) sb.AppendLine();
		string expr = BuildPropertyGetterExpression(property.Name, retKind, targetInstance);

		if (retKind == RetKind.StringReturn)
		{
			sb.AppendLine($"        _linker.DefineFunction(mod, \"get-{witBase}\", (Caller caller, int retArea) => {{ using var _scope = BeginProfileApiCall({methodIndex}); string s = {expr}; WriteGuestString(caller, retArea, s); }});");
		}
		else
		{
			sb.AppendLine($"        _linker.DefineFunction(mod, \"get-{witBase}\", () => {{ using var _scope = BeginProfileApiCall({methodIndex}); return {expr}; }});");
		}

		lastWasMultiLine = false;
	}

	private static void EmitPropertySetter(StringBuilder sb, IPropertySymbol property, string witBase, string targetInstance, HashSet<string> definedFunctions, List<string> methodNames)
	{
		if (property.SetMethod == null) return;

		string setWitBase = $"set-{witBase}";
		if (!definedFunctions.Add(setWitBase)) return;

		int methodIndex = methodNames.Count;
		methodNames.Add(setWitBase);

		if (property.Type.SpecialType == SpecialType.System_Boolean)
			sb.AppendLine($"        _linker.DefineFunction(mod, \"{setWitBase}\", (int val) => {{ using var _scope = BeginProfileApiCall({methodIndex}); if ({targetInstance} != null) {targetInstance}.{property.Name} = val != 0; }});");
		else if (property.Type.SpecialType == SpecialType.System_Single)
			sb.AppendLine($"        _linker.DefineFunction(mod, \"{setWitBase}\", (float val) => {{ using var _scope = BeginProfileApiCall({methodIndex}); if ({targetInstance} != null) {targetInstance}.{property.Name} = val; }});");
		else if (property.Type.SpecialType == SpecialType.System_Int32)
			sb.AppendLine($"        _linker.DefineFunction(mod, \"{setWitBase}\", (int val) => {{ using var _scope = BeginProfileApiCall({methodIndex}); if ({targetInstance} != null) {targetInstance}.{property.Name} = val; }});");
		else if (property.Type.SpecialType == SpecialType.System_String)
			sb.AppendLine($"        _linker.DefineFunction(mod, \"{setWitBase}\", (Caller caller, int valPtr, int valLen) => {{ using var _scope = BeginProfileApiCall({methodIndex}); if ({targetInstance} != null) {targetInstance}.{property.Name} = ReadGuestString(caller, valPtr, valLen); }});");
	}

	private static string BuildPropertyGetterExpression(string propertyName, RetKind retKind, string targetInstance)
	{
		return retKind switch
		{
			RetKind.DirectFloat => $"{targetInstance}?.{propertyName} ?? 0f",
			RetKind.DirectInt => $"(int)({targetInstance}?.{propertyName} ?? 0)",
			RetKind.BoolReturn => $"({targetInstance}?.{propertyName} ?? false) ? 1 : 0",
			RetKind.StringReturn => $"{targetInstance}?.{propertyName} ?? \"\"",
			_ => "0"
		};
	}

	private static void EmitMethodBinding(StringBuilder sb, IMethodSymbol method, ref bool lastWasMultiLine, string targetInstance, INamedTypeSymbol gameApiSymbol, HashSet<string> definedFunctions, List<string> methodNames)
	{
		if (method.Parameters.Any(p => p.RefKind != RefKind.None)) return;

		var paramInfos = new List<ParamInfo>();
		foreach (var param in method.Parameters)
		{
			var kind = ClassifyParam(param.Type);
			if (kind == PrmKind.Unsupported) return;
			paramInfos.Add(new ParamInfo(param.Name, kind, param.Type));
		}

		var retKind = ClassifyReturn(method.ReturnType);
		bool isVector3Ret = method.ReturnType.ToDisplayString() == "System.Numerics.Vector3";
		if (retKind == RetKind.Unsupported && !isVector3Ret) return;

		var witName = ToKebabCase(method.Name);
		if (!definedFunctions.Add(witName)) return;

		if (retKind == RetKind.StringListReturn)
		{
			EmitStringListMethodBinding(sb, method, witName, paramInfos, targetInstance, gameApiSymbol, ref lastWasMultiLine, definedFunctions, methodNames);
			return;
		}

		EmitStandardMethodBinding(sb, method, witName, paramInfos, retKind, isVector3Ret, targetInstance, gameApiSymbol, ref lastWasMultiLine, definedFunctions, methodNames);
	}

	private static void EmitStringListMethodBinding(StringBuilder sb, IMethodSymbol method, string witName, List<ParamInfo> paramInfos, string targetInstance, INamedTypeSymbol gameApiSymbol, ref bool lastWasMultiLine, HashSet<string> definedFunctions, List<string> methodNames)
	{
		string countName = $"{witName}-count";
		int countIdx = methodNames.Count;
		methodNames.Add(countName);

		var lambdaParamsCount = BuildLambdaParams(paramInfos, paramInfos.Exists(p => p.Kind == PrmKind.StringParam), false);
		sb.AppendLine($"        _linker.DefineFunction(mod, \"{countName}\", ({lambdaParamsCount}) =>");
		sb.AppendLine("        {");
		sb.AppendLine($"            using var _scope = BeginProfileApiCall({countIdx});");
		EmitEntityMethodParameters(sb, paramInfos, gameApiSymbol);
		string callExprCount = BuildCallExpressionFull(method, paramInfos, targetInstance);
		sb.AppendLine($"            return {callExprCount}?.Count() ?? 0;");
		sb.AppendLine("        });");

		string getName = $"{witName}-get";
		int getIdx = methodNames.Count;
		methodNames.Add(getName);

		var paramInfosGet = new List<ParamInfo>(paramInfos);
		paramInfosGet.Add(new ParamInfo("index", PrmKind.DirectInt, gameApiSymbol));
		var lambdaParamsGet = BuildLambdaParams(paramInfosGet, needsCaller: true, needsRetArea: true);

		sb.AppendLine($"        _linker.DefineFunction(mod, \"{getName}\", ({lambdaParamsGet}) =>");
		sb.AppendLine("        {");
		sb.AppendLine($"            using var _scope = BeginProfileApiCall({getIdx});");
		EmitEntityMethodParameters(sb, paramInfos, gameApiSymbol);
		string callExprGet = BuildCallExpressionFull(method, paramInfos, targetInstance);
		sb.AppendLine($"            string s = {callExprGet}?.ElementAtOrDefault(index) ?? \"\";");
		sb.AppendLine("            WriteGuestString(caller, retArea, s);");
		sb.AppendLine("        });");

		lastWasMultiLine = true;
	}

	private static void EmitStandardMethodBinding(StringBuilder sb, IMethodSymbol method, string witName, List<ParamInfo> paramInfos, RetKind retKind, bool isVector3Ret, string targetInstance, INamedTypeSymbol gameApiSymbol, ref bool lastWasMultiLine, HashSet<string> definedFunctions, List<string> methodNames)
	{
		bool hasStringParam = method.Parameters.Any(p => p.Type.SpecialType == SpecialType.System_String);
		bool hasStringListParam = method.Parameters.Any(p => ClassifyParam(p.Type) == PrmKind.StringListParam);
		bool needsRetArea = !isVector3Ret && (retKind == RetKind.StringReturn || retKind == RetKind.EntityListReturn);
		bool needsCaller = hasStringParam || hasStringListParam || needsRetArea;

		var lambdaParams = BuildLambdaParams(paramInfos, needsCaller, needsRetArea);

		if (CanUseExpressionBody(method, isVector3Ret, hasStringParam, hasStringListParam, needsRetArea, retKind))
		{
			int methodIndex = methodNames.Count;
			methodNames.Add(witName);
			string expr = BuildSimpleExpression(method, paramInfos, retKind, targetInstance, gameApiSymbol);
			if (lastWasMultiLine) sb.AppendLine();
			if (method.ReturnType.SpecialType == SpecialType.System_Void)
			{
				sb.AppendLine($"        _linker.DefineFunction(mod, \"{witName}\", ({lambdaParams}) => {{ using var _scope = BeginProfileApiCall({methodIndex}); {expr}; }});");
			}
			else
			{
				sb.AppendLine($"        _linker.DefineFunction(mod, \"{witName}\", ({lambdaParams}) => {{ using var _scope = BeginProfileApiCall({methodIndex}); return {expr}; }});");
			}
			lastWasWasMultiLine(ref lastWasMultiLine, false);
			return;
		}

		if (lastWasMultiLine) sb.AppendLine();

		if (isVector3Ret)
		{
			EmitVector3AxisBinding(sb, witName, "x", lambdaParams, paramInfos, method, targetInstance, gameApiSymbol, methodNames);
			EmitVector3AxisBinding(sb, witName, "y", lambdaParams, paramInfos, method, targetInstance, gameApiSymbol, methodNames);
			EmitVector3AxisBinding(sb, witName, "z", lambdaParams, paramInfos, method, targetInstance, gameApiSymbol, methodNames);
			lastWasWasMultiLine(ref lastWasMultiLine, true);
			return;
		}

		int blockIndex = methodNames.Count;
		methodNames.Add(witName);

		sb.AppendLine($"        _linker.DefineFunction(mod, \"{witName}\", ({lambdaParams}) =>");
		sb.AppendLine("        {");
		sb.AppendLine($"            using var _scope = BeginProfileApiCall({blockIndex});");
		EmitBlockBody(sb, method, paramInfos, retKind, targetInstance, gameApiSymbol);
		sb.AppendLine("        });");
		sb.AppendLine();
		lastWasWasMultiLine(ref lastWasMultiLine, true);
	}

	private static bool CanUseExpressionBody(IMethodSymbol method, bool isVector3Ret, bool hasStringParam, bool hasStringListParam, bool needsRetArea, RetKind retKind)
	{
		bool hasVector2Param = method.Parameters.Any(p => p.Type.ToDisplayString() == "System.Numerics.Vector2");
		bool hasVector3Param = method.Parameters.Any(p => p.Type.ToDisplayString() == "System.Numerics.Vector3");
		bool hasVector3NullableParam = method.Parameters.Any(p => IsNullableVector3(p.Type));
		bool hasUnitParam = method.Parameters.Any(p => IsEntityInterface(p.Type, out _) || IsEntityIdStruct(p.Type, out _));

		return !isVector3Ret && !hasStringParam && !hasStringListParam && !hasVector2Param && !hasVector3Param && !hasVector3NullableParam && !hasUnitParam && !needsRetArea && retKind != RetKind.EntityReturn && retKind != RetKind.EntityNullableReturn;
	}

	private static void EmitVector3AxisBinding(StringBuilder sb, string witName, string axis, string lambdaParams, List<ParamInfo> paramInfos, IMethodSymbol method, string targetInstance, INamedTypeSymbol gameApiSymbol, List<string> methodNames)
	{
		string axisName = $"{witName}-{axis}";
		int methodIndex = methodNames.Count;
		methodNames.Add(axisName);

		sb.AppendLine($"        _linker.DefineFunction(mod, \"{axisName}\", ({lambdaParams}) =>");
		sb.AppendLine("        {");
		sb.AppendLine($"            using var _scope = BeginProfileApiCall({methodIndex});");
		foreach (var p in paramInfos)
			if (p.Kind == PrmKind.StringParam)
				sb.AppendLine($"            string {p.Name} = ReadGuestString(caller, {p.Name}Ptr, {p.Name}Len);");
		foreach (var p in paramInfos)
			if (p.Kind == PrmKind.Vector2Param)
				sb.AppendLine($"            var {p.Name} = new System.Numerics.Vector2({p.Name}X, {p.Name}Y);");
		foreach (var p in paramInfos)
			if (p.Kind == PrmKind.Vector3Param)
				sb.AppendLine($"            var {p.Name} = new System.Numerics.Vector3({p.Name}X, {p.Name}Y, {p.Name}Z);");
		foreach (var p in paramInfos)
			if (p.Kind == PrmKind.EntityParam)
			{
				if (IsEntityIdStruct(p.Type, out _))
				{
					sb.AppendLine($"            var {p.Name} = ({p.Type.ToDisplayString()}){p.Name}RawId;");
				}
				else if (IsEntityInterface(p.Type, out var unwrapped))
				{
					string res = FindResolverMember(gameApiSymbol, unwrapped);
					sb.AppendLine($"            var {p.Name} = (_cachedApi ?? (IGameAPI?)Realm.Client.Core.GameHost.Instance)?.{res}({p.Name}RawId);");
				}
			}

		var entityParams = paramInfos.FindAll(p => p.Kind == PrmKind.EntityParam && !IsEntityIdStruct(p.Type, out _));
		string callExpr = BuildCallExpressionFull(method, paramInfos, targetInstance);

		if (entityParams.Count == 0)
		{
			sb.AppendLine($"            return ({callExpr}).GetValueOrDefault().{axis.ToUpper()};");
		}
		else
		{
			string condition = string.Join(" && ", entityParams.Select(u => $"{u.Name} != null"));
			sb.AppendLine($"            return ({condition}) ? ({callExpr}).GetValueOrDefault().{axis.ToUpper()} : 0f;");
		}

		sb.AppendLine("        });");
	}

	private static string BuildLambdaParams(List<ParamInfo> paramInfos, bool needsCaller, bool needsRetArea)
	{
		var parts = new List<string>();

		if (needsCaller) parts.Add("Caller caller");
		parts.AddRange(paramInfos.Select(GetLambdaParamDefinition).Where(s => !string.IsNullOrEmpty(s)));
		if (needsRetArea) parts.Add("int retArea");

		return string.Join(", ", parts);
	}

	private static string BuildCallExpressionSimple(IMethodSymbol method, List<ParamInfo> paramInfos, string targetInstance)
	{
		var args = new List<string>();
		foreach (var p in method.Parameters)
		{
			if (p.Type.ToDisplayString() == "System.Numerics.Vector3")
				args.Add(p.Name);
			else if (IsNullableVector3(p.Type))
				args.Add(p.Name);
			else if (p.Type.SpecialType == SpecialType.System_Boolean)
				args.Add($"{p.Name} != 0");
			else if (p.Type.TypeKind == TypeKind.Enum)
				args.Add($"({p.Type.ToDisplayString()}){p.Name}");
			else
				args.Add(p.Name);
		}

		return $"{targetInstance}?.{method.Name}({string.Join(", ", args)})";
	}

	private static string BuildCallExpressionFull(IMethodSymbol method, List<ParamInfo> paramInfos, string targetInstance)
	{
		var args = new List<string>();
		foreach (var p in method.Parameters)
		{
			if (p.Type.ToDisplayString() == "System.Numerics.Vector3")
				args.Add(p.Name);
			else if (IsNullableVector3(p.Type))
				args.Add(p.Name);
			else if (p.Type.SpecialType == SpecialType.System_Boolean)
				args.Add($"{p.Name} != 0");
			else if (p.Type.TypeKind == TypeKind.Enum)
				args.Add($"({p.Type.ToDisplayString()}){p.Name}");
			else
				args.Add(p.Name);
		}

		return $"{targetInstance}?.{method.Name}({string.Join(", ", args)})";
	}

	private static void EmitBlockBody(StringBuilder sb, IMethodSymbol method, List<ParamInfo> paramInfos, RetKind retKind, string targetInstance, INamedTypeSymbol gameApiSymbol)
	{
		ReadStringParameters(sb, paramInfos);
		ReadVector3Parameters(sb, paramInfos);
		ReadEntityParameters(sb, paramInfos, gameApiSymbol);

		var entityParams = paramInfos.FindAll(p => p.Kind == PrmKind.EntityParam);
		string callExpr = BuildCallExpressionFull(method, paramInfos, targetInstance);

		EmitMethodCallAndReturn(sb, method, retKind, gameApiSymbol, entityParams, callExpr);
	}

	private static void ReadStringParameters(StringBuilder sb, List<ParamInfo> paramInfos)
	{
		foreach (var p in paramInfos)
		{
			if (p.Kind == PrmKind.StringParam)
				sb.AppendLine($"            string {p.Name} = ReadGuestString(caller, {p.Name}Ptr, {p.Name}Len);");
			else if (p.Kind == PrmKind.StringListParam)
				sb.AppendLine($"            string[] {p.Name} = ReadGuestStringList(caller, {p.Name}Ptr, {p.Name}Len);");
		}
	}

	private static void ReadVector3Parameters(StringBuilder sb, List<ParamInfo> paramInfos)
	{
		var processedVec = new HashSet<string>();
		foreach (var p in paramInfos)
		{
			if (p.Kind == PrmKind.Vector2Param && p.Name.EndsWith("X"))
			{
				string baseName = p.Name.Substring(0, p.Name.Length - 1);
				if (processedVec.Add(baseName))
					sb.AppendLine($"            var {baseName} = new System.Numerics.Vector2({baseName}X, {baseName}Y);");
			}
			else if (p.Kind == PrmKind.Vector2Param)
				sb.AppendLine($"            var {p.Name} = new System.Numerics.Vector2({p.Name}X, {p.Name}Y);");
			else if (p.Kind == PrmKind.Vector3Param && p.Name.EndsWith("X"))
			{
				string baseName = p.Name.Substring(0, p.Name.Length - 1);
				if (processedVec.Add(baseName))
					sb.AppendLine($"            var {baseName} = new System.Numerics.Vector3({baseName}X, {baseName}Y, {baseName}Z);");
			}
			else if (p.Kind == PrmKind.Vector3Param)
				sb.AppendLine($"            var {p.Name} = new System.Numerics.Vector3({p.Name}X, {p.Name}Y, {p.Name}Z);");
			else if (p.Kind == PrmKind.Vector3NullableParam)
				sb.AppendLine($"            System.Numerics.Vector3? {p.Name} = {p.Name}HasColor != 0 ? new System.Numerics.Vector3({p.Name}R, {p.Name}G, {p.Name}B) : null;");
		}
	}

	private static void ReadEntityParameters(StringBuilder sb, List<ParamInfo> paramInfos, INamedTypeSymbol gameApiSymbol)
	{
		foreach (var p in paramInfos)
		{
			if (p.Kind == PrmKind.EntityParam)
			{
				if (IsEntityIdStruct(p.Type, out _))
				{
					sb.AppendLine($"            var {p.Name} = ({p.Type.ToDisplayString()}){p.Name}RawId;");
				}
				else if (IsEntityInterface(p.Type, out var unwrapped))
				{
					string res = FindResolverMember(gameApiSymbol, unwrapped);
					sb.AppendLine($"            var {p.Name} = (_cachedApi ?? (IGameAPI?)Realm.Client.Core.GameHost.Instance)?.{res}({p.Name}RawId);");
				}
			}
		}
	}

	private static void EmitMethodCallAndReturn(StringBuilder sb, IMethodSymbol method, RetKind retKind, INamedTypeSymbol gameApiSymbol, List<ParamInfo> entityParams, string callExpr)
	{
		var nonStructEntityParams = entityParams.FindAll(p => !IsEntityIdStruct(p.Type, out _));
		string condition = nonStructEntityParams.Count > 0 ? string.Join(" && ", nonStructEntityParams.Select(u => $"{u.Name} != null")) : "";

		switch (retKind)
		{
			case RetKind.Void:
				EmitVoidReturn(sb, nonStructEntityParams.Count == 0, condition, callExpr);
				break;
			case RetKind.StringReturn:
				EmitStringReturn(sb, callExpr);
				break;
			case RetKind.EntityListReturn:
				EmitEntityListReturn(sb, method, gameApiSymbol, callExpr);
				break;
			case RetKind.EntityReturn:
			case RetKind.EntityNullableReturn:
				EmitEntitySingleReturn(sb, method, gameApiSymbol, nonStructEntityParams.Count == 0, condition, callExpr);
				break;
			case RetKind.BoolReturn:
				EmitBoolReturn(sb, nonStructEntityParams.Count == 0, condition, callExpr);
				break;
			default:
				EmitDefaultReturn(sb, retKind, nonStructEntityParams.Count == 0, condition, callExpr);
				break;
		}
	}

	private static void EmitVoidReturn(StringBuilder sb, bool noEntityParams, string condition, string callExpr)
	{
		if (noEntityParams)
			sb.AppendLine($"            {callExpr};");
		else
			sb.AppendLine($"            if ({condition}) {callExpr};");
	}

	private static void EmitStringReturn(StringBuilder sb, string callExpr)
	{
		sb.AppendLine($"            string result = {callExpr} ?? \"\";");
		sb.AppendLine("            WriteGuestString(caller, retArea, result);");
	}

	private static void EmitEntityListReturn(StringBuilder sb, IMethodSymbol method, INamedTypeSymbol gameApiSymbol, string callExpr)
	{
		IsCollection(method.ReturnType, out var elemType);
		if (IsEntityIdStruct(elemType, out _))
		{
			var args = new List<string>();
			foreach (var p in method.Parameters)
			{
				if (p.Type.ToDisplayString() == "System.Numerics.Vector3")
					args.Add(p.Name);
				else if (IsNullableVector3(p.Type))
					args.Add(p.Name);
				else if (p.Type.SpecialType == SpecialType.System_Boolean)
					args.Add($"{p.Name} != 0");
				else if (p.Type.TypeKind == TypeKind.Enum)
					args.Add($"({p.Type.ToDisplayString()}){p.Name}");
				else
					args.Add(p.Name);
			}
			string argsCall = string.Join(", ", args);
			sb.AppendLine($"            var api = (_cachedApi ?? (IGameAPI?)Realm.Client.Core.GameHost.Instance);");
			sb.AppendLine($"            var items = api != null ? api.{method.Name}({argsCall}) : ReadOnlySpan<{elemType.ToDisplayString()}>.Empty;");
			sb.AppendLine($"            WriteGuestIntSpan(caller, retArea, MemoryMarshal.Cast<{elemType.ToDisplayString()}, int>(items));");
			return;
		}

		bool hasUniqueId = elemType.GetMembers().Any(m => m is IPropertySymbol p && p.Name == "UniqueId");
		sb.AppendLine($"            var items = {callExpr} ?? Array.Empty<{elemType.ToDisplayString()}>();");

		if (hasUniqueId)
		{
			sb.AppendLine("            WriteGuestIntList(caller, retArea, items.Select(e => e.UniqueId).ToList());");
		}
		else
		{
			string colName = FindCollectionMember(gameApiSymbol, elemType);
			sb.AppendLine($"            WriteGuestIntList(caller, retArea, items.Select(e => ((_cachedApi ?? (IGameAPI?)Realm.Client.Core.GameHost.Instance)?.{colName}.ToList().IndexOf(e) ?? -1).ToList());");
		}
	}

	private static void EmitEntitySingleReturn(StringBuilder sb, IMethodSymbol method, INamedTypeSymbol gameApiSymbol, bool noEntityParams, string condition, string callExpr)
	{
		if (IsEntityIdStruct(method.ReturnType, out _))
		{
			if (noEntityParams)
			{
				sb.AppendLine($"            return (int)({callExpr});");
			}
			else
			{
				sb.AppendLine($"            if ({condition})");
				sb.AppendLine("            {");
				sb.AppendLine($"                return (int)({callExpr});");
				sb.AppendLine("            }");
				sb.AppendLine("            return 0;");
			}
			return;
		}

		IsEntityInterface(method.ReturnType, out var unwrapped);
		bool hasUniqueId = unwrapped.GetMembers().Any(m => m is IPropertySymbol p && p.Name == "UniqueId");

		if (noEntityParams)
		{
			sb.AppendLine($"            var result = {callExpr};");
			EmitEntityReturnMapping(sb, gameApiSymbol, unwrapped, hasUniqueId, "result");
		}
		else
		{
			sb.AppendLine($"            if ({condition})");
			sb.AppendLine("            {");
			sb.AppendLine($"                var result = {callExpr};");
			EmitEntityReturnMapping(sb, gameApiSymbol, unwrapped, hasUniqueId, "result");
			sb.AppendLine("            }");
			sb.AppendLine($"            return {(hasUniqueId ? "0" : "-1")};");
		}
	}

	private static void EmitBoolReturn(StringBuilder sb, bool noEntityParams, string condition, string callExpr)
	{
		if (noEntityParams)
			sb.AppendLine($"            return ({callExpr} ?? false) ? 1 : 0;");
		else
			sb.AppendLine($"            return ({condition}) ? (({callExpr} ?? false) ? 1 : 0) : 0;");
	}

	private static void EmitDefaultReturn(StringBuilder sb, RetKind retKind, bool noEntityParams, string condition, string callExpr)
	{
		string defaultVal = retKind == RetKind.DirectFloat ? "0f" : "0";
		if (noEntityParams)
			sb.AppendLine($"            return {callExpr} ?? {defaultVal};");
		else
			sb.AppendLine($"            return ({condition}) ? ({callExpr} ?? {defaultVal}) : {defaultVal};");
	}

	private static void EmitEntityReturnMapping(StringBuilder sb, INamedTypeSymbol gameApiSymbol, ITypeSymbol unwrapped, bool hasUniqueId, string resultVar)
	{
		if (hasUniqueId)
		{
			sb.AppendLine($"                return {resultVar}?.UniqueId ?? 0;");
		}
		else
		{
			string colName = FindCollectionMember(gameApiSymbol, unwrapped);
			sb.AppendLine($"                return ({resultVar} != null) ? ((_cachedApi ?? (IGameAPI?)Realm.Client.Core.GameHost.Instance)?.{colName}.ToList().IndexOf({resultVar}) ?? -1) : -1;");
		}
	}

	private static string BuildSimpleExpression(IMethodSymbol method, List<ParamInfo> paramInfos, RetKind retKind, string targetInstance, INamedTypeSymbol gameApiSymbol)
	{
		string callExpr = BuildCallExpressionSimple(method, paramInfos, targetInstance);
		if (retKind == RetKind.EntityReturn || retKind == RetKind.EntityNullableReturn)
		{
			IsEntityInterface(method.ReturnType, out var unwrapped);
			bool hasUniqueId = unwrapped.GetMembers().Any(m => m is IPropertySymbol p && p.Name == "UniqueId");
			if (hasUniqueId)
			{
				return $"({callExpr})?.UniqueId ?? 0";
			}
			else
			{
				string colName = FindCollectionMember(gameApiSymbol, unwrapped);
				return $"({callExpr} != null) ? ((_cachedApi ?? (IGameAPI?)Realm.Client.Core.GameHost.Instance)?.{colName}.ToList().IndexOf({callExpr}) ?? -1) : -1";
			}
		}

		return retKind switch
		{
			RetKind.Void => callExpr,
			RetKind.DirectInt => $"(int)({callExpr} ?? 0)",
			RetKind.DirectFloat => $"{callExpr} ?? 0f",
			RetKind.BoolReturn => $"({callExpr} ?? false) ? 1 : 0",
			RetKind.StringReturn => $"{callExpr} ?? \"\"",
			_ => callExpr
		};
	}

	private static PrmKind ClassifyParam(ITypeSymbol type)
	{
		if (type.SpecialType == SpecialType.System_Int32) return PrmKind.DirectInt;
		if (type.TypeKind == TypeKind.Enum) return PrmKind.DirectInt;
		if (type.SpecialType == SpecialType.System_Single) return PrmKind.DirectFloat;
		if (type.SpecialType == SpecialType.System_Boolean) return PrmKind.BoolParam;
		if (type.SpecialType == SpecialType.System_String) return PrmKind.StringParam;

		if (IsCollection(type, out var elemType) && elemType.SpecialType == SpecialType.System_String)
			return PrmKind.StringListParam;

		string displayName = type.ToDisplayString();

		if (displayName == "System.Numerics.Vector2") return PrmKind.Vector2Param;
		if (displayName == "System.Numerics.Vector3") return PrmKind.Vector3Param;
		if (IsNullableVector3(type)) return PrmKind.Vector3NullableParam;
		if (IsEntityIdStruct(type, out _)) return PrmKind.EntityParam;
		if (IsEntityInterface(type, out _)) return PrmKind.EntityParam;

		return PrmKind.Unsupported;
	}

	private static RetKind ClassifyReturn(ITypeSymbol type)
	{
		var specialRet = ClassifySpecialReturn(type.SpecialType);
		if (specialRet != RetKind.Unsupported) return specialRet;

		if (type.TypeKind == TypeKind.Enum) return RetKind.DirectInt;

		if (IsEntityIdStruct(type, out _))
		{
			bool isNullable = type.NullableAnnotation == NullableAnnotation.Annotated || type.ToDisplayString().EndsWith("?");
			return isNullable ? RetKind.EntityNullableReturn : RetKind.EntityReturn;
		}

		if (IsEntityInterface(type, out _))
		{
			bool isNullable = type.NullableAnnotation == NullableAnnotation.Annotated || type.ToDisplayString().EndsWith("?");
			return isNullable ? RetKind.EntityNullableReturn : RetKind.EntityReturn;
		}

		return ClassifyCollectionReturn(type);
	}

	private static RetKind ClassifySpecialReturn(SpecialType specialType)
	{
		return specialType switch
		{
			SpecialType.System_Void => RetKind.Void,
			SpecialType.System_Int32 => RetKind.DirectInt,
			SpecialType.System_Single => RetKind.DirectFloat,
			SpecialType.System_Boolean => RetKind.BoolReturn,
			SpecialType.System_String => RetKind.StringReturn,
			_ => RetKind.Unsupported
		};
	}

	private static RetKind ClassifyCollectionReturn(ITypeSymbol type)
	{
		if (!IsCollection(type, out var elemType)) return RetKind.Unsupported;

		if (elemType.SpecialType == SpecialType.System_String) return RetKind.StringListReturn;
		if (IsEntityIdStruct(elemType, out _)) return RetKind.EntityListReturn;
		if (IsEntityInterface(elemType, out _)) return RetKind.EntityListReturn;

		return RetKind.Unsupported;
	}

	private static bool IsNullableVector3(ITypeSymbol type)
	{
		if (type is INamedTypeSymbol named && named.IsGenericType && named.ConstructedFrom.SpecialType == SpecialType.System_Nullable_T)
		{
			return named.TypeArguments[0].ToDisplayString() == "System.Numerics.Vector3";
		}

		return false;
	}

	private static void lastWasWasMultiLine(ref bool last, bool current)
	{
		last = current;
	}

	// ── Output 2: Wit bindings dynamic generation ──────────────────────────────

	private static string GenerateWitContent(INamedTypeSymbol gameApiSymbol, HashSet<INamedTypeSymbol> entityInterfaces, string manualFunctions)
	{
		var functions = new SortedDictionary<string, string>(StringComparer.Ordinal);

		AppendWitGameApiMembers(functions, gameApiSymbol);
		AppendWitEntityInterfaces(functions, entityInterfaces);

		var witBody = new StringBuilder();
		foreach (var kvp in functions)
		{
			witBody.AppendLine($"    {kvp.Key}: {kvp.Value};");
		}

		if (!string.IsNullOrEmpty(manualFunctions))
		{
			witBody.AppendLine();
			witBody.AppendLine("    // Manual WIT entries");
			witBody.AppendLine("    " + manualFunctions.Replace("\r\n", "\n").Replace("\n", "\n    "));
		}

		var wit = new StringBuilder();
		wit.AppendLine("package custom:game;");
		wit.AppendLine();
		wit.AppendLine("interface game-api {");
		wit.Append(witBody.ToString());
		wit.AppendLine("}");
		wit.AppendLine();
		wit.AppendLine("world game-client {");
		wit.AppendLine("    import game-api;");
		wit.AppendLine("}");

		return wit.ToString();
	}

	private static void AppendWitGameApiMembers(SortedDictionary<string, string> functions, INamedTypeSymbol gameApiSymbol)
	{
		var propertyAccessorMethods = CollectPropertyAccessorNames(gameApiSymbol);

		foreach (var member in gameApiSymbol.GetMembers().OrderBy(m => m.Name, StringComparer.Ordinal))
		{
			if (member is IEventSymbol || !member.IsAbstract) continue;

			if (member is IPropertySymbol property)
			{
				AppendWitProperty(functions, property);
			}
			else if (member is IMethodSymbol method && method.MethodKind == MethodKind.Ordinary)
			{
				if (!propertyAccessorMethods.Contains(method.Name))
				{
					AppendWitMethod(functions, method);
				}
			}
		}
	}

	private static void AppendWitEntityInterfaces(SortedDictionary<string, string> functions, HashSet<INamedTypeSymbol> entityInterfaces)
	{
		foreach (var entityIface in entityInterfaces.OrderBy(e => e.Name, StringComparer.Ordinal))
		{
			AppendWitEntityInterface(functions, entityIface);
		}
	}

	private static string GenerateWitConstant(INamedTypeSymbol gameApiSymbol, HashSet<INamedTypeSymbol> entityInterfaces, string manualFunctions)
	{
		string witBody = GenerateWitContent(gameApiSymbol, entityInterfaces, manualFunctions);

		var wit = new StringBuilder();
		wit.AppendLine("// Do not edit file directly.");
		wit.AppendLine("// This file is auto-generated by the custom WasmLinkerGenerator tool.");
		wit.AppendLine("// Generator source: file:///D:/git/Realm/Realm.WasmBindgen/WasmLinkerGenerator.cs");
		wit.AppendLine();
		wit.AppendLine("namespace Realm.Client.Core;");
		wit.AppendLine();
		wit.AppendLine("public static class GeneratedWit");
		wit.AppendLine("{");
		wit.AppendLine("    public const string Content = \"\"\"");
		wit.Append(witBody);
		wit.AppendLine("\"\"\";");
		wit.AppendLine("}");

		return wit.ToString();
	}

	private static void AppendWitEntityInterface(SortedDictionary<string, string> functions, INamedTypeSymbol entitySymbol)
	{
		string tKebab = ToKebabCase(CleanInterfaceName(entitySymbol));
		bool hasUniqueId = entitySymbol.GetMembers().Any(m => m is IPropertySymbol p && p.Name == "UniqueId");

		if (!hasUniqueId)
		{
			AppendFunction(functions, $"{tKebab}-count", "func() -> s32");
		}

		var propertyAccessors = CollectPropertyAccessorNames(entitySymbol);
		foreach (var member in entitySymbol.GetMembers().OrderBy(m => m.Name, StringComparer.Ordinal))
		{
			if (!member.IsAbstract) continue;

			if (member is IPropertySymbol property && property.Name != "UniqueId")
			{
				AppendWitEntityProperties(functions, property, tKebab);
			}
			else if (member is IMethodSymbol method && method.MethodKind == MethodKind.Ordinary)
			{
				if (!propertyAccessors.Contains(method.Name))
				{
					AppendWitEntityMethod(functions, method, tKebab);
				}
			}
		}
	}

	private static void AppendWitEntityProperties(SortedDictionary<string, string> functions, IPropertySymbol property, string tKebab)
	{
		string witBase = $"{tKebab}-{ToKebabCase(property.Name)}";

		if (property.Type.ToDisplayString() == "System.Numerics.Vector3")
		{
			AppendFunction(functions, $"{witBase}-x", "func(id: s32) -> f32");
			AppendFunction(functions, $"{witBase}-y", "func(id: s32) -> f32");
			AppendFunction(functions, $"{witBase}-z", "func(id: s32) -> f32");

			if (property.SetMethod != null)
			{
				AppendFunction(functions, $"set-{witBase}", "func(id: s32, val-x: f32, val-y: f32, val-z: f32)");
			}

			return;
		}

		var retKind = ClassifyReturn(property.Type);
		if (retKind == RetKind.Unsupported) return;

		string? witType = RetKindToWit(retKind);
		if (witType != null)
		{
			AppendFunction(functions, witBase, $"func(id: s32) -> {witType}");
			if (property.SetMethod != null)
			{
				string? paramType = RetKindToWitParam(retKind);
				if (paramType != null)
					AppendFunction(functions, $"set-{witBase}", $"func(id: s32, val: {paramType})");
			}
		}
	}

	private static void AppendWitEntityMethod(SortedDictionary<string, string> functions, IMethodSymbol method, string tKebab)
	{
		if (method.Parameters.Any(p => p.RefKind != RefKind.None)) return;

		var retKind = ClassifyReturn(method.ReturnType);
		bool isVector3Ret = method.ReturnType.ToDisplayString() == "System.Numerics.Vector3";

		if (retKind == RetKind.Unsupported && !isVector3Ret && method.ReturnType.SpecialType != SpecialType.System_Void) return;
		if (retKind == RetKind.EntityListReturn) return;

		string paramsStr = BuildWitEntityMethodParams(method);
		if (paramsStr == null) return;

		string witName = tKebab + "-" + ToKebabCase(method.Name);

		if (isVector3Ret)
		{
			AppendWitVector3Method(functions, witName, paramsStr);
			return;
		}

		if (retKind == RetKind.StringListReturn)
		{
			AppendWitStringListMethod(functions, witName, paramsStr);
			return;
		}

		AppendWitStandardMethod(functions, method, witName, paramsStr, retKind);
	}

	private static void AppendWitVector3Method(SortedDictionary<string, string> functions, string witName, string paramsStr)
	{
		AppendFunction(functions, $"{witName}-x", $"func({paramsStr}) -> f32");
		AppendFunction(functions, $"{witName}-y", $"func({paramsStr}) -> f32");
		AppendFunction(functions, $"{witName}-z", $"func({paramsStr}) -> f32");
	}

	private static void AppendWitStringListMethod(SortedDictionary<string, string> functions, string witName, string paramsStr)
	{
		AppendFunction(functions, $"{witName}-count", $"func({paramsStr}) -> s32");
		AppendFunction(functions, $"{witName}-get", $"func({paramsStr}, index: s32) -> string");
	}

	private static void AppendWitStandardMethod(SortedDictionary<string, string> functions, IMethodSymbol method, string witName, string paramsStr, RetKind retKind)
	{
		string retStr = (retKind == RetKind.Void || method.ReturnType.SpecialType == SpecialType.System_Void) ? "" : $" -> {RetKindToWit(retKind)}";
		AppendFunction(functions, witName, $"func({paramsStr}){retStr}");
	}

	private static string? BuildWitEntityMethodParams(IMethodSymbol method)
	{
		var witParams = new List<string> { "id: s32" };
		foreach (var param in method.Parameters)
		{
			string? paramWit = ParamToWit(param.Type, param.Name);
			if (paramWit == null) return null;
			witParams.Add(paramWit);
		}

		return string.Join(", ", witParams);
	}

	private static void AppendWitProperty(SortedDictionary<string, string> functions, IPropertySymbol property)
	{
		var retKind = ClassifyReturn(property.Type);
		if (retKind == RetKind.Unsupported)
			return;

		string witBase = ToKebabCase(property.Name);
		string? witType = RetKindToWit(retKind);
		if (witType == null)
			return;

		if (property.GetMethod != null)
			AppendFunction(functions, $"get-{witBase}", $"func() -> {witType}");

		if (property.SetMethod != null)
		{
			string? paramType = RetKindToWitParam(retKind);
			if (paramType != null)
				AppendFunction(functions, $"set-{witBase}", $"func(val: {paramType})");
		}
	}

	private static void AppendWitMethod(SortedDictionary<string, string> functions, IMethodSymbol method)
	{
		if (method.Parameters.Any(p => p.RefKind != RefKind.None)) return;

		var retKind = ClassifyReturn(method.ReturnType);
		bool isVector3Ret = method.ReturnType.ToDisplayString() == "System.Numerics.Vector3";
		if (retKind == RetKind.Unsupported && !isVector3Ret) return;

		string paramsStr = BuildWitMethodParams(method);
		if (paramsStr == null && method.Parameters.Length > 0) return;
		paramsStr ??= "";

		string witName = ToKebabCase(method.Name);

		if (retKind == RetKind.StringListReturn)
		{
			AppendFunction(functions, $"{witName}-count", $"func({paramsStr}) -> s32");
			AppendFunction(functions, $"{witName}-get", $"func({paramsStr}, index: s32) -> string");
		}
		else if (isVector3Ret)
		{
			AppendFunction(functions, $"{witName}-x", $"func({paramsStr}) -> f32");
			AppendFunction(functions, $"{witName}-y", $"func({paramsStr}) -> f32");
			AppendFunction(functions, $"{witName}-z", $"func({paramsStr}) -> f32");
		}
		else
		{
			string retStr = (retKind == RetKind.Void || method.ReturnType.SpecialType == SpecialType.System_Void) ? "" : $" -> {RetKindToWit(retKind)}";
			AppendFunction(functions, witName, $"func({paramsStr}){retStr}");
		}
	}

	private static string? BuildWitMethodParams(IMethodSymbol method)
	{
		if (method.Parameters.Length == 0) return "";
		var witParams = new List<string>();
		foreach (var param in method.Parameters)
		{
			string? paramWit = ParamToWit(param.Type, param.Name);
			if (paramWit == null) return null;
			witParams.Add(paramWit);
		}

		return string.Join(", ", witParams);
	}

	private static string EscapeWitIdentifier(string name)
	{
		string kebab = ToKebabCase(name);
		return kebab switch
		{
			"flags" or "type" or "resource" or "enum" or "record" or "variant" or "interface" or "world" or "package" or "import" or "export" or "use" or "default"
				=> $"%{kebab}",
			_ => kebab
		};
	}

	private static string? ParamToWit(ITypeSymbol type, string name)
	{
		var kind = ClassifyParam(type);
		string paramName = EscapeWitIdentifier(name);
		return kind switch
		{
			PrmKind.DirectInt => $"{paramName}: s32",
			PrmKind.DirectFloat => $"{paramName}: f32",
			PrmKind.BoolParam => $"{paramName}: bool",
			PrmKind.StringParam => $"{paramName}: string",
			PrmKind.StringListParam => $"{paramName}: list<string>",
			PrmKind.Vector2Param => $"{paramName}-x: f32, {paramName}-y: f32",
			PrmKind.Vector3Param => $"{paramName}-x: f32, {paramName}-y: f32, {paramName}-z: f32",
			PrmKind.EntityParam => paramName.EndsWith("-id", StringComparison.OrdinalIgnoreCase) ? $"{paramName}: s32" : $"{paramName}-id: s32",
			PrmKind.Vector3NullableParam => $"{paramName}-r: f32, {paramName}-g: f32, {paramName}-b: f32, {paramName}-has-color: bool",
			_ => null
		};
	}

	private static string? RetKindToWit(RetKind retKind)
	{
		return retKind switch
		{
			RetKind.Void => null,
			RetKind.DirectInt => "s32",
			RetKind.DirectFloat => "f32",
			RetKind.BoolReturn => "bool",
			RetKind.StringReturn => "string",
			RetKind.EntityReturn => "s32",
			RetKind.EntityNullableReturn => "s32",
			RetKind.EntityListReturn => "list<s32>",
			_ => null
		};
	}

	private static string? RetKindToWitParam(RetKind retKind)
	{
		return retKind switch
		{
			RetKind.Void => null,
			RetKind.DirectInt => "s32",
			RetKind.DirectFloat => "f32",
			RetKind.BoolReturn => "bool",
			RetKind.StringReturn => "string",
			RetKind.EntityReturn => "s32",
			RetKind.EntityNullableReturn => "s32",
			_ => null
		};
	}

	private static void AppendFunction(SortedDictionary<string, string> functions, string name, string body)
	{
		if (!functions.ContainsKey(name))
		{
			functions.Add(name, body);
		}
	}

	// ── Output 3: Auto-Events bindings dynamic generation ───────────────────────

	private static string GenerateAutoEvents(INamedTypeSymbol gameApiSymbol)
	{
		var sb = new StringBuilder();
		sb.AppendLine("// <auto-generated/>");
		sb.AppendLine("#nullable enable");
		sb.AppendLine("using System;");
		sb.AppendLine("using System.Linq;");
		sb.AppendLine("using Wasmtime;");
		sb.AppendLine("using Realm.MapAPI;");
		sb.AppendLine();
		sb.AppendLine("namespace Realm.Client.Core;");
		sb.AppendLine();
		sb.AppendLine("partial class WasmRuntime");
		sb.AppendLine("{");

		var events = gameApiSymbol.GetMembers().OfType<IEventSymbol>().ToList();

		AppendAutoEventsFields(sb, events);
		AppendInitializeAutoEvents(sb, events);
		AppendSubscribeAutoEvents(sb, events);
		AppendUnsubscribeAutoEvents(sb, events);
		AppendAutoEventHandlers(sb, events, gameApiSymbol);

		sb.AppendLine("}");
		return sb.ToString();
	}

	private static void AppendAutoEventsFields(StringBuilder sb, List<IEventSymbol> events)
	{
		foreach (var ev in events)
		{
			var delegateMethod = ((INamedTypeSymbol)ev.Type).DelegateInvokeMethod;
			if (delegateMethod == null) continue;

			bool hasString = delegateMethod.Parameters.Any(p => p.Type.SpecialType == SpecialType.System_String);
			string fieldType = hasString ? "Function" : GetWasmDelegateType(delegateMethod.Parameters);
			sb.AppendLine($"    private {fieldType}? {GetWasmField(ev.Name)};");
		}

		sb.AppendLine();
	}

	private static void AppendInitializeAutoEvents(StringBuilder sb, List<IEventSymbol> events)
	{
		sb.AppendLine("    private partial void InitializeAutoEvents()");
		sb.AppendLine("    {");
		foreach (var ev in events)
		{
			var delegateMethod = ((INamedTypeSymbol)ev.Type).DelegateInvokeMethod;
			if (delegateMethod == null) continue;

			string witName = ToKebabCase(ev.Name);
			bool hasString = delegateMethod.Parameters.Any(p => p.Type.SpecialType == SpecialType.System_String);

			if (hasString)
			{
				sb.AppendLine($"        {GetWasmField(ev.Name)} = _instance.GetFunction(\"{witName}\");");
			}
			else
			{
				string typeArgs = GetWasmDelegateTypeArgs(delegateMethod.Parameters);
				string wrapMethod = string.IsNullOrEmpty(typeArgs) ? "WrapAction" : $"WrapAction<{typeArgs}>";
				sb.AppendLine($"        {GetWasmField(ev.Name)} = _instance.GetFunction(\"{witName}\")?.{wrapMethod}();");
			}
		}

		sb.AppendLine("    }");
		sb.AppendLine();
	}

	private static void AppendSubscribeAutoEvents(StringBuilder sb, List<IEventSymbol> events)
	{
		sb.AppendLine("    private partial void SubscribeAutoEvents(IGameAPI api)");
		sb.AppendLine("    {");
		foreach (var ev in events)
		{
			sb.AppendLine($"        api.{ev.Name} += {GetWasmHandler(ev.Name)};");
		}

		sb.AppendLine("    }");
		sb.AppendLine();
	}

	private static void AppendUnsubscribeAutoEvents(StringBuilder sb, List<IEventSymbol> events)
	{
		sb.AppendLine("    private partial void UnsubscribeAutoEvents()");
		sb.AppendLine("    {");
		sb.AppendLine("        if (_cachedApi != null)");
		sb.AppendLine("        {");
		foreach (var ev in events)
		{
			sb.AppendLine($"            _cachedApi.{ev.Name} -= {GetWasmHandler(ev.Name)};");
		}

		sb.AppendLine("        }");
		sb.AppendLine("    }");
		sb.AppendLine();
	}

	private static void AppendAutoEventHandlers(StringBuilder sb, List<IEventSymbol> events, INamedTypeSymbol gameApiSymbol)
	{
		foreach (var ev in events)
		{
			var delegateMethod = ((INamedTypeSymbol)ev.Type).DelegateInvokeMethod;
			if (delegateMethod == null) continue;

			string handlerName = GetWasmHandler(ev.Name);
			var sigParams = new List<string>();
			foreach (var p in delegateMethod.Parameters)
			{
				sigParams.Add($"{p.Type.ToDisplayString()} {p.Name}");
			}

			sb.AppendLine($"    private void {handlerName}({string.Join(", ", sigParams)})");
			sb.AppendLine("    {");

			string fieldName = GetWasmField(ev.Name);
			bool hasString = delegateMethod.Parameters.Any(p => p.Type.SpecialType == SpecialType.System_String);

			if (hasString)
			{
				EmitStringAutoEventHandlerBody(sb, delegateMethod, fieldName, gameApiSymbol);
			}
			else
			{
				EmitStandardAutoEventHandlerBody(sb, delegateMethod, fieldName, gameApiSymbol);
			}

			sb.AppendLine("    }");
			sb.AppendLine();
		}
	}

	private static void EmitStringAutoEventHandlerBody(StringBuilder sb, IMethodSymbol delegateMethod, string fieldName, INamedTypeSymbol gameApiSymbol)
	{
		sb.AppendLine($"        if (IsFaulted || {fieldName} == null) return;");
		sb.AppendLine("        ExecuteGuardedAction(() =>");
		sb.AppendLine("        {");
		sb.AppendLine("            var memory = _instance.GetMemory(\"memory\");");
		sb.AppendLine("            if (memory != null)");
		sb.AppendLine("            {");

		var invokeArgs = new List<string>();
		foreach (var p in delegateMethod.Parameters)
		{
			if (p.Type.SpecialType == SpecialType.System_String)
			{
				sb.AppendLine($"                byte[] bytes_{p.Name} = System.Text.Encoding.UTF8.GetBytes({p.Name});");
				sb.AppendLine($"                int ptr_{p.Name} = AllocateInGuest(bytes_{p.Name}.Length);");
				sb.AppendLine($"                bytes_{p.Name}.CopyTo(memory.GetSpan(ptr_{p.Name}, bytes_{p.Name}.Length));");
				invokeArgs.Add($"ptr_{p.Name}");
				invokeArgs.Add($"bytes_{p.Name}.Length");
			}
			else if (p.Type.ToDisplayString() == "System.Numerics.Vector3" || p.Type.ToDisplayString() == "System.Numerics.Vector3?")
			{
				invokeArgs.Add($"{p.Name}.X");
				invokeArgs.Add($"{p.Name}.Y");
				invokeArgs.Add($"{p.Name}.Z");
			}
			else if (IsEntityIdStruct(p.Type, out _))
			{
				invokeArgs.Add($"(int){p.Name}");
			}
			else if (IsEntityInterface(p.Type, out var unwrapped))
			{
				bool hasId = unwrapped.GetMembers().Any(m => m is IPropertySymbol prop && prop.Name == "UniqueId");
				if (hasId)
				{
					invokeArgs.Add($"{p.Name}?.UniqueId ?? 0");
				}
				else
				{
					string col = FindCollectionMember(gameApiSymbol, unwrapped);
					invokeArgs.Add($"{p.Name} != null ? (((_cachedApi ?? (IGameAPI?)Realm.Client.Core.GameHost.Instance)?.{col}().ToList().IndexOf({p.Name}) ?? -1) : -1");
				}
			}
			else
			{
				invokeArgs.Add(p.Name);
			}
		}

		sb.AppendLine($"                {fieldName}.Invoke({string.Join(", ", invokeArgs)});");
		sb.AppendLine("            }");
		sb.AppendLine("        }, \"event handler\");");
	}

	private static void EmitStandardAutoEventHandlerBody(StringBuilder sb, IMethodSymbol delegateMethod, string fieldName, INamedTypeSymbol gameApiSymbol)
	{
		sb.AppendLine($"        if (IsFaulted || {fieldName} == null) return;");
		sb.AppendLine("        ExecuteGuardedAction(() =>");
		sb.AppendLine("        {");

		var invokeArgs = new List<string>();
		foreach (var p in delegateMethod.Parameters)
		{
			if (IsEntityIdStruct(p.Type, out _))
			{
				invokeArgs.Add($"(int){p.Name}");
			}
			else if (IsEntityInterface(p.Type, out var unwrapped))
			{
				bool hasId = unwrapped.GetMembers().Any(m => m is IPropertySymbol prop && prop.Name == "UniqueId");
				if (hasId)
				{
					invokeArgs.Add($"{p.Name}?.UniqueId ?? 0");
				}
				else
				{
					string col = FindCollectionMember(gameApiSymbol, unwrapped);
					invokeArgs.Add($"{p.Name} != null ? (((_cachedApi ?? (IGameAPI?)Realm.Client.Core.GameHost.Instance)?.{col}().ToList().IndexOf({p.Name}) ?? -1) : -1");
				}
			}
			else
			{
				invokeArgs.Add(p.Name);
			}
		}

		sb.AppendLine($"            {fieldName}.Invoke({string.Join(", ", invokeArgs)});");
		sb.AppendLine("        }, \"event handler\");");
	}

	private static string GetWasmField(string eventName)
	{
		return $"_{char.ToLowerInvariant(eventName[2])}{eventName.Substring(3)}";
	}

	private static string GetWasmHandler(string eventName) => $"{eventName}Handler";

	private static string GetWasmDelegateType(ImmutableArray<IParameterSymbol> parameters)
	{
		if (parameters.Length == 0) return "Action";
		return $"Action<{GetWasmDelegateTypeArgs(parameters)}>";
	}

	private static string GetWasmDelegateTypeArgs(ImmutableArray<IParameterSymbol> parameters)
	{
		var list = new List<string>();
		foreach (var p in parameters)
		{
			if (IsEntityIdStruct(p.Type, out _) || IsEntityInterface(p.Type, out _))
				list.Add("int");
			else if (p.Type.SpecialType == SpecialType.System_Int32)
				list.Add("int");
			else if (p.Type.SpecialType == SpecialType.System_Single)
				list.Add("float");
			else if (p.Type.SpecialType == SpecialType.System_Boolean)
				list.Add("bool");
		}

		return string.Join(", ", list);
	}

	// ── Output 4: Guest-side WasmWrappers dynamic generation ────────────────────

	private static string GenerateWasmWrappers(INamedTypeSymbol gameApiSymbol, HashSet<INamedTypeSymbol> entityInterfaces)
	{
		var sb = new StringBuilder();
		sb.AppendLine("// <auto-generated/>");
		sb.AppendLine("#nullable enable");
		sb.AppendLine("#pragma warning disable CS8603, CS1591, CS1573");
		sb.AppendLine("using System;");
		sb.AppendLine("using System.Collections.Generic;");
		sb.AppendLine("using System.Linq;");
		sb.AppendLine("using System.Numerics;");
		sb.AppendLine("using System.Runtime.InteropServices;");
		sb.AppendLine("using GameClientWorld.wit.Imports.custom.game;");
		sb.AppendLine();
		sb.AppendLine("namespace Realm.MapAPI;");
		sb.AppendLine();
		sb.AppendLine("public interface IWasmWrapper");
		sb.AppendLine("{");
		sb.AppendLine("    int WasmId { get; }");
		sb.AppendLine("}");
		sb.AppendLine();

		GenerateEntityProxies(sb, entityInterfaces);
		GenerateGameApiProxy(sb, gameApiSymbol);

		return sb.ToString();
	}

	private static void GenerateEntityProxies(StringBuilder sb, HashSet<INamedTypeSymbol> entityInterfaces)
	{
		foreach (var entityIface in entityInterfaces)
		{
			GenerateSingleEntityProxy(sb, entityIface);
		}
	}

	private static void GenerateSingleEntityProxy(StringBuilder sb, INamedTypeSymbol entityIface)
	{
		string cleanName = CleanInterfaceName(entityIface);
		bool hasUniqueId = entityIface.GetMembers().Any(m => m is IPropertySymbol p && p.Name == "UniqueId");

		sb.AppendLine($"public class {cleanName}_WasmModule : {entityIface.ToDisplayString()}, IWasmWrapper");
		sb.AppendLine("{");

		EmitProxyIdProperty(sb, cleanName, hasUniqueId);

		string key = hasUniqueId ? "UniqueId" : "_index";
		var accessorMethods = CollectPropertyAccessorNames(entityIface);

		foreach (var member in entityIface.GetMembers())
		{
			EmitProxyMember(sb, member, cleanName, key, accessorMethods);
		}

		sb.AppendLine("}");
		sb.AppendLine();
	}

	private static void EmitProxyIdProperty(StringBuilder sb, string cleanName, bool hasUniqueId)
	{
		if (hasUniqueId)
		{
			sb.AppendLine("    public int UniqueId { get; }");
			sb.AppendLine("    public int WasmId => UniqueId;");
			sb.AppendLine($"    public {cleanName}_WasmModule(int id) => UniqueId = id;");
		}
		else
		{
			sb.AppendLine("    private readonly int _index;");
			sb.AppendLine("    public int WasmId => _index;");
			sb.AppendLine($"    public {cleanName}_WasmModule(int index) => _index = index;");
		}

		sb.AppendLine();
	}

	private static void EmitProxyMember(StringBuilder sb, ISymbol member, string cleanName, string key, HashSet<string> accessorMethods)
	{
		if (!member.IsAbstract) return;

		if (member is IPropertySymbol property && property.Name != "UniqueId")
		{
			EmitGuestProperty(sb, property, cleanName, key);
		}
		else if (member is IMethodSymbol method && method.MethodKind == MethodKind.Ordinary)
		{
			if (!accessorMethods.Contains(method.Name))
			{
				EmitGuestMethod(sb, method, cleanName, key);
			}
		}
	}

	private static void GenerateGameApiProxy(StringBuilder sb, INamedTypeSymbol gameApiSymbol)
	{
		sb.AppendLine("public class GameAPI_WasmModule : IGameAPI");
		sb.AppendLine("{");
		sb.AppendLine("    private readonly Dictionary<int, Action> _timers = new();");
		sb.AppendLine();

		var propertyAccessorMethods = CollectPropertyAccessorNames(gameApiSymbol);
		foreach (var member in gameApiSymbol.GetMembers())
		{
			if (member is IEventSymbol || !member.IsAbstract) continue;

			if (member is IPropertySymbol property)
			{
				EmitGuestProperty(sb, property, "", "");
			}
			else if (member is IMethodSymbol method && method.MethodKind == MethodKind.Ordinary)
			{
				if (!propertyAccessorMethods.Contains(method.Name))
				{
					EmitGuestMethod(sb, method, "", "");
				}
			}
		}

		GenerateProxyEvents(sb, gameApiSymbol);

		sb.AppendLine("}");
	}

	private static void GenerateProxyEvents(StringBuilder sb, INamedTypeSymbol gameApiSymbol)
	{
		var events = gameApiSymbol.GetMembers().OfType<IEventSymbol>().ToList();

		foreach (var ev in events)
		{
			EmitProxyEventDeclaration(sb, ev);
		}

		sb.AppendLine();

		foreach (var ev in events)
		{
			EmitProxyEventTrigger(sb, ev);
		}
	}

	private static void EmitProxyEventDeclaration(StringBuilder sb, IEventSymbol ev)
	{
		var delegateMethod = ((INamedTypeSymbol)ev.Type).DelegateInvokeMethod;
		if (delegateMethod == null) return;

		sb.AppendLine($"    public event Action<{string.Join(", ", delegateMethod.Parameters.Select(p => p.Type.ToDisplayString()))}>? {ev.Name};");
	}

	private static void EmitProxyEventTrigger(StringBuilder sb, IEventSymbol ev)
	{
		var delegateMethod = ((INamedTypeSymbol)ev.Type).DelegateInvokeMethod;
		if (delegateMethod == null) return;

		var triggerParams = new List<string>();
		foreach (var p in delegateMethod.Parameters)
		{
			if (IsEntityIdStruct(p.Type, out _) || IsEntityInterface(p.Type, out _))
				triggerParams.Add($"int {p.Name}");
			else if (p.Type.ToDisplayString().Contains("Vector3"))
				triggerParams.Add($"Vector3 {p.Name}");
			else
				triggerParams.Add($"{p.Type.ToDisplayString()} {p.Name}");
		}

		var invokeArgs = new List<string>();
		foreach (var p in delegateMethod.Parameters)
		{
			if (IsEntityIdStruct(p.Type, out var structName))
			{
				invokeArgs.Add($"({structName}){p.Name}");
			}
			else if (IsEntityInterface(p.Type, out var unwrapped))
			{
				string cleanElem = CleanInterfaceName(unwrapped);
				if (p.Type.NullableAnnotation == NullableAnnotation.Annotated || p.Type.ToDisplayString().Contains("?"))
					invokeArgs.Add($"{p.Name} > 0 ? new {cleanElem}_WasmModule({p.Name}) : null");
				else
					invokeArgs.Add($"new {cleanElem}_WasmModule({p.Name})");
			}
			else
			{
				invokeArgs.Add(p.Name);
			}
		}

		sb.AppendLine($"    public void TriggerOn{ev.Name.Substring(2)}({string.Join(", ", triggerParams)})");
		sb.AppendLine($"        => {ev.Name}?.Invoke({string.Join(", ", invokeArgs)});");
	}

	private static void EmitGuestProperty(StringBuilder sb, IPropertySymbol property, string prefix, string key)
	{
		string typeStr = property.Type.ToDisplayString();
		string keyArg = string.IsNullOrEmpty(key) ? "" : key;

		if (property.Type.ToDisplayString() == "System.Numerics.Vector3")
		{
			EmitGuestVector3Property(sb, property, typeStr, prefix, keyArg);
			return;
		}

		var retKind = ClassifyReturn(property.Type);
		if (retKind == RetKind.Unsupported) return;

		EmitGuestStandardProperty(sb, property, typeStr, prefix, keyArg, retKind);
	}

	private static void EmitGuestVector3Property(StringBuilder sb, IPropertySymbol property, string typeStr, string prefix, string keyArg)
	{
		sb.AppendLine($"    public {typeStr} {property.Name}");
		sb.AppendLine("    {");
		sb.AppendLine($"        get => new Vector3(");
		sb.AppendLine($"            IGameApiImports.{prefix}{property.Name}X({keyArg}),");
		sb.AppendLine($"            IGameApiImports.{prefix}{property.Name}Y({keyArg}),");
		sb.AppendLine($"            IGameApiImports.{prefix}{property.Name}Z({keyArg}));");

		if (property.SetMethod != null)
		{
			string commaKeyArg = string.IsNullOrEmpty(keyArg) ? "" : $"{keyArg}, ";
			sb.AppendLine("        set");
			sb.AppendLine("        {");
			sb.AppendLine($"            IGameApiImports.Set{prefix}{property.Name}({commaKeyArg}value.X, value.Y, value.Z);");
			sb.AppendLine("        }");
		}

		sb.AppendLine("    }");
	}

	private static void EmitGuestStandardProperty(StringBuilder sb, IPropertySymbol property, string typeStr, string prefix, string keyArg, RetKind retKind)
	{
		sb.AppendLine($"    public {typeStr} {property.Name}");
		sb.AppendLine("    {");

		string getterName = string.IsNullOrEmpty(prefix) ? $"Get{property.Name}" : $"{prefix}{property.Name}";

		if (property.Type.TypeKind == TypeKind.Enum)
		{
			sb.AppendLine($"        get => ({typeStr})IGameApiImports.{getterName}({keyArg});");
		}
		else if (retKind == RetKind.EntityReturn || retKind == RetKind.EntityNullableReturn)
		{
			IsEntityInterface(property.Type, out var unwrapped);
			string cleanElem = CleanInterfaceName(unwrapped);
			if (retKind == RetKind.EntityNullableReturn)
			{
				sb.AppendLine($"        get {{ int id = IGameApiImports.{getterName}({keyArg}); return id > 0 ? new {cleanElem}_WasmModule(id) : null; }}");
			}
			else
			{
				sb.AppendLine($"        get => new {cleanElem}_WasmModule(IGameApiImports.{getterName}({keyArg}));");
			}
		}
		else
		{
			sb.AppendLine($"        get => IGameApiImports.{getterName}({keyArg});");
		}

		if (property.SetMethod != null)
		{
			string setVal = "value";
			if (property.Type.TypeKind == TypeKind.Enum)
			{
				setVal = "(int)value";
			}
			else if (IsEntityInterface(property.Type, out _))
			{
				bool isNullable = property.Type.NullableAnnotation == NullableAnnotation.Annotated || property.Type.ToDisplayString().Contains("?");
				setVal = isNullable ? "(value != null) ? ((IWasmWrapper)value).WasmId : 0" : "((IWasmWrapper)value).WasmId";
			}

			string setArgs = string.IsNullOrEmpty(keyArg) ? setVal : $"{keyArg}, {setVal}";
			sb.AppendLine($"        set => IGameApiImports.Set{prefix}{property.Name}({setArgs});");
		}

		sb.AppendLine("    }");
	}

	private static void EmitGuestMethod(StringBuilder sb, IMethodSymbol method, string prefix, string key)
	{
		if (string.IsNullOrEmpty(prefix))
		{
			if (BuildWitMethodParams(method) == null) return;
		}
		else
		{
			if (BuildWitEntityMethodParams(method) == null) return;
		}

		var retKind = ClassifyReturn(method.ReturnType);
		string typeStr = method.ReturnType.ToDisplayString();

		var paramsDecl = BuildGuestMethodParameters(method);
		var callArgs = BuildGuestMethodCallArgs(method, prefix, key);

		string importName = string.IsNullOrEmpty(prefix) ? method.Name : $"{prefix}{method.Name}";
		string importCall = $"IGameApiImports.{importName}({string.Join(", ", callArgs)})";

		sb.AppendLine($"    public {typeStr} {method.Name}({string.Join(", ", paramsDecl)})");
		sb.AppendLine("    {");

		EmitGuestMethodBody(sb, method, typeStr, retKind, importName, importCall, callArgs);

		sb.AppendLine("    }");
	}

	private static void EmitGuestMethodBody(StringBuilder sb, IMethodSymbol method, string typeStr, RetKind retKind, string importName, string importCall, List<string> callArgs)
	{
		if (method.ReturnType.TypeKind == TypeKind.Enum)
		{
			sb.AppendLine($"        return ({typeStr}){importCall};");
			return;
		}

		switch (typeStr)
		{
			case "System.Numerics.Vector3":
				EmitGuestMethodVector3Return(sb, importName, callArgs);
				return;
		}

		switch (retKind)
		{
			case RetKind.StringListReturn:
				EmitGuestMethodStringListReturn(sb, importName, callArgs);
				return;
			case RetKind.EntityReturn:
			case RetKind.EntityNullableReturn:
				EmitGuestMethodEntityReturn(sb, method, importCall, retKind);
				return;
			case RetKind.EntityListReturn:
				EmitGuestMethodEntityListReturn(sb, method, importCall);
				return;
			case RetKind.Void:
				sb.AppendLine($"        {importCall};");
				return;
		}

		if (method.ReturnType.SpecialType == SpecialType.System_Void)
		{
			sb.AppendLine($"        {importCall};");
			return;
		}

		sb.AppendLine($"        return {importCall};");
	}

	private static List<string> BuildGuestMethodParameters(IMethodSymbol method)
	{
		var paramsDecl = new List<string>();
		foreach (var p in method.Parameters)
		{
			string pType = p.Type.ToDisplayString();
			string pName = p.Name;
			if (p.HasExplicitDefaultValue)
			{
				string defaultVal = p.ExplicitDefaultValue == null ? "null" : (p.ExplicitDefaultValue is bool b ? b.ToString().ToLower() : (p.ExplicitDefaultValue is string s ? $"\"{s}\"" : (p.ExplicitDefaultValue is float f ? $"{f.ToString(CultureInfo.InvariantCulture)}f" : (p.ExplicitDefaultValue is double d ? $"{d.ToString(CultureInfo.InvariantCulture)}" : p.ExplicitDefaultValue.ToString()))));
				paramsDecl.Add($"{pType} {pName} = {defaultVal}");
			}
			else
			{
				paramsDecl.Add($"{pType} {pName}");
			}
		}

		return paramsDecl;
	}

	private static List<string> BuildGuestMethodCallArgs(IMethodSymbol method, string prefix, string key)
	{
		var callArgs = new List<string>();
		if (!string.IsNullOrEmpty(prefix)) callArgs.Add(key);

		foreach (var p in method.Parameters)
		{
			AppendGuestMethodCallArg(callArgs, p);
		}

		return callArgs;
	}

	private static void AppendGuestMethodCallArg(List<string> callArgs, IParameterSymbol p)
	{
		if (TryAppendEntityArg(callArgs, p)) return;
		if (TryAppendVector2Arg(callArgs, p)) return;
		if (TryAppendVector3Arg(callArgs, p)) return;
		if (TryAppendStringArg(callArgs, p)) return;
		if (TryAppendMiscArg(callArgs, p)) return;

		if (p.Type.TypeKind == TypeKind.Enum)
		{
			callArgs.Add($"(int){p.Name}");
			return;
		}

		callArgs.Add(p.Name);
	}

	private static bool TryAppendVector2Arg(List<string> callArgs, IParameterSymbol p)
	{
		string typeStr = p.Type.ToDisplayString();
		if (typeStr == "System.Numerics.Vector2")
		{
			callArgs.Add($"{p.Name}.X");
			callArgs.Add($"{p.Name}.Y");
			return true;
		}

		return false;
	}

	private static bool TryAppendEntityArg(List<string> callArgs, IParameterSymbol p)
	{
		if (IsEntityIdStruct(p.Type, out _))
		{
			bool isNullable = p.Type.NullableAnnotation == NullableAnnotation.Annotated || p.Type.ToDisplayString().Contains("?");
			callArgs.Add(isNullable ? $"({p.Name} != null) ? (int){p.Name} : 0" : $"(int){p.Name}");
			return true;
		}

		if (!IsEntityInterface(p.Type, out _)) return false;

		bool isNullableIface = p.Type.NullableAnnotation == NullableAnnotation.Annotated || p.Type.ToDisplayString().Contains("?");
		callArgs.Add(isNullableIface ? $"({p.Name} != null) ? ((IWasmWrapper){p.Name}).WasmId : 0" : $"((IWasmWrapper){p.Name}).WasmId");
		return true;
	}

	private static bool TryAppendVector3Arg(List<string> callArgs, IParameterSymbol p)
	{
		string typeStr = p.Type.ToDisplayString();
		if (typeStr == "System.Numerics.Vector3")
		{
			callArgs.Add($"{p.Name}.X");
			callArgs.Add($"{p.Name}.Y");
			callArgs.Add($"{p.Name}.Z");
			return true;
		}

		if (typeStr == "System.Numerics.Vector3?")
		{
			callArgs.Add($"{p.Name}?.X ?? 0f");
			callArgs.Add($"{p.Name}?.Y ?? 0f");
			callArgs.Add($"{p.Name}?.Z ?? 0f");
			callArgs.Add($"{p.Name}.HasValue");
			return true;
		}

		return false;
	}

	private static bool TryAppendStringArg(List<string> callArgs, IParameterSymbol p)
	{
		if (p.Type.SpecialType == SpecialType.System_String)
		{
			bool isNullable = p.NullableAnnotation == NullableAnnotation.Annotated || p.Type.ToDisplayString().Contains("?");
			callArgs.Add(isNullable ? $"{p.Name} ?? \"\"" : p.Name);
			return true;
		}

		return false;
	}

	private static bool TryAppendMiscArg(List<string> callArgs, IParameterSymbol p)
	{
		string typeStr = p.Type.ToDisplayString();
		if (typeStr == "object")
		{
			callArgs.Add($"{p.Name}?.ToString() ?? \"\"");
			return true;
		}

		if (ClassifyParam(p.Type) == PrmKind.StringListParam)
		{
			callArgs.Add($"{p.Name}?.ToList() ?? new List<string>()");
			return true;
		}

		return false;
	}

	private static void EmitGuestMethodVector3Return(StringBuilder sb, string importName, List<string> callArgs)
	{
		sb.AppendLine($"        return new Vector3(");
		sb.AppendLine($"            IGameApiImports.{importName}X({string.Join(", ", callArgs)}),");
		sb.AppendLine($"            IGameApiImports.{importName}Y({string.Join(", ", callArgs)}),");
		sb.AppendLine($"            IGameApiImports.{importName}Z({string.Join(", ", callArgs)}));");
	}

	private static void EmitGuestMethodStringListReturn(StringBuilder sb, string importName, List<string> callArgs)
	{
		var callArgsCount = new List<string>(callArgs);
		var callArgsGet = new List<string>(callArgs);
		callArgsGet.Add("i");

		sb.AppendLine($"        int count = IGameApiImports.{importName}Count({string.Join(", ", callArgsCount)});");
		sb.AppendLine($"        var list = new List<string>(count);");
		sb.AppendLine($"        for (int i = 0; i < count; i++)");
		sb.AppendLine($"            list.Add(IGameApiImports.{importName}Get({string.Join(", ", callArgsGet)}));");
		sb.AppendLine("        return list;");
	}

	private static void EmitGuestMethodEntityReturn(StringBuilder sb, IMethodSymbol method, string importCall, RetKind retKind)
	{
		if (IsEntityIdStruct(method.ReturnType, out var structName))
		{
			sb.AppendLine($"        return ({structName}){importCall};");
			return;
		}

		IsEntityInterface(method.ReturnType, out var unwrapped);
		string cleanElem = CleanInterfaceName(unwrapped);
		if (retKind == RetKind.EntityNullableReturn)
		{
			sb.AppendLine($"        int id = {importCall};");
			sb.AppendLine($"        return id > 0 ? new {cleanElem}_WasmModule(id) : null;");
		}
		else
		{
			sb.AppendLine($"        return new {cleanElem}_WasmModule({importCall});");
		}
	}

	private static void EmitGuestMethodEntityListReturn(StringBuilder sb, IMethodSymbol method, string importCall)
	{
		IsCollection(method.ReturnType, out var elemType);
		if (IsEntityIdStruct(elemType, out _))
		{
			sb.AppendLine($"        return MemoryMarshal.Cast<int, {elemType.ToDisplayString()}>({importCall});");
			return;
		}

		string cleanElem = CleanInterfaceName(elemType);
		sb.AppendLine($"        return {importCall}.Select(id => ({elemType.ToDisplayString()})new {cleanElem}_WasmModule(id));");
	}

	private static string ToKebabCase(string value)
	{
		if (string.IsNullOrEmpty(value)) return "";
		var sb = new StringBuilder();
		sb.Append(char.ToLowerInvariant(value[0]));
		for (int i = 1; i < value.Length; i++)
		{
			char c = value[i];
			if (char.IsUpper(c))
			{
				sb.Append('-');
				sb.Append(char.ToLowerInvariant(c));
			}
			else
			{
				sb.Append(c);
			}
		}

		return sb.ToString();
	}

	private static string ToPascalCase(string kebab)
	{
		if (string.IsNullOrEmpty(kebab)) return "";
		var sb = new StringBuilder();
		bool nextUpper = true;
		foreach (char c in kebab)
		{
			if (c == '-')
			{
				nextUpper = true;
			}
			else
			{
				if (nextUpper)
				{
					sb.Append(char.ToUpperInvariant(c));
					nextUpper = false;
				}
				else
				{
					sb.Append(c);
				}
			}
		}

		return sb.ToString();
	}

	private static HashSet<string> CollectPropertyAccessorNames(INamedTypeSymbol symbol)
	{
		var set = new HashSet<string>(StringComparer.Ordinal);
		foreach (var member in symbol.GetMembers())
		{
			if (member is IPropertySymbol prop)
			{
				if (prop.GetMethod != null) set.Add(prop.GetMethod.Name);
				if (prop.SetMethod != null) set.Add(prop.SetMethod.Name);
			}
		}

		return set;
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

	private static void GenerateWasmEntryPoint(string repoRoot, INamedTypeSymbol gameApiSymbol)
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

	private static void EmitWasmEntryPointEventHandler(StringBuilder stringBuilder, IEventSymbol eventSymbol)
	{
		var delegateMethod = ((INamedTypeSymbol)eventSymbol.Type).DelegateInvokeMethod;
		if (delegateMethod == null) return;

		string entryPointName = ToKebabCase(eventSymbol.Name);
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
			else if (parameterType.ToDisplayString() == "System.Numerics.Vector3" || IsNullableVector3(parameterType))
			{
				methodParameters.Add($"float {parameterName}X, float {parameterName}Y, float {parameterName}Z");
				invokeArguments.Add($"new Vector3({parameterName}X, {parameterName}Y, {parameterName}Z)");
			}
			else if (parameterType.ToDisplayString() == "System.Numerics.Vector2")
			{
				methodParameters.Add($"float {parameterName}X, float {parameterName}Y");
				invokeArguments.Add($"new Vector2({parameterName}X, {parameterName}Y)");
			}
			else if (IsEntityInterface(parameterType, out _) || IsEntityIdStruct(parameterType, out _))
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

	private static List<string> GenerateWasmEntryPointParameterNames(ImmutableArray<IParameterSymbol> parameters)
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

	private static bool HasAuthoredSymbolParameterName(IParameterSymbol parameter)
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

	private static string GetDefaultBaseSymbolParameterName(ITypeSymbol type)
	{
		if (IsEntityInterface(type, out var unwrapped))
		{
			string name = CleanInterfaceName(unwrapped);
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

	private static string GetCleanSymbolTypeName(ITypeSymbol type)
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

	private static void GenerateLldbScript(string repoRoot, Compilation compilation)
	{
		string targetPath = Path.Combine(repoRoot, "realm_lldb.py");
		string templatePath = Path.Combine(repoRoot, "Realm.WasmBindgen", "realm_lldb.template.py");

		string templateContent = File.Exists(templatePath)
			? File.ReadAllText(templatePath)
			: "";

		if (string.IsNullOrEmpty(templateContent)) return;

		var sharedMemorySymbol = compilation.GetTypeByMetadataName("Realm.MapAPI.EcsSharedMemory");
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
			("ARMOR", "Realm.MapAPI.Armor"),
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

	private static int GetFieldSize(ITypeSymbol type)
	{
		if (type.SpecialType == SpecialType.System_Boolean || type.SpecialType == SpecialType.System_Byte || type.SpecialType == SpecialType.System_SByte)
			return 1;
		if (type.SpecialType == SpecialType.System_Int16 || type.SpecialType == SpecialType.System_UInt16)
			return 2;
		if (type.SpecialType == SpecialType.System_Int32 || type.SpecialType == SpecialType.System_UInt32 || type.SpecialType == SpecialType.System_Single)
			return 4;
		if (type.SpecialType == SpecialType.System_Int64 || type.SpecialType == SpecialType.System_UInt64 || type.SpecialType == SpecialType.System_Double)
			return 8;
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
		if (type.SpecialType == SpecialType.System_Boolean || type.SpecialType == SpecialType.System_Byte || type.SpecialType == SpecialType.System_SByte)
			return 1;
		if (type.SpecialType == SpecialType.System_Int16 || type.SpecialType == SpecialType.System_UInt16)
			return 2;
		if (type.SpecialType == SpecialType.System_Int32 || type.SpecialType == SpecialType.System_UInt32 || type.SpecialType == SpecialType.System_Single)
			return 4;
		if (type.SpecialType == SpecialType.System_Int64 || type.SpecialType == SpecialType.System_UInt64 || type.SpecialType == SpecialType.System_Double)
			return 8;
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

	private static string FormatPythonFieldList(List<(string Name, int Offset, string TypeName)> fields)
	{
		var sb = new StringBuilder();
		sb.AppendLine("[");
		foreach (var (name, offset, typeName) in fields)
		{
			sb.AppendLine($"    (\"{name}\", {offset}, \"{typeName}\"),");
		}
		sb.Append("]");
		return sb.ToString();
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
			Path.Combine("Realm.Ecs", "Components", "Combat", "Armor.cs"),
			Path.Combine("Realm.Ecs", "Components", "Core", "Position.cs"),
			Path.Combine("Realm.Ecs", "Components", "Core", "Health.cs"),
			Path.Combine("Realm.Ecs", "Components", "Core", "Mana.cs"),
			Path.Combine("Realm.Ecs", "Components", "Core", "PlayerPopulation.cs"),
			Path.Combine("Realm.Ecs", "Components", "Core", "ScriptPlayer.cs"),
			Path.Combine("Realm.Ecs", "Components", "Core", "UnitOwnerPlayer.cs"),
			Path.Combine("Realm.Ecs", "Components", "Meta", "Experience.cs"),
			Path.Combine("Realm.Ecs", "Components", "Meta", "Level.cs"),
			Path.Combine("Realm.Ecs", "Components", "Meta", "ModelScale.cs"),
			Path.Combine("Realm.Ecs", "Components", "Meta", "RotationY.cs"),
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

	private static readonly Dictionary<string, string> ComponentMemberDescriptions = new(StringComparer.OrdinalIgnoreCase)
	{
		["Health.Current"] = "The current health points of the entity.",
		["Health.Max"] = "The maximum health point capacity of the entity.",
		["Health.HpRegen"] = "Health point regeneration rate per second.",
		["Health.HpRegenCombatDelay"] = "Delay duration in seconds before health regeneration resumes after taking damage.",
		["Health.TimeSinceLastDamage"] = "Elapsed time in seconds since the entity last sustained damage.",
		["Health.CurrentHp"] = "Gets or sets the current health points (alias for Current).",
		["Health.MaxHp"] = "Gets or sets the maximum health capacity (alias for Max).",
		["Mana.Current"] = "The current mana points of the entity.",
		["Mana.Max"] = "The maximum mana capacity of the entity.",
		["Mana.ManaRegen"] = "Mana point regeneration rate per second.",
		["Mana.CurrentMana"] = "Gets or sets the current mana points (alias for Current).",
		["Mana.MaxMana"] = "Gets or sets the maximum mana capacity (alias for Max).",
		["Position.Value"] = "The 3D world coordinate position vector.",
		["Attack.Damage"] = "Base attack damage dealt.",
		["Attack.Range"] = "Maximum attack range distance in world units.",
		["Attack.Cooldown"] = "Attack interval or cooldown duration in seconds.",
		["Attack.CurrentCooldown"] = "Current remaining attack cooldown in seconds.",
		["Attack.DamageVariance"] = "Damage variance percentage or range.",
		["Attack.DamageTypeId"] = "String registry identifier for the damage category.",
		["Attack.FlatArmorPenetration"] = "Flat armor penetration amount.",
		["Attack.PercentArmorPenetration"] = "Percent armor penetration ratio (0.0 to 1.0).",
		["Attack.CritChance"] = "Critical strike probability ratio (0.0 to 1.0).",
		["Attack.CritMultiplier"] = "Critical strike damage multiplier.",
		["Attack.SplashType"] = "Area-of-effect splash falloff style.",
		["Attack.SplashInnerRadius"] = "Inner splash radius distance in world units.",
		["Attack.SplashMediumRadius"] = "Medium splash radius distance in world units.",
		["Attack.SplashOuterRadius"] = "Outer splash radius distance in world units.",
		["Attack.SplashInnerRatio"] = "Damage multiplier applied within inner splash radius.",
		["Attack.SplashMediumRatio"] = "Damage multiplier applied within medium splash radius.",
		["Attack.SplashOuterRatio"] = "Damage multiplier applied within outer splash radius.",
		["Attack.FriendlyFireByte"] = "Backing byte indicating whether splash damage hurts allied units (1 if true, 0 if false).",
		["Attack.FriendlyFire"] = "Gets or sets a value indicating whether splash damage affects friendly units.",
		["Attack.DamageType"] = "Gets or sets the damage category name.",
		["Armor.FlatArmor"] = "Flat armor value directly mitigating damage.",
		["Armor.RatedArmor"] = "Rated armor value providing percentage-based diminishing return damage mitigation.",
		["Armor.StringId"] = "String registry identifier for the armor category.",
		["Armor.Value"] = "Gets the effective flat armor rating (alias for FlatArmor).",
		["Armor.ArmorType"] = "Gets or sets the armor category name.",
		["MovementStats.Speed"] = "Maximum linear traversal speed in world units per second.",
		["MovementStats.Acceleration"] = "Acceleration rate in units per second squared.",
		["MovementStats.TurnRate"] = "Rotational turning rate in radians per second.",
		["MovementStats.PushPriority"] = "Crowd simulation push avoidance priority.",
		["MovementStats.Type"] = "Locomotion traversal medium.",
		["MovementStats.MovementType"] = "Gets or sets the movement type category as a readable string.",
		["PlayerResources.MaxCustomSlots"] = "Maximum custom resource slots supported in fixed buffer.",
		["PlayerResources.Gold"] = "Gold resource stockpile amount.",
		["PlayerResources.Wood"] = "Wood resource stockpile amount.",
		["PlayerResources.Stone"] = "Stone resource stockpile amount.",
		["PlayerResources.Ore"] = "Ore resource stockpile amount.",
		["PlayerResources.CustomSlots"] = "Fixed array buffer for custom resource categories.",
		["ScriptPlayer.Gold"] = "Current gold resource amount.",
		["ScriptPlayer.Wood"] = "Current wood resource amount.",
		["ScriptPlayer.Stone"] = "Current stone resource amount.",
		["ScriptPlayer.ActiveByte"] = "Backing byte indicating whether the player slot is active (1 if active, 0 otherwise).",
		["ScriptPlayer.IsComputerByte"] = "Backing byte indicating whether the player is controlled by AI (1 if AI, 0 if human).",
		["ScriptPlayer.Reserved0"] = "Reserved alignment padding byte.",
		["ScriptPlayer.Reserved1"] = "Reserved alignment padding byte.",
		["ScriptPlayer.Team"] = "Team identifier number.",
		["ScriptPlayer.NameStringId"] = "String registry identifier for player display name.",
		["ScriptPlayer.KillCount"] = "Total enemy unit kill count credited to this player.",
		["ScriptPlayer.Active"] = "Gets or sets a value indicating whether this player slot is active.",
		["ScriptPlayer.IsComputer"] = "Gets or sets a value indicating whether this player slot is controlled by computer AI.",
		["ScriptPlayer.Name"] = "Gets or sets the player display name.",
		["PlayerPopulation.Current"] = "Current food population consumed.",
		["PlayerPopulation.Max"] = "Maximum food population capacity.",
		["UnitOwnerPlayer.Value"] = "Controlling player slot index value.",
		["Level.Value"] = "Current hero advancement level value.",
		["Experience.Current"] = "Current accumulated experience points value.",
		["ModelScale.Value"] = "Mesh scaling factor value.",
		["RotationY.Value"] = "Rotational azimuth heading around vertical Y-axis in radians.",
		["ResourceId.StringId"] = "String registry identifier for the resource name.",
		["ResourceId.Value"] = "Canonical string name of the resource.",
	};

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
					if (ComponentMemberDescriptions.TryGetValue(key, out var knownDoc))
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