using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using Realm.Client.Services;
using Realm.Shared.Distribution;
using Realm.Shared.Services;

namespace Realm.Client.Utils;


public static partial class MapAssetHelper
{
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
		MapFileService.SaveManifest(targetDirectory, manifest);
	}

	public static void RemoveManifestAsset(
		string? mapDirectory,
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

			MapFileService.SaveManifest(targetDirectory, manifest);
		}
		catch (Exception ex)
		{
			GameLog.LogError($"[MapAssetHelper] PruneNonExistentAssetsFromManifest error: {ex.Message}");
		}
	}

	public static void PruneUnusedAssetsFromManifest(string mapDirectory)
	{
		string targetDirectory = string.IsNullOrEmpty(mapDirectory)
			? MapWorkspaceService.GetActiveWorkspacePath()
			: mapDirectory;

		if (!Directory.Exists(targetDirectory)) return;

		try
		{
			var manifest = MapFileService.LoadManifest(targetDirectory);
			if (manifest.Assets == null) return;

			var metadata = MapFileService.LoadMetadata(targetDirectory);
			string metadataJson = MapFileService.SaveMetadataToJson(metadata);

			foreach (var categoryKvp in manifest.Assets.GetAllCategories())
			{
				PruneUnusedCategoryAssets(categoryKvp.Value, metadataJson);
			}

			MapFileService.SaveManifest(targetDirectory, manifest);
		}
		catch (Exception ex)
		{
			GameLog.LogError($"[MapAssetHelper] PruneUnusedAssetsFromManifest error: {ex.Message}");
		}
	}

	private static void PruneUnusedCategoryAssets(Dictionary<string, string> catDict, string metadataJson)
	{
		var itemsToRemove = new List<string>();
		foreach (var itemKvp in catDict)
		{
			string assetName = itemKvp.Key;
			if (!IsAssetReferencedInMetadataJson(metadataJson, assetName))
			{
				itemsToRemove.Add(assetName);
			}
		}

		foreach (var item in itemsToRemove)
		{
			catDict.Remove(item);
		}
	}

	public static bool IsAssetReferencedInMetadataJson(string metadataJson, string assetName)
	{
		if (string.IsNullOrWhiteSpace(assetName)) return false;

		string doubleQuoted = $"\"{assetName}\"";
		string singleQuoted = $"'{assetName}'";

		if (metadataJson.Contains(doubleQuoted, StringComparison.OrdinalIgnoreCase) ||
		    metadataJson.Contains(singleQuoted, StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}

		string baseName = Path.GetFileName(assetName);
		if (!string.Equals(baseName, assetName, StringComparison.OrdinalIgnoreCase))
		{
			if (metadataJson.Contains($"\"{baseName}\"", StringComparison.OrdinalIgnoreCase) ||
			    metadataJson.Contains($"'{baseName}'", StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}

		return false;
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
			MapFileService.SaveManifest(directory, manifest);
		}
	}

}
