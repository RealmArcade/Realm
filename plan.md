The issue says:
"Duplicate Godot RPC bodies across different networking nodes (e.g., EnetMapTransferService, LobbyManager) should be refactored into 1-liner delegate calls to static helper methods in MapTransferHelper to maintain the required Godot [Rpc] attributes on node classes while centralizing logic."

Wait, actually, I am looking at the `EnetMapTransferService` and `LobbyManager`.
The duplicates listed in the prompt are:
1. `RequestNextMapTransferChunkRpc`, `BeginMapTransferRpc`, `SendMapTransferChunkRpc`.
2. `RequestMapManifestRpc`, `ReceiveMapManifestRpc`, `RequestMapAssetTransferRpc`.

However, the problem is not just these RPCs, but potentially `HandleHostAssetTransferAsync`, `SendHostChunkAsync`, `SendChunkPacketsAsync`, `TransmitChunkPacketsAsync`, `PrepareHostTransferSession`.
Wait, the prompt says:
```
Found a 16 line (148 tokens) duplication in the following files: 
Starting at line 364 of @Realm.Client\Network\EnetMapTransferService.cs
Starting at line 1892 of @Realm.Client\Network\LobbyManager.cs
```
The exact block:
```csharp
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
```
and
```
Found a 13 line (105 tokens) duplication in the following files: 
Starting at line 235 of @Realm.Client\Network\EnetMapTransferService.cs
Starting at line 1761 of @Realm.Client\Network\LobbyManager.cs
```
The exact block:
```csharp
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
```

Wait! Is there a duplication because these methods do EXACTLY the same thing, and they also share the `HandleHostAssetTransferAsync`, `SendHostChunkAsync`, `TransmitChunkPacketsAsync` (or `SendChunkPacketsAsync`) code?
Actually, `EnetMapTransferService` and `LobbyManager` have nearly identical `HandleHostAssetTransferAsync` and `SendHostChunkAsync` blocks!
Let me review `EnetMapTransferService.cs`:
```csharp
	private async Task HandleHostAssetTransferAsync(int peerId, string transferId, string mapName, string mapVersion, string[] missingHashes)
	...
	private async Task SendHostChunkAsync(HostTransferSession session, int chunkIndex)
	...
	private async Task TransmitChunkPacketsAsync(HostTransferSession session, int chunkIndex, byte[] compressedChunkBytes)
	...
```
and `LobbyManager.cs`:
```csharp
	private async Task HandleHostAssetTransferAsync(int peerId, string transferId, string mapName, string mapVersion, string[] missingHashes)
	...
	private void PrepareHostTransferSession(HostTransferSession session, string mapName, string mapVersion, string[] missingHashes)
	...
	private Dictionary<string, string> BuildLocalFileMap(string mapName, string mapVersion)
	...
	private async Task SendHostChunkAsync(HostTransferSession session, int chunkIndex)
	...
	private async Task SendChunkPacketsAsync(HostTransferSession session, int chunkIndex, byte[] compressedChunkBytes, int totalPacketsInChunk)
	...
```
If we extract all this logic into `MapTransferHelper`, we can call it from `LobbyManager` and `EnetMapTransferService`.

