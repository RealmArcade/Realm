using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using Realm.Client.Network;
using Realm.Shared;
using Realm.Shared.Distribution;
using Realm.Shared.Services;
using FileAccess = Godot.FileAccess;

namespace Realm.Client.UI;

public static class MapInfoHelper
{
	public static List<MapBriefingDetails> GetAvailableMaps()
	{
		var maps = new List<MapBriefingDetails>();
		var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		ScanDir("res://Maps", maps, seen);
		ScanDir("user://maps", maps, seen);

		maps.Sort((a, b) => string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase));
		return maps;
	}

	private static bool IsValidDirName(string name) =>
		!name.StartsWith(".") &&
		!string.Equals(name, "assets", StringComparison.OrdinalIgnoreCase) &&
		!string.Equals(name, "temp_pck", StringComparison.OrdinalIgnoreCase) &&
		!string.Equals(name, "bin", StringComparison.OrdinalIgnoreCase) &&
		!string.Equals(name, "obj", StringComparison.OrdinalIgnoreCase);

	private static bool IsValidJsonName(string name) =>
		!name.StartsWith(".") &&
		name.EndsWith(".json", StringComparison.OrdinalIgnoreCase) &&
		!string.Equals(name, "pck_cache.json", StringComparison.OrdinalIgnoreCase) &&
		!string.Equals(name, "servers.json", StringComparison.OrdinalIgnoreCase);

	private static void ProcessMapDetails(MapBriefingDetails details, List<MapBriefingDetails> maps, HashSet<string> seen)
	{
		if (seen.Add(details.PathName))
		{
			maps.Add(details);
		}
	}

	private static void ProcessGodotItem(string itemName, string basePath, DirAccess dir, List<MapBriefingDetails> maps, HashSet<string> seen)
	{
		if (dir.CurrentIsDir() && IsValidDirName(itemName))
		{
			if (!TryLoadMapFromFolder(itemName, basePath, out var mapDetails)) return;
			ProcessMapDetails(mapDetails, maps, seen);
			return;
		}

		if (!dir.CurrentIsDir() && IsValidJsonName(itemName))
		{
			if (!TryLoadMapFromManifestFile($"{basePath}/{itemName}", itemName, out var mapDetails)) return;
			ProcessMapDetails(mapDetails, maps, seen);
		}
	}

	private static void ScanGodotDir(string basePath, DirAccess dir, List<MapBriefingDetails> maps, HashSet<string> seen)
	{
		dir.ListDirBegin();
		string itemName = dir.GetNext();
		while (itemName != "")
		{
			ProcessGodotItem(itemName, basePath, dir, maps, seen);
			itemName = dir.GetNext();
		}

		dir.ListDirEnd();
	}

	private static void ScanSystemDir(string basePath, List<MapBriefingDetails> maps, HashSet<string> seen)
	{
		string globalPath = basePath;
		try
		{
			globalPath = ProjectSettings.GlobalizePath(basePath);
		}
		catch
		{
		}

		if (!Directory.Exists(globalPath)) return;

		foreach (var dirPath in Directory.GetDirectories(globalPath))
		{
			string dirName = Path.GetFileName(dirPath);
			if (!IsValidDirName(dirName)) continue;
			if (!TryLoadMapFromFolder(dirName, basePath, out var mapDetails)) continue;
			ProcessMapDetails(mapDetails, maps, seen);
		}

		foreach (var filePath in Directory.GetFiles(globalPath, "*.json"))
		{
			string fileName = Path.GetFileName(filePath);
			if (!IsValidJsonName(fileName)) continue;
			if (!TryLoadMapFromManifestFile(filePath, fileName, out var mapDetails)) continue;
			ProcessMapDetails(mapDetails, maps, seen);
		}
	}

	private static void ScanDir(string basePath, List<MapBriefingDetails> maps, HashSet<string> seen)
	{
		using var dir = DirAccess.Open(basePath);
		if (dir != null) ScanGodotDir(basePath, dir, maps, seen);
		else ScanSystemDir(basePath, maps, seen);
	}

	public static MapBriefingDetails LoadMapDetails(string mapFolder, string basePath = "res://Maps")
	{
		if (TryLoadMapFromFolder(mapFolder, basePath, out var details))
		{
			return details;
		}

		return new MapBriefingDetails
		{
			PathName = mapFolder,
			DisplayName = FormatMapDisplayName(mapFolder),
			Description = "",
			GameBuildNumber = "v0.0.0",
			Version = "1.0.0",
			ManifestHash = "",
			ThumbnailPath = FindThumbnailForMap(mapFolder),
			Attribution_SourceMap = new List<string>()
		};
	}

	private static string ExtractMapNameFromFileName(string name)
	{
		if (name.EndsWith("_manifest.json", StringComparison.OrdinalIgnoreCase)) return name.Substring(0, name.Length - "_manifest.json".Length);
		if (name.EndsWith(".manifest.json", StringComparison.OrdinalIgnoreCase)) return name.Substring(0, name.Length - ".manifest.json".Length);
		if (name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) return name.Substring(0, name.Length - ".json".Length);
		return string.Empty;
	}

	private static string ResolveMapJsonDirectory(string jsonPath)
	{
		if (string.IsNullOrEmpty(jsonPath)) return jsonPath;

		return jsonPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
			? Path.GetDirectoryName(jsonPath) ?? jsonPath
			: jsonPath;
	}

	private static string TryGetManifestHash(string filePath)
	{
		try
		{
			if (File.Exists(filePath))
			{
				return RealmMetadataHelper.ComputeBlake3(File.ReadAllBytes(filePath), ".json");
			}
		}
		catch
		{
		}

		return "";
	}

	private static string GetMapName(MapManifest manifest, MapMetadata metadata, string fileName)
	{
		string mapName = manifest.MapName;
		if (string.IsNullOrWhiteSpace(mapName))
		{
			if (metadata.MapProperties != null && !string.IsNullOrWhiteSpace(metadata.MapProperties.MapName))
			{
				mapName = metadata.MapProperties.MapName;
			}
			else
			{
				mapName = ExtractMapNameFromFileName(fileName);
			}
		}

		return mapName;
	}

	private static string GetMapDisplayName(string mapName, MapMetadata metadata)
	{
		string displayName = FormatMapDisplayName(mapName);
		if (metadata.MapProperties != null && !string.IsNullOrWhiteSpace(metadata.MapProperties.MapName))
		{
			displayName = metadata.MapProperties.MapName;
		}

		return displayName;
	}

	private static string GetMapDescription(MapManifest manifest, MapMetadata metadata)
	{
		if (!string.IsNullOrEmpty(manifest.Description)) return manifest.Description;
		if (metadata.MapProperties != null && !string.IsNullOrEmpty(metadata.MapProperties.MapDescription)) return metadata.MapProperties.MapDescription;
		return string.Empty;
	}

	private static string GetMapVersion(MapManifest manifest, MapMetadata metadata)
	{
		if (!string.IsNullOrEmpty(manifest.Version)) return manifest.Version.Trim();
		if (metadata.MapProperties != null && !string.IsNullOrEmpty(metadata.MapProperties.Version)) return metadata.MapProperties.Version;
		return "1.0.0";
	}

	private static bool TryLoadMapFromManifestFile(string filePath, string fileName, out MapBriefingDetails details)
	{
		details = default;
		if (!File.Exists(filePath) && !FileAccess.FileExists(filePath)) return false;

		try
		{
			MapManifest manifest = MapFileService.LoadManifest(filePath);
			MapMetadata metadata = MapFileService.LoadMetadata(ResolveMapJsonDirectory(filePath));

			string mapName = GetMapName(manifest, metadata, fileName);
			if (string.IsNullOrWhiteSpace(mapName)) return false;

			string displayName = GetMapDisplayName(mapName, metadata);
			string description = GetMapDescription(manifest, metadata);
			string version = GetMapVersion(manifest, metadata);

			string gameBuildNumber = RealmVersion.GameBuildNumber;
			if (!string.IsNullOrEmpty(metadata.GameBuildNumber)) gameBuildNumber = metadata.GameBuildNumber.Trim();

			string manifestHash = TryGetManifestHash(filePath);

			string? versionDir = Path.GetDirectoryName(filePath);
			string thumbPath = FindThumbnailForMap(mapName, version);

			if (!string.IsNullOrEmpty(versionDir))
			{
				string localThumb = ResolveThumbnailPath(versionDir);
				if (!string.IsNullOrEmpty(localThumb)) thumbPath = localThumb;
			}

			details = new MapBriefingDetails
			{
				PathName = mapName,
				DisplayName = displayName,
				Description = description,
				GameBuildNumber = gameBuildNumber,
				Version = version,
				ManifestHash = manifestHash,
				ThumbnailPath = thumbPath,
				Attribution_SourceMap = manifest.Attribution_SourceMap ?? new List<string>()
			};
			return true;
		}
		catch
		{
			return false;
		}
	}

	private static void ScanGodotSubdirs(string mapFolderPath, List<string> candidatePaths)
	{
		using var subDir = DirAccess.Open(mapFolderPath);
		if (subDir == null) return;

		subDir.ListDirBegin();
		string subItem = subDir.GetNext();
		while (subItem != "")
		{
			if (subDir.CurrentIsDir() && !subItem.StartsWith("."))
			{
				candidatePaths.Add($"{mapFolderPath}/{subItem}/manifest.json");
				candidatePaths.Add($"{mapFolderPath}/{subItem}/metadata.json");
			}

			subItem = subDir.GetNext();
		}

		subDir.ListDirEnd();
	}

	private static void ScanSystemSubdirs(string mapFolderPath, List<string> candidatePaths)
	{
		string globalFolderPath = mapFolderPath;
		try
		{
			globalFolderPath = ProjectSettings.GlobalizePath(mapFolderPath);
		}
		catch
		{
		}

		if (!Directory.Exists(globalFolderPath)) return;

		foreach (var subDirPath in Directory.GetDirectories(globalFolderPath))
		{
			string subDirName = Path.GetFileName(subDirPath);
			if (subDirName.StartsWith(".")) continue;

			candidatePaths.Add(Path.Combine(subDirPath, "manifest.json"));
			candidatePaths.Add(Path.Combine(subDirPath, "metadata.json"));

			foreach (var grandChild in Directory.GetDirectories(subDirPath))
			{
				candidatePaths.Add(Path.Combine(grandChild, "manifest.json"));
			}
		}
	}

	private static string ResolveDisplayName(string mapFolder, MapManifest manifest, MapMetadata metadata)
	{
		string displayName = FormatMapDisplayName(mapFolder);
		if (!string.IsNullOrWhiteSpace(manifest.MapName)) displayName = FormatMapDisplayName(manifest.MapName);
		if (metadata.MapProperties != null && !string.IsNullOrWhiteSpace(metadata.MapProperties.MapName))
		{
			displayName = metadata.MapProperties.MapName;
		}

		return displayName;
	}

	private static bool TryParseMapJson(string mapFolder, string mapFolderPath, string globalPath, out MapBriefingDetails result)
	{
		result = default;
		try
		{
			string mapDirectory = ResolveMapJsonDirectory(globalPath);
			MapManifest manifest = MapFileService.LoadManifest(mapDirectory);
			MapMetadata metadata = MapFileService.LoadMetadata(mapDirectory);

			string displayName = ResolveDisplayName(mapFolder, manifest, metadata);
			string description = GetMapDescription(manifest, metadata);
			string version = GetMapVersion(manifest, metadata);

			string gameBuildNumber = !string.IsNullOrEmpty(metadata.GameBuildNumber) ? metadata.GameBuildNumber : "v0.0.0";

			string manifestHash = TryGetManifestHash(globalPath);

			string thumbPath = FindThumbnailForMap(mapFolder, version);
			string localThumb = ResolveThumbnailPath(mapFolderPath);
			if (!string.IsNullOrEmpty(localThumb)) thumbPath = localThumb;

			result = new MapBriefingDetails
			{
				PathName = mapFolder,
				DisplayName = displayName,
				Description = description,
				GameBuildNumber = gameBuildNumber,
				Version = version,
				ManifestHash = manifestHash,
				ThumbnailPath = thumbPath,
				Attribution_SourceMap = manifest.Attribution_SourceMap ?? new List<string>()
			};
			return true;
		}
		catch
		{
			return false;
		}
	}

	private static bool TryLoadMapFromFolder(string mapFolder, string basePath, out MapBriefingDetails details)
	{
		details = default;
		var candidatePaths = new List<string>
		{
			$"{basePath}/{mapFolder}/manifest.json",
			$"{basePath}/{mapFolder}/metadata.json",
			$"user://maps/{mapFolder}/manifest.json",
			$"user://maps/{mapFolder}/metadata.json",
			$"res://Maps/{mapFolder}/manifest.json",
			$"res://Maps/{mapFolder}/metadata.json"
		};

		string mapFolderPath = $"{basePath}/{mapFolder}";
		ScanGodotSubdirs(mapFolderPath, candidatePaths);
		ScanSystemSubdirs(mapFolderPath, candidatePaths);

		foreach (var path in candidatePaths)
		{
			string globalPath = path;
			try
			{
				globalPath = ProjectSettings.GlobalizePath(path);
			}
			catch
			{
			}

			if (!File.Exists(globalPath) && !FileAccess.FileExists(path)) continue;

			if (TryParseMapJson(mapFolder, mapFolderPath, globalPath, out details))
			{
				return true;
			}
		}

		return false;
	}

	public static string FormatMapDisplayName(string rawName)
	{
		if (string.IsNullOrEmpty(rawName))
		{
			return "";
		}

		string formatted = rawName.Replace('_', ' ');
		string[] words = formatted.Split(' ', StringSplitOptions.RemoveEmptyEntries);
		for (int i = 0; i < words.Length; i++)
		{
			if (words[i].Equals("td", StringComparison.OrdinalIgnoreCase))
			{
				words[i] = "TD";
			}
			else if (words[i].Length > 0)
			{
				words[i] = char.ToUpper(words[i][0]) + words[i].Substring(1).ToLower();
			}
		}

		return string.Join(" ", words);
	}

	public static string FormatVersionDisplay(string? version, string? manifestBlake3 = null)
	{
		string cleanVer = !string.IsNullOrWhiteSpace(version) ? version.TrimStart('v', 'V').Trim() : "1.0.0";
		if (string.IsNullOrEmpty(cleanVer)) cleanVer = "1.0.0";

		if (!string.IsNullOrWhiteSpace(manifestBlake3))
		{
			string normHash = ContentAddressableStorage.NormalizeBlake3Hash(manifestBlake3);
			string shortHash = normHash.Length >= 4 ? normHash.Substring(0, 4) : normHash;
			if (!string.IsNullOrEmpty(shortHash))
			{
				return $"v{cleanVer} ({shortHash})";
			}
		}

		return $"v{cleanVer}";
	}

	private static void AddVariation(string? name, HashSet<string> variations)
	{
		if (string.IsNullOrWhiteSpace(name)) return;
		string trimmed = name.Trim();
		variations.Add(trimmed);
		variations.Add(trimmed.Replace(' ', '_'));
		variations.Add(trimmed.Replace('_', ' '));
	}

	private static void TryAddVersion(string? ver, string? manifestHash, List<string> versions, HashSet<string> seen)
	{
		if (string.IsNullOrWhiteSpace(ver)) return;
		string label = FormatVersionDisplay(ver, manifestHash);
		if (seen.Add(label)) versions.Add(label);
	}

	private static void ProcessHashChildDir(string hashChildDir, string subDirName, List<string> versions, HashSet<string> seen)
	{
		string hashChildManifest = Path.Combine(hashChildDir, "manifest.json");
		if (!File.Exists(hashChildManifest)) return;

		string ver = ExtractVersionFromJson(hashChildManifest);
		string childHash = Path.GetFileName(hashChildDir);
		if (string.IsNullOrWhiteSpace(childHash) || childHash.Length < 4)
		{
			try
			{
				childHash = RealmMetadataHelper.ComputeBlake3(File.ReadAllBytes(hashChildManifest), ".json");
			}
			catch
			{
			}
		}

		TryAddVersion(!string.IsNullOrWhiteSpace(ver) ? ver : subDirName, childHash, versions, seen);
	}

	private static void ProcessSubDir(string subDirPath, string subDirName, List<string> versions, HashSet<string> seen)
	{
		string manifestPath = Path.Combine(subDirPath, "manifest.json");
		string metadataPath = Path.Combine(subDirPath, "metadata.json");

		string? targetJson = File.Exists(manifestPath) ? manifestPath : (File.Exists(metadataPath) ? metadataPath : null);

		if (targetJson != null)
		{
			string ver = ExtractVersionFromJson(targetJson);
			string manifestHash = "";
			if (targetJson == manifestPath)
			{
				try
				{
					manifestHash = RealmMetadataHelper.ComputeBlake3(File.ReadAllBytes(manifestPath), ".json");
				}
				catch
				{
				}
			}

			if (!string.IsNullOrWhiteSpace(ver)) TryAddVersion(ver, manifestHash, versions, seen);
			else if (char.IsDigit(subDirName[0]) || subDirName.StartsWith("v", StringComparison.OrdinalIgnoreCase)) TryAddVersion(subDirName, manifestHash, versions, seen);
		}

		foreach (var hashChildDir in Directory.GetDirectories(subDirPath))
		{
			ProcessHashChildDir(hashChildDir, subDirName, versions, seen);
		}
	}

	private static void ScanFolderForVersions(string dirPath, List<string> versions, HashSet<string> seen)
	{
		if (string.IsNullOrWhiteSpace(dirPath)) return;

		string globalPath = dirPath;
		try
		{
			globalPath = ProjectSettings.GlobalizePath(dirPath);
		}
		catch
		{
		}

		if (!Directory.Exists(globalPath)) return;

		foreach (var subDirPath in Directory.GetDirectories(globalPath))
		{
			string subDirName = Path.GetFileName(subDirPath);
			if (!subDirName.StartsWith(".")) ProcessSubDir(subDirPath, subDirName, versions, seen);
		}

		ProcessRootJson(globalPath, versions, seen);
	}

	private static void ProcessRootJson(string globalPath, List<string> versions, HashSet<string> seen)
	{
		string rootManifest = Path.Combine(globalPath, "manifest.json");
		string rootMetadata = Path.Combine(globalPath, "metadata.json");
		string? rootJson = GetTargetJsonFile(rootManifest, rootMetadata);

		if (rootJson == null) return;

		string ver = ExtractVersionFromJson(rootJson);
		if (string.IsNullOrWhiteSpace(ver)) return;

		string manifestHash = "";
		if (rootJson == rootManifest)
		{
			try
			{
				manifestHash = RealmMetadataHelper.ComputeBlake3(File.ReadAllBytes(rootManifest), ".json");
			}
			catch
			{
			}
		}

		TryAddVersion(ver, manifestHash, versions, seen);
	}

	private static string? GetTargetJsonFile(string manifestPath, string metadataPath)
	{
		if (File.Exists(manifestPath)) return manifestPath;
		if (File.Exists(metadataPath)) return metadataPath;
		return null;
	}

	private static void ProcessVariationFolder(string folder, string folderName, string variation, List<string> versions, HashSet<string> seen)
	{
		if (folderName.Equals(variation, StringComparison.OrdinalIgnoreCase))
		{
			ScanFolderForVersions(folder, versions, seen);
			return;
		}

		if (folderName.StartsWith(variation + "_", StringComparison.OrdinalIgnoreCase))
		{
			string suffix = folderName.Substring(variation.Length + 1);
			string manifestPath = Path.Combine(folder, "manifest.json");
			if (File.Exists(manifestPath))
			{
				string ver = ExtractVersionFromJson(manifestPath);
				string manifestHash = "";
				try
				{
					manifestHash = RealmMetadataHelper.ComputeBlake3(File.ReadAllBytes(manifestPath), ".json");
				}
				catch
				{
				}

				TryAddVersion(!string.IsNullOrWhiteSpace(ver) ? ver : suffix, manifestHash, versions, seen);
			}
			else
			{
				TryAddVersion(suffix, null, versions, seen);
			}
		}
	}

	private static void ProcessJsonFile(string file, string variation, List<string> versions, HashSet<string> seen)
	{
		string fileName = Path.GetFileName(file);
		if (fileName.StartsWith(variation + "_", StringComparison.OrdinalIgnoreCase))
		{
			string ver = ExtractVersionFromJson(file);
			if (!string.IsNullOrWhiteSpace(ver)) TryAddVersion(ver, null, versions, seen);
		}
	}

	private static void SearchRoot(string root, HashSet<string> variations, List<string> versions, HashSet<string> seen)
	{
		string globalRoot = root;
		try
		{
			globalRoot = ProjectSettings.GlobalizePath(root);
		}
		catch
		{
		}

		if (!Directory.Exists(globalRoot)) return;

		foreach (var variation in variations)
		{
			ScanFolderForVersions(Path.Combine(globalRoot, variation), versions, seen);
		}

		foreach (var folder in Directory.GetDirectories(globalRoot))
		{
			string folderName = Path.GetFileName(folder);
			foreach (var variation in variations)
			{
				ProcessVariationFolder(folder, folderName, variation, versions, seen);
			}
		}

		foreach (var file in Directory.GetFiles(globalRoot, "*.json"))
		{
			foreach (var variation in variations)
			{
				ProcessJsonFile(file, variation, versions, seen);
			}
		}
	}

	private static void SearchRoots(HashSet<string> variations, List<string> versions, HashSet<string> seen)
	{
		string[] searchRoots = new[] { "res://Maps", "user://maps", "user://p2p_cache" };
		foreach (var root in searchRoots)
		{
			SearchRoot(root, variations, versions, seen);
		}
	}

	public static List<string> GetDownloadedVersionsForMap(string mapPathName, string? mapDisplayName = null)
	{
		var versions = new List<string>();
		var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		var variations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		AddVariation(mapPathName, variations);
		AddVariation(mapDisplayName, variations);

		SearchRoots(variations, versions, seen);

		if (versions.Count == 0) versions.Add(FormatVersionDisplay("1.0.0", null));

		versions.Sort((a, b) => ParseVersion(b).CompareTo(ParseVersion(a)));
		return versions;
	}

	private static string ExtractVersionFromJson(string jsonFilePath)
	{
		try
		{
			if (File.Exists(jsonFilePath))
			{
				var manifest = MapFileService.LoadManifest(jsonFilePath);
				if (!string.IsNullOrEmpty(manifest.Version)) return manifest.Version.Trim();

				var metadata = MapFileService.LoadMetadata(ResolveMapJsonDirectory(jsonFilePath));
				if (!string.IsNullOrEmpty(metadata.MapProperties?.Version)) return metadata.MapProperties.Version.Trim();
			}
		}
		catch
		{
		}

		return "";
	}

	public static Version ParseVersion(string versionStr)
	{
		if (string.IsNullOrWhiteSpace(versionStr)) return new Version(1, 0, 0);
		string cleaned = versionStr.TrimStart('v', 'V').Trim();
		int parenIdx = cleaned.IndexOf('(');
		if (parenIdx >= 0)
		{
			cleaned = cleaned.Substring(0, parenIdx).Trim();
		}

		if (Version.TryParse(cleaned, out var v))
		{
			return v;
		}

		var parts = cleaned.Split('.');
		if (parts.Length == 1 && int.TryParse(parts[0], out int major))
		{
			return new Version(major, 0, 0);
		}

		if (parts.Length == 2 && int.TryParse(parts[0], out int maj) && int.TryParse(parts[1], out int min))
		{
			return new Version(maj, min, 0);
		}

		return new Version(0, 0, 0);
	}

	private static string ResolveThumbnailPath(string directory)
	{
		string webpThumb = Path.Combine(directory, "thumbnail.webp");
		if (File.Exists(webpThumb)) return webpThumb;

		string legacyPngThumb = Path.Combine(directory, "thumbnail.png");
		if (File.Exists(legacyPngThumb)) return legacyPngThumb;

		return string.Empty;
	}

	private static string SearchSubdirsForThumb(string basePath)
	{
		string thumb = ResolveThumbnailPath(basePath);
		if (!string.IsNullOrEmpty(thumb)) return thumb;

		foreach (var sub in Directory.GetDirectories(basePath))
		{
			string subThumb = ResolveThumbnailPath(sub);
			if (!string.IsNullOrEmpty(subThumb)) return subThumb;
		}

		return string.Empty;
	}

	private static string TryArchiveVersionSearch(string globalArchive, string mapFolderOrName, string? version)
	{
		if (string.IsNullOrEmpty(version)) return string.Empty;

		string candidateVersionDir = Path.Combine(globalArchive, mapFolderOrName, version);
		if (!Directory.Exists(candidateVersionDir)) return string.Empty;

		return SearchSubdirsForThumb(candidateVersionDir);
	}

	private static string TryArchiveMapDirSearch(string globalArchive, string mapFolderOrName)
	{
		string candidateMapDir = Path.Combine(globalArchive, mapFolderOrName);
		if (!Directory.Exists(candidateMapDir)) return string.Empty;

		string thumb = ResolveThumbnailPath(candidateMapDir);
		if (!string.IsNullOrEmpty(thumb)) return thumb;

		foreach (var verDir in Directory.GetDirectories(candidateMapDir))
		{
			string found = SearchSubdirsForThumb(verDir);
			if (!string.IsNullOrEmpty(found)) return found;
		}

		return string.Empty;
	}

	private static string CheckProjectSettingsPaths(string mapFolderOrName)
	{
		try
		{
			string resMap = ResolveThumbnailPath(ProjectSettings.GlobalizePath($"res://Maps/{mapFolderOrName}"));
			if (!string.IsNullOrEmpty(resMap)) return resMap;
		}
		catch
		{
		}

		try
		{
			string userMap = ResolveThumbnailPath(ProjectSettings.GlobalizePath($"user://maps/{mapFolderOrName}"));
			if (!string.IsNullOrEmpty(userMap)) return userMap;
		}
		catch
		{
		}

		return string.Empty;
	}

	public static string FindThumbnailForMap(string mapFolderOrName, string? version = null)
	{
		if (string.IsNullOrWhiteSpace(mapFolderOrName)) return string.Empty;

		string globalArchive = MapAssetManager.GlobalArchiveDirectory;
		if (Directory.Exists(globalArchive))
		{
			string archiveVersionFound = TryArchiveVersionSearch(globalArchive, mapFolderOrName, version);
			if (!string.IsNullOrEmpty(archiveVersionFound)) return archiveVersionFound;

			string archiveMapDirFound = TryArchiveMapDirSearch(globalArchive, mapFolderOrName);
			if (!string.IsNullOrEmpty(archiveMapDirFound)) return archiveMapDirFound;
		}

		string projectSettingsFound = CheckProjectSettingsPaths(mapFolderOrName);
		if (!string.IsNullOrEmpty(projectSettingsFound)) return projectSettingsFound;

		if (Directory.Exists(mapFolderOrName))
		{
			string directThumb = ResolveThumbnailPath(mapFolderOrName);
			if (!string.IsNullOrEmpty(directThumb)) return directThumb;
		}

		return string.Empty;
	}
}