using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Realm.WasmBindgen;

/// <summary>
///     Keeps the map-script import surface additive. A compiled map links against the host imports this build exposes, and
///     an import that disappears or changes signature cannot be repaired at runtime: only a recompile fixes the map, and
///     only its author can recompile it. So the surface this build generates is compared with a committed baseline, and a
///     removal or a retype fails the build unless the engine generation was bumped in the same change - a deliberate,
///     visible edit rather than a build that quietly breaks every map already in the Workshop.
/// </summary>
public static partial class MapApiSurfaceGuard
{
	/// <summary>The committed baseline the generated surface is compared with, at the repository root.</summary>
	public const string BaselineFileName = "MapApiSurface.baseline.json";

	/// <summary>The MSBuild property that writes a new baseline instead of verifying one.</summary>
	public const string BaselineOutputProperty = "RealmMapApiSurfaceBaselineOut";

	/// <summary>The command that regenerates the baseline, which is the one the guard tells an author to run.</summary>
	public const string RegenerationCommand = "pwsh scripts/generate-map-api-surface.ps1";

	private static readonly DiagnosticDescriptor RemovalDescriptor = new(
		"REALMAPI001",
		"A map-script import was removed or retyped without an engine generation bump",
		"{0}",
		"Realm.MapAPI.Surface",
		DiagnosticSeverity.Error,
		true);

	private static readonly DiagnosticDescriptor NoteDescriptor = new(
		"REALMAPI002",
		"The map-script import surface moved",
		"{0}",
		"Realm.MapAPI.Surface",
		DiagnosticSeverity.Info,
		true);

	/// <summary>
	///     Verifies, or writes, the baseline for the surface this build generated.
	/// </summary>
	/// <param name="context">The generator context that reports the result.</param>
	/// <param name="repositoryRoot">The repository root, or empty when the generator could not find it.</param>
	/// <param name="witContent">The generated WIT document, which is the import surface itself.</param>
	/// <param name="options">The build's analyzer options, which carry the regeneration property.</param>
	public static void Enforce(SourceProductionContext context, string repositoryRoot, string witContent, AnalyzerConfigOptionsProvider options)
	{
		if (string.IsNullOrEmpty(repositoryRoot) || string.IsNullOrEmpty(witContent)) return;

		Dictionary<string, string> currentImports = ParseImports(witContent);
		if (currentImports.Count == 0) return;

		if (TryGetProperty(options, BaselineOutputProperty, out string baselineOutputPath))
		{
			WriteBaseline(repositoryRoot, baselineOutputPath, currentImports);
			context.ReportDiagnostic(Diagnostic.Create(NoteDescriptor, Location.None,
				$"The map API baseline was written to {baselineOutputPath}. Commit it with the change that moved the surface."));
			return;
		}

		string baselinePath = Path.Combine(repositoryRoot, BaselineFileName);
		if (!File.Exists(baselinePath))
		{
			context.ReportDiagnostic(Diagnostic.Create(NoteDescriptor, Location.None,
				$"There is no map API baseline at {BaselineFileName}, so the additive guard has nothing to compare against. Create it with '{RegenerationCommand}'."));
			return;
		}

		int baselineGeneration = 1;
		Dictionary<string, string> baselineImports;
		try
		{
			JsonNode? root = JsonNode.Parse(File.ReadAllText(baselinePath));
			baselineGeneration = root?["generation"]?.GetValue<int>() ?? 1;
			baselineImports = new Dictionary<string, string>(StringComparer.Ordinal);
			if (root?["imports"] is JsonObject importsObject)
			{
				foreach (KeyValuePair<string, JsonNode?> pair in importsObject)
				{
					baselineImports[pair.Key] = pair.Value?.GetValue<string>() ?? string.Empty;
				}
			}
		}
		catch (Exception exception)
		{
			context.ReportDiagnostic(Diagnostic.Create(RemovalDescriptor, Location.None,
				$"{BaselineFileName} could not be read ({exception.Message}), so the additive map-script guard cannot judge this build."));
			return;
		}

		var removed = baselineImports.Keys.Where(name => !currentImports.ContainsKey(name)).OrderBy(name => name, StringComparer.Ordinal).ToList();
		var retyped = baselineImports.Keys
			.Where(name => currentImports.TryGetValue(name, out string? signature) && !string.Equals(signature, baselineImports[name], StringComparison.Ordinal))
			.OrderBy(name => name, StringComparer.Ordinal)
			.ToList();
		var added = currentImports.Keys.Where(name => !baselineImports.ContainsKey(name)).OrderBy(name => name, StringComparer.Ordinal).ToList();

		if (removed.Count == 0 && retyped.Count == 0)
		{
			if (added.Count > 0)
			{
				context.ReportDiagnostic(Diagnostic.Create(NoteDescriptor, Location.None,
					$"{added.Count} map-script import(s) were added, which is the normal direction of growth. Run '{RegenerationCommand}' to record them in {BaselineFileName}."));
			}

			return;
		}

		int currentGeneration = ReadEngineGeneration(repositoryRoot);
		string change = DescribeChange(removed, retyped);
		if (baselineGeneration >= currentGeneration)
		{
			context.ReportDiagnostic(Diagnostic.Create(RemovalDescriptor, Location.None,
				$"The map-script import surface lost {change} while the baseline still records engine generation {baselineGeneration.ToString(CultureInfo.InvariantCulture)} and RealmVersion states {currentGeneration.ToString(CultureInfo.InvariantCulture)}. A map that already shipped cannot recompile itself, so this change has to bump EngineGeneration and regenerate {BaselineFileName} with '{RegenerationCommand}'."));
			return;
		}

		context.ReportDiagnostic(Diagnostic.Create(NoteDescriptor, Location.None,
			$"The map-script import surface lost {change}, which engine generation {currentGeneration.ToString(CultureInfo.InvariantCulture)} allows. Run '{RegenerationCommand}' to record the new surface in {BaselineFileName}."));
	}

