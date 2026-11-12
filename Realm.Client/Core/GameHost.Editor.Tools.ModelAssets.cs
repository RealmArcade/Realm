using System;
using System.Collections.Generic;
using System.IO;
using Realm.Ecs;
using Godot;
using Realm.Client.ReplaySystem;
using Realm.Client.Services;
using Realm.Client.UI;
using Realm.Client.UI.MapEditor;
using Realm.Client.VFX;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Terrain;
using Realm.Ecs.Services;
using Realm.Shared.Services;

namespace Realm.Client.Core
{
	public partial class GameHost
	{
		public string GetModelAssetKey(object objOrId)
		{
			if (objOrId == null) return "";
			if (objOrId is Unit3D unit) return GetUnit3DModelPath(unit);
			if (objOrId is Prop3D prop) return GetProp3DModelPath(prop);
			if (objOrId is string str) return GetStringModelPath(str);
			return "";
		}

		private string GetUnit3DModelPath(Unit3D unit)
		{
			if (!string.IsNullOrEmpty(unit.ModelPath))
				return GameContext.EditorService.NormalizeModelAssetKey(unit.ModelPath);
			if (UnitRegistry != null && UnitRegistry.TryGetValue(unit.UnitId, out var meta) && !string.IsNullOrEmpty(meta.ModelPath))
				return GameContext.EditorService.NormalizeModelAssetKey(meta.ModelPath);
			if (BuildingRegistry != null && BuildingRegistry.TryGetValue(unit.UnitId, out var bldMeta) && !string.IsNullOrEmpty(bldMeta.ModelPath))
				return GameContext.EditorService.NormalizeModelAssetKey(bldMeta.ModelPath);
			return "";
		}

		private string GetProp3DModelPath(Prop3D prop)
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

			string clean = Path.GetFileNameWithoutExtension(str);
			if (!string.IsNullOrEmpty(clean) && !clean.Equals(str, StringComparison.OrdinalIgnoreCase))
			{
				string cleanPath = GetStringModelPath(clean, false);
				if (!string.IsNullOrEmpty(cleanPath)) return cleanPath;
			}

			return GameContext.EditorService.NormalizeModelAssetKey(str);
		}

		private string? CheckPropOrResourceRegistryForModelPath(string str)
		{
			if (PropRegistry != null && PropRegistry.TryGetValue(str, out var propMeta) && !string.IsNullOrEmpty(propMeta.ModelPath))
				return GameContext.EditorService.NormalizeModelAssetKey(propMeta.ModelPath);
			if (ResourceRegistry != null && ResourceRegistry.TryGetValue(str, out var resMeta) && !string.IsNullOrEmpty(resMeta.ModelPath))
				return GameContext.EditorService.NormalizeModelAssetKey(resMeta.ModelPath);
			return null;
		}

		private string? CheckUnitOrBuildingRegistryForModelPath(string str)
		{
			if (UnitRegistry != null && UnitRegistry.TryGetValue(str, out var unitMeta) && !string.IsNullOrEmpty(unitMeta.ModelPath))
				return GameContext.EditorService.NormalizeModelAssetKey(unitMeta.ModelPath);
			if (BuildingRegistry != null && BuildingRegistry.TryGetValue(str, out var bldMeta) && !string.IsNullOrEmpty(bldMeta.ModelPath))
				return GameContext.EditorService.NormalizeModelAssetKey(bldMeta.ModelPath);
			return null;
		}

		public string GetSelectedEntityOrAssetKey(object objOrId)
		{
			if (objOrId == null) return "";
			if (objOrId is Unit3D unit)
			{
				if (!string.IsNullOrEmpty(unit.UnitId)) return unit.UnitId;
				return GetModelAssetKey(unit);
			}

			if (objOrId is Prop3D prop)
			{
				if (!string.IsNullOrEmpty(prop.PropId)) return prop.PropId;
				return GetModelAssetKey(prop);
			}

			if (objOrId is LootItem3D lootItem)
			{
				if (!string.IsNullOrEmpty(lootItem.ItemTypeId)) return lootItem.ItemTypeId;
				return GetModelAssetKey(lootItem);
			}

			if (objOrId is string str) return str;
			return GetModelAssetKey(objOrId);
		}

		public bool MatchesEntityOrAssetKey(object objOrId, string targetKey)
		{
			if (objOrId == null || string.IsNullOrEmpty(targetKey)) return false;
			string normTarget = GameContext.EditorService.NormalizeModelAssetKey(targetKey);

			if (objOrId is Unit3D unit) return MatchesUnit3DKey(unit, normTarget);
			if (objOrId is Prop3D prop) return MatchesProp3DKey(prop, normTarget);
			if (objOrId is string str) return MatchesStringKey(str, normTarget);

			return GetModelAssetKey(objOrId) == normTarget;
		}

