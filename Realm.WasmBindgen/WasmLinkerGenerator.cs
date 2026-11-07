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
			GenerateWasmEntryPoint(repoRoot, gameApiSymbol);
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
				SourceText.From(GenerateAutoEvents(gameApiSymbol, host.Namespace, host.Type), Encoding.UTF8));

			context.AddSource("GeneratedWit.g.cs",
				SourceText.From(GenerateWitConstant(gameApiSymbol, entityInterfaces, manualFunctions, host.Namespace), Encoding.UTF8));
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


	private static string GenerateAutoEvents(INamedTypeSymbol gameApiSymbol, string hostNamespace, string hostType)
	{
		var sb = new StringBuilder();
		sb.AppendLine("// <auto-generated/>");
		sb.AppendLine("#nullable enable");
		sb.AppendLine("using System;");
		sb.AppendLine("using System.Linq;");
		sb.AppendLine("using Wasmtime;");
		sb.AppendLine("using Realm.Ecs;");
		sb.AppendLine("using Realm.MapAPI;");
		sb.AppendLine();
		sb.AppendLine($"namespace {hostNamespace};");
		sb.AppendLine();
		sb.AppendLine($"partial class {hostType}");
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

			string witName = WitIdentifierFormat.ToKebabCase(ev.Name);
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
			else
			{
				AppendEntityInvokeArg(invokeArgs, p, gameApiSymbol);
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
			AppendEntityInvokeArg(invokeArgs, p, gameApiSymbol);
		}

		sb.AppendLine($"            {fieldName}.Invoke({string.Join(", ", invokeArgs)});");
		sb.AppendLine("        }, \"event handler\");");
	}

	private static void AppendEntityInvokeArg(List<string> invokeArgs, IParameterSymbol p, INamedTypeSymbol gameApiSymbol)
	{
		if (WasmEntityDiscovery.IsEntityIdStruct(p.Type, out _))
		{
			invokeArgs.Add($"(int){p.Name}");
		}
		else if (WasmEntityDiscovery.IsEntityInterface(p.Type, out var unwrapped))
		{
			bool hasId = unwrapped.GetMembers().Any(m => m is IPropertySymbol prop && prop.Name == "UniqueId");
			if (hasId)
			{
				invokeArgs.Add($"{p.Name}?.UniqueId ?? 0");
			}
			else
			{
				string col = WasmEntityDiscovery.FindCollectionMember(gameApiSymbol, unwrapped);
				invokeArgs.Add($"{p.Name} != null ? (((_cachedApi ?? (IGameAPI?)Realm.MapAPI.GameAPI.Current)?.{col}().ToList().IndexOf({p.Name}) ?? -1) : -1");
			}
		}
		else
		{
			invokeArgs.Add(p.Name);
		}
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
			if (WasmEntityDiscovery.IsEntityIdStruct(p.Type, out _) || WasmEntityDiscovery.IsEntityInterface(p.Type, out _))
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
		string cleanName = WitIdentifierFormat.CleanInterfaceName(entityIface);
		bool hasUniqueId = entityIface.GetMembers().Any(m => m is IPropertySymbol p && p.Name == "UniqueId");

		sb.AppendLine($"public class {cleanName}_WasmModule : {entityIface.ToDisplayString()}, IWasmWrapper");
		sb.AppendLine("{");

		EmitProxyIdProperty(sb, cleanName, hasUniqueId);

		string key = hasUniqueId ? "UniqueId" : "_index";
		var accessorMethods = WasmHostBindingEmitter.CollectPropertyAccessorNames(entityIface);

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

		var propertyAccessorMethods = WasmHostBindingEmitter.CollectPropertyAccessorNames(gameApiSymbol);
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
			if (WasmEntityDiscovery.IsEntityIdStruct(p.Type, out _) || WasmEntityDiscovery.IsEntityInterface(p.Type, out _))
				triggerParams.Add($"int {p.Name}");
			else if (p.Type.ToDisplayString().Contains("Vector3"))
				triggerParams.Add($"Vector3 {p.Name}");
			else
				triggerParams.Add($"{p.Type.ToDisplayString()} {p.Name}");
		}

		var invokeArgs = new List<string>();
		foreach (var p in delegateMethod.Parameters)
		{
			if (WasmEntityDiscovery.IsEntityIdStruct(p.Type, out var structName))
			{
				invokeArgs.Add($"({structName}){p.Name}");
			}
			else if (WasmEntityDiscovery.IsEntityInterface(p.Type, out var unwrapped))
			{
				string cleanElem = WitIdentifierFormat.CleanInterfaceName(unwrapped);
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

		var retKind = WasmTypeClassifier.ClassifyReturn(property.Type);
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
			WasmEntityDiscovery.IsEntityInterface(property.Type, out var unwrapped);
			string cleanElem = WitIdentifierFormat.CleanInterfaceName(unwrapped);
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
			else if (WasmEntityDiscovery.IsEntityInterface(property.Type, out _))
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
			if (WasmWitDocumentWriter.BuildWitMethodParams(method) == null) return;
		}
		else
		{
			if (WasmWitDocumentWriter.BuildWitEntityMethodParams(method) == null) return;
		}

		var retKind = WasmTypeClassifier.ClassifyReturn(method.ReturnType);
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
		if (WasmEntityDiscovery.IsEntityIdStruct(p.Type, out _))
		{
			bool isNullable = p.Type.NullableAnnotation == NullableAnnotation.Annotated || p.Type.ToDisplayString().Contains("?");
			callArgs.Add(isNullable ? $"({p.Name} != null) ? (int){p.Name} : 0" : $"(int){p.Name}");
			return true;
		}

		if (!WasmEntityDiscovery.IsEntityInterface(p.Type, out _)) return false;

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

		if (WasmTypeClassifier.ClassifyParam(p.Type) == PrmKind.StringListParam)
		{
			callArgs.Add($"{p.Name}?.ToList() ?? new List<string>()");
			return true;
		}

		if (WasmTypeClassifier.ClassifyParam(p.Type) == PrmKind.FloatListParam)
		{
			callArgs.Add(p.Type is IArrayTypeSymbol
				? $"{p.Name} ?? System.Array.Empty<float>()"
				: $"{p.Name}.ToArray()");
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
		if (WasmEntityDiscovery.IsEntityIdStruct(method.ReturnType, out var structName))
		{
			sb.AppendLine($"        return ({structName}){importCall};");
			return;
		}

		WasmEntityDiscovery.IsEntityInterface(method.ReturnType, out var unwrapped);
		string cleanElem = WitIdentifierFormat.CleanInterfaceName(unwrapped);
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
		WasmEntityDiscovery.IsCollection(method.ReturnType, out var elemType);
		if (WasmEntityDiscovery.IsEntityIdStruct(elemType, out _))
		{
			sb.AppendLine($"        return MemoryMarshal.Cast<int, {elemType.ToDisplayString()}>({importCall});");
			return;
		}

		string cleanElem = WitIdentifierFormat.CleanInterfaceName(elemType);
		sb.AppendLine($"        return {importCall}.Select(id => ({elemType.ToDisplayString()})new {cleanElem}_WasmModule(id));");
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