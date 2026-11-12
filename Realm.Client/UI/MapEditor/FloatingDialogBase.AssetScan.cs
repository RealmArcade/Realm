using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using Fastenshtein;
using Godot;
using Realm.Client.Services;
using Realm.Client.Utils;
using Realm.Shared.Distribution;
using Realm.Shared.Services;
using Environment = Godot.Environment;

namespace Realm.Client.UI.MapEditor;

public partial class FloatingDialogBase
{
	public static List<string> ScanAvailableAssets(string category, bool includeAllFolders = false, string? subFolder = null)
	{
		var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		string wsPath = !string.IsNullOrEmpty(MapWorkspaceService.GetActiveWorkspacePath())
			? MapWorkspaceService.GetActiveWorkspacePath()
			: GameContext.EditorService._editingDirectory;

		try
		{
			var assetsObj = MapAssetHelper.LoadAssets(wsPath);
			string manifestPath = Path.Combine(wsPath, "manifest.json");
			MapManifest? manifest = LoadManifest(manifestPath);

			if (assetsObj == null && manifest == null) return new List<string>();

			ScanCategoryAssets(category, result, assetsObj, manifest, wsPath, includeAllFolders, subFolder);
		}
		catch (Exception ex)
		{
			GameLog.LogError($"[FloatingDialogBase] ScanAvailableAssets metadata error: {ex.Message}");
		}

		var list = new List<string>(result);
		list.Sort(StringComparer.OrdinalIgnoreCase);
		return list;
	}

	private static MapManifest? LoadManifest(string manifestPath)
	{
		if (!File.Exists(manifestPath)) return null;
		try
		{
			return MapManifest.LoadFromFile(manifestPath);
		}
		catch
		{
			return null;
		}
	}

	private static void ScanCategoryAssets(string category, HashSet<string> result, MapManifestAssets? assetsObj, MapManifest? manifest, string wsPath, bool includeAllFolders, string? subFolder)
	{
		if (TryScanMediaAssets(category, result, assetsObj, manifest, wsPath)) return;
		if (TryScanVisualAssets(category, result, assetsObj, manifest, wsPath, includeAllFolders, subFolder)) return;
		if (TryScanEnvironmentAssets(category, result, assetsObj, manifest, wsPath)) return;
	}

	private static bool TryScanMediaAssets(string category, HashSet<string> result, MapManifestAssets? assetsObj, MapManifest? manifest, string wsPath)
	{
		if (category is "audio" or "sound" or "sfx" or "music")
		{
			ScanAudioAssets(result, assetsObj, manifest, wsPath);
			return true;
		}

		if (category is "spritesheets" or "spritesheet" or "spritesheet_rtex")
		{
			ScanSpritesheetAssets(result, assetsObj, manifest, wsPath);
			return true;
		}

		if (category is "vfx" or "vfx_spritesheets" or "vfx_radial" or "vfx_vertical")
		{
			ScanVfxAssets(category, result, assetsObj, wsPath);
			return true;
		}

		if (category is "decals" or "decal")
		{
			ScanDecalAssets(result, assetsObj, manifest, wsPath);
			return true;
		}

		return false;
	}

	private static bool TryScanVisualAssets(string category, HashSet<string> result, MapManifestAssets? assetsObj, MapManifest? manifest, string wsPath, bool includeAllFolders, string? subFolder)
	{
		if (category is "models" or "glb" or "attachments" or "items" or "weapons" or "projectiles")
		{
			ScanModelAssets(category, result, assetsObj, manifest, wsPath, includeAllFolders, subFolder);
			return true;
		}

		if (category is "ribbons" or "ribbon_textures")
		{
			ScanRibbonAssets(result, assetsObj, manifest, wsPath);
			return true;
		}

		if (category is "animations" or "ranim" or "animation")
		{
			ScanAnimationAssets(result, assetsObj, wsPath);
			return true;
		}

		return false;
	}

	private static bool TryScanEnvironmentAssets(string category, HashSet<string> result, MapManifestAssets? assetsObj, MapManifest? manifest, string wsPath)
	{
		if (category is "icons" or "icon")
		{
			ScanIconAssets(result, assetsObj, manifest, wsPath);
			return true;
		}

		if (category is "noise" or "noise_textures")
		{
			ScanNoiseAssets(result, assetsObj, manifest, wsPath);
			return true;
		}

		if (category is "textures" or "terrain")
		{
			ScanTerrainAssets(result, assetsObj, manifest, wsPath);
			return true;
		}

		return false;
	}