		private bool MatchesProp3DKey(Prop3D prop, string normTarget)
		{
			if (IsMatch(prop.PropId, normTarget)) return true;
			string cleanPropId = Path.GetFileNameWithoutExtension(prop.PropId);

			if (CheckPropRegistryForMatch(prop.PropId, cleanPropId, normTarget)) return true;
			if (CheckResourceRegistryForMatch(prop.PropId, cleanPropId, normTarget)) return true;
			if (CheckBuildingRegistryForMatch(prop.PropId, cleanPropId, normTarget)) return true;
			if (CheckUnitRegistryForMatch(prop.PropId, cleanPropId, normTarget)) return true;

			return false;
		}

		private bool MatchesStringKey(string str, string normTarget)
		{
			if (IsMatch(str, normTarget)) return true;
			string cleanStr = Path.GetFileNameWithoutExtension(str);

			if (CheckUnitRegistryForMatch(str, cleanStr, normTarget)) return true;
			if (CheckBuildingRegistryForMatch(str, cleanStr, normTarget)) return true;
			if (CheckPropRegistryForMatch(str, cleanStr, normTarget)) return true;
			if (CheckResourceRegistryForMatch(str, cleanStr, normTarget)) return true;

			return false;
		}

		private bool IsMatch(string? key, string normTarget)
		{
			return !string.IsNullOrEmpty(key) && GameContext.EditorService.NormalizeModelAssetKey(key) == normTarget;
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
			string norm = GameContext.EditorService.NormalizeModelAssetKey(assetKey);
			if (string.IsNullOrEmpty(norm)) return;

			GameContext.ModelOverrideService.ModelYOffsets[norm] = offset;

			UpdatePropsYOffset(norm, offset);
			UpdateUnitsYOffset(norm, offset);
			UpdatePreviewYOffset(norm, offset);

			GameContext.EditorService.ModelYOffsetSavePending = true;
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

			GameContext.PropMultiMesh?.MarkDirty(normKey);
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

			if (_editorPreviewNode is Unit3D previewUnit)
			{
				previewUnit.UpdateModelYOffset(offset);
			}
			else if (_editorPreviewNode is Prop3D previewProp)
			{
				previewProp.UpdateVisualYOffset(offset);
			}
		}

		public void SetModelScale(string assetKey, float scale)
		{
			string norm = GameContext.EditorService.NormalizeModelAssetKey(assetKey);
			if (string.IsNullOrEmpty(norm)) return;

			float clampedScale = Mathf.Clamp(scale, 0.05f, 20.0f);
			GameContext.ModelOverrideService.ModelScales[norm] = clampedScale;

			UpdatePropsScale(norm, clampedScale);
			UpdateUnitsScale(norm, clampedScale);
			UpdatePreviewScale(norm, clampedScale);

			GameContext.EditorService.ModelYOffsetSavePending = true;
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

			GameContext.PropMultiMesh?.MarkDirty(normKey);
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

			if (_editorPreviewNode is Unit3D previewUnit)
			{
				previewUnit.UpdateModelScale(scale);
			}
			else if (_editorPreviewNode is Prop3D previewProp)
			{
				previewProp.UpdateVisualScale(scale);
			}
		}

		public void SetModelCollisionCircleRatio(string assetKey, float ratio)
		{
			string norm = GameContext.EditorService.NormalizeModelAssetKey(assetKey);
			if (string.IsNullOrEmpty(norm)) return;

			GameContext.ModelOverrideService.ModelCollisionCircleRatios[norm] = ratio;

			string modelAsset = GetModelAssetKey(assetKey);
			if (!string.IsNullOrEmpty(modelAsset))
			{
				GameContext.ModelOverrideService.ModelCollisionCircleRatios[GameContext.EditorService.NormalizeModelAssetKey(modelAsset)] = ratio;
			}

			UpdateCollisionRadiiForAsset(norm);

			GameContext.EditorService.ModelCollisionCircleSavePending = true;
			EditorHasUnsavedChanges = true;
		}

