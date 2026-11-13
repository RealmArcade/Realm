using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Realm.Shared.Distribution;
using Realm.Shared.Metadata;
using Realm.Shared.Terrain;

namespace Realm.Shared.Services;

public static class MapFileService
{
	/// <summary>
	///     The folder inside a map's <c>bin</c> that holds the one compiled script the map runs. Every build
	///     configuration publishes here, so the artifact present is the one built last.
	/// </summary>
	public const string ScriptOutputDirectoryName = "script";

	/// <summary>
	///     The file beside a compiled script that records which build configuration produced it, so a release export can
	///     tell a stripped artifact from one built to be debugged.
	/// </summary>
	public const string BuildFlavorFileName = ".build-flavor";

	public const string DebugBuildFlavor = "Debug";

	public const string ReleaseBuildFlavor = "Release";

	public static Action<string>? FileWritten { get; set; }

	public static string ResolveScriptOutputDirectory(string mapFolder) => Path.Combine(mapFolder, "bin", ScriptOutputDirectoryName);

	public static string ResolveBuildFlavorPath(string mapFolder) => Path.Combine(ResolveScriptOutputDirectory(mapFolder), BuildFlavorFileName);

	private static readonly JsonSerializerOptions ManifestJsonOptions = new()
	{
		AllowTrailingCommas = true,
		ReadCommentHandling = JsonCommentHandling.Skip,
		WriteIndented = true,
		PropertyNameCaseInsensitive = true,
		UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
		Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
	};

	private static readonly JsonSerializerOptions MetadataJsonOptions = new()
	{
		PropertyNameCaseInsensitive = true,
		IncludeFields = true,
		WriteIndented = true,
		UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
		Converters = { new JsonStringEnumConverter() },
		Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
	};

	private static readonly JsonSerializerOptions TerrainJsonOptions = new()
	{
		PropertyNameCaseInsensitive = true,
		IncludeFields = true,
		WriteIndented = true,
		UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
		Converters = { new JsonStringEnumConverter() },
		Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
	};

