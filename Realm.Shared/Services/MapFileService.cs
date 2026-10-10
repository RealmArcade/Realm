using Realm.Shared.Distribution;
using Realm.Shared.Metadata;
using Realm.Shared.Terrain;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Realm.Shared.Services;

public static class MapFileService
{
	private static readonly JsonSerializerOptions ManifestJsonOptions = new()
	{
		AllowTrailingCommas = true,
		ReadCommentHandling = JsonCommentHandling.Skip,
		WriteIndented = true,
		PropertyNameCaseInsensitive = true
	};

	private static readonly JsonSerializerOptions MetadataJsonOptions = new()
	{
		PropertyNameCaseInsensitive = true,
		IncludeFields = true,
		WriteIndented = true,
		Converters = { new JsonStringEnumConverter() }
	};

	private static readonly JsonSerializerOptions TerrainJsonOptions = new()
	{
		PropertyNameCaseInsensitive = true,
		IncludeFields = true,
		WriteIndented = true,
		Converters = { new JsonStringEnumConverter() }
	};

	public static string ResolvePath(string pathOrDirectory, string targetFileName)
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
		return LoadManifestFromJson(json);
	}

	public static MapManifest LoadManifestFromJson(string json)
	{
		if (string.IsNullOrWhiteSpace(json))
		{
			return new MapManifest();
		}

		try
		{
			return JsonSerializer.Deserialize<MapManifest>(json, ManifestJsonOptions) ?? new MapManifest();
		}
		catch
		{
			return new MapManifest();
		}
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
		File.WriteAllText(targetPath, json);
	}

	public static string SaveManifestToJson(MapManifest manifest)
	{
		return JsonSerializer.Serialize(manifest, ManifestJsonOptions);
	}

	public static MapMetadata LoadMetadata(string pathOrDirectory)
	{
		string targetPath = ResolvePath(pathOrDirectory, "metadata.json");
		if (!File.Exists(targetPath))
		{
			return new MapMetadata();
		}

		string json = File.ReadAllText(targetPath);
		return LoadMetadataFromJson(json);
	}

	public static MapMetadata LoadMetadataFromJson(string json)
	{
		if (string.IsNullOrWhiteSpace(json))
		{
			return new MapMetadata();
		}

		try
		{
			return JsonSerializer.Deserialize<MapMetadata>(json, MetadataJsonOptions) ?? new MapMetadata();
		}
		catch
		{
			return new MapMetadata();
		}
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
		File.WriteAllText(targetPath, json);
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
		return LoadTerrainFromJson(json);
	}

	public static MapSaveData LoadTerrainFromJson(string json)
	{
		if (string.IsNullOrWhiteSpace(json))
		{
			return new MapSaveData();
		}

		try
		{
			return JsonSerializer.Deserialize<MapSaveData>(json, TerrainJsonOptions) ?? new MapSaveData();
		}
		catch
		{
			return new MapSaveData();
		}
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
		File.WriteAllText(targetPath, json);
	}

	public static string SaveTerrainToJson(MapSaveData terrain)
	{
		return JsonSerializer.Serialize(terrain, TerrainJsonOptions);
	}
}
