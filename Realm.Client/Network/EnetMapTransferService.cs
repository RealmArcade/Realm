using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using Realm.Client.Services;
using Realm.Shared.Distribution;
using Environment = System.Environment;

namespace Realm.Client.Network;

[GlobalClass]
public partial class EnetMapTransferService : Node
{
	public static EnetMapTransferService Instance { get; private set; } = null!;

	public static event Action<float>? DownloadProgressChanged;
	public static event Action? DownloadCompleted;
	public static event Action? DownloadFailed;


	private readonly ConcurrentDictionary<string, HostTransferSession> _hostTransfers = new();
	private TaskCompletionSource<string>? _manifestTcs;
	private TaskCompletionSource<bool>? _transferCompleteTcs;
	private ClientTransferSession? _currentClientTransfer;
	private int _activeEphemeralTransferPeerId = 0;

	public override void _Ready()
	{
		Instance = this;
		if (string.IsNullOrEmpty(Name))
		{
			Name = "EnetMapTransferService";
		}
	}

	public static EnetMapTransferService EnsureNode(Node treeNode)
	{
		var root = treeNode.GetTree().Root;
		var existing = root.GetNodeOrNull<EnetMapTransferService>("EnetMapTransferService");
		if (existing != null) return existing;

		var service = new EnetMapTransferService();
		service.Name = "EnetMapTransferService";
		root.AddChild(service);
		return service;
	}


	public async Task<bool> RequestAndDownloadMapAsync(
		SceneMultiplayer multiplayer,
		int targetPeerId,
		string targetMap,
		Action<float>? progressCallback = null,
		CancellationToken cancellationToken = default)
	{
		var manifest = await TryGetManifestAsync(targetPeerId, targetMap, cancellationToken);
		if (manifest == null || manifest.Files == null)
		{
			DownloadFailed?.Invoke();
			return false;
		}

		return await DownloadMissingAssetsWithRetriesAsync(targetPeerId, targetMap, manifest, progressCallback, cancellationToken);
	}

	private async Task<MapManifest?> TryGetManifestAsync(int targetPeerId, string targetMap, CancellationToken cancellationToken)
	{
		_manifestTcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
		Callable.From(() => RpcId(targetPeerId, nameof(RequestMapManifestRpc), targetMap)).CallDeferred();

		var manifestTask = await Task.WhenAny(_manifestTcs.Task, Task.Delay(10000, cancellationToken));
		string? manifestJson = manifestTask == _manifestTcs.Task ? _manifestTcs.Task.Result : null;

		if (string.IsNullOrWhiteSpace(manifestJson))
		{
			return null;
		}

		try
		{
			return MapManifest.LoadFromJson(manifestJson) ?? JsonSerializer.Deserialize<MapManifest>(manifestJson);
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[EnetMapTransferService] Failed to deserialize manifest: {ex.Message}");
			return null;
		}
	}

	private async Task<bool> DownloadMissingAssetsWithRetriesAsync(
		int targetPeerId,
		string targetMap,
		MapManifest manifest,
		Action<float>? progressCallback,
		CancellationToken cancellationToken)
	{
		var allHashes = manifest.Files!.Values.ToList();
		string version = !string.IsNullOrWhiteSpace(manifest.Version) ? manifest.Version.Trim() : "1.0.0";
		string targetMapDir = MapAssetManager.GetMapDirectory(targetMap, version);

		int maxRetries = 10;
		int retryCount = 0;

		while (retryCount < maxRetries && !cancellationToken.IsCancellationRequested)
		{
			var missingHashes = await Task.Run(() => MapAssetManager.GetMissingHashes(allHashes));

			if (missingHashes.Count == 0)
			{
				await FinalizeAssetDownloadAsync(manifest, targetMapDir);
				InvokeDownloadCompleted(progressCallback);
				return true;
			}

			bool transferSuccess = await AttemptAssetTransferSessionAsync(
				targetPeerId, targetMap, version, manifest, allHashes.Count, missingHashes, progressCallback, cancellationToken);

			if (transferSuccess)
			{
				_currentClientTransfer = null;
				InvokeDownloadCompleted(progressCallback);
				return true;
			}

			CleanupFailedTransferSession();
			retryCount++;
			await Task.Delay(1000, cancellationToken);
		}

		DownloadFailed?.Invoke();
		return false;
	}

