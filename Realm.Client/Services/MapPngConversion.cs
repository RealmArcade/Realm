using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Realm.Client.Core;
using Realm.Shared.Metadata;
using Realm.Shared.Services;
using Realm.Shared.Textures;

namespace Realm.Client.Services;

/// <summary>
///     Turning a workspace's .png assets into the .rtex textures the game loads. Each png's asset type and
///     sprite grid are recovered from its embedded metadata, then the map assets, then the vfx table, and
///     finally from where the file sits in the Assets tree; the converted texture replaces the png and the
///     map manifest and metadata are updated to point at it. Only used by the workspace save paths.
/// </summary>
internal static class MapPngConversion
{
	internal static void EnsurePngAssetsConverted(string workspacePath)
	{
		if (string.IsNullOrEmpty(workspacePath) || !Directory.Exists(workspacePath)) return;

		string assetsDir = Path.Combine(workspacePath, "Assets");
		if (!Directory.Exists(assetsDir)) return;

		try
		{
			string[] pngFiles = Directory.GetFiles(assetsDir, "*.png", SearchOption.AllDirectories);
			if (pngFiles.Length == 0) return;

			GameContext.MetadataService.TryLoadMetadata(workspacePath, out var metadataRoot);

			foreach (string pngPath in pngFiles)
			{
				if (MapWorkspacePaths.IsBuildOutputPath(pngPath)) continue;
				ProcessPngAsset(pngPath, workspacePath, metadataRoot);
			}
		}
		catch (Exception ex)
		{
			GameLog.LogError($"[MapPngConversion] EnsurePngAssetsConverted error: {ex.Message}");
		}
	}


	internal static async Task EnsurePngAssetsConvertedCooperativeAsync(
		string workspacePath,
		Func<int, int, string, Task>? onTextureProgress = null)
	{
		if (string.IsNullOrEmpty(workspacePath) || !Directory.Exists(workspacePath)) return;

		string assetsDir = Path.Combine(workspacePath, "Assets");
		if (!Directory.Exists(assetsDir)) return;

		try
		{
			string[] pngFiles = Directory.GetFiles(assetsDir, "*.png", SearchOption.AllDirectories);
			if (pngFiles.Length == 0) return;

			var validPngs = pngFiles.Where(f => !MapWorkspacePaths.IsBuildOutputPath(f)).ToList();
			if (validPngs.Count == 0) return;

			GameContext.MetadataService.TryLoadMetadata(workspacePath, out var metadataRoot);

			for (int i = 0; i < validPngs.Count; i++)
			{
				string pngPath = validPngs[i];

				if (onTextureProgress != null)
				{
					try
					{
						await onTextureProgress(i + 1, validPngs.Count, Path.GetFileName(pngPath));
					}
					catch (Exception ex)
					{
						GameLog.LogError($"[MapPngConversion] Progress callback error: {ex.Message}");
					}
				}

				ProcessPngAsset(pngPath, workspacePath, metadataRoot, i, validPngs.Count);
			}
		}
		catch (Exception ex)
		{
			GameLog.LogError($"[MapPngConversion] EnsurePngAssetsConvertedCooperativeAsync error: {ex.Message}");
		}
	}

	private static (string AssetType, int Columns, int Rows) DetectPngAssetInfo(string workspacePath, string pngPath, MapMetadata? metadata = null)
	{
		if (TryDetectFromEmbeddedMetadata(pngPath, out var embeddedInfo))
			return embeddedInfo;

		string fileName = Path.GetFileName(pngPath);
		string cleanName = Path.GetFileNameWithoutExtension(pngPath);

		if (TryDetectFromMapAssets(workspacePath, fileName, cleanName, metadata, out var mapAssetInfo))
			return mapAssetInfo;

		return DetectFromPathHeuristics(pngPath);
	}

	private static int GetJsonIntOrDefault(JsonObject node, string key, int defaultValue)
	{
		if (node.TryGetPropertyValue(key, out var valNode) && valNode != null)
		{
			return valNode.GetValue<int>();
		}

		return defaultValue;
	}

