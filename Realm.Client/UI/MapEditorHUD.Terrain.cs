using Godot;
using NSec.Cryptography;
using Realm.EditorAPI;
using Realm.Client.Services;
using Realm.Client.UI.MapEditor;
using Realm.Client.VFX;
using Realm.Shared;
using Realm.Shared.Distribution;
using Realm.Shared.ModelOptimization;
using Realm.Shared.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MirrorMode = Realm.Ecs.Components.Core.MirrorMode;
using PasteReflection = Realm.Ecs.Components.Core.PasteReflection;
using TerrainCell = Realm.Ecs.Components.Terrain.TerrainCell;
using WaterType = Realm.Ecs.Components.Terrain.WaterType;

namespace Realm.Client.UI
{
    public partial class MapEditorHUD
    {

        public int GetSelectedPathingMask()
        {
            int mask = 0;
            mask = ApplyPathingMask(mask, _chkGround, Realm.Client.EditableTerrain.PATHING_GROUND);
            mask = ApplyPathingMask(mask, _chkFlying, Realm.Client.EditableTerrain.PATHING_FLYING);
            mask = ApplyPathingMask(mask, _chkShallowWater, Realm.Client.EditableTerrain.PATHING_SHALLOW_WATER);
            mask = ApplyPathingMask(mask, _chkDeepWater, Realm.Client.EditableTerrain.PATHING_DEEP_WATER);
            mask = ApplyPathingMask(mask, _chkBuildable, Realm.Client.EditableTerrain.PATHING_BUILDABLE);
            return mask;
        }

        private int ApplyPathingMask(int currentMask, CheckBox checkBox, int pathingFlag)
        {
            if (checkBox == null) return currentMask;
            if (!checkBox.ButtonPressed) return currentMask;
            return currentMask | pathingFlag;
        }

        public float GetSelectedWaterHeight()
        {
            if (_sldWaterHeight == null) return 0.9f;
            return (float)_sldWaterHeight.Value;
        }

        public void SelectPaintSwatchByIndex(int index)
        {
            if (index >= 0 && index < _swatchButtons.Count)
            {
                if (_chkApplyCliffTexture != null && _chkApplyCliffTexture.ButtonPressed && (_chkApplyGroundTexture == null || !_chkApplyGroundTexture.ButtonPressed))
                {
                    SelectCliffTexture(index);
                }
                else
                {
                    SelectTerrainTexture(index, _swatchButtons[index]);
                }
            }
        }

        public void ResetToBlankMap()
        {
            if (_isSyncing) return;
            _isSyncing = true;
            if (_editorService != null)
            {
                _editorService.IsPaused = true;
            }
            try
            {
                ResetFolderLocations();
                Realm.Client.Core.GameHost.Instance?.ClearMapEntirely();
                string terrainPath = System.IO.Path.Combine(_tempWorkspacePath, "terrain.json");
                string metadataPath = System.IO.Path.Combine(_tempWorkspacePath, "metadata.json");
                _lastTerrainSyncTime = GetMaxTerrainWriteTime(terrainPath);
                _lastMetadataSyncTime = GetLastWriteTimeSafe(metadataPath);
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[MapEditorHUD] ResetToBlankMap error: {ex.Message}");
            }
            finally
            {
                if (_editorService != null)
                {
                    _editorService.UpdateWatchedFileTimestamps();
                    _editorService.IsPaused = false;
                }
                _isSyncing = false;
            }
        }

        public void LoadMapProperties()
        {
            Realm.Client.RuntimeTerrain.Instance?.ReloadWaterProfiles();
            RefreshWaterSwatches();
            _mapSettingsDialog?.LoadMapProperties();
        }

        private void ImportTerrainFromMinimapPath(string selectedPath)
        {
            if (Realm.Client.Core.GameHost.Instance?.GroundTerrain == null) return;

            ResetFolderLocations();
            Realm.Client.Core.GameHost.Instance.ClearMapEntirely();
            if (!Realm.Client.Core.GameHost.Instance.ImportTerrainFromMinimap(selectedPath, out var smoothedHeights, out var splatMap, out var treePositions)) return;

            var terrain = Realm.Client.Core.GameHost.Instance.GroundTerrain;
            int width = terrain.Width;
            int depth = terrain.Depth;

            EnsureTerrainArraysInitialized(terrain, width, depth);
            ApplyTerrainData(terrain, width, depth, smoothedHeights, splatMap, terrain.Cells);

            Realm.Client.Core.GameHost.Instance.AlignTerrainSplatMapExternal();
            terrain.UpdateMeshAndPhysics();

            string wsPath = !string.IsNullOrEmpty(_tempWorkspacePath) ? _tempWorkspacePath : ProjectSettings.GlobalizePath(TempWorkspaceGodotPath);
            var treeModels = GetTreeModels(wsPath);

            SpawnTrees(treeModels, treePositions);

            ShowFeedback(TranslationServer.Translate("Terrain imported from minimap image successfully!"));
        }