	private static void ScanAudioAssets(HashSet<string> result, MapManifestAssets? assetsObj, MapManifest? manifest, string wsPath)
	{
		ScanAudioAssetsFromCategories(result, assetsObj);
		ScanAudioAssetsFromManifestFiles(result, manifest);
		ScanAudioAssetsFromDirectory(result, wsPath);
	}

	private static void ScanAudioAssetsFromCategories(HashSet<string> result, MapManifestAssets? assetsObj)
	{
		foreach (var key in new[] { "SoundEffect", "Music" })
		{
			var catDict = assetsObj?.GetCategory(key);
			if (catDict == null) continue;

			foreach (var kvp in catDict)
			{
				if (IsAudioAsset(kvp.Key))
				{
					result.Add(Path.GetFileName(kvp.Key));
				}
			}
		}
	}

	private static void ScanAudioAssetsFromManifestFiles(HashSet<string> result, MapManifest? manifest)
	{
		if (manifest?.Files == null) return;

		foreach (var kvp in manifest.Files)
		{
			if (IsAudioAsset(kvp.Key))
			{
				result.Add(Path.GetFileName(kvp.Key));
			}
		}
	}

	private static void ScanAudioAssetsFromDirectory(HashSet<string> result, string wsPath)
	{
		string audioDir = Path.Combine(wsPath, "Assets", "audio");
		if (!Directory.Exists(audioDir)) return;

		foreach (var file in Directory.EnumerateFiles(audioDir, "*.*", SearchOption.AllDirectories))
		{
			if (IsAudioAsset(file))
			{
				result.Add(Path.GetFileName(file));
			}
		}
	}

