echo "--- EditorService ProcessDecalBombingRules ---"
cat Realm.Client/Services/EditorService.cs | awk '/private void ProcessDecalBombingRules/,/}/'

echo "--- TemplateManagerDialog RemoveTerrainTexture ---"
cat Realm.Client/UI/MapEditor/TemplateManagerDialog.cs | awk '/private void RemoveTerrainTexture/,/}/'

echo "--- MapStorageService CheckCompiledWasmBinaryExists ---"
cat Realm.Client/Services/MapStorageService.cs | awk '/public bool CheckCompiledWasmBinaryExists/,/^	}/'

echo "--- MapDetails LoadTextureSafe ---"
cat Realm.Client/UI/MapDetails.cs | awk '/private Texture2D LoadTextureSafe/,/^	}/'

echo "--- RuntimeTerrain AddWaterWallX ---"
cat Realm.Client/RuntimeTerrain.cs | awk '/private void AddWaterWallX/,/^	}/'

echo "--- EditableTerrain RemapSplatIndices ---"
cat Realm.Client/EditableTerrain.cs | awk '/public override void RemapSplatIndices/,/^	}/'

echo "--- NetworkService HandleAttackCommand ---"
cat Realm.Client/Services/NetworkService.cs | awk '/private void HandleAttackCommand/,/^	}/'
