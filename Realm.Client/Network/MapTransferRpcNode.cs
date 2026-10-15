using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Godot;

namespace Realm.Client.Network;

public abstract partial class MapTransferRpcNode : Node
{
	protected abstract bool CanRequestChunk();
	protected abstract ConcurrentDictionary<string, HostTransferSession> GetHostTransfers();
	protected abstract Task SendHostChunkAsync(HostTransferSession session, int chunkIndex);

	protected abstract ClientTransferSession? GetCurrentClientTransfer();
	protected abstract TaskCompletionSource<bool>? GetTransferCompleteTcs();
	protected abstract TaskCompletionSource<string>? GetManifestTcs();

	protected abstract void OnChunkProgressUpdated(float overallProgress);
	protected abstract void FinalizeReceivedChunk(int chunkIndex, int totalChunks);
	protected abstract void ClearActiveEphemeralTransferPeerId(int peerId);

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	protected void RequestNextMapTransferChunkRpc(string transferId, int nextChunkIndex)
	{
		if (!CanRequestChunk()) return;
		MapTransferHelper.HandleRequestNextMapTransferChunkRpc(transferId, nextChunkIndex, GetHostTransfers(), (s, i) => { _ = SendHostChunkAsync(s, i); });
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	protected void BeginMapTransferRpc(string transferId, string mapName, string mapVersion, long totalBytes, int totalChunks)
	{
		var currentTransfer = GetCurrentClientTransfer();
		var tcs = GetTransferCompleteTcs();

		MapTransferHelper.HandleBeginMapTransferRpc(currentTransfer, transferId, mapName, mapVersion, totalBytes, totalChunks, () =>
		{
			Callable.From(() => RpcId(1, nameof(AcknowledgeMapTransferCompleteRpc), transferId)).CallDeferred();
			tcs?.TrySetResult(true);
		}, ex => tcs?.TrySetException(ex));
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	protected void SendMapTransferChunkRpc(string transferId, int chunkIndex, int totalChunks, int packetIndex, int totalPacketsInChunk, long totalBytes, byte[] packetData)
	{
		var currentTransfer = GetCurrentClientTransfer();
		var tcs = GetTransferCompleteTcs();

		if (currentTransfer == null || currentTransfer.TransferId != transferId) return;

		try
		{
			MapTransferHelper.HandleSendMapTransferChunk(
				currentTransfer, chunkIndex, totalChunks, packetIndex, totalPacketsInChunk, totalBytes, packetData,
				OnChunkProgressUpdated,
				() => FinalizeReceivedChunk(chunkIndex, totalChunks)
			);
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[MapTransfer] Error writing transfer packet: {ex.Message}");
			tcs?.TrySetException(ex);
		}
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	protected void ReceiveMapManifestRpc(string mapName, string mapVersion, string manifestJson)
	{
		MapTransferHelper.HandleReceiveMapManifestRpc(manifestJson, GetManifestTcs());
	}
	
	[Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	protected virtual void AcknowledgeMapTransferCompleteRpc(string transferId)
	{
		if (GetHostTransfers().TryRemove(transferId, out var session))
		{
			session.Cts.Cancel();
			ClearActiveEphemeralTransferPeerId(session.PeerId);
		}
	}
}
