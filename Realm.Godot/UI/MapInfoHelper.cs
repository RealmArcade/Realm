using Godot;
using System;
using System.Collections.Generic;
using System.Text.Json;

public static class MapInfoHelper
{
	public static List<MapBriefingDetails> GetAvailableMaps()
	{
		var maps = new List<MapBriefingDetails>();
		var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		void ScanDir(string basePath)
		{
			using var dir = DirAccess.Open(basePath);
			if (dir != null)
			{
				dir.ListDirBegin();
				string itemName = dir.GetNext();
				while (itemName != "")
				{
					if (!itemName.StartsWith("."))
					{
						if (dir.CurrentIsDir())
						{
							if (!string.Equals(itemName, "assets", StringComparison.OrdinalIgnoreCase) &&
							    !string.Equals(itemName, "temp_pck", StringComparison.OrdinalIgnoreCase) &&
							    !string.Equals(itemName, "bin", StringComparison.OrdinalIgnoreCase) &&
							    !string.Equals(itemName, "obj", StringComparison.OrdinalIgnoreCase))
							{
								if (TryLoadMapFromFolder(itemName, basePath, out var mapDetails) && seen.Add(mapDetails.PathName))
								{
									maps.Add(mapDetails);
								}
							}
						}
						else if (itemName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
						{
							if (!string.Equals(itemName, "pck_cache.json", StringComparison.OrdinalIgnoreCase) &&
							    !string.Equals(itemName, "servers.json", StringComparison.OrdinalIgnoreCase))
							{
								if (TryLoadMapFromManifestFile($"{basePath}/{itemName}", itemName, out var mapDetails) && seen.Add(mapDetails.PathName))
								{
									maps.Add(mapDetails);
								}
							}
						}
					}
					itemName = dir.GetNext();
				}
				dir.ListDirEnd();
			}
			else
			{
				string globalPath = basePath;
				try
				{
					globalPath = ProjectSettings.GlobalizePath(basePath);
				}
				catch
				{
				}

				if (System.IO.Directory.Exists(globalPath))
				{
					foreach (var dirPath in System.IO.Directory.GetDirectories(globalPath))
					{
						string dirName = System.IO.Path.GetFileName(dirPath);
						if (!dirName.StartsWith(".") &&
						    !string.Equals(dirName, "assets", StringComparison.OrdinalIgnoreCase) &&
						    !string.Equals(dirName, "temp_pck", StringComparison.OrdinalIgnoreCase) &&
						    !string.Equals(dirName, "bin", StringComparison.OrdinalIgnoreCase) &&
						    !string.Equals(dirName, "obj", StringComparison.OrdinalIgnoreCase))
						{
							if (TryLoadMapFromFolder(dirName, basePath, out var mapDetails) && seen.Add(mapDetails.PathName))
							{
								maps.Add(mapDetails);
							}
						}
					}

					foreach (var filePath in System.IO.Directory.GetFiles(globalPath, "*.json"))
					{
						string fileName = System.IO.Path.GetFileName(filePath);
						if (!fileName.StartsWith(".") &&
						    !string.Equals(fileName, "pck_cache.json", StringComparison.OrdinalIgnoreCase) &&
						    !string.Equals(fileName, "servers.json", StringComparison.OrdinalIgnoreCase))
						{
							if (TryLoadMapFromManifestFile(filePath, fileName, out var mapDetails) && seen.Add(mapDetails.PathName))
							{
								maps.Add(mapDetails);
							}
						}
					}
				}
			}
		}

		ScanDir("res://Maps");
		ScanDir("user://maps");
		
		maps.Sort((a, b) => string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase));
		return maps;
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

	private static bool TryLoadMapFromManifestFile(string filePath, string fileName, out MapBriefingDetails details)
	{
		details = default;
		string jsonText = "";

		if (FileAccess.FileExists(filePath))
		{
			using var file = FileAccess.Open(filePath, FileAccess.ModeFlags.Read);
			if (file != null)
			{
				jsonText = file.GetAsText();
			}
		}
		else if (System.IO.File.Exists(filePath))
		{
			try
			{
				jsonText = System.IO.File.ReadAllText(filePath);
			}
			catch
			{
			}
		}

		if (string.IsNullOrWhiteSpace(jsonText))
		{
			return false;
		}

		try
		{
			using var jsonDoc = JsonDocument.Parse(jsonText);
			var root = jsonDoc.RootElement;

			string mapName = string.Empty;
			if (root.TryGetProperty("MapName", out var mapNameProp) && mapNameProp.ValueKind == JsonValueKind.String)
			{
				mapName = mapNameProp.GetString() ?? string.Empty;
			}

			if (string.IsNullOrWhiteSpace(mapName) && root.TryGetProperty("MapProperties", out var mapProps) && mapProps.TryGetProperty("MapName", out var mpNameProp) && mpNameProp.ValueKind == JsonValueKind.String)
			{
				mapName = mpNameProp.GetString() ?? string.Empty;
			}

			if (string.IsNullOrWhiteSpace(mapName))
			{
				if (fileName.EndsWith("_manifest.json", StringComparison.OrdinalIgnoreCase))
				{
					mapName = fileName.Substring(0, fileName.Length - "_manifest.json".Length);
				}
				else if (fileName.EndsWith(".manifest.json", StringComparison.OrdinalIgnoreCase))
				{
					mapName = fileName.Substring(0, fileName.Length - ".manifest.json".Length);
				}
				else if (fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
				{
					mapName = fileName.Substring(0, fileName.Length - ".json".Length);
				}
			}

			if (string.IsNullOrWhiteSpace(mapName))
			{
				return false;
			}

			bool isLikelyManifest = root.TryGetProperty("Assets", out _) ||
			                        root.TryGetProperty("Files", out _) ||
			                        root.TryGetProperty("Author", out _) ||
			                        root.TryGetProperty("MapProperties", out _) ||
			                        root.TryGetProperty("CustomUnits", out _) ||
			                        root.TryGetProperty("MapName", out _);

			if (!isLikelyManifest)
			{
				return false;
			}

			string displayName = FormatMapDisplayName(mapName);
			if (root.TryGetProperty("MapProperties", out var displayMapProps) && displayMapProps.TryGetProperty("MapName", out var explicitNameProp) && explicitNameProp.ValueKind == JsonValueKind.String)
			{
				string? explicitName = explicitNameProp.GetString();
				if (!string.IsNullOrWhiteSpace(explicitName))
				{
					displayName = explicitName;
				}
			}

			string description = string.Empty;
			if (root.TryGetProperty("Description", out var descProp) && descProp.ValueKind == JsonValueKind.String)
			{
				description = descProp.GetString() ?? string.Empty;
			}
			else if (root.TryGetProperty("MapProperties", out var descMapProps) && descMapProps.TryGetProperty("MapDescription", out var explicitDescProp) && explicitDescProp.ValueKind == JsonValueKind.String)
			{
				description = explicitDescProp.GetString() ?? string.Empty;
			}

			string gameBuildNumber = Realm.Shared.RealmVersion.GameBuildNumber;
			if (root.TryGetProperty("GameBuildNumber", out var gbnProp) && gbnProp.ValueKind == JsonValueKind.String)
			{
				string? gbn = gbnProp.GetString();
				if (!string.IsNullOrWhiteSpace(gbn))
				{
					gameBuildNumber = gbn.Trim();
				}
			}

			string version = "1.0.0";
			if (root.TryGetProperty("Version", out var verProp) && verProp.ValueKind == JsonValueKind.String)
			{
				string? v = verProp.GetString();
				if (!string.IsNullOrWhiteSpace(v)) version = v.Trim();
			}

			string manifestHash = "";
			if (!string.IsNullOrWhiteSpace(jsonText))
			{
				try
				{
					manifestHash = Realm.Shared.Metadata.RealmMetadataHelper.ComputeBlake3(System.Text.Encoding.UTF8.GetBytes(jsonText), ".json");
				}
				catch { }
			}

			string? versionDir = System.IO.Path.GetDirectoryName(filePath);
			string thumbPath = !string.IsNullOrEmpty(versionDir) && System.IO.File.Exists(System.IO.Path.Combine(versionDir, "thumbnail.png"))
				? System.IO.Path.Combine(versionDir, "thumbnail.png")
				: FindThumbnailForMap(mapName, version);

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
		using (var subDir = DirAccess.Open(mapFolderPath))
		{
			if (subDir != null)
			{
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
		}

		string globalFolderPath = mapFolderPath;
		try
		{
			globalFolderPath = ProjectSettings.GlobalizePath(mapFolderPath);
		}
		catch { }

		if (System.IO.Directory.Exists(globalFolderPath))
		{
			foreach (var subDirPath in System.IO.Directory.GetDirectories(globalFolderPath))
			{
				string subDirName = System.IO.Path.GetFileName(subDirPath);
				if (!subDirName.StartsWith("."))
				{
					candidatePaths.Add(System.IO.Path.Combine(subDirPath, "manifest.json"));
					candidatePaths.Add(System.IO.Path.Combine(subDirPath, "metadata.json"));

					foreach (var grandChild in System.IO.Directory.GetDirectories(subDirPath))
					{
						candidatePaths.Add(System.IO.Path.Combine(grandChild, "manifest.json"));
					}
				}
			}
		}

		foreach (var path in candidatePaths)
		{
			string jsonText = "";
			if (FileAccess.FileExists(path))
			{
				using var file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
				if (file != null)
				{
					jsonText = file.GetAsText();
				}
			}
			else
			{
				string globalPath = path;
				try
				{
					globalPath = ProjectSettings.GlobalizePath(path);
				}
				catch
				{
				}

				if (System.IO.File.Exists(globalPath))
				{
					try
					{
						jsonText = System.IO.File.ReadAllText(globalPath);
					}
					catch
					{
					}
				}
			}

			if (!string.IsNullOrWhiteSpace(jsonText))
			{
				try
				{
					using var jsonDoc = JsonDocument.Parse(jsonText);
					var root = jsonDoc.RootElement;

					string displayName = FormatMapDisplayName(mapFolder);
					string description = string.Empty;
					string gameBuildNumber = path.EndsWith("manifest.json", StringComparison.OrdinalIgnoreCase)
						? Realm.Shared.RealmVersion.GameBuildNumber
						: "v0.0.0";

					if (root.TryGetProperty("GameBuildNumber", out var gbnProp) && gbnProp.ValueKind == JsonValueKind.String)
					{
						string? gbn = gbnProp.GetString();
						if (!string.IsNullOrWhiteSpace(gbn))
						{
							gameBuildNumber = gbn.Trim();
						}
					}

					if (root.TryGetProperty("MapName", out var mapNameProp) && mapNameProp.ValueKind == JsonValueKind.String)
					{
						string? nameVal = mapNameProp.GetString();
						if (!string.IsNullOrWhiteSpace(nameVal))
						{
							displayName = FormatMapDisplayName(nameVal);
						}
					}

					if (root.TryGetProperty("Description", out var descProp) && descProp.ValueKind == JsonValueKind.String)
					{
						description = descProp.GetString() ?? string.Empty;
					}

					if (root.TryGetProperty("MapProperties", out var mapProps))
					{
						if (mapProps.TryGetProperty("MapName", out var nameProp) && nameProp.ValueKind == JsonValueKind.String)
						{
							string? nameVal = nameProp.GetString();
							if (!string.IsNullOrWhiteSpace(nameVal))
							{
								displayName = nameVal;
							}
						}
						if (mapProps.TryGetProperty("MapDescription", out var propDescProp) && propDescProp.ValueKind == JsonValueKind.String)
						{
							description = propDescProp.GetString() ?? string.Empty;
						}
					}

					string version = "1.0.0";
					if (root.TryGetProperty("Version", out var verProp) && verProp.ValueKind == JsonValueKind.String)
					{
						string? v = verProp.GetString();
						if (!string.IsNullOrWhiteSpace(v)) version = v.Trim();
					}

					string manifestHash = "";
					try
					{
						manifestHash = Realm.Shared.Metadata.RealmMetadataHelper.ComputeBlake3(System.Text.Encoding.UTF8.GetBytes(jsonText), ".json");
					}
					catch { }

					string thumbPath = "";
					if (System.IO.File.Exists(System.IO.Path.Combine(mapFolderPath, "thumbnail.png")))
					{
						thumbPath = System.IO.Path.Combine(mapFolderPath, "thumbnail.png");
					}
					else
					{
						thumbPath = FindThumbnailForMap(mapFolder, version);
					}

					details = new MapBriefingDetails
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
				}
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

	public static List<string> GetDownloadedVersionsForMap(string mapPathName, string? mapDisplayName = null)
	{
		var versions = new List<string>();
		var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		var variations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		void AddVariation(string? name)
		{
			if (string.IsNullOrWhiteSpace(name)) return;
			string trimmed = name.Trim();
			variations.Add(trimmed);
			variations.Add(trimmed.Replace(' ', '_'));
			variations.Add(trimmed.Replace('_', ' '));
		}

		AddVariation(mapPathName);
		AddVariation(mapDisplayName);

		void TryAddVersion(string? ver, string? manifestHash = null)
		{
			if (string.IsNullOrWhiteSpace(ver)) return;
			string label = FormatVersionDisplay(ver, manifestHash);
			if (seen.Add(label))
			{
				versions.Add(label);
			}
		}

		void ScanFolderForVersions(string dirPath)
		{
			if (string.IsNullOrWhiteSpace(dirPath)) return;

			string globalPath = dirPath;
			try
			{
				globalPath = ProjectSettings.GlobalizePath(dirPath);
			}
			catch { }

			if (!System.IO.Directory.Exists(globalPath)) return;

			foreach (var subDirPath in System.IO.Directory.GetDirectories(globalPath))
			{
				string subDirName = System.IO.Path.GetFileName(subDirPath);
				if (subDirName.StartsWith(".")) continue;

				string manifestPath = System.IO.Path.Combine(subDirPath, "manifest.json");
				string metadataPath = System.IO.Path.Combine(subDirPath, "metadata.json");

				string? targetJson = System.IO.File.Exists(manifestPath) ? manifestPath
					: System.IO.File.Exists(metadataPath) ? metadataPath
					: null;

				if (targetJson != null)
				{
					string ver = ExtractVersionFromJson(targetJson);
					string manifestHash = "";
					if (targetJson == manifestPath)
					{
						try { manifestHash = Realm.Shared.Metadata.RealmMetadataHelper.ComputeBlake3(System.IO.File.ReadAllBytes(manifestPath), ".json"); } catch { }
					}
					if (!string.IsNullOrWhiteSpace(ver))
					{
						TryAddVersion(ver, manifestHash);
					}
					else if (char.IsDigit(subDirName[0]) || subDirName.StartsWith("v", StringComparison.OrdinalIgnoreCase))
					{
						TryAddVersion(subDirName, manifestHash);
					}
				}

				foreach (var hashChildDir in System.IO.Directory.GetDirectories(subDirPath))
				{
					string hashChildManifest = System.IO.Path.Combine(hashChildDir, "manifest.json");
					if (System.IO.File.Exists(hashChildManifest))
					{
						string ver = ExtractVersionFromJson(hashChildManifest);
						string childHash = System.IO.Path.GetFileName(hashChildDir);
						if (string.IsNullOrWhiteSpace(childHash) || childHash.Length < 4)
						{
							try { childHash = Realm.Shared.Metadata.RealmMetadataHelper.ComputeBlake3(System.IO.File.ReadAllBytes(hashChildManifest), ".json"); } catch { }
						}
						TryAddVersion(!string.IsNullOrWhiteSpace(ver) ? ver : subDirName, childHash);
					}
				}
			}

			string rootManifest = System.IO.Path.Combine(globalPath, "manifest.json");
			string rootMetadata = System.IO.Path.Combine(globalPath, "metadata.json");
			string? rootJson = System.IO.File.Exists(rootManifest) ? rootManifest
				: System.IO.File.Exists(rootMetadata) ? rootMetadata
				: null;

			if (rootJson != null)
			{
				string ver = ExtractVersionFromJson(rootJson);
				string manifestHash = "";
				if (rootJson == rootManifest)
				{
					try { manifestHash = Realm.Shared.Metadata.RealmMetadataHelper.ComputeBlake3(System.IO.File.ReadAllBytes(rootManifest), ".json"); } catch { }
				}
				if (!string.IsNullOrWhiteSpace(ver))
				{
					TryAddVersion(ver, manifestHash);
				}
			}
		}

		string[] searchRoots = new[]
		{
			"res://Maps",
			"user://maps",
			"user://p2p_cache"
		};

		foreach (var root in searchRoots)
		{
			string globalRoot = root;
			try { globalRoot = ProjectSettings.GlobalizePath(root); } catch { }

			if (!System.IO.Directory.Exists(globalRoot)) continue;

			foreach (var variation in variations)
			{
				string directFolder = System.IO.Path.Combine(globalRoot, variation);
				ScanFolderForVersions(directFolder);
			}

			foreach (var folder in System.IO.Directory.GetDirectories(globalRoot))
			{
				string folderName = System.IO.Path.GetFileName(folder);
				foreach (var variation in variations)
				{
					if (folderName.Equals(variation, StringComparison.OrdinalIgnoreCase))
					{
						ScanFolderForVersions(folder);
					}
					else if (folderName.StartsWith(variation + "_", StringComparison.OrdinalIgnoreCase))
					{
						string suffix = folderName.Substring(variation.Length + 1);
						string manifestPath = System.IO.Path.Combine(folder, "manifest.json");
						if (System.IO.File.Exists(manifestPath))
						{
							string ver = ExtractVersionFromJson(manifestPath);
							string manifestHash = "";
							try { manifestHash = Realm.Shared.Metadata.RealmMetadataHelper.ComputeBlake3(System.IO.File.ReadAllBytes(manifestPath), ".json"); } catch { }
							TryAddVersion(!string.IsNullOrWhiteSpace(ver) ? ver : suffix, manifestHash);
						}
						else
						{
							TryAddVersion(suffix, null);
						}
					}
				}
			}

			foreach (var file in System.IO.Directory.GetFiles(globalRoot, "*.json"))
			{
				string fileName = System.IO.Path.GetFileName(file);
				foreach (var variation in variations)
				{
					if (fileName.StartsWith(variation + "_", StringComparison.OrdinalIgnoreCase))
					{
						string ver = ExtractVersionFromJson(file);
						if (!string.IsNullOrWhiteSpace(ver))
						{
							TryAddVersion(ver, null);
						}
					}
				}
			}
		}

		if (versions.Count == 0)
		{
			versions.Add(FormatVersionDisplay("1.0.0", null));
		}

		versions.Sort((a, b) => ParseVersion(b).CompareTo(ParseVersion(a)));
		return versions;
	}

	private static string ExtractVersionFromJson(string jsonFilePath)
	{
		try
		{
			if (System.IO.File.Exists(jsonFilePath))
			{
				string text = System.IO.File.ReadAllText(jsonFilePath);
				using var doc = JsonDocument.Parse(text);
				var root = doc.RootElement;
				if (root.TryGetProperty("Version", out var vProp) && vProp.ValueKind == JsonValueKind.String)
				{
					string? v = vProp.GetString();
					if (!string.IsNullOrWhiteSpace(v)) return v.Trim();
				}
				if (root.TryGetProperty("MapVersion", out var mvProp) && mvProp.ValueKind == JsonValueKind.String)
				{
					string? v = mvProp.GetString();
					if (!string.IsNullOrWhiteSpace(v)) return v.Trim();
				}
				if (root.TryGetProperty("MapProperties", out var mp) && mp.TryGetProperty("MapVersion", out var pMv) && pMv.ValueKind == JsonValueKind.String)
				{
					string? v = pMv.GetString();
					if (!string.IsNullOrWhiteSpace(v)) return v.Trim();
				}
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

	public static string FindThumbnailForMap(string mapFolderOrName, string? version = null)
	{
		if (string.IsNullOrWhiteSpace(mapFolderOrName)) return string.Empty;

		string globalArchive = MapAssetManager.GlobalArchiveDirectory;
		if (System.IO.Directory.Exists(globalArchive))
		{
			if (!string.IsNullOrEmpty(version))
			{
				string candidateVersionDir = System.IO.Path.Combine(globalArchive, mapFolderOrName, version);
				if (System.IO.Directory.Exists(candidateVersionDir))
				{
					string thumb = System.IO.Path.Combine(candidateVersionDir, "thumbnail.png");
					if (System.IO.File.Exists(thumb)) return thumb;

					foreach (var sub in System.IO.Directory.GetDirectories(candidateVersionDir))
					{
						string subThumb = System.IO.Path.Combine(sub, "thumbnail.png");
						if (System.IO.File.Exists(subThumb)) return subThumb;
					}
				}
			}

			string candidateMapDir = System.IO.Path.Combine(globalArchive, mapFolderOrName);
			if (System.IO.Directory.Exists(candidateMapDir))
			{
				string thumb = System.IO.Path.Combine(candidateMapDir, "thumbnail.png");
				if (System.IO.File.Exists(thumb)) return thumb;

				foreach (var verDir in System.IO.Directory.GetDirectories(candidateMapDir))
				{
					string verThumb = System.IO.Path.Combine(verDir, "thumbnail.png");
					if (System.IO.File.Exists(verThumb)) return verThumb;

					foreach (var sub in System.IO.Directory.GetDirectories(verDir))
					{
						string subThumb = System.IO.Path.Combine(sub, "thumbnail.png");
						if (System.IO.File.Exists(subThumb)) return subThumb;
					}
				}
			}
		}

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

		if (System.IO.Directory.Exists(mapFolderOrName))
		{
			string directThumb = System.IO.Path.Combine(mapFolderOrName, "thumbnail.png");
			if (System.IO.File.Exists(directThumb)) return directThumb;
		}

		return string.Empty;
	}
}
