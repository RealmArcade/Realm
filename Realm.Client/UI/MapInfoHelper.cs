using Godot;
using Realm.Client.Network;
using Realm.Shared.Services;
using System;
using System.Collections.Generic;

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
		try { globalPath = ProjectSettings.GlobalizePath(basePath); } catch { }

		if (!System.IO.Directory.Exists(globalPath)) return;

		foreach (var dirPath in System.IO.Directory.GetDirectories(globalPath))
		{
			string dirName = System.IO.Path.GetFileName(dirPath);
			if (!IsValidDirName(dirName)) continue;
			if (!TryLoadMapFromFolder(dirName, basePath, out var mapDetails)) continue;
			ProcessMapDetails(mapDetails, maps, seen);
		}

		foreach (var filePath in System.IO.Directory.GetFiles(globalPath, "*.json"))
		{
			string fileName = System.IO.Path.GetFileName(filePath);
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
			ThumbnailPath = FindThumbnailForMap(mapFolder)
		};
	}

	private static string ExtractMapNameFromFileName(string name)
	{
		if (name.EndsWith("_manifest.json", StringComparison.OrdinalIgnoreCase)) return name.Substring(0, name.Length - "_manifest.json".Length);
		if (name.EndsWith(".manifest.json", StringComparison.OrdinalIgnoreCase)) return name.Substring(0, name.Length - ".manifest.json".Length);
		if (name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) return name.Substring(0, name.Length - ".json".Length);
		return string.Empty;
	}

	private static string TryGetManifestHash(string filePath)
	{
		try
		{
			if (System.IO.File.Exists(filePath))
			{
				return Realm.Shared.Metadata.RealmMetadataHelper.ComputeBlake3(System.IO.File.ReadAllBytes(filePath), ".json");
			}
		}
		catch { }
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
		if (!System.IO.File.Exists(filePath) && !FileAccess.FileExists(filePath)) return false;

		try
		{
			MapManifest manifest = MapFileService.LoadManifest(filePath);
			MapMetadata metadata = MapFileService.LoadMetadata(filePath);

			string mapName = GetMapName(manifest, metadata, fileName);
			if (string.IsNullOrWhiteSpace(mapName)) return false;

			string displayName = GetMapDisplayName(mapName, metadata);
			string description = GetMapDescription(manifest, metadata);
			string version = GetMapVersion(manifest, metadata);
			
			string gameBuildNumber = Realm.Shared.RealmVersion.GameBuildNumber;
			if (!string.IsNullOrEmpty(metadata.GameBuildNumber)) gameBuildNumber = metadata.GameBuildNumber.Trim();

			string manifestHash = TryGetManifestHash(filePath);

			string? versionDir = System.IO.Path.GetDirectoryName(filePath);
			string thumbPath = FindThumbnailForMap(mapName, version);
			
			if (!string.IsNullOrEmpty(versionDir))
			{
				string localThumb = System.IO.Path.Combine(versionDir, "thumbnail.png");
				if (System.IO.File.Exists(localThumb)) thumbPath = localThumb;
			}

			details = new MapBriefingDetails
			{
				PathName = mapName,
				DisplayName = displayName,
				Description = description,
				GameBuildNumber = gameBuildNumber,
				Version = version,
				ManifestHash = manifestHash,
				ThumbnailPath = thumbPath
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
		try { globalFolderPath = ProjectSettings.GlobalizePath(mapFolderPath); } catch { }

		if (!System.IO.Directory.Exists(globalFolderPath)) return;

		foreach (var subDirPath in System.IO.Directory.GetDirectories(globalFolderPath))
		{
			string subDirName = System.IO.Path.GetFileName(subDirPath);
			if (subDirName.StartsWith(".")) continue;

			candidatePaths.Add(System.IO.Path.Combine(subDirPath, "manifest.json"));
			candidatePaths.Add(System.IO.Path.Combine(subDirPath, "metadata.json"));

			foreach (var grandChild in System.IO.Directory.GetDirectories(subDirPath))
			{
				candidatePaths.Add(System.IO.Path.Combine(grandChild, "manifest.json"));
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

	private static bool TryParseMapJson(string mapFolder, string mapFolderPath, string path, string globalPath, out MapBriefingDetails result)
	{
		result = default;
		try
		{
			MapManifest manifest = MapFileService.LoadManifest(globalPath);
			MapMetadata metadata = MapFileService.LoadMetadata(globalPath);

			string displayName = ResolveDisplayName(mapFolder, manifest, metadata);
			string description = GetMapDescription(manifest, metadata);
			string version = GetMapVersion(manifest, metadata);
			
			string gameBuildNumber = path.EndsWith("manifest.json", StringComparison.OrdinalIgnoreCase)
				? Realm.Shared.RealmVersion.GameBuildNumber
				: (!string.IsNullOrEmpty(metadata.GameBuildNumber) ? metadata.GameBuildNumber : "v0.0.0");

			string manifestHash = TryGetManifestHash(globalPath);

			string thumbPath = FindThumbnailForMap(mapFolder, version);
			string localThumb = System.IO.Path.Combine(mapFolderPath, "thumbnail.png");
			if (System.IO.File.Exists(localThumb)) thumbPath = localThumb;

			result = new MapBriefingDetails
			{
				PathName = mapFolder,
				DisplayName = displayName,
				Description = description,
				GameBuildNumber = gameBuildNumber,
				Version = version,
				ManifestHash = manifestHash,
				ThumbnailPath = thumbPath
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
			try { globalPath = ProjectSettings.GlobalizePath(path); } catch { }

			if (!System.IO.File.Exists(globalPath) && !FileAccess.FileExists(path)) continue;

			if (TryParseMapJson(mapFolder, mapFolderPath, path, globalPath, out details))
			{
				return true;
			}
		}

		return false;
	}

	private static string FormatMapDisplayName(string rawName)
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
			string normHash = Realm.Shared.Distribution.ContentAddressableStorage.NormalizeBlake3Hash(manifestBlake3);
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
		string hashChildManifest = System.IO.Path.Combine(hashChildDir, "manifest.json");
		if (!System.IO.File.Exists(hashChildManifest)) return;

		string ver = ExtractVersionFromJson(hashChildManifest);
		string childHash = System.IO.Path.GetFileName(hashChildDir);
		if (string.IsNullOrWhiteSpace(childHash) || childHash.Length < 4)
		{
			try { childHash = Realm.Shared.Metadata.RealmMetadataHelper.ComputeBlake3(System.IO.File.ReadAllBytes(hashChildManifest), ".json"); } catch { }
		}
		TryAddVersion(!string.IsNullOrWhiteSpace(ver) ? ver : subDirName, childHash, versions, seen);
	}

	private static void ProcessSubDir(string subDirPath, string subDirName, List<string> versions, HashSet<string> seen)
	{
		string manifestPath = System.IO.Path.Combine(subDirPath, "manifest.json");
		string metadataPath = System.IO.Path.Combine(subDirPath, "metadata.json");

		string? targetJson = System.IO.File.Exists(manifestPath) ? manifestPath : (System.IO.File.Exists(metadataPath) ? metadataPath : null);

		if (targetJson != null)
		{
			string ver = ExtractVersionFromJson(targetJson);
			string manifestHash = "";
			if (targetJson == manifestPath)
			{
				try { manifestHash = Realm.Shared.Metadata.RealmMetadataHelper.ComputeBlake3(System.IO.File.ReadAllBytes(manifestPath), ".json"); } catch { }
			}

			if (!string.IsNullOrWhiteSpace(ver)) TryAddVersion(ver, manifestHash, versions, seen);
			else if (char.IsDigit(subDirName[0]) || subDirName.StartsWith("v", StringComparison.OrdinalIgnoreCase)) TryAddVersion(subDirName, manifestHash, versions, seen);
		}

		foreach (var hashChildDir in System.IO.Directory.GetDirectories(subDirPath))
		{
			ProcessHashChildDir(hashChildDir, subDirName, versions, seen);
		}
	}

	private static void ScanFolderForVersions(string dirPath, List<string> versions, HashSet<string> seen)
	{
		if (string.IsNullOrWhiteSpace(dirPath)) return;

		string globalPath = dirPath;
		try { globalPath = ProjectSettings.GlobalizePath(dirPath); } catch { }

		if (!System.IO.Directory.Exists(globalPath)) return;

		foreach (var subDirPath in System.IO.Directory.GetDirectories(globalPath))
		{
			string subDirName = System.IO.Path.GetFileName(subDirPath);
			if (!subDirName.StartsWith(".")) ProcessSubDir(subDirPath, subDirName, versions, seen);
		}

		ProcessRootJson(globalPath, versions, seen);
	}

	private static void ProcessRootJson(string globalPath, List<string> versions, HashSet<string> seen)
	{
		string rootManifest = System.IO.Path.Combine(globalPath, "manifest.json");
		string rootMetadata = System.IO.Path.Combine(globalPath, "metadata.json");
		string? rootJson = GetTargetJsonFile(rootManifest, rootMetadata);

		if (rootJson == null) return;

		string ver = ExtractVersionFromJson(rootJson);
		if (string.IsNullOrWhiteSpace(ver)) return;

		string manifestHash = "";
		if (rootJson == rootManifest)
		{
			try { manifestHash = Realm.Shared.Metadata.RealmMetadataHelper.ComputeBlake3(System.IO.File.ReadAllBytes(rootManifest), ".json"); } catch { }
		}
		
		TryAddVersion(ver, manifestHash, versions, seen);
	}

	private static string? GetTargetJsonFile(string manifestPath, string metadataPath)
	{
		if (System.IO.File.Exists(manifestPath)) return manifestPath;
		if (System.IO.File.Exists(metadataPath)) return metadataPath;
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
			string manifestPath = System.IO.Path.Combine(folder, "manifest.json");
			if (System.IO.File.Exists(manifestPath))
			{
				string ver = ExtractVersionFromJson(manifestPath);
				string manifestHash = "";
				try { manifestHash = Realm.Shared.Metadata.RealmMetadataHelper.ComputeBlake3(System.IO.File.ReadAllBytes(manifestPath), ".json"); } catch { }
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
		string fileName = System.IO.Path.GetFileName(file);
		if (fileName.StartsWith(variation + "_", StringComparison.OrdinalIgnoreCase))
		{
			string ver = ExtractVersionFromJson(file);
			if (!string.IsNullOrWhiteSpace(ver)) TryAddVersion(ver, null, versions, seen);
		}
	}

	private static void SearchRoot(string root, HashSet<string> variations, List<string> versions, HashSet<string> seen)
	{
		string globalRoot = root;
		try { globalRoot = ProjectSettings.GlobalizePath(root); } catch { }

		if (!System.IO.Directory.Exists(globalRoot)) return;

		foreach (var variation in variations)
		{
			ScanFolderForVersions(System.IO.Path.Combine(globalRoot, variation), versions, seen);
		}

		foreach (var folder in System.IO.Directory.GetDirectories(globalRoot))
		{
			string folderName = System.IO.Path.GetFileName(folder);
			foreach (var variation in variations)
			{
				ProcessVariationFolder(folder, folderName, variation, versions, seen);
			}
		}

		foreach (var file in System.IO.Directory.GetFiles(globalRoot, "*.json"))
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
			if (System.IO.File.Exists(jsonFilePath))
			{
				var manifest = MapFileService.LoadManifest(jsonFilePath);
				if (!string.IsNullOrEmpty(manifest.Version)) return manifest.Version.Trim();

				var metadata = MapFileService.LoadMetadata(jsonFilePath);
				if (!string.IsNullOrEmpty(metadata.MapProperties?.Version)) return metadata.MapProperties.Version.Trim();
			}
		}
		catch { }
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

	private static string SearchSubdirsForThumb(string basePath)
	{
		string thumb = System.IO.Path.Combine(basePath, "thumbnail.png");
		if (System.IO.File.Exists(thumb)) return thumb;

		foreach (var sub in System.IO.Directory.GetDirectories(basePath))
		{
			string subThumb = System.IO.Path.Combine(sub, "thumbnail.png");
			if (System.IO.File.Exists(subThumb)) return subThumb;
		}
		return string.Empty;
	}

	private static string TryArchiveVersionSearch(string globalArchive, string mapFolderOrName, string? version)
	{
		if (string.IsNullOrEmpty(version)) return string.Empty;
		
		string candidateVersionDir = System.IO.Path.Combine(globalArchive, mapFolderOrName, version);
		if (!System.IO.Directory.Exists(candidateVersionDir)) return string.Empty;
		
		return SearchSubdirsForThumb(candidateVersionDir);
	}

	private static string TryArchiveMapDirSearch(string globalArchive, string mapFolderOrName)
	{
		string candidateMapDir = System.IO.Path.Combine(globalArchive, mapFolderOrName);
		if (!System.IO.Directory.Exists(candidateMapDir)) return string.Empty;
		
		string thumb = System.IO.Path.Combine(candidateMapDir, "thumbnail.png");
		if (System.IO.File.Exists(thumb)) return thumb;

		foreach (var verDir in System.IO.Directory.GetDirectories(candidateMapDir))
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
			string resMap = ProjectSettings.GlobalizePath($"res://Maps/{mapFolderOrName}/thumbnail.png");
			if (System.IO.File.Exists(resMap)) return resMap;
		}
		catch { }

		try
		{
			string userMap = ProjectSettings.GlobalizePath($"user://maps/{mapFolderOrName}/thumbnail.png");
			if (System.IO.File.Exists(userMap)) return userMap;
		}
		catch { }

		return string.Empty;
	}

	public static string FindThumbnailForMap(string mapFolderOrName, string? version = null)
	{
		if (string.IsNullOrWhiteSpace(mapFolderOrName)) return string.Empty;

		string globalArchive = MapAssetManager.GlobalArchiveDirectory;
		if (System.IO.Directory.Exists(globalArchive))
		{
			string archiveVersionFound = TryArchiveVersionSearch(globalArchive, mapFolderOrName, version);
			if (!string.IsNullOrEmpty(archiveVersionFound)) return archiveVersionFound;

			string archiveMapDirFound = TryArchiveMapDirSearch(globalArchive, mapFolderOrName);
			if (!string.IsNullOrEmpty(archiveMapDirFound)) return archiveMapDirFound;
		}

		string projectSettingsFound = CheckProjectSettingsPaths(mapFolderOrName);
		if (!string.IsNullOrEmpty(projectSettingsFound)) return projectSettingsFound;

		if (System.IO.Directory.Exists(mapFolderOrName))
		{
			string directThumb = System.IO.Path.Combine(mapFolderOrName, "thumbnail.png");
			if (System.IO.File.Exists(directThumb)) return directThumb;
		}

		return string.Empty;
	}
}