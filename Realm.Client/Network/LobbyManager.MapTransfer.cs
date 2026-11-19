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
using Realm.Shared.Distribution;
using Realm.Shared.Maps;
using Realm.Shared.Metadata;
using Realm.Shared.Network;
using Realm.Shared.Services;
using Environment = System.Environment;
using FileAccess = System.IO.FileAccess;

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

		if (HoldsAdvertisedMapCopy(targetMap, targetVersion))
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

			if (manifestReply is not { Kind: MapTransferReplyKind.Manifest } servedManifest || servedManifest.ManifestBytes is not { Length: > 0 } servedManifestBytes)
			{
				GameLog.LogError($"[LobbyManager] The lobby did not serve a manifest for '{targetMap}': {manifestReply?.Refusal}");
				CallDeferred(nameof(EmitDownloadFailed));
				return false;
			}

			MapManifest? manifest = MapManifest.LoadFromJson(Encoding.UTF8.GetString(servedManifestBytes));
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
				MapReferenceResolver.ClearReadOnlyRecursive(targetDirectory);
				MapAssetManager.ExtractManifestFiles(manifest, targetDirectory);
				// The host's manifest replaces whatever manifest was there, byte for byte, because that file is what makes
				// this copy the advertised package rather than one that only reads the same.
				File.WriteAllBytes(Path.Combine(targetDirectory, "manifest.json"), servedManifestBytes);
				GameContext.AssetIndexService.RegisterManifest(manifest, Path.Combine(targetDirectory, "manifest.json"));
				MapReferenceResolver.SetReadOnlyRecursive(targetDirectory);
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

	/// <summary>
	///     Whether this machine already holds the package the lobby advertises, which is what makes taking the host's copy
	///     unnecessary. A package is the map's own manifest, so holding a different version of the same map is a package
	///     this screen does not have and has to be given. A lobby that states no package hash says nothing to compare with,
	///     and whatever is installed is then what it plays.
	/// </summary>
	/// <param name="mapName">The map's path name.</param>
	/// <param name="mapVersion">The map's version, as the lobby states it.</param>
	/// <returns>Whether the advertised package is the one on this machine.</returns>
	private bool HoldsAdvertisedMapCopy(string mapName, string mapVersion)
	{
		if (!MapAssetManager.IsMapDownloaded(mapName, mapVersion)) return false;

		string advertisedHash = SteamLobbyDirectory.GetData(SteamLobbyId, SteamLobbyDirectory.MapHashKey);
		if (advertisedHash.Length == 0) return true;

		string? localDirectory = GameHost.ResolveMapDirectory(mapName, mapVersion);
		if (string.IsNullOrEmpty(localDirectory)) return false;

		string localHash = MapSessionCopyService.ReadStatedPackageHash(localDirectory);
		return localHash.Length == 0 || string.Equals(advertisedHash, localHash, StringComparison.OrdinalIgnoreCase);
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
