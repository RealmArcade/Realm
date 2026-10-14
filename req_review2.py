plan = """
1. Create and execute a uniquely named Python script `refactor_cas_helper.py` to add `private (bool Success, string Message, bool Deduplicated, bool Merged, string Blake3Hash) PerformWithLock(string normalizedHash, string extension, string? metadataHeadersJson, string? authorPublicKey, string? authorSignature, Action<string> saveAction)` helper in `Realm.Shared/Distribution/ContentAddressableStorage.cs`.
2. Run `git diff Realm.Shared/Distribution/ContentAddressableStorage.cs` to verify the helper was added.
3. Create and execute a uniquely named Python script `refactor_cas_1.py` to replace the 14-line lock and disk-space check logic inside `StoreAssetWithPrecomputedHash` with a call to `PerformWithLock` in `Realm.Shared/Distribution/ContentAddressableStorage.cs`.
4. Run `git diff Realm.Shared/Distribution/ContentAddressableStorage.cs` to verify.
5. Create and execute a uniquely named Python script `refactor_cas_2.py` to replace the lock and disk-space check logic inside `MoveTempAssetToStorage` with a call to `PerformWithLock` in `Realm.Shared/Distribution/ContentAddressableStorage.cs`.
6. Run `git diff Realm.Shared/Distribution/ContentAddressableStorage.cs` to verify.
7. Create and execute a uniquely named Python script `refactor_cas_3.py` to replace the 14-line lock and disk-space check logic inside `StoreAssetFromFile` with a call to `PerformWithLock` in `Realm.Shared/Distribution/ContentAddressableStorage.cs`.
8. Run `git diff Realm.Shared/Distribution/ContentAddressableStorage.cs` to verify.
9. Create and execute a uniquely named Python script `refactor_cas_4.py` to replace the 14-line lock and disk-space check logic inside `StoreAsset` (byte array overload) with a call to `PerformWithLock` in `Realm.Shared/Distribution/ContentAddressableStorage.cs`.
10. Run `git diff Realm.Shared/Distribution/ContentAddressableStorage.cs` to verify.
11. Create and execute a uniquely named Python script `refactor_container.py` to add `public static byte[] PackPayloadItems(IList<byte[]>? items)` and `public static List<byte[]> UnpackPayloadItems(ReadOnlySpan<byte> itemsSpan)` to `Realm.Shared/Metadata/RealmContainerFile.cs`.
12. Run `git diff Realm.Shared/Metadata/RealmContainerFile.cs` to verify.
13. Create and execute a uniquely named Python script `refactor_raud_pack.py` to replace the body of `PackTracks` in `Realm.Shared/Audio/RaudFile.cs` with a call to `RealmContainerFile.PackPayloadItems`.
14. Run `git diff Realm.Shared/Audio/RaudFile.cs` to verify.
15. Create and execute a uniquely named Python script `refactor_raud_unpack.py` to replace the body of `UnpackTracks` in `Realm.Shared/Audio/RaudFile.cs` with a call to `RealmContainerFile.UnpackPayloadItems`.
16. Run `git diff Realm.Shared/Audio/RaudFile.cs` to verify.
17. Create and execute a uniquely named Python script `refactor_rtex_pack.py` to replace the body of `PackLayers` in `Realm.Shared/Textures/RtexFile.cs` with a call to `RealmContainerFile.PackPayloadItems`.
18. Run `git diff Realm.Shared/Textures/RtexFile.cs` to verify.
19. Create and execute a uniquely named Python script `refactor_rtex_unpack.py` to replace the body of `UnpackLayers` in `Realm.Shared/Textures/RtexFile.cs` with a call to `RealmContainerFile.UnpackPayloadItems`.
20. Run `git diff Realm.Shared/Textures/RtexFile.cs` to verify.
21. Create and execute a uniquely named Python script `refactor_glb_helpers.py` to add `private static void AppendIndexedFaces<T>` and `private static void AppendSequentialFaces<T>` in `Realm.Shared/GlbPlayerColorProcessor.cs`.
22. Run `git diff Realm.Shared/GlbPlayerColorProcessor.cs` to verify.
23. Create and execute a uniquely named Python script `refactor_glb_vec2_1.py` to replace the body of `AppendIndexedVec2Faces` with a call to `AppendIndexedFaces` in `Realm.Shared/GlbPlayerColorProcessor.cs`.
24. Run `git diff Realm.Shared/GlbPlayerColorProcessor.cs` to verify.
25. Create and execute a uniquely named Python script `refactor_glb_vec2_2.py` to replace the body of `AppendSequentialVec2Faces` with a call to `AppendSequentialFaces` in `Realm.Shared/GlbPlayerColorProcessor.cs`.
26. Run `git diff Realm.Shared/GlbPlayerColorProcessor.cs` to verify.
27. Create and execute a uniquely named Python script `refactor_glb_vec3_1.py` to replace the body of `AppendIndexedVec3Faces` with a call to `AppendIndexedFaces` in `Realm.Shared/GlbPlayerColorProcessor.cs`.
28. Run `git diff Realm.Shared/GlbPlayerColorProcessor.cs` to verify.
29. Create and execute a uniquely named Python script `refactor_glb_vec3_2.py` to replace the body of `AppendSequentialVec3Faces` with a call to `AppendSequentialFaces` in `Realm.Shared/GlbPlayerColorProcessor.cs`.
30. Run `git diff Realm.Shared/GlbPlayerColorProcessor.cs` to verify.
31. Create and execute a uniquely named Python script `refactor_glb_pixel_helper.py` to add `private static bool TryGetValidOklabPixel(SkiaSharp.SKColor pixel, out float pL, out float pA, out float pB)` in `Realm.Shared/GlbPlayerColorProcessor.cs`.
32. Run `git diff Realm.Shared/GlbPlayerColorProcessor.cs` to verify.
33. Create and execute a uniquely named Python script `refactor_glb_pixel_1.py` to replace the alpha check and RGB conversion in `ProcessCandidatePixelsForChromaKey` with a call to `TryGetValidOklabPixel` in `Realm.Shared/GlbPlayerColorProcessor.cs`.
34. Run `git diff Realm.Shared/GlbPlayerColorProcessor.cs` to verify.
35. Create and execute a uniquely named Python script `refactor_glb_pixel_2.py` to replace the alpha check and RGB conversion in `PopulateChromaBins` with a call to `TryGetValidOklabPixel` in `Realm.Shared/GlbPlayerColorProcessor.cs`.
36. Run `git diff Realm.Shared/GlbPlayerColorProcessor.cs` to verify.
37. Run tests using `export GODOT_BIN=/usr/bin/godot && export MSBUILDDISABLENODEREUSE=1 && dotnet test Realm.slnx` to ensure no regressions were introduced.
38. Complete pre-commit steps to ensure proper testing, verification, review, and reflection are done.
39. Submit change.
"""
print(plan)
