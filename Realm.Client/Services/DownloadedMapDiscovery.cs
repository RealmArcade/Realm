using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Godot;
using Realm.Client.Network;
using Realm.Shared.Distribution;

namespace Realm.Client.Services;

/// <summary>
///     Reads the download archive directories and turns the manifests it finds into the map list the import dialog shows.
///     Every member here is a pure function of the filesystem, so it holds no state and can be called from any thread that
///     is willing to pay for the directory walk.
/// </summary>
internal static class DownloadedMapDiscovery
{
	internal static IReadOnlyList<DownloadedMapInfo> Collect(bool includeBundledMaps)
	{
		var mapDictionary = new Dictionary<string, DownloadedMapInfo>(StringComparer.OrdinalIgnoreCase);
		var searchRoots = new List<string>();

		string globalArchive = MapAssetManager.GlobalArchiveDirectory;
		if (Directory.Exists(globalArchive))
		{
			searchRoots.Add(globalArchive);
		}

		if (includeBundledMaps)
		{
			try
			{
				string resMaps = ProjectSettings.GlobalizePath("res://Maps");
				if (Directory.Exists(resMaps) && !searchRoots.Contains(resMaps, StringComparer.OrdinalIgnoreCase))
				{
					searchRoots.Add(resMaps);
				}
			}
			catch
			{
			}
		}

		foreach (string root in searchRoots)
		{
			if (!Directory.Exists(root)) continue;

			ProcessTopLevelFiles(root, mapDictionary);
			ProcessMapFolders(root, mapDictionary);
		}

		var result = mapDictionary.Values.Where(m => m.Versions.Count > 0).ToList();
		foreach (var map in result)
		{
			map.Versions.Sort((a, b) => CompareVersionsDescending(a.Version, b.Version));
		}

		result.Sort((a, b) => string.Compare(a.Title, b.Title, StringComparison.OrdinalIgnoreCase));
		MapAssetManager.Log($"[MapStorageService] Discovered {result.Count} downloaded map(s).");
		return result;
	}

	private static void ProcessTopLevelFiles(string root, Dictionary<string, DownloadedMapInfo> mapDictionary)
	{
		string[] topLevelFiles = Array.Empty<string>();
		try
		{
			topLevelFiles = Directory.GetFiles(root, "*.json", SearchOption.TopDirectoryOnly);
		}
		catch
		{
			return;
		}

		foreach (string filePath in topLevelFiles)
		{
			string fileName = Path.GetFileName(filePath);
			if (IsIgnoredTopLevelFile(fileName)) continue;

			TryProcessManifestFile(filePath, root, mapDictionary);
		}
	}

	private static bool IsIgnoredTopLevelFile(string fileName)
	{
		return fileName.StartsWith(".") ||
		       string.Equals(fileName, "pck_cache.json", StringComparison.OrdinalIgnoreCase) ||
		       string.Equals(fileName, "servers.json", StringComparison.OrdinalIgnoreCase) ||
		       string.Equals(fileName, "asset_index.cache", StringComparison.OrdinalIgnoreCase) ||
		       string.Equals(fileName, "settings.json", StringComparison.OrdinalIgnoreCase) ||
		       string.Equals(fileName, "editor_settings.json", StringComparison.OrdinalIgnoreCase);
	}

	private static void ProcessMapFolders(string root, Dictionary<string, DownloadedMapInfo> mapDictionary)
	{
		string[] mapFolders = Array.Empty<string>();
		try
		{
			mapFolders = Directory.GetDirectories(root);
		}
		catch
		{
			return;
		}

		foreach (string mapFolder in mapFolders)
		{
			string folderName = Path.GetFileName(mapFolder);
			if (IsIgnoredFolder(folderName)) continue;

			ProcessMapFolder(mapFolder, mapDictionary);
		}
	}

