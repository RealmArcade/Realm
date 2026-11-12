using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Godot;
using Realm.Client.Animation;
using Realm.Client.Core;
using Realm.Client.Services;
using Realm.EditorAPI;
using Realm.Shared.Audio;
using Realm.Shared.Textures;
using Realm.Client.Utils;

namespace Realm.Client.UI.MapEditor;

public partial class AssetManagerDialog
{
	public void RefreshAssetList()
	{
		string wsPath = GetWorkspacePath();
		_matchingAssets.Clear();

		string manifestPath = Path.Combine(wsPath, "manifest.json");
		if (!File.Exists(manifestPath))
		{
			_lblAssetCount.Text = "0 " + TranslationServer.Translate("assets");
			_lblEmptyState.Visible = true;
			UpdateVirtualGridSize(_matchingAssets.Count);
			UpdateVisibleGridCells();
			UpdateSelectedAssetDisplay();
			return;
		}

		try
		{
			string manifestJson = File.ReadAllText(manifestPath);
			if (!string.IsNullOrWhiteSpace(manifestJson))
			{
				ProcessManifestAssets(manifestJson, wsPath);
			}
		}
		catch (Exception ex)
		{
			GameLog.LogError($"[AssetManagerDialog] RefreshAssetList error: {ex.Message}");
		}

		_matchingAssets.Sort((a, b) => string.Compare(a.FileName, b.FileName, StringComparison.OrdinalIgnoreCase));

		_lblAssetCount.Text = $"{_matchingAssets.Count} " + TranslationServer.Translate("assets");
		_lblEmptyState.Visible = _matchingAssets.Count == 0;

		if (_selectedAsset != null && !_matchingAssets.Any(a => string.Equals(a.FilePath, _selectedAsset.FilePath, StringComparison.OrdinalIgnoreCase)))
		{
			_selectedAsset = null;
		}

		UpdateVirtualGridSize(_matchingAssets.Count);
		UpdateVisibleGridCells();
		UpdateSelectedAssetDisplay();
	}

	private void ProcessManifestAssets(string manifestJson, string wsPath)
	{
		var manifestDoc = JsonNode.Parse(manifestJson)?.AsObject();
		if (manifestDoc?["Assets"] is not JsonObject assetsObj) return;

		foreach (var categoryKvp in assetsObj)
		{
			if (categoryKvp.Value is JsonObject categoryDict)
			{
				ProcessCategoryItems(categoryDict, categoryKvp.Key, wsPath);
			}
		}
	}

	private void ProcessCategoryItems(JsonObject categoryDict, string categoryKey, string wsPath)
	{
		string groupKey = categoryKey.ToLowerInvariant();
		string normalizedCategory = MapAssetHelper.NormalizeCategoryKey(categoryKey);

		foreach (var itemKvp in categoryDict)
		{
			ProcessSingleAsset(itemKvp, groupKey, normalizedCategory, wsPath);
		}
	}

	private void ProcessSingleAsset(KeyValuePair<string, JsonNode?> itemKvp, string groupKey, string normalizedCategory, string wsPath)
	{
		string fileName = itemKvp.Key;
		string itemAssetType = DetermineItemAssetType(itemKvp, groupKey, normalizedCategory, fileName);

		if (_selectedAssetType != "All" && !string.Equals(_selectedAssetType, itemAssetType, StringComparison.OrdinalIgnoreCase))
		{
			return;
		}

		string fullPath = ResolveAssetFullPath(itemAssetType, groupKey, fileName, wsPath);
		var tags = ExtractAssetTags(fullPath);

		if (!MatchesSearchFilter(fileName, tags))
		{
			return;
		}

		string blake3 = itemKvp.Value?.ToString() ?? string.Empty;
		long fileSize = File.Exists(fullPath) ? new FileInfo(fullPath).Length : 0;
		DateTime lastModified = File.Exists(fullPath) ? File.GetLastWriteTimeUtc(fullPath) : DateTime.UtcNow;

		_matchingAssets.Add(new IndexedAsset
		{
			FilePath = fullPath,
			FileName = fileName,
			Extension = Path.GetExtension(fileName),
			FileSizeBytes = fileSize,
			LastModifiedUtc = lastModified,
			Blake3 = blake3,
			AssetType = RealmMetadataHelper.NormalizeAssetType(itemAssetType),
			Tags = tags
		});
	}

