using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Realm.Godot.Services;
using Realm.Shared.Distribution;
using Realm.Shared.Metadata;
using Realm.Shared.Services;

namespace Realm.Godot.Utils;

public static class MapAssetHelper
{
	public static MapManifestAssets LoadAssets(string mapDirectory)
	{
		string targetDirectory = string.IsNullOrEmpty(mapDirectory)
			? MapWorkspaceService.GetActiveWorkspacePath()
			: mapDirectory;

		var manifest = MapFileService.LoadManifest(targetDirectory);
		var assets = manifest.Assets ?? new MapManifestAssets();

		EnsureAllAssetsHaveBlake3Hashes(assets, targetDirectory);

		return assets;
	}

	public static (bool IsValid, List<string> MissingFiles) ValidateWorkspaceAssets(string workspacePath)
	{
		var missingFiles = new List<string>();
		if (string.IsNullOrEmpty(workspacePath) || !Directory.Exists(workspacePath))
		{
			missingFiles.Add(string.IsNullOrEmpty(workspacePath) ? "Workspace path is empty" : $"Workspace directory does not exist: {workspacePath}");
			return (false, missingFiles);
		}

		string manifestPath = Path.Combine(workspacePath, "manifest.json");
		string metadataPath = Path.Combine(workspacePath, "metadata.json");

		if (File.Exists(manifestPath))
		{
			try
			{
				var manifest = MapFileService.LoadManifest(workspacePath);
				if (manifest.Assets != null)
				{
					var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
					MapManifest.FlattenAssetsInto(dict, manifest.Assets);
					foreach (var kvp in dict)
					{
						string relPath = kvp.Key.TrimStart('/', '\\');
						string fullPath = Path.Combine(workspacePath, relPath);
						if (!File.Exists(fullPath))
						{
							string fileName = Path.GetFileName(relPath);
							string? resolvedModel = FindModelOnDisk(workspacePath, null, fileName);
							if (string.IsNullOrEmpty(resolvedModel) || !File.Exists(resolvedModel))
							{
								string subFolder = relPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) && relPath.Split('/').Length > 2
									? relPath.Split('/')[1]
									: "icons";
								string? resolvedAsset = FindAssetOnDisk(workspacePath, subFolder, relPath);
								if (string.IsNullOrEmpty(resolvedAsset) || !File.Exists(resolvedAsset))
								{
									missingFiles.Add(relPath);
								}
							}
						}
					}
				}
			}
			catch (Exception ex)
			{
				GD.PrintErr($"[MapAssetHelper] ValidateWorkspaceAssets error reading manifest: {ex.Message}");
			}
		}