	private static bool IsIgnoredFolder(string folderName)
	{
		return folderName.StartsWith(".") ||
		       string.Equals(folderName, "assets", StringComparison.OrdinalIgnoreCase) ||
		       string.Equals(folderName, "user_p2p_cache", StringComparison.OrdinalIgnoreCase) ||
		       string.Equals(folderName, "p2p_cache", StringComparison.OrdinalIgnoreCase) ||
		       string.Equals(folderName, "bin", StringComparison.OrdinalIgnoreCase) ||
		       string.Equals(folderName, "obj", StringComparison.OrdinalIgnoreCase) ||
		       string.Equals(folderName, "temp_pck", StringComparison.OrdinalIgnoreCase);
	}

	private static void ProcessMapFolder(string mapFolder, Dictionary<string, DownloadedMapInfo> mapDictionary)
	{
		var candidateFiles = new List<string>();
		try
		{
			var foundManifests = Directory.GetFiles(mapFolder, "manifest.json", SearchOption.AllDirectories);
			candidateFiles.AddRange(foundManifests);
		}
		catch
		{
		}

		if (candidateFiles.Count == 0)
		{
			try
			{
				var foundMetadatas = Directory.GetFiles(mapFolder, "metadata.json", SearchOption.AllDirectories);
				candidateFiles.AddRange(foundMetadatas);
			}
			catch
			{
			}
		}

		foreach (string candidatePath in candidateFiles)
		{
			TryProcessManifestFile(candidatePath, mapFolder, mapDictionary);
		}
	}

	private static void TryProcessManifestFile(string filePath, string fallbackFolder, Dictionary<string, DownloadedMapInfo> mapDictionary)
	{
		try
		{
			string jsonText = File.ReadAllText(filePath);
			if (string.IsNullOrWhiteSpace(jsonText)) return;

			string fileName = Path.GetFileName(filePath);
			string versionDir = Path.GetDirectoryName(filePath) ?? fallbackFolder;
			string mapFolderName = Path.GetFileName(fallbackFolder);

			var manifest = MapManifest.LoadFromJson(jsonText);
			if (IsManifestValid(manifest) && !ValidateManifestFilesExist(manifest!, versionDir))
			{
				return;
			}

			ExtractManifestData(manifest, jsonText, fileName, mapFolderName, out string mapName, out string author, out string version, out string description, out List<string> tags, out string manifestHash);

			long versionSize = CalculateDirectorySize(versionDir);
			var versionInfo = new DownloadedMapVersionInfo
			{
				Version = version,
				ManifestHash = manifestHash,
				DirectoryPath = versionDir,
				ManifestFilePath = filePath,
				TotalSizeBytes = versionSize,
				FormattedSize = MapStorageService.FormatBytes(versionSize),
				LastModified = File.GetLastWriteTime(filePath)
			};

			UpdateMapDictionary(mapDictionary, versionDir, mapName, author, description, tags, manifestHash, version, versionInfo);
		}
		catch (Exception ex)
		{
			MapAssetManager.LogErr($"[MapStorageService] Error parsing map manifest {filePath}: {ex.Message}");
		}
	}

	private static void ExtractManifestData(MapManifest? manifest, string jsonText, string fileName, string mapFolderName, out string mapName, out string author, out string version, out string description, out List<string> tags, out string manifestHash)
	{
		tags = new List<string>();
		if (IsManifestValid(manifest))
		{
			mapName = !string.IsNullOrWhiteSpace(manifest!.MapName) ? manifest.MapName.Trim() : mapFolderName;
			author = !string.IsNullOrWhiteSpace(manifest.Author) ? manifest.Author.Trim() : "Unknown";
			version = !string.IsNullOrWhiteSpace(manifest.Version) ? manifest.Version.Trim() : "1.0.0";
			description = !string.IsNullOrWhiteSpace(manifest.Description) ? manifest.Description : string.Empty;
			if (manifest.Tags != null) tags.AddRange(manifest.Tags);
			manifestHash = MapAssetManager.ComputeManifestBlake3(manifest);
		}
		else
		{
			ExtractFallbackManifestInfo(jsonText, fileName, mapFolderName, out mapName, out author, out version, out description, out manifestHash);
		}

		if (string.IsNullOrWhiteSpace(mapName))
		{
			mapName = mapFolderName;
		}
	}