        private void EnsureTerrainArraysInitialized(Realm.Client.RuntimeTerrain terrain, int width, int depth)
        {
            if (terrain.CliffSplatMap == null || terrain.CliffSplatMap.GetLength(0) != width + 1 || terrain.CliffSplatMap.GetLength(1) != depth + 1)
            {
                terrain.CliffSplatMap = new TerrainSplatWeights[width + 1, depth + 1];
            }

            var pathingCodes = terrain.PathingCodes;
            if (pathingCodes == null || pathingCodes.GetLength(0) != width || pathingCodes.GetLength(1) != depth)
            {
                terrain.PathingCodes = new int[width, depth];
            }
        }

        private void ApplyTerrainData(Realm.Client.RuntimeTerrain terrain, int width, int depth, float[,] smoothedHeights, TerrainSplatWeights[,] splatMap, TerrainCell[,] cells)
        {
            terrain.SetHeights(smoothedHeights);
            var pathingCodes = terrain.PathingCodes;

            for (int gz = 0; gz <= depth; gz++)
            {
                for (int gx = 0; gx <= width; gx++)
                {
                    ApplyCellData(terrain, gx, gz, splatMap, cells, width, depth, pathingCodes);
                }
            }
        }

        private void ApplyCellData(Realm.Client.RuntimeTerrain terrain, int gx, int gz, TerrainSplatWeights[,] splatMap, TerrainCell[,] cells, int width, int depth, int[,] pathingCodes)
        {
            if (gx < splatMap.GetLength(0) && gz < splatMap.GetLength(1))
            {
                terrain.SplatMap[gx, gz] = splatMap[gx, gz];
            }
            terrain.CliffSplatMap[gx, gz] = TerrainSplatWeights.CreateSolid(Realm.Client.Core.GameHost.Instance.EditorCliffPaintTextureIndex);

            if (gx < width && gz < depth)
            {
                pathingCodes[gx, gz] = cells != null ? Realm.Client.EditableTerrain.GetDefaultPathingCode(cells[gx, gz]) : Realm.Client.EditableTerrain.GetDefaultPathingCode(WaterType.None);
            }
        }

        private List<string> GetTreeModels(string wsPath)
        {
            var treeModels = new List<string>();
            if (MetadataService.Instance.TryLoadMetadata(wsPath, out var metadata))
            {
                LoadTreeModelsFromMetadata(metadata, wsPath, treeModels);
            }

            if (treeModels.Count == 0 && Realm.Client.Core.GameHost.ResourceRegistry != null && Realm.Client.Core.GameHost.ResourceRegistry.Count > 0)
            {
                LoadTreeModelsFromRegistry(treeModels);
            }

            return treeModels;
        }

        private void LoadTreeModelsFromMetadata(Realm.Shared.Metadata.MapMetadata metadata, string wsPath, List<string> treeModels)
        {
            try
            {
                ExtractTreeModelsFromTemplates(metadata, treeModels);
                if (treeModels.Count == 0)
                {
                    ExtractTreeModelsFromAssets(wsPath, treeModels);
                }
            }
            catch (Exception ex)
            {
                GD.PrintErr($"Failed to read tree models from metadata.json: {ex.Message}");
            }
        }

        private void ExtractTreeModelsFromTemplates(Realm.Shared.Metadata.MapMetadata metadata, List<string> treeModels)
        {
            var resources = metadata.Templates?.Resources;
            if (resources == null || resources.Count == 0) return;

            foreach (var rObj in resources)
            {
                if (IsTreeResource(rObj))
                {
                    treeModels.Add(rObj.TemplateID);
                }
            }

            if (treeModels.Count == 0)
            {
                foreach (var rObj in resources)
                {
                    if (!string.IsNullOrEmpty(rObj.TemplateID))
                    {
                        treeModels.Add(rObj.TemplateID);
                    }
                }
            }
        }

        private bool IsTreeResource(Realm.Shared.Metadata.ResourceMetadata rObj)
        {
            if (string.IsNullOrEmpty(rObj.TemplateID)) return false;

            if (rObj.TemplateID.Contains("tree", StringComparison.OrdinalIgnoreCase)) return true;
            if (rObj.Name != null && rObj.Name.Contains("tree", StringComparison.OrdinalIgnoreCase)) return true;
            if (rObj.ModelPath != null && rObj.ModelPath.Contains("tree", StringComparison.OrdinalIgnoreCase)) return true;

            return false;
        }

