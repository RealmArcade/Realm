using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using LiteDB;
using Realm.Client.Network;
using Realm.Client.Utils;
using Realm.EditorAPI;
using Realm.Shared;
using Realm.Shared.Distribution;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace Realm.Client.Services;

/// <summary>
///     Changing the record of one indexed asset: retagging it or retyping it, writing the change back to the
///     file's metadata headers, and keeping the sidecar in step - through the CAS sidecar cache for a
///     content-addressed file, and through a plain .json next to the file otherwise.
/// </summary>
internal static class AssetIndexAssetWrites
{
	internal static void UpdateTags(ILiteCollection<IndexedAsset> assets, LiteDatabase database, object syncLock, string globalCasDirectory, string filePath, List<string> newTags)
	{
		if (string.IsNullOrWhiteSpace(filePath))
		{
			return;
		}

		string normalizedFilePath = AssetIndexPaths.Normalize(filePath);
		lock (syncLock)
		{
			var asset = assets.FindOne(x => x.FilePath == normalizedFilePath);
			if (asset == null)
			{
				return;
			}

			asset.Tags = newTags.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim().ToLowerInvariant()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
			asset.MetadataJson = JsonSerializer.Serialize(new AssetMetadataModel { Tags = asset.Tags });
			assets.Update(asset);

			RealmMetadataHelper.SetTags(normalizedFilePath, asset.Tags);

			bool isCasFile = normalizedFilePath.StartsWith(globalCasDirectory + "/", StringComparison.OrdinalIgnoreCase);
			if (isCasFile)
			{
				string hash = ContentAddressableStorage.NormalizeBlake3Hash(Path.GetFileName(normalizedFilePath));
				string? updatedMeta = RealmMetadataHelper.ExtractMetadata(normalizedFilePath);
				if (!string.IsNullOrWhiteSpace(updatedMeta))
				{
					MapAssetManager.Storage.UpdateSidecarCache(hash, updatedMeta);
				}
			}
			else
			{
				try
				{
					string sidecarPath = normalizedFilePath + ".json";
					var root = new JsonObject
					{
						["tags"] = new JsonArray(asset.Tags.Select(t => (JsonNode)JsonValue.Create(t)!).ToArray())
					};
					File.WriteAllText(sidecarPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
				}
				catch (Exception ex)
				{
					GameLog.LogError($"[AssetIndexService] UpdateAssetTags error writing sidecar: {ex.Message}");
				}
			}

			database.Checkpoint();
		}
	}

	internal static void UpdateType(ILiteCollection<IndexedAsset> assets, LiteDatabase database, object syncLock, string globalCasDirectory, string filePath, AssetType newAssetType)
	{
		if (string.IsNullOrWhiteSpace(filePath))
		{
			return;
		}

		string normalizedFilePath = AssetIndexPaths.Normalize(filePath);
		lock (syncLock)
		{
			var asset = assets.FindOne(x => x.FilePath == normalizedFilePath);
			if (asset == null)
			{
				return;
			}

			asset.AssetType = newAssetType;
			asset.HasRealmMetadata = true;
			assets.Update(asset);

			RealmMetadataHelper.SetAssetType(normalizedFilePath, newAssetType.ToCanonicalString());

			bool isCasFile = normalizedFilePath.StartsWith(globalCasDirectory + "/", StringComparison.OrdinalIgnoreCase);
			if (isCasFile)
			{
				string hash = ContentAddressableStorage.NormalizeBlake3Hash(Path.GetFileName(normalizedFilePath));
				string? updatedMeta = RealmMetadataHelper.ExtractMetadata(normalizedFilePath);
				if (!string.IsNullOrWhiteSpace(updatedMeta))
				{
					MapAssetManager.Storage.UpdateSidecarCache(hash, updatedMeta);
				}
			}

			database.Checkpoint();
		}
	}

	internal static void UpdateType(ILiteCollection<IndexedAsset> assets, LiteDatabase database, object syncLock, string globalCasDirectory, string filePath, string newAssetType)
	{
		var norm = RealmMetadataHelper.NormalizeAssetType(newAssetType);
		if (norm.HasValue)
		{
			UpdateType(assets, database, syncLock, globalCasDirectory, filePath, norm.Value);
		}
	}
}