	private static bool TryDetectFromEmbeddedMetadata(string pngPath, out (string AssetType, int Columns, int Rows) info)
	{
		info = default;
		string? metaJson = RealmMetadataHelper.ExtractMetadata(pngPath);
		if (string.IsNullOrEmpty(metaJson)) return false;

		try
		{
			if (JsonNode.Parse(metaJson) is not JsonObject node) return false;

			if (!node.TryGetPropertyValue("type", out var typeNode)) return false;
			if (typeNode == null) return false;

			string? type = typeNode.GetValue<string>();
			if (string.IsNullOrEmpty(type)) return false;

			bool isDecal = type.Contains("decal", StringComparison.OrdinalIgnoreCase);
			int defaultGrid = 4;
			if (isDecal)
			{
				defaultGrid = 1;
			}

			int cols = GetJsonIntOrDefault(node, "columns", defaultGrid);
			int rows = GetJsonIntOrDefault(node, "rows", defaultGrid);

			info = (type, cols, rows);
			return true;
		}
		catch
		{
			return false;
		}
	}

	private static bool ContainsAssetKey(Dictionary<string, string>? dict, string fileName, string cleanName)
	{
		if (dict == null) return false;

		if (dict.ContainsKey(fileName) || dict.ContainsKey($"{cleanName}.rtex")) return true;

		return dict.Any(kvp =>
			kvp.Key.EndsWith("/" + fileName, StringComparison.OrdinalIgnoreCase) ||
			kvp.Key.EndsWith($"/{cleanName}.rtex", StringComparison.OrdinalIgnoreCase));
	}

	private static readonly (string Category, string Type)[] _assetCategoryTypes = new[]
	{
		("Icon", "icon"),
		("Skybox", "skybox"),
		("Terrain", "terrain_texture"),
		("Ribbon", "ribbon_texture"),
		("Noise", "noise_texture")
	};

	private static bool TryDetectFromMapAssets(string workspacePath, string fileName, string cleanName, MapMetadata? metadata, out (string AssetType, int Columns, int Rows) info)
	{
		info = default;
		var assetsObj = MapAssetHelper.LoadAssets(workspacePath);
		if (assetsObj == null) return false;

		var loadedMetadata = metadata ?? MapFileService.LoadMetadata(workspacePath);
		if (loadedMetadata?.Decals != null && (loadedMetadata.Decals.ContainsKey(fileName) || loadedMetadata.Decals.ContainsKey($"{cleanName}.rtex")))
		{
			info = ("decal", 1, 1);
			return true;
		}

		foreach (var (category, type) in _assetCategoryTypes)
		{
			if (ContainsAssetKey(assetsObj.GetCategory(category), fileName, cleanName))
			{
				info = (type, 4, 4);
				return true;
			}
		}

		if (TryDetectFromVfxMetadata(loadedMetadata, fileName, cleanName, out info)) return true;

		return false;
	}

	private static bool TryDetectFromVfxMetadata(MapMetadata? metadata, string fileName, string cleanName, out (string AssetType, int Columns, int Rows) info)
	{
		info = default;
		if (metadata?.VfxSpritesheets == null) return false;

		VfxMetadata? vmeta = null;
		if (metadata.VfxSpritesheets.TryGetValue(fileName, out var v1)) vmeta = v1;
		else if (metadata.VfxSpritesheets.TryGetValue($"{cleanName}.rtex", out var v2)) vmeta = v2;

		if (vmeta == null) return false;

		int cols = vmeta.Columns > 0 ? vmeta.Columns : 4;
		int rows = vmeta.Rows > 0 ? vmeta.Rows : 4;
		info = ("vfx_spritesheet", cols, rows);
		return true;
	}

	private static (string AssetType, int Columns, int Rows) DetectFromPathHeuristics(string pngPath)
	{
		string normalized = pngPath.Replace("\\", "/").ToLowerInvariant();

		if (normalized.Contains("/assets/decals/")) return ("decal", 1, 1);
		if (normalized.Contains("/assets/icons/")) return ("icon", 4, 4);
		if (normalized.Contains("/assets/skyboxes/")) return ("skybox", 4, 4);
		if (normalized.Contains("/assets/vfx/")) return ("vfx_spritesheet", 4, 4);
		if (normalized.Contains("/assets/ribbons/")) return ("ribbon_texture", 4, 4);
		if (normalized.Contains("/assets/noise/")) return ("noise_texture", 4, 4);

		return ("terrain_texture", 4, 4);
	}

