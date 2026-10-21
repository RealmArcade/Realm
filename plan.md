1. **Realm.Client/Services/MapStorageService.cs**: `GetExportMapDefaultFileName` duplicated logic in `ResolveExportDestinationPath`.
2. **Realm.Client/Services/NetworkService.cs**: `CreateUnitSnapshot` logic duplicated in `ProcessUnitForBaselineSnapshot`.
3. **Realm.Client/UI/LobbyRoom.cs & Realm.Client/UI/MapInfoHelper.cs**: Duplicate `FormatMapDisplayName`.
4. **Realm.Client/UI/UIStyle.cs**: Duplicate stylebox logic for lobby buttons. Create `CreateCustomLobbyButtonTexture`.
5. **Realm.Client/VFX/ProceduralVfxMeshGenerator.cs**: Ring index loop duplicated in `BuildTorusMesh` and `BuildFunnelConeMesh`. Use `AddRingIndices`.
6. **Realm.Client/UI/MapEditor/AssetManagerDialog.cs & Realm.Client/UI/MapEditor/ConvertGlbDialog.cs**: AI Button `btnWebAi` logic duplicated. Create `UIStyle.CreateWebAiButton()`.
7. **Realm.Client/Decal3D.cs & Realm.Client/UI/MapEditor/DecalSnapshot.cs**: Extract animation variables into `DecalAnimationConfig`.
8. **Realm.Client/RuntimeTerrain.cs**: `ClampSwatchParams` duplicated within the file.
9. **Realm.Client/Core/GameHost.Input.cs**: Duplicate `CalculatePlacementPosition` logic.
10. **Realm.Client/Network/EnetMapTransferService.cs & Realm.Client/Network/LobbyManager.cs**: `GatherItemsToPack` duplicate logic. Use `MapTransferHelper.GatherItemsToPack`.
11. Pre commit steps.