		public void SetModelBrightness(string assetKey, float brightness)
		{
			string norm = GameContext.EditorService.NormalizeModelAssetKey(assetKey);
			if (string.IsNullOrEmpty(norm)) return;

			float k = Mathf.Clamp(brightness, 0.10f, 2.0f);
			GameContext.ModelOverrideService.ModelBrightness[norm] = k;
			UpdateMaterialOverridesForAsset(norm);

			GameContext.EditorService.ModelYOffsetSavePending = true;
			EditorHasUnsavedChanges = true;
		}

		public void SetModelColorTint(string assetKey, Color color)
		{
			string norm = GameContext.EditorService.NormalizeModelAssetKey(assetKey);
			if (string.IsNullOrEmpty(norm)) return;

			Color clamped = new Color(
				Mathf.Clamp(color.R, 0.0f, 1.0f),
				Mathf.Clamp(color.G, 0.0f, 1.0f),
				Mathf.Clamp(color.B, 0.0f, 1.0f),
				1.0f
			);
			GameContext.ModelOverrideService.ModelColorTint[norm] = clamped;
			UpdateMaterialOverridesForAsset(norm);

			GameContext.EditorService.ModelYOffsetSavePending = true;
			EditorHasUnsavedChanges = true;
		}

		public void SetModelDespillPlayerColor(string assetKey, bool despillPlayerColor)
		{
			string norm = GameContext.EditorService.NormalizeModelAssetKey(assetKey);
			if (string.IsNullOrEmpty(norm)) return;

			GameContext.ModelOverrideService.ModelDespillPlayerColor[norm] = despillPlayerColor;

			string modelAsset = GetModelAssetKey(assetKey);
			string? normModel = !string.IsNullOrEmpty(modelAsset) ? GameContext.EditorService.NormalizeModelAssetKey(modelAsset) : null;
			if (!string.IsNullOrEmpty(normModel) && !normModel.Equals(norm, StringComparison.OrdinalIgnoreCase))
			{
				GameContext.ModelOverrideService.ModelDespillPlayerColor[normModel] = despillPlayerColor;
			}

			string primaryKey = GetSelectedEntityOrAssetKey(assetKey);
			string? normPrimary = !string.IsNullOrEmpty(primaryKey) ? GameContext.EditorService.NormalizeModelAssetKey(primaryKey) : null;
			if (!string.IsNullOrEmpty(normPrimary) && !normPrimary.Equals(norm, StringComparison.OrdinalIgnoreCase))
			{
				GameContext.ModelOverrideService.ModelDespillPlayerColor[normPrimary] = despillPlayerColor;
			}

			UpdateDespillMetadataForAsset(assetKey, despillPlayerColor);
			UpdateMaterialOverridesForAsset(norm);

			GameContext.EditorService.ModelYOffsetSavePending = true;
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
			string normPrimary = GameContext.EditorService.NormalizeModelAssetKey(primaryKey);
			if (!string.IsNullOrEmpty(normPrimary) && GameContext.ModelOverrideService.ModelSpawnShaders.TryGetValue(normPrimary, out var s1))
				return s1;

			string assetKey = GetModelAssetKey(objOrId);
			string normAsset = GameContext.EditorService.NormalizeModelAssetKey(assetKey);
			if (!string.IsNullOrEmpty(normAsset) && GameContext.ModelOverrideService.ModelSpawnShaders.TryGetValue(normAsset, out var s2))
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
			string norm = GameContext.EditorService.NormalizeModelAssetKey(assetKey);
			if (string.IsNullOrEmpty(norm)) return;

			string modelAsset = GetModelAssetKey(assetKey);
			string? normModel = !string.IsNullOrEmpty(modelAsset) ? GameContext.EditorService.NormalizeModelAssetKey(modelAsset) : null;

			UpdateModelSpawnShaders(norm, normModel, shaderKey);
			UpdateRegistrySpawnShaders(assetKey, shaderKey);

			GameContext.EditorService.ModelYOffsetSavePending = true;
			EditorHasUnsavedChanges = true;
		}

		private void UpdateModelSpawnShaders(string norm, string? normModel, string shaderKey)
		{
			if (string.IsNullOrWhiteSpace(shaderKey))
			{
				GameContext.ModelOverrideService.ModelSpawnShaders.Remove(norm);
				if (!string.IsNullOrEmpty(normModel)) GameContext.ModelOverrideService.ModelSpawnShaders.Remove(normModel);
			}
			else
			{
				string trimmedShader = shaderKey.Trim();
				GameContext.ModelOverrideService.ModelSpawnShaders[norm] = trimmedShader;
				if (!string.IsNullOrEmpty(normModel)) GameContext.ModelOverrideService.ModelSpawnShaders[normModel] = trimmedShader;
			}
		}

