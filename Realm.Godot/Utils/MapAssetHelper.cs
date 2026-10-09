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
	private static readonly Dictionary<string, string> CategoryNormalizationMap = new(StringComparer.OrdinalIgnoreCase)
	{
		{ "character", "Character" }, { "characters", "Character" }, { "unit", "Character" }, { "units", "Character" },
		{ "building", "Building" }, { "buildings", "Building" },
		{ "prop", "Prop" }, { "props", "Prop" }, { "resource", "Prop" }, { "resources", "Prop" }, { "environment", "Prop" },
		{ "item", "Item" }, { "items", "Item" }, { "projectile", "Item" }, { "projectiles", "Item" }, { "attachment", "Item" }, { "attachments", "Item" }, { "weapon", "Item" }, { "weapons", "Item" },
		{ "terrain", "Terrain" }, { "textures", "Terrain" }, { "texture", "Terrain" },
		{ "decal", "Decal" }, { "decals", "Decal" },
		{ "icon", "Icon" }, { "icons", "Icon" },
		{ "noise", "Noise" }, { "noisetextures", "Noise" },
		{ "ribbon", "Ribbon" }, { "ribbons", "Ribbon" }, { "ribbontextures", "Ribbon" },
		{ "skybox", "Skybox" }, { "skyboxes", "Skybox" },
		{ "spritesheet", "Spritesheet" }, { "spritesheets", "Spritesheet" }, { "vfxspritesheets", "Spritesheet" }, { "vfx", "Spritesheet" }, { "vfx_spritesheets", "Spritesheet" },
		{ "vfxradial", "vfx_radial" },
		{ "vfxvertical", "vfx_vertical" },
		{ "soundeffect", "SoundEffect" }, { "sfx", "SoundEffect" }, { "audio", "SoundEffect" }, { "sound", "SoundEffect" }, { "sounds", "SoundEffect" },
		{ "music", "Music" },
		{ "animation", "Animation" }, { "animations", "Animation" },
		{ "shader", "Shader" }, { "shaders", "Shader" }
	};

	private static readonly string[] IgnoredPrefixes = { "bin/", "obj/", ".git/", ".vscode/", ".godot/", ".sidecarcache/", ".backups/" };
	private static readonly string[] IgnoredExtensions = { ".tmp", ".rmap", ".7z", ".zip", ".rar", ".tar", ".gz", ".bak", ".backup", ".rkey" };

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

		ValidateManifestAssets(workspacePath, missingFiles);
		ValidateMetadataAssets(workspacePath, missingFiles);

		var distinctMissing = missingFiles.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
		return (distinctMissing.Count == 0, distinctMissing);
	}

	private static void ValidateManifestAssets(string workspacePath, List<string> missingFiles)
	{
		string manifestPath = Path.Combine(workspacePath, "manifest.json");
		if (!File.Exists(manifestPath)) return;

		try
		{
			var manifest = MapFileService.LoadManifest(workspacePath);
			if (manifest.Assets == null) return;

			var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			MapManifest.FlattenAssetsInto(dict, manifest.Assets);

			foreach (var kvp in dict)
			{
				CheckManifestAsset(workspacePath, kvp.Key, missingFiles);
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[MapAssetHelper] ValidateWorkspaceAssets error reading manifest: {ex.Message}");
		}
	}

	private static void CheckManifestAsset(string workspacePath, string relPathRaw, List<string> missingFiles)
	{
		string relPath = relPathRaw.TrimStart('/', '\\');
		string fullPath = Path.Combine(workspacePath, relPath);
		if (File.Exists(fullPath)) return;

		string fileName = Path.GetFileName(relPath);
		string? resolvedModel = FindModelOnDisk(workspacePath, null, fileName);
		if (!string.IsNullOrEmpty(resolvedModel) && File.Exists(resolvedModel)) return;

		string subFolder = relPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) && relPath.Split('/').Length > 2
			? relPath.Split('/')[1]
			: "icons";

		string? resolvedAsset = FindAssetOnDisk(workspacePath, subFolder, relPath);
		if (string.IsNullOrEmpty(resolvedAsset) || !File.Exists(resolvedAsset))
		{
			missingFiles.Add(relPath);
		}
	}

	private static void ValidateMetadataAssets(string workspacePath, List<string> missingFiles)
	{
		string metadataPath = Path.Combine(workspacePath, "metadata.json");
		if (!File.Exists(metadataPath)) return;

		try
		{
			var metadata = MapFileService.LoadMetadata(workspacePath);
			ValidateTemplateModels(workspacePath, metadata, missingFiles);
			ValidateDictionaryAssets(workspacePath, metadata, missingFiles);
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[MapAssetHelper] ValidateWorkspaceAssets error reading metadata: {ex.Message}");
		}
	}

	private static void ValidateTemplateModels(string workspacePath, MapMetadata metadata, List<string> missingFiles)
	{
		if (metadata.Templates == null) return;

		CheckModelPaths(workspacePath, metadata.Templates.Units?.SelectMany(e => new[] { e.ModelPath, e.PortraitModelPath }), "units", missingFiles);
		CheckModelPaths(workspacePath, metadata.Templates.Buildings?.SelectMany(e => new[] { e.ModelPath, e.PortraitModelPath }), "buildings", missingFiles);
		CheckModelPaths(workspacePath, metadata.Templates.Props?.SelectMany(e => new[] { e.ModelPath, e.PortraitModelPath }), "props", missingFiles);
		CheckModelPaths(workspacePath, metadata.Templates.Resources?.SelectMany(e => new[] { e.ModelPath, e.PortraitModelPath }), "resources", missingFiles);
		CheckModelPaths(workspacePath, metadata.Templates.Weapons?.Select(e => e.ProjectileModelPath), "weapons", missingFiles);
		CheckModelPaths(workspacePath, metadata.Templates.Attachments?.Select(e => e.ModelPath), "attachments", missingFiles);
	}

	private static void CheckModelPaths(string workspacePath, IEnumerable<string?>? paths, string subCat, List<string> missingFiles)
	{
		if (paths == null) return;

		foreach (var modelPath in paths)
		{
			if (string.IsNullOrWhiteSpace(modelPath) || modelPath.StartsWith("res://", StringComparison.OrdinalIgnoreCase) || modelPath.StartsWith("user://", StringComparison.OrdinalIgnoreCase)) continue;

			string fileName = Path.GetFileName(modelPath);
			string? diskPath = FindModelOnDisk(workspacePath, subCat, fileName) 
				?? FindAssetOnDisk(workspacePath, "decals", fileName) 
				?? FindAssetOnDisk(workspacePath, "textures", fileName);

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

	private static void ValidateDictionaryAssets(string workspacePath, MapMetadata metadata, List<string> missingFiles)
	{
		ValidateAssetDict(workspacePath, metadata.Textures, v => v.TexturePath, "textures", missingFiles);
		ValidateAssetDict(workspacePath, metadata.Decals, v => v.TexturePath, "decals", missingFiles);
		ValidateAssetDict(workspacePath, metadata.VfxSpritesheets, v => v.TexturePath, "vfx_spritesheets", missingFiles);
		ValidateAssetDict(workspacePath, metadata.NoiseTextures, v => v.TexturePath, "noise", missingFiles);
		ValidateAssetDict(workspacePath, metadata.Icons, v => v.TexturePath, "icons", missingFiles);
		ValidateAssetDict(workspacePath, metadata.Skyboxes, v => v.TexturePath, "skyboxes", missingFiles);
		ValidateAssetDict(workspacePath, metadata.Ribbons, v => v.TexturePath, "ribbons", missingFiles);
	}

	private static void ValidateAssetDict<T>(string workspacePath, Dictionary<string, T>? dict, Func<T, string?> pathSelector, string subFolder, List<string> missingFiles)
	{
		if (dict == null) return;
		var keys = dict.Select(k => 
		{
			string? path = k.Value != null ? pathSelector(k.Value) : null;
			return !string.IsNullOrEmpty(path) ? path : k.Key;
		});
		CheckDictionaryAssets(workspacePath, keys, subFolder, missingFiles);
	}

	private static void CheckDictionaryAssets(string workspacePath, IEnumerable<string>? keys, string subFolder, List<string> missingFiles)
	{
		if (keys == null) return;
		foreach (var key in keys)
		{
			if (string.IsNullOrWhiteSpace(key)) continue;

			string? diskPath = FindAssetOnDisk(workspacePath, subFolder, key);
			if (string.IsNullOrEmpty(diskPath) || !File.Exists(diskPath))
			{
				var (pType, pSlug) = TemplateIDHelper.ParseTemplateID(key);
				string fileToReport = (!string.IsNullOrEmpty(pType) && !string.IsNullOrEmpty(pSlug)) ? pSlug : key;
				if (!Path.HasExtension(fileToReport))
				{
					string defaultExt = GetDefaultExtensionForSubFolder(subFolder);
					fileToReport += defaultExt;
				}
				string expectedRel = subFolder == "other" ? fileToReport : $"Assets/{subFolder}/{fileToReport}".Replace('\\', '/');
				missingFiles.Add(expectedRel);
			}
		}
	}

	private static string GetDefaultExtensionForSubFolder(string subFolder)
	{
		return subFolder switch
		{
			"animations" => ".ranim",
			"audio/sfx" or "audio/music" => ".raud",
			"models/units" or "models/buildings" or "models/props" or "models/items" => ".rmesh",
			_ => ".rtex"
		};
	}

	private static bool IsIgnoredAssetFile(string relativePath)
	{
		foreach (var prefix in IgnoredPrefixes)
		{
			if (relativePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return true;
		}

		foreach (var ext in IgnoredExtensions)
		{
			if (relativePath.EndsWith(ext, StringComparison.OrdinalIgnoreCase)) return true;
		}

		if (string.Equals(relativePath, "manifest.json", StringComparison.OrdinalIgnoreCase) ||
			string.Equals(Path.GetFileName(relativePath), "authorship_key.pem", StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}

		return false;
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

			if (IsIgnoredAssetFile(relativePath) && !(relativePath.StartsWith("bin/", StringComparison.OrdinalIgnoreCase) && relativePath.EndsWith(".wasm", StringComparison.OrdinalIgnoreCase)))
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
				PruneCategoryAssets(targetDirectory, categoryKvp.Key, categoryKvp.Value);
			}

			EditorService.LastInternalSaveTimeUtc = DateTime.UtcNow;
			MapFileService.SaveManifest(targetDirectory, manifest);
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[MapAssetHelper] PruneNonExistentAssetsFromManifest error: {ex.Message}");
		}
	}

	private static void PruneCategoryAssets(string targetDirectory, string rawCategory, Dictionary<string, string> catDict)
	{
		string category = NormalizeCategoryKey(rawCategory);
		string subFolder = GetSubFolderForCategory(category);
		bool isModel = IsModelCategory(category);

		var itemsToRemove = new List<string>();
		foreach (var itemKvp in catDict)
		{
			string fileName = itemKvp.Key;
			string? diskPath = isModel
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

	private static readonly Dictionary<string, string> CategoryToSubFolder = new(StringComparer.OrdinalIgnoreCase)
	{
		{ "Character", "models/units" },
		{ "Building", "models/buildings" },
		{ "Prop", "models/props" },
		{ "Item", "models/items" },
		{ "Spritesheet", "vfx_spritesheets" },
		{ "vfx_spritesheets", "vfx_spritesheets" },
		{ "vfxspritesheets", "vfx_spritesheets" },
		{ "vfx", "vfx_spritesheets" },
		{ "vfx_radial", "vfx_radial" },
		{ "vfx_vertical", "vfx_vertical" },
		{ "Animation", "animations" },
		{ "SoundEffect", "audio/sfx" },
		{ "Music", "audio/music" },
		{ "Icon", "icons" },
		{ "Decal", "decals" },
		{ "Ribbon", "ribbons" },
		{ "Noise", "noise" },
		{ "Skybox", "skyboxes" },
		{ "Terrain", "textures" },
		{ "Shader", "shaders" }
	};

	private static string GetSubFolderForCategory(string category)
	{
		if (CategoryToSubFolder.TryGetValue(category, out string? subFolder))
		{
			return subFolder;
		}
		return category.ToLowerInvariant();
	}

	private static bool IsModelCategory(string category)
	{
		return category is "Character" or "Building" or "Prop" or "Item";
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
		if (string.IsNullOrEmpty(targetDirectory) || !Directory.Exists(targetDirectory)) return null;

		string modelsDir = Path.Combine(targetDirectory, "Assets", "models");
		if (!Directory.Exists(modelsDir)) return null;

		var candidateFiles = GetCandidateModelFiles(fileName);

		string? foundPath = CheckPreferredCategory(modelsDir, preferredSubCategory, candidateFiles, ref resolvedSubCategory);
		if (foundPath != null) return foundPath;

		foundPath = CheckKnownSubCategories(modelsDir, candidateFiles, ref resolvedSubCategory);
		if (foundPath != null) return foundPath;

		foundPath = CheckUnknownSubCategories(modelsDir, candidateFiles, ref resolvedSubCategory);
		if (foundPath != null) return foundPath;

		return CheckDirectPath(modelsDir, candidateFiles);
	}

	private static readonly string[] KnownModelSubCategories = { "units", "buildings", "resources", "props", "projectiles", "attachments", "weapons", "environment" };

	private static string? CheckPreferredCategory(string modelsDir, string? preferredSubCategory, string[] candidateFiles, ref string resolvedSubCategory)
	{
		if (string.IsNullOrEmpty(preferredSubCategory)) return null;

		string prefSub = NormalizeGlbSubCategory(preferredSubCategory);
		string? prefPath = TryFindModelInSubDir(modelsDir, prefSub, candidateFiles);
		if (prefPath != null)
		{
			resolvedSubCategory = prefSub;
			return prefPath;
		}
		return null;
	}

	private static string? CheckKnownSubCategories(string modelsDir, string[] candidateFiles, ref string resolvedSubCategory)
	{
		foreach (var sub in KnownModelSubCategories)
		{
			string? candPath = TryFindModelInSubDir(modelsDir, sub, candidateFiles);
			if (candPath != null)
			{
				resolvedSubCategory = sub;
				return candPath;
			}
		}
		return null;
	}

	private static string? CheckUnknownSubCategories(string modelsDir, string[] candidateFiles, ref string resolvedSubCategory)
	{
		foreach (var dir in Directory.GetDirectories(modelsDir))
		{
			string sub = Path.GetFileName(dir).ToLowerInvariant();
			if (KnownModelSubCategories.Contains(sub)) continue;
			
			string? candPath = TryFindModelInSubDir(modelsDir, sub, candidateFiles);
			if (candPath != null)
			{
				resolvedSubCategory = sub;
				return candPath;
			}
		}
		return null;
	}

	private static string? CheckDirectPath(string modelsDir, string[] candidateFiles)
	{
		foreach (var cand in candidateFiles)
		{
			string directPath = Path.Combine(modelsDir, cand);
			if (File.Exists(directPath)) return directPath;
		}
		return null;
	}

	private static string[] GetCandidateModelFiles(string fileName)
	{
		var (pType, pSlug) = TemplateIDHelper.ParseTemplateID(fileName);
		string effectiveName = (!string.IsNullOrEmpty(pType) && !string.IsNullOrEmpty(pSlug)) ? pSlug : fileName;

		if (effectiveName.EndsWith(".rmesh", StringComparison.OrdinalIgnoreCase))
		{
			return new[] { effectiveName, Path.GetFileName(fileName) };
		}
		
		return new[]
		{
			$"{effectiveName}.rmesh",
			$"{Path.GetFileNameWithoutExtension(effectiveName)}.rmesh",
			$"{Path.GetFileNameWithoutExtension(fileName)}.rmesh"
		};
	}

	private static string? TryFindModelInSubDir(string modelsDir, string subDir, string[] candidateFiles)
	{
		foreach (var cand in candidateFiles)
		{
			string path = Path.Combine(modelsDir, subDir, cand);
			if (File.Exists(path)) return path;
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

		string effectiveKey = NormalizeAssetKey(subFolder, relativeKey);
		string assetsDir = Path.Combine(workspacePath, "Assets");
		var candidateNames = GetCandidateAssetNames(subFolder, effectiveKey);
		var subFoldersToCheck = GetSubFoldersToCheck(subFolder);

		string? foundPath = TryFindAssetInSubFolders(workspacePath, assetsDir, subFoldersToCheck, candidateNames);
		if (foundPath != null) return foundPath;

		foundPath = TryFindAssetInRootOrAssets(workspacePath, assetsDir, candidateNames);
		if (foundPath != null) return foundPath;

		return TryFindAssetDeepSearch(assetsDir, subFoldersToCheck, candidateNames);
	}

	private static string NormalizeAssetKey(string subFolder, string relativeKey)
	{
		string normKey = relativeKey.Replace('\\', '/').TrimStart('/');
		if (normKey.StartsWith("res://", StringComparison.OrdinalIgnoreCase))
		{
			normKey = normKey.Substring(6).TrimStart('/');
		}
		if (normKey.StartsWith($"Assets/{subFolder}/", StringComparison.OrdinalIgnoreCase))
		{
			normKey = normKey.Substring($"Assets/{subFolder}/".Length);
		}

		var (parsedType, parsedSlug) = TemplateIDHelper.ParseTemplateID(normKey);
		return (!string.IsNullOrEmpty(parsedType) && !string.IsNullOrEmpty(parsedSlug)) ? parsedSlug : normKey;
	}

	private static List<string> GetCandidateAssetNames(string subFolder, string effectiveKey)
	{
		string baseName = Path.GetFileName(effectiveKey);
		var candidates = new List<string> { effectiveKey, baseName };
		if (!Path.HasExtension(baseName))
		{
			string defaultExt = GetDefaultExtensionForSubFolder(subFolder);
			candidates.Add($"{effectiveKey}{defaultExt}");
			candidates.Add($"{baseName}{defaultExt}");
		}
		return candidates;
	}

	private static List<string> GetSubFoldersToCheck(string subFolder)
	{
		var list = new List<string> { subFolder };
		if (subFolder == "vfx_spritesheets") list.Add("vfx");
		else if (subFolder == "vfx") list.Add("vfx_spritesheets");
		return list;
	}

	private static string? TryFindAssetInSubFolders(string workspacePath, string assetsDir, List<string> subFolders, List<string> candidateNames)
	{
		foreach (var sf in subFolders)
		{
			foreach (var cand in candidateNames)
			{
				string p1 = sf == "other" ? Path.Combine(workspacePath, cand) : Path.Combine(assetsDir, sf, cand);
				if (File.Exists(p1)) return p1;

				if (sf is "audio/sfx" or "audio/music")
				{
					string p2 = Path.Combine(assetsDir, sf.Substring(6), cand);
					if (File.Exists(p2)) return p2;
				}

				if (sf == "icons")
				{
					string pAbilities = Path.Combine(assetsDir, "icons", "abilities", cand);
					if (File.Exists(pAbilities)) return pAbilities;
				}
			}
		}
		return null;
	}

	private static string? TryFindAssetInRootOrAssets(string workspacePath, string assetsDir, List<string> candidateNames)
	{
		foreach (var cand in candidateNames)
		{
			string pAssets = Path.Combine(assetsDir, cand);
			if (File.Exists(pAssets)) return pAssets;

			string pRoot = Path.Combine(workspacePath, cand);
			if (File.Exists(pRoot)) return pRoot;
		}
		return null;
	}

	private static string? TryFindAssetDeepSearch(string assetsDir, List<string> subFolders, List<string> candidateNames)
	{
		foreach (var sf in subFolders)
		{
			string searchRoot = Path.Combine(assetsDir, sf);
			if (!Directory.Exists(searchRoot)) continue;

			try
			{
				foreach (var cand in candidateNames)
				{
					var match = Directory.EnumerateFiles(searchRoot, cand, SearchOption.AllDirectories).FirstOrDefault();
					if (!string.IsNullOrEmpty(match) && File.Exists(match)) return match;
				}
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
			EnsureCategoryHashes(categoryPair.Key, categoryPair.Value, targetDirectory, hasAssetsDir);
		}
	}

	private static void EnsureCategoryHashes(string rawCategory, Dictionary<string, string> catDict, string targetDirectory, bool hasAssetsDir)
	{
		string category = NormalizeCategoryKey(rawCategory);
		string subFolder = GetSubFolderForCategory(category);
		bool isModel = IsModelCategory(category);

		foreach (var itemPair in catDict.ToList())
		{
			string fileName = itemPair.Key;
			string hash = itemPair.Value;
			if (!string.IsNullOrEmpty(hash)) continue;

			string? diskPath = isModel
				? (hasAssetsDir ? FindModelOnDisk(targetDirectory, subFolder, fileName) : null)
				: FindAssetOnDisk(targetDirectory, subFolder, fileName);

			if (!string.IsNullOrEmpty(diskPath) && File.Exists(diskPath))
			{
				hash = RealmMetadataHelper.ComputeBlake3(diskPath);
				if (!string.IsNullOrEmpty(hash))
				{
					catDict[fileName] = hash;
				}
			}
		}
	}

	public static string NormalizeCategoryKey(string category)
	{
		string trimmed = category.Trim();
		string norm = trimmed.Replace("_", "").ToLowerInvariant();
		
		if (CategoryNormalizationMap.TryGetValue(norm, out string? mappedValue))
		{
			return mappedValue;
		}
		return trimmed;
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
