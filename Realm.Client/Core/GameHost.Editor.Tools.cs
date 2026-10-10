using Arch.Core;
using Godot;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Resources;
using Realm.Ecs.Components.Tags;
using Realm.Ecs.Components.Terrain;
using Realm.Client;
using Realm.Client.Core;
using Realm.Client.Services;
using Realm.Client.UI;
using Realm.Client.VFX;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Realm.Client.Core
{
    public partial class GameHost
    {

        public string GetModelAssetKey(object objOrId)
        {
            if (objOrId == null) return "";
            if (objOrId is Realm.Client.Unit3D unit) return GetUnit3DModelPath(unit);
            if (objOrId is Realm.Client.Prop3D prop) return GetProp3DModelPath(prop);
            if (objOrId is string str) return GetStringModelPath(str);
            return "";
        }

        private string GetUnit3DModelPath(Realm.Client.Unit3D unit)
        {
            if (!string.IsNullOrEmpty(unit.ModelPath))
                return NormalizeModelAssetKey(unit.ModelPath);
            if (UnitRegistry != null && UnitRegistry.TryGetValue(unit.UnitId, out var meta) && !string.IsNullOrEmpty(meta.ModelPath))
                return NormalizeModelAssetKey(meta.ModelPath);
            if (BuildingRegistry != null && BuildingRegistry.TryGetValue(unit.UnitId, out var bldMeta) && !string.IsNullOrEmpty(bldMeta.ModelPath))
                return NormalizeModelAssetKey(bldMeta.ModelPath);
            return "";
        }

        private string GetProp3DModelPath(Realm.Client.Prop3D prop)
        {
            return GetStringModelPath(prop.PropId, false);
        }

        private string GetStringModelPath(string str, bool checkCleanName = true)
        {
            var pPath = CheckPropOrResourceRegistryForModelPath(str);
            if (!string.IsNullOrEmpty(pPath)) return pPath;

            var uPath = CheckUnitOrBuildingRegistryForModelPath(str);
            if (!string.IsNullOrEmpty(uPath)) return uPath;

            if (!checkCleanName) return "";

            string clean = System.IO.Path.GetFileNameWithoutExtension(str);
            if (!string.IsNullOrEmpty(clean) && !clean.Equals(str, StringComparison.OrdinalIgnoreCase))
            {
                string cleanPath = GetStringModelPath(clean, false);
                if (!string.IsNullOrEmpty(cleanPath)) return cleanPath;
            }
            return NormalizeModelAssetKey(str);
        }

        private string CheckPropOrResourceRegistryForModelPath(string str)
        {
            if (PropRegistry != null && PropRegistry.TryGetValue(str, out var propMeta) && !string.IsNullOrEmpty(propMeta.ModelPath))
                return NormalizeModelAssetKey(propMeta.ModelPath);
            if (ResourceRegistry != null && ResourceRegistry.TryGetValue(str, out var resMeta) && !string.IsNullOrEmpty(resMeta.ModelPath))
                return NormalizeModelAssetKey(resMeta.ModelPath);
            return null;
        }

        private string CheckUnitOrBuildingRegistryForModelPath(string str)
        {
            if (UnitRegistry != null && UnitRegistry.TryGetValue(str, out var unitMeta) && !string.IsNullOrEmpty(unitMeta.ModelPath))
                return NormalizeModelAssetKey(unitMeta.ModelPath);
            if (BuildingRegistry != null && BuildingRegistry.TryGetValue(str, out var bldMeta) && !string.IsNullOrEmpty(bldMeta.ModelPath))
                return NormalizeModelAssetKey(bldMeta.ModelPath);
            return null;
        }

        public string GetSelectedEntityOrAssetKey(object objOrId)
        {
            if (objOrId == null) return "";
            if (objOrId is Realm.Client.Unit3D unit)
            {
                if (!string.IsNullOrEmpty(unit.UnitId)) return unit.UnitId;
                return GetModelAssetKey(unit);
            }
            if (objOrId is Realm.Client.Prop3D prop)
            {
                if (!string.IsNullOrEmpty(prop.PropId)) return prop.PropId;
                return GetModelAssetKey(prop);
            }
            if (objOrId is string str) return str;
            return GetModelAssetKey(objOrId);
        }
        public bool MatchesEntityOrAssetKey(object objOrId, string targetKey)
        {
            if (objOrId == null || string.IsNullOrEmpty(targetKey)) return false;
            string normTarget = NormalizeModelAssetKey(targetKey);

            if (objOrId is Realm.Client.Unit3D unit) return MatchesUnit3DKey(unit, normTarget);
            if (objOrId is Realm.Client.Prop3D prop) return MatchesProp3DKey(prop, normTarget);
            if (objOrId is string str) return MatchesStringKey(str, normTarget);

            return GetModelAssetKey(objOrId) == normTarget;
        }

        private bool MatchesProp3DKey(Realm.Client.Prop3D prop, string normTarget)
        {
            if (IsMatch(prop.PropId, normTarget)) return true;
            string cleanPropId = System.IO.Path.GetFileNameWithoutExtension(prop.PropId);

            if (CheckPropRegistryForMatch(prop.PropId, cleanPropId, normTarget)) return true;
            if (CheckResourceRegistryForMatch(prop.PropId, cleanPropId, normTarget)) return true;
            if (CheckBuildingRegistryForMatch(prop.PropId, cleanPropId, normTarget)) return true;
            if (CheckUnitRegistryForMatch(prop.PropId, cleanPropId, normTarget)) return true;

            return false;
        }

        private bool MatchesStringKey(string str, string normTarget)
        {
            if (IsMatch(str, normTarget)) return true;
            string cleanStr = System.IO.Path.GetFileNameWithoutExtension(str);

            if (CheckUnitRegistryForMatch(str, cleanStr, normTarget)) return true;
            if (CheckBuildingRegistryForMatch(str, cleanStr, normTarget)) return true;
            if (CheckPropRegistryForMatch(str, cleanStr, normTarget)) return true;
            if (CheckResourceRegistryForMatch(str, cleanStr, normTarget)) return true;

            return false;
        }

        private bool IsMatch(string key, string normTarget)
        {
            return !string.IsNullOrEmpty(key) && NormalizeModelAssetKey(key) == normTarget;
        }

        private bool CheckResourceRegistryForMatch(string id, string cleanId, string normTarget)
        {
            if (ResourceRegistry.TryGetValue(id, out var meta) && IsMatch(meta.ModelPath, normTarget)) return true;
            if (!string.IsNullOrEmpty(cleanId) && ResourceRegistry.TryGetValue(cleanId, out meta) && IsMatch(meta.ModelPath, normTarget)) return true;
            return false;
        }

        private bool CheckUnitRegistryForMatch(string id, string cleanId, string normTarget)
        {
            if (UnitRegistry.TryGetValue(id, out var meta) && IsMatch(meta.ModelPath, normTarget)) return true;
            if (!string.IsNullOrEmpty(cleanId) && UnitRegistry.TryGetValue(cleanId, out meta) && IsMatch(meta.ModelPath, normTarget)) return true;
            return false;
        }

        public void SetModelYOffset(string assetKey, float offset)
        {
            string norm = NormalizeModelAssetKey(assetKey);
            if (string.IsNullOrEmpty(norm)) return;

            ModelYOffsets[norm] = offset;

            UpdatePropsYOffset(norm, offset);
            UpdateUnitsYOffset(norm, offset);
            UpdatePreviewYOffset(norm, offset);

            _modelYOffsetSavePending = true;
            EditorHasUnsavedChanges = true;
        }

        private void UpdatePropsYOffset(string normKey, float offset)
        {
            if (AllProps == null) return;
            foreach (var prop in AllProps)
            {
                if (GodotObject.IsInstanceValid(prop) && MatchesEntityOrAssetKey(prop, normKey))
                {
                    prop.UpdateVisualYOffset(offset);
                }
            }
            Realm.Client.PropMultiMeshManager.Instance?.MarkDirty(normKey);
        }

        private void UpdateUnitsYOffset(string normKey, float offset)
        {
            if (AllUnits == null) return;
            foreach (var unit in AllUnits)
            {
                if (GodotObject.IsInstanceValid(unit) && MatchesEntityOrAssetKey(unit, normKey))
                {
                    unit.UpdateModelYOffset(offset);
                }
            }
        }

        private void UpdatePreviewYOffset(string normKey, float offset)
        {
            if (_editorPreviewNode == null || !GodotObject.IsInstanceValid(_editorPreviewNode)) return;
            if (!MatchesEntityOrAssetKey(_editorPreviewNode, normKey)) return;

            if (_editorPreviewNode is Realm.Client.Unit3D previewUnit)
            {
                previewUnit.UpdateModelYOffset(offset);
            }
            else if (_editorPreviewNode is Realm.Client.Prop3D previewProp)
            {
                previewProp.UpdateVisualYOffset(offset);
            }
        }

        public void SetModelScale(string assetKey, float scale)
        {
            string norm = NormalizeModelAssetKey(assetKey);
            if (string.IsNullOrEmpty(norm)) return;

            float clampedScale = Mathf.Clamp(scale, 0.05f, 20.0f);
            ModelScales[norm] = clampedScale;

            UpdatePropsScale(norm, clampedScale);
            UpdateUnitsScale(norm, clampedScale);
            UpdatePreviewScale(norm, clampedScale);

            _modelYOffsetSavePending = true;
            EditorHasUnsavedChanges = true;
        }

        private void UpdatePropsScale(string normKey, float scale)
        {
            if (AllProps == null) return;
            foreach (var prop in AllProps)
            {
                if (GodotObject.IsInstanceValid(prop) && MatchesEntityOrAssetKey(prop, normKey))
                {
                    prop.UpdateVisualScale(scale);
                }
            }
            Realm.Client.PropMultiMeshManager.Instance?.MarkDirty(normKey);
        }

        private void UpdateUnitsScale(string normKey, float scale)
        {
            if (AllUnits == null) return;
            foreach (var unit in AllUnits)
            {
                if (GodotObject.IsInstanceValid(unit) && MatchesEntityOrAssetKey(unit, normKey))
                {
                    unit.UpdateModelScale(scale);
                }
            }
        }

        private void UpdatePreviewScale(string normKey, float scale)
        {
            if (_editorPreviewNode == null || !GodotObject.IsInstanceValid(_editorPreviewNode)) return;
            if (!MatchesEntityOrAssetKey(_editorPreviewNode, normKey)) return;

            if (_editorPreviewNode is Realm.Client.Unit3D previewUnit)
            {
                previewUnit.UpdateModelScale(scale);
            }
            else if (_editorPreviewNode is Realm.Client.Prop3D previewProp)
            {
                previewProp.UpdateVisualScale(scale);
            }
        }

        public void SetModelCollisionCircleRatio(string assetKey, float ratio)
        {
            string norm = NormalizeModelAssetKey(assetKey);
            if (string.IsNullOrEmpty(norm)) return;

            ModelCollisionCircleRatios[norm] = ratio;

            string modelAsset = GetModelAssetKey(assetKey);
            if (!string.IsNullOrEmpty(modelAsset))
            {
                ModelCollisionCircleRatios[NormalizeModelAssetKey(modelAsset)] = ratio;
            }

            UpdateCollisionRadiiForAsset(norm);

            _modelCollisionCircleSavePending = true;
            EditorHasUnsavedChanges = true;
        }

        public void SetModelBrightness(string assetKey, float brightness)
        {
            string norm = NormalizeModelAssetKey(assetKey);
            if (string.IsNullOrEmpty(norm)) return;

            float k = Mathf.Clamp(brightness, 0.10f, 2.0f);
            ModelBrightness[norm] = k;
            UpdateMaterialOverridesForAsset(norm);

            _modelYOffsetSavePending = true;
            EditorHasUnsavedChanges = true;
        }

        public void SetModelColorTint(string assetKey, Color color)
        {
            string norm = NormalizeModelAssetKey(assetKey);
            if (string.IsNullOrEmpty(norm)) return;

            Color clamped = new Color(
                Mathf.Clamp(color.R, 0.0f, 1.0f),
                Mathf.Clamp(color.G, 0.0f, 1.0f),
                Mathf.Clamp(color.B, 0.0f, 1.0f),
                1.0f
            );
            ModelColorTint[norm] = clamped;
            UpdateMaterialOverridesForAsset(norm);

            _modelYOffsetSavePending = true;
            EditorHasUnsavedChanges = true;
        }

        public void SetModelDespillPlayerColor(string assetKey, bool despillPlayerColor)
        {
            string norm = NormalizeModelAssetKey(assetKey);
            if (string.IsNullOrEmpty(norm)) return;

            ModelDespillPlayerColor[norm] = despillPlayerColor;

            string modelAsset = GetModelAssetKey(assetKey);
            string normModel = !string.IsNullOrEmpty(modelAsset) ? NormalizeModelAssetKey(modelAsset) : null;
            if (!string.IsNullOrEmpty(normModel) && !normModel.Equals(norm, StringComparison.OrdinalIgnoreCase))
            {
                ModelDespillPlayerColor[normModel] = despillPlayerColor;
            }

            string primaryKey = GetSelectedEntityOrAssetKey(assetKey);
            string normPrimary = !string.IsNullOrEmpty(primaryKey) ? NormalizeModelAssetKey(primaryKey) : null;
            if (!string.IsNullOrEmpty(normPrimary) && !normPrimary.Equals(norm, StringComparison.OrdinalIgnoreCase))
            {
                ModelDespillPlayerColor[normPrimary] = despillPlayerColor;
            }

            UpdateDespillMetadataForAsset(assetKey, despillPlayerColor);
            UpdateMaterialOverridesForAsset(norm);

            _modelYOffsetSavePending = true;
            EditorHasUnsavedChanges = true;
        }

        private void UpdateDespillMetadataForAsset(string assetKey, bool despillPlayerColor)
        {
            if (UnitRegistry != null && UnitRegistry.TryGetValue(assetKey, out var unitMeta))
            {
                unitMeta.DespillPlayerColor = despillPlayerColor;
                UnitRegistry[assetKey] = unitMeta;
            }
            if (BuildingRegistry != null && BuildingRegistry.TryGetValue(assetKey, out var bldMeta))
            {
                bldMeta.DespillPlayerColor = despillPlayerColor;
                BuildingRegistry[assetKey] = bldMeta;
            }
            if (ResourceRegistry != null && ResourceRegistry.TryGetValue(assetKey, out var resMeta))
            {
                resMeta.DespillPlayerColor = despillPlayerColor;
                ResourceRegistry[assetKey] = resMeta;
            }
            if (PropRegistry != null && PropRegistry.TryGetValue(assetKey, out var propMeta))
            {
                propMeta.DespillPlayerColor = despillPlayerColor;
                PropRegistry[assetKey] = propMeta;
            }
        }
        public string GetModelSpawnShader(object objOrId)
        {
            if (objOrId == null) return "";

            string primaryKey = GetSelectedEntityOrAssetKey(objOrId);
            string normPrimary = NormalizeModelAssetKey(primaryKey);
            if (!string.IsNullOrEmpty(normPrimary) && ModelSpawnShaders.TryGetValue(normPrimary, out string s1))
                return s1;

            string assetKey = GetModelAssetKey(objOrId);
            string normAsset = NormalizeModelAssetKey(assetKey);
            if (!string.IsNullOrEmpty(normAsset) && ModelSpawnShaders.TryGetValue(normAsset, out string s2))
                return s2;

            return GetRegistrySpawnShader(primaryKey);
        }

        private string GetRegistrySpawnShader(string primaryKey)
        {
            if (string.IsNullOrEmpty(primaryKey)) return "";

            if (UnitRegistry.TryGetValue(primaryKey, out var meta) && !string.IsNullOrWhiteSpace(meta.SpawnShader)) return meta.SpawnShader;
            if (BuildingRegistry.TryGetValue(primaryKey, out var bldMeta) && !string.IsNullOrWhiteSpace(bldMeta.SpawnShader)) return bldMeta.SpawnShader;
            if (ResourceRegistry.TryGetValue(primaryKey, out var resMeta) && !string.IsNullOrWhiteSpace(resMeta.SpawnShader)) return resMeta.SpawnShader;
            if (PropRegistry.TryGetValue(primaryKey, out var propMeta) && !string.IsNullOrWhiteSpace(propMeta.SpawnShader)) return propMeta.SpawnShader;

            return "";
        }
        public void SetModelSpawnShader(string assetKey, string shaderKey)
        {
            string norm = NormalizeModelAssetKey(assetKey);
            if (string.IsNullOrEmpty(norm)) return;

            string modelAsset = GetModelAssetKey(assetKey);
            string normModel = !string.IsNullOrEmpty(modelAsset) ? NormalizeModelAssetKey(modelAsset) : null;

            UpdateModelSpawnShaders(norm, normModel, shaderKey);
            UpdateRegistrySpawnShaders(assetKey, shaderKey);

            _modelYOffsetSavePending = true;
            EditorHasUnsavedChanges = true;
        }

        private void UpdateModelSpawnShaders(string norm, string normModel, string shaderKey)
        {
            if (string.IsNullOrWhiteSpace(shaderKey))
            {
                ModelSpawnShaders.Remove(norm);
                if (!string.IsNullOrEmpty(normModel)) ModelSpawnShaders.Remove(normModel);
            }
            else
            {
                string trimmedShader = shaderKey.Trim();
                ModelSpawnShaders[norm] = trimmedShader;
                if (!string.IsNullOrEmpty(normModel)) ModelSpawnShaders[normModel] = trimmedShader;
            }
        }

        private void UpdateRegistrySpawnShaders(string assetKey, string shaderKey)
        {
            string finalShader = string.IsNullOrWhiteSpace(shaderKey) ? null : shaderKey.Trim();

            if (UnitRegistry.TryGetValue(assetKey, out var unitMeta))
            {
                unitMeta.SpawnShader = finalShader;
                UnitRegistry[assetKey] = unitMeta;
            }
            if (BuildingRegistry.TryGetValue(assetKey, out var bldMeta))
            {
                bldMeta.SpawnShader = finalShader;
                BuildingRegistry[assetKey] = bldMeta;
            }
            if (PropRegistry.TryGetValue(assetKey, out var propMeta))
            {
                propMeta.SpawnShader = finalShader;
                PropRegistry[assetKey] = propMeta;
            }
            if (ResourceRegistry.TryGetValue(assetKey, out var resMeta))
            {
                resMeta.SpawnShader = finalShader;
                ResourceRegistry[assetKey] = resMeta;
            }
        }

        public void SetModelDeathShader(string assetKey, string shaderKey)
        {
            string norm = NormalizeModelAssetKey(assetKey);
            if (string.IsNullOrEmpty(norm)) return;

            string modelAsset = GetModelAssetKey(assetKey);
            string normModel = !string.IsNullOrEmpty(modelAsset) ? NormalizeModelAssetKey(modelAsset) : null;

            if (string.IsNullOrWhiteSpace(shaderKey))
            {
                ModelDeathShaders.Remove(norm);
                if (!string.IsNullOrEmpty(normModel))
                {
                    ModelDeathShaders.Remove(normModel);
                }
            }
            else
            {
                string trimmedShader = shaderKey.Trim();
                ModelDeathShaders[norm] = trimmedShader;
                if (!string.IsNullOrEmpty(normModel))
                {
                    ModelDeathShaders[normModel] = trimmedShader;
                }
            }

            UpdateDeathShaderMetadataForAsset(assetKey, shaderKey);

            _modelYOffsetSavePending = true;
            EditorHasUnsavedChanges = true;
        }

        private void UpdateDeathShaderMetadataForAsset(string assetKey, string shaderKey)
        {
            string cleanShaderKey = string.IsNullOrWhiteSpace(shaderKey) ? null : shaderKey.Trim();

            if (UnitRegistry != null && UnitRegistry.TryGetValue(assetKey, out var unitMeta))
            {
                unitMeta.DeathShader = cleanShaderKey;
                UnitRegistry[assetKey] = unitMeta;
            }
            if (BuildingRegistry != null && BuildingRegistry.TryGetValue(assetKey, out var bldMeta))
            {
                bldMeta.DeathShader = cleanShaderKey;
                BuildingRegistry[assetKey] = bldMeta;
            }
            if (PropRegistry != null && PropRegistry.TryGetValue(assetKey, out var propMeta))
            {
                propMeta.DeathShader = cleanShaderKey;
                PropRegistry[assetKey] = propMeta;
            }
            if (ResourceRegistry != null && ResourceRegistry.TryGetValue(assetKey, out var resMeta))
            {
                resMeta.DeathShader = cleanShaderKey;
                ResourceRegistry[assetKey] = resMeta;
            }
        }

        public string GetModelProceduralAnimation(object objOrId)
        {
            if (objOrId == null) return "";
            string primaryKey = GetSelectedEntityOrAssetKey(objOrId);
            string normPrimary = NormalizeModelAssetKey(primaryKey);
            if (!string.IsNullOrEmpty(normPrimary) && ModelProceduralAnimations.TryGetValue(normPrimary, out string pa1))
                return pa1;

            string assetKey = GetModelAssetKey(objOrId);
            string normAsset = NormalizeModelAssetKey(assetKey);
            if (!string.IsNullOrEmpty(normAsset) && ModelProceduralAnimations.TryGetValue(normAsset, out string pa2))
                return pa2;

            return "";
        }

        public void SetModelProceduralAnimation(string assetKey, string animId)
        {
            string norm = NormalizeModelAssetKey(assetKey);
            if (string.IsNullOrEmpty(norm)) return;

            string modelAsset = GetModelAssetKey(assetKey);
            string normModel = !string.IsNullOrEmpty(modelAsset) ? NormalizeModelAssetKey(modelAsset) : null;

            if (string.IsNullOrWhiteSpace(animId))
            {
                ModelProceduralAnimations.Remove(norm);
                if (!string.IsNullOrEmpty(normModel))
                {
                    ModelProceduralAnimations.Remove(normModel);
                }
            }
            else
            {
                string trimmed = animId.Trim();
                ModelProceduralAnimations[norm] = trimmed;
                if (!string.IsNullOrEmpty(normModel))
                {
                    ModelProceduralAnimations[normModel] = trimmed;
                }
            }

            _modelYOffsetSavePending = true;
            EditorHasUnsavedChanges = true;
        }

        private bool CheckRegistryIgnorePlayerColor(string primaryKey)
        {
            if (string.IsNullOrEmpty(primaryKey)) return false;
            if (UnitRegistry.TryGetValue(primaryKey, out var meta)) return meta.IgnorePlayerColor;
            if (ResourceRegistry.TryGetValue(primaryKey, out var resMeta)) return resMeta.IgnorePlayerColor;
            if (PropRegistry.TryGetValue(primaryKey, out var propMeta)) return propMeta.IgnorePlayerColor;
            if (AttachmentRegistry.TryGetValue(primaryKey, out _)) return true;
            return false;
        }

        private bool CheckModelCacheIgnorePlayerColor(string normPrimary, string normAsset)
        {
            string lookupKey = !string.IsNullOrEmpty(normPrimary) ? normPrimary : normAsset;
            if (string.IsNullOrEmpty(lookupKey)) return false;

            var modelNode = ModelCache.GetModel(lookupKey) as Node;
            if (modelNode != null && !ModelShaderManager.ModelHasPlayerMask(modelNode))
            {
                ModelIgnorePlayerColor[lookupKey] = true;
                _modelYOffsetSavePending = true;
                EditorHasUnsavedChanges = true;
                return true;
            }

            return false;
        }

        public void SetModelIgnorePlayerColor(string assetKey, bool ignorePlayerColor)
        {
            string norm = NormalizeModelAssetKey(assetKey);
            if (string.IsNullOrEmpty(norm)) return;

            ModelIgnorePlayerColor[norm] = ignorePlayerColor;

            string modelAsset = GetModelAssetKey(assetKey);
            string normModel = !string.IsNullOrEmpty(modelAsset) ? NormalizeModelAssetKey(modelAsset) : null;
            if (!string.IsNullOrEmpty(normModel) && !normModel.Equals(norm, StringComparison.OrdinalIgnoreCase))
            {
                ModelIgnorePlayerColor[normModel] = ignorePlayerColor;
            }

            string primaryKey = GetSelectedEntityOrAssetKey(assetKey);
            string normPrimary = !string.IsNullOrEmpty(primaryKey) ? NormalizeModelAssetKey(primaryKey) : null;
            if (!string.IsNullOrEmpty(normPrimary) && !normPrimary.Equals(norm, StringComparison.OrdinalIgnoreCase))
            {
                ModelIgnorePlayerColor[normPrimary] = ignorePlayerColor;
            }

            UpdateIgnoreColorMetadataForAsset(assetKey, ignorePlayerColor);

            _modelYOffsetSavePending = true;
            EditorHasUnsavedChanges = true;
            UpdateMaterialOverridesForAsset(norm);
        }

        private void UpdateIgnoreColorMetadataForAsset(string assetKey, bool ignorePlayerColor)
        {
            if (UnitRegistry != null && UnitRegistry.TryGetValue(assetKey, out var unitMeta))
            {
                unitMeta.IgnorePlayerColor = ignorePlayerColor;
                UnitRegistry[assetKey] = unitMeta;
            }
            if (BuildingRegistry != null && BuildingRegistry.TryGetValue(assetKey, out var bldMeta))
            {
                bldMeta.IgnorePlayerColor = ignorePlayerColor;
                BuildingRegistry[assetKey] = bldMeta;
            }
            if (ResourceRegistry != null && ResourceRegistry.TryGetValue(assetKey, out var resMeta))
            {
                resMeta.IgnorePlayerColor = ignorePlayerColor;
                ResourceRegistry[assetKey] = resMeta;
            }
            if (PropRegistry != null && PropRegistry.TryGetValue(assetKey, out var propMeta))
            {
                propMeta.IgnorePlayerColor = ignorePlayerColor;
                PropRegistry[assetKey] = propMeta;
            }
        }

        private static void FindMeshInstancesRecursive(Node parent, List<MeshInstance3D> result)
        {
            if (parent == null) return;
            if (parent is MeshInstance3D mi)
            {
                result.Add(mi);
            }
            int childCount = parent.GetChildCount();
            for (int i = 0; i < childCount; i++)
            {
                FindMeshInstancesRecursive(parent.GetChild(i), result);
            }
        }

        private static List<MeshInstance3D> FindMeshInstancesRecursive(Node parent)
        {
            var list = new List<MeshInstance3D>();
            FindMeshInstancesRecursive(parent, list);
            return list;
        }

        private void UpdatePreviewNodeCollisionRadii(string normAssetKey, float ratio)
        {
            if (_editorPreviewNode == null || !GodotObject.IsInstanceValid(_editorPreviewNode) || !MatchesEntityOrAssetKey(_editorPreviewNode, normAssetKey)) return;

            if (_editorPreviewNode is Realm.Client.Unit3D previewUnit) previewUnit.UpdateCollisionCircleScale(ratio);
            else if (_editorPreviewNode is Realm.Client.Prop3D previewProp) previewProp.UpdateCollisionCircleScale(ratio);
        }

        private bool IsValidModelScale(string assetKey, float val)
        {
            if (!float.IsFinite(val) || val < MinSafeModelScale || val > MaxSafeModelScale)
            {
                GD.PushWarning($"Ignoring invalid scale {val} for model '{assetKey}' (expected {MinSafeModelScale}..{MaxSafeModelScale}).");
                return false;
            }
            return true;
        }

        public void ClearMapEditorState()
        {
            ModelYOffsets.Clear();
            ModelScales.Clear();
            ModelCollisionCircleRatios.Clear();
            ModelObstacleRadii.Clear();
            ModelBrightness.Clear();
            ModelColorTint.Clear();
            ModelDespillPlayerColor.Clear();
            ModelNormalizeLuminance.Clear();
            ModelIgnorePlayerColor.Clear();
            ModelSpawnShaders.Clear();
            ModelDeathShaders.Clear();
            ModelProceduralAnimations.Clear();
            ModelEnableProceduralAnimations.Clear();
        }
        private void UpdatePropOverrides(List<PropMetadata> props)
        {
            if (props == null) return;
            for (int i = 0; i < props.Count; i++)
            {
                var prop = props[i];
                if (string.IsNullOrEmpty(prop.TemplateID)) continue;

                bool despill = prop.DespillPlayerColor;
                bool normLuma = prop.NormalizeLuminance;
                bool ignoreColor = prop.IgnorePlayerColor;
                string spawnShader = prop.SpawnShader;
                string deathShader = prop.DeathShader;

                ApplyVisualOverrides(prop.TemplateID, prop.ModelPath, ref despill, ref normLuma, ref ignoreColor, ref spawnShader, ref deathShader);

                prop.DespillPlayerColor = despill;
                prop.NormalizeLuminance = normLuma;
                prop.IgnorePlayerColor = ignoreColor;
                prop.SpawnShader = spawnShader;
                prop.DeathShader = deathShader;

                props[i] = prop;
            }
        }

        public bool IsStaticPropAsset(string propIdOrEntityId)
        {
            if (string.IsNullOrEmpty(propIdOrEntityId)) return false;

            if (PropRegistry.ContainsKey(propIdOrEntityId) || ResourceRegistry.ContainsKey(propIdOrEntityId))
                return true;

            string clean = System.IO.Path.GetFileNameWithoutExtension(propIdOrEntityId);
            if (!string.IsNullOrEmpty(clean) && (PropRegistry.ContainsKey(clean) || ResourceRegistry.ContainsKey(clean)))
                return true;

            return false;
        }

        public void DeleteStaticPropAtPosition(string propId, Vector3 hitPos)
        {
            if (EcsWorld == null) return;
            Entity targetEntity = Entity.Null;
            float minDistance = 2.0f;

            var propQuery = Realm.Ecs.Common.QueryCache.AllPropIdentityAndPositionQuery;
            EcsWorld.Query(in propQuery, (Entity entity, ref PropIdentity pid, ref Position pos) =>
            {
                if (EntityToProp3D.ContainsKey(entity)) return;
                if (!string.IsNullOrEmpty(propId) && !pid.PropId.Equals(propId, StringComparison.OrdinalIgnoreCase)) return;

                Vector3 wPos = new Vector3(pos.Value.X, pos.Value.Y, pos.Value.Z);
                float d = wPos.DistanceTo(hitPos);
                if (d < minDistance)
                {
                    minDistance = d;
                    targetEntity = entity;
                }
            });

            if (targetEntity != Entity.Null && EcsWorld.IsAlive(targetEntity))
            {
                string pid = EcsWorld.Get<PropIdentity>(targetEntity).PropId;
                EcsWorld.Destroy(targetEntity);
                Realm.Client.PropMultiMeshManager.Instance?.MarkDirty(pid);
            }
        }

        public Realm.Client.Prop3D SpawnPropExternal(string propId, Vector3 position)
        {
            float rotY = IsMapEditorMode ? EditorPlacementRotation : 0f;
            float scale = IsMapEditorMode ? EditorPlacementScale : 1f;
            return SpawnPropExternalWithParams(propId, position, rotY, scale);
        }

        private Realm.Client.Decal3D FindDecal3DInParentChain(Node node)
        {
            if (!IsMapEditorMode) return null;
            Node current = node;
            while (current != null && current != this)
            {
                if (current is Realm.Client.Decal3D d) return d;
                current = current.GetParent();
            }
            return null;
        }

        private void ProcessMapEditorPhysics(float fDelta)
        {
            if (_simulationService == null || EcsWorld == null) return;

            _simulationService.TickEditorPhysics(fDelta);
            var query = Realm.Ecs.Common.QueryCache.AllPositionAndMoveToAndMovementStatsNoneDeadQuery;
            var arrivedUnits = _simulationService.GetEditorArrivedUnits();
            arrivedUnits.Clear();
            EcsWorld.Query(in query, _simulationService.EditorMovementQueryDelegate);

            foreach (var entity in arrivedUnits)
            {
                if (EcsWorld.IsAlive(entity) && EcsWorld.Has<MoveTo>(entity))
                {
                    EcsWorld.Remove<MoveTo>(entity);
                }
            }
        }

        private ImageTexture GenerateNormalMap(Image albedoImg, int sKey)
        {
            int w = Math.Min(albedoImg.GetWidth(), 512);
            int h = Math.Min(albedoImg.GetHeight(), 512);

            var workImg = albedoImg.Duplicate() as Image;
            if (workImg == null) return null;

            if (workImg.GetWidth() != w || workImg.GetHeight() != h) workImg.Resize(w, h, Image.Interpolation.Bilinear);
            workImg.Convert(Image.Format.Rgba8);

            var normImg = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
            float scale = (sKey / 20f) * 4.0f;

            for (int y = 0; y < h; y++)
            {
                ProcessNormalMapRow(w, h, y, workImg, normImg, scale);
            }

            return ImageTexture.CreateFromImage(normImg);
        }

        private void ProcessNormalMapRow(int w, int h, int y, Image workImg, Image normImg, float scale)
        {
            int yPrev = y > 0 ? y - 1 : y;
            int yNext = y < h - 1 ? y + 1 : y;

            for (int x = 0; x < w; x++)
            {
                int xPrev = x > 0 ? x - 1 : x;
                int xNext = x < w - 1 ? x + 1 : x;

                float lLeft = workImg.GetPixel(xPrev, y).Luminance;
                float lRight = workImg.GetPixel(xNext, y).Luminance;
                float lUp = workImg.GetPixel(x, yPrev).Luminance;
                float lDown = workImg.GetPixel(x, yNext).Luminance;

                float dx = (lRight - lLeft) * scale;
                float dy = (lDown - lUp) * scale;
                float dz = 1.0f;
                float len = Mathf.Sqrt(dx * dx + dy * dy + dz * dz);

                float nx = (-dx / len) * 0.5f + 0.5f;
                float ny = (-dy / len) * 0.5f + 0.5f;
                float nz = (dz / len) * 0.5f + 0.5f;

                normImg.SetPixel(x, y, new Color(nx, ny, nz, 1.0f));
            }
        }

        public void ApplyDecalPropertiesFromMetadata(Decal decal, string decalId)
        {
            if (decal == null || !GodotObject.IsInstanceValid(decal) || string.IsNullOrEmpty(decalId)) return;
            try
            {
                ApplyDecalPropertiesFromMetadataInternal(decal, decalId);
            }
            catch { }
        }

        private void ApplyDecalPropertiesFromMetadataInternal(Decal decal, string decalId)
        {
            ApplyDecalTextures(decal, decalId, out bool hasNormal);
            Realm.Shared.Metadata.DecalMetadata meta = LoadAndGetDecalMetadata(decalId);
            ApplyDecalRendering(decal, decalId, meta, hasNormal);
            ApplyDecalAnimation(decal, meta);
        }

        private void ApplyDecalTextures(Decal decal, string decalId, out bool hasNormal)
        {
            var assetData = LoadDecalAsset(decalId);
            decal.TextureAlbedo = assetData.PrimaryTexture;
            hasNormal = assetData.PrimaryNormal != null;
            if (hasNormal) decal.TextureNormal = assetData.PrimaryNormal;
        }

        private Realm.Shared.Metadata.DecalMetadata LoadAndGetDecalMetadata(string decalId)
        {
            string wsPath = Realm.Client.Services.MapWorkspaceService.GetActiveWorkspacePath();
            var metadata = Realm.Shared.Services.MapFileService.LoadMetadata(wsPath);
            return TryGetDecalMetadata(metadata, decalId);
        }

        private void ApplyDecalRendering(Decal decal, string decalId, Realm.Shared.Metadata.DecalMetadata meta, bool hasNormal)
        {
            float brightness = GetDecalRenderingFloat(meta?.Brightness, 1.0f);
            Color tint = GetDecalColorTint(meta);
            float contrast = GetDecalRenderingFloat(meta?.Contrast, 1.0f);
            float saturation = GetDecalRenderingFloat(meta?.Saturation, 1.0f);
            float opacity = GetDecalRenderingFloat(meta?.Opacity, 1.0f);
            float albedoMix = GetDecalRenderingFloat(meta?.AlbedoMix, 1.0f);
            float normalStrength = GetDecalRenderingFloat(meta?.NormalStrength, hasNormal ? 1.0f : 0.0f);
            float roughness = GetDecalRenderingFloat(meta?.Roughness, 1.0f);
            float metallic = meta?.Metallic ?? 0.0f;
            string blendMode = meta?.BlendMode ?? "Mix";

            ApplyDecalRenderingProperties(
                decal, brightness, tint, contrast, saturation, opacity, albedoMix,
                normalStrength, roughness, metallic, blendMode, decalId
            );
        }

        private void ApplyDecalAnimation(Decal decal, Realm.Shared.Metadata.DecalMetadata meta)
        {
            float upperFade = GetDecalRenderingFloat(meta?.UpperFade, 0.3f);
            float lowerFade = GetDecalRenderingFloat(meta?.LowerFade, 0.3f);
            ApplyDecalAnimationProperties(decal, meta, upperFade, lowerFade);
        }

        private float GetDecalRenderingFloat(float? val, float def)
        {
            return val > 0f ? val.Value : def;
        }

        private Color GetDecalColorTint(Realm.Shared.Metadata.DecalMetadata meta)
        {
            if (meta != null && !string.IsNullOrEmpty(meta.Tint) && meta.Tint.StartsWith("#"))
                return Color.FromHtml(meta.Tint);
            return Colors.White;
        }

        private Realm.Shared.Metadata.DecalMetadata TryGetDecalMetadata(Realm.Shared.Metadata.MapMetadata metadata, string decalId)
        {
            if (metadata?.Decals == null) return null;

            if (metadata.Decals.TryGetValue(decalId, out var d0)) return d0;

            string key = System.IO.Path.GetFileName(decalId);
            if (metadata.Decals.TryGetValue(key, out var d1)) return d1;

            string baseKey = System.IO.Path.GetFileNameWithoutExtension(decalId);
            if (metadata.Decals.TryGetValue(baseKey, out var d2)) return d2;
            if (metadata.Decals.TryGetValue($"{baseKey}.rtex", out var d3)) return d3;
            if (metadata.Decals.TryGetValue($"{baseKey}.png", out var d4)) return d4;

            return null;
        }

        private void ApplyDecalAnimationProperties(Decal decal, Realm.Shared.Metadata.DecalMetadata meta, float upperFade, float lowerFade)
        {
            if (decal is Realm.Client.Decal3D d3d)
            {
                ApplyDecal3DAnimationProperties(d3d, meta, upperFade, lowerFade);
            }
            else
            {
                decal.UpperFade = upperFade;
                decal.LowerFade = lowerFade;
            }
        }

        private void ApplyDecal3DAnimationProperties(Realm.Client.Decal3D d3d, Realm.Shared.Metadata.DecalMetadata meta, float upperFade, float lowerFade)
        {
            if (meta == null) return;

            bool animateOpacity = meta.AnimateOpacity;
            float opacityPulseSpeed = GetDecalRenderingFloat(meta.OpacityPulseSpeed, 1.0f);
            float minOpacity = GetDecalRenderingFloat(meta.MinOpacity, 0.2f);
            float maxOpacity = GetDecalRenderingFloat(meta.MaxOpacity, 1.0f);

            bool animateEmission = meta.AnimateEmission;
            float emissionPulseSpeed = GetDecalRenderingFloat(meta.EmissionPulseSpeed, 1.0f);
            float minEmission = meta.MinEmission;
            float maxEmission = GetDecalRenderingFloat(meta.MaxEmission, 2.0f);

            bool animateScale = meta.AnimateScale;
            float scalePulseSpeed = GetDecalRenderingFloat(meta.ScalePulseSpeed, 1.0f);
            float minScaleRatio = GetDecalRenderingFloat(meta.MinScaleRatio, 0.8f);
            float maxScaleRatio = GetDecalRenderingFloat(meta.MaxScaleRatio, 1.2f);

            d3d.SetPropertyAnimation(
                animateOpacity, opacityPulseSpeed, minOpacity, maxOpacity,
                animateEmission, emissionPulseSpeed, minEmission, maxEmission,
                animateScale, scalePulseSpeed, minScaleRatio, maxScaleRatio,
                upperFade, lowerFade
            );
        }

        public void DeleteNodeExternal(Node node)
        {
            if (node == null || !GodotObject.IsInstanceValid(node))
            {
                Realm.Client.UI.MapEditorHUD.Instance?.ShowFeedbackExternal("[Debug] DeleteNodeExternal: node is NULL or invalid");
                return;
            }

            if (DeleteUnitExternal(node)) return;
            if (DeletePropExternal(node)) return;
            if (DeleteDecalExternal(node)) return;
            if (DeleteVfxExternal(node)) return;

            if (_selectedEditorObject == node)
            {
                SelectedEditorObject = null;
            }
            node.QueueFree();
        }

        private bool DeleteUnitExternal(Node node)
        {
            var unit = (node as Realm.Client.Unit3D) ?? FindUnit3DInParentChain(node);
            if (unit != null && GodotObject.IsInstanceValid(unit))
            {
                if (unit == _selectedEditorObject || FindUnit3DInParentChain(_selectedEditorObject) == unit)
                {
                    SelectedEditorObject = null;
                }
                SelectedUnits.Remove(unit);
                AllUnits.Remove(unit);
                EntityToUnit3D.Remove(unit.Entity);
                if (EcsWorld.IsAlive(unit.Entity))
                {
                    EcsWorld.Destroy(unit.Entity);
                }
                unit.QueueFree();
                return true;
            }
            return false;
        }

        private bool DeletePropExternal(Node node)
        {
            var prop = (node as Realm.Client.Prop3D) ?? FindProp3DInParentChain(node);
            if (prop != null && GodotObject.IsInstanceValid(prop))
            {
                if (prop == _selectedEditorObject || FindProp3DInParentChain(_selectedEditorObject) == prop)
                {
                    SelectedEditorObject = null;
                }
                string propId = prop.PropId;
                AllProps.Remove(prop);
                EntityToProp3D.Remove(prop.Entity);
                if (EcsWorld.IsAlive(prop.Entity))
                {
                    EcsWorld.Destroy(prop.Entity);
                }
                prop.QueueFree();
                Realm.Client.PropMultiMeshManager.Instance?.MarkDirty(propId);
                return true;
            }
            return false;
        }

        private bool DeleteDecalExternal(Node node)
        {
            var decal = (node as Decal) ?? FindDecalInParentChain(node);
            if (decal != null && GodotObject.IsInstanceValid(decal))
            {
                if (decal == _selectedEditorObject || FindDecalInParentChain(_selectedEditorObject) == decal)
                {
                    SelectedEditorObject = null;
                }
                AllDecals.Remove(decal);
                if (decal is Realm.Client.Decal3D decal3D && EcsWorld.IsAlive(decal3D.Entity))
                {
                    EcsWorld.Destroy(decal3D.Entity);
                }
                decal.QueueFree();
                return true;
            }
            return false;
        }

        private bool DeleteVfxExternal(Node node)
        {
            var vfx = (node as ProceduralVfxInstance3D) ?? FindVfxInParentChain(node);
            if (vfx != null && GodotObject.IsInstanceValid(vfx))
            {
                if (vfx == _selectedEditorObject || FindVfxInParentChain(_selectedEditorObject) == vfx)
                {
                    SelectedEditorObject = null;
                }
                AllVfx.Remove(vfx);
                EntityToVfx3D.Remove(vfx.Entity);
                if (EcsWorld.IsAlive(vfx.Entity))
                {
                    EcsWorld.Destroy(vfx.Entity);
                }
                vfx.QueueFree();
                return true;
            }
            return false;
        }

        public IEditorAction DeleteObjectAtWithUndo(Node collider, Vector3 hitPos)
        {
            if (collider == null || !GodotObject.IsInstanceValid(collider))
            {
                Realm.Client.UI.MapEditorHUD.Instance?.ShowFeedbackExternal("[Debug] DeleteObjectAtWithUndo: collider is NULL or invalid");
                return null;
            }

            var directAction = DeleteByDirectParentChain(collider);
            if (directAction != null) return directAction;

            var proxAction = DeleteByProximitySearch(hitPos);
            if (proxAction != null) return proxAction;

            Realm.Client.UI.MapEditorHUD.Instance?.ShowFeedbackExternal("[Debug] DeleteObjectAtWithUndo returned NULL (no direct or proximity match)");
            return null;
        }

        private IEditorAction DeleteByDirectParentChain(Node collider)
        {
            var unitAction = TryDeleteUnitFromParent(collider);
            if (unitAction != null) return unitAction;

            var propAction = TryDeletePropFromParent(collider);
            if (propAction != null) return propAction;

            var decalAction = TryDeleteDecalFromParent(collider);
            if (decalAction != null) return decalAction;

            var vfxAction = TryDeleteVfxFromParent(collider);
            if (vfxAction != null) return vfxAction;

            return null;
        }

        private IEditorAction TryDeleteUnitFromParent(Node collider)
        {
            var unit = (collider as Realm.Client.Unit3D) ?? FindUnit3DInParentChain(collider);
            if (unit != null && GodotObject.IsInstanceValid(unit))
            {
                if (unit == _selectedEditorObject || FindUnit3DInParentChain(_selectedEditorObject) == unit) SelectedEditorObject = null;
                var action = new ObjectDeleteAction("unit", unit.UnitId, unit.Position, unit.RotationDegrees.Y, unit.Scale.X, unit.IsEnemy, unit, unit.Player);
                SelectedUnits.Remove(unit);
                AllUnits.Remove(unit);
                EntityToUnit3D.Remove(unit.Entity);
                if (EcsWorld.IsAlive(unit.Entity)) EcsWorld.Destroy(unit.Entity);
                unit.QueueFree();
                return action;
            }
            return null;
        }

        private IEditorAction TryDeletePropFromParent(Node collider)
        {
            var prop = (collider as Realm.Client.Prop3D) ?? FindProp3DInParentChain(collider);
            if (prop != null && GodotObject.IsInstanceValid(prop))
            {
                if (prop == _selectedEditorObject || FindProp3DInParentChain(_selectedEditorObject) == prop) SelectedEditorObject = null;
                string propId = prop.PropId;
                var action = new ObjectDeleteAction("prop", propId, prop.Position, prop.RotationDegrees.Y, prop.Scale.X, false, prop);
                AllProps.Remove(prop);
                EntityToProp3D.Remove(prop.Entity);
                if (EcsWorld.IsAlive(prop.Entity)) EcsWorld.Destroy(prop.Entity);
                prop.QueueFree();
                Realm.Client.PropMultiMeshManager.Instance?.MarkDirty(propId);
                return action;
            }
            return null;
        }

        private IEditorAction TryDeleteDecalFromParent(Node collider)
        {
            var decal = (collider as Decal) ?? FindDecalInParentChain(collider);
            if (decal != null && GodotObject.IsInstanceValid(decal))
            {
                if (decal == _selectedEditorObject || FindDecalInParentChain(_selectedEditorObject) == decal) SelectedEditorObject = null;
                string decalId = decal is Realm.Client.Decal3D d3d ? d3d.DecalId : "logo";
                var action = new ObjectDeleteAction("decal", decalId, decal.Position, decal.RotationDegrees.Y, decal.Scale.X, false, decal);
                AllDecals.Remove(decal);
                if (decal is Realm.Client.Decal3D d3 && EcsWorld.IsAlive(d3.Entity)) EcsWorld.Destroy(d3.Entity);
                decal.QueueFree();
                return action;
            }
            return null;
        }

        private IEditorAction TryDeleteVfxFromParent(Node collider)
        {
            var vfx = (collider as ProceduralVfxInstance3D) ?? FindVfxInParentChain(collider);
            if (vfx != null && GodotObject.IsInstanceValid(vfx))
            {
                if (vfx == _selectedEditorObject || FindVfxInParentChain(_selectedEditorObject) == vfx) SelectedEditorObject = null;
                string vfxId = vfx.Config?.VfxId ?? "vfx";
                var action = new ObjectDeleteAction("vfx", vfxId, vfx.Position, vfx.RotationDegrees.Y, vfx.Scale.X, false, vfx);
                AllVfx.Remove(vfx);
                EntityToVfx3D.Remove(vfx.Entity);
                if (EcsWorld.IsAlive(vfx.Entity)) EcsWorld.Destroy(vfx.Entity);
                vfx.QueueFree();
                return action;
            }
            return null;
        }

        private IEditorAction DeleteByProximitySearch(Vector3 hitPos)
        {
            var (closestUnit, closestUnitDist) = FindClosestUnitDistance(hitPos);
            var (closestProp, closestPropDist) = FindClosestPropDistance(hitPos);
            var (closestStaticPropEntity, closestStaticPropId, closestStaticPropPos, closestStaticPropRotY, closestStaticPropScale, closestStaticPropDist) = FindClosestStaticPropDistance(hitPos);
            var (closestDecal, closestDecalDist) = FindClosestDecalDistance(hitPos);
            var (closestVfx, closestVfxDist) = FindClosestVfxDistance(hitPos);

            float minDistance = Mathf.Min(closestUnitDist, Mathf.Min(closestPropDist, Mathf.Min(closestStaticPropDist, Mathf.Min(closestDecalDist, closestVfxDist))));
            if (minDistance >= 2.0f) return null;

            var dynAction = TryExecuteDynamicProximityDelete(minDistance, closestUnit, closestUnitDist, closestProp, closestPropDist, closestDecal, closestDecalDist, closestVfx, closestVfxDist);
            if (dynAction != null) return dynAction;

            if (closestStaticPropEntity != Entity.Null && minDistance == closestStaticPropDist)
                return ExecuteStaticPropProximityDelete(closestStaticPropEntity, closestStaticPropId, closestStaticPropPos, closestStaticPropRotY, closestStaticPropScale);

            return null;
        }

        private IEditorAction TryExecuteDynamicProximityDelete(float minDistance, Realm.Client.Unit3D closestUnit, float closestUnitDist, Realm.Client.Prop3D closestProp, float closestPropDist, Decal closestDecal, float closestDecalDist, ProceduralVfxInstance3D closestVfx, float closestVfxDist)
        {
            if (closestUnit != null && minDistance == closestUnitDist)
                return ExecuteUnitProximityDelete(closestUnit);
            if (closestProp != null && minDistance == closestPropDist)
                return ExecutePropProximityDelete(closestProp);
            if (closestDecal != null && minDistance == closestDecalDist)
                return ExecuteDecalProximityDelete(closestDecal);
            if (closestVfx != null && minDistance == closestVfxDist)
                return ExecuteVfxProximityDelete(closestVfx);
            return null;
        }

        private (Realm.Client.Unit3D, float) FindClosestUnitDistance(Vector3 hitPos)
        {
            Realm.Client.Unit3D closestUnit = null;
            float closestDist = 2.0f;
            foreach (var u in AllUnits)
            {
                if (GodotObject.IsInstanceValid(u))
                {
                    float d = u.Position.DistanceTo(hitPos);
                    if (d < closestDist) { closestDist = d; closestUnit = u; }
                }
            }
            return (closestUnit, closestDist);
        }

        private (Realm.Client.Prop3D, float) FindClosestPropDistance(Vector3 hitPos)
        {
            Realm.Client.Prop3D closestProp = null;
            float closestDist = 2.0f;
            foreach (var p in AllProps)
            {
                if (GodotObject.IsInstanceValid(p))
                {
                    float d = p.Position.DistanceTo(hitPos);
                    if (d < closestDist) { closestDist = d; closestProp = p; }
                }
            }
            return (closestProp, closestDist);
        }

        private (Entity, string, Vector3, float, float, float) FindClosestStaticPropDistance(Vector3 hitPos)
        {
            Entity closestEntity = Entity.Null;
            string closestId = null;
            Vector3 closestPos = Vector3.Zero;
            float closestRotY = 0f;
            float closestScale = 1f;
            float closestDist = 2.0f;

            if (EcsWorld != null)
            {
                var propQuery = Realm.Ecs.Common.QueryCache.AllPropIdentityAndPositionQuery;
                EcsWorld.Query(in propQuery, (Entity entity, ref PropIdentity pId, ref Position pPos) =>
                {
                    if (EntityToProp3D.ContainsKey(entity)) return;
                    Vector3 wPos = new Vector3(pPos.Value.X, pPos.Value.Y, pPos.Value.Z);
                    float d = wPos.DistanceTo(hitPos);
                    if (d < closestDist)
                    {
                        closestDist = d;
                        closestEntity = entity;
                        closestId = pId.PropId;
                        closestPos = wPos;
                        closestRotY = EcsWorld.Has<RotationY>(entity) ? EcsWorld.Get<RotationY>(entity).Value : 0f;
                        closestScale = EcsWorld.Has<ModelScale>(entity) ? EcsWorld.Get<ModelScale>(entity).Value : 1f;
                    }
                });
            }
            return (closestEntity, closestId, closestPos, closestRotY, closestScale, closestDist);
        }

        private (Decal, float) FindClosestDecalDistance(Vector3 hitPos)
        {
            Decal closestDecal = null;
            float closestDist = 2.0f;
            foreach (var decalObj in AllDecals)
            {
                if (GodotObject.IsInstanceValid(decalObj))
                {
                    float d = decalObj.GlobalPosition.DistanceTo(hitPos);
                    if (d < closestDist) { closestDist = d; closestDecal = decalObj; }
                }
            }
            return (closestDecal, closestDist);
        }

        private (ProceduralVfxInstance3D, float) FindClosestVfxDistance(Vector3 hitPos)
        {
            ProceduralVfxInstance3D closestVfx = null;
            float closestDist = 2.0f;
            foreach (var vfxObj in AllVfx)
            {
                if (GodotObject.IsInstanceValid(vfxObj))
                {
                    float d = vfxObj.GlobalPosition.DistanceTo(hitPos);
                    if (d < closestDist) { closestDist = d; closestVfx = vfxObj; }
                }
            }
            return (closestVfx, closestDist);
        }

        private IEditorAction ExecuteUnitProximityDelete(Realm.Client.Unit3D closestUnit)
        {
            if (closestUnit == _selectedEditorObject) SelectedEditorObject = null;
            var action = new ObjectDeleteAction("unit", closestUnit.UnitId, closestUnit.Position, closestUnit.RotationDegrees.Y, closestUnit.Scale.X, closestUnit.IsEnemy, closestUnit, closestUnit.Player);
            SelectedUnits.Remove(closestUnit);
            AllUnits.Remove(closestUnit);
            EntityToUnit3D.Remove(closestUnit.Entity);
            if (EcsWorld.IsAlive(closestUnit.Entity)) EcsWorld.Destroy(closestUnit.Entity);
            closestUnit.QueueFree();
            return action;
        }

        private IEditorAction ExecutePropProximityDelete(Realm.Client.Prop3D closestProp)
        {
            if (closestProp == _selectedEditorObject) SelectedEditorObject = null;
            string propId = closestProp.PropId;
            var action = new ObjectDeleteAction("prop", propId, closestProp.Position, closestProp.RotationDegrees.Y, closestProp.Scale.X, false, closestProp);
            AllProps.Remove(closestProp);
            EntityToProp3D.Remove(closestProp.Entity);
            if (EcsWorld.IsAlive(closestProp.Entity)) EcsWorld.Destroy(closestProp.Entity);
            closestProp.QueueFree();
            Realm.Client.PropMultiMeshManager.Instance?.MarkDirty(propId);
            return action;
        }

        private IEditorAction ExecuteStaticPropProximityDelete(Entity closestStaticPropEntity, string closestStaticPropId, Vector3 closestStaticPropPos, float closestStaticPropRotY, float closestStaticPropScale)
        {
            var action = new ObjectDeleteAction("prop", closestStaticPropId, closestStaticPropPos, closestStaticPropRotY, closestStaticPropScale, false, null);
            EcsWorld.Destroy(closestStaticPropEntity);
            Realm.Client.PropMultiMeshManager.Instance?.MarkDirty(closestStaticPropId);
            return action;
        }

        private IEditorAction ExecuteDecalProximityDelete(Decal closestDecal)
        {
            if (closestDecal == _selectedEditorObject) SelectedEditorObject = null;
            string decalId = closestDecal is Realm.Client.Decal3D d3d ? d3d.DecalId : "logo";
            var action = new ObjectDeleteAction("decal", decalId, closestDecal.Position, closestDecal.RotationDegrees.Y, closestDecal.Scale.X, false, closestDecal);
            AllDecals.Remove(closestDecal);
            if (closestDecal is Realm.Client.Decal3D d3 && EcsWorld.IsAlive(d3.Entity)) EcsWorld.Destroy(d3.Entity);
            closestDecal.QueueFree();
            return action;
        }

        private IEditorAction ExecuteVfxProximityDelete(ProceduralVfxInstance3D closestVfx)
        {
            if (closestVfx == _selectedEditorObject) SelectedEditorObject = null;
            string vfxId = closestVfx.Config?.VfxId ?? "vfx";
            var action = new ObjectDeleteAction("vfx", vfxId, closestVfx.Position, closestVfx.RotationDegrees.Y, closestVfx.Scale.X, false, closestVfx);
            AllVfx.Remove(closestVfx);
            EntityToVfx3D.Remove(closestVfx.Entity);
            if (EcsWorld.IsAlive(closestVfx.Entity)) EcsWorld.Destroy(closestVfx.Entity);
            closestVfx.QueueFree();
            return action;
        }

        private void UpdateEditorPreview(Vector3 position)
        {
            bool needsPreview = ActiveEditorTool == EditorTool.PlaceUnit ||
                                ActiveEditorTool == EditorTool.PlaceProp ||
                                ActiveEditorTool == EditorTool.PlaceDecal ||
                                ActiveEditorTool == EditorTool.PlaceVfx;

            if (!needsPreview)
            {
                ClearEditorPreview();
                return;
            }

            string reqType = ActiveEditorTool.ToString();
            string reqId = ActivePlaceId;
            bool reqIsEnemy = PlaceUnitIsEnemy;

            UpdatePreviewNodeIfNeeded(reqType, reqId, reqIsEnemy);

            if (_editorPreviewNode != null && GodotObject.IsInstanceValid(_editorPreviewNode))
            {
                ApplyAllGlobalOverridesToObject(_editorPreviewNode);

                if (EditorClumpMode || ActiveEditorTool == EditorTool.PlacePropClump)
                {
                    _editorPreviewNode.Visible = false;
                    return;
                }

                PositionEditorPreview(position);
            }
        }

        private void UpdatePreviewNodeIfNeeded(string reqType, string reqId, bool reqIsEnemy)
        {
            if (_editorPreviewNode != null && GodotObject.IsInstanceValid(_editorPreviewNode) &&
                _editorPreviewType == reqType && _editorPreviewId == reqId && _editorPreviewIsEnemy == reqIsEnemy)
            {
                return;
            }

            ClearEditorPreview();

            _editorPreviewType = reqType;
            _editorPreviewId = reqId;
            _editorPreviewIsEnemy = reqIsEnemy;

            if (ActiveEditorTool == EditorTool.PlaceUnit) CreateOrUpdatePreviewUnit(reqId, reqIsEnemy);
            else if (ActiveEditorTool == EditorTool.PlaceProp) CreateOrUpdatePreviewProp(reqId);
            else if (ActiveEditorTool == EditorTool.PlaceDecal) CreateOrUpdatePreviewDecal(reqId);
            else if (ActiveEditorTool == EditorTool.PlaceVfx) CreateOrUpdatePreviewVfx(reqId);
        }

        private void CreateOrUpdatePreviewUnit(string reqId, bool reqIsEnemy)
        {
            if (!UnitRegistry.ContainsKey(reqId) && !BuildingRegistry.ContainsKey(reqId))
            {
                LoadUnitMetadata(!string.IsNullOrEmpty(CurrentMapDirectory) ? CurrentMapDirectory : Godot.ProjectSettings.GlobalizePath(Realm.Client.UI.MapEditorHUD.TempWorkspaceGodotPath));
            }

            bool isBuilding = false;
            Realm.Shared.Metadata.UnitMetadata meta;
            if (!UnitRegistry.TryGetValue(reqId, out meta) && BuildingRegistry.TryGetValue(reqId, out meta))
                isBuilding = true;

            if (meta.TemplateID != null)
            {
                string targetModel = !string.IsNullOrEmpty(meta.ModelPath) ? meta.ModelPath : reqId;
                string modelPath = GetFallbackModelPath(targetModel, isBuilding);

                var previewUnit = new Realm.Client.Unit3D();
                previewUnit.UnitId = reqId;
                previewUnit.IsBuilding = isBuilding;
                previewUnit.IsEnemy = reqIsEnemy;
                previewUnit.IsPreview = true;
                AddChild(previewUnit);
                previewUnit.LoadModel(modelPath);

                Color color = reqIsEnemy ? new Color(1.0f, 0.3f, 0.15f) : new Color(0.15f, 0.65f, 1.0f);
                MakeHologramRecursive(previewUnit, color);
                _editorPreviewNode = previewUnit;
            }
        }

        private void CreateOrUpdatePreviewProp(string reqId)
        {
            if (!HasPreviewPropRequirement(reqId))
            {
                LoadUnitMetadata(!string.IsNullOrEmpty(CurrentMapDirectory) ? CurrentMapDirectory : Godot.ProjectSettings.GlobalizePath(Realm.Client.UI.MapEditorHUD.TempWorkspaceGodotPath));
                if (!HasPreviewPropRequirement(reqId))
                {
                    return;
                }
            }

            var previewProp = new Realm.Client.Prop3D();
            previewProp.PropId = reqId;
            previewProp.IsPreview = true;
            AddChild(previewProp);

            Color color = new Color(0.95f, 0.82f, 0.15f);
            MakeHologramRecursive(previewProp, color);
            _editorPreviewNode = previewProp;
        }

        private bool HasPreviewPropRequirement(string reqId)
        {
            if (PropRegistry.ContainsKey(reqId) || ResourceRegistry.ContainsKey(reqId)) return true;
            string cleanReqId = System.IO.Path.GetFileNameWithoutExtension(reqId);
            if (!string.IsNullOrEmpty(cleanReqId) && (PropRegistry.ContainsKey(cleanReqId) || ResourceRegistry.ContainsKey(cleanReqId))) return true;
            return false;
        }

        private void CreateOrUpdatePreviewDecal(string reqId)
        {
            var previewDecal = new Realm.Client.Decal3D();
            previewDecal.CullMask = Realm.Client.RuntimeTerrain.TerrainDecalCullMask;
            previewDecal.DecalId = string.IsNullOrEmpty(reqId) ? "logo" : reqId;
            AddChild(previewDecal);
            ApplyDecalPropertiesFromMetadata(previewDecal, reqId);
            previewDecal.Size = new Vector3(6.0f, 20.0f, 6.0f) * EditorPlacementScale;
            previewDecal.AlbedoMix = 0.5f;
            _editorPreviewNode = previewDecal;
        }

        private void CreateOrUpdatePreviewVfx(string reqId)
        {
            Realm.Shared.Metadata.VfxAttachmentConfig config;
            if (VfxRegistry.TryGetValue(reqId, out var regCfg))
            {
                config = regCfg.Clone();
            }
            else if (Enum.TryParse<Realm.Shared.Metadata.VfxPrimitiveType>(reqId, true, out var primType))
            {
                config = new Realm.Shared.Metadata.VfxAttachmentConfig { VfxId = reqId, PrimitiveType = primType };
            }
            else
            {
                config = new Realm.Shared.Metadata.VfxAttachmentConfig { VfxId = reqId, Name = reqId };
            }

            var previewVfx = new ProceduralVfxInstance3D(config);
            previewVfx.IsPreview = true;
            AddChild(previewVfx);
            _editorPreviewNode = previewVfx;
        }

        private void PositionEditorPreview(Vector3 position)
        {
            if (!_editorService.HasCachedRandom) _editorService.GenerateNewRandomPlacementRotationAndScale();
            float previewRot = (EditorRandomRotation && !_editorService.IsPastingObject) ? _editorService.CachedRandomRotation : EditorPlacementRotation;
            float previewScaleVal = (EditorRandomScale && !_editorService.IsPastingObject) ? _editorService.CachedRandomScale : EditorPlacementScale;

            Vector3 previewPos = CalculateEditorPreviewPosition(position, previewScaleVal);

            float safePreviewScale = previewScaleVal <= 0.001f ? 1.0f : previewScaleVal;
            
            if (_editorPreviewNode is ProceduralVfxInstance3D previewVfxNode)
            {
                PositionVfxPreview(previewVfxNode, previewPos, previewRot, safePreviewScale);
            }
            else if (_editorPreviewNode is Decal previewDecal)
            {
                PositionDecalPreview(previewDecal, previewPos, previewRot, safePreviewScale);
            }
            else
            {
                PositionStandardPreview(_editorPreviewNode, previewPos, previewRot, safePreviewScale);
            }
            
            _editorPreviewNode.Visible = true;
        }

        private Vector3 CalculateEditorPreviewPosition(Vector3 position, float previewScaleVal)
        {
            Vector3 previewPos = position;
            if (EditorSnapToGrid && GroundTerrain != null)
            {
                previewPos = _editorService.SnapToGrid(previewPos);
            }
            if (ActiveEditorTool != EditorTool.PlaceDecal)
            {
                previewPos.Y = _editorService.GetTerrainHeightAt(previewPos);
            }
            if (ActiveEditorTool == EditorTool.PlaceUnit || ActiveEditorTool == EditorTool.PlaceProp)
            {
                float radius = GetPlacementRadius(ActivePlaceId, previewScaleVal);
                var finalPos = FindNearestFreePosition(previewPos, radius);
                if (finalPos != null)
                {
                    previewPos = finalPos.Value;
                }
            }
            return previewPos;
        }

        private void PositionVfxPreview(ProceduralVfxInstance3D previewVfxNode, Vector3 previewPos, float previewRot, float safePreviewScale)
        {
            float normalOffset = previewVfxNode.Config?.SurfaceNormalOffset ?? 0.02f;
            if (previewVfxNode.Config?.PlacementMode == Realm.Shared.Metadata.VfxPlacementMode.SurfaceSnap)
            {
                Vector3 hitNormal = GetTerrainNormalAt(previewPos);
                Basis alignedBasis = CreateAlignedBasis(hitNormal);
                previewVfxNode.Basis = alignedBasis;
                previewVfxNode.RotateObjectLocal(Vector3.Up, Mathf.DegToRad(previewRot));
                previewVfxNode.Position = previewPos + hitNormal * normalOffset;
            }
            else
            {
                previewVfxNode.Position = previewPos + Vector3.Up * normalOffset;
                previewVfxNode.RotationDegrees = new Vector3(0.0f, previewRot, 0.0f);
            }
            previewVfxNode.Scale = Vector3.One * safePreviewScale;
        }

        private void PositionDecalPreview(Decal previewDecal, Vector3 previewPos, float previewRot, float safePreviewScale)
        {
            Vector3 hitNormal = GetTerrainNormalAt(previewPos);
            var mousePos = GetViewport().GetMousePosition();
            var terrainHit = RaycastTerrainFromMouse(mousePos);
            if (terrainHit != null && terrainHit.ContainsKey("normal"))
            {
                hitNormal = terrainHit["normal"].AsVector3();
            }
            Basis alignedBasis = CreateAlignedBasis(hitNormal);
            previewDecal.Basis = alignedBasis;
            previewDecal.RotateObjectLocal(Vector3.Up, Mathf.DegToRad(previewRot));
            previewDecal.Position = previewPos;
            previewDecal.Size = new Vector3(6.0f, 20.0f, 6.0f) * safePreviewScale;
            previewDecal.Scale = Vector3.One;
        }

        private void PositionStandardPreview(Node3D previewNode, Vector3 previewPos, float previewRot, float safePreviewScale)
        {
            previewNode.Position = previewPos;
            previewNode.RotationDegrees = new Vector3(0.0f, previewRot, 0.0f);
            previewNode.Scale = Vector3.One * safePreviewScale;
        }

        private void ClearEditorPreview()
        {
            if (_editorPreviewNode != null && GodotObject.IsInstanceValid(_editorPreviewNode))
            {
                _editorPreviewNode.QueueFree();
            }
            _editorPreviewNode = null;
            _editorPreviewType = "";
            _editorPreviewId = "";
            _editorPreviewIsEnemy = false;
        }

        private void MakeHologramRecursive(Node node, Color color)
        {
            if (node is MeshInstance3D meshInstance)
            {
                var mat = new StandardMaterial3D();
                mat.AlbedoColor = new Color(color.R, color.G, color.B, 0.4f);
                mat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
                mat.EmissionEnabled = true;
                mat.Emission = new Color(color.R, color.G, color.B) * 0.5f;
                meshInstance.MaterialOverride = mat;
            }
            foreach (var child in node.GetChildren())
            {
                MakeHologramRecursive(child, color);
            }
        }

        private void ProcessMapEditorTick(float fDelta)
        {
            _editorService.TickClumpCooldown(fDelta);

            var mousePos = GetViewport().GetMousePosition();
            var terrainHit = RaycastTerrainFromMouse(mousePos);
            Vector3 hitPos = Vector3.Zero;
            bool hasHit = false;

            if (terrainHit != null && terrainHit.ContainsKey("position"))
            {
                hitPos = terrainHit["position"].AsVector3();
                hasHit = true;
            }
            else
            {
                var camera = GetViewport().GetCamera3D();
                if (camera != null)
                {
                    var from = camera.ProjectRayOrigin(mousePos);
                    var normal = camera.ProjectRayNormal(mousePos);
                    if (Mathf.Abs(normal.Y) > 0.0001f)
                    {
                        float t = (0.0f - from.Y) / normal.Y;
                        hitPos = from + normal * t;
                        hasHit = true;
                    }
                }
            }

            if (hasHit)
            {
                UpdateBrushIndicator(hitPos);
                UpdateEditorPreview(hitPos);
                HandleMapEditorTerrainHit(hitPos);
                HandleMapEditorMouseHover(mousePos);
                HandleMapEditorClickEvents(hitPos, mousePos, fDelta);
            }
            else
            {
                ClearEditorPreviewState();
            }

            ProcessMapEditorPhysics(fDelta);
        }

        private void HandleMapEditorTerrainHit(Vector3 hitPos)
        {
            if (GroundTerrain == null) return;

            if (ProcessAreaSelectionTool(hitPos)) return;
            if (ProcessCoordinateTool(hitPos)) return;
            if (ProcessPasteTool(hitPos)) return;
            if (ProcessMeasureTool(hitPos)) return;
        }

        private bool ProcessAreaSelectionTool(Vector3 hitPos)
        {
            if (ActiveEditorTool == EditorTool.SelectArea && _editorService.IsSelectingArea && _editorService.SelectionStart != null)
            {
                HandleSelectAreaHit(hitPos);
                return true;
            }
            return false;
        }

        private bool ProcessCoordinateTool(Vector3 hitPos)
        {
            if (ActiveEditorTool == EditorTool.DrawCoordinate && _editorService.IsSelectingArea && _editorService.SelectionStart != null)
            {
                HandleDrawCoordinateHit(hitPos);
                return true;
            }
            return false;
        }

        private bool ProcessPasteTool(Vector3 hitPos)
        {
            if (ActiveEditorTool == EditorTool.PasteArea && _editorService.HasCopiedArea)
            {
                HandlePasteAreaHit(hitPos);
                return true;
            }
            return false;
        }

        private bool ProcessMeasureTool(Vector3 hitPos)
        {
            if (ActiveEditorTool == EditorTool.Measure && EditorTapeMeasureActive && EditorTapeMeasureStart.HasValue)
            {
                HandleMeasureHit(hitPos);
                return true;
            }
            return false;
        }

        private void HandleSelectAreaHit(Vector3 hitPos)
        {
            var (cx, cz) = _editorService.WorldPosToCellCoords(hitPos);
            _editorService.SetSelectionEnd(new Vector2I(cx, cz));
            int minX = Mathf.Min(_editorService.SelectionStart.Value.X, cx);
            int minZ = Mathf.Min(_editorService.SelectionStart.Value.Y, cz);
            int maxX = Mathf.Max(_editorService.SelectionStart.Value.X, cx);
            int maxZ = Mathf.Max(_editorService.SelectionStart.Value.Y, cz);
            CreateSelectionHighlight();
            RebuildSelectionHighlightMesh(minX, minZ, maxX, maxZ);
        }

        private void HandleDrawCoordinateHit(Vector3 hitPos)
        {
            var (rcx, rcz) = _editorService.WorldPosToCellCoords(hitPos);
            _editorService.SetSelectionEnd(new Vector2I(rcx, rcz));
            int rMinX = Mathf.Min(_editorService.SelectionStart.Value.X, rcx);
            int rMinZ = Mathf.Min(_editorService.SelectionStart.Value.Y, rcz);
            int rMaxX = Mathf.Max(_editorService.SelectionStart.Value.X, rcx);
            int rMaxZ = Mathf.Max(_editorService.SelectionStart.Value.Y, rcz);
            UpdateCoordinatePreviewMesh(rMinX, rMinZ, rMaxX, rMaxZ);
        }

        private void HandlePasteAreaHit(Vector3 hitPos)
        {
            var (cx, cz) = _editorService.WorldPosToCellCoords(hitPos);
            var (startX, startZ, targetWidth, targetDepth) = _editorService.GetAnchoredPasteBounds(cx, cz, EditorPasteRotation, EditorPasteReflection);

            UpdatePasteSelectionHighlights(startX, startZ, targetWidth, targetDepth);

            int srcAnchorX = _editorService.CopiedAreaSourceMinX + _editorService.CopiedAreaAnchorX;
            int srcAnchorZ = _editorService.CopiedAreaSourceMinZ + _editorService.CopiedAreaAnchorZ;
            int deltaX = cx - srcAnchorX;
            int deltaZ = cz - srcAnchorZ;

            float quadSize = GroundTerrain.QuadSize;
            float halfW = GroundTerrain.Width / 2.0f;
            float halfD = GroundTerrain.Depth / 2.0f;
            Vector2 targetWorld2D = new Vector2((cx - halfW) * quadSize, (cz - halfD) * quadSize);
            Vector2 pivotWorld2D = _editorService.SymmetryPivot;
            Vector2 diffFromPivot = targetWorld2D - pivotWorld2D;
            float distFromPivot = diffFromPivot.Length();
            float angleFromPivot = Mathf.RadToDeg(Mathf.Atan2(diffFromPivot.Y, diffFromPivot.X));
            if (angleFromPivot < 0) angleFromPivot += 360.0f;

            Realm.Client.UI.MapEditorHUD.Instance?.UpdatePasteTelemetry(deltaX, deltaZ, distFromPivot / quadSize, distFromPivot, angleFromPivot);
        }

        private void HandleMeasureHit(Vector3 hitPos)
        {
            EditorTapeMeasureEnd = hitPos;
            UpdateMeasureVisuals(EditorTapeMeasureStart.Value, hitPos);

            Vector3 delta = hitPos - EditorTapeMeasureStart.Value;
            float quadSize = GroundTerrain.QuadSize;
            float dx = delta.X / quadSize;
            float dz = delta.Z / quadSize;
            float dy = delta.Y;
            float eucTiles = Mathf.Sqrt(dx * dx + dz * dz);
            float eucWorld = delta.Length();
            float manhattanTiles = Mathf.Abs(dx) + Mathf.Abs(dz);
            float angleDeg = Mathf.RadToDeg(Mathf.Atan2(delta.Z, delta.X));
            if (angleDeg < 0) angleDeg += 360.0f;
            float slopePct = eucTiles > 0.001f ? (Mathf.Abs(dy) / (eucTiles * quadSize)) * 100.0f : 0.0f;

            Realm.Client.UI.MapEditorHUD.Instance?.UpdateMeasureTelemetry(eucTiles, eucWorld, manhattanTiles, dx, dz, dy, angleDeg, slopePct);
        }

        private void HandleMapEditorMouseHover(Vector2 mousePos)
        {
            bool canHover = (ActiveEditorTool == EditorTool.SelectMove && !_isDraggingObject) ||
                            ActiveEditorTool == EditorTool.DeleteObject ||
                            ActiveEditorTool == EditorTool.Eyedropper;
                            
            Node newHovered = null;
            if (canHover && !IsMouseOverUI())
            {
                newHovered = ResolveHoveredEditorObject(mousePos);
            }

            if (_hoveredEditorObject != newHovered)
            {
                SetHoverState(_hoveredEditorObject, false);
                _hoveredEditorObject = newHovered;
                SetHoverState(_hoveredEditorObject, true);
            }
        }

        private Node ResolveHoveredEditorObject(Vector2 mousePos)
        {
            var objectHit = RaycastFromMouse(mousePos);
            var collider = (objectHit != null && objectHit.ContainsKey("collider")) ? objectHit["collider"].As<Node>() : null;
            
            Node newHovered = null;
            if (collider != null)
            {
                newHovered = FindUnit3DInParentChain(collider);
                if (newHovered == null) newHovered = FindProp3DInParentChain(collider);
            }
            if (newHovered == null) newHovered = FindDecal3DInParentChain(collider);
            if (newHovered == null) newHovered = FindVfxInParentChain(collider);
            
            return newHovered;
        }

        private void SetHoverState(Node node, bool isHovered)
        {
            if (GodotObject.IsInstanceValid(node))
            {
                if (node is Realm.Client.Unit3D u) u.IsHovered = isHovered;
                else if (node is Realm.Client.Prop3D p) p.IsHovered = isHovered;
                else if (node is Decal d) UpdateDecalHoverRing(d, isHovered);
                else if (node is ProceduralVfxInstance3D v) v.IsHovered = isHovered;
            }
        }

        private void HandleMapEditorClickEvents(Vector3 hitPos, Vector2 mousePos, float fDelta)
        {
            if (Input.IsMouseButtonPressed(MouseButton.Left) && !_leftClickInitiatedOverUI && !Realm.Client.UI.MapEditor.FloatingDialogBase.HasAnyDialogOpen)
            {
                TryStartDragOperation(mousePos);

                if (!IsMouseOverUI())
                {
                    TryHandleClumpSpawn(hitPos);
                    TryHandleObjectDragging(hitPos, mousePos);
                    TryApplyTerrainEditing(hitPos, fDelta);
                }
            }
            else
            {
                ClearEditorDragAndDrawState();
            }
        }

        private void TryStartDragOperation(Vector2 mousePos)
        {
            if (_is3DLeftClickDown && !_is3DDragOperationActive && mousePos.DistanceTo(_leftClick3DStartPos) > 3.0f)
            {
                _is3DDragOperationActive = true;
                Realm.Client.UI.MapEditorHUD.Instance?.Set3DInteractionActive(true);
            }
        }

        private void TryHandleClumpSpawn(Vector3 hitPos)
        {
            if ((ActiveEditorTool == EditorTool.PlaceUnit || ActiveEditorTool == EditorTool.PlaceProp || ActiveEditorTool == EditorTool.PlaceDecal) && EditorClumpMode)
            {
                if (!_editorService.IsDrawingClump)
                {
                    _editorService.BeginClumpSession();
                }
                if (_editorService.CanSpawnClump())
                {
                    ApplyGeneralClumpSpawn(hitPos);
                    _editorService.SetClumpCooldown(0.15f);
                }
            }
        }

        private void TryHandleObjectDragging(Vector3 hitPos, Vector2 mousePos)
        {
            if (ActiveEditorTool == EditorTool.SelectMove && _isDraggingObject && GodotObject.IsInstanceValid(SelectedEditorObject))
            {
                float mouseDistPx = mousePos.DistanceTo(_dragStartMousePos);
                if (!_dragObjectHasMoved && mouseDistPx > 4.0f)
                {
                    _dragObjectHasMoved = true;
                    if (!_is3DDragOperationActive)
                    {
                        _is3DDragOperationActive = true;
                        Realm.Client.UI.MapEditorHUD.Instance?.Set3DInteractionActive(true);
                    }
                }

                if (_dragObjectHasMoved)
                {
                    UpdateDraggedObjectPosition(hitPos);
                }
            }
        }

        private void TryApplyTerrainEditing(Vector3 hitPos, float fDelta)
        {
            bool isTerrainTool = IsTerrainEditingTool(ActiveEditorTool);

            bool firstClick = false;
            if (isTerrainTool && !_editorService.IsDrawingTerrain && GroundTerrain != null)
            {
                firstClick = true;
                float targetBlockHeight = ActiveEditorTool == EditorTool.Height ? EditorExactHeight : EditorBlockLevelHeight;
                _editorService.BeginTerrainDraw(hitPos, ActiveEditorTool, EditorBlockMode, targetBlockHeight, null, GroundTerrain.SplatMap, GroundTerrain.PathingCodes, GroundTerrain.CliffSplatMap);
            }

            ApplyContinuousTerrainEditing(hitPos, fDelta, firstClick);
        }

        private bool IsTerrainEditingTool(EditorTool tool)
        {
            return tool == EditorTool.Raise ||
                   tool == EditorTool.Lower ||
                   tool == EditorTool.Height ||
                   tool == EditorTool.Smooth ||
                   tool == EditorTool.Plateau ||
                   tool == EditorTool.PaintTexture ||
                   tool == EditorTool.Noise ||
                   tool == EditorTool.PaintPathing;
        }

        private void UpdateDraggedObjectPosition(Vector3 hitPos)
        {
            var node3D = SelectedEditorObject as Node3D;
            Vector3 delta = hitPos - _dragStartGroundPos;
            Vector3 dragPos = _dragObjectStartPos + delta;
            if (EditorSnapToGrid && GroundTerrain != null)
            {
                dragPos = _editorService.SnapToGrid(dragPos);
            }
            float authoredYOffset = _dragObjectStartPos.Y - _editorService.GetTerrainHeightAt(_dragObjectStartPos);
            dragPos.Y = _editorService.GetTerrainHeightAt(dragPos) + (Mathf.Abs(authoredYOffset) < 0.05f ? 0f : authoredYOffset);
            node3D.Position = dragPos;
            
            if (SelectedEditorObject is Realm.Client.Unit3D unit)
            {
                UpdateDraggedUnitECS(unit, dragPos);
            }
            else if (SelectedEditorObject is Realm.Client.Prop3D prop)
            {
                UpdateDraggedPropECS(prop, dragPos);
            }
            else if (SelectedEditorObject is Realm.Client.Decal3D decal3D)
            {
                UpdateDraggedDecalECS(decal3D, dragPos);
            }
            else if (SelectedEditorObject is ProceduralVfxInstance3D vfxInst)
            {
                UpdateDraggedVfxECS(vfxInst, dragPos);
            }
            
            Realm.Client.UI.MapEditorHUD.Instance?.UpdateSelectedObjectInfo();
        }

        private void UpdateDraggedUnitECS(Realm.Client.Unit3D unit, Vector3 dragPos)
        {
            if (EcsWorld.IsAlive(unit.Entity))
            {
                EcsWorld.Set(unit.Entity, new Position(new System.Numerics.Vector3(dragPos.X, dragPos.Y, dragPos.Z)));
                UpdateEditorCoverageOverlay();
            }
        }

        private void UpdateDraggedPropECS(Realm.Client.Prop3D prop, Vector3 dragPos)
        {
            if (EcsWorld.IsAlive(prop.Entity))
            {
                EcsWorld.Set(prop.Entity, new Position(new System.Numerics.Vector3(dragPos.X, dragPos.Y, dragPos.Z)));
            }
            Realm.Client.PropMultiMeshManager.Instance?.MarkDirty(prop.PropId);
        }

        private void UpdateDraggedDecalECS(Realm.Client.Decal3D decal3D, Vector3 dragPos)
        {
            if (EcsWorld.IsAlive(decal3D.Entity))
            {
                EcsWorld.Set(decal3D.Entity, new Position(new System.Numerics.Vector3(dragPos.X, dragPos.Y, dragPos.Z)));
            }
        }

        private void UpdateDraggedVfxECS(ProceduralVfxInstance3D vfxInst, Vector3 dragPos)
        {
            if (EcsWorld.IsAlive(vfxInst.Entity))
            {
                EcsWorld.Set(vfxInst.Entity, new Position(new System.Numerics.Vector3(dragPos.X, dragPos.Y, dragPos.Z)));
            }
        }

        private void ClearEditorDragAndDrawState()
        {
            Clear3DDragOperationState();
            ClearClumpSessionState();

            _editorService.ResetDrawState();

            ClearDraggingState();
            ClearSelectionState();
            ClearTerrainDrawingState();
        }

        private void Clear3DDragOperationState()
        {
            if (_is3DDragOperationActive)
            {
                _is3DDragOperationActive = false;
                _is3DLeftClickDown = false;
                Realm.Client.UI.MapEditorHUD.Instance?.Set3DInteractionActive(false);
            }
        }

        private void ClearClumpSessionState()
        {
            if (_editorService.IsDrawingClump)
            {
                var composite = _editorService.EndClumpSession();
                if (composite != null)
                {
                    EditorHistoryManager.RecordAction(composite);
                    EditorHasUnsavedChanges = true;
                }
            }
        }

        private void ClearDraggingState()
        {
            if (_isDraggingObject)
            {
                _isDraggingObject = false;
                if (GodotObject.IsInstanceValid(SelectedEditorObject))
                {
                    var node3D = SelectedEditorObject as Node3D;
                    bool isUnit = SelectedEditorObject is Realm.Client.Unit3D;
                    bool isEnemy = isUnit ? (SelectedEditorObject as Realm.Client.Unit3D).IsEnemy : false;
                    if (SelectedEditorObject is Realm.Client.Prop3D prop && SelectedEditorObject is not Realm.Client.Unit3D)
                    {
                        Realm.Client.PropMultiMeshManager.Instance?.MarkDirty(prop.PropId);
                    }
                    if (node3D.Position.DistanceTo(_dragObjectStartPos) > 0.05f)
                    {
                        var action = new ObjectTransformAction(node3D, _dragObjectStartPos, node3D.Position, _dragObjectStartRot, node3D.RotationDegrees, _dragObjectStartScale, node3D.Scale, _dragObjectStartIsEnemy, isEnemy);
                        EditorHistoryManager.RecordAction(action);
                        Realm.Client.UI.MapEditorHUD.Instance?.ShowFeedbackExternal("Moved Object");
                        EditorHasUnsavedChanges = true;
                    }
                }
            }
        }

        private void ClearSelectionState()
        {
            if (_editorService.IsSelectingArea)
            {
                _editorService.SetIsSelectingArea(false);
            }
        }

        private void ClearTerrainDrawingState()
        {
            if (!_editorService.IsDrawingTerrain) return;

            if (GroundTerrain != null && GroundTerrain.Cells != null && GroundTerrain.SplatMap != null && GroundTerrain.PathingCodes != null)
            {
                var action = _editorService.EndTerrainDraw(null, GroundTerrain.SplatMap, GroundTerrain.PathingCodes, GroundTerrain.CliffSplatMap);
                EditorHistoryManager.RecordAction(action);
                
                if (IsTerrainHeightsTool(ActiveEditorTool))
                {
                    GroundTerrain.UpdatePhysics();
                    RebuildGridOverlayMeshExternal();
                    UpdatePathingOverlay();
                }
                FlushTerrainMeshAndPhysics();
                EditorHasUnsavedChanges = true;
            }
            else
            {
                _editorService.EndTerrainDraw(null, null, null, null);
            }
        }

        private bool IsTerrainHeightsTool(EditorTool tool)
        {
            return tool == EditorTool.Raise || 
                   tool == EditorTool.Lower || 
                   tool == EditorTool.Height || 
                   tool == EditorTool.Smooth || 
                   tool == EditorTool.Plateau || 
                   tool == EditorTool.Noise || 
                   tool == EditorTool.PaintPathing;
        }

        private void ClearEditorPreviewState()
        {
            if (_is3DDragOperationActive)
            {
                _is3DDragOperationActive = false;
                _is3DLeftClickDown = false;
                Realm.Client.UI.MapEditorHUD.Instance?.Set3DInteractionActive(false);
            }

            if (_brushIndicatorMesh != null)
                _brushIndicatorMesh.Visible = false;
            ClearEditorPreview();
            
            ClearEditorDragAndDrawState();
        }

        public void StartMapEditorMode()
        {
            Realm.Client.ReplaySystem.ReplayPlaybackManager.Instance.StopReplay();
            IsMapEditorMode = true;

            string wsPath = Godot.ProjectSettings.GlobalizePath(Realm.Client.UI.MapEditorHUD.TempWorkspaceGodotPath);
            try
            {
                System.IO.Directory.CreateDirectory(wsPath);
                Realm.Client.Services.MapWorkspaceService.SetupWorkspace(wsPath, "MapScript");
            }
            catch (Exception ex)
            {
                GD.PrintErr($"Failed setting up map workspace: {ex.Message}");
            }

            ActiveEditorTool = EditorTool.None;
            EditorHistoryManager.Clear();

            if (Realm.Client.UI.MapEditorHUD.ReturningFromTest)
            {
                RestoreMapEditorStateFromTest();
            }
            else
            {
                ResetWorldAndState();
                ClearMapEntirely();
            }

            CreateBrushIndicator();
            UpdateGridOverlayVisibility();
            InitializeCameraBoundsOverlay();
            UpdateEditorShadows();
            UpdateDayNightVisuals(0.0f);
            GroundTerrain?.SetShroudEnabled(false);

            if (AllVfx != null)
            {
                foreach (var vfx in AllVfx)
                {
                    if (vfx != null && GodotObject.IsInstanceValid(vfx))
                    {
                        vfx.SetEditorBaseRingVisible(true);
                    }
                }
            }
        }

        private void RestoreMapEditorStateFromTest()
        {
            try
            {
                LoadMapFromFile(Realm.Client.UI.MapEditorHUD.TempWorkspaceGodotPath + "/terrain.json");

                EditorGridMode = Realm.Client.UI.MapEditorHUD.SavedGridMode;
                EditorCameraBoundsVisible = Realm.Client.UI.MapEditorHUD.SavedCameraBoundsVisible;
                EditorDisableShadows = Realm.Client.UI.MapEditorHUD.SavedDisableShadows;

                var camera = MainCamera as Realm.Client.CameraControl;
                if (camera != null)
                {
                    camera.Position = Realm.Client.UI.MapEditorHUD.SavedCameraPosition;
                    if (EcsWorld != null && EcsWorld.IsAlive(WorldEntity) && EcsWorld.Has<CameraState>(WorldEntity))
                    {
                        ref var state = ref EcsWorld.Get<CameraState>(WorldEntity);
                        state.TargetHeight = Realm.Client.UI.MapEditorHUD.SavedTargetHeight;
                        state.CurrentHeight = Realm.Client.UI.MapEditorHUD.SavedTargetHeight;
                        state.TargetYaw = Realm.Client.UI.MapEditorHUD.SavedTargetYaw;
                        state.CurrentYaw = Realm.Client.UI.MapEditorHUD.SavedTargetYaw;
                        state.TargetPitch = Realm.Client.UI.MapEditorHUD.SavedTargetPitch;
                        state.CurrentPitch = Realm.Client.UI.MapEditorHUD.SavedTargetPitch;
                        state.IsTopDown = Realm.Client.UI.MapEditorHUD.SavedIsTopDown;
                        state.YawSwing = Realm.Client.UI.MapEditorHUD.SavedYawSwing;
                        state.PitchSwing = Realm.Client.UI.MapEditorHUD.SavedPitchSwing;
                    }
                }
            }
            catch (Exception ex)
            {
                GD.PrintErr($"Failed loading map state: {ex.Message}. Resetting to blank map.");
                ResetWorldAndState();
                ClearMapEntirely();
            }
        }

        public void ExitMapEditorMode()
        {
            IsMapEditorMode = false;
            ActiveEditorTool = EditorTool.None;
            EditorHistoryManager.Clear();
            ClearEditorPreview();
            UpdateEditorShadows();

            if (AllVfx != null)
            {
                foreach (var vfx in AllVfx)
                {
                    if (vfx != null && GodotObject.IsInstanceValid(vfx))
                    {
                        vfx.SetEditorBaseRingVisible(false);
                    }
                }
            }

            if (_brushIndicatorMesh != null)
            {
                _brushIndicatorMesh.QueueFree();
                _brushIndicatorMesh = null;
            }

            if (GroundTerrain != null)
            {
                GroundTerrain.SetGridVisible(false);
                GroundTerrain.SetPathingVisible(false);
                GroundTerrain.SetShroudEnabled(true);
            }

            if (_cameraBoundsOverlayMesh != null)
            {
                _cameraBoundsOverlayMesh.QueueFree();
                _cameraBoundsOverlayMesh = null;
            }

            HideCoordinateSelectionOutline();
            HideCoordinatePreviewMesh();
            RebuildAllCoordinatePersistentMeshes();

            var groundNode = GetNodeOrNull("Ground");
            if (groundNode != null)
            {
                groundNode.QueueFree();
                RemoveChild(groundNode);
            }

            CreateGround();
        }

        private void CreateBrushIndicator()
        {
            if (_brushIndicatorMesh != null) return;

            _brushIndicatorMesh = new MeshInstance3D();
            _brushIndicatorMesh.Name = "BrushIndicator";

            var mat = new StandardMaterial3D();
            mat.AlbedoColor = new Color(0.15f, 0.65f, 1.0f, 0.3f);
            mat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
            mat.EmissionEnabled = true;
            mat.Emission = new Color(0.15f, 0.65f, 1.0f) * 0.5f;
            mat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
            _brushIndicatorMesh.MaterialOverride = mat;

            AddChild(_brushIndicatorMesh);
            _brushIndicatorMesh.Visible = false;

            UpdateBrushMesh();
        }

        public void UpdateBrushMesh()
        {
            if (_brushIndicatorMesh == null) return;
            if (EditorBrushIsSquare)
            {
                var plane = new PlaneMesh();
                plane.Size = new Vector2(Realm.Client.EditableTerrain.DefaultQuadSize, Realm.Client.EditableTerrain.DefaultQuadSize);
                _brushIndicatorMesh.Mesh = plane;
            }
            else
            {
                var torus = new TorusMesh();
                torus.InnerRadius = 0.95f;
                torus.OuterRadius = 1.05f;
                _brushIndicatorMesh.Mesh = torus;
            }
        }
        private void UpdateBrushIndicator(Vector3 position)
        {
            if (_brushIndicatorMesh == null) return;

            bool isVertexTool = IsVertexTool(ActiveEditorTool);
            Vector3 targetPos = GetBrushTargetPosition(position, isVertexTool);

            _brushIndicatorMesh.Position = new Vector3(targetPos.X, targetPos.Y + 0.1f, targetPos.Z);
            _brushIndicatorMesh.Scale = new Vector3(EditorBrushRadius, 0.1f, EditorBrushRadius);

            _brushIndicatorMesh.Visible = IsTerrainTool(isVertexTool);
        }

        private bool IsVertexTool(EditorTool tool)
        {
            return tool == EditorTool.Raise ||
                   tool == EditorTool.Lower ||
                   tool == EditorTool.Height ||
                   tool == EditorTool.Smooth ||
                   tool == EditorTool.Plateau ||
                   tool == EditorTool.PaintTexture ||
                   tool == EditorTool.Noise ||
                   tool == EditorTool.Ramp;
        }

        private bool IsTerrainTool(bool isVertexTool)
        {
            return isVertexTool ||
                   ActiveEditorTool == EditorTool.PlacePropClump ||
                   ActiveEditorTool == EditorTool.PaintPathing ||
                   ((ActiveEditorTool == EditorTool.PlaceUnit || ActiveEditorTool == EditorTool.PlaceProp || ActiveEditorTool == EditorTool.PlaceDecal) && EditorClumpMode);
        }

        private Vector3 GetBrushTargetPosition(Vector3 position, bool isVertexTool)
        {
            if (GroundTerrain != null)
            {
                if (isVertexTool) return _editorService.SnapToVertex(position);
                if (EditorSnapToGrid) return _editorService.SnapToGrid(position);
            }
            return position;
        }

        public void ClearMeasureVisuals()
        {
            if (_measureMeshInstance != null)
            {
                _measureMeshInstance.Visible = false;
                _measureImmediateMesh?.ClearSurfaces();
            }
            EditorTapeMeasureStart = null;
            EditorTapeMeasureEnd = null;
            EditorTapeMeasureActive = false;
        }

        public void UpdateSymmetryPivotVisuals()
        {
            if (!IsMapEditorMode || GroundTerrain == null)
            {
                if (_symmetryPivotMarkerMesh != null) _symmetryPivotMarkerMesh.Visible = false;
                return;
            }

            bool showPivot = EditorMirrorMode != MirrorMode.Rotational && (EditorMirrorMode != MirrorMode.None || EditorPolarOverlayVisible);
            if (!showPivot)
            {
                if (_symmetryPivotMarkerMesh != null) _symmetryPivotMarkerMesh.Visible = false;
                return;
            }

            if (_symmetryPivotMarkerMesh == null)
            {
                _symmetryPivotMarkerMesh = new MeshInstance3D();
                _symmetryPivotMarkerMesh.Name = "SymmetryPivotMarker";
                var imm = new ImmediateMesh();
                _symmetryPivotMarkerMesh.Mesh = imm;
                var mat = new StandardMaterial3D
                {
                    ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                    AlbedoColor = new Color(0.2f, 0.9f, 1.0f, 0.95f),
                    Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                    CullMode = BaseMaterial3D.CullModeEnum.Disabled
                };
                _symmetryPivotMarkerMesh.MaterialOverride = mat;
                AddChild(_symmetryPivotMarkerMesh);
            }

            var immMesh = (ImmediateMesh)_symmetryPivotMarkerMesh.Mesh;
            immMesh.ClearSurfaces();
            immMesh.SurfaceBegin(Mesh.PrimitiveType.Lines);

            float px = _editorService.SymmetryPivot.X;
            float pz = _editorService.SymmetryPivot.Y;
            float py = _editorService.GetTerrainHeightAt(new Vector3(px, 0, pz)) + 0.25f;
            float s = 1.5f;

            immMesh.SurfaceAddVertex(new Vector3(px - s, py, pz));
            immMesh.SurfaceAddVertex(new Vector3(px + s, py, pz));
            immMesh.SurfaceAddVertex(new Vector3(px, py, pz - s));
            immMesh.SurfaceAddVertex(new Vector3(px, py, pz + s));

            immMesh.SurfaceEnd();
            _symmetryPivotMarkerMesh.Visible = true;
        }

        public void ClearRampStartPosExternal()
        {
            _editorService.SetRampStartPos(null);
        }

        public List<MirroredTransform> GetMirroredTransforms(Vector3 pos, float rotation)
        {
            return _editorService.GetMirroredTransforms(pos, rotation, EditorMirrorMode);
        }

        private bool ApplyRampInternal(Vector3 start, Vector3 end)
        {
            return _editorService.ApplyRamp(start, end, EditorBrushRadius, EditorBlockMode, EditorBlockLevelHeight);
        }

        private void ApplyGeneralClumpSpawn(Vector3 centerPos)
        {
            float autoDetectedRadius = GetOrCalculateObstacleRadius(ActivePlaceId, _editorPreviewNode);
            string assetKey = GetModelAssetKey(_editorPreviewNode ?? (object)ActivePlaceId);
            float ratio = GetModelCollisionCircleRatio(assetKey);
            float assetBaseCollisionRadius = Mathf.Max(0.1f, autoDetectedRadius * ratio);

            var requests = _editorService.BuildClumpSpawnRequests(
                centerPos,
                ActiveEditorTool,
                ActivePlaceId,
                PlaceUnitIsEnemy,
                EditorPlacementScale,
                EditorClumpCount,
                EditorClumpScale,
                EditorBrushRadius,
                EditorBrushIsSquare,
                EditorRandomRotation,
                EditorRandomScale,
                EditorPlacementRotation,
                EditorMirrorMode,
                assetBaseCollisionRadius);

            foreach (var req in requests)
            {
                Node spawnedNode = null;
                if (req.Type == "unit")
                    spawnedNode = SpawnUnitExternal(req.Id, req.Position, req.IsEnemy, req.Rotation, req.Scale);
                else if (req.Type == "prop")
                    spawnedNode = SpawnPropExternalWithParams(req.Id, req.Position, req.Rotation, req.Scale);
                else if (req.Type == "decal")
                    spawnedNode = SpawnDecalExternalWithParams(req.Id, req.Position, req.Rotation, req.Scale);

                if (spawnedNode != null)
                {
                    _editorService.RecordClumpSpawnAction(new ObjectSpawnAction(req.Type, req.Id, req.Position, req.Rotation, req.Scale, req.IsEnemy, spawnedNode));
                }
            }
        }

        public void PerformFloodFill(Vector3 clickPos, int fillTextureIndex, bool isCliff = false)
        {
            if (GroundTerrain == null || GroundTerrain.Cells == null || GroundTerrain.SplatMap == null) return;

            if (GroundTerrain.CliffSplatMap == null)
            {
                GroundTerrain.CliffSplatMap = new TerrainSplatWeights[GroundTerrain.Width + 1, GroundTerrain.Depth + 1];
            }

            var splatBefore = (TerrainSplatWeights[,])GroundTerrain.SplatMap.Clone();
            var cliffBefore = (TerrainSplatWeights[,])GroundTerrain.CliffSplatMap.Clone();
            int cliffTextureIndex = EditorCliffPaintTextureIndex;

            var result = _editorService.PerformFloodFill(clickPos, fillTextureIndex, cliffTextureIndex, EditorMirrorMode, isCliff);
            if (result.SplatMap == null) return;

            if (result.IsCliff)
            {
                Array.Copy(result.SplatMap, GroundTerrain.CliffSplatMap, result.SplatMap.Length);
            }
            else
            {
                Array.Copy(result.SplatMap, GroundTerrain.SplatMap, result.SplatMap.Length);
            }

            GroundTerrain.UpdateMeshAndPhysics(false, false);
            var splatAfter = (TerrainSplatWeights[,])GroundTerrain.SplatMap.Clone();
            var cliffAfter = (TerrainSplatWeights[,])GroundTerrain.CliffSplatMap.Clone();
            var action = new TerrainModifyAction((Realm.Ecs.Components.Terrain.TerrainCell[,])null, (Realm.Ecs.Components.Terrain.TerrainCell[,])null, splatBefore, splatAfter, null, null, cliffBefore, cliffAfter);
            EditorHistoryManager.RecordAction(action);
            EditorHasUnsavedChanges = true;
            Realm.Client.UI.MapEditorHUD.Instance?.ShowFeedbackExternal(result.IsCliff ? "Flood filled cliff face area" : "Flood filled terrain area");
        }

        public void HideSelectionHighlight()
        {
            if (_selectionHighlightMesh != null)
            {
                _selectionHighlightMesh.Visible = false;
            }
            foreach (var symMesh in _symmetryHighlightMeshes)
            {
                if (GodotObject.IsInstanceValid(symMesh))
                {
                    symMesh.Visible = false;
                }
            }
            _editorService?.SetIsSelectingArea(false);
            _editorService?.SetSelectionStart(null);
            _editorService?.SetSelectionEnd(null);
        }

        public void UpdatePasteSelectionHighlights(int startX, int startZ, int targetWidth, int targetDepth)
        {
            if (GroundTerrain == null || GroundTerrain.Cells == null || !_editorService.HasCopiedArea) return;

            int width = GroundTerrain.Width;
            int depth = GroundTerrain.Depth;

            int minX = Mathf.Clamp(startX, 0, width - 1);
            int minZ = Mathf.Clamp(startZ, 0, depth - 1);
            int maxX = Mathf.Clamp(startX + targetWidth - 1, 0, width - 1);
            int maxZ = Mathf.Clamp(startZ + targetDepth - 1, 0, depth - 1);

            CreateSelectionHighlight();
            RebuildSelectionHighlightMesh(minX, minZ, maxX, maxZ);

            int symCount = 0;
            if (EditorMirrorMode != MirrorMode.None)
            {
                float quadSize = GroundTerrain.QuadSize;
                Vector3 centerPos = new Vector3((startX + targetWidth / 2.0f - width / 2.0f) * quadSize, 0, (startZ + targetDepth / 2.0f - depth / 2.0f) * quadSize);
                var transforms = _editorService.GetMirroredTransforms(centerPos, 0.0f, EditorMirrorMode);
                symCount = transforms.Count;
                for (int i = 0; i < transforms.Count; i++)
                {
                    var t = transforms[i];
                    var (rcx, rcz) = _editorService.WorldPosToCellCoords(t.Position);
                    int rStartX = rcx - targetWidth / 2;
                    int rStartZ = rcz - targetDepth / 2;
                    int rMinX = Mathf.Clamp(rStartX, 0, width - 1);
                    int rMinZ = Mathf.Clamp(rStartZ, 0, depth - 1);
                    int rMaxX = Mathf.Clamp(rStartX + targetWidth - 1, 0, width - 1);
                    int rMaxZ = Mathf.Clamp(rStartZ + targetDepth - 1, 0, depth - 1);

                    var meshInst = GetOrCreateSymmetryHighlightMesh(i);
                    BuildHighlightMeshForBounds(meshInst, rMinX, rMinZ, rMaxX, rMaxZ);
                    meshInst.Visible = true;
                }
            }

            for (int i = symCount; i < _symmetryHighlightMeshes.Count; i++)
            {
                if (GodotObject.IsInstanceValid(_symmetryHighlightMeshes[i]))
                {
                    _symmetryHighlightMeshes[i].Visible = false;
                }
            }
        }

        private bool ShouldIncludeQuad(int sx, int sz, int minX, int minZ, float selCenterX, float selCenterZ, float rx, float rz)
        {
            if (ActiveEditorTool == EditorTool.SelectArea)
            {
                return EditorBrushIsSquare || IsWithinBrushRadius(sx, sz, minX, minZ, selCenterX, selCenterZ, rx, rz);
            }

            if (ActiveEditorTool == EditorTool.PasteArea)
            {
                if (_editorService.HasCopiedArea && _editorService.HasCopiedAreaMask)
                {
                    return IsCopiedQuadMasked(sx, sz);
                }
                if (!EditorBrushIsSquare && !_editorService.HasCopiedArea)
                {
                    return IsWithinBrushRadius(sx, sz, minX, minZ, selCenterX, selCenterZ, rx, rz);
                }
            }

            return true;
        }

        private bool IsWithinBrushRadius(int sx, int sz, int minX, int minZ, float selCenterX, float selCenterZ, float rx, float rz)
        {
            float cellCenterX = minX + sx + 0.5f;
            float cellCenterZ = minZ + sz + 0.5f;
            float ndx = (cellCenterX - selCenterX) / rx;
            float ndz = (cellCenterZ - selCenterZ) / rz;
            return (ndx * ndx + ndz * ndz <= 1.05f);
        }

        private void CreateCoordinatePreviewMesh()
        {
            if (_coordinatePreviewMesh != null) return;
            _coordinatePreviewMesh = new MeshInstance3D();
            _coordinatePreviewMesh.Name = "CoordinatePreview";
            var mat = new StandardMaterial3D();
            mat.AlbedoColor = new Color(0.1f, 1.0f, 0.3f, 0.4f);
            mat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
            mat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
            mat.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
            _coordinatePreviewMesh.MaterialOverride = mat;
            AddChild(_coordinatePreviewMesh);
            _coordinatePreviewMesh.Visible = false;
        }

        public void UpdateCoordinatePreviewMesh(int minX, int minZ, int maxX, int maxZ)
        {
            CreateCoordinatePreviewMesh();
            RebuildCoordinateMeshInstance(_coordinatePreviewMesh, minX, minZ, maxX, maxZ, new Color(0.1f, 1.0f, 0.3f, 0.4f));
        }

        public void DeleteCoordinateExternal(string coordinateName)
        {
            var oldCoordinates = new List<EditorCoordinate>(EditorCoordinates);
            EditorCoordinates.RemoveAll(r => string.Equals(r.Name, coordinateName, StringComparison.OrdinalIgnoreCase));
            HideCoordinateSelectionOutline();
            HideSelectionHighlight();
            HideCoordinatePreviewMesh();
            RebuildAllCoordinatePersistentMeshes();

            var newCoordinates = new List<EditorCoordinate>(EditorCoordinates);
            var action = new CoordinateAction(oldCoordinates, newCoordinates);
            EditorHistoryManager.RecordAction(action);
            EditorHasUnsavedChanges = true;
        }

        public void RebuildAllCoordinatePersistentMeshes()
        {
            foreach (var mesh in _coordinatePersistentMeshes)
            {
                if (GodotObject.IsInstanceValid(mesh))
                {
                    RemoveChild(mesh);
                    mesh.QueueFree();
                }
            }
            _coordinatePersistentMeshes.Clear();

            if (ActiveEditorTool != EditorTool.DrawCoordinate)
            {
                HideCoordinatePreviewMesh();
            }

            if (!IsMapEditorMode || ActiveEditorTool != EditorTool.DrawCoordinate)
            {
                return;
            }

            if (GroundTerrain == null || GroundTerrain.Cells == null) return;

            int width = GroundTerrain.Width;
            int depth = GroundTerrain.Depth;
            float quadSize = GroundTerrain.QuadSize;

            foreach (var coord in EditorCoordinates)
            {
                int minX = Mathf.Clamp((int)Mathf.Round(coord.MinX / quadSize + width / 2.0f), 0, width);
                int minZ = Mathf.Clamp((int)Mathf.Round(coord.MinZ / quadSize + depth / 2.0f), 0, depth);
                int maxX = Mathf.Clamp((int)Mathf.Round(coord.MaxX / quadSize + width / 2.0f), 0, width);
                int maxZ = Mathf.Clamp((int)Mathf.Round(coord.MaxZ / quadSize + depth / 2.0f), 0, depth);

                var meshInst = new MeshInstance3D();
                meshInst.Name = $"Coordinate_{coord.Name}";
                var mat = new StandardMaterial3D();
                mat.AlbedoColor = new Color(0.1f, 0.9f, 0.3f, 0.25f);
                mat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
                mat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
                mat.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
                meshInst.MaterialOverride = mat;
                AddChild(meshInst);
                RebuildCoordinateMeshInstance(meshInst, minX, minZ, maxX, maxZ, new Color(0.1f, 0.9f, 0.3f, 0.25f));
                _coordinatePersistentMeshes.Add(meshInst);
            }
        }
        public void SelectCoordinateExternal(string coordinateName)
        {
            if (string.IsNullOrEmpty(coordinateName) || GroundTerrain == null || GroundTerrain.Cells == null)
            {
                HideCoordinateSelectionOutline();
                return;
            }

            EditorCoordinate? found = FindCoordinate(coordinateName);
            if (!found.HasValue)
            {
                HideCoordinateSelectionOutline();
                return;
            }

            UpdateCoordinateOutline(found.Value);
            FocusCameraOnCoordinate(found.Value);
        }

        private EditorCoordinate? FindCoordinate(string coordinateName)
        {
            foreach (var r in EditorCoordinates)
            {
                if (string.Equals(r.Name, coordinateName, StringComparison.OrdinalIgnoreCase)) return r;
            }
            return null;
        }

        public void PerformEraseAreaExternal()
        {
            PerformEraseArea();
            if (GroundTerrain != null && _editorService.SelectionStart != null && _editorService.SelectionEnd != null)
            {
                var (minX, minZ, maxX, maxZ) = _editorService.GetCurrentSelectionBounds();
                if (_selectionHighlightMesh != null && _selectionHighlightMesh.Visible)
                {
                    RebuildSelectionHighlightMesh(minX, minZ, maxX, maxZ);
                }
            }
        }

        public void PerformCutAreaExternal()
        {
            if (GroundTerrain == null || _editorService.SelectionStart == null || _editorService.SelectionEnd == null)
            {
                Realm.Client.UI.MapEditorHUD.Instance?.ShowFeedbackExternal("Nothing to Cut (select an area first)");
                return;
            }

            PerformCopyArea();
            PerformEraseArea();

            var (minX, minZ, maxX, maxZ) = _editorService.GetCurrentSelectionBounds();
            if (_selectionHighlightMesh != null && _selectionHighlightMesh.Visible)
            {
                RebuildSelectionHighlightMesh(minX, minZ, maxX, maxZ);
            }

            Realm.Client.UI.MapEditorHUD.Instance?.ShowFeedbackExternal("Area Cut");
        }

        public void RefreshSelectionHighlight()
        {
            if (GroundTerrain != null && _editorService.SelectionStart != null && _editorService.SelectionEnd != null)
            {
                var (minX, minZ, maxX, maxZ) = _editorService.GetCurrentSelectionBounds();
                if (_selectionHighlightMesh != null && _selectionHighlightMesh.Visible)
                {
                    RebuildSelectionHighlightMesh(minX, minZ, maxX, maxZ);
                }
            }
        }

        public void UpdateEditorShadows()
        {
            var sun = GetNodeOrNull<DirectionalLight3D>("DirectionalLight3D");
            if (sun != null && GodotObject.IsInstanceValid(sun))
            {
                GameSettings.ApplyDirectionalLightQuality(sun, GameSettings.QualityIdx);
            }
        }
    }
}
