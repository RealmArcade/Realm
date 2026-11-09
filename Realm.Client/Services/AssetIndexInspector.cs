using System;
using System.IO;
using System.Text.Json.Nodes;
using Realm.Client.Network;
using Realm.EditorAPI;
using Realm.Shared;
using Realm.Shared.Distribution;

namespace Realm.Client.Services;

/// <summary>
///     Reads one indexed record back against the file it describes and answers why the two disagree, or nothing when
///     they agree. Every check is a pure function of the record, the file on disk and the CAS layout, so it takes no
///     lock and touches no index state: the verification passes that call it keep their own lock for the query they run
///     before inspecting, and that is unchanged by this class.
/// </summary>
public static class AssetIndexInspector
{
	public static string? Inspect(IndexedAsset asset, bool verifyContentHash)
	{
		if (string.IsNullOrEmpty(asset.FilePath) || !File.Exists(asset.FilePath))
		{
			return "file is missing on disk";
		}

		var fileInfo = new FileInfo(asset.FilePath);
		if (fileInfo.Length != asset.FileSizeBytes)
		{
			return $"recorded size {asset.FileSizeBytes} does not match {fileInfo.Length} byte(s) on disk";
		}

		string diskExtension = Path.GetExtension(asset.FilePath).ToLowerInvariant();
		if (!string.Equals(diskExtension, asset.Extension, StringComparison.OrdinalIgnoreCase))
		{
			return $"recorded extension '{asset.Extension}' does not match '{diskExtension}' on disk";
		}

		if (string.IsNullOrEmpty(asset.Blake3))
		{
			return "recorded hash is empty";
		}

		string recordedHash = ContentAddressableStorage.NormalizeBlake3Hash(asset.Blake3);
		if (IsCasAsset(asset))
		{
			string pathHash = ContentAddressableStorage.NormalizeBlake3Hash(Path.GetFileName(asset.FilePath));
			if (!string.Equals(pathHash, recordedHash, StringComparison.OrdinalIgnoreCase))
			{
				return $"recorded hash {recordedHash} does not match the hash {pathHash} encoded in its path";
			}
		}

		if (verifyContentHash)
		{
			string contentHash = ContentAddressableStorage.NormalizeBlake3Hash(RealmMetadataHelper.ComputeBlake3(asset.FilePath));
			if (!string.Equals(contentHash, recordedHash, StringComparison.OrdinalIgnoreCase))
			{
				return $"recorded hash {recordedHash} does not match the content hash {contentHash} of the file on disk";
			}
		}

		string? diskMetadataJson = RealmMetadataHelper.ExtractMetadata(asset.FilePath);

		AssetType? diskAssetType = RealmMetadataHelper.NormalizeAssetType(RealmMetadataHelper.ExtractAssetType(asset.FilePath));
		if (diskAssetType.HasValue)
		{
			if (diskAssetType.Value != asset.AssetType)
			{
				return $"recorded asset type '{asset.AssetType}' does not match '{diskAssetType.Value}' in the file metadata";
			}
		}
		else if (asset.AssetType.HasValue &&
		         RealmMetadataHelper.GetValidAssetTypesForExtension(asset.FilePath).Count > 0 &&
		         !RealmMetadataHelper.IsValidAssetTypeForExtension(asset.FilePath, asset.AssetType.Value, out _))
		{
			return $"recorded asset type '{asset.AssetType.Value}' is not valid for '{diskExtension}'";
		}

		return InspectEmbeddedHash(diskMetadataJson, recordedHash);
	}

	private static string? InspectEmbeddedHash(string? diskMetadataJson, string recordedHash)
	{
		if (string.IsNullOrEmpty(diskMetadataJson)) return null;

		try
		{
			if (JsonNode.Parse(diskMetadataJson) is JsonObject obj && obj.ContainsKey(AssetMetadataKeys.Blake3Hash))
			{
				string? embeddedHash = obj[AssetMetadataKeys.Blake3Hash]?.ToString();
				if (!string.IsNullOrEmpty(embeddedHash) &&
				    !string.Equals(ContentAddressableStorage.NormalizeBlake3Hash(embeddedHash), recordedHash, StringComparison.OrdinalIgnoreCase))
				{
					return $"recorded hash {recordedHash} does not match the embedded metadata hash {embeddedHash}";
				}
			}
		}
		catch
		{
			return "embedded metadata is not valid JSON";
		}

		return null;
	}

	public static bool IsCasAsset(IndexedAsset asset)
	{
		string p2pCasAssetsDirectory = P2PCasAssetsDirectory;
		if (string.Equals(asset.DirectoryPath, AssetIndexService.GlobalCasAssetsDirectory, StringComparison.OrdinalIgnoreCase)) return true;
		if (string.Equals(asset.DirectoryPath, p2pCasAssetsDirectory, StringComparison.OrdinalIgnoreCase)) return true;
		if (string.IsNullOrEmpty(asset.FilePath)) return false;

		string normalizedFilePath = AssetIndexPaths.Normalize(asset.FilePath);
		return normalizedFilePath.StartsWith(AssetIndexService.GlobalCasAssetsDirectory + "/", StringComparison.OrdinalIgnoreCase) ||
		       normalizedFilePath.StartsWith(p2pCasAssetsDirectory + "/", StringComparison.OrdinalIgnoreCase);
	}

	private static string P2PCasAssetsDirectory => AssetIndexPaths.Normalize(Path.Combine(MapAssetManager.P2PArchiveDirectory, "assets"));
}
