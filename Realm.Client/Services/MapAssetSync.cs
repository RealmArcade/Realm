using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Realm.Client.Core;
using Realm.Shared.Distribution;

namespace Realm.Client.Services;

/// <summary>
///     Keeping a map's asset folder and its manifest in step: which folder each category belongs in,
///     where a named asset actually sits on disk, and the sync that drops stale import files, rewrites
///     each asset's hash into the manifest and removes the directories left empty.
/// </summary>
public static class MapAssetSync
{
	public static bool IsValidPropObjectId(string propId, string? mapDirectory = null)
	{
		if (string.IsNullOrWhiteSpace(propId)) return false;

		if (GameHost.PropRegistry?.ContainsKey(propId) == true || GameHost.ResourceRegistry?.ContainsKey(propId) == true) return true;

		return HasPropMetadata(propId, mapDirectory);
	}

	private static bool HasPropMetadata(string propId, string? mapDirectory)
	{
		string targetDir = !string.IsNullOrEmpty(mapDirectory) ? mapDirectory : MapWorkspaceService.GetActiveWorkspacePath();
		if (!GameContext.MetadataService.TryLoadMetadata(targetDir, out var metadata) || metadata.Templates == null) return false;

		bool hasProp = metadata.Templates.Props?.Any(p => string.Equals(propId, p.TemplateID, StringComparison.OrdinalIgnoreCase)) ?? false;
		if (hasProp) return true;

		return metadata.Templates.Resources?.Any(r => string.Equals(propId, r.TemplateID, StringComparison.OrdinalIgnoreCase)) ?? false;
	}

	public static bool IsValidUnitObjectId(string unitId, string? mapDirectory = null)
	{
		if (string.IsNullOrWhiteSpace(unitId)) return false;

		if (GameHost.UnitRegistry?.ContainsKey(unitId) == true || GameHost.BuildingRegistry?.ContainsKey(unitId) == true) return true;

		return HasUnitMetadata(unitId, mapDirectory);
	}

	private static bool HasUnitMetadata(string unitId, string? mapDirectory)
	{
		string targetDir = !string.IsNullOrEmpty(mapDirectory) ? mapDirectory : MapWorkspaceService.GetActiveWorkspacePath();
		if (!GameContext.MetadataService.TryLoadMetadata(targetDir, out var metadata) || metadata.Templates == null) return false;

		bool hasUnit = metadata.Templates.Units?.Any(u => string.Equals(unitId, u.TemplateID, StringComparison.OrdinalIgnoreCase)) ?? false;
		if (hasUnit) return true;

		return metadata.Templates.Buildings?.Any(b => string.Equals(unitId, b.TemplateID, StringComparison.OrdinalIgnoreCase)) ?? false;
	}

	public static bool IsValidItemObjectId(string itemId, string? mapDirectory = null)
	{
		if (string.IsNullOrWhiteSpace(itemId)) return false;

		if (GameContext.RegistryService?.ItemRegistry.ContainsKey(itemId) == true) return true;

		return HasItemMetadata(itemId, mapDirectory);
	}

	private static bool HasItemMetadata(string itemId, string? mapDirectory)
	{
		string targetDir = !string.IsNullOrEmpty(mapDirectory) ? mapDirectory : MapWorkspaceService.GetActiveWorkspacePath();
		if (!GameContext.MetadataService.TryLoadMetadata(targetDir, out var metadata) || metadata.Templates == null) return false;

		return metadata.Templates.Items?.Any(i => string.Equals(itemId, i.TemplateID, StringComparison.OrdinalIgnoreCase)) ?? false;
	}

	private static readonly Dictionary<string, string> CategorySubFolderMap = new(StringComparer.OrdinalIgnoreCase)
	{
		{ "character", "models/units" },
		{ "building", "models/buildings" },
		{ "prop", "models/props" },
		{ "item", "models/items" },
		{ "spritesheet", "vfx_spritesheets" }, { "vfx", "vfx_spritesheets" }, { "vfx_spritesheets", "vfx_spritesheets" },
		{ "animation", "animations" }, { "animations", "animations" },
		{ "soundeffect", "audio/sfx" }, { "sfx", "audio/sfx" },
		{ "music", "audio/music" },
		{ "icon", "icons" }, { "icons", "icons" },
		{ "decal", "decals" }, { "decals", "decals" },
		{ "ribbon", "ribbons" }, { "ribbons", "ribbons" }, { "ribbon_textures", "ribbons" },
		{ "noise", "noise" }, { "noise_textures", "noise" },
		{ "skybox", "skyboxes" }, { "skyboxes", "skyboxes" },
		{ "terrain", "textures" }, { "textures", "textures" },
		{ "shader", "shaders" }, { "shaders", "shaders" }
	};

	private static string GetSubFolderForCategory(string category)
	{
		return CategorySubFolderMap.TryGetValue(category, out string? folder)
			? folder ?? category.ToLowerInvariant()
			: category.ToLowerInvariant();
	}

