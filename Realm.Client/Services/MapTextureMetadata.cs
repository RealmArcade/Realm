using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json.Nodes;
using Realm.Client.Core;
using Realm.Client.Utils;
using Realm.Shared;
using Realm.Shared.Distribution;
using Realm.Shared.Metadata;
using Realm.Shared.Services;
using Realm.Shared.Textures;

namespace Realm.Client.Services;

/// <summary>
///     Keeping a workspace's manifest.json texture table and its metadata in step with the files on disk:
///     plain hash strings are turned into entries, every entry gets a swatch slot and a luminance scale
///     factor, and the model and animation hashes are rewritten when an asset is reimported.
/// </summary>
public static class MapTextureMetadata
{
	public static float ExtractRtexScaleFactor(string rtexPath)
	{
		if (string.IsNullOrEmpty(rtexPath) || !File.Exists(rtexPath)) return 1.0f;

		try
		{
			string? rtexMeta = RealmMetadataHelper.ExtractMetadata(rtexPath);
			if (string.IsNullOrEmpty(rtexMeta)) return 1.0f;

			var rNode = JsonNode.Parse(rtexMeta);
			if (rNode is not JsonObject rObj) return 1.0f;

			var keys = new[] { "Scale_Factor", "scale_factor", "scaleFactor", "ScaleFactor" };
			foreach (var key in keys)
			{
				if (rObj.TryGetPropertyValue(key, out var sfVal) &&
					float.TryParse(sfVal?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out float parsedScale))
				{
					return Math.Clamp(parsedScale, 0.10f, 4.0f);
				}
			}
		}
		catch
		{
		}

		return 1.0f;
	}

	public static bool NormalizeTextureEntries(JsonObject root, string? wsPath = null)
	{
		if (root == null || !(root["textures"] is JsonObject texturesObj)) return false;

		bool modified = false;
		modified |= ConvertSimpleHashesToObjects(texturesObj);
		modified |= EnsureUniqueSwatchIndices(texturesObj);
		modified |= EnsureScaleFactors(texturesObj, wsPath);

		return modified;
	}

	private static bool ConvertSimpleHashesToObjects(JsonObject texturesObj)
	{
		bool modified = false;
		var entriesToConvert = new List<(string Key, string Hash)>();

		foreach (var kvp in texturesObj)
		{
			if (kvp.Value is JsonValue val && val.TryGetValue<string>(out string? hashStr) && hashStr != null)
				entriesToConvert.Add((kvp.Key, hashStr));
		}

		foreach (var (k, h) in entriesToConvert)
		{
			texturesObj[k] = new JsonObject { ["hash"] = h };
			modified = true;
		}

		return modified;
	}

	private static void GatherUsedSwatchIndices(JsonObject texturesObj, HashSet<int> usedIndices)
	{
		foreach (var kvp in texturesObj)
		{
			if (kvp.Value is JsonObject texObj &&
				texObj.TryGetPropertyValue("swatchIndex", out var sIdxNode) &&
				sIdxNode != null && int.TryParse(sIdxNode.ToString(), out int parsedIdx) && parsedIdx >= 0)
			{
				usedIndices.Add(parsedIdx);
			}
		}
	}

	private static bool AssignMissingSwatchIndices(JsonObject texturesObj, HashSet<int> usedIndices)
	{
		bool modified = false;
		int nextAvailable = 0;
		foreach (var kvp in texturesObj)
		{
			if (kvp.Value is not JsonObject texObj) continue;

			bool hasValidIdx = texObj.TryGetPropertyValue("swatchIndex", out var sIdxNode) &&
							   sIdxNode != null && int.TryParse(sIdxNode.ToString(), out int parsedIdx) && parsedIdx >= 0;

			if (hasValidIdx) continue;

			while (usedIndices.Contains(nextAvailable))
				nextAvailable++;

			texObj["swatchIndex"] = nextAvailable;
			usedIndices.Add(nextAvailable);
			modified = true;
		}

		return modified;
	}