	private async Task<bool> AttemptAssetTransferSessionAsync(
		int targetPeerId,
		string targetMap,
		string version,
		MapManifest manifest,
		int totalManifestFiles,
		List<string> missingHashes,
		Action<float>? progressCallback,
		CancellationToken cancellationToken)
	{
		float initialProgress = totalManifestFiles > 0
			? Math.Clamp((float)(totalManifestFiles - missingHashes.Count) / totalManifestFiles, 0.0f, 1.0f)
			: 0.0f;
		progressCallback?.Invoke(initialProgress);
		DownloadProgressChanged?.Invoke(initialProgress);

		string transferId = Guid.NewGuid().ToString("N");
		var transferSession = new ClientTransferSession
		{
			TransferId = transferId,
			MapName = targetMap,
			MapVersion = version,
			Manifest = manifest,
			CurrentChunkStream = new MemoryStream(),
			ProgressCallback = progressCallback,
			TotalAssetsInManifest = totalManifestFiles,
			AlreadyPresentAssets = totalManifestFiles - missingHashes.Count,
			SessionMissingAssets = missingHashes.Count,
			LastActivityTimeUtc = DateTime.UtcNow
		};

		_currentClientTransfer = transferSession;
		_transferCompleteTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

		Callable.From(() => RpcId(targetPeerId, nameof(RequestMapAssetTransferRpc), transferId, targetMap, version, missingHashes.ToArray())).CallDeferred();

		return await WaitForTransferCompletionAsync(transferSession, cancellationToken);
	}

	private async Task<bool> WaitForTransferCompletionAsync(ClientTransferSession transferSession, CancellationToken cancellationToken)
	{
		while (_transferCompleteTcs != null && !_transferCompleteTcs.Task.IsCompleted && !cancellationToken.IsCancellationRequested)
		{
			var completedTask = await Task.WhenAny(_transferCompleteTcs.Task, Task.Delay(2000, cancellationToken));
			if (completedTask == _transferCompleteTcs.Task)
			{
				return _transferCompleteTcs.Task.Result;
			}

			if (DateTime.UtcNow - transferSession.LastActivityTimeUtc > TimeSpan.FromSeconds(45))
			{
				GD.PrintErr("[EnetMapTransferService] Transfer inactivity timeout. Retrying remaining assets...");
				break;
			}
		}

		return false;
	}

	private async Task FinalizeAssetDownloadAsync(MapManifest manifest, string targetMapDir)
	{
		await Task.Run(() =>
		{
			MapAssetManager.ExtractManifestFiles(manifest, targetMapDir);
			string localManifestPath = Path.Combine(targetMapDir, "manifest.json");
			GameContext.AssetIndexService.RegisterManifest(manifest, localManifestPath);
		});
	}

	private void InvokeDownloadCompleted(Action<float>? progressCallback)
	{
		progressCallback?.Invoke(1.0f);
		DownloadProgressChanged?.Invoke(1.0f);
		DownloadCompleted?.Invoke();
	}

	private void CleanupFailedTransferSession()
	{
		if (_currentClientTransfer != null)
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
	}


	[Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void RequestMapManifestRpc(string targetMap) => MapTransferHelper.HandleRequestMapManifest(this, Multiplayer.GetRemoteSenderId(), targetMap, nameof(ReceiveMapManifestRpc));

	[Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void ReceiveMapManifestRpc(string mapName, string mapVersion, string manifestJson) => MapTransferHelper.HandleReceiveMapManifest(_manifestTcs, manifestJson);

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void RequestMapAssetTransferRpc(string transferId, string mapName, string mapVersion, string[] missingHashes) => MapTransferHelper.HandleRequestMapAssetTransferRpc(this, transferId, mapName, mapVersion, missingHashes, ref _activeEphemeralTransferPeerId, _hostTransfers, SendHostChunkAsync, nameof(BeginMapTransferRpc), "EnetMapTransferService");

	private Task SendHostChunkAsync(HostTransferSession session, int chunkIndex) => MapTransferHelper.SendHostChunkAsync(session, chunkIndex, this, nameof(SendMapTransferChunkRpc), "EnetMapTransferService");

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void RequestNextMapTransferChunkRpc(string transferId, int nextChunkIndex) => MapTransferHelper.HandleRequestNextMapTransferChunkRpc(transferId, nextChunkIndex, _hostTransfers, SendHostChunkAsync);

	[Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)] private void BeginMapTransferRpc(string transferId, string mapName, string mapVersion, long totalBytes, int totalChunks) => MapTransferHelper.HandleBeginMapTransferRpc(_currentClientTransfer, transferId, mapName, mapVersion, totalBytes, totalChunks, this, nameof(AcknowledgeMapTransferCompleteRpc), _transferCompleteTcs);
	[Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)] private void SendMapTransferChunkRpc(string transferId, int chunkIndex, int totalChunks, int packetIndex, int totalPacketsInChunk, long totalBytes, byte[] packetData) => MapTransferHelper.HandleSendMapTransferChunkRpcWrapper(_currentClientTransfer, transferId, chunkIndex, totalChunks, packetIndex, totalPacketsInChunk, totalBytes, packetData, OnDownloadProgress, this, nameof(RequestNextMapTransferChunkRpc), nameof(AcknowledgeMapTransferCompleteRpc), _transferCompleteTcs, "EnetMapTransferService");

	private void OnDownloadProgress(float progress)
	{
		_currentClientTransfer?.ProgressCallback?.Invoke(progress);
		DownloadProgressChanged?.Invoke(progress);
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void AcknowledgeMapTransferCompleteRpc(string transferId) => MapTransferHelper.HandleAcknowledgeMapTransferCompleteRpc(transferId, _hostTransfers, ref _activeEphemeralTransferPeerId);
}