Let's look at `MapTransferHelper.cs`.
It already has `HandleBeginMapTransferRpc`, `HandleSendMapTransferChunkRpcWrapper`, `HandleRequestMapManifest`, `HandleReceiveMapManifest`, etc.
Wait, `RequestMapAssetTransferRpc` already exists in both. It calls:
```csharp
		int senderId = Multiplayer.GetRemoteSenderId();
		if (!MapTransferHelper.HandleBeginMapTransfer(this, senderId, transferId, ref _activeEphemeralTransferPeerId)) return;

		MapTransferHelper.CancelHostTransfersForPeer(senderId, _hostTransfers);

		_ = HandleHostAssetTransferAsync(senderId, transferId, mapName, mapVersion, missingHashes);
```
Wait, we can create:
```csharp
public static async Task HandleHostAssetTransferAsync(
	Node rpcNode, 
	int peerId, 
	string transferId, 
	string mapName, 
	string mapVersion, 
	string[] missingHashes, 
	ConcurrentDictionary<string, HostTransferSession> hostTransfers, 
	Func<HostTransferSession, int, Task> sendHostChunkAsync,
	string beginMapTransferRpcName,
	string logPrefix)
{
	var session = new HostTransferSession
	{
		TransferId = transferId,
		PeerId = peerId,
		MapName = mapName,
		MapVersion = mapVersion,
		Cts = new CancellationTokenSource()
	};
	hostTransfers[transferId] = session;

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
		}, session.Cts.Token);

		int totalChunks = session.TotalChunks;
		long totalBytes = session.TotalRawBytes;

		Callable.From(() => rpcNode.RpcId(peerId, beginMapTransferRpcName, transferId, mapName, mapVersion, totalBytes, totalChunks)).CallDeferred();

		if (totalChunks > 0)
		{
			await sendHostChunkAsync(session, 0);
		}
	}
	catch (Exception ex)
	{
		GD.PrintErr($"[{logPrefix}] Error during host asset transfer {transferId} to peer {peerId}: {ex.Message}");
	}
}
```

Wait, what about `SendHostChunkAsync` and `SendChunkPacketsAsync` / `TransmitChunkPacketsAsync`?
Can we extract them to `MapTransferHelper` as well?

```csharp
public static async Task SendHostChunkAsync(
	HostTransferSession session, 
	int chunkIndex, 
	Node rpcNode, 
	string sendMapTransferChunkRpcName, 
	string logPrefix)
{
	if (chunkIndex < 0 || chunkIndex >= session.ChunkItems.Count || session.Cts.IsCancellationRequested) return;

	try
	{
		byte[] compressedChunkBytes = await MapTransferHelper.CompressChunkAsync(session, chunkIndex);

		int totalPacketsInChunk = (int)Math.Ceiling((double)compressedChunkBytes.Length / ZstdAssetBundleHelper.PacketChunkSize);
		if (totalPacketsInChunk <= 0) totalPacketsInChunk = 1;

		await SendChunkPacketsAsync(session, chunkIndex, compressedChunkBytes, totalPacketsInChunk, rpcNode, sendMapTransferChunkRpcName);
	}
	catch (Exception ex)
	{
		GD.PrintErr($"[{logPrefix}] Error sending chunk {chunkIndex} for transfer {session.TransferId}: {ex.Message}");
	}
}

public static async Task SendChunkPacketsAsync(
	HostTransferSession session, 
	int chunkIndex, 
	byte[] compressedChunkBytes, 
	int totalPacketsInChunk, 
	Node rpcNode, 
	string sendMapTransferChunkRpcName)
{
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
		long totalBytes = session.TotalRawBytes;

		Callable.From(() => rpcNode.RpcId(
			session.PeerId,
			sendMapTransferChunkRpcName,
			session.TransferId,
			currentChunkIndex,
			totalChunks,
			currentPacketIndex,
			totalPacketsInChunk,
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
```

