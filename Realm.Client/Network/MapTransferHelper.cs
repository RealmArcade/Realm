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
	public static void HandleReceiveMapManifestRpc(string manifestJson, System.Threading.Tasks.TaskCompletionSource<string>? manifestTcs)
	{
		manifestTcs?.TrySetResult(manifestJson);
	}
	
	public static void HandleRequestMapManifestRpc(string targetMap, System.Action<string, string, string> receiveMapManifestRpc)
	{
		var manifest = MapAssetManager.FindHostManifest(targetMap);
		if (manifest != null)
		{
			string json = System.Text.Json.JsonSerializer.Serialize(manifest);
			Godot.Callable.From(() => receiveMapManifestRpc(targetMap, manifest.Version ?? "1.0.0", json)).CallDeferred();
		}
		else
		{
			Godot.Callable.From(() => receiveMapManifestRpc(targetMap, "", "")).CallDeferred();
		}
	}

	public static void HandleRequestNextMapTransferChunkRpc(string transferId, int nextChunkIndex, System.Collections.Concurrent.ConcurrentDictionary<string, HostTransferSession> hostTransfers, System.Action<HostTransferSession, int> sendHostChunkAsync)
	{
		if (hostTransfers.TryGetValue(transferId, out var session) && !session.Cts.IsCancellationRequested)
		{
			session.CurrentChunkIndex = nextChunkIndex;
			sendHostChunkAsync?.Invoke(session, nextChunkIndex);
		}
	}

	public static void HandleBeginMapTransfer(ClientTransferSession transferSession, string mapName, string mapVersion, long totalBytes, int totalChunks, System.Action onCompleteCallback)
	{
		transferSession.LastActivityTimeUtc = System.DateTime.UtcNow;
		transferSession.ExpectedTotalBytes = totalBytes;
		transferSession.ExpectedTotalChunks = totalChunks;
		transferSession.CurrentChunkIndex = 0;
		transferSession.ReceivedPacketsInCurrentChunk = 0;
		transferSession.CurrentChunkStream.SetLength(0);
		transferSession.CurrentChunkStream.Position = 0;

		if (totalChunks == 0)
		{
			string targetMapDir = MapAssetManager.GetMapDirectory(mapName, mapVersion);
			MapAssetManager.ExtractManifestFiles(transferSession.Manifest, targetMapDir);
			string localManifestPath = System.IO.Path.Combine(targetMapDir, "manifest.json");
			Realm.Client.Services.GameContext.AssetIndexService.RegisterManifest(transferSession.Manifest, localManifestPath);

			onCompleteCallback?.Invoke();
		}
	}

	public static void HandleSendMapTransferChunk(
		ClientTransferSession transferSession,
		int chunkIndex,
		int totalChunks,
		int packetIndex,
		int totalPacketsInChunk,
		long totalBytes,
		byte[] packetData,
		System.Action<float> onProgressUpdated,
		System.Action onChunkEnded)
	{
		transferSession.LastActivityTimeUtc = System.DateTime.UtcNow;
		transferSession.ExpectedTotalChunks = totalChunks;
		transferSession.ExpectedTotalBytes = totalBytes;
		transferSession.CurrentChunkIndex = chunkIndex;

		transferSession.CurrentChunkStream.Write(packetData, 0, packetData.Length);
		transferSession.ReceivedPacketsInCurrentChunk++;
		transferSession.TotalReceivedBytes += packetData.Length;

		float chunkFraction = totalPacketsInChunk > 0 ? (float)(packetIndex + 1) / totalPacketsInChunk : 1.0f;
		float sessionFraction = totalChunks > 0
			? System.Math.Clamp(((float)chunkIndex + chunkFraction) / totalChunks, 0.0f, 1.0f)
			: 1.0f;

		float overallProgress = sessionFraction;
		if (transferSession.TotalAssetsInManifest > 0)
		{
			float downloadedAssetsInSession = sessionFraction * transferSession.SessionMissingAssets;
			overallProgress = System.Math.Clamp((transferSession.AlreadyPresentAssets + downloadedAssetsInSession) / transferSession.TotalAssetsInManifest, 0.0f, 1.0f);
		}

		bool isChunkEnd = packetIndex + 1 >= totalPacketsInChunk;
		long nowTicks = System.Environment.TickCount64;

		if (isChunkEnd || overallProgress >= 1.0f || System.Math.Abs(overallProgress - transferSession.LastEmittedProgress) >= 0.005f || (nowTicks - transferSession.LastProgressEmitTicks) >= 100)
		{
			transferSession.LastEmittedProgress = overallProgress;
			transferSession.LastProgressEmitTicks = nowTicks;
			onProgressUpdated?.Invoke(overallProgress);
		}

		if (isChunkEnd)
		{
			onChunkEnded?.Invoke();
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

	public static List<HostAssetTransferItem> CollectTransferItems(string[] missingHashes, Dictionary<string, string> localFileMap)
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
				itemsToPack.Add(new HostAssetTransferItem
				{
					AssetKey = hash,
					SourceFilePath = filePath,
					Metadata = meta,
					Size = new FileInfo(filePath).Length
				});
			}
			else
			{
				GD.PrintErr($"[MapTransferHelper] Host could not find asset payload for hash: {hash}");
			}
		}

		return itemsToPack;
	}
}