	public static void UpdateMetadataConvertedTexture(
		string workspacePath,
		string pngFileName,
		string rtexFileName,
		string assetType,
		string newHash,
		int columns = 4,
		int rows = 4)
	{
		try
		{
			string categoryKey = MapAssetHelper.NormalizeCategoryKey(assetType);
			MapAssetHelper.UpdateManifestAsset(workspacePath, categoryKey, rtexFileName, newHash);
			if (!string.Equals(pngFileName, rtexFileName, StringComparison.OrdinalIgnoreCase))
			{
				MapAssetHelper.RemoveManifestAsset(workspacePath, categoryKey, pngFileName);
			}

			if (string.Equals(categoryKey, "Spritesheet", StringComparison.OrdinalIgnoreCase))
			{
				string slug = TemplateIDHelper.GenerateSlug(rtexFileName);
				string spritesheetTemplateId = TemplateIDHelper.NormalizeTemplateID("spritesheet", slug);
				GameContext.MetadataService.UpdateMetadata(workspacePath, m =>
				{
					m.VfxSpritesheets ??= new(StringComparer.OrdinalIgnoreCase);
					m.VfxSpritesheets[spritesheetTemplateId] = new VfxMetadata
					{
						TexturePath = rtexFileName,
						Columns = columns,
						Rows = rows
					};
				});
			}
			else if (string.Equals(categoryKey, "Terrain", StringComparison.OrdinalIgnoreCase))
			{
				string slug = TemplateIDHelper.GenerateSlug(rtexFileName);
				string terrainTemplateId = TemplateIDHelper.NormalizeTemplateID("terrain", slug);
				GameContext.MetadataService.UpdateMetadata(workspacePath, m =>
				{
					m.Textures ??= new(StringComparer.OrdinalIgnoreCase);
					if (!m.Textures.TryGetValue(terrainTemplateId, out var tex))
					{
						tex = new TextureMetadata();
						m.Textures[terrainTemplateId] = tex;
					}

					tex.TexturePath = rtexFileName;
					if (tex.ScaleFactor <= 0.0001f)
					{
						string fullRtexPath = Path.Combine(workspacePath, "Assets", "textures", rtexFileName);
						float sf = TextureConverter.CalculateLuminanceScaleFactor(fullRtexPath);
						tex.ScaleFactor = sf <= 0.0001f ? 1.0f : sf;
					}
				});
			}
		}
		catch (Exception ex)
		{
			GameLog.LogError($"[MapPngConversion] Failed to update metadata for converted texture {pngFileName}: {ex.Message}");
		}
	}


	private static void ProcessPngAsset(string pngPath, string workspacePath, MapMetadata? metadataRoot, int? index = null, int? totalFiles = null)
	{
		string pngFileName = Path.GetFileName(pngPath);
		string rtexFileName = Path.ChangeExtension(pngFileName, ".rtex");
		string rtexPath = Path.ChangeExtension(pngPath, ".rtex");

		var (assetType, columns, rows) = DetectPngAssetInfo(workspacePath, pngPath, metadataRoot);

		string progressStr = index.HasValue && totalFiles.HasValue ? $" ({index.Value + 1}/{totalFiles.Value})" : "";
		GameLog.LogToConsole($"[MapPngConversion] Converting PNG asset '{pngFileName}'{progressStr} (type: {assetType}) to .rtex...");

		var convResult = TextureConverter.ConvertTextureFile(pngPath, rtexPath, assetType, columns, rows);

		if (convResult.Success && File.Exists(rtexPath))
		{
			FinalizePngConversion(pngPath, rtexPath, workspacePath, pngFileName, rtexFileName, assetType, columns, rows);
		}
		else
		{
			GameLog.LogError($"[MapPngConversion] Failed to convert PNG '{pngFileName}': {convResult.ErrorMessage}");
		}
	}

	private static void FinalizePngConversion(string pngPath, string rtexPath, string workspacePath, string pngFileName, string rtexFileName, string assetType, int columns, int rows)
	{
		byte[] rtexBytes = File.ReadAllBytes(rtexPath);
		string newHash = RealmMetadataHelper.ComputeBlake3(rtexBytes, ".rtex");

		UpdateMetadataConvertedTexture(workspacePath, pngFileName, rtexFileName, assetType, newHash, columns, rows);

		try
		{
			var attrs = File.GetAttributes(pngPath);
			if ((attrs & FileAttributes.ReadOnly) != 0)
				File.SetAttributes(pngPath, attrs & ~FileAttributes.ReadOnly);

			File.Delete(pngPath);
			GameLog.LogToConsole($"[MapPngConversion] Successfully converted '{pngFileName}' to '{rtexFileName}'.");
		}
		catch (Exception ex)
		{
			GameLog.LogError($"[MapPngConversion] Failed to delete PNG {pngPath}: {ex.Message}");
		}
	}
}
