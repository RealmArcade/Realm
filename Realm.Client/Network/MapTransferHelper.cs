using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Godot;
using Realm.Client.Core;
using Realm.Client.Services;
using Realm.Shared.Metadata;
using Realm.Shared.Distribution;

namespace Realm.Client.Network;

public static class MapTransferHelper
{
	public static Dictionary<string, string> BuildLocalFileMap(string mapName, string mapVersion, MapManifest? manifest)
	{
		var localFileMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		string? manifestPath = MapAssetManager.FindManifestPath(mapName, mapVersion);

		if (string.IsNullOrEmpty(manifestPath) || !File.Exists(manifestPath) || manifest == null || manifest.Files == null)
		{
			return localFileMap;
		}

		string mapDir = Path.GetDirectoryName(manifestPath)!;
		foreach (var kvp in manifest.Files)
		{
			string normKvp = ContentAddressableStorage.NormalizeBlake3Hash(kvp.Value);
			string relPath = kvp.Key.Replace("res://", "").TrimStart('/', '\\');
			string localFilePath = Path.Combine(mapDir, relPath);
			if (File.Exists(localFilePath))
			{
				localFileMap[normKvp] = localFilePath;
			}
		}

		return localFileMap;
	}

	public static byte[] PopChunkBytes(ClientTransferSession session)
	{
		byte[] chunkBytes = session.CurrentChunkStream.ToArray();
		session.CurrentChunkStream.SetLength(0);
		session.CurrentChunkStream.Position = 0;
		session.ReceivedPacketsInCurrentChunk = 0;
		return chunkBytes;
	}

	public static async Task ProcessReceivedChunkAsync(
		ClientTransferSession transferSession, 
		int chunkIndex, 
		int totalChunks, 
		byte[] chunkBytes, 
		Action<int> requestNextChunkRpc, 
		Action acknowledgeCompleteRpc,
		TaskCompletionSource<bool>? transferCompleteTcs,
		string logPrefix)
	{
		try
		{
			var extractedAssets = await Task.Run(() => ZstdAssetBundleHelper.ExtractBundleBytes(chunkBytes));
			GD.Print($"[{logPrefix}] Decompressed chunk {chunkIndex + 1}/{totalChunks} ({extractedAssets.Count} assets) in memory.");
			transferSession.LastActivityTimeUtc = DateTime.UtcNow;

			await Task.Run(() =>
			{
				foreach (var (assetKey, data, metadata) in extractedAssets)
				{
					string ext = Path.GetExtension(assetKey);
					MapAssetManager.Storage.StoreAsset(data, ext, metadata, precomputedBlake3: assetKey);
				}
			});

			transferSession.LastActivityTimeUtc = DateTime.UtcNow;

			if (chunkIndex + 1 < totalChunks)
			{
				Callable.From(() => requestNextChunkRpc(chunkIndex + 1)).CallDeferred();
			}
			else
			{
				string targetMapDir = MapAssetManager.GetMapDirectory(transferSession.MapName, transferSession.MapVersion);
				await Task.Run(() =>
				{
					MapAssetManager.ExtractManifestFiles(transferSession.Manifest, targetMapDir);
					string localManifestPath = Path.Combine(targetMapDir, "manifest.json");
					GameContext.AssetIndexService.RegisterManifest(transferSession.Manifest, localManifestPath);
				});

				Callable.From(() => acknowledgeCompleteRpc()).CallDeferred();
				transferCompleteTcs?.TrySetResult(true);
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[{logPrefix}] Error extracting chunk {chunkIndex}: {ex.Message}");
			transferCompleteTcs?.TrySetException(ex);
		}
	}
}