		private void UpdateRegistrySpawnShaders(string assetKey, string shaderKey)
		{
			string? finalShader = string.IsNullOrWhiteSpace(shaderKey) ? null : shaderKey.Trim();

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
			string norm = GameContext.EditorService.NormalizeModelAssetKey(assetKey);
			if (string.IsNullOrEmpty(norm)) return;

			string modelAsset = GetModelAssetKey(assetKey);
			string? normModel = !string.IsNullOrEmpty(modelAsset) ? GameContext.EditorService.NormalizeModelAssetKey(modelAsset) : null;

			if (string.IsNullOrWhiteSpace(shaderKey))
			{
				GameContext.ModelOverrideService.ModelDeathShaders.Remove(norm);
				if (!string.IsNullOrEmpty(normModel))
				{
					GameContext.ModelOverrideService.ModelDeathShaders.Remove(normModel);
				}
			}
			else
			{
				string trimmedShader = shaderKey.Trim();
				GameContext.ModelOverrideService.ModelDeathShaders[norm] = trimmedShader;
				if (!string.IsNullOrEmpty(normModel))
				{
					GameContext.ModelOverrideService.ModelDeathShaders[normModel] = trimmedShader;
				}
			}

			UpdateDeathShaderMetadataForAsset(assetKey, shaderKey);

			GameContext.EditorService.ModelYOffsetSavePending = true;
			EditorHasUnsavedChanges = true;
		}

		private void UpdateDeathShaderMetadataForAsset(string assetKey, string shaderKey)
		{
			string? cleanShaderKey = string.IsNullOrWhiteSpace(shaderKey) ? null : shaderKey.Trim();

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
			string normPrimary = GameContext.EditorService.NormalizeModelAssetKey(primaryKey);
			if (!string.IsNullOrEmpty(normPrimary) && GameContext.ModelOverrideService.ModelProceduralAnimations.TryGetValue(normPrimary, out var pa1))
				return pa1;

			string assetKey = GetModelAssetKey(objOrId);
			string normAsset = GameContext.EditorService.NormalizeModelAssetKey(assetKey);
			if (!string.IsNullOrEmpty(normAsset) && GameContext.ModelOverrideService.ModelProceduralAnimations.TryGetValue(normAsset, out var pa2))
				return pa2;

			return "";
		}

		public void SetModelProceduralAnimation(string assetKey, string animId)
		{
			string norm = GameContext.EditorService.NormalizeModelAssetKey(assetKey);
			if (string.IsNullOrEmpty(norm)) return;

			string modelAsset = GetModelAssetKey(assetKey);
			string? normModel = !string.IsNullOrEmpty(modelAsset) ? GameContext.EditorService.NormalizeModelAssetKey(modelAsset) : null;

			if (string.IsNullOrWhiteSpace(animId))
			{
				GameContext.ModelOverrideService.ModelProceduralAnimations.Remove(norm);
				if (!string.IsNullOrEmpty(normModel))
				{
					GameContext.ModelOverrideService.ModelProceduralAnimations.Remove(normModel);
				}
			}
			else
			{
				string trimmed = animId.Trim();
				GameContext.ModelOverrideService.ModelProceduralAnimations[norm] = trimmed;
				if (!string.IsNullOrEmpty(normModel))
				{
					GameContext.ModelOverrideService.ModelProceduralAnimations[normModel] = trimmed;
				}
			}

			GameContext.EditorService.ModelYOffsetSavePending = true;
			EditorHasUnsavedChanges = true;
			GameContext.PropMultiMesh?.RefreshProceduralAnimation(assetKey);
		}

		private bool CheckRegistryIgnorePlayerColor(string primaryKey)
		{
			if (string.IsNullOrEmpty(primaryKey)) return false;
			if (UnitRegistry.TryGetValue(primaryKey, out var meta)) return meta.IgnorePlayerColor;
			if (BuildingRegistry.TryGetValue(primaryKey, out var buildingMeta)) return buildingMeta.IgnorePlayerColor;
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
			if (modelNode != null)
			{
				try
				{
					if (!ModelShaderManager.ModelHasPlayerMask(modelNode))
					{
						GameContext.ModelOverrideService.ModelIgnorePlayerColor[lookupKey] = true;
						GameContext.EditorService.ModelYOffsetSavePending = true;
						EditorHasUnsavedChanges = true;
						return true;
					}

					GameContext.ModelOverrideService.ModelIgnorePlayerColor[lookupKey] = false;
				}
				finally
				{
					modelNode.QueueFree();
				}
			}

			return false;
		}