	private static bool IsManifestValid(MapManifest? manifest)
	{
		return manifest != null && (!string.IsNullOrWhiteSpace(manifest.MapName) || manifest.Assets != null || (manifest.Files != null && manifest.Files.Count > 0));
	}

	private static bool ValidateManifestFilesExist(MapManifest manifest, string versionDir)
	{
		if (manifest.Files == null || manifest.Files.Count == 0) return true;

		var missing = MapAssetManager.GetMissingHashes(manifest.Files.Values);
		if (missing.Count == 0) return true;

		bool allFilesOnDisk = true;
		foreach (var kvp in manifest.Files)
		{
			string rel = kvp.Key.StartsWith("res://", StringComparison.OrdinalIgnoreCase) ? kvp.Key.Substring(6) : kvp.Key;
			rel = rel.TrimStart('/', '\\');
			if (!File.Exists(Path.Combine(versionDir, rel)))
			{
				allFilesOnDisk = false;
				break;
			}
		}

		return allFilesOnDisk || missing.Count != manifest.Files.Count;
	}

	private static void ExtractFallbackManifestInfo(string jsonText, string fileName, string mapFolderName, out string mapName, out string author, out string version, out string description, out string manifestHash)
	{
		using var jsonDoc = JsonDocument.Parse(jsonText);
		var root = jsonDoc.RootElement;

		mapName = ExtractFallbackMapName(root, fileName, mapFolderName);
		author = ExtractFallbackAuthor(root);
		version = ExtractFallbackVersion(root);
		description = ExtractFallbackDescription(root);
		manifestHash = string.Empty;

		try
		{
			manifestHash = RealmMetadataHelper.ComputeBlake3(Encoding.UTF8.GetBytes(jsonText), ".json");
		}
		catch (Exception ex)
		{
			GameLog.LogError($"could not hash the map manifest, so change detection for this map is disabled: {ex.GetType().Name}: {ex.Message}");
		}
	}

	private static string ExtractFallbackMapName(JsonElement root, string fileName, string mapFolderName)
	{
		string mapName = TryExtractMapNameFromJson(root);
		if (!string.IsNullOrWhiteSpace(mapName))
			return mapName;

		return TryExtractMapNameFromFileName(fileName, mapFolderName);
	}

	private static string TryExtractMapNameFromJson(JsonElement root)
	{
		if (root.TryGetProperty("MapName", out var mnProp) && mnProp.ValueKind == JsonValueKind.String)
		{
			return mnProp.GetString() ?? string.Empty;
		}

		if (root.TryGetProperty("MapProperties", out var mp) && mp.TryGetProperty("MapName", out var mpn) && mpn.ValueKind == JsonValueKind.String)
		{
			return mpn.GetString() ?? string.Empty;
		}

		return string.Empty;
	}

