echo "--- EditorService ---"
grep -n -C 5 "ProcessDecalBombingRules" Realm.Client/Services/EditorService.cs
echo "--- MapStorageService ---"
grep -n -C 5 "CheckCompiledWasmBinaryExists" Realm.Client/Services/MapStorageService.cs
echo "--- UI MapEditorHUD Tools ---"
grep -n -C 5 "public bool CheckCompiledWasmBinaryExists" Realm.Client/UI/MapEditorHUD.Tools.cs
echo "--- UIStyle ---"
grep -n -C 5 "ApplyTitle" Realm.Client/UI/UIStyle.cs
echo "--- ComparePositions / AabbExtensions / UIStyle check ---"
find Realm.Client -name "SharedMaterials.cs" -o -name "ComparePositions*.cs" -o -name "AabbExtensions.cs" -o -name "UIStyle.cs"
