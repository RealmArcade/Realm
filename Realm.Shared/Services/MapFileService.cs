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
	///     The folder inside a map's <c>bin</c> that holds the compiled scripts a map can run: the optimized one a match
	///     plays, and the one carrying debug information a playtest attaches a debugger to. Each build writes its own
	///     artifact, so a playtest never replaces the script a match runs.
	/// </summary>
	public const string ScriptOutputDirectoryName = "script";

	/// <summary>
	///     The marker an older workspace used to record which configuration produced the single artifact it held. Nothing
	///     writes it now that an artifact's own name states its flavor, and workspace cleanup removes one it finds.
	/// </summary>
	public const string RetiredBuildFlavorFileName = ".build-flavor";

	public const string DebugBuildFlavor = "Debug";
	public const string ReleaseBuildFlavor = "Release";

	/// <summary>
	///     The part a script's file name gains when it carries debug information, so the two artifacts of one map sit
	///     beside each other and neither is mistaken for the other: <c>MapScript.wasm</c> is what a match runs and
	///     <c>MapScript.debug.wasm</c> is what a debugger steps.
	/// </summary>
	public const string DebugScriptNameInfix = ".debug";

	public static Action<string>? FileWritten { get; set; }

	public static string ResolveScriptOutputDirectory(string mapFolder) => Path.Combine(mapFolder, "bin", ScriptOutputDirectoryName);

	/// <summary>
	///     The folder a build writes into before its artifact is published, one per configuration, so a compile of one
	///     flavor never disturbs the artifact of the other while it runs.
	/// </summary>
	public const string ScriptStagingDirectoryNamePrefix = ".build-";

	public static string ResolveScriptStagingDirectory(string mapFolder, bool isDebug) => Path.Combine(mapFolder, "bin", ScriptStagingDirectoryNamePrefix + (isDebug ? DebugBuildFlavor : ReleaseBuildFlavor).ToLowerInvariant());

	/// <summary>
	///     States the file name an artifact built in one configuration is published as.
	/// </summary>
	/// <param name="scriptFileName">The name the compiler produced, such as <c>MapScript.wasm</c>.</param>
	/// <param name="isDebug">Whether the artifact was built with debug information in it.</param>
	/// <returns>The name the artifact is published as.</returns>
	public static string ScriptFileNameForFlavor(string scriptFileName, bool isDebug)
	{
		return isDebug ? Path.GetFileNameWithoutExtension(scriptFileName) + DebugScriptNameInfix + ".wasm" : scriptFileName;
	}

	/// <summary>
	///     Reports whether a file name belongs to an artifact built with debug information in it.
	/// </summary>
	/// <param name="scriptFileName">The file name to read.</param>
	/// <returns>Whether the artifact is the one a debugger steps.</returns>
	public static bool IsDebugScriptFileName(string scriptFileName)
	{
		return scriptFileName.EndsWith(DebugScriptNameInfix + ".wasm", StringComparison.OrdinalIgnoreCase);
	}

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

	/// <summary>
	///     Writes a metadata document, dropping any authorship signature it carried. A save rewrites the bytes the
	///     signature covers, so it can no longer prove anything about the file; <see cref="SaveSignedMetadata"/> is the
	///     only write that puts one back.
	/// </summary>
	public static void SaveMetadata(string pathOrDirectory, MapMetadata metadata)
	{
		if (metadata == null) return;
		metadata.Signature = null;
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

	/// <summary>
	///     The exact text an authorship signature covers: the document as this build serializes it, with no signature
	///     member in it. Signing hashes this text and the signed file is written from <see cref="SaveSignedMetadata"/>.
	/// </summary>
	public static string SaveUnsignedMetadataToJson(MapMetadata metadata)
	{
		if (metadata == null) return string.Empty;
		string? signature = metadata.Signature;
		metadata.Signature = null;
		try
		{
			return SaveMetadataToJson(metadata);
		}
		finally
		{
			metadata.Signature = signature;
		}
	}

	/// <summary>
	///     Writes a metadata document together with the signature computed over its unsigned text, which is the only way a
	///     signature reaches a file. The caller signs <see cref="SaveUnsignedMetadataToJson"/> for the same document.
	/// </summary>
	public static void SaveSignedMetadata(string pathOrDirectory, MapMetadata metadata, string signatureBase64)
	{
		if (metadata == null || string.IsNullOrWhiteSpace(signatureBase64)) return;
		metadata.Signature = signatureBase64;
		string targetPath = ResolvePath(pathOrDirectory, "metadata.json");
		string? dir = Path.GetDirectoryName(targetPath);
		if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
		{
			Directory.CreateDirectory(dir);
		}

		WriteAllTextAtomically(targetPath, SaveMetadataToJson(metadata));
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