	private static bool IsAudioAsset(string path)
	{
		if (string.IsNullOrWhiteSpace(path)) return false;
		return path.EndsWith(".raud", StringComparison.OrdinalIgnoreCase) ||
		       path.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase) ||
		       path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase);
	}

	private static void ScanSpritesheetAssets(HashSet<string> result, MapManifestAssets? assetsObj, MapManifest? manifest, string wsPath)
	{
		ScanRtexAssetsFromCategory(result, assetsObj?.GetCategory("Spritesheet") ?? assetsObj?.GetCategory("spritesheets"));
		ScanRtexAssetsFromCategory(result, manifest?.Assets?.Spritesheet);
		ScanSpritesheetAssetsFromManifestFiles(result, manifest);
		ScanSpritesheetAssetsFromMetadata(result, wsPath);
		ScanRtexAssetsFromDirectory(result, Path.Combine(wsPath, "Assets", "vfx"));
		ScanRtexAssetsFromDirectory(result, Path.Combine(ProjectSettings.GlobalizePath("res://"), "Assets", "vfx"));
	}

	private static void ScanRtexAssetsFromCategory(HashSet<string> result, IDictionary<string, string>? dict)
	{
		if (dict == null) return;
		foreach (var kvp in dict)
		{
			if (IsRtexAsset(kvp.Key))
				result.Add(Path.GetFileName(kvp.Key));
		}
	}

	private static void ScanRtexAssetsFromDirectory(HashSet<string> result, string dirPath)
	{
		if (!Directory.Exists(dirPath)) return;
		foreach (var file in Directory.EnumerateFiles(dirPath, "*.rtex", SearchOption.AllDirectories))
		{
			result.Add(Path.GetFileName(file));
		}
	}

	private static bool IsRtexAsset([NotNullWhen(true)] string? path)
	{
		return !string.IsNullOrWhiteSpace(path) && path.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase);
	}

	private static void ScanSpritesheetAssetsFromManifestFiles(HashSet<string> result, MapManifest? manifest)
	{
		if (manifest?.Files == null) return;

		foreach (var kvp in manifest.Files)
		{
			if (IsRtexAsset(kvp.Key) &&
			    (kvp.Key.StartsWith("Assets/vfx/spritesheets/", StringComparison.OrdinalIgnoreCase) ||
			     kvp.Key.StartsWith("Assets/spritesheets/", StringComparison.OrdinalIgnoreCase) ||
			     kvp.Key.StartsWith("Assets/vfx/", StringComparison.OrdinalIgnoreCase)))
			{
				result.Add(Path.GetFileName(kvp.Key));
			}
		}
	}

	private static void ScanSpritesheetAssetsFromMetadata(HashSet<string> result, string wsPath)
	{
		if (GameContext.MetadataService.TryLoadMetadata(wsPath, out var ssMetaRoot) && ssMetaRoot?.VfxSpritesheets != null)
		{
			foreach (var kvp in ssMetaRoot.VfxSpritesheets)
			{
				string? texturePath = kvp.Value?.TexturePath;
				if (IsRtexAsset(texturePath))
				{
					result.Add(Path.GetFileName(texturePath));
				}
			}
		}
	}

	private static void ScanVfxAssets(string category, HashSet<string> result, MapManifestAssets? assetsObj, string wsPath)
	{
		ScanVfxAssetsFromRegistry(category, result);
		ScanVfxAssetsFromCategories(category, result, assetsObj);
		ScanVfxAssetsFromMetadata(result, wsPath);
	}

	private static void ScanVfxAssetsFromRegistry(string category, HashSet<string> result)
	{
		if (category is not ("vfx" or "vfx_spritesheets")) return;
		if (GameContext.RegistryService.VfxRegistry == null) return;

		foreach (var kvp in GameContext.RegistryService.VfxRegistry)
		{
			result.Add(kvp.Key.StartsWith("vfx:", StringComparison.OrdinalIgnoreCase) ? kvp.Key : $"vfx:{kvp.Key}");
		}
	}

	private static void ScanVfxAssetsFromCategories(string category, HashSet<string> result, MapManifestAssets? assetsObj)
	{
		string[] searchKeys = category switch
		{
			"vfx_radial" => new[] { "vfx_radial", "Spritesheet" },
			"vfx_vertical" => new[] { "vfx_vertical", "Spritesheet" },
			_ => new[] { "Spritesheet", "vfx_radial", "vfx_vertical" }
		};

		foreach (var key in searchKeys)
		{
			ScanRtexAssetsFromCategory(result, assetsObj?.GetCategory(key));
		}
	}

	private static void ScanVfxAssetsFromMetadata(HashSet<string> result, string wsPath)
	{
		if (!GameContext.MetadataService.TryLoadMetadata(wsPath, out var vfxMetaRoot)) return;
		if (vfxMetaRoot?.Templates?.Vfx == null) return;

		foreach (var cv in vfxMetaRoot.Templates.Vfx)
		{
			if (string.IsNullOrWhiteSpace(cv.VfxId)) continue;

			result.Add(cv.VfxId.StartsWith("vfx:", StringComparison.OrdinalIgnoreCase) ? cv.VfxId : $"vfx:{cv.VfxId}");
			result.Add(cv.VfxId);
		}
	}

	private static void ScanDecalAssets(HashSet<string> result, MapManifestAssets? assetsObj, MapManifest? manifest, string wsPath)
	{
		ScanRtexAssetsFromCategory(result, assetsObj?.GetCategory("Decal"));
		ScanRtexAssetsFromCategory(result, manifest?.Assets?.Decal);
		ScanDecalAssetsFromManifestFiles(result, manifest);
		ScanRtexAssetsFromDirectory(result, Path.Combine(wsPath, "Assets", "decals"));
		ScanRtexAssetsFromDirectory(result, Path.Combine(ProjectSettings.GlobalizePath("res://"), "Assets", "decals"));
	}

	private static void ScanDecalAssetsFromManifestFiles(HashSet<string> result, MapManifest? manifest)
	{
		if (manifest?.Files == null) return;

		foreach (var kvp in manifest.Files)
		{
			if (IsRtexAsset(kvp.Key) &&
			    (kvp.Key.StartsWith("Assets/decals/", StringComparison.OrdinalIgnoreCase) ||
			     kvp.Key.StartsWith("assets/decals/", StringComparison.OrdinalIgnoreCase) ||
			     kvp.Key.StartsWith("decals/", StringComparison.OrdinalIgnoreCase)))
			{
				result.Add(Path.GetFileName(kvp.Key));
			}
		}
	}

	private static void ScanModelAssets(string category, HashSet<string> result, MapManifestAssets? assetsObj, MapManifest? manifest, string wsPath, bool includeAllFolders, string? subFolder)
	{
		ScanModelAssetsFromRegistry(category, result, includeAllFolders);
		ScanModelAssetsFromCategories(category, result, assetsObj, includeAllFolders, subFolder);
		ScanModelAssetsFromManifestFiles(result, manifest);
		ScanModelAssetsFromDirectory(result, Path.Combine(wsPath, "Assets", "models"));
	}

	private static void ScanModelAssetsFromRegistry(string category, HashSet<string> result, bool includeAllFolders)
	{
		if (!(category is "attachments" or "items" || includeAllFolders)) return;
		if (GameContext.RegistryService.VfxRegistry == null) return;

		foreach (var kvp in GameContext.RegistryService.VfxRegistry)
		{
			result.Add(kvp.Key.StartsWith("vfx:", StringComparison.OrdinalIgnoreCase) ? kvp.Key : $"vfx:{kvp.Key}");
		}
	}

	private static void ScanModelAssetsFromCategories(string category, HashSet<string> result, MapManifestAssets? assetsObj, bool includeAllFolders, string? subFolder)
	{
		string defaultFolder = !string.IsNullOrEmpty(subFolder) ? subFolder : (category is "attachments" or "items" ? "items" : "projectiles");

		foreach (var catName in new[] { "Character", "Building", "Prop", "Item" })
		{
			if (IsModelCategoryMatch(catName, defaultFolder, includeAllFolders))
			{
				ScanRmeshAssetsFromCategory(result, assetsObj?.GetCategory(catName));
			}
		}
	}

	private static bool IsModelCategoryMatch(string catName, string defaultFolder, bool includeAllFolders)
	{
		return includeAllFolders || catName.Equals(defaultFolder, StringComparison.OrdinalIgnoreCase) ||
		       (defaultFolder == "items" && catName == "Item") ||
		       (defaultFolder == "units" && catName == "Character") ||
		       (defaultFolder == "buildings" && catName == "Building") ||
		       (defaultFolder == "props" && catName == "Prop");
	}

	private static void ScanRmeshAssetsFromCategory(HashSet<string> result, IDictionary<string, string>? dict)
	{
		if (dict == null) return;
		foreach (var kvp in dict)
		{
			if (IsRmeshAsset(kvp.Key))
				result.Add(Path.GetFileName(kvp.Key));
		}
	}

	private static bool IsRmeshAsset([NotNullWhen(true)] string? path)
	{
		return !string.IsNullOrWhiteSpace(path) && path.EndsWith(".rmesh", StringComparison.OrdinalIgnoreCase);
	}

	private static void ScanModelAssetsFromManifestFiles(HashSet<string> result, MapManifest? manifest)
	{
		if (manifest?.Files == null) return;

		foreach (var kvp in manifest.Files)
		{
			if (IsRmeshAsset(kvp.Key))
			{
				result.Add(Path.GetFileName(kvp.Key));
			}
		}
	}

	private static void ScanModelAssetsFromDirectory(HashSet<string> result, string dirPath)
	{
		if (!Directory.Exists(dirPath)) return;

		foreach (var file in Directory.EnumerateFiles(dirPath, "*.rmesh", SearchOption.AllDirectories))
		{
			result.Add(Path.GetFileName(file));
		}
	}

	private static void ScanRibbonAssets(HashSet<string> result, MapManifestAssets? assetsObj, MapManifest? manifest, string wsPath)
	{
		ScanRtexAssetsFromCategory(result, assetsObj?.GetCategory("Ribbon"));
		ScanRtexAssetsFromCategory(result, manifest?.Assets?.Ribbon);
		ScanRtexAssetsFromDirectory(result, Path.Combine(wsPath, "Assets", "ribbons"));
	}

	private static void ScanAnimationAssets(HashSet<string> result, MapManifestAssets? assetsObj, string wsPath)
	{
		var aDict = assetsObj?.GetCategory("Animation");
		if (aDict != null)
		{
			foreach (var kvp in aDict)
			{
				if (!string.IsNullOrWhiteSpace(kvp.Key) && kvp.Key.EndsWith(".ranim", StringComparison.OrdinalIgnoreCase))
				{
					result.Add(Path.GetFileName(kvp.Key));
				}
			}
		}

		string animsDir = Path.Combine(wsPath, "Assets", "animations");
		if (Directory.Exists(animsDir))
		{
			foreach (var file in Directory.EnumerateFiles(animsDir, "*.ranim", SearchOption.AllDirectories))
			{
				result.Add(Path.GetFileName(file));
			}
		}
	}

	private static void ScanIconAssets(HashSet<string> result, MapManifestAssets? assetsObj, MapManifest? manifest, string wsPath)
	{
		ScanRtexAssetsFromCategory(result, assetsObj?.GetCategory("Icon") ?? assetsObj?.GetCategory("icons"));
		ScanRtexAssetsFromCategory(result, manifest?.Assets?.Icon);
		ScanIconAssetsFromManifestFiles(result, manifest);
		ScanIconAssetsFromMetadata(result, wsPath);
		ScanRtexAssetsFromDirectory(result, Path.Combine(wsPath, "Assets", "icons"));
		ScanRtexAssetsFromDirectory(result, Path.Combine(ProjectSettings.GlobalizePath("res://"), "Assets", "icons"));
	}

	private static void ScanIconAssetsFromManifestFiles(HashSet<string> result, MapManifest? manifest)
	{
		if (manifest?.Files == null) return;

		foreach (var kvp in manifest.Files)
		{
			if (IsRtexAsset(kvp.Key) &&
			    (kvp.Key.StartsWith("Assets/icons/", StringComparison.OrdinalIgnoreCase) ||
			     kvp.Key.StartsWith("assets/icons/", StringComparison.OrdinalIgnoreCase) ||
			     kvp.Key.StartsWith("icons/", StringComparison.OrdinalIgnoreCase)))
			{
				result.Add(Path.GetFileName(kvp.Key));
			}
		}
	}

	private static void ScanIconAssetsFromMetadata(HashSet<string> result, string wsPath)
	{
		if (GameContext.MetadataService.TryLoadMetadata(wsPath, out var iconMetaRoot) && iconMetaRoot?.Icons != null)
		{
			foreach (var kvp in iconMetaRoot.Icons)
			{
				if (kvp.Key.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase))
				{
					result.Add(Path.GetFileName(kvp.Key));
				}
			}
		}
	}

	private static void ScanNoiseAssets(HashSet<string> result, MapManifestAssets? assetsObj, MapManifest? manifest, string wsPath)
	{
		ScanRtexAssetsFromCategory(result, assetsObj?.GetCategory("Noise"));
		ScanRtexAssetsFromCategory(result, manifest?.Assets?.Noise);
		ScanRtexAssetsFromDirectory(result, Path.Combine(wsPath, "Assets", "noise"));
	}

	private static void ScanTerrainAssets(HashSet<string> result, MapManifestAssets? assetsObj, MapManifest? manifest, string wsPath)
	{
		ScanRtexAssetsFromCategory(result, assetsObj?.GetCategory("Terrain") ?? assetsObj?.GetCategory("textures"));
		ScanRtexAssetsFromCategory(result, manifest?.Assets?.Terrain);
		ScanTerrainAssetsFromManifestFiles(result, manifest);
		ScanRtexAssetsFromDirectory(result, Path.Combine(wsPath, "Assets", "textures"));
		ScanRtexAssetsFromDirectory(result, Path.Combine(ProjectSettings.GlobalizePath("res://"), "Assets", "textures"));
	}

	private static void ScanTerrainAssetsFromManifestFiles(HashSet<string> result, MapManifest? manifest)
	{
		if (manifest?.Files == null) return;

		foreach (var kvp in manifest.Files)
		{
			if (IsRtexAsset(kvp.Key) &&
			    (kvp.Key.StartsWith("Assets/textures/", StringComparison.OrdinalIgnoreCase) ||
			     kvp.Key.StartsWith("assets/textures/", StringComparison.OrdinalIgnoreCase) ||
			     kvp.Key.StartsWith("textures/", StringComparison.OrdinalIgnoreCase)))
			{
				result.Add(Path.GetFileName(kvp.Key));
			}
		}
	}
}