        private void ExtractTreeModelsFromAssets(string wsPath, List<string> treeModels)
        {
            var assetsObj = Realm.Client.Utils.MapAssetHelper.LoadAssets(wsPath);
            var propsDict = assetsObj?.GetCategory("Prop");
            if (propsDict == null) return;

            foreach (var kvp in propsDict)
            {
                if (kvp.Key.Contains("tree", StringComparison.OrdinalIgnoreCase))
                {
                    treeModels.Add(System.IO.Path.GetFileNameWithoutExtension(kvp.Key));
                }
            }

            if (treeModels.Count == 0)
            {
                foreach (var kvp in propsDict)
                {
                    treeModels.Add(System.IO.Path.GetFileNameWithoutExtension(kvp.Key));
                }
            }
        }

        private void LoadTreeModelsFromRegistry(List<string> treeModels)
        {
            foreach (var kvp in Realm.Client.Core.GameHost.ResourceRegistry)
            {
                bool isTree = kvp.Key.ToString().Contains("tree", StringComparison.OrdinalIgnoreCase) ||
                              (!string.IsNullOrEmpty(kvp.Value.Name) && kvp.Value.Name.Contains("tree", StringComparison.OrdinalIgnoreCase)) ||
                              (!string.IsNullOrEmpty(kvp.Value.ModelPath) && kvp.Value.ModelPath.Contains("tree", StringComparison.OrdinalIgnoreCase));
                if (isTree) treeModels.Add(kvp.Key);
            }

            if (treeModels.Count == 0)
            {
                foreach (var kvp in Realm.Client.Core.GameHost.ResourceRegistry)
                {
                    treeModels.Add(kvp.Key);
                }
            }
        }

        private void SpawnTrees(List<string> treeModels, List<(float x, float y, float z, float rot, float scale)> treePositions)
        {
            if (treeModels.Count == 0) return;
            var random = new Random();
            foreach (var (x, y, z, rot, scale) in treePositions)
            {
                string treePropId = treeModels[random.Next(treeModels.Count)];
                Realm.Client.Core.GameHost.Instance.SpawnPropExternalWithParams(treePropId, new Vector3(x, y, z), rot, scale);
            }
        }

        private void SelectTerrainTexture(int index, Button swatch)
        {
            if (Realm.Client.Core.GameHost.Instance != null)
            {
                if (_chkApplyCliffTexture != null && _chkApplyCliffTexture.ButtonPressed && (_chkApplyGroundTexture == null || !_chkApplyGroundTexture.ButtonPressed))
                {
                    SelectCliffTexture(index);
                    return;
                }

                Realm.Client.Core.GameHost.Instance.EditorPaintTextureIndex = index;
                HighlightSwatch(swatch);

                if (!IsSwatchCompatibleTool(Realm.Client.Core.GameHost.Instance.ActiveEditorTool))
                {
                    TriggerToolSelection(Realm.Client.Core.GameHost.EditorTool.PaintTexture, _btnTextureBrush);
                }

                UpdateTextureLabels();

                string name = TranslationServer.Translate(_swatchDisplayNames[index]);
                ShowFeedback(string.Format(TranslationServer.Translate("Selected Terrain: {0}"), name));
            }
        }

        private void ApplyLiveLightingTuning()
        {
            if (Realm.Client.EditableTerrain.Instance?.Material != null)
            {
                Realm.Client.EditableTerrain.Instance.CliffJitterStrength = _tuneCliffJitterStrength;
                Realm.Client.EditableTerrain.Instance.CliffJitterScale = _tuneCliffJitterScale;
                Realm.Client.EditableTerrain.Instance.CliffRimNoiseStrength = _tuneCliffRimNoiseStrength;
                Realm.Client.EditableTerrain.Instance.BlendSoftness = _tuneHeightBlendSoftness;
                Realm.Client.EditableTerrain.Instance.BlendNoiseStrength = _tuneBlendNoiseStrength;
                Realm.Client.EditableTerrain.Instance.BlendNoiseScale = _tuneBlendNoiseScale;

                Realm.Client.EditableTerrain.Instance.Material.SetShaderParameter("cliff_jitter_strength", _tuneCliffJitterStrength);
                Realm.Client.EditableTerrain.Instance.Material.SetShaderParameter("cliff_jitter_scale", _tuneCliffJitterScale);
                Realm.Client.EditableTerrain.Instance.Material.SetShaderParameter("blend_softness", _tuneHeightBlendSoftness);
                Realm.Client.EditableTerrain.Instance.Material.SetShaderParameter("blend_noise_strength", _tuneBlendNoiseStrength);
                Realm.Client.EditableTerrain.Instance.Material.SetShaderParameter("blend_noise_scale", _tuneBlendNoiseScale);
            }
        }
    }
}
