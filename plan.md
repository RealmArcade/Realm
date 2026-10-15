1. **Remove duplicated code for `IsUnitActive`.**
    - The memory states: "In Realm.Client, checking if a unit is currently performing an active action in the simulation (e.g., `IsUnitActive`) is Domain Logic managed by `SimulationService`."
    - Extract `IsUnitActive` to `Realm.Client.Services.SimulationService`.
    - Update `Realm.Client.Services.InputService` and `Realm.Client.Services.NetworkService` to call `GameContext.SimulationService.IsUnitActive(entity)`.

2. **Remove duplicated code for UI virtual grid (`AssetBrowserDialog.cs` and `AssetManagerDialog.cs`).**
    - Both classes have `BindVisibleGridCells` and `OnAssetCellClicked`. I will examine them, extract the common logic into `Realm.Client.UI.VirtualGridHelper` (or just remove the duplicate by having `AssetManagerDialog` use the implementation inside a helper or just deduplicate it). Wait, `VirtualGridHelper.UpdateVisibleGridCells` takes the bind action as a parameter. We can create `VirtualGridHelper.BindAssetGridCells(...)` and `VirtualGridHelper.HandleAssetCellClicked(...)` but that requires passing all parameters. I'll look at the specific diff required.

3. **Remove duplicated code for UI Tooltip Construction (`InGameHUD.cs` and `CommandPanel.cs`).**
    - Create a static helper `FormatUnitCostTooltip` in `Realm.Client.UI.UIStyle`. Both classes will call this method instead of duplicating the string formatting and metadata lookups.

4. **Remove duplicated Map Transfer RPC block (`EnetMapTransferService.cs` and `LobbyManager.cs`).**
    - The `BeginMapTransferRpc` and `SendMapTransferChunkRpc` methods duplicate identical bodies that handle RPCs using `MapTransferHelper`. I will check if any try/catch blocks or specific logging can be unified inside `MapTransferHelper.HandleBeginMapTransferRpc` or `HandleSendMapTransferChunk`, but actually they are already calling the helpers, the duplicate is just the Godot node wiring which might be unavoidable or minimal. The prompt mentions 24 line duplication, which points exactly to `BeginMapTransferRpc` and `SendMapTransferChunkRpc` bodies. I can extract the try-catch and finalizer callbacks to `MapTransferHelper`. Wait, `MapTransferHelper` already takes delegates for callbacks. 

5. **Remove duplicated code for Entity Creation (`NetworkService.cs` and `ReplayService.cs`).**
    - Use `UnitSpawnService.InstantiateEntityAndMapSnapshot` to replace the duplicated block. 

6. **Remove duplicated code for Spawn Shader Metadata serialization (`TemplateManagerDialog.cs`).**
    - Deduplicate the inline lambdas by extracting them into a local or private helper method in `TemplateManagerDialog.cs`.

7. **Complete pre-commit steps to ensure proper testing, verification, review, and reflection are done.**
8. **Submit the changes.**