	private static string TryExtractMapNameFromFileName(string fileName, string mapFolderName)
	{
		if (fileName.EndsWith("_manifest.json", StringComparison.OrdinalIgnoreCase))
			return fileName.Substring(0, fileName.Length - "_manifest.json".Length);

		if (fileName.EndsWith(".manifest.json", StringComparison.OrdinalIgnoreCase))
			return fileName.Substring(0, fileName.Length - ".manifest.json".Length);

		if (fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
			return fileName.Substring(0, fileName.Length - ".json".Length);

		return mapFolderName;
	}

	private static string ExtractFallbackAuthor(JsonElement root)
	{
		if (root.TryGetProperty("Author", out var aProp) && aProp.ValueKind == JsonValueKind.String)
		{
			return aProp.GetString() ?? "Unknown";
		}

		return "Unknown";
	}

	private static string ExtractFallbackVersion(JsonElement root)
	{
		if (root.TryGetProperty("Version", out var vProp) && vProp.ValueKind == JsonValueKind.String)
		{
			return vProp.GetString() ?? "1.0.0";
		}

		return "1.0.0";
	}

	private static string ExtractFallbackDescription(JsonElement root)
	{
		if (root.TryGetProperty("Description", out var dProp) && dProp.ValueKind == JsonValueKind.String)
		{
			return dProp.GetString() ?? string.Empty;
		}

		if (root.TryGetProperty("MapProperties", out var mProps) && mProps.TryGetProperty("MapDescription", out var mdProp) && mdProp.ValueKind == JsonValueKind.String)
		{
			return mdProp.GetString() ?? string.Empty;
		}

		return string.Empty;
	}

	private static void UpdateMapDictionary(Dictionary<string, DownloadedMapInfo> mapDictionary, string versionDir, string mapName, string author, string description, List<string> tags, string manifestHash, string version, DownloadedMapVersionInfo versionInfo)
	{
		if (!mapDictionary.TryGetValue(mapName, out var mapInfo))
		{
			mapInfo = new DownloadedMapInfo
			{
				Title = mapName,
				Author = author,
				Description = description,
				Tags = tags,
				ThumbnailPath = FindThumbnailPath(versionDir)
			};
			mapDictionary[mapName] = mapInfo;
		}

		if (string.IsNullOrEmpty(mapInfo.ThumbnailPath))
		{
			mapInfo.ThumbnailPath = FindThumbnailPath(versionDir);
		}

		if (!mapInfo.Versions.Any(v => string.Equals(v.Version, version, StringComparison.OrdinalIgnoreCase) &&
		                               (string.IsNullOrEmpty(manifestHash) || string.IsNullOrEmpty(v.ManifestHash) || string.Equals(v.ManifestHash, manifestHash, StringComparison.OrdinalIgnoreCase))))
		{
			mapInfo.Versions.Add(versionInfo);
		}
	}

	private static string FindThumbnailPath(string versionDirectory)
	{
		string webpPath = Path.Combine(versionDirectory, "thumbnail.webp");
		if (File.Exists(webpPath))
		{
			return webpPath;
		}

		string legacyPngPath = Path.Combine(versionDirectory, "thumbnail.png");
		if (File.Exists(legacyPngPath))
		{
			return legacyPngPath;
		}

		return string.Empty;
	}

	private static long CalculateDirectorySize(string directoryPath)
	{
		if (!Directory.Exists(directoryPath)) return 0;
		try
		{
			var dirInfo = new DirectoryInfo(directoryPath);
			long total = 0;
			foreach (var file in dirInfo.EnumerateFiles("*", SearchOption.AllDirectories))
			{
				total += file.Length;
			}

			return total;
		}
		catch
		{
			return 0;
		}
	}

	private static int CompareVersionsDescending(string v1, string v2)
	{
		bool p1 = TryParseVersion(v1, out var ver1);
		bool p2 = TryParseVersion(v2, out var ver2);

		if (p1 && p2)
		{
			return ver2.CompareTo(ver1);
		}

		return string.Compare(v2, v1, StringComparison.OrdinalIgnoreCase);
	}

	internal static bool TryParseVersion(string vStr, out Version version)
	{
		vStr = vStr.Trim().TrimStart('v', 'V');
		if (Version.TryParse(vStr, out version!))
		{
			return true;
		}

		int dotCount = vStr.Count(c => c == '.');
		if (dotCount == 0 && int.TryParse(vStr, out int major))
		{
			version = new Version(major, 0);
			return true;
		}

		if (dotCount == 1 && Version.TryParse(vStr, out version!))
		{
			version = new Version(version.Major, version.Minor);
			return true;
		}

		version = new Version(0, 0);
		return false;
	}
}