		public void SetModelIgnorePlayerColor(string assetKey, bool ignorePlayerColor)
		{
			string norm = GameContext.EditorService.NormalizeModelAssetKey(assetKey);
			if (string.IsNullOrEmpty(norm)) return;

			GameContext.ModelOverrideService.ModelIgnorePlayerColor[norm] = ignorePlayerColor;

			string modelAsset = GetModelAssetKey(assetKey);
			string? normModel = !string.IsNullOrEmpty(modelAsset) ? GameContext.EditorService.NormalizeModelAssetKey(modelAsset) : null;
			if (!string.IsNullOrEmpty(normModel) && !normModel.Equals(norm, StringComparison.OrdinalIgnoreCase))
			{
				GameContext.ModelOverrideService.ModelIgnorePlayerColor[normModel] = ignorePlayerColor;
			}

			string primaryKey = GetSelectedEntityOrAssetKey(assetKey);
			string? normPrimary = !string.IsNullOrEmpty(primaryKey) ? GameContext.EditorService.NormalizeModelAssetKey(primaryKey) : null;
			if (!string.IsNullOrEmpty(normPrimary) && !normPrimary.Equals(norm, StringComparison.OrdinalIgnoreCase))
			{
				GameContext.ModelOverrideService.ModelIgnorePlayerColor[normPrimary] = ignorePlayerColor;
			}

			UpdateIgnoreColorMetadataForAsset(assetKey, ignorePlayerColor);

			GameContext.EditorService.ModelYOffsetSavePending = true;
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

			if (_editorPreviewNode is Unit3D previewUnit) previewUnit.UpdateCollisionCircleScale(ratio);
			else if (_editorPreviewNode is Prop3D previewProp) previewProp.UpdateCollisionCircleScale(ratio);
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
			GameContext.ModelOverrideService.ModelYOffsets.Clear();
			GameContext.ModelOverrideService.ClearAll();
			GameContext.ModelOverrideService.ModelProceduralAnimations.Clear();
			GameContext.ModelOverrideService.ModelEnableProceduralAnimations.Clear();
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
				string? spawnShader = prop.SpawnShader;
				string? deathShader = prop.DeathShader;

				ApplyVisualOverrides(prop.TemplateID, prop.ModelPath, ref despill, ref normLuma, ref ignoreColor, ref spawnShader, ref deathShader);

				prop.DespillPlayerColor = despill;
				prop.NormalizeLuminance = normLuma;
				prop.IgnorePlayerColor = ignoreColor;
				prop.SpawnShader = spawnShader;
				prop.DeathShader = deathShader;
				(prop.ProceduralAnimation, prop.EnableProceduralAnimation) = ResolveProceduralAnimationOverrides(prop.TemplateID, prop.ProceduralAnimation, prop.EnableProceduralAnimation);

				props[i] = prop;
			}
		}

		public bool IsStaticPropAsset(string propIdOrEntityId)
		{
			if (string.IsNullOrEmpty(propIdOrEntityId)) return false;

			if (PropRegistry.ContainsKey(propIdOrEntityId) || ResourceRegistry.ContainsKey(propIdOrEntityId))
				return true;

			string clean = Path.GetFileNameWithoutExtension(propIdOrEntityId);
			if (!string.IsNullOrEmpty(clean) && (PropRegistry.ContainsKey(clean) || ResourceRegistry.ContainsKey(clean)))
				return true;

			return false;
		}

		public void DeleteStaticPropAtPosition(string propId, Vector3 hitPos)
		{
			if (GameContext.World == null) return;
			Entity targetEntity = Entity.Null;
			float minDistance = 2.0f;

			var propQuery = QueryCache.AllPropIdentityAndPositionQuery;
			GameContext.World.Query(in propQuery, (Entity entity, ref PropIdentity pid, ref Position pos) =>
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

			if (targetEntity != Entity.Null && GameContext.World.IsAlive(targetEntity))
			{
				string pid = GameContext.World.Get<PropIdentity>(targetEntity).PropId;
				GameContext.World.Destroy(targetEntity);
				GameContext.PropMultiMesh?.MarkDirty(pid);
			}
		}

		public Prop3D? SpawnPropExternal(string propId, Vector3 position)
		{
			float rotY = GameContext.SessionState.IsMapEditorMode ? GameContext.EditorService.Tools.PlacementRotation : 0f;
			float scale = GameContext.SessionState.IsMapEditorMode ? EditorPlacementScale : 1f;
			return SpawnPropExternalWithParams(propId, position, rotY, scale);
		}

	}
}