	public static string ResolvePath(string? pathOrDirectory, string targetFileName)
	{
		if (string.IsNullOrWhiteSpace(pathOrDirectory))
		{
			return targetFileName;
		}

		if (Directory.Exists(pathOrDirectory) || !pathOrDirectory.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
		{
			return Path.Combine(pathOrDirectory, targetFileName);
		}

		return pathOrDirectory;
	}

	public static MapManifest LoadManifest(string pathOrDirectory)
	{
		string targetPath = ResolvePath(pathOrDirectory, "manifest.json");
		if (!File.Exists(targetPath))
		{
			return new MapManifest();
		}

		string json = File.ReadAllText(targetPath);
		return DeserializeOrExplain<MapManifest>(json, ManifestJsonOptions, targetPath) ?? new MapManifest();
	}

	public static MapManifest LoadManifestFromJson(string json)
	{
		if (string.IsNullOrWhiteSpace(json))
		{
			return new MapManifest();
		}

		return JsonSerializer.Deserialize<MapManifest>(json, ManifestJsonOptions) ?? new MapManifest();
	}

	public static void SaveManifest(string pathOrDirectory, MapManifest manifest)
	{
		if (manifest == null) return;
		string targetPath = ResolvePath(pathOrDirectory, "manifest.json");
		string? dir = Path.GetDirectoryName(targetPath);
		if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
		{
			Directory.CreateDirectory(dir);
		}

		string json = SaveManifestToJson(manifest);
		WriteAllTextAtomically(targetPath, json);
	}

	public static string SaveManifestToJson(MapManifest manifest)
	{
		return JsonSerializer.Serialize(manifest, ManifestJsonOptions);
	}

	public static MapMetadata LoadMetadata(string? pathOrDirectory)
	{
		string targetPath = ResolvePath(pathOrDirectory, "metadata.json");
		if (!File.Exists(targetPath))
		{
			return new MapMetadata();
		}

		string json = File.ReadAllText(targetPath);
		return DeserializeOrExplain<MapMetadata>(json, MetadataJsonOptions, targetPath) ?? new MapMetadata();
	}

	public static MapMetadata LoadMetadataFromJson(string json)
	{
		if (string.IsNullOrWhiteSpace(json))
		{
			return new MapMetadata();
		}

		return JsonSerializer.Deserialize<MapMetadata>(json, MetadataJsonOptions) ?? new MapMetadata();
	}

	public static void SaveMetadata(string pathOrDirectory, MapMetadata metadata)
	{
		if (metadata == null) return;
		string targetPath = ResolvePath(pathOrDirectory, "metadata.json");
		string? dir = Path.GetDirectoryName(targetPath);
		if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
		{
			Directory.CreateDirectory(dir);
		}

		string json = SaveMetadataToJson(metadata);
		WriteAllTextAtomically(targetPath, json);
	}

	public static string SaveMetadataToJson(MapMetadata metadata)
	{
		return JsonSerializer.Serialize(metadata, MetadataJsonOptions);
	}

	public static MapSaveData LoadTerrain(string pathOrDirectory)
	{
		string targetPath = ResolvePath(pathOrDirectory, "terrain.json");
		if (!File.Exists(targetPath))
		{
			return new MapSaveData();
		}

		string json = File.ReadAllText(targetPath);
		return DeserializeOrExplain<MapSaveData>(json, TerrainJsonOptions, targetPath) ?? new MapSaveData();
	}

	public static MapSaveData LoadTerrainFromJson(string json)
	{
		if (string.IsNullOrWhiteSpace(json))
		{
			return new MapSaveData();
		}

		return JsonSerializer.Deserialize<MapSaveData>(json, TerrainJsonOptions) ?? new MapSaveData();
	}

	public static void SaveTerrain(string pathOrDirectory, MapSaveData terrain)
	{
		if (terrain == null) return;
		string targetPath = ResolvePath(pathOrDirectory, "terrain.json");
		string? dir = Path.GetDirectoryName(targetPath);
		if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
		{
			Directory.CreateDirectory(dir);
		}

		string json = SaveTerrainToJson(terrain);
		WriteAllTextAtomically(targetPath, json);
	}

	public static string SaveTerrainToJson(MapSaveData terrain)
	{
		return JsonSerializer.Serialize(terrain, TerrainJsonOptions);
	}

	/// <summary>
	///     Reads a map document and, when its content is no longer something this build can hold, says which file, which
	///     member and which path in that file is at fault. A map whose format this build does not read is a map authored
	///     for another build: it fails rather than loading as an empty document, and it fails with a sentence an author can
	///     act on rather than with the serializer's own account of a .NET type.
	/// </summary>
	/// <param name="json">The document text.</param>
	/// <param name="options">The options the document is read with.</param>
	/// <param name="targetPath">The file the text came from.</param>
	/// <returns>The deserialized document, or null when the document is empty.</returns>
	private static T? DeserializeOrExplain<T>(string json, JsonSerializerOptions options, string targetPath) where T : class
	{
		try
		{
			return JsonSerializer.Deserialize<T>(json, options);
		}
		catch (JsonException exception)
		{
			throw new JsonException(ExplainFailure(targetPath, exception), exception.Path, exception.LineNumber, exception.BytePositionInLine, exception);
		}
	}

	private static string ExplainFailure(string targetPath, JsonException exception)
	{
		string location = string.IsNullOrEmpty(exception.Path) ? "" : $" at {exception.Path}";
		string line = exception.LineNumber is long lineNumber ? $" (line {lineNumber + 1})" : "";
		return $"{Path.GetFileName(targetPath)} in '{targetPath}' cannot be read by this build{location}{line}: {exception.Message} " +
			"A map states exactly the members its build defines, so a member this build does not know means the map was authored for another build; " +
			"open it in the map editor so it is upgraded before it is played.";
	}

	public static void WriteAllTextAtomically(string targetPath, string contents)
	{
		AtomicFileWriter.WriteAllText(targetPath, contents);
		NotifyFileWritten(targetPath);
	}

	public static void NotifyFileWritten(string targetPath)
	{
		FileWritten?.Invoke(targetPath);
	}
}