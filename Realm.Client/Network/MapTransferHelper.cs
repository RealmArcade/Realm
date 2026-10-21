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

	public static void CancelHostTransfersForPeer(int peerId, System.Collections.Concurrent.ConcurrentDictionary<string, HostTransferSession> hostTransfers)
	{
		foreach (var kvp in hostTransfers)
		{
			if (kvp.Value.PeerId == peerId)
			{
				kvp.Value.Cts.Cancel();
				hostTransfers.TryRemove(kvp.Key, out _);
			}
		}
	}

	public static float CalculateOverallTransferProgress(ClientTransferSession session, int chunkIndex, int totalChunks, int packetIndex, int totalPacketsInChunk)
	{
		float chunkFraction = totalPacketsInChunk > 0 ? (float)(packetIndex + 1) / totalPacketsInChunk : 1.0f;
		float sessionFraction = totalChunks > 0
			? Math.Clamp(((float)chunkIndex + chunkFraction) / totalChunks, 0.0f, 1.0f)
			: 1.0f;

		if (session.TotalAssetsInManifest > 0)
		{
			float downloadedAssetsInSession = sessionFraction * session.SessionMissingAssets;
			return Math.Clamp((session.AlreadyPresentAssets + downloadedAssetsInSession) / session.TotalAssetsInManifest, 0.0f, 1.0f);
		}

		return sessionFraction;
	}

	public static void EmitTransferProgressIfRequired(ClientTransferSession session, float overallProgress, bool isChunkEnd, Action<float> emitAction)
	{
		long nowTicks = System.Environment.TickCount64;
		if (isChunkEnd || overallProgress >= 1.0f || Math.Abs(overallProgress - session.LastEmittedProgress) >= 0.005f || (nowTicks - session.LastProgressEmitTicks) >= 100)
		{
			session.LastEmittedProgress = overallProgress;
			session.LastProgressEmitTicks = nowTicks;
			emitAction(overallProgress);
		}
	}

	public static void UpdateTransferState(ClientTransferSession session, int chunkIndex, int totalChunks, long totalBytes, byte[] packetData)
	{
		session.LastActivityTimeUtc = DateTime.UtcNow;
		session.ExpectedTotalChunks = totalChunks;
		session.ExpectedTotalBytes = totalBytes;
		session.CurrentChunkIndex = chunkIndex;

		session.CurrentChunkStream.Write(packetData, 0, packetData.Length);
		session.ReceivedPacketsInCurrentChunk++;
		session.TotalReceivedBytes += packetData.Length;
	}

	public static void HandleSendMapTransferChunkRpc(
		ClientTransferSession? session,
		string transferId,
		int chunkIndex,
		int totalChunks,
		int packetIndex,
		int totalPacketsInChunk,
		long totalBytes,
		byte[] packetData,
		Action<float> emitProgress,
		Action finalizeChunkAction,
		TaskCompletionSource<bool>? transferCompleteTcs,
		string logPrefix)
	{
		if (session == null || session.TransferId != transferId) return;

		try
		{
			UpdateTransferState(session, chunkIndex, totalChunks, totalBytes, packetData);

			float overallProgress = CalculateOverallTransferProgress(session, chunkIndex, totalChunks, packetIndex, totalPacketsInChunk);
			bool isChunkEnd = packetIndex + 1 >= totalPacketsInChunk;

			EmitTransferProgressIfRequired(session, overallProgress, isChunkEnd, emitProgress);

			if (isChunkEnd)
			{
				finalizeChunkAction();
			}
		}
		catch (Exception ex)
		{
			Godot.GD.PrintErr($"[{logPrefix}] Chunk write error: {ex.Message}");
			transferCompleteTcs?.TrySetException(ex);
		}
	}

	public static void HandleRequestNextMapTransferChunkRpc(
		string transferId, 
		int nextChunkIndex, 
		System.Collections.Concurrent.ConcurrentDictionary<string, HostTransferSession> hostTransfers, 
		Func<HostTransferSession, int, Task> sendHostChunkAsync)
	{
		if (hostTransfers.TryGetValue(transferId, out var session) && !session.Cts.IsCancellationRequested)
		{
			session.CurrentChunkIndex = nextChunkIndex;
			_ = sendHostChunkAsync(session, nextChunkIndex);
		}
	}

	public static void HandleBeginMapTransferRpc(ClientTransferSession? currentClientTransfer, string transferId, string mapName, string mapVersion, long totalBytes, int totalChunks, Node rpcNode, string acknowledgeCompleteRpcName, TaskCompletionSource<bool>? transferCompleteTcs)
	{
		HandleBeginMapTransfer(currentClientTransfer, transferId, mapName, mapVersion, totalBytes, totalChunks, tId =>
		{
			Callable.From(() => rpcNode.RpcId(1, acknowledgeCompleteRpcName, tId)).CallDeferred();
			transferCompleteTcs?.TrySetResult(true);
		});
	}

	public static void ProcessCompletedChunk(ClientTransferSession currentClientTransfer, int chunkIndex, int totalChunks, Node rpcNode, string requestNextChunkRpcName, string acknowledgeCompleteRpcName, TaskCompletionSource<bool>? transferCompleteTcs, string logPrefix)
	{
		byte[] chunkBytes = PopChunkBytes(currentClientTransfer);

		_ = ProcessReceivedChunkAsync(
			currentClientTransfer,
			chunkIndex,
			totalChunks,
			chunkBytes,
			nextChunkIndex => rpcNode.RpcId(1, requestNextChunkRpcName, currentClientTransfer.TransferId, nextChunkIndex),
			() => rpcNode.RpcId(1, acknowledgeCompleteRpcName, currentClientTransfer.TransferId),
			transferCompleteTcs,
			logPrefix
		);
	}

	public static void HandleSendMapTransferChunkRpcWrapper(ClientTransferSession? currentClientTransfer, string transferId, int chunkIndex, int totalChunks, int packetIndex, int totalPacketsInChunk, long totalBytes, byte[] packetData, Action<float> emitProgress, Node rpcNode, string requestNextChunkRpcName, string acknowledgeCompleteRpcName, TaskCompletionSource<bool>? transferCompleteTcs, string logPrefix)
	{
		HandleSendMapTransferChunkRpc(
			currentClientTransfer, transferId, chunkIndex, totalChunks, packetIndex, totalPacketsInChunk, totalBytes, packetData,
			emitProgress,
			() => ProcessCompletedChunk(currentClientTransfer!, chunkIndex, totalChunks, rpcNode, requestNextChunkRpcName, acknowledgeCompleteRpcName, transferCompleteTcs, logPrefix),
			transferCompleteTcs,
			logPrefix
		);
	}
}
