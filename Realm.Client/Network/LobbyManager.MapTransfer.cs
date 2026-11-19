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
				ChunkIndex = 0
			});

			if (manifestReply is not { Kind: MapTransferReplyKind.Manifest } servedManifest || servedManifest.ManifestBytes is not { Length: > 0 } servedManifestBytes)
			{
				string reason = manifestReply is { } answered
					? answered.Refusal.Length > 0
						? answered.Refusal
						: $"the host answered {answered.Kind} with an empty manifest, so the folder it runs states none"
					: "the host did not answer in time";
				GameLog.LogError($"[LobbyManager] The lobby did not serve a manifest for '{targetMap}': {reason}");
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
			int totalChunks = servedManifest.TotalChunks;
			if (totalChunks <= 0)
			{
				GameLog.LogError($"[LobbyManager] The lobby served a manifest for '{targetMap}' that lists no files.");
				CallDeferred(nameof(EmitDownloadFailed));
				return false;
			}

			if (!await AnnounceHeldFilesAsync(targetMap, transferId, manifest)) return false;

			for (int chunkIndex = 0; chunkIndex < totalChunks; chunkIndex++)
			{
				var pieces = new List<byte[]>(2);
				bool nothingNeeded = false;
				int packetIndex = 0;
				int packetCount = 1;
				while (packetIndex < packetCount)
				{
					MapTransferReply? pieceReply = await RequestMapTransferAsync(new MapTransferRequest
					{
						Kind = MapTransferRequestKind.Chunk,
						TransferId = transferId,
						MapName = targetMap,
						ChunkIndex = chunkIndex,
						PacketIndex = packetIndex
					}, 60000);

					if (pieceReply is not { Kind: MapTransferReplyKind.Chunk } servedPiece)
					{
						GameLog.LogError($"[LobbyManager] The lobby stopped serving '{targetMap}' at bundle {chunkIndex} piece {packetIndex}: {(pieceReply is { } answeredPiece ? answeredPiece.Refusal.Length > 0 ? answeredPiece.Refusal : $"the host answered {answeredPiece.Kind}" : "the host did not answer in time")}");
						CallDeferred(nameof(EmitDownloadFailed));
						return false;
					}

					if (servedPiece.PacketCount <= 0)
					{
						nothingNeeded = true;
						break;
					}

					if (servedPiece.Bundle is not { Length: > 0 } pieceBytes)
					{
						GameLog.LogError($"[LobbyManager] The lobby sent an empty piece of '{targetMap}' at bundle {chunkIndex} piece {packetIndex}.");
						CallDeferred(nameof(EmitDownloadFailed));
						return false;
					}

					packetCount = Math.Max(1, servedPiece.PacketCount);
					pieces.Add(pieceBytes);
					packetIndex++;
				}

				if (nothingNeeded)
				{
					CallDeferred(nameof(EmitDownloadProgress), Math.Clamp((float)(chunkIndex + 1) / totalChunks, 0.0f, 1.0f));
					continue;
				}

				byte[] bundle = MapTransferPackets.Join(pieces);
				List<(string AssetKey, byte[] Data, string? Metadata)> assets = await Task.Run(() => ZstdAssetBundleHelper.ExtractBundleBytes(bundle));
				await Task.Run(() =>
				{
					foreach (var (assetKey, data, metadata) in assets)
					{
						MapAssetManager.Storage.StoreAsset(data, Path.GetExtension(assetKey), metadata, precomputedBlake3: assetKey);
					}
				});

				float progress = Math.Clamp((float)(chunkIndex + 1) / totalChunks, 0.0f, 1.0f);
				CallDeferred(nameof(EmitDownloadProgress), progress);
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

		CallDeferred(nameof(EmitDownloadCompleted));
		StartMatchPreparationForCurrentMap();
		return true;
	}
		catch (Exception ex)
		{
			GameLog.LogError($"[LobbyManager] Map download failed for '{targetMap}': {ex.Message}");
			CallDeferred(nameof(EmitDownloadFailed));
			return false;
		}
		finally
		{
			_downloadingMapName = null;
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

	/// <summary>
	///     Tells the lobby which files of the map this machine already holds, so its bundles carry only what is missing.
	/// </summary>
	/// <param name="targetMap">The map being transferred.</param>
	/// <param name="transferId">The transfer the announcement belongs to.</param>
	/// <param name="manifest">The manifest the lobby served.</param>
	/// <returns>Whether the lobby recorded the announcement.</returns>
	private async Task<bool> AnnounceHeldFilesAsync(string targetMap, string transferId, MapManifest manifest)
	{
		List<string> missing = await Task.Run(() => MapAssetManager.GetMissingHashes(manifest.Files.Values));
		List<string> held = MapTransferSelection.Held(manifest.Files.Values, missing);
		if (held.Count == 0) return true;

		for (int offset = 0; offset < held.Count; offset += LobbyLimits.MaximumHeldHashesPerAnnouncement)
		{
			int count = Math.Min(LobbyLimits.MaximumHeldHashesPerAnnouncement, held.Count - offset);
			MapTransferReply? reply = await RequestMapTransferAsync(new MapTransferRequest
			{
				Kind = MapTransferRequestKind.Held,
				TransferId = transferId,
				MapName = targetMap,
				ChunkIndex = 0,
				HeldHashes = held.GetRange(offset, count).ToArray()
			});

			if (reply is { Kind: MapTransferReplyKind.Held }) continue;

			GameLog.LogError($"[LobbyManager] The lobby did not record the files '{targetMap}' already holds: {(reply is { } answered ? answered.Refusal.Length > 0 ? answered.Refusal : $"the host answered {answered.Kind}" : "the host did not answer in time")}");
			CallDeferred(nameof(EmitDownloadFailed));
			return false;
		}

		return true;
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