	private static readonly Dictionary<string, string> AssetSubFolders = new(StringComparer.OrdinalIgnoreCase)
	{
		{ "Character", "models/units" },
		{ "Building", "models/buildings" },
		{ "Prop", "models/props" },
		{ "Item", "models/items" },
		{ "Spritesheet", "vfx" },
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

	private string DetermineItemAssetType(KeyValuePair<string, JsonNode?> itemKvp, string groupKey, string normalizedCategory, string fileName)
	{
		if (IsAnimation(groupKey, fileName)) return "Animation";
		if (IsShader(groupKey, fileName)) return "Shader";

		string? extracted = ExtractAssetTypeFromJson(itemKvp.Value as JsonObject);
		if (!string.IsNullOrEmpty(extracted)) return extracted;

		if (IsValidCategory(normalizedCategory)) return normalizedCategory;

		return GetDefaultAssetTypeFromExtension(fileName);
	}

	private static bool IsAnimation(string groupKey, string fileName)
	{
		return groupKey == "ranim" || fileName.EndsWith(".ranim", StringComparison.OrdinalIgnoreCase);
	}

	private static bool IsShader(string groupKey, string fileName)
	{
		return groupKey == "shaders" || fileName.EndsWith(".gdshader", StringComparison.OrdinalIgnoreCase);
	}

	private static string? ExtractAssetTypeFromJson(JsonObject? itemObj)
	{
		if (itemObj == null) return null;
		string? assetType = itemObj["asset_type"]?.ToString();
		return !string.IsNullOrEmpty(assetType) ? assetType : itemObj["AssetType"]?.ToString();
	}

	private static bool IsValidCategory(string normalizedCategory)
	{
		if (string.IsNullOrEmpty(normalizedCategory)) return false;
		if (normalizedCategory.Equals("other", StringComparison.OrdinalIgnoreCase)) return false;
		return !normalizedCategory.Equals("assets", StringComparison.OrdinalIgnoreCase);
	}

	private static string GetDefaultAssetTypeFromExtension(string fileName)
	{
		return Path.GetExtension(fileName).ToLowerInvariant() switch
		{
			".rmesh" => "Prop",
			".rtex" => "Terrain",
			".raud" => "SoundEffect",
			".ranim" => "Animation",
			_ => "Prop"
		};
	}

	private string ResolveAssetFullPath(string itemAssetType, string groupKey, string fileName, string wsPath)
	{
		string subFolder = GetAssetSubFolder(itemAssetType, groupKey);
		string? fullPath = FindAssetOnDisk(itemAssetType, subFolder, fileName, wsPath);

		if (string.IsNullOrEmpty(fullPath) || !File.Exists(fullPath))
		{
			return Path.Combine(wsPath, "Assets", subFolder, fileName);
		}

		return fullPath;
	}

	private static string GetAssetSubFolder(string assetType, string fallbackGroupKey)
	{
		return AssetSubFolders.TryGetValue(assetType, out string? folder) ? folder : fallbackGroupKey;
	}

	private static string? FindAssetOnDisk(string itemAssetType, string subFolder, string fileName, string wsPath)
	{
		return itemAssetType is "Character" or "Building" or "Prop" or "Item"
			? MapAssetHelper.FindModelOnDisk(wsPath, subFolder, fileName)
			: MapAssetHelper.FindAssetOnDisk(wsPath, subFolder, fileName);
	}

	private List<string> ExtractAssetTags(string fullPath)
	{
		var tags = new List<string>();
		if (!File.Exists(fullPath)) return tags;

		string? metaJson = RealmMetadataHelper.ExtractMetadata(fullPath);
		if (string.IsNullOrEmpty(metaJson)) return tags;

		try
		{
			var metaObj = JsonNode.Parse(metaJson)?.AsObject();
			if (metaObj?["tags"] is JsonArray tArr)
			{
				foreach (var t in tArr)
				{
					if (t != null) tags.Add(t.ToString());
				}
			}
		}
		catch
		{
		}

		return tags;
	}

	private bool MatchesSearchFilter(string fileName, List<string> tags)
	{
		if (string.IsNullOrEmpty(_searchFilterText)) return true;

		bool matchName = fileName.IndexOf(_searchFilterText, StringComparison.OrdinalIgnoreCase) >= 0;
		bool matchTag = tags.Any(t => t.IndexOf(_searchFilterText, StringComparison.OrdinalIgnoreCase) >= 0);

		return matchName || matchTag;
	}

	public void RefreshAssetListAndSelect(string preferredAssetKey)
	{
		RefreshAssetList();
		if (!string.IsNullOrEmpty(preferredAssetKey))
		{
			string targetFileName = Path.GetFileName(preferredAssetKey);
			var target = _matchingAssets.FirstOrDefault(a => string.Equals(a.FileName, targetFileName, StringComparison.OrdinalIgnoreCase));
			if (target != null)
			{
				_selectedAsset = target;
				UpdateSelectedAssetDisplay();
				UpdateVisibleGridCells();
			}
		}
	}
}
