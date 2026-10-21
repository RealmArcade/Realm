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
	private void RequestMapManifestRpc(string targetMap)
	{
		int senderId = Multiplayer.GetRemoteSenderId();
		MapTransferHelper.HandleRequestMapManifest(this, senderId, targetMap, nameof(ReceiveMapManifestRpc));
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void ReceiveMapManifestRpc(string mapName, string mapVersion, string manifestJson)
	{
		MapTransferHelper.HandleReceiveMapManifest(_manifestTcs, manifestJson);
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void RequestMapAssetTransferRpc(string transferId, string mapName, string mapVersion, string[] missingHashes)
	{
		int senderId = Multiplayer.GetRemoteSenderId();
		if (!MapTransferHelper.HandleBeginMapTransfer(this, senderId, transferId, ref _activeEphemeralTransferPeerId)) return;

		MapTransferHelper.CancelHostTransfersForPeer(senderId, _hostTransfers);

		_ = HandleHostAssetTransferAsync(senderId, transferId, mapName, mapVersion, missingHashes);
	}


	private async Task HandleHostAssetTransferAsync(int peerId, string transferId, string mapName, string mapVersion, string[] missingHashes)
	{
		var cts = new CancellationTokenSource();
		var session = new HostTransferSession
		{
			TransferId = transferId,
			PeerId = peerId,
			MapName = mapName,
			MapVersion = mapVersion,
			Cts = cts
		};
		_hostTransfers[transferId] = session;

		try
		{
			await Task.Run(() =>
			{
				var manifest = MapAssetManager.FindHostManifest(mapName, mapVersion);
				var localFileMap = MapTransferHelper.BuildLocalFileMap(mapName, mapVersion, manifest);
				var itemsToPack = MapTransferHelper.GatherItemsToPack(missingHashes, localFileMap);
				var chunkList = MapTransferHelper.ChunkTransferItems(itemsToPack);

				session.ChunkItems = chunkList;
				session.TotalRawBytes = itemsToPack.Sum(a => a.Size);
			}, cts.Token);

			int totalChunks = session.TotalChunks;
			long totalBytes = session.TotalRawBytes;

			Callable.From(() => RpcId(peerId, nameof(BeginMapTransferRpc), transferId, mapName, mapVersion, totalBytes, totalChunks)).CallDeferred();

			if (totalChunks > 0)
			{
				await SendHostChunkAsync(session, 0);
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[EnetMapTransferService] Transfer error: {ex.Message}");
		}
	}

	private async Task SendHostChunkAsync(HostTransferSession session, int chunkIndex)
	{
		if (chunkIndex < 0 || chunkIndex >= session.ChunkItems.Count || session.Cts.IsCancellationRequested)
		{
			return;
		}

		try
		{
			byte[] compressedChunkBytes = await MapTransferHelper.CompressChunkAsync(session, chunkIndex);
			await TransmitChunkPacketsAsync(session, chunkIndex, compressedChunkBytes);
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[EnetMapTransferService] Error sending chunk {chunkIndex} for transfer {session.TransferId}: {ex.Message}");
		}
	}

	private async Task TransmitChunkPacketsAsync(HostTransferSession session, int chunkIndex, byte[] compressedChunkBytes)
	{
		int totalPacketsInChunk = (int)Math.Ceiling((double)compressedChunkBytes.Length / ZstdAssetBundleHelper.PacketChunkSize);
		if (totalPacketsInChunk <= 0) totalPacketsInChunk = 1;

		int packetIndex = 0;
		int offset = 0;

		while (offset < compressedChunkBytes.Length)
		{
			if (session.Cts.IsCancellationRequested) break;

			int packetSize = Math.Min(ZstdAssetBundleHelper.PacketChunkSize, compressedChunkBytes.Length - offset);
			byte[] packetData = new byte[packetSize];
			Buffer.BlockCopy(compressedChunkBytes, offset, packetData, 0, packetSize);

			int currentPacketIndex = packetIndex;
			int currentChunkIndex = chunkIndex;
			int totalChunks = session.TotalChunks;
			int totalPackets = totalPacketsInChunk;
			long totalBytes = session.TotalRawBytes;

			Callable.From(() => RpcId(
				session.PeerId,
				nameof(SendMapTransferChunkRpc),
				session.TransferId,
				currentChunkIndex,
				totalChunks,
				currentPacketIndex,
				totalPackets,
				totalBytes,
				packetData)).CallDeferred();

			offset += packetSize;
			packetIndex++;

			if (packetIndex % 2 == 0)
			{
				await Task.Delay(1, session.Cts.Token);
			}
		}
	}


	[Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void RequestNextMapTransferChunkRpc(string transferId, int nextChunkIndex)
	{
		MapTransferHelper.HandleRequestNextMapTransferChunkRpc(transferId, nextChunkIndex, _hostTransfers, SendHostChunkAsync);
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void BeginMapTransferRpc(string transferId, string mapName, string mapVersion, long totalBytes, int totalChunks)
	{
		MapTransferHelper.HandleBeginMapTransferRpc(_currentClientTransfer, transferId, mapName, mapVersion, totalBytes, totalChunks, this, nameof(AcknowledgeMapTransferCompleteRpc), _transferCompleteTcs);
	}


	[Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void SendMapTransferChunkRpc(string transferId, int chunkIndex, int totalChunks, int packetIndex, int totalPacketsInChunk, long totalBytes, byte[] packetData)
	{
		MapTransferHelper.HandleSendMapTransferChunkRpcWrapper(
			_currentClientTransfer, transferId, chunkIndex, totalChunks, packetIndex, totalPacketsInChunk, totalBytes, packetData,
			progress => {
				_currentClientTransfer?.ProgressCallback?.Invoke(progress);
				DownloadProgressChanged?.Invoke(progress);
			},
			this,
			nameof(RequestNextMapTransferChunkRpc),
			nameof(AcknowledgeMapTransferCompleteRpc),
			_transferCompleteTcs,
			"EnetMapTransferService"
		);
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void AcknowledgeMapTransferCompleteRpc(string transferId)
	{
		if (_hostTransfers.TryRemove(transferId, out var session))
		{
			session.Cts.Cancel();
			if (_activeEphemeralTransferPeerId == session.PeerId)
			{
				_activeEphemeralTransferPeerId = 0;
			}
		}
	}
}