		if (File.Exists(metadataPath))
		{
			try
			{
				var metadata = MapFileService.LoadMetadata(workspacePath);
				if (metadata.Templates != null)
				{
					void CheckUnits(IEnumerable<UnitMetadata>? list, string subCat)
					{
						if (list == null) return;
						foreach (var entity in list)
						{
							CheckModelPath(entity.ModelPath, subCat);
							CheckModelPath(entity.PortraitModelPath, subCat);
						}
					}

					void CheckProps(IEnumerable<PropMetadata>? list, string subCat)
					{
						if (list == null) return;
						foreach (var entity in list)
						{
							CheckModelPath(entity.ModelPath, subCat);
							CheckModelPath(entity.PortraitModelPath, subCat);
						}
					}

					void CheckResources(IEnumerable<ResourceMetadata>? list, string subCat)
					{
						if (list == null) return;
						foreach (var entity in list)
						{
							CheckModelPath(entity.ModelPath, subCat);
							CheckModelPath(entity.PortraitModelPath, subCat);
						}
					}

					void CheckWeapons(IEnumerable<WeaponMetadata>? list, string subCat)
					{
						if (list == null) return;
						foreach (var entity in list)
						{
							CheckModelPath(entity.ProjectileModelPath, subCat);
						}
					}

					void CheckAttachments(IEnumerable<AttachmentMetadata>? list, string subCat)
					{
						if (list == null) return;
						foreach (var entity in list)
						{
							CheckModelPath(entity.ModelPath, subCat);
						}
					}

					void CheckModelPath(string? modelPath, string subCat)
					{
						if (!string.IsNullOrWhiteSpace(modelPath) && !modelPath.StartsWith("res://", StringComparison.OrdinalIgnoreCase) && !modelPath.StartsWith("user://", StringComparison.OrdinalIgnoreCase))
						{
							string fileName = Path.GetFileName(modelPath);
							string? diskPath = FindModelOnDisk(workspacePath, subCat, fileName);
							if (string.IsNullOrEmpty(diskPath) || !File.Exists(diskPath))
							{
								diskPath = FindAssetOnDisk(workspacePath, "decals", fileName) ?? FindAssetOnDisk(workspacePath, "textures", fileName);
							}
							if (string.IsNullOrEmpty(diskPath) || !File.Exists(diskPath))
							{
								string expectedRel = $"Assets/models/{subCat}/{fileName}".Replace('\\', '/');
								if (!expectedRel.EndsWith(".rmesh", StringComparison.OrdinalIgnoreCase) && !expectedRel.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase))
								{
									expectedRel = Path.ChangeExtension(expectedRel, ".rmesh");
								}
								missingFiles.Add(expectedRel);
							}
						}
					}

					CheckUnits(metadata.Templates.Units, "units");
					CheckUnits(metadata.Templates.Buildings, "buildings");
					CheckProps(metadata.Templates.Props, "props");
					CheckResources(metadata.Templates.Resources, "resources");
					CheckWeapons(metadata.Templates.Weapons, "weapons");
					CheckAttachments(metadata.Templates.Attachments, "attachments");
				}

				void CheckDictionaryAssets(IEnumerable<string>? keys, string subFolder)
				{
					if (keys == null) return;
					foreach (var fileName in keys)
					{
						if (string.IsNullOrWhiteSpace(fileName)) continue;

						string? diskPath = FindAssetOnDisk(workspacePath, subFolder, fileName);
						if (string.IsNullOrEmpty(diskPath) || !File.Exists(diskPath))
						{
							string expectedRel = subFolder == "other" ? fileName : $"Assets/{subFolder}/{fileName}".Replace('\\', '/');
							missingFiles.Add(expectedRel);
						}
					}
				}

				if (metadata.Textures != null) CheckDictionaryAssets(metadata.Textures.Select(k => k.Value?.TexturePath ?? k.Key), "textures");
				if (metadata.Decals != null) CheckDictionaryAssets(metadata.Decals.Select(k => k.Value?.TexturePath ?? k.Key), "decals");
				if (metadata.VfxSpritesheets != null) CheckDictionaryAssets(metadata.VfxSpritesheets.Select(k => k.Value?.TexturePath ?? k.Key), "vfx");
				if (metadata.NoiseTextures != null) CheckDictionaryAssets(metadata.NoiseTextures.Select(k => k.Value?.TexturePath ?? k.Key), "noise");
				if (metadata.Icons != null) CheckDictionaryAssets(metadata.Icons.Select(k => k.Value?.TexturePath ?? k.Key), "icons");
				if (metadata.Skyboxes != null) CheckDictionaryAssets(metadata.Skyboxes.Select(k => k.Value?.TexturePath ?? k.Key), "skyboxes");
				if (metadata.Ribbons != null) CheckDictionaryAssets(metadata.Ribbons.Select(k => k.Value?.TexturePath ?? k.Key), "ribbons");
			}
			catch (Exception ex)
			{
				GD.PrintErr($"[MapAssetHelper] ValidateWorkspaceAssets error reading metadata: {ex.Message}");
			}
		}

		var distinctMissing = missingFiles.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
		return (distinctMissing.Count == 0, distinctMissing);
	}

	public static (bool IsValid, List<(string RelativePath, long SizeBytes, double SizeMB)> OversizedFiles) ValidateWorkspaceAssetSizes(string workspacePath, long maxSizeBytes = ContentAddressableStorage.MaximumAssetSizeBytes)
	{
		var oversized = new List<(string RelativePath, long SizeBytes, double SizeMB)>();
		if (string.IsNullOrEmpty(workspacePath) || !Directory.Exists(workspacePath))
		{
			return (true, oversized);
		}

		string fullDirectoryPath = Path.GetFullPath(workspacePath);
		string[] allFiles = Directory.GetFiles(fullDirectoryPath, "*.*", SearchOption.AllDirectories);

		foreach (string filePath in allFiles)
		{
			string relativePath = Path.GetRelativePath(fullDirectoryPath, filePath).Replace('\\', '/');

			if ((relativePath.StartsWith("bin/", StringComparison.OrdinalIgnoreCase) && !relativePath.EndsWith(".wasm", StringComparison.OrdinalIgnoreCase)) ||
				relativePath.StartsWith("obj/", StringComparison.OrdinalIgnoreCase) ||
				relativePath.StartsWith(".git/", StringComparison.OrdinalIgnoreCase) ||
				relativePath.StartsWith(".vscode/", StringComparison.OrdinalIgnoreCase) ||
				relativePath.StartsWith(".godot/", StringComparison.OrdinalIgnoreCase) ||
				relativePath.StartsWith(".sidecarcache/", StringComparison.OrdinalIgnoreCase) ||
				relativePath.StartsWith(".backups/", StringComparison.OrdinalIgnoreCase) ||
				string.Equals(relativePath, "manifest.json", StringComparison.OrdinalIgnoreCase) ||
				relativePath.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) ||
				relativePath.EndsWith(".rmap", StringComparison.OrdinalIgnoreCase) ||
				relativePath.EndsWith(".7z", StringComparison.OrdinalIgnoreCase) ||
				relativePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
				relativePath.EndsWith(".rar", StringComparison.OrdinalIgnoreCase) ||
				relativePath.EndsWith(".tar", StringComparison.OrdinalIgnoreCase) ||
				relativePath.EndsWith(".gz", StringComparison.OrdinalIgnoreCase) ||
				relativePath.EndsWith(".bak", StringComparison.OrdinalIgnoreCase) ||
				relativePath.EndsWith(".backup", StringComparison.OrdinalIgnoreCase) ||
				relativePath.EndsWith(".rkey", StringComparison.OrdinalIgnoreCase) ||
				string.Equals(Path.GetFileName(relativePath), "authorship_key.pem", StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			var fileInfo = new FileInfo(filePath);
			if (fileInfo.Exists && fileInfo.Length > maxSizeBytes)
			{
				double sizeMb = fileInfo.Length / (1024.0 * 1024.0);
				oversized.Add((relativePath, fileInfo.Length, sizeMb));
			}
		}

		return (oversized.Count == 0, oversized);
	}

	public static void SaveAssetsToManifest(string mapDirectory, MapManifestAssets assets, bool removeFromMetadata = true)
	{
		if (assets == null) return;

		string targetDirectory = string.IsNullOrEmpty(mapDirectory)
			? MapWorkspaceService.GetActiveWorkspacePath()
			: mapDirectory;

		if (!Directory.Exists(targetDirectory))
		{
			Directory.CreateDirectory(targetDirectory);
		}

		EnsureAllAssetsHaveBlake3Hashes(assets, targetDirectory);

		var manifest = MapFileService.LoadManifest(targetDirectory);
		manifest.Assets = assets;
		EditorService.LastInternalSaveTimeUtc = DateTime.UtcNow;
		MapFileService.SaveManifest(targetDirectory, manifest);
	}

	public static void UpdateManifestAsset(
		string mapDirectory,
		string category,
		string fileName,
		string blake3Hash)
	{
		string targetDirectory = string.IsNullOrEmpty(mapDirectory)
			? MapWorkspaceService.GetActiveWorkspacePath()
			: mapDirectory;

		var manifest = MapFileService.LoadManifest(targetDirectory);
		manifest.Assets ??= new MapManifestAssets();

		string normalizedCategory = NormalizeCategoryKey(category);
		var dict = manifest.Assets.GetCategory(normalizedCategory);
		if (dict == null)
		{
			dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			manifest.Assets.SetCategory(normalizedCategory, dict);
		}

		dict[fileName] = blake3Hash;
		EditorService.LastInternalSaveTimeUtc = DateTime.UtcNow;
		MapFileService.SaveManifest(targetDirectory, manifest);
	}

	public static void RemoveManifestAsset(
		string mapDirectory,
		string category,
		string fileName)
	{
		string targetDirectory = string.IsNullOrEmpty(mapDirectory)
			? MapWorkspaceService.GetActiveWorkspacePath()
			: mapDirectory;

		var manifest = MapFileService.LoadManifest(targetDirectory);
		if (manifest.Assets != null)
		{
			string normalizedCategory = NormalizeCategoryKey(category);
			var dict = manifest.Assets.GetCategory(normalizedCategory);
			if (dict != null)
			{
				dict.Remove(fileName);
				EditorService.LastInternalSaveTimeUtc = DateTime.UtcNow;
				MapFileService.SaveManifest(targetDirectory, manifest);
			}
		}
	}

	public static void PruneNonExistentAssetsFromManifest(string mapDirectory)
	{
		string targetDirectory = string.IsNullOrEmpty(mapDirectory)
			? MapWorkspaceService.GetActiveWorkspacePath()
			: mapDirectory;

		if (!Directory.Exists(targetDirectory)) return;

		try
		{
			var manifest = MapFileService.LoadManifest(targetDirectory);
			if (manifest.Assets == null) return;

			foreach (var categoryKvp in manifest.Assets.GetAllCategories())
			{
				string category = NormalizeCategoryKey(categoryKvp.Key);
				var catDict = categoryKvp.Value;
				string subFolder = category switch
				{
					"Character" => "models/units",
					"Building" => "models/buildings",
					"Prop" => "models/props",
					"Item" => "models/items",
					"Spritesheet" => "vfx",
					"vfx_radial" => "vfx_radial",
					"vfx_vertical" => "vfx_vertical",
					"Animation" => "animations",
					"SoundEffect" => "audio/sfx",
					"Music" => "audio/music",
					"Icon" => "icons",
					"Decal" => "decals",
					"Ribbon" => "ribbons",
					"Noise" => "noise",
					"Skybox" => "skyboxes",
					"Terrain" => "textures",
					"Shader" => "shaders",
					_ => category.ToLowerInvariant()
				};

				var itemsToRemove = new List<string>();
				foreach (var itemKvp in catDict)
				{
					string fileName = itemKvp.Key;
					string? diskPath = (category is "Character" or "Building" or "Prop" or "Item")
						? FindModelOnDisk(targetDirectory, subFolder, fileName)
						: FindAssetOnDisk(targetDirectory, subFolder, fileName);

					if (string.IsNullOrEmpty(diskPath) || !File.Exists(diskPath))
					{
						itemsToRemove.Add(fileName);
					}
					else if (string.IsNullOrEmpty(itemKvp.Value))
					{
						string hash = RealmMetadataHelper.ComputeBlake3(diskPath);
						if (!string.IsNullOrEmpty(hash))
						{
							catDict[fileName] = hash;
						}
					}
				}

				foreach (var item in itemsToRemove)
				{
					catDict.Remove(item);
				}
			}

			EditorService.LastInternalSaveTimeUtc = DateTime.UtcNow;
			MapFileService.SaveManifest(targetDirectory, manifest);
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[MapAssetHelper] PruneNonExistentAssetsFromManifest error: {ex.Message}");
		}
	}

	public static void EnsureManifestJson(string directory)
	{
		if (string.IsNullOrEmpty(directory)) return;

		string manifestPath = Path.Combine(directory, "manifest.json");
		if (!File.Exists(manifestPath) || new FileInfo(manifestPath).Length == 0)
		{
			string templateManifest = MapWorkspaceService.GetTemplatePath("manifest.json");
			if (!string.IsNullOrEmpty(templateManifest) && File.Exists(templateManifest))
			{
				try
				{
					File.Copy(templateManifest, manifestPath, true);
				}
				catch
				{
				}
			}
		}

		var manifest = MapFileService.LoadManifest(directory);
		if (manifest.Assets == null)
		{
			manifest.Assets = new MapManifestAssets();
			EditorService.LastInternalSaveTimeUtc = DateTime.UtcNow;
			MapFileService.SaveManifest(directory, manifest);
		}
	}

	public static string? FindModelOnDisk(string targetDirectory, string? preferredSubCategory, string fileName, out string resolvedSubCategory)
	{
		resolvedSubCategory = !string.IsNullOrEmpty(preferredSubCategory) ? preferredSubCategory : "props";
		if (string.IsNullOrEmpty(targetDirectory) || !Directory.Exists(targetDirectory))
		{
			return null;
		}

		string modelsDir = Path.Combine(targetDirectory, "Assets", "models");
		if (!Directory.Exists(modelsDir))
		{
			return null;
		}

		string[] candidateFiles;
		if (fileName.EndsWith(".rmesh", StringComparison.OrdinalIgnoreCase))
		{
			candidateFiles = new[] { fileName };
		}
		else
		{
			candidateFiles = new[] { $"{fileName}.rmesh", $"{Path.GetFileNameWithoutExtension(fileName)}.rmesh" };
		}

		if (!string.IsNullOrEmpty(preferredSubCategory))
		{
			string prefSub = NormalizeGlbSubCategory(preferredSubCategory);
			foreach (var cand in candidateFiles)
			{
				string preferredPath = Path.Combine(modelsDir, prefSub, cand);
				if (File.Exists(preferredPath))
				{
					resolvedSubCategory = prefSub;
					return preferredPath;
				}
			}
		}

		string[] subCategories = new[] { "units", "buildings", "resources", "props", "projectiles", "attachments", "weapons", "environment" };
		foreach (var sub in subCategories)
		{
			foreach (var cand in candidateFiles)
			{
				string candPath = Path.Combine(modelsDir, sub, cand);
				if (File.Exists(candPath))
				{
					resolvedSubCategory = sub;
					return candPath;
				}
			}
		}

		if (Directory.Exists(modelsDir))
		{
			foreach (var dir in Directory.GetDirectories(modelsDir))
			{
				string sub = Path.GetFileName(dir).ToLowerInvariant();
				if (subCategories.Contains(sub)) continue;
				foreach (var cand in candidateFiles)
				{
					string candPath = Path.Combine(dir, cand);
					if (File.Exists(candPath))
					{
						resolvedSubCategory = sub;
						return candPath;
					}
				}
			}
		}

		foreach (var cand in candidateFiles)
		{
			string directPath = Path.Combine(modelsDir, cand);
			if (File.Exists(directPath))
			{
				return directPath;
			}
		}

		return null;
	}

	public static string? FindModelOnDisk(string targetDirectory, string? preferredSubCategory, string fileName)
	{
		return FindModelOnDisk(targetDirectory, preferredSubCategory, fileName, out _);
	}

	public static string? FindAssetOnDisk(string workspacePath, string subFolder, string relativeKey)
	{
		if (string.IsNullOrEmpty(workspacePath) || string.IsNullOrEmpty(relativeKey)) return null;

		string normKey = relativeKey.Replace('\\', '/').TrimStart('/');
		if (normKey.StartsWith("res://", StringComparison.OrdinalIgnoreCase))
		{
			normKey = normKey.Substring(6).TrimStart('/');
		}
		if (normKey.StartsWith($"Assets/{subFolder}/", StringComparison.OrdinalIgnoreCase))
		{
			normKey = normKey.Substring($"Assets/{subFolder}/".Length);
		}

		string assetsDir = Path.Combine(workspacePath, "Assets");

		string p1 = subFolder == "other" ? Path.Combine(workspacePath, normKey) : Path.Combine(assetsDir, subFolder, normKey);
		if (File.Exists(p1)) return p1;

		if (subFolder is "audio/sfx" or "audio/music")
		{
			string p2 = Path.Combine(assetsDir, subFolder.Substring(6), normKey);
			if (File.Exists(p2)) return p2;
		}

		string baseName = Path.GetFileName(normKey);
		if (subFolder == "icons")
		{
			string pAbilities = Path.Combine(assetsDir, "icons", "abilities", baseName);
			if (File.Exists(pAbilities)) return pAbilities;
		}

		string pAssets = Path.Combine(assetsDir, baseName);
		if (File.Exists(pAssets)) return pAssets;

		string pRoot = Path.Combine(workspacePath, normKey);
		if (File.Exists(pRoot)) return pRoot;

		string pRootBase = Path.Combine(workspacePath, baseName);
		if (File.Exists(pRootBase)) return pRootBase;

		string searchRoot = Path.Combine(assetsDir, subFolder);
		if (Directory.Exists(searchRoot))
		{
			try
			{
				var match = Directory.EnumerateFiles(searchRoot, baseName, SearchOption.AllDirectories).FirstOrDefault();
				if (!string.IsNullOrEmpty(match) && File.Exists(match)) return match;
			}
			catch { }
		}

		return null;
	}

	private static void EnsureAllAssetsHaveBlake3Hashes(MapManifestAssets assets, string targetDirectory)
	{
		string assetsDir = Path.Combine(targetDirectory, "Assets");
		bool hasAssetsDir = Directory.Exists(assetsDir);

		foreach (var categoryPair in assets.GetAllCategories())
		{
			string category = NormalizeCategoryKey(categoryPair.Key);
			var catDict = categoryPair.Value;
			foreach (var itemPair in catDict.ToList())
			{
				string fileName = itemPair.Key;
				string hash = itemPair.Value;
				if (string.IsNullOrEmpty(hash))
				{
					string subFolder = category switch
					{
						"Character" => "models/units",
						"Building" => "models/buildings",
						"Prop" => "models/props",
						"Item" => "models/items",
						"Spritesheet" => "vfx",
						"vfx_radial" => "vfx_radial",
						"vfx_vertical" => "vfx_vertical",
						"Animation" => "animations",
						"SoundEffect" => "audio/sfx",
						"Music" => "audio/music",
						"Icon" => "icons",
						"Decal" => "decals",
						"Ribbon" => "ribbons",
						"Noise" => "noise",
						"Skybox" => "skyboxes",
						"Terrain" => "textures",
						"Shader" => "shaders",
						_ => category.ToLowerInvariant()
					};
					string? diskPath = (category is "Character" or "Building" or "Prop" or "Item")
						? (hasAssetsDir ? FindModelOnDisk(targetDirectory, subFolder, fileName) : null)
						: FindAssetOnDisk(targetDirectory, subFolder, fileName);

					if (!string.IsNullOrEmpty(diskPath) && File.Exists(diskPath))
					{
						hash = RealmMetadataHelper.ComputeBlake3(diskPath);
					}

					if (!string.IsNullOrEmpty(hash))
					{
						catDict[fileName] = hash;
					}
				}
			}
		}
	}

	public static string NormalizeCategoryKey(string category)
	{
		string trimmed = category.Trim();
		string norm = trimmed.Replace("_", "").ToLowerInvariant();
		return norm switch
		{
			"character" or "characters" or "unit" or "units" => "Character",
			"building" or "buildings" => "Building",
			"prop" or "props" or "resource" or "resources" or "environment" => "Prop",
			"item" or "items" or "projectile" or "projectiles" or "attachment" or "attachments" or "weapon" or "weapons" => "Item",
			"terrain" or "textures" or "texture" => "Terrain",
			"decal" or "decals" => "Decal",
			"icon" or "icons" => "Icon",
			"noise" or "noisetextures" => "Noise",
			"ribbon" or "ribbons" or "ribbontextures" => "Ribbon",
			"skybox" or "skyboxes" => "Skybox",
			"spritesheet" or "spritesheets" or "vfxspritesheets" or "vfx" => "Spritesheet",
			"vfxradial" => "vfx_radial",
			"vfxvertical" => "vfx_vertical",
			"soundeffect" or "sfx" or "audio" or "sound" or "sounds" => "SoundEffect",
			"music" => "Music",
			"animation" or "animations" => "Animation",
			"shader" or "shaders" => "Shader",
			_ => trimmed
		};
	}

	public static string NormalizeGlbSubCategory(string subCategory)
	{
		string lower = subCategory.ToLowerInvariant();
		if (lower.StartsWith("rmesh_")) lower = lower.Substring(6);
		return lower switch
		{
			"unit" or "units" or "character" or "characters" => "units",
			"building" or "buildings" => "buildings",
			"resource" or "resources" or "environment" => "resources",
			"prop" or "props" => "props",
			"projectile" or "projectiles" => "projectiles",
			"attachment" or "attachments" => "attachments",
			"weapon" or "weapons" => "weapons",
			"item" or "items" => "items",
			_ => lower
		};
	}
}
