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
	private static readonly string[] IgnoredExtensions = { ".tmp", ".rmap", ".7z", ".zip", ".rar", ".tar", ".gz", ".bak", ".backup", ".rkey", ".elf", ".jit.elf" };

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