	private static bool EnsureUniqueSwatchIndices(JsonObject texturesObj)
	{
		var usedIndices = new HashSet<int>();
		GatherUsedSwatchIndices(texturesObj, usedIndices);
		return AssignMissingSwatchIndices(texturesObj, usedIndices);
	}

	private static bool EnsureScaleFactors(JsonObject texturesObj, string? wsPath)
	{
		bool modified = false;
		foreach (var kvp in texturesObj)
		{
			if (kvp.Value is not JsonObject texObj) continue;

			bool hasValidSf = texObj.TryGetPropertyValue("Scale_Factor", out var sfNode) &&
							  sfNode != null && float.TryParse(sfNode.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out float parsedSf) && parsedSf > 0.0001f;

			if (!hasValidSf)
			{
				float sf = CalculateTextureScaleFactor(kvp.Key, wsPath);
				texObj["Scale_Factor"] = sf;
				modified = true;
			}
		}

		return modified;
	}

	private static float CalculateTextureScaleFactor(string fileName, string? wsPath)
	{
		string rtexFileName = fileName.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase) ? fileName : fileName + ".rtex";
		string? rtexPath = FindRtexPath(rtexFileName, wsPath);

		if (string.IsNullOrEmpty(rtexPath) || !File.Exists(rtexPath)) return 1.0f;

		float rtexSf = ExtractRtexScaleFactor(rtexPath);
		if (rtexSf > 0.0001f && MathF.Abs(rtexSf - 1.0f) > 0.001f)
			return rtexSf;

		float calcSf = TextureConverter.CalculateLuminanceScaleFactor(rtexPath);
		return calcSf > 0.0001f ? calcSf : 1.0f;
	}

	private static string? FindRtexPath(string rtexFileName, string? wsPath)
	{
		if (!string.IsNullOrEmpty(wsPath))
		{
			string p1 = Path.Combine(wsPath, "Assets", "textures", rtexFileName);
			if (File.Exists(p1)) return p1;

			string p2 = Path.Combine(wsPath, rtexFileName);
			if (File.Exists(p2)) return p2;
		}

		string? found = PathUtils.FindPath($"Assets/textures/{rtexFileName}");
		if (!string.IsNullOrEmpty(found)) return found;

		return PathUtils.FindPath($"MapTemplate/Assets/textures/{rtexFileName}");
	}

	public static void NormalizeTextureEntries(MapManifestAssets assets, string workspacePath)
	{
		if (assets == null || string.IsNullOrEmpty(workspacePath)) return;
		MapAssetHelper.SaveAssetsToManifest(workspacePath, assets, removeFromMetadata: true);
	}

	public static void NormalizeMetadataTextureEntries(string workspacePath)
	{
		if (string.IsNullOrEmpty(workspacePath) || !Directory.Exists(workspacePath)) return;

		try
		{
			var assets = MapAssetHelper.LoadAssets(workspacePath);
			NormalizeTextureEntries(assets, workspacePath);
		}
		catch (Exception ex)
		{
			GameLog.LogError($"[MapTextureMetadata] NormalizeMetadataTextureEntries error: {ex.Message}");
		}
	}

	public static void UpdateMetadataGlbHash(string workspacePath, string fileName, string newHash)
	{
		try
		{
			string category = MapAssetHelper.NormalizeCategoryKey("Prop");
			MapAssetHelper.UpdateManifestAsset(workspacePath, category, fileName, newHash);
		}
		catch (Exception ex)
		{
			GameLog.LogError($"[MapTextureMetadata] Failed to update manifest.json for {fileName}: {ex.Message}");
		}
	}

	public static void UpdateMetadataAnimationHash(string workspacePath, string animFileName, string newHash)
	{
		try
		{
			MapAssetHelper.UpdateManifestAsset(workspacePath, "animations", animFileName, newHash);
		}
		catch (Exception ex)
		{
			GameLog.LogError($"[MapTextureMetadata] Failed to update manifest.json for {animFileName}: {ex.Message}");
		}
	}

	public static void DeduplicateWorkspaceBuildArtifacts(string workspacePath)
	{
		MapArchiveHelper.DeduplicateWorkspaceBuildArtifacts(workspacePath);
	}
}
