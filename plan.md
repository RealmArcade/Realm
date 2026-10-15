1. **Refactor `MapTransferHelper` to contain a reusable chunk transmission loop.**
   - Add a method `TransmitChunkPacketsAsync` to `MapTransferHelper.cs`.
   - The loop logic relies on Godot's RPC call, so we'll pass an `Action<int, int, int, int, long, byte[]>` (representing the actual `RpcId` execution logic inside `CallDeferred()`) and `HostTransferSession`, `int chunkIndex`, `byte[] compressedChunkBytes`, and `int totalPacketsInChunk` to the new helper method.

2. **Replace `TransmitChunkPacketsAsync` / `SendChunkPacketsAsync` in `EnetMapTransferService.cs` and `LobbyManager.cs`.**
   - In `EnetMapTransferService.cs`, replace `TransmitChunkPacketsAsync` content with a call to the new `MapTransferHelper.TransmitChunkPacketsAsync`.
   - Note: the RPC lambda needs to invoke `RpcId`. We can define `Action<int, int, int, int, long, byte[]> rpcCall = (ci, tc, cpi, tpc, tb, pd) => Callable.From(() => RpcId(session.PeerId, nameof(SendMapTransferChunkRpc), session.TransferId, ci, tc, cpi, tpc, tb, pd)).CallDeferred();`
   - Oh wait! The method uses `RpcId` internally! However `RpcId` is protected in `Node`. Since `MapTransferHelper` is a static class without access to `Node`, we must pass the action.

3. **Refactor `AcknowledgeMapTransferCompleteRpc`.**
   - The exact same code exists in both `EnetMapTransferService.cs` and `LobbyManager.cs`:
     ```csharp
     [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
     protected override void AcknowledgeMapTransferCompleteRpc(string transferId)
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
   - We can add abstract properties/methods to `MapTransferRpcNode` to manage `_activeEphemeralTransferPeerId`, and move the implementation of `AcknowledgeMapTransferCompleteRpc` entirely into `MapTransferRpcNode.cs`.
   - Specifically, add `protected abstract void ClearActiveEphemeralTransferPeerId(int peerId);` to `MapTransferRpcNode.cs`.
   - Move the body of `AcknowledgeMapTransferCompleteRpc` to `MapTransferRpcNode.cs`:
     ```csharp
     [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
     protected virtual void AcknowledgeMapTransferCompleteRpc(string transferId)
     {
         if (GetHostTransfers().TryRemove(transferId, out var session))
         {
             session.Cts.Cancel();
             ClearActiveEphemeralTransferPeerId(session.PeerId);
         }
     }
     ```
   - In `EnetMapTransferService.cs` and `LobbyManager.cs`, delete the override of `AcknowledgeMapTransferCompleteRpc`. Add the implementation of `ClearActiveEphemeralTransferPeerId(int peerId)`.

4. **Complete pre-commit steps.**
   - Complete pre-commit steps to ensure proper testing, verification, review, and reflection are done.

5. **Submit the changes.**
