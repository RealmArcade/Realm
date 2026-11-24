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
			_unreadTransferReplies.Clear();
			CallDeferred(nameof(EmitDownloadProgress), 0.0f);

			var transferClock = Stopwatch.StartNew();
			long transferredBytes = 0;

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
				_unreadTransferReplies.Clear();

				MapTransferReply? firstReply = await RequestMapTransferAsync(new MapTransferRequest
				{
					Kind = MapTransferRequestKind.Chunk,
					TransferId = transferId,
					MapName = targetMap,
					ChunkIndex = chunkIndex,
					PacketIndex = 0,
					PacketWindow = LobbyLimits.MaximumPiecesPerRequest
				}, 30000);

				if (firstReply is not { Kind: MapTransferReplyKind.Chunk } firstPiece)
				{
					GameLog.LogError($"[LobbyManager] The lobby stopped serving '{targetMap}' at bundle {chunkIndex}: {DescribeTransferFailure(firstReply)}");
					CallDeferred(nameof(EmitDownloadFailed));
					return false;
				}

				if (firstPiece.PacketCount <= 0)
				{
					CallDeferred(nameof(EmitDownloadProgress), Math.Clamp((float)(chunkIndex + 1) / totalChunks, 0.0f, 1.0f));
					continue;
				}

				int packetCount = firstPiece.PacketCount;
				var pieces = new byte[packetCount][];
				int receivedCount = 0;
				int awaiting = Math.Min(LobbyLimits.MaximumPiecesPerRequest, packetCount);

				if (firstPiece.Bundle is { Length: > 0 } firstBytes && firstPiece.PacketIndex >= 0 && firstPiece.PacketIndex < packetCount)
				{
					pieces[firstPiece.PacketIndex] = firstBytes;
					receivedCount = 1;
				}

				awaiting--;
				int attemptsWithoutProgress = 0;

				while (receivedCount < packetCount)
				{
					if (awaiting <= 0)
					{
						int firstMissing = IndexOfFirstMissingPiece(pieces, packetCount);
						if (firstMissing < 0) break;

						int window = Math.Min(LobbyLimits.MaximumPiecesPerRequest, packetCount - firstMissing);
						if (!SendMapTransferRequest(new MapTransferRequest
						{
							Kind = MapTransferRequestKind.Chunk,
							TransferId = transferId,
							MapName = targetMap,
							ChunkIndex = chunkIndex,
							PacketIndex = firstMissing,
							PacketWindow = window
						}))
						{
							GameLog.LogError($"[LobbyManager] This screen lost its lobby while '{targetMap}' was being sent.");
							CallDeferred(nameof(EmitDownloadFailed));
							return false;
						}

						awaiting = window;
					}

					MapTransferReply? pieceReply = await ReadMapTransferReplyAsync(20000);
					awaiting = Math.Max(0, awaiting - 1);

					if (pieceReply is not { Kind: MapTransferReplyKind.Chunk } arrivedPiece || arrivedPiece.Bundle is not { Length: > 0 } pieceBytes || arrivedPiece.ChunkIndex != chunkIndex)
					{
						if (++attemptsWithoutProgress > 3)
						{
							GameLog.LogError($"[LobbyManager] The lobby stopped serving '{targetMap}' at bundle {chunkIndex}: {DescribeTransferFailure(pieceReply)}");
							CallDeferred(nameof(EmitDownloadFailed));
							return false;
						}

						awaiting = 0;
						continue;
					}

					if (arrivedPiece.PacketIndex < 0 || arrivedPiece.PacketIndex >= packetCount) continue;
					if (pieces[arrivedPiece.PacketIndex] != null) continue;

					pieces[arrivedPiece.PacketIndex] = pieceBytes;
					receivedCount++;
					attemptsWithoutProgress = 0;
				}

				var orderedPieces = new List<byte[]>(packetCount);
				for (int pieceIndex = 0; pieceIndex < packetCount; pieceIndex++) orderedPieces.Add(pieces[pieceIndex]!);

				byte[] bundle = MapTransferPackets.Join(orderedPieces);
				transferredBytes += bundle.Length;
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

			// The rate is what tells a host whether a slow download is the line or this screen: it is the compressed bytes
			// that actually crossed the connection, over the wall clock the whole transfer took.
			double seconds = Math.Max(transferClock.Elapsed.TotalSeconds, 0.001);
			GameLog.LogToConsole($"[LobbyManager] '{targetMap}' arrived as {transferredBytes} byte(s) in {seconds:F1} s ({transferredBytes / seconds / 1024.0:F0} KB/s)");

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
	/// <remarks>
	///     The copy a join prepared is asked about first, because a map taken from a Workshop item is read where Steam put it
	///     and never enters the download archive. Asking the archive alone would have this screen wait for a download of the
	///     package it is already holding.
	/// </remarks>
	/// <param name="mapName">The map's path name.</param>
	/// <param name="mapVersion">The map's version, as the lobby states it.</param>
	/// <returns>Whether the advertised package is the one on this machine.</returns>
	private bool HoldsAdvertisedMapCopy(string mapName, string mapVersion)
	{
		string advertisedHash = SteamLobbyDirectory.GetData(SteamLobbyId, SteamLobbyDirectory.MapHashKey);

		string sessionDirectory = ActiveMapSessionDirectory;
		if (sessionDirectory.Length > 0 && Directory.Exists(sessionDirectory) &&
		    string.Equals(mapName, ActiveMapName, StringComparison.OrdinalIgnoreCase))
		{
			string sessionHash = MapSessionCopyService.ReadStatedPackageHash(sessionDirectory);
			if (advertisedHash.Length == 0 || sessionHash.Length == 0 || string.Equals(advertisedHash, sessionHash, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}

		if (!MapAssetManager.IsMapDownloaded(mapName, mapVersion)) return false;

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

	private static int IndexOfFirstMissingPiece(byte[]?[] pieces, int packetCount)
	{
		for (int pieceIndex = 0; pieceIndex < packetCount; pieceIndex++)
		{
			if (pieces[pieceIndex] == null) return pieceIndex;
		}

		return -1;
	}

	private static string DescribeTransferFailure(MapTransferReply? reply)
	{
		if (reply is not { } answered) return "the host did not answer in time";
		if (answered.Refusal.Length > 0) return answered.Refusal;

		return $"the host answered {answered.Kind} with nothing in it";
	}

	private void OnMapTransferReplied(MapTransferReply reply)
	{
		TaskCompletionSource<MapTransferReply>? waiting = _transferReplyTcs;
		if (waiting != null && waiting.TrySetResult(reply)) return;

		// One request can be answered with several pieces, and the screen is reading them one at a time, so a piece that
		// arrives with nobody waiting for it is held for the next read rather than dropped.
		_unreadTransferReplies.Enqueue(reply);
	}

	private bool SendMapTransferRequest(MapTransferRequest request)
	{
		ClientServerSession? session = _lobbySession;
		return session != null && session.RequestMapTransfer(request);
	}

	private async Task<MapTransferReply?> ReadMapTransferReplyAsync(int timeoutMilliseconds)
	{
		if (_unreadTransferReplies.Count > 0) return _unreadTransferReplies.Dequeue();

		var reply = new TaskCompletionSource<MapTransferReply>(TaskCreationOptions.RunContinuationsAsynchronously);
		_transferReplyTcs = reply;
		try
		{
			Task completed = await Task.WhenAny(reply.Task, Task.Delay(timeoutMilliseconds));
			return completed == reply.Task ? reply.Task.Result : null;
		}
		finally
		{
			if (ReferenceEquals(_transferReplyTcs, reply)) _transferReplyTcs = null;
		}
	}

	private async Task<MapTransferReply?> RequestMapTransferAsync(MapTransferRequest request, int timeoutMilliseconds = 20000)
	{
		if (!SendMapTransferRequest(request)) return null;

		return await ReadMapTransferReplyAsync(timeoutMilliseconds);
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
