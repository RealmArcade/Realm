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
	private async Task<bool> TryDownloadMapFromHostAsync(string targetMap)
	{
		var manifest = await RequestManifestFromHostAsync(targetMap);
		if (manifest == null) return false;

		var allHashes = manifest.Files != null ? manifest.Files.Values.ToList() : new List<string>();
		int totalManifestFiles = allHashes.Count;
		int maxRetries = 10;
		int retryCount = 0;

		while (retryCount < maxRetries && _isConnectedToHost)
		{
			var missingHashes = await Task.Run(() => MapAssetManager.GetMissingHashes(allHashes));

			if (missingHashes.Count == 0)
			{
				await RegisterDownloadedMapAsync(targetMap, manifest);
				return true;
			}

			float initialProgress = totalManifestFiles > 0 ? Math.Clamp((float)(totalManifestFiles - missingHashes.Count) / totalManifestFiles, 0.0f, 1.0f) : 0.0f;
			CallDeferred(nameof(EmitDownloadProgress), initialProgress);

			bool transferSuccess = await PerformMapAssetTransferAsync(targetMap, manifest, missingHashes, totalManifestFiles);

			if (transferSuccess)
			{
				CallDeferred(nameof(EmitDownloadProgress), 1.0f);
				CallDeferred(nameof(EmitDownloadCompleted));
				return true;
			}

			if (!_isConnectedToHost) break;

			retryCount++;
			await Task.Delay(1000);
		}

		return false;
	}

	private async Task<MapManifest?> RequestManifestFromHostAsync(string targetMap)
	{
		_manifestTcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

		Callable.From(() => RpcId(1, nameof(RequestMapManifestRpc), targetMap)).CallDeferred();

		var manifestTask = await Task.WhenAny(_manifestTcs.Task, Task.Delay(10000));
		string? manifestJson = manifestTask == _manifestTcs.Task ? _manifestTcs.Task.Result : null;

		if (string.IsNullOrWhiteSpace(manifestJson)) return null;

		try
		{
			return MapManifest.LoadFromJson(manifestJson);
		}
		catch (Exception ex)
		{
			GameLog.LogError($"[LobbyManager] Failed to deserialize manifest from host: {ex.Message}");
			return null;
		}
	}

	private async Task RegisterDownloadedMapAsync(string targetMap, MapManifest manifest)
	{
		string targetMapDir = MapAssetManager.GetMapDirectory(targetMap, manifest.Version ?? "1.0.0");
		await Task.Run(() =>
		{
			MapAssetManager.ExtractManifestFiles(manifest, targetMapDir);
			string localManifestPath = Path.Combine(targetMapDir, "manifest.json");
			GameContext.AssetIndexService.RegisterManifest(manifest, localManifestPath);
		});

		CallDeferred(nameof(EmitDownloadProgress), 1.0f);
		CallDeferred(nameof(EmitDownloadCompleted));
	}

	private async Task<bool> PerformMapAssetTransferAsync(string targetMap, MapManifest manifest, List<string> missingHashes, int totalManifestFiles)
	{
		string transferId = Guid.NewGuid().ToString("N");

		_currentClientTransfer = new ClientTransferSession
		{
			TransferId = transferId,
			MapName = targetMap,
			MapVersion = manifest.Version ?? "1.0.0",
			Manifest = manifest,
			CurrentChunkStream = new MemoryStream(),
			TotalAssetsInManifest = totalManifestFiles,
			AlreadyPresentAssets = totalManifestFiles - missingHashes.Count,
			SessionMissingAssets = missingHashes.Count,
			LastActivityTimeUtc = DateTime.UtcNow
		};

		_transferCompleteTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

		Callable.From(() => RpcId(1, nameof(RequestMapAssetTransferRpc), transferId, targetMap, manifest.Version ?? "1.0.0", missingHashes.ToArray())).CallDeferred();

		bool transferSuccess = await MonitorTransferProgressAsync(_currentClientTransfer);

		if (!transferSuccess && _currentClientTransfer != null)
		{
			try
			{
				_currentClientTransfer.CurrentChunkStream.Dispose();
			}
			catch
			{
			}

			_currentClientTransfer = null;
		}

		if (transferSuccess) _currentClientTransfer = null;

		return transferSuccess;
	}

	private async Task<bool> MonitorTransferProgressAsync(ClientTransferSession session)
	{
		while (!_transferCompleteTcs!.Task.IsCompleted)
		{
			if (!_isConnectedToHost || Multiplayer.MultiplayerPeer == null)
			{
				GameLog.LogError("[LobbyManager] Disconnected from the peer serving the map.");
				return false;
			}

			var completedTask = await Task.WhenAny(_transferCompleteTcs.Task, Task.Delay(2000));
			if (completedTask == _transferCompleteTcs.Task) return _transferCompleteTcs.Task.Result;

			if (DateTime.UtcNow - session.LastActivityTimeUtc > TimeSpan.FromSeconds(45))
			{
				GameLog.LogError("[LobbyManager] ENet transfer inactivity timeout for chunk. Retrying remaining missing assets...");
				return false;
			}
		}

		return false;
	}

	public async Task<bool> DownloadMapEphemerallyAsync(string targetIp, int targetPort, string mapName, Action<float>? progressCallback = null, CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrWhiteSpace(mapName) || string.IsNullOrWhiteSpace(targetIp) || targetPort <= 0) return false;
		if (_lobbySession != null) return false;

		if (MapAssetManager.IsMapDownloaded(mapName))
		{
			EmitMapDownloadComplete(progressCallback);
			return true;
		}

		bool wasSeeding = StopSeedingIfActive();
		Action<float>? progressHandler = SetupProgressHandler(progressCallback);

		try
		{
			var peer = await EstablishEphemeralConnectionAsync(targetIp, targetPort, cancellationToken);
			if (peer == null) return false;

			_isConnectedToHost = true;
			bool downloadResult = await EnsureMapDownloadedAsync(mapName);

			CleanupEphemeralConnection(peer);
			return downloadResult;
		}
		catch (Exception ex)
		{
			GameLog.LogError($"[LobbyManager] Ephemeral map download error: {ex.Message}");
			return false;
		}
		finally
		{
			TeardownEphemeralState(progressHandler, wasSeeding);
		}
	}

	private void EmitMapDownloadComplete(Action<float>? progressCallback)
	{
		progressCallback?.Invoke(1.0f);
		MapDownloadProgressChanged?.Invoke(1.0f);
		MapDownloadCompleted?.Invoke();
	}

	/// <summary>
	///     Gives the multiplayer peer slot back before a lobby or a match claims it, so a seeder that is winding down can never
	///     leave the registry advertising an address whose socket has already been replaced.
	/// </summary>
	private static void ReleaseSeedingSlot()
	{
		GameContext.PeerSeeder?.Stop();
	}

	private bool StopSeedingIfActive()
	{
		bool wasSeeding = GameContext.PeerSeeder != null && GameContext.PeerSeeder.IsSeeding;
		if (wasSeeding)
		{
			GameContext.PeerSeeder!.Stop();
		}

		return wasSeeding;
	}

	private Action<float>? SetupProgressHandler(Action<float>? progressCallback)
	{
		if (progressCallback == null) return null;
		Action<float> handler = p => progressCallback.Invoke(p);
		MapDownloadProgressChanged += handler;
		return handler;
	}

	private async Task<ENetMultiplayerPeer?> EstablishEphemeralConnectionAsync(string targetIp, int targetPort, CancellationToken cancellationToken)
	{
		bool isLocal = targetIp == "127.0.0.1" || targetIp == "localhost" || targetIp == PublicIP;
		int clientPort = ENetPort + 16;
		if (!isLocal) await UdpHolePuncher.PunchHoleAsync(targetIp, targetPort, clientPort);

		var peer = new ENetMultiplayerPeer();
		int localPortToUse = isLocal ? 0 : clientPort;
		var err = peer.CreateClient(targetIp, targetPort, localPort: localPortToUse);
		if (err != Error.Ok && localPortToUse != 0)
		{
			err = peer.CreateClient(targetIp, targetPort, localPort: 0);
		}

		if (err != Error.Ok)
		{
			GameLog.LogError($"[LobbyManager] Failed to create ephemeral ENet client: {err}");
			return null;
		}

		var packetPeer = peer.GetPeer(1);
		if (packetPeer != null) packetPeer.SetTimeout(32, 5000, 15000);

		_isConnectedToHost = false;
		Multiplayer.MultiplayerPeer = peer;

		if (!await WaitForEphemeralConnectionAsync(cancellationToken))
		{
			GameLog.LogError($"[LobbyManager] Ephemeral connection to {targetIp}:{targetPort} timed out or failed.");
			CleanupEphemeralConnection(peer);
			return null;
		}

		return peer;
	}

	private async Task<bool> WaitForEphemeralConnectionAsync(CancellationToken cancellationToken)
	{
		var connectedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		void OnConnected() => connectedTcs.TrySetResult(true);
		void OnFailed() => connectedTcs.TrySetResult(false);

		Multiplayer.ConnectedToServer += OnConnected;
		Multiplayer.ConnectionFailed += OnFailed;

		var connectTask = await Task.WhenAny(connectedTcs.Task, Task.Delay(8000, cancellationToken));

		Multiplayer.ConnectedToServer -= OnConnected;
		Multiplayer.ConnectionFailed -= OnFailed;

		return connectTask == connectedTcs.Task && connectedTcs.Task.Result;
	}

	private void CleanupEphemeralConnection(ENetMultiplayerPeer peer)
	{
		try
		{
			peer.Close();
		}
		catch
		{
		}

		Multiplayer.MultiplayerPeer = null;
		_isConnectedToHost = false;
	}

	private void TeardownEphemeralState(Action<float>? progressHandler, bool wasSeeding)
	{
		if (progressHandler != null)
		{
			MapDownloadProgressChanged -= progressHandler;
		}

		if (Multiplayer.MultiplayerPeer != null)
		{
			try
			{
				Multiplayer.MultiplayerPeer.Close();
			}
			catch
			{
			}

			Multiplayer.MultiplayerPeer = null;
		}

		_isConnectedToHost = false;

		if (wasSeeding)
		{
			GameContext.PeerSeeder?.CheckIdleAndSeedStatus();
		}
	}

	private static string FormatLinkMilliseconds(int milliseconds) => milliseconds < 0 ? "--" : $"{milliseconds} ms";

	private static string FormatLinkPercent(float percent) => percent < 0f ? "--" : $"{(int)Math.Round(percent)}%";

	private static uint PackColor(Color color)
	{
		uint red = (uint)Math.Clamp(Math.Round(color.R * 255.0), 0.0, 255.0);
		uint green = (uint)Math.Clamp(Math.Round(color.G * 255.0), 0.0, 255.0);
		uint blue = (uint)Math.Clamp(Math.Round(color.B * 255.0), 0.0, 255.0);
		uint alpha = (uint)Math.Clamp(Math.Round(color.A * 255.0), 0.0, 255.0);
		return (red << 24) | (green << 16) | (blue << 8) | alpha;
	}

	private static Color UnpackColor(uint rgba)
	{
		if (rgba == 0) return PlayerColorConfig.GetColor(1);

		float red = ((rgba >> 24) & 0xFF) / 255.0f;
		float green = ((rgba >> 16) & 0xFF) / 255.0f;
		float blue = ((rgba >> 8) & 0xFF) / 255.0f;
		float alpha = (rgba & 0xFF) / 255.0f;
		return new Color(red, green, blue, alpha);
	}

	private static string Tr(string text) => TranslationServer.Translate(text);
}
