using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Text.Json;
using Godot;
using Realm.Client.Core;
using Realm.Client.Services;
using Realm.Shared.Metadata;
using Realm.Shared.Distribution;

namespace Realm.Client.Network;

public static class MapTransferHelper
{
	public static void HandleReceiveMapManifest(TaskCompletionSource<string>? tcs, string manifestJson)
	{
		tcs?.TrySetResult(manifestJson);
	}

	public static void HandleRequestMapManifest(Node rpcNode, int senderId, string targetMap, string receiveRpcMethodName)
	{
		var manifest = MapAssetManager.FindHostManifest(targetMap);
		if (manifest != null)
		{
			string json = JsonSerializer.Serialize(manifest);
			Callable.From(() => rpcNode.RpcId(senderId, receiveRpcMethodName, targetMap, manifest.Version ?? "1.0.0", json)).CallDeferred();
		}
		else
		{
			Callable.From(() => rpcNode.RpcId(senderId, receiveRpcMethodName, targetMap, "", "")).CallDeferred();
		}
	}

	public static void HandleBeginMapTransfer(ClientTransferSession? currentTransfer, string transferId, string mapName, string mapVersion, long totalBytes, int totalChunks, Action<string> acknowledgeCompleteAction)
	{
		if (currentTransfer != null && currentTransfer.TransferId == transferId)
		{
			currentTransfer.LastActivityTimeUtc = DateTime.UtcNow;
			currentTransfer.ExpectedTotalBytes = totalBytes;
			currentTransfer.ExpectedTotalChunks = totalChunks;
			currentTransfer.CurrentChunkIndex = 0;
			currentTransfer.ReceivedPacketsInCurrentChunk = 0;
			currentTransfer.CurrentChunkStream.SetLength(0);
			currentTransfer.CurrentChunkStream.Position = 0;

			if (totalChunks == 0)
			{
				string targetMapDir = MapAssetManager.GetMapDirectory(mapName, mapVersion);
				MapAssetManager.ExtractManifestFiles(currentTransfer.Manifest, targetMapDir);
				string localManifestPath = Path.Combine(targetMapDir, "manifest.json");
				GameContext.AssetIndexService.RegisterManifest(currentTransfer.Manifest, localManifestPath);

				acknowledgeCompleteAction?.Invoke(transferId);
			}
		}
	}

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

	public static List<HostAssetTransferItem> GatherItemsToPack(string[] missingHashes, Dictionary<string, string> localFileMap)
	{
		var itemsToPack = new List<HostAssetTransferItem>(missingHashes.Length);
		foreach (var hash in missingHashes)
		{
			string norm = ContentAddressableStorage.NormalizeBlake3Hash(hash);
			string? filePath = MapAssetManager.Storage.FindAssetFilePath(norm) ?? MapAssetManager.P2PStorage.FindAssetFilePath(norm);
			string? meta = null;

			if (filePath != null && File.Exists(filePath))
			{
				meta = MapAssetManager.Storage.GetAssetMetadata(norm);
			}
			else if (localFileMap.TryGetValue(norm, out var localPath) && File.Exists(localPath))
			{
				filePath = localPath;
			}

			if (filePath != null && File.Exists(filePath))
			{
				long size = new FileInfo(filePath).Length;
				itemsToPack.Add(new HostAssetTransferItem
				{
					AssetKey = hash,
					SourceFilePath = filePath,
					Metadata = meta,
					Size = size
				});
			}
		}

		return itemsToPack;
	}

	public static List<List<HostAssetTransferItem>> ChunkTransferItems(List<HostAssetTransferItem> itemsToPack)
	{
		var chunkList = new List<List<HostAssetTransferItem>>();
		var currentChunk = new List<HostAssetTransferItem>();
		long currentChunkBytes = 0;

		foreach (var item in itemsToPack)
		{
			long estimated = item.Size + (item.AssetKey?.Length ?? 0) * 2 + (item.Metadata?.Length ?? 0) * 2 + 16;
			if (currentChunk.Count > 0 && currentChunkBytes + estimated > ZstdAssetBundleHelper.MaxBundleChunkSize)
			{
				chunkList.Add(currentChunk);
				currentChunk = new List<HostAssetTransferItem>();
				currentChunkBytes = 0;
			}

			currentChunk.Add(item);
			currentChunkBytes += estimated;
		}

		if (currentChunk.Count > 0)
		{
			chunkList.Add(currentChunk);
		}

		return chunkList;
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

	public static Task<byte[]> CompressChunkAsync(HostTransferSession session, int chunkIndex)
	{
		return Task.Run(() =>
		{
			var chunkItems = session.ChunkItems[chunkIndex];
			var chunkAssets = new System.Collections.Generic.List<(string AssetKey, byte[] Data, string? Metadata)>(chunkItems.Count);
			foreach (var item in chunkItems)
			{
				if (session.Cts.IsCancellationRequested) break;
				if (System.IO.File.Exists(item.SourceFilePath))
				{
					byte[] data = System.IO.File.ReadAllBytes(item.SourceFilePath);
					chunkAssets.Add((item.AssetKey, data, item.Metadata));
				}
			}

			return ZstdAssetBundleHelper.CreateBundleBytes(chunkAssets, 1);
		}, session.Cts.Token);
	}

	public static bool HandleBeginMapTransfer(Godot.Node context, int senderId, string transferId, ref int activeEphemeralTransferPeerId)
	{
		if (PeerSeederManager.Instance != null && PeerSeederManager.Instance.IsSeeding)
		{
			if (activeEphemeralTransferPeerId != 0 && activeEphemeralTransferPeerId != senderId)
			{
				Godot.GD.Print($"[{context.GetType().Name}] Rejecting map transfer {transferId} from peer {senderId}: Seeder busy with peer {activeEphemeralTransferPeerId}.");
				return false;
			}

			activeEphemeralTransferPeerId = senderId;
		}
		return true;
	}
}
