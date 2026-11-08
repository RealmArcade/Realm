using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Realm.Client.UI.MapEditor;
using Realm.EditorAPI;
using Realm.Shared;
using Realm.Shared.Distribution;
using Realm.Shared.Metadata;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace Realm.Client.Services;

/// <summary>
///     Turns one file on disk into an index record: reads its sidecar metadata, derives the asset type, computes or
///     carries its hash and assembles its tags. Shared by the directory scan and by manifest registration, so both
///     record the same file the same way. Every member is a pure function of the path, the file and its metadata; none
///     of them reads a collection or takes a lock, and the callers keep their own lock for the write that follows.
/// </summary>
internal static class AssetRecordFactory
{

	internal static bool IsIgnoredFileExtension(string extension)
	{
		return extension == ".cache" || extension == ".log" || extension == ".tmp" || extension == ".uid";
	}


	internal static bool IsMetadataSidecarFile(string normalizedFilePath)
	{
		return normalizedFilePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase) &&
		       File.Exists(normalizedFilePath.Substring(0, normalizedFilePath.Length - 5));
	}


	internal static AssetType? GetExpectedAssetTypeFromDirectory(string normalizedFilePath)
	{
		string? dir = Path.GetDirectoryName(normalizedFilePath);
		if (string.IsNullOrEmpty(dir)) return null;

		string[] dirParts = dir.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
		foreach (var part in dirParts.Reverse())
		{
			var norm = RealmMetadataHelper.NormalizeAssetType(part);
			if (norm.HasValue && RealmMetadataHelper.IsValidAssetTypeForExtension(normalizedFilePath, norm.Value, out _))
			{
				return norm;
			}
		}

		return null;
	}


	internal static bool IsExistingAssetUpToDate(IndexedAsset? existingAsset, FileInfo fileInfo, AssetType? expectedDirType, string normalizedFilePath)
	{
		if (existingAsset == null) return false;
		if (existingAsset.FileSizeBytes != fileInfo.Length) return false;
		if (existingAsset.LastModifiedUtc != fileInfo.LastWriteTimeUtc) return false;
		if (!IsAssetMetadataValid(existingAsset, expectedDirType)) return false;

		EnqueueThumbnailRequestIfNeeded(existingAsset.Extension, normalizedFilePath, existingAsset.Blake3);

		return true;
	}


	internal static bool IsAssetMetadataValid(IndexedAsset existingAsset, AssetType? expectedDirType)
	{
		if (string.IsNullOrEmpty(existingAsset.Blake3)) return false;
		if (!existingAsset.AssetType.HasValue) return false;
		if (existingAsset.ChromaKey == null) return false;
		if (expectedDirType.HasValue && existingAsset.AssetType != expectedDirType) return false;

		return true;
	}


	internal static IndexedAsset GenerateIndexedAssetFromFile(string normalizedFilePath, string normalizedDirectoryPath, FileInfo fileInfo, string extension, AssetType? expectedDirType, IndexedAsset? existingAsset)
	{
		string? metaJson = RealmMetadataHelper.ExtractMetadata(normalizedFilePath);
		var parsedMeta = ExtractParsedMetadata(metaJson, normalizedFilePath);
		var tags = LoadTagsForFile(normalizedFilePath, normalizedDirectoryPath, parsedMeta.Tags);

		var asset = existingAsset ?? new IndexedAsset();
		PopulateBasicAssetData(asset, normalizedFilePath, normalizedDirectoryPath, fileInfo, extension, tags, existingAsset);
		PopulateAssetMetadata(asset, normalizedFilePath, expectedDirType, metaJson, parsedMeta, tags);

		return asset;
	}


	internal static void PopulateBasicAssetData(IndexedAsset asset, string normalizedFilePath, string normalizedDirectoryPath, FileInfo fileInfo, string extension, List<string> tags, IndexedAsset? existingAsset)
	{
		asset.FilePath = normalizedFilePath;
		asset.FileName = Path.GetFileName(normalizedFilePath);
		asset.Extension = extension;
		asset.DirectoryPath = normalizedDirectoryPath;
		asset.FileSizeBytes = fileInfo.Length;
		asset.LastModifiedUtc = fileInfo.LastWriteTimeUtc;
		asset.Tags = tags;
		asset.MetadataJson = JsonSerializer.Serialize(new AssetMetadataModel { Tags = tags });
		asset.MapName = existingAsset?.MapName ?? asset.MapName;
		asset.MapVersion = existingAsset?.MapVersion ?? asset.MapVersion;
	}


	internal static void PopulateAssetMetadata(IndexedAsset asset, string normalizedFilePath, AssetType? expectedDirType, string? metaJson, (AssetType? AssetType, string? Blake3, List<string> Tags, bool? HasMask, string? ChromaKey) parsedMeta, List<string> tags)
	{
		AssetType? assetType = DetermineAssetType(parsedMeta.AssetType, expectedDirType, normalizedFilePath, tags);
		string normBlake3 = GetOrComputeBlake3(parsedMeta.Blake3, normalizedFilePath);

		asset.HasRealmMetadata = !string.IsNullOrEmpty(metaJson) || assetType.HasValue;
		asset.AssetType = assetType;
		asset.HasPlayerColorMask = parsedMeta.HasMask ?? DetermineHasPlayerColorMask(metaJson, normalizedFilePath);

		string? extractedChroma = RealmMetadataHelper.ExtractChromaKeyFromMetadataJson(metaJson) ?? RealmMetadataHelper.ExtractChromaKey(normalizedFilePath);
		asset.ChromaKey = parsedMeta.ChromaKey ?? extractedChroma ?? string.Empty;
		asset.Blake3 = !string.IsNullOrEmpty(normBlake3) ? ContentAddressableStorage.NormalizeBlake3Hash(normBlake3) : string.Empty;
	}


	internal static (AssetType? AssetType, string? Blake3, List<string> Tags, bool? HasMask, string? ChromaKey) ExtractParsedMetadata(string? metaJson, string normalizedFilePath)
	{
		if (string.IsNullOrEmpty(metaJson))
		{
			return (null, null, new List<string>(), null, null);
		}

		return ExtractParsedMetadataFromJson(metaJson, normalizedFilePath);
	}


	internal static (AssetType? AssetType, string? Blake3, List<string> Tags, bool? HasMask, string? ChromaKey) ExtractParsedMetadataFromJson(string metaJson, string normalizedFilePath)
	{
		try
		{
			var node = JsonNode.Parse(metaJson);
			if (node is JsonObject obj)
			{
				AssetType? assetType = ExtractAssetTypeFromJson(obj, normalizedFilePath);
				string? normBlake3 = obj.TryGetPropertyValue(AssetMetadataKeys.Blake3Hash, out var blake3Node) ? blake3Node?.ToString() : null;
				var embeddedTags = ExtractTagsFromJson(obj);

				return (assetType, normBlake3, embeddedTags, null, null);
			}
		}
		catch
		{
		}

		return (null, null, new List<string>(), null, null);
	}


	internal static AssetType? ExtractAssetTypeFromJson(JsonObject obj, string normalizedFilePath)
	{
		string? typeVal = obj[AssetMetadataKeys.AssetType]?.ToString();
		if (!string.IsNullOrEmpty(typeVal))
		{
			var norm = RealmMetadataHelper.NormalizeAssetType(typeVal);
			if (norm.HasValue && RealmMetadataHelper.IsValidAssetTypeForExtension(normalizedFilePath, norm.Value, out _))
			{
				return norm;
			}
		}

		return null;
	}


	internal static List<string> ExtractTagsFromJson(JsonObject obj)
	{
		var embeddedTags = new List<string>();
		if (obj.TryGetPropertyValue("tags", out var tagsNode) && tagsNode is JsonArray arr)
		{
			foreach (var item in arr)
			{
				string? t = item?.ToString()?.Trim();
				if (!string.IsNullOrEmpty(t) && !embeddedTags.Contains(t, StringComparer.OrdinalIgnoreCase))
				{
					embeddedTags.Add(t);
				}
			}
		}

		return embeddedTags;
	}


	internal static AssetType? DetermineAssetType(AssetType? parsedType, AssetType? expectedDirType, string normalizedFilePath, List<string> tags)
	{
		if (parsedType.HasValue) return parsedType;
		if (expectedDirType.HasValue) return expectedDirType;

		if (tags != null)
		{
			foreach (var tag in tags)
			{
				var norm = RealmMetadataHelper.NormalizeAssetType(tag);
				if (norm.HasValue && RealmMetadataHelper.IsValidAssetTypeForExtension(normalizedFilePath, norm.Value, out _))
				{
					return norm;
				}
			}
		}

		return null;
	}


	internal static string GetOrComputeBlake3(string? parsedBlake3, string normalizedFilePath)
	{
		if (!string.IsNullOrEmpty(parsedBlake3)) return parsedBlake3;
		try
		{
			return RealmMetadataHelper.ComputeBlake3(normalizedFilePath);
		}
		catch
		{
			return string.Empty;
		}
	}


	internal static ParsedAssetMetadata ParseMetadataHeaders(string? metaJson, string filePath)
	{
		var result = new ParsedAssetMetadata { Tags = new List<string>() };
		if (string.IsNullOrWhiteSpace(metaJson)) return result;

		try
		{
			if (JsonNode.Parse(metaJson) is JsonObject obj)
			{
				result.AssetType = ExtractAssetTypeFromMetadata(obj, filePath);
				result.SupportsTeamColor = ExtractSupportsTeamColor(obj);
				result.ChromaKey = ExtractChromaKeyFromMetadata(obj);
				ExtractTagsFromMetadata(obj, result.Tags);
			}
		}
		catch
		{
		}

		return result;
	}


	internal static AssetType? ExtractAssetTypeFromMetadata(JsonObject obj, string filePath)
	{
		string? typeVal = obj[AssetMetadataKeys.AssetType]?.ToString();
		if (!string.IsNullOrEmpty(typeVal))
		{
			var norm = RealmMetadataHelper.NormalizeAssetType(typeVal);
			if (norm.HasValue && RealmMetadataHelper.IsValidAssetTypeForExtension(filePath, norm.Value, out _))
			{
				return norm;
			}
		}

		return null;
	}


	internal static bool? ExtractSupportsTeamColor(JsonObject obj)
	{
		if (obj["team_color"] != null && bool.TryParse(obj["team_color"]?.ToString(), out bool tcVal)) return tcVal;
		if (obj["supports_team_color"] != null && bool.TryParse(obj["supports_team_color"]?.ToString(), out bool stcVal)) return stcVal;
		if (obj["has_player_color_mask"] != null && bool.TryParse(obj["has_player_color_mask"]?.ToString(), out bool hpcmVal)) return hpcmVal;
		return null;
	}


	internal static string? ExtractChromaKeyFromMetadata(JsonObject obj)
	{
		return obj["chroma_key"]?.ToString() ?? obj["chromaKey"]?.ToString() ?? obj["target_hex"]?.ToString() ?? obj["targetHex"]?.ToString();
	}


	internal static void ExtractTagsFromMetadata(JsonObject obj, List<string> resultTags)
	{
		if (obj["tags"] is JsonArray arr)
		{
			foreach (var item in arr)
			{
				string? t = item?.ToString()?.Trim();
				if (!string.IsNullOrEmpty(t) && !resultTags.Contains(t, StringComparer.OrdinalIgnoreCase))
				{
					resultTags.Add(t);
				}
			}
		}
	}


	internal static bool DetermineHasPlayerColorMask(string? metaJson, string? filePath)
	{
		if (!string.IsNullOrEmpty(metaJson) && RealmMetadataHelper.ExtractSupportsTeamColorFromMetadataJson(metaJson))
		{
			return true;
		}

		if (!string.IsNullOrEmpty(filePath))
		{
			bool? supports = RealmMetadataHelper.ExtractSupportsTeamColorWithMetadata(metaJson, filePath);
			if (supports.HasValue)
			{
				return supports.Value;
			}
		}

		return false;
	}


	internal static List<string> LoadTagsForFile(string filePath, string rootDirectory, List<string>? preloadedTags = null)
	{
		var tagSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		PopulateInitialTags(filePath, preloadedTags, tagSet);

		if (tagSet.Count == 0 && !filePath.StartsWith(AssetIndexService.GlobalCasAssetsDirectory, StringComparison.OrdinalIgnoreCase))
		{
			TryLoadTagsFromSidecar(filePath, tagSet);
		}

		if (tagSet.Count == 0)
		{
			GenerateFallbackTags(filePath, rootDirectory, tagSet);
		}

		return tagSet.OrderBy(t => t, StringComparer.OrdinalIgnoreCase).ToList();
	}


	internal static void PopulateInitialTags(string filePath, List<string>? preloadedTags, HashSet<string> tagSet)
	{
		if (preloadedTags != null && preloadedTags.Count > 0)
		{
			foreach (var tag in preloadedTags) tagSet.Add(tag);
		}
		else
		{
			var embeddedTags = RealmMetadataHelper.ExtractTags(filePath);
			foreach (var tag in embeddedTags) tagSet.Add(tag);
		}
	}


	internal static void TryLoadTagsFromSidecar(string filePath, HashSet<string> tagSet)
	{
		string sidecarJsonWithExt = filePath + ".json";
		string sidecarJsonNoExt = Path.Combine(Path.GetDirectoryName(filePath)!, Path.GetFileNameWithoutExtension(filePath) + ".json");

		string? foundMetadataPath = null;
		if (File.Exists(sidecarJsonWithExt)) foundMetadataPath = sidecarJsonWithExt;
		else if (File.Exists(sidecarJsonNoExt) && !string.Equals(sidecarJsonNoExt, filePath, StringComparison.OrdinalIgnoreCase)) foundMetadataPath = sidecarJsonNoExt;

		if (foundMetadataPath != null)
		{
			ParseTagsFromSidecarJson(foundMetadataPath, tagSet);
		}
	}


	internal static void ParseTagsFromSidecarJson(string foundMetadataPath, HashSet<string> tagSet)
	{
		try
		{
			string jsonContent = File.ReadAllText(foundMetadataPath);
			if (JsonNode.Parse(jsonContent) is JsonObject jsonObject && jsonObject["tags"] is JsonArray tagsArray)
			{
				foreach (var item in tagsArray)
				{
					string? tagStr = item?.ToString()?.Trim();
					if (!string.IsNullOrEmpty(tagStr)) tagSet.Add(tagStr);
				}
			}
		}
		catch
		{
		}
	}


	internal static void GenerateFallbackTags(string filePath, string rootDirectory, HashSet<string> tagSet)
	{
		string nameWithoutExt = Path.GetFileNameWithoutExtension(filePath);
		var nameTokens = nameWithoutExt.Split(new[] { '_', '-', ' ', '.', '@' }, StringSplitOptions.RemoveEmptyEntries);
		foreach (var token in nameTokens)
		{
			if (token.Length > 1 && !char.IsDigit(token[0])) tagSet.Add(token.ToLowerInvariant());
		}

		string relativeDir = Path.GetRelativePath(rootDirectory, Path.GetDirectoryName(filePath) ?? rootDirectory);
		if (!string.IsNullOrEmpty(relativeDir) && relativeDir != ".")
		{
			var dirTokens = relativeDir.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
			foreach (var dir in dirTokens)
			{
				if (!string.IsNullOrWhiteSpace(dir)) tagSet.Add(dir.ToLowerInvariant());
			}
		}

		string ext = Path.GetExtension(filePath).TrimStart('.').ToLowerInvariant();
		if (!string.IsNullOrEmpty(ext)) tagSet.Add(ext);
	}


	internal static void EnqueueThumbnailRequestIfNeeded(string extension, string normPath, string? norm)
	{
		if (extension == ".rmesh" && !GlbThumbnailRenderer.HasDiskCache(normPath, norm))
		{
			GlbThumbnailRenderer.EnqueueRequest(normPath, new FileInfo(normPath).LastWriteTimeUtc, norm, isHighPriority: false);
		}
	}
}