	private static string ResolveFullDiskPath(string mapDirectory, string assetsDir, string relPath, string category, string baseFileName, ref Dictionary<string, string>? cachedAssetFiles)
	{
		string fullDiskPath = Path.Combine(mapDirectory, relPath);
		if (!File.Exists(fullDiskPath))
		{
			if (category is "Character" or "Building" or "Prop" or "Item")
			{
				string? modelDisk = MapAssetHelper.FindModelOnDisk(mapDirectory, category, baseFileName);
				if (!string.IsNullOrEmpty(modelDisk) && File.Exists(modelDisk))
				{
					fullDiskPath = modelDisk;
				}
			}
			else
			{
				string? altPath = FindAssetFileByName(assetsDir, baseFileName, ref cachedAssetFiles);
				if (altPath != null && File.Exists(altPath))
				{
					fullDiskPath = altPath;
				}
				else
				{
					string directMapPath = Path.Combine(mapDirectory, baseFileName);
					if (File.Exists(directMapPath))
					{
						fullDiskPath = directMapPath;
					}
				}
			}
		}

		return fullDiskPath;
	}

	public static void SyncMetadataAssetsAndPrune(string mapDirectory)
	{
		if (string.IsNullOrEmpty(mapDirectory) || !Directory.Exists(mapDirectory)) return;

		try
		{
			var assetsObj = MapAssetHelper.LoadAssets(mapDirectory);
			string assetsDir = Path.Combine(mapDirectory, "Assets");

			var assetsToSync = CollectAssetsToSync(assetsObj);

			PruneImportFilesAndEmptyDirs(assetsDir);

			SyncAssetHashes(mapDirectory, assetsDir, assetsObj, assetsToSync);

			MapAssetHelper.SaveAssetsToManifest(mapDirectory, assetsObj, removeFromMetadata: true);
		}
		catch (Exception ex)
		{
			GameLog.LogError($"[SaveLoadService] SyncMetadataAssetsAndPrune error: {ex.Message}");
		}
	}

	private static List<(string RelativePath, string Category, string FileName)> CollectAssetsToSync(MapManifestAssets assetsObj)
	{
		var assetsToSync = new List<(string RelativePath, string Category, string FileName)>();
		foreach (var categoryKvp in assetsObj.GetAllCategories())
		{
			string category = categoryKvp.Key;
			string subFolder = GetSubFolderForCategory(category);
			foreach (var itemKvp in categoryKvp.Value)
			{
				string fileName = itemKvp.Key;
				string relPath = Path.Combine("Assets", subFolder, fileName).Replace('\\', '/');
				assetsToSync.Add((relPath, category, fileName));
			}
		}

		return assetsToSync;
	}

	private static void PruneImportFilesAndEmptyDirs(string assetsDir)
	{
		if (!Directory.Exists(assetsDir)) return;

		string[] importFiles = Directory.GetFiles(assetsDir, "*.import", SearchOption.AllDirectories);
		foreach (var importFile in importFiles)
		{
			string sourceFile = importFile.Substring(0, importFile.Length - ".import".Length);
			if (!File.Exists(sourceFile))
			{
				try
				{
					File.Delete(importFile);
				}
				catch
				{
				}
			}
		}

		DeleteEmptyDirectoriesRecursive(assetsDir);
	}

	private static void SyncAssetHashes(string mapDirectory, string assetsDir, MapManifestAssets assetsObj, List<(string RelativePath, string Category, string FileName)> assetsToSync)
	{
		Dictionary<string, string>? cachedAssetFiles = null;

		foreach (var (relPath, category, fileName) in assetsToSync)
		{
			string baseFileName = Path.GetFileName(relPath);
			string fullDiskPath = ResolveFullDiskPath(mapDirectory, assetsDir, relPath, category, baseFileName, ref cachedAssetFiles);

			if (!File.Exists(fullDiskPath)) continue;

			string canonicalBlake3 = RealmMetadataHelper.ComputeBlake3(fullDiskPath);
			if (string.IsNullOrEmpty(canonicalBlake3)) continue;

			var catDict = assetsObj.GetCategory(category);
			if (catDict != null)
			{
				catDict[fileName] = canonicalBlake3;
			}

			RealmMetadataHelper.SyncBlake3Metadata(fullDiskPath, canonicalBlake3);
		}
	}

	private static string? FindAssetFileByName(string searchDir, string fileName, ref Dictionary<string, string>? cachedFiles)
	{
		if (!Directory.Exists(searchDir)) return null;
		if (cachedFiles == null)
		{
			cachedFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			try
			{
				foreach (var file in Directory.EnumerateFiles(searchDir, "*", SearchOption.AllDirectories))
				{
					string name = Path.GetFileName(file);
					cachedFiles.TryAdd(name, file);
				}
			}
			catch
			{
			}
		}

		return cachedFiles.TryGetValue(fileName, out var path) ? path : null;
	}

	private static void DeleteEmptyDirectoriesRecursive(string directory)
	{
		if (!Directory.Exists(directory)) return;
		foreach (var sub in Directory.GetDirectories(directory))
		{
			DeleteEmptyDirectoriesRecursive(sub);
		}

		if (Directory.GetFiles(directory).Length == 0 && Directory.GetDirectories(directory).Length == 0)
		{
			try
			{
				Directory.Delete(directory, false);
			}
			catch
			{
			}
		}
	}
}
