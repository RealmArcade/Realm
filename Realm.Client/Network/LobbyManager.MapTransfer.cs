using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using Realm.Client.Core;
using Realm.Client.Services;
using Realm.Client.UI;
using Realm.Client.Utils;
using Realm.Shared;
using Realm.Shared.Accounts;
using Realm.Shared.Distribution;
using Realm.Shared.Maps;
using Realm.Shared.Metadata;
using Realm.Shared.Network;
using Realm.Shared.Services;
using Environment = System.Environment;
using FileAccess = System.IO.FileAccess;
using HttpClient = System.Net.Http.HttpClient;

namespace Realm.Client.Network;
public partial class LobbyManager
{
	public async Task<bool> EnsureMapDownloadedAsync(string? mapName = null, string? version = null)
	{
		string targetMap = !string.IsNullOrWhiteSpace(mapName) ? mapName : ActiveMapName;
		string targetVersion = !string.IsNullOrWhiteSpace(version) ? version : ActiveMapVersion;
		if (string.IsNullOrWhiteSpace(targetMap) || IsHost)
		{
			ReportLocalMapReadyState(true);
			return true;
		}

		if (MapAssetManager.IsMapDownloaded(targetMap, targetVersion))
		{
		_downloadingMapName = null;
		CallDeferred(nameof(EmitDownloadCompleted));
		StartMatchPreparationForCurrentMap();
		return true;
	}

		if (_downloadingMapName == targetMap) return false;

		if (_lobbySession == null && _isConnectedToHost) return await TryDownloadMapFromHostAsync(targetMap);
		if (_lobbySession == null) return false;

		_downloadingMapName = targetMap;

		await _mapDownloadLock.WaitAsync();
		try
		{
			ReportLocalMapReadyState(false);
			CallDeferred(nameof(EmitDownloadProgress), 0.0f);

			MapTransferReply? manifestReply = await RequestMapTransferAsync(new MapTransferRequest
			{
				Kind = MapTransferRequestKind.Manifest,
				TransferId = Guid.NewGuid().ToString("N"),
				MapName = targetMap,
				MissingHashes = Array.Empty<string>(),
				ChunkIndex = 0
			});

			if (manifestReply is not { Kind: MapTransferReplyKind.Manifest } servedManifest || string.IsNullOrEmpty(servedManifest.ManifestJson))
			{
				GameLog.LogError($"[LobbyManager] The lobby did not serve a manifest for '{targetMap}': {manifestReply?.Refusal}");
				CallDeferred(nameof(EmitDownloadFailed));
				return false;
			}

			MapManifest? manifest = MapManifest.LoadFromJson(servedManifest.ManifestJson);
			if (manifest == null)
			{
				CallDeferred(nameof(EmitDownloadFailed));
				return false;
			}

			string transferId = Guid.NewGuid().ToString("N");
			List<string> allHashes = manifest.Files.Values.ToList();
			int totalAssets = allHashes.Count;
			if (totalAssets == 0)
			{
				GameLog.LogError($"[LobbyManager] The lobby served a manifest for '{targetMap}' that lists no files.");
				_downloadingMapName = null;
				CallDeferred(nameof(EmitDownloadFailed));
				return false;
			}

			List<string> missing = await Task.Run(() => MapAssetManager.GetMissingHashes(allHashes));

			int chunkIndex = 0;
			int totalChunks = 1;
			while (missing.Count > 0 && chunkIndex < totalChunks)
			{
				MapTransferReply? chunkReply = await RequestMapTransferAsync(new MapTransferRequest
				{
					Kind = MapTransferRequestKind.Chunk,
					TransferId = transferId,
					MapName = targetMap,
					MissingHashes = missing.ToArray(),
					ChunkIndex = chunkIndex
				}, 60000);

				if (chunkReply is not { Kind: MapTransferReplyKind.Chunk } servedChunk || servedChunk.Bundle == null)
				{
					GameLog.LogError($"[LobbyManager] The lobby stopped serving '{targetMap}' at bundle {chunkIndex}: {chunkReply?.Refusal}");
					CallDeferred(nameof(EmitDownloadFailed));
					return false;
				}

				totalChunks = Math.Max(1, servedChunk.TotalChunks);
				List<(string AssetKey, byte[] Data, string? Metadata)> assets = await Task.Run(() => ZstdAssetBundleHelper.ExtractBundleBytes(servedChunk.Bundle));
				await Task.Run(() =>
				{
					foreach (var (assetKey, data, metadata) in assets)
					{
						MapAssetManager.Storage.StoreAsset(data, Path.GetExtension(assetKey), metadata, precomputedBlake3: assetKey);
					}
				});

				chunkIndex++;
				float progress = totalChunks > 0 ? Math.Clamp((float)chunkIndex / totalChunks, 0.0f, 1.0f) : 0.0f;
				CallDeferred(nameof(EmitDownloadProgress), progress);
				if (totalAssets > 0 && assets.Count == 0) break;
			}

			string targetDirectory = MapAssetManager.GetMapDirectory(targetMap, targetVersion);
			await Task.Run(() =>
			{
				MapAssetManager.ExtractManifestFiles(manifest, targetDirectory);
				GameContext.AssetIndexService.RegisterManifest(manifest, Path.Combine(targetDirectory, "manifest.json"));
			});

		_downloadingMapName = null;
		CallDeferred(nameof(EmitDownloadCompleted));
		StartMatchPreparationForCurrentMap();
		return true;
	}
		catch (Exception ex)
		{
			GameLog.LogError($"[LobbyManager] Map download failed for '{targetMap}': {ex.Message}");
			_downloadingMapName = null;
			CallDeferred(nameof(EmitDownloadFailed));
			return false;
		}
		finally
		{
			_mapDownloadLock.Release();
		}
	}

	private void OnMapTransferReplied(MapTransferReply reply) => _transferReplyTcs?.TrySetResult(reply);

	private async Task<MapTransferReply?> RequestMapTransferAsync(MapTransferRequest request, int timeoutMilliseconds = 20000)
	{
		ClientServerSession? session = _lobbySession;
		if (session == null) return null;

		var reply = new TaskCompletionSource<MapTransferReply>(TaskCreationOptions.RunContinuationsAsynchronously);
		_transferReplyTcs = reply;
		try
		{
			if (!session.RequestMapTransfer(request)) return null;

			var completed = await Task.WhenAny(reply.Task, Task.Delay(timeoutMilliseconds));
			return completed == reply.Task ? reply.Task.Result : null;
		}
		finally
		{
			if (ReferenceEquals(_transferReplyTcs, reply)) _transferReplyTcs = null;
		}
	}

	private int _reportedMapProgressPercent = -1;

	private void EmitDownloadProgress(float progress)
	{
		MapDownloadProgressChanged?.Invoke(progress);
		ReportLocalMapProgress((int)MathF.Round(Math.Clamp(progress, 0f, 1f) * 100f));
	}

	private void EmitDownloadCompleted()
	{
		ReportLocalMapReadyState(true);
		MapDownloadProgressChanged?.Invoke(1.0f);
		MapDownloadCompleted?.Invoke();
	}

	private void EmitDownloadFailed()
	{
		MapDownloadFailed?.Invoke();
	}

}
