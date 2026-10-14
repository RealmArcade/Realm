1. **Remove duplicated Aabb corners creation logic**
   - Extract `Vector3[] corners = new[] { ... }` into `AabbExtensions.cs` using an extension method `GetCorners`. We will use `replace_with_git_merge_diff` to update `Unit3D.cs`, `AnimationPreviewDialog.cs`, `EntityVisualEditDialog.cs`, and `ObjectAttachmentDialog.cs` to use this extension method. Verify by listing file.

2. **Remove duplicated UI button setup logic**
   - Use `write_file` to create a shared class `UIHelper.cs` and place the duplicated button styling logic. Verify by reading file.
   - Use `replace_with_git_merge_diff` to update `InGameHUD.cs` and `CommandPanel.cs` to use `UIHelper.SetupHUDButton`. 

3. **Remove duplicated code for updating buff states**
   - Extract the shared dictionary update logic from `UpdateBuffsQueryAction` and `UpdateBuffStateQueryAction` into a generic private method `UpdateBuffDictionary(Dictionary<string, float> buffsDict)` in `SimulationService.cs`. Use `replace_with_git_merge_diff` and verify by checking the file.

4. **Remove duplicated color modulation logic for Map Editor**
   - Extract the `r, g, b` modulation math block from `DecalSettingsDialog.cs` lines 1191 and 1278 into a private method `Color CalculateColorModulation(...)` or equivalent. Use `replace_with_git_merge_diff` and verify.

5. **Remove duplicated random button style creation**
   - Extract the `mapEdBtnTex` style creation in `NoiseTextureDialog.cs` and `WaterProfileDialog.cs` into `UIStyle.CreateMapEditorRandomButtonStyle(Button btn)` using `replace_with_git_merge_diff`. Verify.

6. **Remove duplicated network chunks creation**
   - Create a `HostTransferSessionHelper.cs` class using `write_file`. Verify by reading file.
   - Extract `CreateCompressedChunkAsync` and `SendChunkPacketsAsync` (renamed as `TransmitChunkPacketsAsync`) into it and make them static.
   - Update `LobbyManager.cs` and `EnetMapTransferService.cs` using `replace_with_git_merge_diff`. Verify.

7. **Remove duplicated bounding box size calculations in MapEditor**
   - Create `CalculateBoundingBox(...)` returning a tuple `(float minWorldX, float maxWorldX, float minWorldZ, float maxWorldZ, float centerWorldX, float centerWorldZ, float rxWorld, float rzWorld)` in `EditorService.cs`.
   - Use `replace_with_git_merge_diff` to replace duplicated logic in `EditorService.cs`. Verify.

8. **Ensure proper testing, verification, review, and reflection are done**
   - Complete pre commit steps to ensure proper testing, verification, review, and reflection are done.

9. **Run tests**
   - Use `dotnet test --filter "FullyQualifiedName!~Realm.Client.Tests&FullyQualifiedName!~Realm.Distribution.Tests&FullyQualifiedName!~Realm.Ecs.Tests&FullyQualifiedName!~Realm.TranslationTool" Realm.slnx` to run the test suite.
