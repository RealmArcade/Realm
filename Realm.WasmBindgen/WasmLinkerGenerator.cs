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
		string ecsComponents = !string.IsNullOrEmpty(repoRoot) ? WasmEcsComponentEmitter.GenerateEcsComponents(repoRoot) : "";
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
			WasmLldbScriptWriter.GenerateLldbScript(repoRoot, lldbCompilation);
			WasmNatvisWriter.GenerateNatvisFile(repoRoot, lldbCompilation);
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

	[GeneratedRegex(@"interface\s+game-api\s*\{(.*?)\}", RegexOptions.Singleline)]
	private static partial Regex GameApiInterfaceRegex();
}