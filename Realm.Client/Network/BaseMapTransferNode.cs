using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Godot;
using Realm.Shared.Distribution;

namespace Realm.Client.Network;

public abstract partial class BaseMapTransferNode : Node
{
    protected readonly ConcurrentDictionary<string, HostTransferSession> _hostTransfers = new();
    protected TaskCompletionSource<string>? _manifestTcs;
    protected TaskCompletionSource<bool>? _transferCompleteTcs;
    protected ClientTransferSession? _currentClientTransfer;
    protected int _activeEphemeralTransferPeerId = 0;

    protected abstract string TransferLogPrefix { get; }
    protected virtual bool IsMapTransferAllowed() => true;
    protected abstract void OnDownloadProgressChanged(float progress);

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    protected void RequestMapManifestRpc(string targetMap)
    {
        if (!IsMapTransferAllowed()) return;
        MapTransferHelper.HandleRequestMapManifest(this, Multiplayer.GetRemoteSenderId(), targetMap, nameof(ReceiveMapManifestRpc));
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    protected void ReceiveMapManifestRpc(string mapName, string mapVersion, string manifestJson) => 
        MapTransferHelper.HandleReceiveMapManifest(_manifestTcs, manifestJson);

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    protected void RequestMapAssetTransferRpc(string transferId, string mapName, string mapVersion, string[] missingHashes)
    {
        if (!IsMapTransferAllowed()) return;
        MapTransferHelper.HandleRequestMapAssetTransferRpc(this, transferId, mapName, mapVersion, missingHashes, ref _activeEphemeralTransferPeerId, _hostTransfers, SendHostChunkAsync, nameof(BeginMapTransferRpc), TransferLogPrefix);
    }

    protected Task SendHostChunkAsync(HostTransferSession session, int chunkIndex) => 
        MapTransferHelper.SendHostChunkAsync(session, chunkIndex, this, nameof(SendMapTransferChunkRpc), TransferLogPrefix);

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    protected void RequestNextMapTransferChunkRpc(string transferId, int nextChunkIndex)
    {
        if (!IsMapTransferAllowed()) return;
        MapTransferHelper.HandleRequestNextMapTransferChunkRpc(transferId, nextChunkIndex, _hostTransfers, SendHostChunkAsync);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    protected void BeginMapTransferRpc(string transferId, string mapName, string mapVersion, long totalBytes, int totalChunks) =>
        MapTransferHelper.HandleBeginMapTransferRpc(_currentClientTransfer, transferId, mapName, mapVersion, totalBytes, totalChunks, this, nameof(AcknowledgeMapTransferCompleteRpc), _transferCompleteTcs);

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    protected void SendMapTransferChunkRpc(string transferId, int chunkIndex, int totalChunks, int packetIndex, int totalPacketsInChunk, long totalBytes, byte[] packetData) =>
        MapTransferHelper.HandleSendMapTransferChunkRpcWrapper(_currentClientTransfer, transferId, chunkIndex, totalChunks, packetIndex, totalPacketsInChunk, totalBytes, packetData, EmitDownloadProgressInternal, this, nameof(RequestNextMapTransferChunkRpc), nameof(AcknowledgeMapTransferCompleteRpc), _transferCompleteTcs, TransferLogPrefix);

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    protected void AcknowledgeMapTransferCompleteRpc(string transferId) =>
        MapTransferHelper.HandleAcknowledgeMapTransferCompleteRpc(transferId, _hostTransfers, ref _activeEphemeralTransferPeerId);

    protected virtual void EmitDownloadProgressInternal(float progress)
    {
        _currentClientTransfer?.ProgressCallback?.Invoke(progress);
        OnDownloadProgressChanged(progress);
    }
}