Wait, `EnetMapTransferService` and `LobbyManager` have:
```csharp
	[Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void RequestMapAssetTransferRpc(string transferId, string mapName, string mapVersion, string[] missingHashes)
	{
		int senderId = Multiplayer.GetRemoteSenderId();
		if (!MapTransferHelper.HandleBeginMapTransfer(this, senderId, transferId, ref _activeEphemeralTransferPeerId)) return;

		MapTransferHelper.CancelHostTransfersForPeer(senderId, _hostTransfers);

		_ = MapTransferHelper.HandleHostAssetTransferAsync(this, senderId, transferId, mapName, mapVersion, missingHashes, _hostTransfers, SendHostChunkAsync, nameof(BeginMapTransferRpc), "EnetMapTransferService");
	}

	private Task SendHostChunkAsync(HostTransferSession session, int chunkIndex)
	{
		return MapTransferHelper.SendHostChunkAsync(session, chunkIndex, this, nameof(SendMapTransferChunkRpc), "EnetMapTransferService");
	}
```
Wait! I can also just make `RequestMapAssetTransferRpc` a 1-liner:
```csharp
	[Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void RequestMapAssetTransferRpc(string transferId, string mapName, string mapVersion, string[] missingHashes)
	{
		MapTransferHelper.HandleRequestMapAssetTransferRpc(this, transferId, mapName, mapVersion, missingHashes, ref _activeEphemeralTransferPeerId, _hostTransfers, SendHostChunkAsync, nameof(BeginMapTransferRpc), "EnetMapTransferService");
	}
```
Wait, in `LobbyManager` there's a check:
```csharp
	[Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void RequestMapAssetTransferRpc(string transferId, string mapName, string mapVersion, string[] missingHashes)
	{
		if (!IsHost && (PeerSeederManager.Instance == null || !PeerSeederManager.Instance.IsSeeding)) return;
        // ...
```
So I can pass a `Func<bool> canProceed` or just do the check before calling `MapTransferHelper`.
Actually, if the check is just at the top of the RPC:
```csharp
	[Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void RequestMapAssetTransferRpc(string transferId, string mapName, string mapVersion, string[] missingHashes)
	{
		if (!IsHost && (PeerSeederManager.Instance == null || !PeerSeederManager.Instance.IsSeeding)) return;
		MapTransferHelper.HandleRequestMapAssetTransferRpc(this, transferId, mapName, mapVersion, missingHashes, ref _activeEphemeralTransferPeerId, _hostTransfers, SendHostChunkAsync, nameof(BeginMapTransferRpc), "LobbyManager");
	}
```

Wait, `HandleRequestMapAssetTransferRpc` can just be:
```csharp
	public static void HandleRequestMapAssetTransferRpc(
		Node rpcNode,
		string transferId,
		string mapName,
		string mapVersion,
		string[] missingHashes,
		ref int activeEphemeralTransferPeerId,
		ConcurrentDictionary<string, HostTransferSession> hostTransfers,
		Func<HostTransferSession, int, Task> sendHostChunkAsync,
		string beginMapTransferRpcName,
		string logPrefix)
	{
		int senderId = rpcNode.Multiplayer.GetRemoteSenderId();
		if (!HandleBeginMapTransfer(rpcNode, senderId, transferId, ref activeEphemeralTransferPeerId)) return;

		CancelHostTransfersForPeer(senderId, hostTransfers);

		_ = HandleHostAssetTransferAsync(rpcNode, senderId, transferId, mapName, mapVersion, missingHashes, hostTransfers, sendHostChunkAsync, beginMapTransferRpcName, logPrefix);
	}
```

This way I can remove `HandleHostAssetTransferAsync`, `PrepareHostTransferSession`, `BuildLocalFileMap`, `SendHostChunkAsync`, `SendChunkPacketsAsync`, `TransmitChunkPacketsAsync` from both `EnetMapTransferService.cs` and `LobbyManager.cs`.

Also, there is `AcknowledgeMapTransferCompleteRpc`. Both have identical implementations:
```csharp
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
```
I can add `HandleAcknowledgeMapTransferCompleteRpc` to `MapTransferHelper`:
```csharp
	public static void HandleAcknowledgeMapTransferCompleteRpc(string transferId, ConcurrentDictionary<string, HostTransferSession> hostTransfers, ref int activeEphemeralTransferPeerId)
	{
		if (hostTransfers.TryRemove(transferId, out var session))
		{
			session.Cts.Cancel();
			if (activeEphemeralTransferPeerId == session.PeerId)
			{
				activeEphemeralTransferPeerId = 0;
			}
		}
	}
```

Let's do this refactoring and then check what's left.