	/// <summary>
	///     Reads the imports a generated WIT document states, keyed by name. Manual entries are parsed the same way, because
	///     they are imports the host still has to satisfy.
	/// </summary>
	/// <param name="witContent">The generated WIT document.</param>
	/// <returns>Each import name with its stated signature.</returns>
	public static Dictionary<string, string> ParseImports(string witContent)
	{
		var imports = new Dictionary<string, string>(StringComparer.Ordinal);
		foreach (string line in witContent.Replace("\r\n", "\n").Split('\n'))
		{
			string trimmed = line.TrimEnd();
			if (!trimmed.StartsWith("    ", StringComparison.Ordinal)) continue;

			string body = trimmed.TrimStart();
			if (body.StartsWith("//", StringComparison.Ordinal)) continue;

			Match match = ImportLineRegex().Match(body);
			if (!match.Success) continue;

			imports[match.Groups[1].Value] = match.Groups[2].Value.Trim();
		}

		return imports;
	}

	private static string DescribeChange(List<string> removed, List<string> retyped)
	{
		var builder = new StringBuilder();
		if (removed.Count > 0)
		{
			builder.Append(removed.Count == 1 ? "the import " : "the imports ");
			builder.Append(string.Join(", ", removed.Select(name => $"'{name}'")));
		}

		if (retyped.Count > 0)
		{
			if (builder.Length > 0) builder.Append(", and changed the signature of ");
			else builder.Append("the signature of ");
			builder.Append(string.Join(", ", retyped.Select(name => $"'{name}'")));
		}

		return builder.ToString();
	}

	private static void WriteBaseline(string repositoryRoot, string baselineOutputPath, Dictionary<string, string> imports)
	{
		var importObject = new JsonObject();
		foreach (KeyValuePair<string, string> import in imports.OrderBy(pair => pair.Key, StringComparer.Ordinal))
		{
			importObject[import.Key] = import.Value;
		}

		var root = new JsonObject
		{
			["comment"] = "The map-script import surface as it shipped. Additive growth is normal; a removal or a retype is a break that only an engine generation bump may carry.",
			["generation"] = ReadEngineGeneration(repositoryRoot),
			["imports"] = importObject
		};

		string resolvedPath = Path.IsPathRooted(baselineOutputPath) ? baselineOutputPath : Path.Combine(repositoryRoot, baselineOutputPath);
		string? directory = Path.GetDirectoryName(resolvedPath);
		if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory)) Directory.CreateDirectory(directory);

		File.WriteAllText(resolvedPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n", new UTF8Encoding(false));
	}

	private static int ReadEngineGeneration(string repositoryRoot)
	{
		string versionPath = Path.Combine(repositoryRoot, "Realm.Shared", "RealmVersion.cs");
		if (!File.Exists(versionPath)) return 1;

		Match match = EngineGenerationRegex().Match(File.ReadAllText(versionPath));
		return match.Success && int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int generation)
			? generation
			: 1;
	}

	private static bool TryGetProperty(AnalyzerConfigOptionsProvider options, string name, out string value)
	{
		value = string.Empty;
		string key = $"build_property.{name}";
		if (!options.GlobalOptions.TryGetValue(key, out string? stored)) return false;
		if (string.IsNullOrWhiteSpace(stored)) return false;

		value = stored.Trim();
		return true;
	}

	[GeneratedRegex(@"^([A-Za-z0-9][A-Za-z0-9-]*):\s*(.+);$")]
	private static partial Regex ImportLineRegex();

	[GeneratedRegex(@"public\s+const\s+int\s+EngineGeneration\s*=\s*(\d+)\s*;")]
	private static partial Regex EngineGenerationRegex();
}
