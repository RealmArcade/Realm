1. **Network Transfer RPC duplication** (`LobbyManager.cs` vs `EnetMapTransferService.cs`):
   - I will use `replace_with_git_merge_diff` to extract the common RPC methods into a new abstract base class `MapTransferBaseNode` in `Realm.Client/Network/MapTransferBaseNode.cs`.
   - I will verify the creation of `Realm.Client/Network/MapTransferBaseNode.cs` using `cat`.
   - Both `LobbyManager` and `EnetMapTransferService` will inherit from this base class instead of `Node`, effectively removing the duplicated `[Rpc]` methods from both files. (The `if (!IsHost...)` checks in `LobbyManager` will be retained via a protected virtual method `CanProcessRpc` overridden in `LobbyManager`).

2. **MapEditor AssetBrowserDialog/AssetManagerDialog**:
   - Use `replace_with_git_merge_diff` on `Realm.Client/UI/MapEditor/AssetBrowserDialog.cs` and `Realm.Client/UI/MapEditor/AssetManagerDialog.cs` to remove `BindVisibleGridCells` and modify `UpdateVisibleGridCells` to call a new shared method `VirtualGridHelper.BindAssetGridCells`. I will add `BindAssetGridCells` to `Realm.Client/UI/MapEditor/VirtualGridHelper.cs` (verified location).

3. **InGameHUD and CommandPanel**:
   - Use `replace_with_git_merge_diff` to extract the tooltip formatting logic. I'll add `FormatUnitCostTooltip` to `Realm.Client.UI.UIStyle` (in `Realm.Client/UI/UIStyle.cs`) and update both `Realm.Client/UI/InGameHUD.cs` and `Realm.Client/UI/InGame/CommandPanel.cs` to call it.

4. **InputService and NetworkService (`IsUnitActive`)**:
   - Use `replace_with_git_merge_diff` to move `IsUnitActive` and `EnqueueCommand` to `Realm.Client/Services/SimulationService.cs` (decorated with `[Realm.Client.Core.DomainLogic]`). Update `Realm.Client/Services/InputService.cs` and `Realm.Client/Services/NetworkService.cs` to call `GameContext.SimulationService.IsUnitActive` and `GameContext.SimulationService.EnqueueCommand`.

5. **TemplateManagerDialog**:
   - Use `replace_with_git_merge_diff` on `Realm.Client/UI/MapEditor/TemplateManagerDialog.cs` (lines 1540-1600) to combine the duplication in the duplication block by defining a local variable `Action<TemplateManagerDialog, MapMetadata, ObjectItemInfo, string, string> HandleShaderMetadata` and mapping the keys `"shaders"`, `"shader"`, `"spawnshader"`, `"spawnshaders"` to it in the dictionary initializer.

6. **NetworkService and ReplayService (snapshot mapping)**:
   - Use `replace_with_git_merge_diff` to extract the duplicated mapping logic into `Realm.Client/Services/UnitSpawnService.cs` as a new method `InstantiateEntityAndMapSnapshot`. Update `Realm.Client/Services/NetworkService.cs` and `Realm.Client/Services/ReplayService.cs` to call this new method.

7. **Complete pre commit steps**:
   - Complete pre commit steps to ensure proper testing, verification, review, and reflection are done.

8. **Submit**:
   - Run the test suite using `dotnet test --filter "FullyQualifiedName!~Realm.Client.Tests&FullyQualifiedName!~Realm.Distribution.Tests&FullyQualifiedName!~Realm.Ecs.Tests&FullyQualifiedName!~Realm.TranslationTool" Realm.slnx` to verify changes and ensure no regressions were introduced. Wait, after testing, submit the change using `submit`.
