using System.Text.Json.Nodes;
using Realm.Shared.Distribution;
using Realm.Shared.Services;

namespace Realm.Simulation.Tests;

/// <summary>
///     Composes the melee ruleset into the Secluded Crest board the way a save composes it: a copy of the terrain map's
///     folder, a <c>references.json</c> that names the Melee Ruleset, the resolver run over it, and the map script compiled
///     in the composed folder exactly as the editor compiles it. What is left is one ordinary map folder, which is what a
///     player downloads and what the authority loads.
/// </summary>
internal sealed class ComposedMeleeMap : IDisposable
{
	public const string TerrainFileName = "terrain.json";
	public const string HeightsFileName = "terrain_heights.exr";
	public const string RulesScriptFileName = "MapScript.cs";
	public const string RulesAffinityFileName = "MapScript.DamageAffinity.cs";

	private static readonly string[] SkippedFolders = { "obj", ".git", ".vscode", "bin" };

	private readonly string _scriptPath;

	private ComposedMeleeMap(string folder, string scriptPath, MapReferenceResolutionResult firstPass, MapReferenceResolutionResult secondPass)
	{
		Folder = folder;
		_scriptPath = scriptPath;
		FirstPass = firstPass;
		SecondPass = secondPass;
	}

	public string Folder { get; }

	/// <summary>The save that wrote the referenced map's content into the folder.</summary>
	public MapReferenceResolutionResult FirstPass { get; }

	/// <summary>The save straight after it, which has nothing left to write.</summary>
	public MapReferenceResolutionResult SecondPass { get; }

	public string ScriptPath => _scriptPath;

	public static string ParentFolder => MeleeTestMaps.TerrainFolder;

	public static string ReferencedFolder => MeleeTestMaps.MeleeFolder;

	public static MapManifest ParentManifest => MapManifest.LoadFromFile(Path.Combine(ParentFolder, "manifest.json"));

	public static MapManifest ReferencedManifest => MapManifest.LoadFromFile(Path.Combine(ReferencedFolder, "manifest.json"));

	public static string ReferencedManifestBlake3 => ReferencedManifest.ComputeManifestBlake3();

	public static string ReferencedMapName => ReferencedManifest.MapName;

	/// <summary>The folder the referenced map's sources are composed into, named the way the resolver names it.</summary>
	public static string ComposedRulesFolder => $"{MapScriptComposer.ComposedDirectoryName}/{MapScriptComposer.ToIdentifier(ReferencedMapName)}";

	public static string ComposedRulesScriptFileName => $"{ComposedRulesFolder}/{RulesScriptFileName}";

	public static string ComposedRulesAffinityFileName => $"{ComposedRulesFolder}/{RulesAffinityFileName}";

	/// <summary>The namespace the composed rules occupy, so two maps that both declare <c>Realm.Maps.MapScript</c> still compile.</summary>
	public static string ComposedRulesNamespace => MapScriptComposer.GetNamespaceFor(ReferencedMapName);

	public static ComposedMeleeMap Assemble()
	{
		string folder = Path.Combine(Path.GetTempPath(), "realm-melee-references", Guid.NewGuid().ToString("N"));
		CopyFolder(ParentFolder, folder);
		WriteReferences(folder);

		MapReferenceResolutionResult firstPass = MapReferenceResolver.Apply(folder);
		if (!firstPass.Success) throw new InvalidOperationException($"The referenced map could not be applied: {firstPass.Message}");

		string scriptPath = MeleeTestMaps.PublishScript(folder);

		MapManifest parentManifest = ParentManifest;
		MapManifest composedManifest = MapManifest.CreateFromDirectory(folder, parentManifest.MapName, parentManifest.Author, parentManifest.Version);
		MapFileService.SaveManifest(folder, composedManifest);

		MapReferenceResolutionResult secondPass = MapReferenceResolver.Apply(folder);
		if (!secondPass.Success) throw new InvalidOperationException($"The second save of the composed map failed: {secondPass.Message}");

		return new ComposedMeleeMap(folder, scriptPath, firstPass, secondPass);
	}

	private static void WriteReferences(string folder)
	{
		MapManifest referenced = ReferencedManifest;
		var references = new MapReferences();
		references.References.Add(new MapReference
		{
			MapName = ReferencedMapName,
			Version = string.IsNullOrWhiteSpace(referenced.Version) ? "1.0.0" : referenced.Version,
			ManifestBlake3 = referenced.ComputeManifestBlake3(),
			FolderPath = ReferencedFolder
		});
		references.Save(folder);
	}

	private static void CopyFolder(string source, string destination)
	{
		Directory.CreateDirectory(destination);
		foreach (string directory in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
		{
			string relative = Path.GetRelativePath(source, directory);
			if (relative.Split(Path.DirectorySeparatorChar).Any(segment => SkippedFolders.Contains(segment, StringComparer.OrdinalIgnoreCase))) continue;

			Directory.CreateDirectory(Path.Combine(destination, relative));
		}

		foreach (string filePath in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
		{
			string relative = Path.GetRelativePath(source, filePath);
			if (relative.Split(Path.DirectorySeparatorChar).Any(segment => SkippedFolders.Contains(segment, StringComparer.OrdinalIgnoreCase))) continue;

			string destinationPath = Path.Combine(destination, relative);
			string? destinationDirectory = Path.GetDirectoryName(destinationPath);
			if (!string.IsNullOrEmpty(destinationDirectory)) Directory.CreateDirectory(destinationDirectory);
			File.Copy(filePath, destinationPath, overwrite: true);
		}
	}

	public JsonNode ReadMetadata() => JsonNode.Parse(File.ReadAllText(AbsolutePathOf(MapMetadataMerger.MetadataFileName)))!;

	public JsonNode ReadMetadataPartial() => JsonNode.Parse(File.ReadAllText(AbsolutePathOf(MapMetadataMerger.MetadataPartialFileName)))!;

	public MapManifest Manifest => MapManifest.LoadFromFile(AbsolutePathOf("manifest.json"));

	public bool HasFile(string relativePath) => File.Exists(AbsolutePathOf(relativePath));

	public string AbsolutePathOf(string relativePath) => Path.Combine(Folder, relativePath.Replace('/', Path.DirectorySeparatorChar));

	public static string ParentPathOf(string relativePath) => Path.Combine(ParentFolder, relativePath.Replace('/', Path.DirectorySeparatorChar));

	public static string ReferencedPathOf(string relativePath) => Path.Combine(ReferencedFolder, relativePath.Replace('/', Path.DirectorySeparatorChar));

	public static IEnumerable<string> TemplateIdsOf(string metadataPath, string category)
	{
		JsonArray? entries = JsonNode.Parse(File.ReadAllText(metadataPath))?["Templates"]?[category] as JsonArray;
		if (entries == null) yield break;

		foreach (JsonNode? entry in entries)
		{
			string? identifier = entry?["TemplateID"]?.GetValue<string>();
			if (!string.IsNullOrEmpty(identifier)) yield return identifier;
		}
	}

	public void Dispose()
	{
		try
		{
			MapReferenceResolver.ClearReadOnlyRecursive(Folder);
			Directory.Delete(Folder, recursive: true);
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
		}
	}
}
