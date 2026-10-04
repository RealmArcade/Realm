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
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Realm.Godot.Services;
using Realm.Godot.Utils;
using Realm.Godot.VFX;

public partial class GameHost
{
	public readonly Dictionary<string, float> ModelYOffsets = new(StringComparer.OrdinalIgnoreCase);
	public readonly Dictionary<string, float> ModelScales = new(StringComparer.OrdinalIgnoreCase);
	public readonly Dictionary<string, float> ModelCollisionCircleRatios = new(StringComparer.OrdinalIgnoreCase);
	public readonly Dictionary<string, float> ModelObstacleRadii = new(StringComparer.OrdinalIgnoreCase);
	public readonly Dictionary<string, float> ModelBrightness = new(StringComparer.OrdinalIgnoreCase);
	public readonly Dictionary<string, Color> ModelColorTint = new(StringComparer.OrdinalIgnoreCase);
	public readonly Dictionary<string, bool> ModelIgnorePlayerColor = new(StringComparer.OrdinalIgnoreCase);
	public readonly Dictionary<string, bool> ModelDespillPlayerColor = new(StringComparer.OrdinalIgnoreCase);
	public readonly Dictionary<string, bool> ModelNormalizeLuminance = new(StringComparer.OrdinalIgnoreCase);
	public readonly Dictionary<string, string> ModelSpawnShaders = new(StringComparer.OrdinalIgnoreCase);
	public readonly Dictionary<string, string> ModelDeathShaders = new(StringComparer.OrdinalIgnoreCase);
	public readonly Dictionary<string, string> ModelProceduralAnimations = new(StringComparer.OrdinalIgnoreCase);
	public readonly Dictionary<string, bool> ModelEnableProceduralAnimations = new(StringComparer.OrdinalIgnoreCase);
	private bool _modelYOffsetSavePending = false;
	private bool _modelCollisionCircleSavePending = false;

	private readonly Dictionary<string, string> _normalizedAssetKeyCache = new(StringComparer.OrdinalIgnoreCase);

	public string NormalizeModelAssetKey(string pathOrId)
	{
		if (string.IsNullOrEmpty(pathOrId)) return "";
		if (_normalizedAssetKeyCache.TryGetValue(pathOrId, out string cached))
		{
			return cached;
		}

		string filename = System.IO.Path.GetFileName(pathOrId);
		if (filename.EndsWith(".glb", StringComparison.OrdinalIgnoreCase) || filename.EndsWith(".gltf", StringComparison.OrdinalIgnoreCase))
		{
			filename = System.IO.Path.GetFileNameWithoutExtension(filename) + ".rmesh";
		}
		else if (filename.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase) || filename.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || filename.EndsWith(".webp", StringComparison.OrdinalIgnoreCase))
		{
			// Preserve decal/texture extension
		}
		else if (!filename.EndsWith(".rmesh", StringComparison.OrdinalIgnoreCase))
		{
			filename += ".rmesh";
		}
		string result = filename.ToLowerInvariant();
		_normalizedAssetKeyCache[pathOrId] = result;
		return result;
	}

	public string GetModelAssetKey(object objOrId)
	{
		if (objOrId == null) return "";
		if (objOrId is Unit3D unit)
		{
			if (!string.IsNullOrEmpty(unit.ModelPath))
				return NormalizeModelAssetKey(unit.ModelPath);
			if (UnitRegistry.TryGetValue(unit.UnitId, out var meta) && !string.IsNullOrEmpty(meta.ModelPath))
				return NormalizeModelAssetKey(meta.ModelPath);
			if (BuildingRegistry.TryGetValue(unit.UnitId, out var bldMeta) && !string.IsNullOrEmpty(bldMeta.ModelPath))
				return NormalizeModelAssetKey(bldMeta.ModelPath);
			return "";
		}
		if (objOrId is Prop3D prop)
		{
			if (PropRegistry.TryGetValue(prop.PropId, out var propMeta) && !string.IsNullOrEmpty(propMeta.ModelPath))
				return NormalizeModelAssetKey(propMeta.ModelPath);
			if (ResourceRegistry.TryGetValue(prop.PropId, out var resMeta) && !string.IsNullOrEmpty(resMeta.ModelPath))
				return NormalizeModelAssetKey(resMeta.ModelPath);
			if (UnitRegistry.TryGetValue(prop.PropId, out var unitMeta) && !string.IsNullOrEmpty(unitMeta.ModelPath))
				return NormalizeModelAssetKey(unitMeta.ModelPath);
			if (BuildingRegistry.TryGetValue(prop.PropId, out var bldMeta) && !string.IsNullOrEmpty(bldMeta.ModelPath))
				return NormalizeModelAssetKey(bldMeta.ModelPath);
			return "";
		}
		if (objOrId is string str)
		{
			if (PropRegistry.TryGetValue(str, out var propMeta) && !string.IsNullOrEmpty(propMeta.ModelPath))
				return NormalizeModelAssetKey(propMeta.ModelPath);
			if (ResourceRegistry.TryGetValue(str, out var resMeta) && !string.IsNullOrEmpty(resMeta.ModelPath))
				return NormalizeModelAssetKey(resMeta.ModelPath);
			if (UnitRegistry.TryGetValue(str, out var unitMeta) && !string.IsNullOrEmpty(unitMeta.ModelPath))
				return NormalizeModelAssetKey(unitMeta.ModelPath);
			if (BuildingRegistry.TryGetValue(str, out var bldMeta) && !string.IsNullOrEmpty(bldMeta.ModelPath))
				return NormalizeModelAssetKey(bldMeta.ModelPath);

			string clean = System.IO.Path.GetFileNameWithoutExtension(str);
			if (!string.IsNullOrEmpty(clean) && !clean.Equals(str, StringComparison.OrdinalIgnoreCase))
			{
				if (PropRegistry.TryGetValue(clean, out propMeta) && !string.IsNullOrEmpty(propMeta.ModelPath))
					return NormalizeModelAssetKey(propMeta.ModelPath);
				if (ResourceRegistry.TryGetValue(clean, out resMeta) && !string.IsNullOrEmpty(resMeta.ModelPath))
					return NormalizeModelAssetKey(resMeta.ModelPath);
				if (UnitRegistry.TryGetValue(clean, out unitMeta) && !string.IsNullOrEmpty(unitMeta.ModelPath))
					return NormalizeModelAssetKey(unitMeta.ModelPath);
				if (BuildingRegistry.TryGetValue(clean, out bldMeta) && !string.IsNullOrEmpty(bldMeta.ModelPath))
					return NormalizeModelAssetKey(bldMeta.ModelPath);
			}

			return NormalizeModelAssetKey(str);
		}
		return "";
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
		if (objOrId is string str) return str;
		return GetModelAssetKey(objOrId);
	}

	public bool MatchesEntityOrAssetKey(object objOrId, string targetKey)
	{
		if (objOrId == null || string.IsNullOrEmpty(targetKey)) return false;
		string normTarget = NormalizeModelAssetKey(targetKey);

		if (objOrId is Unit3D unit)
		{
			if (!string.IsNullOrEmpty(unit.UnitId) && NormalizeModelAssetKey(unit.UnitId) == normTarget)
				return true;
			if (!string.IsNullOrEmpty(unit.ModelPath) && NormalizeModelAssetKey(unit.ModelPath) == normTarget)
				return true;
			if (UnitRegistry.TryGetValue(unit.UnitId, out var meta) && !string.IsNullOrEmpty(meta.ModelPath) && NormalizeModelAssetKey(meta.ModelPath) == normTarget)
				return true;
			if (BuildingRegistry.TryGetValue(unit.UnitId, out var bldMeta) && !string.IsNullOrEmpty(bldMeta.ModelPath) && NormalizeModelAssetKey(bldMeta.ModelPath) == normTarget)
				return true;
		}
		else if (objOrId is Prop3D prop)
		{
			if (!string.IsNullOrEmpty(prop.PropId) && NormalizeModelAssetKey(prop.PropId) == normTarget)
				return true;
			string cleanPropId = System.IO.Path.GetFileNameWithoutExtension(prop.PropId);
			if ((PropRegistry.TryGetValue(prop.PropId, out var propMeta) || (!string.IsNullOrEmpty(cleanPropId) && PropRegistry.TryGetValue(cleanPropId, out propMeta))) && !string.IsNullOrEmpty(propMeta.ModelPath) && NormalizeModelAssetKey(propMeta.ModelPath) == normTarget)
				return true;
			if ((ResourceRegistry.TryGetValue(prop.PropId, out var resMeta) || (!string.IsNullOrEmpty(cleanPropId) && ResourceRegistry.TryGetValue(cleanPropId, out resMeta))) && !string.IsNullOrEmpty(resMeta.ModelPath) && NormalizeModelAssetKey(resMeta.ModelPath) == normTarget)
				return true;
			if ((BuildingRegistry.TryGetValue(prop.PropId, out var bldMeta) || (!string.IsNullOrEmpty(cleanPropId) && BuildingRegistry.TryGetValue(cleanPropId, out bldMeta))) && !string.IsNullOrEmpty(bldMeta.ModelPath) && NormalizeModelAssetKey(bldMeta.ModelPath) == normTarget)
				return true;
			if ((UnitRegistry.TryGetValue(prop.PropId, out var uMeta) || (!string.IsNullOrEmpty(cleanPropId) && UnitRegistry.TryGetValue(cleanPropId, out uMeta))) && !string.IsNullOrEmpty(uMeta.ModelPath) && NormalizeModelAssetKey(uMeta.ModelPath) == normTarget)
				return true;
		}
		else if (objOrId is string str)
		{
			if (NormalizeModelAssetKey(str) == normTarget) return true;
			string cleanStr = System.IO.Path.GetFileNameWithoutExtension(str);
			if ((UnitRegistry.TryGetValue(str, out var meta) || (!string.IsNullOrEmpty(cleanStr) && UnitRegistry.TryGetValue(cleanStr, out meta))) && !string.IsNullOrEmpty(meta.ModelPath) && NormalizeModelAssetKey(meta.ModelPath) == normTarget)
				return true;
			if ((BuildingRegistry.TryGetValue(str, out var bldMeta) || (!string.IsNullOrEmpty(cleanStr) && BuildingRegistry.TryGetValue(cleanStr, out bldMeta))) && !string.IsNullOrEmpty(bldMeta.ModelPath) && NormalizeModelAssetKey(bldMeta.ModelPath) == normTarget)
				return true;
			if ((PropRegistry.TryGetValue(str, out var propMeta) || (!string.IsNullOrEmpty(cleanStr) && PropRegistry.TryGetValue(cleanStr, out propMeta))) && !string.IsNullOrEmpty(propMeta.ModelPath) && NormalizeModelAssetKey(propMeta.ModelPath) == normTarget)
				return true;
			if ((ResourceRegistry.TryGetValue(str, out var resMeta) || (!string.IsNullOrEmpty(cleanStr) && ResourceRegistry.TryGetValue(cleanStr, out resMeta))) && !string.IsNullOrEmpty(resMeta.ModelPath) && NormalizeModelAssetKey(resMeta.ModelPath) == normTarget)
				return true;
		}
		return GetModelAssetKey(objOrId) == normTarget;
	}

	public float GetModelYOffset(object objOrId)
	{
		if (objOrId == null) return 0f;
		string primaryKey = GetSelectedEntityOrAssetKey(objOrId);
		string normPrimary = NormalizeModelAssetKey(primaryKey);
		if (!string.IsNullOrEmpty(normPrimary) && ModelYOffsets.TryGetValue(normPrimary, out float val1))
			return val1;

		string assetKey = GetModelAssetKey(objOrId);
		string normAsset = NormalizeModelAssetKey(assetKey);
		if (!string.IsNullOrEmpty(normAsset) && ModelYOffsets.TryGetValue(normAsset, out float val2))
			return val2;

		if (!string.IsNullOrEmpty(primaryKey))
		{
			if (UnitRegistry.TryGetValue(primaryKey, out var meta) && meta.YOffset != 0f) return meta.YOffset;
			if (BuildingRegistry.TryGetValue(primaryKey, out var bldMeta) && bldMeta.YOffset != 0f) return bldMeta.YOffset;
			if (ResourceRegistry.TryGetValue(primaryKey, out var resMeta) && resMeta.YOffset != 0f) return resMeta.YOffset;
			if (PropRegistry.TryGetValue(primaryKey, out var propMeta) && propMeta.YOffset != 0f) return propMeta.YOffset;
		}

		return 0f;
	}

	public void SetModelYOffset(string assetKey, float offset)
	{
		string norm = NormalizeModelAssetKey(assetKey);
		if (string.IsNullOrEmpty(norm)) return;

		ModelYOffsets[norm] = offset;

		foreach (var prop in AllProps)
		{
			if (GodotObject.IsInstanceValid(prop) && MatchesEntityOrAssetKey(prop, norm))
			{
				prop.UpdateVisualYOffset(offset);
			}
		}
		PropMultiMeshManager.Instance?.MarkDirty(norm);

		foreach (var unit in AllUnits)
		{
			if (GodotObject.IsInstanceValid(unit) && MatchesEntityOrAssetKey(unit, norm))
			{
				unit.UpdateModelYOffset(offset);
			}
		}

		if (_editorPreviewNode != null && GodotObject.IsInstanceValid(_editorPreviewNode) && MatchesEntityOrAssetKey(_editorPreviewNode, norm))
		{
			if (_editorPreviewNode is Unit3D previewUnit)
			{
				previewUnit.UpdateModelYOffset(offset);
			}
			else if (_editorPreviewNode is Prop3D previewProp)
			{
				previewProp.UpdateVisualYOffset(offset);
			}
		}

		_modelYOffsetSavePending = true;
		EditorHasUnsavedChanges = true;
	}

	public float GetModelScale(object objOrId)
	{
		if (objOrId == null) return 1.0f;
		string primaryKey = GetSelectedEntityOrAssetKey(objOrId);
		string normPrimary = NormalizeModelAssetKey(primaryKey);
		if (!string.IsNullOrEmpty(normPrimary) && ModelScales.TryGetValue(normPrimary, out float val1) && val1 > 0f)
			return val1;

		string assetKey = GetModelAssetKey(objOrId);
		string normAsset = NormalizeModelAssetKey(assetKey);
		if (!string.IsNullOrEmpty(normAsset) && ModelScales.TryGetValue(normAsset, out float val2) && val2 > 0f)
			return val2;

		if (!string.IsNullOrEmpty(primaryKey))
		{
			if (UnitRegistry.TryGetValue(primaryKey, out var meta) && meta.Scale > 0f) return meta.Scale;
			if (BuildingRegistry.TryGetValue(primaryKey, out var bldMeta) && bldMeta.Scale > 0f) return bldMeta.Scale;
			if (ResourceRegistry.TryGetValue(primaryKey, out var resMeta) && resMeta.Scale > 0f) return resMeta.Scale;
			if (PropRegistry.TryGetValue(primaryKey, out var propMeta) && propMeta.Scale > 0f) return propMeta.Scale;
		}

		if (!string.IsNullOrEmpty(normAsset))
		{
			if (UnitRegistry.TryGetValue(normAsset, out var meta2) && meta2.Scale > 0f) return meta2.Scale;
			if (BuildingRegistry.TryGetValue(normAsset, out var bldMeta2) && bldMeta2.Scale > 0f) return bldMeta2.Scale;
			if (ResourceRegistry.TryGetValue(normAsset, out var resMeta2) && resMeta2.Scale > 0f) return resMeta2.Scale;
			if (PropRegistry.TryGetValue(normAsset, out var propMeta2) && propMeta2.Scale > 0f) return propMeta2.Scale;
		}

		if (objOrId is Unit3D unit && GodotObject.IsInstanceValid(unit))
		{
			if (unit.IsResource) return 2.75f;
			if (unit.IsBuilding) return 1.5f;
			return 1.0f;
		}

		if (!string.IsNullOrEmpty(primaryKey))
		{
			if (ResourceRegistry.ContainsKey(primaryKey)) return 2.75f;
			if (BuildingRegistry.ContainsKey(primaryKey)) return 1.5f;
			if (UnitRegistry.ContainsKey(primaryKey)) return 1.0f;
			if (PropRegistry.ContainsKey(primaryKey)) return 1.25f;
		}

		if (!string.IsNullOrEmpty(normAsset))
		{
			if (ResourceRegistry.ContainsKey(normAsset)) return 2.75f;
			if (BuildingRegistry.ContainsKey(normAsset)) return 1.5f;
			if (UnitRegistry.ContainsKey(normAsset)) return 1.0f;
			if (PropRegistry.ContainsKey(normAsset)) return 1.25f;
		}

		return 1.0f;
	}

	public void SetModelScale(string assetKey, float scale)
	{
		string norm = NormalizeModelAssetKey(assetKey);
		if (string.IsNullOrEmpty(norm)) return;

		float clampedScale = Mathf.Clamp(scale, 0.05f, 20.0f);
		ModelScales[norm] = clampedScale;

		foreach (var prop in AllProps)
		{
			if (GodotObject.IsInstanceValid(prop) && MatchesEntityOrAssetKey(prop, norm))
			{
				prop.UpdateVisualScale(clampedScale);
			}
		}
		PropMultiMeshManager.Instance?.MarkDirty(norm);

		foreach (var unit in AllUnits)
		{
			if (GodotObject.IsInstanceValid(unit) && MatchesEntityOrAssetKey(unit, norm))
			{
				unit.UpdateModelScale(clampedScale);
			}
		}

		if (_editorPreviewNode != null && GodotObject.IsInstanceValid(_editorPreviewNode) && MatchesEntityOrAssetKey(_editorPreviewNode, norm))
		{
			if (_editorPreviewNode is Unit3D previewUnit)
			{
				previewUnit.UpdateModelScale(clampedScale);
			}
			else if (_editorPreviewNode is Prop3D previewProp)
			{
				previewProp.UpdateVisualScale(clampedScale);
			}
		}

		_modelYOffsetSavePending = true;
		EditorHasUnsavedChanges = true;
	}

	public void FlushModelYOffsetSave()
	{
		if (_modelYOffsetSavePending || _modelCollisionCircleSavePending)
		{
			_modelYOffsetSavePending = false;
			_modelCollisionCircleSavePending = false;
			SaveModelYOffsetsToMetadataJson();
		}
	}

	public float GetModelCollisionCircleRatio(object objOrId)
	{
		if (objOrId == null) return 1.0f;
		string primaryKey = GetSelectedEntityOrAssetKey(objOrId);
		string normPrimary = NormalizeModelAssetKey(primaryKey);
		if (!string.IsNullOrEmpty(normPrimary) && ModelCollisionCircleRatios.TryGetValue(normPrimary, out float val1))
			return val1;

		string assetKey = GetModelAssetKey(objOrId);
		string normAsset = NormalizeModelAssetKey(assetKey);
		if (!string.IsNullOrEmpty(normAsset) && ModelCollisionCircleRatios.TryGetValue(normAsset, out float val2))
			return val2;

		if (!string.IsNullOrEmpty(primaryKey))
		{
			if (UnitRegistry.TryGetValue(primaryKey, out var meta) && meta.CollisionCircle > 0f) return meta.CollisionCircle;
			if (ResourceRegistry.TryGetValue(primaryKey, out var resMeta) && resMeta.CollisionCircle > 0f) return resMeta.CollisionCircle;
			if (PropRegistry.TryGetValue(primaryKey, out var propMeta) && propMeta.CollisionCircle > 0f) return propMeta.CollisionCircle;
		}

		return 1.0f;
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

	public float GetModelBrightness(object objOrId)
	{
		if (objOrId == null) return 1.0f;
		string primaryKey = GetSelectedEntityOrAssetKey(objOrId);
		string normPrimary = NormalizeModelAssetKey(primaryKey);
		if (!string.IsNullOrEmpty(normPrimary) && ModelBrightness.TryGetValue(normPrimary, out float val1))
			return Mathf.Clamp(val1, 0.10f, 1.75f);

		string assetKey = GetModelAssetKey(objOrId);
		string normAsset = NormalizeModelAssetKey(assetKey);
		if (!string.IsNullOrEmpty(normAsset) && ModelBrightness.TryGetValue(normAsset, out float val2))
			return Mathf.Clamp(val2, 0.10f, 1.75f);

		if (!string.IsNullOrEmpty(primaryKey))
		{
			if (UnitRegistry.TryGetValue(primaryKey, out var meta) && meta.Brightness > 0f) return Mathf.Clamp(meta.Brightness, 0.10f, 2.0f);
			if (ResourceRegistry.TryGetValue(primaryKey, out var resMeta) && resMeta.Brightness > 0f) return Mathf.Clamp(resMeta.Brightness, 0.10f, 2.0f);
			if (PropRegistry.TryGetValue(primaryKey, out var propMeta) && propMeta.Brightness > 0f) return Mathf.Clamp(propMeta.Brightness, 0.10f, 2.0f);
		}

		return 0.5f;
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

	public Color GetModelColorTint(object objOrId)
	{
		if (objOrId == null) return new Color(1.0f, 1.0f, 1.0f);
		string primaryKey = GetSelectedEntityOrAssetKey(objOrId);
		string normPrimary = NormalizeModelAssetKey(primaryKey);
		if (!string.IsNullOrEmpty(normPrimary) && ModelColorTint.TryGetValue(normPrimary, out Color c1))
			return c1;

		string assetKey = GetModelAssetKey(objOrId);
		string normAsset = NormalizeModelAssetKey(assetKey);
		if (!string.IsNullOrEmpty(normAsset) && ModelColorTint.TryGetValue(normAsset, out Color c2))
			return c2;

		if (!string.IsNullOrEmpty(primaryKey))
		{
			if (UnitRegistry.TryGetValue(primaryKey, out var meta) && !string.IsNullOrEmpty(meta.Tint) && Color.HtmlIsValid(meta.Tint)) return Color.FromHtml(meta.Tint);
			if (ResourceRegistry.TryGetValue(primaryKey, out var resMeta) && !string.IsNullOrEmpty(resMeta.Tint) && Color.HtmlIsValid(resMeta.Tint)) return Color.FromHtml(resMeta.Tint);
			if (PropRegistry.TryGetValue(primaryKey, out var propMeta) && !string.IsNullOrEmpty(propMeta.Tint) && Color.HtmlIsValid(propMeta.Tint)) return Color.FromHtml(propMeta.Tint);
		}

		return new Color(1.0f, 1.0f, 1.0f);
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

	public bool GetModelDespillPlayerColor(object objOrId)
	{
		if (objOrId == null) return false;
		string primaryKey = GetSelectedEntityOrAssetKey(objOrId);
		string normPrimary = NormalizeModelAssetKey(primaryKey);
		if (!string.IsNullOrEmpty(normPrimary) && ModelDespillPlayerColor.TryGetValue(normPrimary, out bool b1))
			return b1;

		string assetKey = GetModelAssetKey(objOrId);
		string normAsset = NormalizeModelAssetKey(assetKey);
		if (!string.IsNullOrEmpty(normAsset) && ModelDespillPlayerColor.TryGetValue(normAsset, out bool b2))
			return b2;

		if (!string.IsNullOrEmpty(primaryKey))
		{
			if (UnitRegistry.TryGetValue(primaryKey, out var meta)) return meta.DespillPlayerColor;
			if (ResourceRegistry.TryGetValue(primaryKey, out var resMeta)) return resMeta.DespillPlayerColor;
			if (PropRegistry.TryGetValue(primaryKey, out var propMeta)) return propMeta.DespillPlayerColor;
		}

		return false;
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

		if (UnitRegistry.TryGetValue(assetKey, out var unitMeta))
		{
			unitMeta.DespillPlayerColor = despillPlayerColor;
			UnitRegistry[assetKey] = unitMeta;
		}
		if (BuildingRegistry.TryGetValue(assetKey, out var bldMeta))
		{
			bldMeta.DespillPlayerColor = despillPlayerColor;
			BuildingRegistry[assetKey] = bldMeta;
		}
		if (ResourceRegistry.TryGetValue(assetKey, out var resMeta))
		{
			resMeta.DespillPlayerColor = despillPlayerColor;
			ResourceRegistry[assetKey] = resMeta;
		}
		if (PropRegistry.TryGetValue(assetKey, out var propMeta))
		{
			propMeta.DespillPlayerColor = despillPlayerColor;
			PropRegistry[assetKey] = propMeta;
		}

		UpdateMaterialOverridesForAsset(norm);

		_modelYOffsetSavePending = true;
		EditorHasUnsavedChanges = true;
	}

	public bool GetModelNormalizeLuminance(object objOrId)
	{
		if (objOrId == null) return true;
		string primaryKey = GetSelectedEntityOrAssetKey(objOrId);
		string normPrimary = NormalizeModelAssetKey(primaryKey);
		if (!string.IsNullOrEmpty(normPrimary) && ModelNormalizeLuminance.TryGetValue(normPrimary, out bool b1))
			return b1;

		string assetKey = GetModelAssetKey(objOrId);
		string normAsset = NormalizeModelAssetKey(assetKey);
		if (!string.IsNullOrEmpty(normAsset) && ModelNormalizeLuminance.TryGetValue(normAsset, out bool b2))
			return b2;

		if (!string.IsNullOrEmpty(primaryKey))
		{
			if (UnitRegistry.TryGetValue(primaryKey, out var meta)) return meta.NormalizeLuminance;
			if (ResourceRegistry.TryGetValue(primaryKey, out var resMeta)) return resMeta.NormalizeLuminance;
			if (PropRegistry.TryGetValue(primaryKey, out var propMeta)) return propMeta.NormalizeLuminance;
		}

		return true;
	}

	public void SetModelNormalizeLuminance(string assetKey, bool normalizeLuminance)
	{
		string norm = NormalizeModelAssetKey(assetKey);
		if (string.IsNullOrEmpty(norm)) return;

		ModelNormalizeLuminance[norm] = normalizeLuminance;

		string modelAsset = GetModelAssetKey(assetKey);
		string normModel = !string.IsNullOrEmpty(modelAsset) ? NormalizeModelAssetKey(modelAsset) : null;
		if (!string.IsNullOrEmpty(normModel) && !normModel.Equals(norm, StringComparison.OrdinalIgnoreCase))
		{
			ModelNormalizeLuminance[normModel] = normalizeLuminance;
		}

		string primaryKey = GetSelectedEntityOrAssetKey(assetKey);
		string normPrimary = !string.IsNullOrEmpty(primaryKey) ? NormalizeModelAssetKey(primaryKey) : null;
		if (!string.IsNullOrEmpty(normPrimary) && !normPrimary.Equals(norm, StringComparison.OrdinalIgnoreCase))
		{
			ModelNormalizeLuminance[normPrimary] = normalizeLuminance;
		}

		if (UnitRegistry.TryGetValue(assetKey, out var unitMeta))
		{
			unitMeta.NormalizeLuminance = normalizeLuminance;
			UnitRegistry[assetKey] = unitMeta;
		}
		if (BuildingRegistry.TryGetValue(assetKey, out var bldMeta))
		{
			bldMeta.NormalizeLuminance = normalizeLuminance;
			BuildingRegistry[assetKey] = bldMeta;
		}
		if (ResourceRegistry.TryGetValue(assetKey, out var resMeta))
		{
			resMeta.NormalizeLuminance = normalizeLuminance;
			ResourceRegistry[assetKey] = resMeta;
		}
		if (PropRegistry.TryGetValue(assetKey, out var propMeta))
		{
			propMeta.NormalizeLuminance = normalizeLuminance;
			PropRegistry[assetKey] = propMeta;
		}

		UpdateMaterialOverridesForAsset(norm);

		_modelYOffsetSavePending = true;
		EditorHasUnsavedChanges = true;
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

		if (!string.IsNullOrEmpty(primaryKey))
		{
			if (UnitRegistry.TryGetValue(primaryKey, out var meta) && !string.IsNullOrWhiteSpace(meta.SpawnShader)) return meta.SpawnShader;
			if (BuildingRegistry.TryGetValue(primaryKey, out var bldMeta) && !string.IsNullOrWhiteSpace(bldMeta.SpawnShader)) return bldMeta.SpawnShader;
			if (ResourceRegistry.TryGetValue(primaryKey, out var resMeta) && !string.IsNullOrWhiteSpace(resMeta.SpawnShader)) return resMeta.SpawnShader;
			if (PropRegistry.TryGetValue(primaryKey, out var propMeta) && !string.IsNullOrWhiteSpace(propMeta.SpawnShader)) return propMeta.SpawnShader;
		}

		return "";
	}

	public void SetModelSpawnShader(string assetKey, string shaderKey)
	{
		string norm = NormalizeModelAssetKey(assetKey);
		if (string.IsNullOrEmpty(norm)) return;

		string modelAsset = GetModelAssetKey(assetKey);
		string normModel = !string.IsNullOrEmpty(modelAsset) ? NormalizeModelAssetKey(modelAsset) : null;

		if (string.IsNullOrWhiteSpace(shaderKey))
		{
			ModelSpawnShaders.Remove(norm);
			if (!string.IsNullOrEmpty(normModel))
			{
				ModelSpawnShaders.Remove(normModel);
			}
		}
		else
		{
			string trimmedShader = shaderKey.Trim();
			ModelSpawnShaders[norm] = trimmedShader;
			if (!string.IsNullOrEmpty(normModel))
			{
				ModelSpawnShaders[normModel] = trimmedShader;
			}
		}

		if (UnitRegistry.TryGetValue(assetKey, out var unitMeta))
		{
			unitMeta.SpawnShader = string.IsNullOrWhiteSpace(shaderKey) ? null : shaderKey.Trim();
			UnitRegistry[assetKey] = unitMeta;
		}
		if (BuildingRegistry.TryGetValue(assetKey, out var bldMeta))
		{
			bldMeta.SpawnShader = string.IsNullOrWhiteSpace(shaderKey) ? null : shaderKey.Trim();
			BuildingRegistry[assetKey] = bldMeta;
		}
		if (PropRegistry.TryGetValue(assetKey, out var propMeta))
		{
			propMeta.SpawnShader = string.IsNullOrWhiteSpace(shaderKey) ? null : shaderKey.Trim();
			PropRegistry[assetKey] = propMeta;
		}
		if (ResourceRegistry.TryGetValue(assetKey, out var resMeta))
		{
			resMeta.SpawnShader = string.IsNullOrWhiteSpace(shaderKey) ? null : shaderKey.Trim();
			ResourceRegistry[assetKey] = resMeta;
		}

		_modelYOffsetSavePending = true;
		EditorHasUnsavedChanges = true;
	}

	public string GetModelDeathShader(object objOrId)
	{
		if (objOrId == null) return "";
		string primaryKey = GetSelectedEntityOrAssetKey(objOrId);
		string normPrimary = NormalizeModelAssetKey(primaryKey);
		if (!string.IsNullOrEmpty(normPrimary) && ModelDeathShaders.TryGetValue(normPrimary, out string d1))
			return d1;

		string assetKey = GetModelAssetKey(objOrId);
		string normAsset = NormalizeModelAssetKey(assetKey);
		if (!string.IsNullOrEmpty(normAsset) && ModelDeathShaders.TryGetValue(normAsset, out string d2))
			return d2;

		if (!string.IsNullOrEmpty(primaryKey))
		{
			if (UnitRegistry.TryGetValue(primaryKey, out var meta) && !string.IsNullOrWhiteSpace(meta.DeathShader)) return meta.DeathShader;
			if (BuildingRegistry.TryGetValue(primaryKey, out var bldMeta) && !string.IsNullOrWhiteSpace(bldMeta.DeathShader)) return bldMeta.DeathShader;
			if (ResourceRegistry.TryGetValue(primaryKey, out var resMeta) && !string.IsNullOrWhiteSpace(resMeta.DeathShader)) return resMeta.DeathShader;
			if (PropRegistry.TryGetValue(primaryKey, out var propMeta) && !string.IsNullOrWhiteSpace(propMeta.DeathShader)) return propMeta.DeathShader;
		}

		return "";
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

		if (UnitRegistry.TryGetValue(assetKey, out var unitMeta))
		{
			unitMeta.DeathShader = string.IsNullOrWhiteSpace(shaderKey) ? null : shaderKey.Trim();
			UnitRegistry[assetKey] = unitMeta;
		}
		if (BuildingRegistry.TryGetValue(assetKey, out var bldMeta))
		{
			bldMeta.DeathShader = string.IsNullOrWhiteSpace(shaderKey) ? null : shaderKey.Trim();
			BuildingRegistry[assetKey] = bldMeta;
		}
		if (PropRegistry.TryGetValue(assetKey, out var propMeta))
		{
			propMeta.DeathShader = string.IsNullOrWhiteSpace(shaderKey) ? null : shaderKey.Trim();
			PropRegistry[assetKey] = propMeta;
		}
		if (ResourceRegistry.TryGetValue(assetKey, out var resMeta))
		{
			resMeta.DeathShader = string.IsNullOrWhiteSpace(shaderKey) ? null : shaderKey.Trim();
			ResourceRegistry[assetKey] = resMeta;
		}

		_modelYOffsetSavePending = true;
		EditorHasUnsavedChanges = true;
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

	public bool GetModelEnableProceduralAnimation(object objOrId)
	{
		if (objOrId == null) return false;
		string primaryKey = GetSelectedEntityOrAssetKey(objOrId);
		string normPrimary = NormalizeModelAssetKey(primaryKey);
		if (!string.IsNullOrEmpty(normPrimary) && ModelEnableProceduralAnimations.TryGetValue(normPrimary, out bool ep1))
			return ep1;

		string assetKey = GetModelAssetKey(objOrId);
		string normAsset = NormalizeModelAssetKey(assetKey);
		if (!string.IsNullOrEmpty(normAsset) && ModelEnableProceduralAnimations.TryGetValue(normAsset, out bool ep2))
			return ep2;

		return false;
	}

	public void SetModelEnableProceduralAnimation(string assetKey, bool enable)
	{
		string norm = NormalizeModelAssetKey(assetKey);
		if (string.IsNullOrEmpty(norm)) return;

		string modelAsset = GetModelAssetKey(assetKey);
		string normModel = !string.IsNullOrEmpty(modelAsset) ? NormalizeModelAssetKey(modelAsset) : null;

		if (!enable)
		{
			ModelEnableProceduralAnimations.Remove(norm);
			if (!string.IsNullOrEmpty(normModel))
			{
				ModelEnableProceduralAnimations.Remove(normModel);
			}
		}
		else
		{
			ModelEnableProceduralAnimations[norm] = true;
			if (!string.IsNullOrEmpty(normModel))
			{
				ModelEnableProceduralAnimations[normModel] = true;
			}
		}

		_modelYOffsetSavePending = true;
		EditorHasUnsavedChanges = true;
	}

	public bool IsPropOrResourceKey(string key)
	{
		if (string.IsNullOrEmpty(key)) return false;
		string norm = NormalizeModelAssetKey(key);

		if (PropRegistry.ContainsKey(key) || PropRegistry.ContainsKey(norm)) return true;
		if (ResourceRegistry.ContainsKey(key) || ResourceRegistry.ContainsKey(norm)) return true;

		foreach (var propMeta in PropRegistry.Values)
		{
			if (!string.IsNullOrEmpty(propMeta.ModelPath) && NormalizeModelAssetKey(propMeta.ModelPath) == norm)
				return true;
			if (!string.IsNullOrEmpty(propMeta.TemplateID) && NormalizeModelAssetKey(propMeta.TemplateID) == norm)
				return true;
		}

		foreach (var resMeta in ResourceRegistry.Values)
		{
			if (!string.IsNullOrEmpty(resMeta.ModelPath) && NormalizeModelAssetKey(resMeta.ModelPath) == norm)
				return true;
			if (!string.IsNullOrEmpty(resMeta.TemplateID) && NormalizeModelAssetKey(resMeta.TemplateID) == norm)
				return true;
		}

		if (key.Contains("/props/", StringComparison.OrdinalIgnoreCase) || key.Contains("\\props\\", StringComparison.OrdinalIgnoreCase) || key.StartsWith("props/", StringComparison.OrdinalIgnoreCase)) return true;
		if (key.Contains("/resources/", StringComparison.OrdinalIgnoreCase) || key.Contains("\\resources\\", StringComparison.OrdinalIgnoreCase) || key.StartsWith("resources/", StringComparison.OrdinalIgnoreCase)) return true;

		if (norm.Contains("/props/", StringComparison.OrdinalIgnoreCase) || norm.Contains("\\props\\", StringComparison.OrdinalIgnoreCase) || norm.StartsWith("props/", StringComparison.OrdinalIgnoreCase)) return true;
		if (norm.Contains("/resources/", StringComparison.OrdinalIgnoreCase) || norm.Contains("\\resources\\", StringComparison.OrdinalIgnoreCase) || norm.StartsWith("resources/", StringComparison.OrdinalIgnoreCase)) return true;

		string resolved = ModelCache.ResolveModelPath(norm);
		if (!string.IsNullOrEmpty(resolved))
		{
			if (resolved.Contains("/props/", StringComparison.OrdinalIgnoreCase) || resolved.Contains("\\props\\", StringComparison.OrdinalIgnoreCase)) return true;
			if (resolved.Contains("/resources/", StringComparison.OrdinalIgnoreCase) || resolved.Contains("\\resources\\", StringComparison.OrdinalIgnoreCase)) return true;
		}

		return false;
	}

	public bool IsAttachmentKey(string key)
	{
		if (string.IsNullOrEmpty(key)) return false;
		string norm = NormalizeModelAssetKey(key);

		if (AttachmentRegistry.ContainsKey(key) || AttachmentRegistry.ContainsKey(norm)) return true;

		foreach (var attMeta in AttachmentRegistry.Values)
		{
			if (!string.IsNullOrEmpty(attMeta.ModelPath) && NormalizeModelAssetKey(attMeta.ModelPath) == norm)
				return true;
			if (!string.IsNullOrEmpty(attMeta.AttachmentId) && NormalizeModelAssetKey(attMeta.AttachmentId) == norm)
				return true;
		}

		if (key.Contains("/attachments/", StringComparison.OrdinalIgnoreCase) || key.Contains("\\attachments\\", StringComparison.OrdinalIgnoreCase) || key.StartsWith("attachments/", StringComparison.OrdinalIgnoreCase)) return true;
		if (key.Contains("/weapons/", StringComparison.OrdinalIgnoreCase) || key.Contains("\\weapons\\", StringComparison.OrdinalIgnoreCase) || key.StartsWith("weapons/", StringComparison.OrdinalIgnoreCase)) return true;
		if (key.Contains("/items/", StringComparison.OrdinalIgnoreCase) || key.Contains("\\items\\", StringComparison.OrdinalIgnoreCase) || key.StartsWith("items/", StringComparison.OrdinalIgnoreCase)) return true;

		if (norm.Contains("/attachments/", StringComparison.OrdinalIgnoreCase) || norm.Contains("\\attachments\\", StringComparison.OrdinalIgnoreCase) || norm.StartsWith("attachments/", StringComparison.OrdinalIgnoreCase)) return true;
		if (norm.Contains("/weapons/", StringComparison.OrdinalIgnoreCase) || norm.Contains("\\weapons\\", StringComparison.OrdinalIgnoreCase) || norm.StartsWith("weapons/", StringComparison.OrdinalIgnoreCase)) return true;
		if (norm.Contains("/items/", StringComparison.OrdinalIgnoreCase) || norm.Contains("\\items\\", StringComparison.OrdinalIgnoreCase) || norm.StartsWith("items/", StringComparison.OrdinalIgnoreCase)) return true;

		string resolved = ModelCache.ResolveModelPath(norm);
		if (!string.IsNullOrEmpty(resolved))
		{
			if (resolved.Contains("/attachments/", StringComparison.OrdinalIgnoreCase) || resolved.Contains("\\attachments\\", StringComparison.OrdinalIgnoreCase)) return true;
			if (resolved.Contains("/weapons/", StringComparison.OrdinalIgnoreCase) || resolved.Contains("\\weapons\\", StringComparison.OrdinalIgnoreCase)) return true;
			if (resolved.Contains("/items/", StringComparison.OrdinalIgnoreCase) || resolved.Contains("\\items\\", StringComparison.OrdinalIgnoreCase)) return true;
		}

		return false;
	}

	public bool GetModelIgnorePlayerColor(object objOrId)
	{
		if (objOrId == null) return false;
		string primaryKey = GetSelectedEntityOrAssetKey(objOrId);
		string normPrimary = NormalizeModelAssetKey(primaryKey);
		if (!string.IsNullOrEmpty(normPrimary) && ModelIgnorePlayerColor.TryGetValue(normPrimary, out bool b1))
			return b1;

		string assetKey = GetModelAssetKey(objOrId);
		string normAsset = NormalizeModelAssetKey(assetKey);
		if (!string.IsNullOrEmpty(normAsset) && ModelIgnorePlayerColor.TryGetValue(normAsset, out bool b2))
			return b2;

		if (!string.IsNullOrEmpty(primaryKey))
		{
			if (UnitRegistry.TryGetValue(primaryKey, out var meta)) return meta.IgnorePlayerColor;
			if (ResourceRegistry.TryGetValue(primaryKey, out var resMeta)) return resMeta.IgnorePlayerColor;
			if (PropRegistry.TryGetValue(primaryKey, out var propMeta)) return propMeta.IgnorePlayerColor;
			if (AttachmentRegistry.TryGetValue(primaryKey, out _)) return true;
		}

		if (!string.IsNullOrEmpty(normAsset))
		{
			if (ResourceRegistry.TryGetValue(normAsset, out var resMeta2)) return resMeta2.IgnorePlayerColor;
			if (PropRegistry.TryGetValue(normAsset, out var propMeta2)) return propMeta2.IgnorePlayerColor;
			if (AttachmentRegistry.TryGetValue(normAsset, out _)) return true;
		}

		if (objOrId is Prop3D || IsPropOrResourceKey(normPrimary) || (!string.IsNullOrEmpty(normAsset) && !string.Equals(normAsset, normPrimary, StringComparison.OrdinalIgnoreCase) && IsPropOrResourceKey(normAsset))
			|| IsAttachmentKey(normPrimary) || (!string.IsNullOrEmpty(normAsset) && !string.Equals(normAsset, normPrimary, StringComparison.OrdinalIgnoreCase) && IsAttachmentKey(normAsset)))
		{
			return true;
		}

		string lookupKey = !string.IsNullOrEmpty(normPrimary) ? normPrimary : normAsset;
		if (!string.IsNullOrEmpty(lookupKey))
		{
			var modelNode = ModelCache.GetModel(lookupKey) as Node;
			if (modelNode != null && !ModelShaderManager.ModelHasPlayerMask(modelNode))
			{
				ModelIgnorePlayerColor[lookupKey] = true;
				_modelYOffsetSavePending = true;
				EditorHasUnsavedChanges = true;
				return true;
			}
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

		if (UnitRegistry.TryGetValue(assetKey, out var unitMeta))
		{
			unitMeta.IgnorePlayerColor = ignorePlayerColor;
			UnitRegistry[assetKey] = unitMeta;
		}
		if (BuildingRegistry.TryGetValue(assetKey, out var bldMeta))
		{
			bldMeta.IgnorePlayerColor = ignorePlayerColor;
			BuildingRegistry[assetKey] = bldMeta;
		}
		if (ResourceRegistry.TryGetValue(assetKey, out var resMeta))
		{
			resMeta.IgnorePlayerColor = ignorePlayerColor;
			ResourceRegistry[assetKey] = resMeta;
		}
		if (PropRegistry.TryGetValue(assetKey, out var propMeta))
		{
			propMeta.IgnorePlayerColor = ignorePlayerColor;
			PropRegistry[assetKey] = propMeta;
		}

		_modelYOffsetSavePending = true;
		EditorHasUnsavedChanges = true;
		UpdateMaterialOverridesForAsset(norm);
	}

	public void UpdateMaterialOverridesForAsset(string normAssetKey)
	{
		if (string.IsNullOrEmpty(normAssetKey)) return;
		if (AllProps.Count == 0 && AllUnits.Count == 0 && _editorPreviewNode == null && (PropMultiMeshManager.Instance == null || !PropMultiMeshManager.Instance.HasGroups))
		{
			return;
		}

		float brightness = GetModelBrightness(normAssetKey);
		Color tint = GetModelColorTint(normAssetKey);
		bool ignorePlayerColor = GetModelIgnorePlayerColor(normAssetKey);
		bool normalizeLuminance = GetModelNormalizeLuminance(normAssetKey);

		foreach (var prop in AllProps)
		{
			if (GodotObject.IsInstanceValid(prop) && MatchesEntityOrAssetKey(prop, normAssetKey))
			{
				ApplyMaterialOverridesToNode(prop, brightness, tint, normalizeLuminance, ignorePlayerColor, false);
			}
		}

		foreach (var unit in AllUnits)
		{
			if (GodotObject.IsInstanceValid(unit) && MatchesEntityOrAssetKey(unit, normAssetKey))
			{
				ApplyMaterialOverridesToNode(unit, brightness, tint, normalizeLuminance, ignorePlayerColor, true);
				if (!ignorePlayerColor)
				{
					unit.UpdatePlayerColorVisual();
				}
			}
		}

		if (_editorPreviewNode != null && GodotObject.IsInstanceValid(_editorPreviewNode) && MatchesEntityOrAssetKey(_editorPreviewNode, normAssetKey))
		{
			ApplyMaterialOverridesToNode(_editorPreviewNode, brightness, tint, normalizeLuminance, ignorePlayerColor, _editorPreviewNode is Unit3D);
		}

		PropMultiMeshManager.Instance?.UpdateMaterialOverrides(normAssetKey);
		PropMultiMeshManager.Instance?.MarkDirty(normAssetKey);
	}

	public void UpdateAllMaterialOverrides()
	{
		if (AllProps.Count == 0 && AllUnits.Count == 0 && _editorPreviewNode == null && (PropMultiMeshManager.Instance == null || !PropMultiMeshManager.Instance.HasGroups))
		{
			return;
		}

		foreach (var prop in AllProps)
		{
			if (GodotObject.IsInstanceValid(prop))
			{
				string assetKey = GetModelAssetKey(prop);
				float brightness = GetModelBrightness(assetKey);
				Color tint = GetModelColorTint(assetKey);
				bool ignorePlayerColor = GetModelIgnorePlayerColor(assetKey);
				bool normalizeLuminance = GetModelNormalizeLuminance(assetKey);
				ApplyMaterialOverridesToNode(prop, brightness, tint, normalizeLuminance, ignorePlayerColor, false);
			}
		}

		foreach (var unit in AllUnits)
		{
			if (GodotObject.IsInstanceValid(unit))
			{
				string assetKey = GetModelAssetKey(unit);
				float brightness = GetModelBrightness(assetKey);
				Color tint = GetModelColorTint(assetKey);
				bool ignorePlayerColor = GetModelIgnorePlayerColor(assetKey);
				bool normalizeLuminance = GetModelNormalizeLuminance(assetKey);
				ApplyMaterialOverridesToNode(unit, brightness, tint, normalizeLuminance, ignorePlayerColor, true);
				if (!ignorePlayerColor)
				{
					unit.UpdatePlayerColorVisual();
				}
			}
		}

		if (_editorPreviewNode != null && GodotObject.IsInstanceValid(_editorPreviewNode))
		{
			string assetKey = GetModelAssetKey(_editorPreviewNode);
			float brightness = GetModelBrightness(assetKey);
			Color tint = GetModelColorTint(assetKey);
			bool ignorePlayerColor = GetModelIgnorePlayerColor(assetKey);
			bool normalizeLuminance = GetModelNormalizeLuminance(assetKey);
			ApplyMaterialOverridesToNode(_editorPreviewNode, brightness, tint, normalizeLuminance, ignorePlayerColor, _editorPreviewNode is Unit3D);
		}

		PropMultiMeshManager.Instance?.UpdateAllMaterialOverrides();
	}

	public void ApplyAllGlobalOverridesToObject(object objOrNode)
	{
		if (objOrNode == null) return;

		if (objOrNode is Unit3D unit && GodotObject.IsInstanceValid(unit))
		{
			float globalScale = GetModelScale(unit);
			unit.UpdateModelScale(globalScale);

			float yOffset = GetModelYOffset(unit);
			unit.UpdateModelYOffset(yOffset);

			float circleRatio = GetModelCollisionCircleRatio(unit);
			unit.UpdateCollisionCircleScale(circleRatio);
			if (unit.Entity != default && EcsWorld.IsAlive(unit.Entity))
			{
				float autoDetected = GetOrCalculateObstacleRadius(unit.UnitId, unit, unit.IsBuilding);
				float baseRadius = autoDetected * circleRatio;
				EcsWorld.SetOrAdd(unit.Entity, new Realm.Ecs.Components.Core.CollisionRadius(baseRadius));
			}

			if (unit.IsPreview) return;

			float brightness = GetModelBrightness(unit);
			Color tint = GetModelColorTint(unit);
			bool ignorePlayerColor = GetModelIgnorePlayerColor(unit);
			bool normalizeLuminance = GetModelNormalizeLuminance(unit);
			ApplyMaterialOverridesToNode(unit, brightness, tint, normalizeLuminance, ignorePlayerColor, true);
			if (!ignorePlayerColor)
			{
				unit.UpdatePlayerColorVisual();
			}

			bool enableProcAnim = GetModelEnableProceduralAnimation(unit);
			string procAnimId = GetModelProceduralAnimation(unit);
			if (enableProcAnim && !string.IsNullOrEmpty(procAnimId))
			{
				var cfg = ProceduralAnimationManager.GetConfig(procAnimId);
				if (cfg != null)
				{
					ModelShaderManager.SetProceduralAnimation(unit, cfg);
				}
			}
			else
			{
				ModelShaderManager.DisableProceduralAnimation(unit);
			}
		}
		else if (objOrNode is Prop3D prop && GodotObject.IsInstanceValid(prop))
		{
			float globalScale = GetModelScale(prop);
			prop.UpdateVisualScale(globalScale);

			float yOffset = GetModelYOffset(prop);
			prop.UpdateVisualYOffset(yOffset);

			float circleRatio = GetModelCollisionCircleRatio(prop);
			prop.UpdateCollisionCircleScale(circleRatio);
			if (prop.Entity != default && EcsWorld.IsAlive(prop.Entity))
			{
				float autoDetected = GetOrCalculateObstacleRadius(prop.PropId, prop);
				float baseRadius = autoDetected * circleRatio;
				EcsWorld.SetOrAdd(prop.Entity, new Realm.Ecs.Components.Core.CollisionRadius(baseRadius));
			}

			if (prop.IsPreview) return;

			float brightness = GetModelBrightness(prop);
			Color tint = GetModelColorTint(prop);
			bool ignorePlayerColor = GetModelIgnorePlayerColor(prop);
			bool normalizeLuminance = GetModelNormalizeLuminance(prop);
			ApplyMaterialOverridesToNode(prop, brightness, tint, normalizeLuminance, ignorePlayerColor, false);

			bool enableProcAnimProp = GetModelEnableProceduralAnimation(prop);
			string procAnimIdProp = GetModelProceduralAnimation(prop);
			if (enableProcAnimProp && !string.IsNullOrEmpty(procAnimIdProp))
			{
				var cfg = ProceduralAnimationManager.GetConfig(procAnimIdProp);
				if (cfg != null)
				{
					ModelShaderManager.SetProceduralAnimation(prop, cfg);
				}
			}
			else
			{
				ModelShaderManager.DisableProceduralAnimation(prop);
			}
		}
	}

	public static void ApplyMaterialOverridesToNode(
		Node node,
		float brightness = 0.5f,
		Color? colorTint = null,
		bool normalizeLuminance = true,
		bool? ignorePlayerColor = null,
		bool? isUnitOrBuilding = null)
	{
		if (node == null || !GodotObject.IsInstanceValid(node)) return;

		bool isUnit = isUnitOrBuilding ?? (node is Unit3D || node.GetParent() is Unit3D || node.Owner is Unit3D);

		Color tint = colorTint ?? new Color(1.0f, 1.0f, 1.0f);
		float multR = brightness * tint.R;
		float multG = brightness * tint.G;
		float multB = brightness * tint.B;
		bool isDefaultColor = MathF.Abs(brightness - 1.0f) < 0.001f && tint == new Color(1.0f, 1.0f, 1.0f);

		if (!isDefaultColor)
		{
			Realm.Godot.Utils.ModelShaderManager.SetBrightnessAndTint(node, brightness, tint);
		}

		Realm.Godot.Utils.ModelShaderManager.RefreshShaderMaterialsForNode(node, normalizeLuminance);
		Realm.Godot.Utils.ModelShaderManager.SetUnitReadability(node, isUnit);
		if (ignorePlayerColor.HasValue)
		{
			Realm.Godot.Utils.ModelShaderManager.SetIgnorePlayerColor(node, ignorePlayerColor.Value);
		}

		var meshNodes = FindMeshInstancesRecursive(node);
		foreach (var meshInst in meshNodes)
		{
			string nameStr = meshInst.Name.ToString();
			if (nameStr.StartsWith("_selection", StringComparison.OrdinalIgnoreCase)
				|| nameStr.StartsWith("Selection", StringComparison.OrdinalIgnoreCase)
				|| nameStr.StartsWith("_hover", StringComparison.OrdinalIgnoreCase)
				|| nameStr.StartsWith("Hover", StringComparison.OrdinalIgnoreCase)
				|| nameStr.StartsWith("BrushIndicator", StringComparison.OrdinalIgnoreCase)
				|| nameStr.StartsWith("DropShadow", StringComparison.OrdinalIgnoreCase)
				|| nameStr.Contains("SelectionRing", StringComparison.OrdinalIgnoreCase)
				|| nameStr.Contains("HoverRing", StringComparison.OrdinalIgnoreCase)) continue;

			if (meshInst.HasMeta("original_mesh"))
			{
				Mesh baseMesh = meshInst.GetMeta("original_mesh").As<Mesh>();
				if (baseMesh != null && meshInst.Mesh != baseMesh)
				{
					meshInst.Mesh = baseMesh;
				}
			}

			meshInst.SetInstanceShaderParameter(new StringName("unit_ambient_boost"), isUnit ? 0.10f : 0.0f);
			meshInst.SetInstanceShaderParameter(new StringName("unit_rim_intensity"), isUnit ? 0.25f : 0.0f);
			if (ignorePlayerColor.HasValue)
			{
				meshInst.SetInstanceShaderParameter(new StringName("ignore_player_color"), ignorePlayerColor.Value ? 1.0f : 0.0f);
			}

			if (!isDefaultColor)
			{
				int surfaceCount = meshInst.Mesh != null ? meshInst.Mesh.GetSurfaceCount() : 0;
				for (int i = 0; i < surfaceCount; i++)
				{
					Material mat = meshInst.GetSurfaceOverrideMaterial(i);
					if (mat == null && meshInst.Mesh != null)
					{
						mat = meshInst.Mesh.SurfaceGetMaterial(i);
					}

					if (mat is BaseMaterial3D baseMat)
					{
						if (meshInst.GetSurfaceOverrideMaterial(i) == null)
						{
							baseMat = (BaseMaterial3D)baseMat.Duplicate();
							meshInst.SetSurfaceOverrideMaterial(i, baseMat);
						}

						baseMat.AlbedoColor = new Color(multR, multG, multB, baseMat.AlbedoColor.A);
					}
				}

				if (meshInst.MaterialOverride is BaseMaterial3D overrideMat)
				{
					overrideMat.AlbedoColor = new Color(multR, multG, multB, overrideMat.AlbedoColor.A);
				}
			}
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

	public void FlushModelCollisionCircleSave()
	{
		FlushModelYOffsetSave();
	}

	public void UpdateCollisionRadiiForAsset(string normAssetKey)
	{
		float ratio = GetModelCollisionCircleRatio(normAssetKey);

		foreach (var prop in AllProps)
		{
			if (GodotObject.IsInstanceValid(prop) && MatchesEntityOrAssetKey(prop, normAssetKey))
			{
				prop.UpdateCollisionCircleScale(ratio);
				if (prop.Entity != default && EcsWorld.IsAlive(prop.Entity))
				{
					float autoDetected = GetOrCalculateObstacleRadius(prop.PropId, prop);
					float baseRadius = autoDetected * ratio;
					EcsWorld.SetOrAdd(prop.Entity, new Realm.Ecs.Components.Core.CollisionRadius(baseRadius));
				}
			}
		}

		foreach (var unit in AllUnits)
		{
			if (GodotObject.IsInstanceValid(unit) && MatchesEntityOrAssetKey(unit, normAssetKey))
			{
				unit.UpdateCollisionCircleScale(ratio);
				if (unit.Entity != default && EcsWorld.IsAlive(unit.Entity))
				{
					float autoDetected = GetOrCalculateObstacleRadius(unit.UnitId, unit);
					float baseRadius = autoDetected * ratio;
					EcsWorld.SetOrAdd(unit.Entity, new Realm.Ecs.Components.Core.CollisionRadius(baseRadius));
				}
			}
		}

		if (_editorPreviewNode != null && GodotObject.IsInstanceValid(_editorPreviewNode) && MatchesEntityOrAssetKey(_editorPreviewNode, normAssetKey))
		{
			if (_editorPreviewNode is Unit3D previewUnit)
			{
				previewUnit.UpdateCollisionCircleScale(ratio);
			}
			else if (_editorPreviewNode is Prop3D previewProp)
			{
				previewProp.UpdateCollisionCircleScale(ratio);
			}
		}
	}

	private const float MaxSafeModelYOffset = 50f;
	private const float MinSafeModelCollisionRatio = 0.1f;
	private const float MaxSafeModelCollisionRatio = 10f;
	private const float MinSafeModelScale = 0.01f;
	private const float MaxSafeModelScale = 20f;

	private bool IsValidModelYOffset(string assetKey, float val)
	{
		if (!float.IsFinite(val) || Math.Abs(val) > MaxSafeModelYOffset)
		{
			GD.PushWarning($"Ignoring invalid y_offset {val} for model '{assetKey}' (|offset| > {MaxSafeModelYOffset}).");
			return false;
		}
		return true;
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

	private bool IsValidModelCollisionRatio(string assetKey, float val)
	{
		if (!float.IsFinite(val) || val < MinSafeModelCollisionRatio || val > MaxSafeModelCollisionRatio)
		{
			GD.PushWarning($"Ignoring invalid collision_circle_ratio {val} for model '{assetKey}' (expected {MinSafeModelCollisionRatio}..{MaxSafeModelCollisionRatio}).");
			return false;
		}
		return true;
	}

	public void LoadModelYOffsetsFromMetadataJson(string directory = null)
	{
		try
		{
			string mapDir = !string.IsNullOrEmpty(directory) ? directory : CurrentMapDirectory;
			if (string.IsNullOrEmpty(mapDir))
			{
				mapDir = Godot.ProjectSettings.GlobalizePath(MapEditorHUD.TempWorkspaceGodotPath);
			}
			LoadUnitMetadata(mapDir);
			var metaService = _metadataService ?? Realm.Godot.Services.MetadataService.Instance;
			var metadata = metaService.LoadMetadata(mapDir);

			ModelYOffsets.Clear();
			ModelScales.Clear();
			ModelCollisionCircleRatios.Clear();
			ModelObstacleRadii.Clear();
			ModelBrightness.Clear();
			ModelColorTint.Clear();
			ModelDespillPlayerColor.Clear();
			ModelIgnorePlayerColor.Clear();
			ModelNormalizeLuminance.Clear();
			ModelSpawnShaders.Clear();
			ModelDeathShaders.Clear();

			void ProcessEntities<T>(IEnumerable<T> items, float defaultScale, Func<T, string> getId, Func<T, string> getModelPath, Func<T, float> getYOffset, Func<T, float> getScale, Func<T, float> getCollisionCircle, Func<T, float> getBrightness, Func<T, string> getTint, Func<T, bool> getDespillPlayerColor, Func<T, bool> getNormalizeLuminance)
			{
				if (items == null) return;
				foreach (var item in items)
				{
					string uId = getId(item);
					if (string.IsNullOrEmpty(uId)) continue;
					string normKey = NormalizeModelAssetKey(uId);
					float yVal = getYOffset(item);
					ModelYOffsets[normKey] = yVal;
					float sVal = getScale(item);
					if (sVal > 0f)
					{
						ModelScales[normKey] = sVal;
					}
					else if (!ModelScales.ContainsKey(normKey))
					{
						ModelScales[normKey] = defaultScale;
					}

					string mPath = getModelPath(item);
					if (!string.IsNullOrEmpty(mPath))
					{
						string normModel = NormalizeModelAssetKey(mPath);
						if (ModelScales.TryGetValue(normKey, out float assignedScale) && !ModelScales.ContainsKey(normModel))
						{
							ModelScales[normModel] = assignedScale;
						}
					}

					ModelCollisionCircleRatios[normKey] = getCollisionCircle(item);
					ModelBrightness[normKey] = getBrightness(item);
					string tintStr = getTint(item);
					if (!string.IsNullOrEmpty(tintStr))
					{
						ModelColorTint[normKey] = Color.FromString(tintStr, new Color(1, 1, 1));
					}
					ModelDespillPlayerColor[normKey] = getDespillPlayerColor(item);
					ModelNormalizeLuminance[normKey] = getNormalizeLuminance(item);
				}
			}

			ProcessEntities(metadata.CustomResources, 2.75f, r => r.TemplateID, r => r.ModelPath, r => r.YOffset, r => r.Scale, r => r.CollisionCircle, r => r.Brightness, r => r.Tint, r => r.DespillPlayerColor, r => r.NormalizeLuminance);
			ProcessEntities(metadata.CustomBuildings, 1.2f, b => b.TemplateID, b => b.ModelPath, b => b.YOffset, b => b.Scale, b => b.CollisionCircle, b => b.Brightness, b => b.Tint, b => b.DespillPlayerColor, b => b.NormalizeLuminance);
			ProcessEntities(metadata.CustomProps, 1.0f, p => p.TemplateID, p => p.ModelPath, p => p.YOffset, p => p.Scale, p => p.CollisionCircle, p => p.Brightness, p => p.Tint, p => p.DespillPlayerColor, p => p.NormalizeLuminance);
			ProcessEntities(metadata.CustomUnits, 1.5f, u => u.TemplateID, u => u.ModelPath, u => u.YOffset, u => u.Scale, u => u.CollisionCircle, u => u.Brightness, u => u.Tint, u => u.DespillPlayerColor, u => u.NormalizeLuminance);

			var assetsObj = Realm.Godot.Utils.MapAssetHelper.LoadAssets(mapDir);
			if (assetsObj != null && assetsObj.ContainsKey("glb") && assetsObj["glb"] is System.Text.Json.Nodes.JsonObject glbObj)
			{
				foreach (var catKvp in glbObj)
				{
					if (catKvp.Value is System.Text.Json.Nodes.JsonObject catDict)
					{
						foreach (var itemKvp in catDict)
						{
							if (itemKvp.Value is System.Text.Json.Nodes.JsonObject itemObj)
							{
								if (itemObj.ContainsKey("y_offset") && float.TryParse(itemObj["y_offset"]?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out float yVal))
								{
									if (IsValidModelYOffset(itemKvp.Key, yVal))
									{
										ModelYOffsets[NormalizeModelAssetKey(itemKvp.Key)] = yVal;
									}
								}
								if (itemObj.ContainsKey("scale") && float.TryParse(itemObj["scale"]?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out float sVal))
								{
									if (IsValidModelScale(itemKvp.Key, sVal))
									{
										ModelScales[NormalizeModelAssetKey(itemKvp.Key)] = sVal;
									}
								}
								else if (itemObj.ContainsKey("model_scale") && float.TryParse(itemObj["model_scale"]?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out float msVal))
								{
									if (IsValidModelScale(itemKvp.Key, msVal))
									{
										ModelScales[NormalizeModelAssetKey(itemKvp.Key)] = msVal;
									}
								}
								if (itemObj.ContainsKey("collision_circle_ratio") && float.TryParse(itemObj["collision_circle_ratio"]?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out float rVal))
								{
									if (IsValidModelCollisionRatio(itemKvp.Key, rVal))
									{
										ModelCollisionCircleRatios[NormalizeModelAssetKey(itemKvp.Key)] = rVal;
									}
								}
								if (itemObj.ContainsKey("collision_radius") && float.TryParse(itemObj["collision_radius"]?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out float radVal) && radVal > 0f)
								{
									ModelObstacleRadii[NormalizeModelAssetKey(itemKvp.Key)] = radVal;
								}
								if (itemObj.ContainsKey("brightness") && float.TryParse(itemObj["brightness"]?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out float brightVal))
								{
									ModelBrightness[NormalizeModelAssetKey(itemKvp.Key)] = brightVal;
								}
								if (itemObj.ContainsKey("tint") && itemObj["tint"] != null && Color.HtmlIsValid(itemObj["tint"]?.ToString()))
								{
									ModelColorTint[NormalizeModelAssetKey(itemKvp.Key)] = Color.FromHtml(itemObj["tint"]!.ToString());
								}
								else if (itemObj.ContainsKey("color_tint") && itemObj["color_tint"] != null && Color.HtmlIsValid(itemObj["color_tint"]?.ToString()))
								{
									ModelColorTint[NormalizeModelAssetKey(itemKvp.Key)] = Color.FromHtml(itemObj["color_tint"]!.ToString());
								}
								else if (itemObj.ContainsKey("ColorTint") && itemObj["ColorTint"] != null && Color.HtmlIsValid(itemObj["ColorTint"]?.ToString()))
								{
									ModelColorTint[NormalizeModelAssetKey(itemKvp.Key)] = Color.FromHtml(itemObj["ColorTint"]!.ToString());
								}
								string normKey = NormalizeModelAssetKey(itemKvp.Key);
								if (itemObj.ContainsKey("DespillPlayerColor") && bool.TryParse(itemObj["DespillPlayerColor"]?.ToString(), out bool dpcVal))
								{
									ModelDespillPlayerColor[normKey] = dpcVal;
								}
								else if (!ModelDespillPlayerColor.ContainsKey(normKey))
								{
									ModelDespillPlayerColor[normKey] = false;
								}
								if (itemObj.ContainsKey("normalize_luminance") && bool.TryParse(itemObj["normalize_luminance"]?.ToString(), out bool nlVal))
								{
									ModelNormalizeLuminance[normKey] = nlVal;
								}
								if (itemObj.ContainsKey("ignore_player_color") && bool.TryParse(itemObj["ignore_player_color"]?.ToString(), out bool ipcVal))
								{
									ModelIgnorePlayerColor[normKey] = ipcVal;
								}
								else if (itemObj.ContainsKey("IgnorePlayerColor") && bool.TryParse(itemObj["IgnorePlayerColor"]?.ToString(), out bool ipcVal2))
								{
									ModelIgnorePlayerColor[normKey] = ipcVal2;
								}
								else if (catKvp.Key == "props" || catKvp.Key == "resources" || catKvp.Key == "attachments" || catKvp.Key == "weapons" || catKvp.Key == "items" || (itemObj.ContainsKey("default_asset_type") && (itemObj["default_asset_type"]?.ToString() == "props" || itemObj["default_asset_type"]?.ToString() == "resources" || itemObj["default_asset_type"]?.ToString() == "attachments" || itemObj["default_asset_type"]?.ToString() == "weapons" || itemObj["default_asset_type"]?.ToString() == "items")))
								{
									ModelIgnorePlayerColor[normKey] = true;
								}
								else if (!ModelIgnorePlayerColor.ContainsKey(normKey))
								{
									ModelIgnorePlayerColor[normKey] = false;
								}

								string itemSpawn = itemObj["spawn_shader"]?.ToString() ?? itemObj["SpawnShader"]?.ToString();
								if (!string.IsNullOrWhiteSpace(itemSpawn))
								{
									ModelSpawnShaders[normKey] = itemSpawn.Trim();
								}
								string itemDeath = itemObj["death_shader"]?.ToString() ?? itemObj["DeathShader"]?.ToString() ?? itemObj["despawn_shader"]?.ToString() ?? itemObj["DespawnShader"]?.ToString();
								if (!string.IsNullOrWhiteSpace(itemDeath))
								{
									ModelDeathShaders[normKey] = itemDeath.Trim();
								}
							}
						}
					}
				}
			}

			if (metadata.Models != null)
			{
				foreach (var kvp in metadata.Models)
				{
					string normKey = NormalizeModelAssetKey(kvp.Key);
					var model = kvp.Value;
					if (model == null) continue;

					if (!string.IsNullOrWhiteSpace(model.SpawnShaders))
					{
						ModelSpawnShaders[normKey] = model.SpawnShaders.Trim();
					}
					if (!string.IsNullOrWhiteSpace(model.DeathShaders))
					{
						ModelDeathShaders[normKey] = model.DeathShaders.Trim();
					}
					if (model.Offsets.HasValue && IsValidModelYOffset(kvp.Key, model.Offsets.Value))
					{
						ModelYOffsets[normKey] = model.Offsets.Value;
					}
					if (model.Scales.HasValue && IsValidModelScale(kvp.Key, model.Scales.Value))
					{
						ModelScales[normKey] = model.Scales.Value;
					}
					if (model.CollisionCircleRatios.HasValue && IsValidModelCollisionRatio(kvp.Key, model.CollisionCircleRatios.Value))
					{
						ModelCollisionCircleRatios[normKey] = model.CollisionCircleRatios.Value;
					}
					if (model.ObstacleRadii.HasValue && model.ObstacleRadii.Value > 0f)
					{
						ModelObstacleRadii[normKey] = model.ObstacleRadii.Value;
					}
					if (model.Brightness.HasValue)
					{
						ModelBrightness[normKey] = model.Brightness.Value;
					}
					if (!string.IsNullOrWhiteSpace(model.ColorTint) && Color.HtmlIsValid(model.ColorTint))
					{
						ModelColorTint[normKey] = Color.FromHtml(model.ColorTint);
					}
					if (model.DespillPlayerColor.HasValue)
					{
						ModelDespillPlayerColor[normKey] = model.DespillPlayerColor.Value;
					}
					if (model.NormalizeLuminance.HasValue)
					{
						ModelNormalizeLuminance[normKey] = model.NormalizeLuminance.Value;
					}
					if (model.IgnorePlayerColor.HasValue)
					{
						ModelIgnorePlayerColor[normKey] = model.IgnorePlayerColor.Value;
					}
					if (!string.IsNullOrWhiteSpace(model.ProceduralAnimation))
					{
						ModelProceduralAnimations[normKey] = model.ProceduralAnimation.Trim();
					}
					if (model.EnableProceduralAnimation.HasValue)
					{
						ModelEnableProceduralAnimations[normKey] = model.EnableProceduralAnimation.Value;
					}
				}
			}

			UpdateAllMaterialOverrides();
		}
		catch (Exception ex)
		{
			GD.PrintErr($"Failed to load metadata overrides from JSON: {ex.Message}");
		}
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

	public void RefreshAllPlacedObjectModels(string targetId = null)
	{
		if (!string.IsNullOrEmpty(targetId))
		{
			ModelCache.InvalidateModelPath(targetId);
			Prop3D.InvalidateModelPathCache(targetId);
		}
		else
		{
			ModelCache.Clear();
			Prop3D.ClearModelPathCache();
		}

		foreach (var unit in AllUnits)
		{
			if (!GodotObject.IsInstanceValid(unit)) continue;
			if (string.IsNullOrEmpty(targetId) 
				|| string.Equals(unit.UnitId, targetId, StringComparison.OrdinalIgnoreCase)
				|| string.Equals(System.IO.Path.GetFileNameWithoutExtension(unit.UnitId), targetId, StringComparison.OrdinalIgnoreCase)
				|| string.Equals(unit.UnitId, System.IO.Path.GetFileNameWithoutExtension(targetId), StringComparison.OrdinalIgnoreCase))
			{
				string targetModel = null;
				bool isBuilding = unit.IsBuilding;
				if (UnitRegistry.TryGetValue(unit.UnitId, out var meta) && !string.IsNullOrEmpty(meta.ModelPath))
				{
					targetModel = meta.ModelPath;
				}
				else if (BuildingRegistry.TryGetValue(unit.UnitId, out var bldMeta) && !string.IsNullOrEmpty(bldMeta.ModelPath))
				{
					targetModel = bldMeta.ModelPath;
					isBuilding = true;
				}
				else if (ResourceRegistry.TryGetValue(unit.UnitId, out var resMeta) && !string.IsNullOrEmpty(resMeta.ModelPath))
				{
					targetModel = resMeta.ModelPath;
				}
				else if (PropRegistry.TryGetValue(unit.UnitId, out var propMeta) && !string.IsNullOrEmpty(propMeta.ModelPath))
				{
					targetModel = propMeta.ModelPath;
				}
				else if (!string.IsNullOrEmpty(unit.ModelPath))
				{
					targetModel = unit.ModelPath;
				}

				if (!string.IsNullOrEmpty(targetModel))
				{
					string modelPath = GetFallbackModelPath(targetModel, isBuilding);
					unit.LoadModel(modelPath);
				}
			}
		}

		foreach (var prop in AllProps)
		{
			if (!GodotObject.IsInstanceValid(prop)) continue;
			if (string.IsNullOrEmpty(targetId) 
				|| string.Equals(prop.PropId, targetId, StringComparison.OrdinalIgnoreCase)
				|| string.Equals(System.IO.Path.GetFileNameWithoutExtension(prop.PropId), targetId, StringComparison.OrdinalIgnoreCase)
				|| string.Equals(prop.PropId, System.IO.Path.GetFileNameWithoutExtension(targetId), StringComparison.OrdinalIgnoreCase))
			{
				prop.RefreshPropVisual();
			}
		}

		if (!string.IsNullOrEmpty(targetId))
		{
			PropMultiMeshManager.Instance?.MarkDirty(targetId);
		}
	}

	public void SaveModelYOffsetsToMetadataJson(string directory = null)
	{
		try
		{
			string mapDir = !string.IsNullOrEmpty(directory) ? directory : CurrentMapDirectory;
			if (string.IsNullOrEmpty(mapDir))
			{
				mapDir = Godot.ProjectSettings.GlobalizePath(MapEditorHUD.TempWorkspaceGodotPath);
			}
			var metaService = _metadataService ?? Realm.Godot.Services.MetadataService.Instance;
			metaService.UpdateMetadata(mapDir, meta =>
			{
				if (meta.Models == null) meta.Models = new Dictionary<string, ModelMetadata>(StringComparer.OrdinalIgnoreCase);

				var allKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
				foreach (var k in ModelYOffsets.Keys) allKeys.Add(k);
				foreach (var k in ModelScales.Keys) allKeys.Add(k);
				foreach (var k in ModelCollisionCircleRatios.Keys) allKeys.Add(k);
				foreach (var k in ModelObstacleRadii.Keys) allKeys.Add(k);
				foreach (var k in ModelBrightness.Keys) allKeys.Add(k);
				foreach (var k in ModelColorTint.Keys) allKeys.Add(k);
				foreach (var k in ModelDespillPlayerColor.Keys) allKeys.Add(k);
				foreach (var k in ModelNormalizeLuminance.Keys) allKeys.Add(k);
				foreach (var k in ModelIgnorePlayerColor.Keys) allKeys.Add(k);
				foreach (var k in ModelSpawnShaders.Keys) allKeys.Add(k);
				foreach (var k in ModelDeathShaders.Keys) allKeys.Add(k);
				foreach (var k in ModelProceduralAnimations.Keys) allKeys.Add(k);
				foreach (var k in ModelEnableProceduralAnimations.Keys) allKeys.Add(k);

				foreach (var key in allKeys)
				{
					if (!meta.Models.TryGetValue(key, out var modelMeta) || modelMeta == null)
					{
						modelMeta = new ModelMetadata();
						meta.Models[key] = modelMeta;
					}

					if (ModelYOffsets.TryGetValue(key, out float yVal)) modelMeta.Offsets = yVal;
					if (ModelScales.TryGetValue(key, out float sVal)) modelMeta.Scales = sVal;
					if (ModelCollisionCircleRatios.TryGetValue(key, out float cVal)) modelMeta.CollisionCircleRatios = cVal;
					if (ModelObstacleRadii.TryGetValue(key, out float rVal)) modelMeta.ObstacleRadii = rVal;
					if (ModelBrightness.TryGetValue(key, out float bVal)) modelMeta.Brightness = bVal;
					if (ModelColorTint.TryGetValue(key, out Color tVal)) modelMeta.ColorTint = $"#{tVal.ToHtml(false)}";
					if (ModelDespillPlayerColor.TryGetValue(key, out bool dVal)) modelMeta.DespillPlayerColor = dVal;
					if (ModelNormalizeLuminance.TryGetValue(key, out bool nVal)) modelMeta.NormalizeLuminance = nVal;
					if (ModelIgnorePlayerColor.TryGetValue(key, out bool iVal)) modelMeta.IgnorePlayerColor = iVal;
					if (ModelSpawnShaders.TryGetValue(key, out string? ssVal) && !string.IsNullOrWhiteSpace(ssVal)) modelMeta.SpawnShaders = ssVal;
					if (ModelDeathShaders.TryGetValue(key, out string? dsVal) && !string.IsNullOrWhiteSpace(dsVal)) modelMeta.DeathShaders = dsVal;
					if (ModelProceduralAnimations.TryGetValue(key, out string? paVal) && !string.IsNullOrWhiteSpace(paVal)) modelMeta.ProceduralAnimation = paVal;
					if (ModelEnableProceduralAnimations.TryGetValue(key, out bool epVal)) modelMeta.EnableProceduralAnimation = epVal;
				}

				void UpdateEntityOverrides(List<UnitMetadata> entities)
				{
					if (entities == null) return;
					for (int i = 0; i < entities.Count; i++)
					{
						var entity = entities[i];
						if (string.IsNullOrEmpty(entity.TemplateID)) continue;
						string normKey = NormalizeModelAssetKey(entity.TemplateID);
						string normModel = !string.IsNullOrEmpty(entity.ModelPath) ? NormalizeModelAssetKey(entity.ModelPath) : "";

						if (ModelDespillPlayerColor.TryGetValue(normKey, out bool dVal1)) entity.DespillPlayerColor = dVal1;
						else if (!string.IsNullOrEmpty(normModel) && ModelDespillPlayerColor.TryGetValue(normModel, out bool dVal2)) entity.DespillPlayerColor = dVal2;

						if (ModelNormalizeLuminance.TryGetValue(normKey, out bool nVal1)) entity.NormalizeLuminance = nVal1;
						else if (!string.IsNullOrEmpty(normModel) && ModelNormalizeLuminance.TryGetValue(normModel, out bool nVal2)) entity.NormalizeLuminance = nVal2;

						if (ModelIgnorePlayerColor.TryGetValue(normKey, out bool iVal1)) entity.IgnorePlayerColor = iVal1;
						else if (!string.IsNullOrEmpty(normModel) && ModelIgnorePlayerColor.TryGetValue(normModel, out bool iVal2)) entity.IgnorePlayerColor = iVal2;

						string sVal = "";
						if (!string.IsNullOrEmpty(normKey) && ModelSpawnShaders.TryGetValue(normKey, out string sv1)) sVal = sv1;
						else if (!string.IsNullOrEmpty(normModel) && ModelSpawnShaders.TryGetValue(normModel, out string sv2)) sVal = sv2;
						else sVal = GetModelSpawnShader(entity.TemplateID);

						entity.SpawnShader = !string.IsNullOrWhiteSpace(sVal) ? sVal : null;

						string dVal = "";
						if (!string.IsNullOrEmpty(normKey) && ModelDeathShaders.TryGetValue(normKey, out string dv1)) dVal = dv1;
						else if (!string.IsNullOrEmpty(normModel) && ModelDeathShaders.TryGetValue(normModel, out string dv2)) dVal = dv2;
						else dVal = GetModelDeathShader(entity.TemplateID);

						entity.DeathShader = !string.IsNullOrWhiteSpace(dVal) ? dVal : null;
						entities[i] = entity;
					}
				}

				UpdateEntityOverrides(meta.CustomUnits);
				UpdateEntityOverrides(meta.CustomBuildings);

				if (meta.CustomResources != null)
				{
					for (int i = 0; i < meta.CustomResources.Count; i++)
					{
						var res = meta.CustomResources[i];
						if (string.IsNullOrEmpty(res.TemplateID)) continue;
						string normKey = NormalizeModelAssetKey(res.TemplateID);
						string normModel = !string.IsNullOrEmpty(res.ModelPath) ? NormalizeModelAssetKey(res.ModelPath) : "";

						if (ModelDespillPlayerColor.TryGetValue(normKey, out bool dVal1)) res.DespillPlayerColor = dVal1;
						else if (!string.IsNullOrEmpty(normModel) && ModelDespillPlayerColor.TryGetValue(normModel, out bool dVal2)) res.DespillPlayerColor = dVal2;

						if (ModelNormalizeLuminance.TryGetValue(normKey, out bool nVal1)) res.NormalizeLuminance = nVal1;
						else if (!string.IsNullOrEmpty(normModel) && ModelNormalizeLuminance.TryGetValue(normModel, out bool nVal2)) res.NormalizeLuminance = nVal2;

						if (ModelIgnorePlayerColor.TryGetValue(normKey, out bool iVal1)) res.IgnorePlayerColor = iVal1;
						else if (!string.IsNullOrEmpty(normModel) && ModelIgnorePlayerColor.TryGetValue(normModel, out bool iVal2)) res.IgnorePlayerColor = iVal2;

						string sVal = !string.IsNullOrEmpty(normKey) && ModelSpawnShaders.TryGetValue(normKey, out string sv1) ? sv1 : (!string.IsNullOrEmpty(normModel) && ModelSpawnShaders.TryGetValue(normModel, out string sv2) ? sv2 : GetModelSpawnShader(res.TemplateID));
						string dVal = !string.IsNullOrEmpty(normKey) && ModelDeathShaders.TryGetValue(normKey, out string dv1) ? dv1 : (!string.IsNullOrEmpty(normModel) && ModelDeathShaders.TryGetValue(normModel, out string dv2) ? dv2 : GetModelDeathShader(res.TemplateID));
						res.SpawnShader = !string.IsNullOrWhiteSpace(sVal) ? sVal : null;
						res.DeathShader = !string.IsNullOrWhiteSpace(dVal) ? dVal : null;
						meta.CustomResources[i] = res;
					}
				}

				if (meta.CustomProps != null)
				{
					for (int i = 0; i < meta.CustomProps.Count; i++)
					{
						var prop = meta.CustomProps[i];
						if (string.IsNullOrEmpty(prop.TemplateID)) continue;
						string normKey = NormalizeModelAssetKey(prop.TemplateID);
						string normModel = !string.IsNullOrEmpty(prop.ModelPath) ? NormalizeModelAssetKey(prop.ModelPath) : "";

						if (ModelDespillPlayerColor.TryGetValue(normKey, out bool dVal1)) prop.DespillPlayerColor = dVal1;
						else if (!string.IsNullOrEmpty(normModel) && ModelDespillPlayerColor.TryGetValue(normModel, out bool dVal2)) prop.DespillPlayerColor = dVal2;

						if (ModelNormalizeLuminance.TryGetValue(normKey, out bool nVal1)) prop.NormalizeLuminance = nVal1;
						else if (!string.IsNullOrEmpty(normModel) && ModelNormalizeLuminance.TryGetValue(normModel, out bool nVal2)) prop.NormalizeLuminance = nVal2;

						if (ModelIgnorePlayerColor.TryGetValue(normKey, out bool iVal1)) prop.IgnorePlayerColor = iVal1;
						else if (!string.IsNullOrEmpty(normModel) && ModelIgnorePlayerColor.TryGetValue(normModel, out bool iVal2)) prop.IgnorePlayerColor = iVal2;

						string sVal = !string.IsNullOrEmpty(normKey) && ModelSpawnShaders.TryGetValue(normKey, out string sv1) ? sv1 : (!string.IsNullOrEmpty(normModel) && ModelSpawnShaders.TryGetValue(normModel, out string sv2) ? sv2 : GetModelSpawnShader(prop.TemplateID));
						string dVal = !string.IsNullOrEmpty(normKey) && ModelDeathShaders.TryGetValue(normKey, out string dv1) ? dv1 : (!string.IsNullOrEmpty(normModel) && ModelDeathShaders.TryGetValue(normModel, out string dv2) ? dv2 : GetModelDeathShader(prop.TemplateID));
						prop.SpawnShader = !string.IsNullOrWhiteSpace(sVal) ? sVal : null;
						prop.DeathShader = !string.IsNullOrWhiteSpace(dVal) ? dVal : null;
						meta.CustomProps[i] = prop;
					}
				}
			});
		}
		catch (Exception ex)
		{
			GD.PrintErr($"SaveModelYOffsetsToMetadataJson error: {ex.Message}");
		}
	}
	public void ClearMapEntirely()
	{
		if (GroundTerrain == null) return;
		
		var unitsCopy = new List<Unit3D>(AllUnits);
		foreach (var unit in unitsCopy)
		{
			if (GodotObject.IsInstanceValid(unit))
			{
				DeleteNodeExternal(unit);
			}
		}
		SelectedUnits.Clear();
		AllUnits.Clear();
		ClearAllBuildQueueGhosts();
		AllProps.Clear();
		PropMultiMeshManager.Instance?.Clear();
		AllDecals.Clear();
		AllVfx.Clear();
		ActivePings.Clear();
		EntityToUnit3D.Clear();
		EntityToProp3D.Clear();
		EntityToVfx3D.Clear();
		if (_controlGroups != null)
		{
			for (int i = 0; i < _controlGroups.Length; i++)
			{
				_controlGroups[i]?.Clear();
			}
		}
		
		var childrenCopy = new List<Node>(GetChildren());
		foreach (var child in childrenCopy)
		{
			if (child is Prop3D prop && GodotObject.IsInstanceValid(prop))
			{
				DeleteNodeExternal(prop);
			}
			else if (child is Decal decal && GodotObject.IsInstanceValid(decal))
			{
				DeleteNodeExternal(decal);
			}
			else if (child is ProceduralVfxInstance3D vfx && GodotObject.IsInstanceValid(vfx))
			{
				DeleteNodeExternal(vfx);
			}
		}
		
		if (GroundTerrain != null)
		{
			int width = GroundTerrain.Width;
			int depth = GroundTerrain.Depth;

			if (GroundTerrain.Cells == null || GroundTerrain.Cells.GetLength(0) != width || GroundTerrain.Cells.GetLength(1) != depth)
			{
				GroundTerrain.Cells = new Realm.Ecs.Components.Terrain.TerrainCell[width, depth];
			}
			var cells = GroundTerrain.Cells;

			if (GroundTerrain.SplatMap == null || GroundTerrain.SplatMap.GetLength(0) < width + 1 || GroundTerrain.SplatMap.GetLength(1) < depth + 1)
			{
				GroundTerrain.SplatMap = new TerrainSplatWeights[width + 1, depth + 1];
			}
			var splatMap = GroundTerrain.SplatMap;

			if (GroundTerrain.CliffSplatMap == null || GroundTerrain.CliffSplatMap.GetLength(0) < width + 1 || GroundTerrain.CliffSplatMap.GetLength(1) < depth + 1)
			{
				GroundTerrain.CliffSplatMap = new TerrainSplatWeights[width + 1, depth + 1];
			}
			var cliffSplatMap = GroundTerrain.CliffSplatMap;

			var pathingCodes = GroundTerrain.PathingCodes;
			if (pathingCodes == null || pathingCodes.GetLength(0) != width || pathingCodes.GetLength(1) != depth)
			{
				pathingCodes = new int[width, depth];
			}

			int defaultPathing = EditableTerrain.GetDefaultPathingCode(Realm.Ecs.Components.Terrain.WaterType.None);
			for (int z = 0; z < depth; z++)
			{
				for (int x = 0; x < width; x++)
				{
					cells[x, z] = new Realm.Ecs.Components.Terrain.TerrainCell(0.0f);
					pathingCodes[x, z] = defaultPathing;
				}
			}

			for (int z = 0; z <= depth; z++)
			{
				for (int x = 0; x <= width; x++)
				{
					splatMap[x, z] = TerrainSplatWeights.CreateSolid(0);
					cliffSplatMap[x, z] = TerrainSplatWeights.CreateSolid(1);
				}
			}

			if (EcsWorld != null && EcsWorld.IsAlive(WorldEntity) && EcsWorld.Has<Realm.Ecs.Components.Terrain.TerrainState>(WorldEntity))
			{
				ref var ts = ref EcsWorld.Get<Realm.Ecs.Components.Terrain.TerrainState>(WorldEntity);
				ts.Cells = cells;
				ts.PathingCodes = pathingCodes;
				EcsWorld.Set(WorldEntity, ts);
			}

			GroundTerrain.UpdateMeshAndPhysics(true, true);
			GroundTerrain.UpdatePathingTexture();
			UpdatePathingOverlay();
		}

		_editorService?.ResetAllState();
		HideSelectionHighlight();

		EditorHistoryManager.Clear();
		EditorHasUnsavedChanges = false;
		RebuildGridOverlayMeshExternal();
		
		EditorCameraBoundsLeft = -95.0f;
		EditorCameraBoundsRight = 95.0f;
		EditorCameraBoundsTop = -95.0f;
		EditorCameraBoundsBottom = 125.0f;
		MapEditorHUD.Instance?.UpdateCameraBoundsUI();
		RebuildCameraBoundsOverlay();

		EditorCoordinates.Clear();
		RebuildAllCoordinatePersistentMeshes();
		HideCoordinateSelectionOutline();
		MapEditorHUD.Instance?.RefreshCoordinateListExternal();

		MapEditorHUD.ResetFolderLocations();
		MapEditorHUD.Instance?.ClearTempWorkspaceExternal();
		MapEditorHUD.Instance?.GenerateVSCodeFilesExternal();
		MapEditorHUD.Instance?.ReadMetadataAndRefreshTextures();
		MapEditorHUD.Instance?.LoadMapProperties();
		MapEditorHUD.Instance?.UpdateMapNameHeader();
		MapEditorHUD.Instance?.SaveCurrentDirectoryBlake3();
		MapEditorHUD.Instance?.ShowFeedbackExternal("Map reset: cleared all entities & terrain");
		MapEditorHUD.Instance?.RegenerateMinimap();
	}

	private bool IsMouseOverUI()
	{
		if (SettingsMenu.IsOpen)
		{
			return true;
		}

		if (GodotObject.IsInstanceValid(MapEditorHUD.Instance))
		{
			return MapEditorHUD.Instance.IsMouseOverUI(GetViewport().GetMousePosition());
		}

		if (GodotObject.IsInstanceValid(InGameHUD.Instance))
		{
			return InGameHUD.Instance.IsMouseOverUI(GetViewport().GetMousePosition());
		}

		var hoveredControl = GetViewport().GuiGetHoveredControl();
		if (hoveredControl != null)
		{
			return true;
		}

		return false;
	}

	private long _lastTerrainMeshRebuildMs = long.MinValue;
	private Rect2I? _terrainFlushRegion;
	private bool _terrainGeometryDirty;
	private bool _terrainHeightsDirty;
	private bool _terrainPathingDirty;
	private const float TerrainMeshRebuildPeriodMs = 33.3f;

	private void ApplyContinuousTerrainEditing(Vector3 worldPos, float delta, bool isFirstClick = false)
	{
		if (GroundTerrain == null) return;

		var positions = new List<Vector3> { worldPos };
		if (EditorMirrorMode != MirrorMode.None)
		{
			foreach (var t in GetMirroredTransforms(worldPos, 0.0f))
			{
				positions.Add(t.Position);
			}
		}

		int pathingMask = 0;
		bool pathingAdd = true;
		if (ActiveEditorTool == EditorTool.PaintPathing && MapEditorHUD.Instance != null)
		{
			pathingMask = MapEditorHUD.Instance.GetSelectedPathingMask();
			pathingAdd = MapEditorHUD.Instance.IsPathingAddMode();
		}

		bool applyGround = MapEditorHUD.Instance?.IsApplyGroundTextureEnabled() ?? true;
		bool applyCliff = MapEditorHUD.Instance?.IsApplyCliffTextureEnabled() ?? true;

		bool anyModified = false;

		float targetBlockHeight = ActiveEditorTool == EditorTool.Height ? EditorExactHeight : EditorBlockLevelHeight;
		foreach (var pos in positions)
		{
			var result = _editorService.ApplyContinuousTerrainEditing(
				pos, delta,
				ActiveEditorTool,
				EditorBrushRadius, EditorBrushStrength,
				EditorBrushIsSquare,
				EditorBlockMode, targetBlockHeight,
				EditorPaintTextureIndex, EditorCliffPaintTextureIndex,
				pathingMask, pathingAdd,
				isFirstClick,
				applyGround, applyCliff);

			if (result.HeightsModified || result.SplatModified || result.PathingModified)
			{
				Rect2I affected = new Rect2I(result.MinX - 2, result.MinZ - 2, result.MaxX - result.MinX + 5, result.MaxZ - result.MinZ + 5);

				// Accumulate the affected region so the periodic and final flush covers all modified cells since the last mesh rebuild.
				_terrainFlushRegion = _terrainFlushRegion.HasValue ? _terrainFlushRegion.Value.Merge(affected) : affected;
				_terrainGeometryDirty = true;
				if (result.HeightsModified) _terrainHeightsDirty = true;
				if (result.PathingModified) _terrainPathingDirty = true;
				anyModified = true;
			}
		}

		if (anyModified)
		{
			// Limit the number of full mesh/water rebuilds while dragging so painting stays smooth
			// on large maps, while the terrain data itself is updated every frame.
			long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
			if (isFirstClick || nowMs - _lastTerrainMeshRebuildMs >= TerrainMeshRebuildPeriodMs)
			{
				_lastTerrainMeshRebuildMs = nowMs;
				if (_terrainFlushRegion.HasValue)
				{
					var flushRegion = _terrainFlushRegion.Value;
					GroundTerrain.UpdateMeshAndPhysics(false, false, flushRegion, _terrainHeightsDirty); // false for physics rebuild during drag
					if (_terrainHeightsDirty)
					{
						AlignAllEntitiesToTerrain(flushRegion);
					}
					if (_terrainPathingDirty && PathingOverlayVisible)
					{
						RebuildPathingOverlay();
					}
					_terrainFlushRegion = null;
					_terrainGeometryDirty = false;
					_terrainHeightsDirty = false;
					_terrainPathingDirty = false;
				}
			}
			EditorHasUnsavedChanges = true;
		}
	}

	public void FlushTerrainMeshAndPhysics()
	{
		if (_terrainGeometryDirty && _terrainFlushRegion.HasValue && GroundTerrain != null)
		{
			var flushRegion = _terrainFlushRegion.Value;
			GroundTerrain.UpdateMeshAndPhysics(_terrainHeightsDirty, false, flushRegion, _terrainHeightsDirty);
			if (_terrainHeightsDirty)
			{
				AlignAllEntitiesToTerrain(flushRegion);
			}
			if (_terrainPathingDirty && PathingOverlayVisible)
			{
				RebuildPathingOverlay();
			}
			_terrainFlushRegion = null;
			_terrainGeometryDirty = false;
			_terrainHeightsDirty = false;
			_terrainPathingDirty = false;
			_lastTerrainMeshRebuildMs = long.MinValue;
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
			PropMultiMeshManager.Instance?.MarkDirty(pid);
		}
	}

	public Prop3D SpawnPropExternal(string propId, Vector3 position)
	{
		float rotY = IsMapEditorMode ? EditorPlacementRotation : 0f;
		float scale = IsMapEditorMode ? EditorPlacementScale : 1f;
		return SpawnPropExternalWithParams(propId, position, rotY, scale);
	}

	public class DecalAssetData
	{
		public string DecalId { get; set; } = "";
		public string TexturePath { get; set; } = "";
		public Texture2D PrimaryTexture { get; set; }
		public Texture2D? PrimaryNormal { get; set; }
		public Texture2D[]? AlbedoFrames { get; set; }
		public Texture2D[]? NormalFrames { get; set; }
		public int Columns { get; set; } = 1;
		public int Rows { get; set; } = 1;
		public float Fps { get; set; } = 12.0f;
		public bool SubframeBlend { get; set; } = true;
		public bool IsAnimated => Columns > 1 || Rows > 1;
	}

	private readonly System.Collections.Generic.Dictionary<string, DecalAssetData> _decalAssetCache = new();

	public void InvalidateDecalCache(string decalId)
	{
		if (string.IsNullOrEmpty(decalId)) return;
		string baseKey = System.IO.Path.GetFileNameWithoutExtension(decalId);
		_decalAssetCache.Remove(decalId);
		_decalAssetCache.Remove(baseKey);
		_decalAssetCache.Remove($"{baseKey}.rtex");
		_decalAssetCache.Remove($"{baseKey}.png");
	}

	public DecalAssetData LoadDecalAsset(
		string decalId,
		int overrideCols = 0,
		int overrideRows = 0,
		float overrideFps = 0f,
		bool? overrideSubframeBlend = null,
		bool forceReload = false)
	{
		if (string.IsNullOrEmpty(decalId)) decalId = "logo";
		string cacheKey = decalId;
		if (!forceReload && overrideCols <= 0 && overrideRows <= 0 && overrideFps <= 0.001f && !overrideSubframeBlend.HasValue)
		{
			if (_decalAssetCache.TryGetValue(cacheKey, out var cachedData) && cachedData != null && GodotObject.IsInstanceValid(cachedData.PrimaryTexture))
			{
				return cachedData;
			}
		}

		if (decalId.StartsWith("res://"))
		{
			try
			{
				if (ResourceLoader.Exists(decalId))
				{
					var resTex = GD.Load<Texture2D>(decalId);
					var resData = new DecalAssetData
					{
						DecalId = decalId,
						TexturePath = decalId,
						PrimaryTexture = resTex,
						Columns = 1,
						Rows = 1
					};
					_decalAssetCache[cacheKey] = resData;
					return resData;
				}
			}
			catch { }
		}

		string filename = System.IO.Path.GetFileName(decalId);
		string baseKey = System.IO.Path.GetFileNameWithoutExtension(decalId);
		string wsPath = ProjectSettings.GlobalizePath(MapEditorHUD.TempWorkspaceGodotPath);

		int detectedCols = 1;
		int detectedRows = 1;
		float detectedFps = 12.0f;
		bool detectedSubframeBlend = true;
		string? explicitTexturePath = null;

		try
		{
			var assetsObj = Realm.Godot.Utils.MapAssetHelper.LoadAssets(wsPath);
			var decalsObj = assetsObj?["decals"] as System.Text.Json.Nodes.JsonObject;
			if (decalsObj != null)
			{
				System.Text.Json.Nodes.JsonObject? meta = null;
				if (decalsObj.TryGetPropertyValue(decalId, out var n0) && n0 is System.Text.Json.Nodes.JsonObject o0) meta = o0;
				else if (decalsObj.TryGetPropertyValue(filename, out var n1) && n1 is System.Text.Json.Nodes.JsonObject o1) meta = o1;
				else if (decalsObj.TryGetPropertyValue(baseKey, out var n2) && n2 is System.Text.Json.Nodes.JsonObject o2) meta = o2;
				else if (decalsObj.TryGetPropertyValue($"{baseKey}.rtex", out var n3) && n3 is System.Text.Json.Nodes.JsonObject o3) meta = o3;
				else if (decalsObj.TryGetPropertyValue($"{baseKey}.png", out var n4) && n4 is System.Text.Json.Nodes.JsonObject o4) meta = o4;

				if (meta != null)
				{
					if (meta.TryGetPropertyValue("texture_path", out var tpNode) && !string.IsNullOrWhiteSpace(tpNode?.ToString()))
						explicitTexturePath = tpNode.ToString();
					else if (meta.TryGetPropertyValue("TexturePath", out var tpNode2) && !string.IsNullOrWhiteSpace(tpNode2?.ToString()))
						explicitTexturePath = tpNode2.ToString();

					if (meta.TryGetPropertyValue("columns", out var cNode) && int.TryParse(cNode?.ToString(), out int c) && c > 0) detectedCols = c;
					if (meta.TryGetPropertyValue("rows", out var rNode) && int.TryParse(rNode?.ToString(), out int r) && r > 0) detectedRows = r;
					if (meta.TryGetPropertyValue("fps", out var fNode) && float.TryParse(fNode?.ToString(), out float f) && f > 0.001f) detectedFps = f;
					else if (meta.TryGetPropertyValue("seconds_per_frame", out var sNode) && float.TryParse(sNode?.ToString(), out float spf) && spf > 0.001f) detectedFps = 1.0f / spf;
					if (meta.TryGetPropertyValue("subframe_blend", out var sbNode) && bool.TryParse(sbNode?.ToString(), out bool sb)) detectedSubframeBlend = sb;
				}
			}
		}
		catch { }

		List<string> candidatePaths = new List<string>();
		void AddTextureCandidates(string pathOrName)
		{
			if (string.IsNullOrWhiteSpace(pathOrName)) return;
			string fName = System.IO.Path.GetFileName(pathOrName);
			if (System.IO.Path.IsPathRooted(pathOrName))
			{
				candidatePaths.Add(pathOrName);
			}
			else
			{
				candidatePaths.Add(System.IO.Path.Combine(wsPath, "Assets", "decals", pathOrName));
				candidatePaths.Add(System.IO.Path.Combine(wsPath, "Assets", "decals", fName));
				candidatePaths.Add(System.IO.Path.Combine(wsPath, pathOrName));
				if (!fName.Contains('.'))
				{
					candidatePaths.Add(System.IO.Path.Combine(wsPath, "Assets", "decals", fName + ".rtex"));
					candidatePaths.Add(System.IO.Path.Combine(wsPath, "Assets", "decals", fName + ".webp"));
					candidatePaths.Add(System.IO.Path.Combine(wsPath, "Assets", "decals", fName + ".png"));
				}
			}
		}

		if (!string.IsNullOrEmpty(explicitTexturePath))
		{
			AddTextureCandidates(explicitTexturePath);
		}
		AddTextureCandidates(decalId);

		foreach (var path in candidatePaths)
		{
			if (!string.IsNullOrEmpty(path) && System.IO.File.Exists(path))
			{
				try
				{
					Image? albedoImg = null;
					Image? normalImg = null;

					if (path.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase))
					{
						byte[] rtexBytes = System.IO.File.ReadAllBytes(path);
						var (customJson, layers, _) = Realm.Shared.Textures.RtexFile.Parse(rtexBytes);
						if (!string.IsNullOrEmpty(customJson))
						{
							try
							{
								var rtexMeta = System.Text.Json.Nodes.JsonNode.Parse(customJson)?.AsObject();
								if (rtexMeta != null)
								{
									if (rtexMeta.TryGetPropertyValue("columns", out var cNode) && int.TryParse(cNode?.ToString(), out int c) && c > 0) detectedCols = c;
									if (rtexMeta.TryGetPropertyValue("rows", out var rNode) && int.TryParse(rNode?.ToString(), out int r) && r > 0) detectedRows = r;
								}
							}
							catch { }
						}

						if (layers.Count > 0 && layers[0] != null && layers[0].Length > 0)
						{
							albedoImg = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8);
							if (albedoImg.LoadWebpFromBuffer(layers[0]) != Error.Ok)
							{
								albedoImg.LoadPngFromBuffer(layers[0]);
							}
						}

						if (layers.Count > 1 && layers[1] != null && layers[1].Length > 0)
						{
							normalImg = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8);
							if (normalImg.LoadWebpFromBuffer(layers[1]) != Error.Ok)
							{
								normalImg.LoadPngFromBuffer(layers[1]);
							}
						}
					}
					else
					{
						albedoImg = Image.LoadFromFile(path);
						string? meta = Realm.Shared.Metadata.RealmMetadataHelper.ExtractMetadata(path);
						if (!string.IsNullOrEmpty(meta))
						{
							try
							{
								var node = System.Text.Json.Nodes.JsonNode.Parse(meta);
								if (node?["columns"] != null && int.TryParse(node["columns"]?.ToString(), out int c) && c > 0) detectedCols = c;
								if (node?["rows"] != null && int.TryParse(node["rows"]?.ToString(), out int r) && r > 0) detectedRows = r;
							}
							catch { }
						}
					}

					if (albedoImg != null)
					{
						var assetData = new DecalAssetData
						{
							DecalId = decalId,
							TexturePath = explicitTexturePath ?? decalId,
							Columns = 1,
							Rows = 1,
							Fps = 12.0f,
							SubframeBlend = false
						};

						if (!albedoImg.HasMipmaps()) albedoImg.GenerateMipmaps();
						assetData.PrimaryTexture = ImageTexture.CreateFromImage(albedoImg);
						if (normalImg != null)
						{
							if (!normalImg.HasMipmaps()) normalImg.GenerateMipmaps();
							assetData.PrimaryNormal = ImageTexture.CreateFromImage(normalImg);
						}

						_decalAssetCache[cacheKey] = assetData;
						return assetData;
					}
				}
				catch (Exception ex)
				{
					GD.PrintErr($"Failed to load decal image from '{path}': {ex.Message}");
				}
			}
		}

		var fallback = new DecalAssetData
		{
			DecalId = decalId,
			TexturePath = explicitTexturePath ?? decalId,
			PrimaryTexture = GD.Load<Texture2D>("res://icon.svg"),
			Columns = 1,
			Rows = 1
		};
		_decalAssetCache[cacheKey] = fallback;
		return fallback;
	}

	public Texture2D LoadDecalTexture(string decalId)
	{
		return LoadDecalAsset(decalId).PrimaryTexture;
	}

	public string GetDecalTexturePath(string decalId)
	{
		if (string.IsNullOrEmpty(decalId))
		{
			decalId = "logo";
		}
		if (decalId.StartsWith("res://")) return decalId;

		string wsPath = ProjectSettings.GlobalizePath(MapEditorHUD.TempWorkspaceGodotPath);
		string targetKey = decalId;

		try
		{
			var assetsObj = Realm.Godot.Utils.MapAssetHelper.LoadAssets(wsPath);
			var decalsObj = assetsObj?["decals"] as System.Text.Json.Nodes.JsonObject;
			if (decalsObj != null)
			{
				string filename = System.IO.Path.GetFileName(decalId);
				string baseKey = System.IO.Path.GetFileNameWithoutExtension(decalId);
				System.Text.Json.Nodes.JsonObject? meta = null;
				if (decalsObj.TryGetPropertyValue(decalId, out var n0) && n0 is System.Text.Json.Nodes.JsonObject o0) meta = o0;
				else if (decalsObj.TryGetPropertyValue(filename, out var n1) && n1 is System.Text.Json.Nodes.JsonObject o1) meta = o1;
				else if (decalsObj.TryGetPropertyValue(baseKey, out var n2) && n2 is System.Text.Json.Nodes.JsonObject o2) meta = o2;

				if (meta != null)
				{
					if (meta.TryGetPropertyValue("texture_path", out var tpNode) && !string.IsNullOrWhiteSpace(tpNode?.ToString()))
						targetKey = tpNode.ToString();
					else if (meta.TryGetPropertyValue("TexturePath", out var tpNode2) && !string.IsNullOrWhiteSpace(tpNode2?.ToString()))
						targetKey = tpNode2.ToString();
				}
			}
		}
		catch { }

		string targetFilename = System.IO.Path.GetFileName(targetKey);
		string candidate1 = System.IO.Path.Combine(wsPath, "Assets", "decals", targetFilename);
		if (System.IO.File.Exists(candidate1)) return candidate1;

		if (!targetFilename.Contains('.'))
		{
			string candidate2 = System.IO.Path.Combine(wsPath, "Assets", "decals", targetFilename + ".png");
			if (System.IO.File.Exists(candidate2)) return candidate2;
			string candidateRtex = System.IO.Path.Combine(wsPath, "Assets", "decals", targetFilename + ".rtex");
			if (System.IO.File.Exists(candidateRtex)) return candidateRtex;
		}

		if (System.IO.Path.IsPathRooted(targetKey) && System.IO.File.Exists(targetKey))
		{
			return targetKey;
		}

		string candidate3 = System.IO.Path.Combine(wsPath, targetKey);
		if (System.IO.File.Exists(candidate3)) return candidate3;

		return "res://icon.svg";
	}

	public Decal SpawnDecalExternal(Vector3 position)
	{
		var entity = EcsWorld.Create();
		var decal = new Decal3D();
		decal.Entity = entity;
		decal.DecalId = "logo";
		decal.TextureAlbedo = GD.Load<Texture2D>("res://icon.svg");
		decal.Size = new Vector3(6.0f, 20.0f, 6.0f);
		decal.AlbedoMix = 1.0f;
		decal.CullMask = RuntimeTerrain.TerrainDecalCullMask;
		AddChild(decal);
		AllDecals.Add(decal);
		
		position.Y = _editorService.GetTerrainHeightAt(position);
		decal.Position = position;
		
		EcsWorld.Add(entity, new Realm.Ecs.Components.Core.Position(new System.Numerics.Vector3(position.X, position.Y, position.Z)));
		EcsWorld.Add(entity, new RotationY(0.0f));
		EcsWorld.Add(entity, new ModelScale(1.0f));
		
		if (IsMapEditorMode)
		{
			decal.RotationDegrees = new Vector3(0.0f, EditorPlacementRotation, 0.0f);
			decal.Size = new Vector3(6.0f, 20.0f, 6.0f) * (EditorPlacementScale <= 0.001f ? 1.0f : EditorPlacementScale);
			decal.Scale = Vector3.One;
			EcsWorld.Set(entity, new RotationY(EditorPlacementRotation));
			EcsWorld.Set(entity, new ModelScale(EditorPlacementScale));
		}
		return decal;
	}

	public float GetTerrainHeightAt(Vector3 worldPos)
	{
		return _editorService.GetTerrainHeightAt(worldPos);
	}

	private void AlignAllEntitiesToTerrain(Rect2I? affectedRegion = null)
	{
		float quadSize = GroundTerrain != null ? GroundTerrain.QuadSize : EditableTerrain.DefaultQuadSize;
		float halfW = GroundTerrain != null ? GroundTerrain.Width / 2.0f * quadSize : 0f;
		float halfD = GroundTerrain != null ? GroundTerrain.Depth / 2.0f * quadSize : 0f;

		bool IsInRegion(Vector3 pos)
		{
			if (!affectedRegion.HasValue) return true;
			var region = affectedRegion.Value;
			float gridX = pos.X / quadSize + halfW / quadSize;
			float gridZ = pos.Z / quadSize + halfD / quadSize;
			int x = (int)Mathf.Round(gridX);
			int z = (int)Mathf.Round(gridZ);
			return x >= region.Position.X - 2 && x <= region.Position.X + region.Size.X + 2 &&
				   z >= region.Position.Y - 2 && z <= region.Position.Y + region.Size.Y + 2;
		}

		foreach (var unit in AllUnits)
		{
			if (GodotObject.IsInstanceValid(unit))
			{
				var pos = unit.GlobalPosition;
				if (!IsInRegion(pos)) continue;
				float targetY = _editorService.GetTerrainHeightAt(pos);
				if (MathF.Abs(pos.Y - targetY) > 0.001f)
				{
					pos.Y = targetY;
					unit.GlobalPosition = pos;
					if (EcsWorld.IsAlive(unit.Entity))
					{
						EcsWorld.Set(unit.Entity, new Realm.Ecs.Components.Core.Position(new System.Numerics.Vector3(pos.X, pos.Y, pos.Z)));
					}
				}
			}
		}

		bool anyPropMoved = false;
		foreach (var child in GetChildren())
		{
			if (child is Prop3D prop && GodotObject.IsInstanceValid(prop))
			{
				var pos = prop.GlobalPosition;
				if (!IsInRegion(pos)) continue;
				float targetY = _editorService.GetTerrainHeightAt(pos);
				if (MathF.Abs(pos.Y - targetY) > 0.001f)
				{
					pos.Y = targetY;
					prop.GlobalPosition = pos;
					anyPropMoved = true;
					PropMultiMeshManager.Instance?.MarkDirty(prop.PropId);
				}
			}
			else if (child is Decal decal && GodotObject.IsInstanceValid(decal))
			{
				var pos = decal.GlobalPosition;
				if (!IsInRegion(pos)) continue;
				float targetY = _editorService.GetTerrainHeightAt(pos);
				if (MathF.Abs(pos.Y - targetY) > 0.001f)
				{
					pos.Y = targetY;
					decal.GlobalPosition = pos;
				}
			}
		}

		if (!affectedRegion.HasValue && anyPropMoved)
		{
			PropMultiMeshManager.Instance?.MarkAllDirty();
		}
	}

	private void DeleteObjectAt(Node collider, Vector3 hitPos)
	{
		var unit = FindUnit3DInParentChain(collider);
		if (unit != null)
		{
			SelectedUnits.Remove(unit);
			AllUnits.Remove(unit);
			EntityToUnit3D.Remove(unit.Entity);
			if (EcsWorld.IsAlive(unit.Entity))
			{
				EcsWorld.Destroy(unit.Entity);
			}
			unit.QueueFree();
			return;
		}
		
		Node current = collider;
		while (current != null && current != this)
		{
			if (current is Prop3D prop)
			{
				AllProps.Remove(prop);
				EntityToProp3D.Remove(prop.Entity);
				if (EcsWorld.IsAlive(prop.Entity))
				{
					EcsWorld.Destroy(prop.Entity);
				}
				PropMultiMeshManager.Instance?.MarkDirty(prop.PropId);
				prop.QueueFree();
				return;
			}
			current = current.GetParent();
		}


		var decal = FindDecal3DInParentChain(collider);
		if (decal != null)
		{
			if (EcsWorld.IsAlive(decal.Entity))
			{
				EcsWorld.Destroy(decal.Entity);
			}
			decal.QueueFree();
		}
	}

		private Decal3D FindDecal3DInParentChain(Node node)
	{
		if (!IsMapEditorMode) return null;
		Node current = node;
		while (current != null && current != this)
		{
			if (current is Decal3D d) return d;
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
	
	public Unit3D SpawnUnitExternal(string unitId, Vector3 position, bool isEnemy, float rotationY, float scale, int player = -1)
	{
		// Preserve the authored Y (saved maps, pasted/cloned/undone objects). Placement paths
		// that want feet-on-terrain snap the Y to the terrain before calling this method.
		int playerIndex = player >= 0 ? player : 0;
		bool actualIsEnemy = player >= 0 ? NetworkService.ArePlayerIndicesEnemies(LocalPlayerIndex, playerIndex) : isEnemy;
		bool isBuilding = false;
		if (!UnitRegistry.ContainsKey(unitId) && !BuildingRegistry.ContainsKey(unitId))
		{
			LoadUnitMetadata(!string.IsNullOrEmpty(CurrentMapDirectory) ? CurrentMapDirectory : Godot.ProjectSettings.GlobalizePath(MapEditorHUD.TempWorkspaceGodotPath));
		}

		UnitMetadata meta;
		if (!UnitRegistry.TryGetValue(unitId, out meta))
		{
			if (BuildingRegistry.TryGetValue(unitId, out meta))
				isBuilding = true;
			else
				return null;
		}

		var playerOwner = GetPlayerEntityForPlayerIndex(playerIndex).AsPlayerEntity(EcsWorld);
		
		if (string.IsNullOrEmpty(meta.ModelPath))
		{
			return null;
		}

		string modelPath = GetFallbackModelPath(meta.ModelPath, isBuilding);

		string name = meta.Name;
		var entity = CreateEcsUnit(unitId, name, meta.MaxHp, meta.Damage, meta.Range, meta.Armor, meta.Speed, position, playerOwner);

		var unit3D = SpawnUnit3D(entity, unitId, modelPath, position, isBuilding, actualIsEnemy, false, playerIndex);
		unit3D.RotationDegrees = new Vector3(0.0f, rotationY, 0.0f);
		unit3D.Scale = Vector3.One * (scale <= 0.001f ? 1.0f : scale);

		EcsWorld.SetOrAdd(entity, new CollisionScale(scale));

		EcsWorld.SetOrAdd(entity, new RotationY(rotationY));

		EcsWorld.SetOrAdd(entity, new ModelScale(scale));

		return unit3D;
	}

	public Prop3D SpawnPropExternalWithParams(string propId, Vector3 position, float rotationY, float scale)
	{
		if (string.IsNullOrEmpty(propId)) return null;

		string cleanId = System.IO.Path.GetFileNameWithoutExtension(propId);
		bool inProp = PropRegistry.ContainsKey(propId) || (!string.IsNullOrEmpty(cleanId) && PropRegistry.ContainsKey(cleanId));
		bool inRes = ResourceRegistry.ContainsKey(propId) || (!string.IsNullOrEmpty(cleanId) && ResourceRegistry.ContainsKey(cleanId));

		if (!inProp && !inRes)
		{
			LoadUnitMetadata(!string.IsNullOrEmpty(CurrentMapDirectory) ? CurrentMapDirectory : Godot.ProjectSettings.GlobalizePath(MapEditorHUD.TempWorkspaceGodotPath));
			inProp = PropRegistry.ContainsKey(propId) || (!string.IsNullOrEmpty(cleanId) && PropRegistry.ContainsKey(cleanId));
			inRes = ResourceRegistry.ContainsKey(propId) || (!string.IsNullOrEmpty(cleanId) && ResourceRegistry.ContainsKey(cleanId));
		}

		if (!inProp && !inRes)
		{
			foreach (var kvp in PropRegistry)
			{
				if (string.Equals(kvp.Value.ModelPath, propId, StringComparison.OrdinalIgnoreCase) ||
					(!string.IsNullOrEmpty(cleanId) && string.Equals(System.IO.Path.GetFileNameWithoutExtension(kvp.Value.ModelPath), cleanId, StringComparison.OrdinalIgnoreCase)))
				{
					inProp = true;
					propId = kvp.Key;
					cleanId = System.IO.Path.GetFileNameWithoutExtension(propId);
					break;
				}
			}
			if (!inProp)
			{
				foreach (var kvp in ResourceRegistry)
				{
					if (string.Equals(kvp.Value.ModelPath, propId, StringComparison.OrdinalIgnoreCase) ||
						(!string.IsNullOrEmpty(cleanId) && string.Equals(System.IO.Path.GetFileNameWithoutExtension(kvp.Value.ModelPath), cleanId, StringComparison.OrdinalIgnoreCase)))
					{
						inRes = true;
						propId = kvp.Key;
						cleanId = System.IO.Path.GetFileNameWithoutExtension(propId);
						break;
					}
				}
			}
		}

		if (!inProp && !inRes)
		{
			return null;
		}

		float defaultAmount = propId switch
		{
			"goldmine" => 2000f,
			"rock" => 1000f,
			"tree" => 500f,
			_ => 0f
		};

		var entity = EcsWorld.Create();
		EcsWorld.Add(entity, new PropIdentity(propId));
		if ((ResourceRegistry.TryGetValue(propId, out var meta) || (!string.IsNullOrEmpty(cleanId) && ResourceRegistry.TryGetValue(cleanId, out meta))) && (meta.MaxCapacity > 0f || defaultAmount > 0f))
		{
			float amount = meta.MaxCapacity > 0f ? meta.MaxCapacity : defaultAmount;
			float harvestRate = meta.HarvestRate > 0f ? meta.HarvestRate : 10f;
			float growthRate = meta.GrowthRate;
			int maxWorkers = meta.MaxWorkers > 0 ? meta.MaxWorkers : 5;
			EcsWorld.Add(entity, new ResourceNode(Guid.Empty, amount, amount, harvestRate, growthRate, maxWorkers));
		}
		else if (defaultAmount > 0f)
		{
			EcsWorld.Add(entity, new ResourceNode(Guid.Empty, defaultAmount, defaultAmount, 10f, 0f, 5));
		}

		position.Y = _editorService.GetTerrainHeightAt(position);

		EcsWorld.Add(entity, new Realm.Ecs.Components.Tags.Prop());
		EcsWorld.Add(entity, new Realm.Ecs.Components.Core.Position(new System.Numerics.Vector3(position.X, position.Y, position.Z)));
		EcsWorld.Add(entity, new RotationY(rotationY));
		EcsWorld.Add(entity, new ModelScale(scale));
		EcsWorld.Add(entity, new CollisionScale(scale));

		string propAssetKey = NormalizeModelAssetKey(propId);
		float autoDetectedRadius = GetOrCalculateObstacleRadius(propId, null);
		float baseRadius = autoDetectedRadius * GetModelCollisionCircleRatio(propAssetKey);
		EcsWorld.Add(entity, new Realm.Ecs.Components.Core.CollisionRadius(baseRadius));

		if (!IsMapEditorMode && IsStaticPropAsset(propId))
		{
			PropMultiMeshManager.Instance?.MarkDirty(propId);
			return null;
		}

		var prop = new Prop3D();
		prop.Entity = entity;
		prop.PropId = propId;
		prop.Position = position;
		prop.RotationDegrees = new Vector3(0.0f, rotationY, 0.0f);
		prop.Scale = Vector3.One * (scale <= 0.001f ? 1.0f : scale);
		AddChild(prop);
		AllProps.Add(prop);
		PropMultiMeshManager.Instance?.MarkDirty(propId);

		EntityToProp3D[entity] = prop;

		return prop;
	}

	public Decal SpawnDecalExternalWithParams(string decalId, Vector3 position, Vector3 rotationDegrees, float scale)
	{
		var entity = EcsWorld.Create();
		var decal = new Decal3D();
		decal.Entity = entity;
		decal.DecalId = string.IsNullOrEmpty(decalId) ? "logo" : decalId;
		decal.TextureAlbedo = LoadDecalTexture(decalId);
		decal.Size = new Vector3(6.0f, 20.0f, 6.0f) * (scale <= 0.001f ? 1.0f : scale);
		decal.AlbedoMix = 1.0f;
		decal.CullMask = RuntimeTerrain.TerrainDecalCullMask;
		AddChild(decal);
		AllDecals.Add(decal);
		
		decal.Position = position;
		decal.RotationDegrees = rotationDegrees;
		decal.Scale = Vector3.One;
		
		EcsWorld.Add(entity, new Realm.Ecs.Components.Core.Position(new System.Numerics.Vector3(position.X, position.Y, position.Z)));
		EcsWorld.Add(entity, new Realm.Ecs.Components.Meta.Rotation3D(new System.Numerics.Vector3(rotationDegrees.X, rotationDegrees.Y, rotationDegrees.Z)));
		EcsWorld.Add(entity, new RotationY(rotationDegrees.Y));
		EcsWorld.Add(entity, new ModelScale(scale));

		ApplyDecalPropertiesFromMetadata(decal, decalId);
		
		return decal;
	}

	public Decal SpawnDecalExternalWithParams(string decalId, Vector3 position, float rotationY, float scale)
	{
		Vector3 pos = position;
		pos.Y = _editorService.GetTerrainHeightAt(pos);
		return SpawnDecalExternalWithParams(decalId, pos, new Vector3(0.0f, rotationY, 0.0f), scale);
	}

	public ProceduralVfxInstance3D SpawnVfxExternalWithParams(
		string vfxId,
		Vector3 position,
		Vector3 rotationDegrees,
		Vector3 scale,
		float normalOffset = 0f,
		VfxAttachmentConfig customConfig = null)
	{
		var entity = EcsWorld.Create();
		VfxAttachmentConfig config;
		if (customConfig != null)
		{
			config = customConfig.Clone();
		}
		else
		{
			string cleanId = vfxId ?? string.Empty;
			if (cleanId.StartsWith("vfx:", StringComparison.OrdinalIgnoreCase))
			{
				cleanId = cleanId.Substring(4);
			}

			if (!string.IsNullOrEmpty(vfxId) && VfxRegistry.TryGetValue(vfxId, out var regCfg))
			{
				config = regCfg.Clone();
			}
			else if (!string.IsNullOrEmpty(cleanId) && VfxRegistry.TryGetValue(cleanId, out var regCfg2))
			{
				config = regCfg2.Clone();
			}
			else if (!string.IsNullOrEmpty(cleanId) && Enum.TryParse<VfxPrimitiveType>(cleanId, true, out var primType))
			{
				config = new VfxAttachmentConfig { VfxId = cleanId, PrimitiveType = primType };
				if (primType == VfxPrimitiveType.ParticleSystem)
				{
					config.ParticleConfig = SpellParticleConfig.CreatePreset(cleanId);
				}
			}
			else if (!string.IsNullOrEmpty(vfxId) && Enum.TryParse<VfxPrimitiveType>(vfxId, true, out var primType2))
			{
				config = new VfxAttachmentConfig { VfxId = vfxId, PrimitiveType = primType2 };
				if (primType2 == VfxPrimitiveType.ParticleSystem)
				{
					config.ParticleConfig = SpellParticleConfig.CreatePreset(vfxId);
				}
			}
			else
			{
				var particlePresets = SpellParticleConfig.GetAllPresets();
				if (particlePresets.ContainsKey(cleanId) || cleanId.StartsWith("particle_", StringComparison.OrdinalIgnoreCase))
				{
					config = new VfxAttachmentConfig
					{
						VfxId = cleanId,
						Name = cleanId,
						PrimitiveType = VfxPrimitiveType.ParticleSystem,
						ParticleConfig = SpellParticleConfig.CreatePreset(cleanId)
					};
				}
				else
				{
					config = new VfxAttachmentConfig
					{
						VfxId = vfxId,
						Name = cleanId,
						BaseTexture = cleanId,
						PrimitiveType = VfxPrimitiveType.VortexDisc
					};
				}
			}
		}

		if (normalOffset != 0f)
		{
			config.SurfaceNormalOffset = normalOffset;
		}

		Vector3 safeScale = scale;
		if (Mathf.IsZeroApprox(safeScale.X)) safeScale.X = 1f;
		if (Mathf.IsZeroApprox(safeScale.Y)) safeScale.Y = 1f;
		if (Mathf.IsZeroApprox(safeScale.Z)) safeScale.Z = 1f;

		var vfx = new ProceduralVfxInstance3D(config);
		vfx.Entity = entity;
		vfx.Position = position;
		vfx.RotationDegrees = rotationDegrees;
		vfx.Scale = safeScale;

		AddChild(vfx);
		AllVfx.Add(vfx);
		EntityToVfx3D[entity] = vfx;

		EcsWorld.Add(entity, new Realm.Ecs.Components.Core.Position(new System.Numerics.Vector3(position.X, position.Y, position.Z)));
		EcsWorld.Add(entity, new RotationY(rotationDegrees.Y));
		EcsWorld.Add(entity, new ModelScale(safeScale.X));

		return vfx;
	}

	public static Basis CreateAlignedBasis(Vector3 normal)
	{
		Vector3 up = normal.Normalized();
		Vector3 tangent = (Mathf.Abs(up.Dot(Vector3.Up)) > 0.9f) ? Vector3.Right : Vector3.Up;
		Vector3 right = tangent.Cross(up).Normalized();
		Vector3 forward = up.Cross(right).Normalized();
		return new Basis(right, up, forward);
	}

	public Vector3 GetTerrainNormalAt(Vector3 pos)
	{
		float step = 0.5f;
		float hL = _editorService.GetTerrainHeightAt(new Vector3(pos.X - step, 0f, pos.Z));
		float hR = _editorService.GetTerrainHeightAt(new Vector3(pos.X + step, 0f, pos.Z));
		float hD = _editorService.GetTerrainHeightAt(new Vector3(pos.X, 0f, pos.Z - step));
		float hU = _editorService.GetTerrainHeightAt(new Vector3(pos.X, 0f, pos.Z + step));
		return new Vector3(hL - hR, 2f * step, hD - hU).Normalized();
	}

	private readonly System.Collections.Generic.Dictionary<(int, int), ImageTexture> _decalOrmCache = new();
	private readonly System.Collections.Generic.Dictionary<string, ImageTexture> _decalNormalCache = new();

	public ImageTexture GetOrCreateOrmTexture(float roughness, float metallic)
	{
		int rKey = Mathf.RoundToInt(Mathf.Clamp(roughness, 0f, 1f) * 100f);
		int mKey = Mathf.RoundToInt(Mathf.Clamp(metallic, 0f, 1f) * 100f);
		if (_decalOrmCache.TryGetValue((rKey, mKey), out var cached) && GodotObject.IsInstanceValid(cached))
		{
			return cached;
		}

		var img = Image.CreateEmpty(4, 4, false, Image.Format.Rgba8);
		img.Fill(new Color(1.0f, rKey / 100f, mKey / 100f, 1.0f));
		var tex = ImageTexture.CreateFromImage(img);
		_decalOrmCache[(rKey, mKey)] = tex;
		return tex;
	}

	public ImageTexture GetOrCreateNormalTexture(Texture2D albedoTex, float normalStrength, string decalKey)
	{
		if (albedoTex == null || normalStrength <= 0.01f) return null;
		int sKey = Mathf.RoundToInt(Mathf.Clamp(normalStrength, 0.05f, 2.0f) * 20f);
		string cacheKey = $"{decalKey}_{albedoTex.GetInstanceId()}_{sKey}";
		if (_decalNormalCache.TryGetValue(cacheKey, out var cached) && GodotObject.IsInstanceValid(cached))
		{
			return cached;
		}

		var albedoImg = albedoTex.GetImage();
		if (albedoImg == null) return null;

		int w = Math.Min(albedoImg.GetWidth(), 512);
		int h = Math.Min(albedoImg.GetHeight(), 512);
		var workImg = albedoImg.Duplicate() as Image;
		if (workImg == null) return null;
		if (workImg.GetWidth() != w || workImg.GetHeight() != h)
		{
			workImg.Resize(w, h, Image.Interpolation.Bilinear);
		}
		workImg.Convert(Image.Format.Rgba8);

		var normImg = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
		float scale = (sKey / 20f) * 4.0f;

		for (int y = 0; y < h; y++)
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

		var normTex = ImageTexture.CreateFromImage(normImg);
		_decalNormalCache[cacheKey] = normTex;
		return normTex;
	}

	public void ApplyDecalRenderingProperties(
		Decal decal,
		float brightness,
		Color tint,
		float contrast,
		float saturation,
		float opacity,
		float albedoMix,
		float normalStrength,
		float roughness,
		float metallic,
		string blendMode,
		string decalKey = "")
	{
		if (decal == null || !GodotObject.IsInstanceValid(decal)) return;

		decal.CullMask = RuntimeTerrain.TerrainDecalCullMask;

		// 1. Color Modulation with Brightness, Tint, Contrast, and Saturation
		float r = tint.R * brightness;
		float g = tint.G * brightness;
		float b = tint.B * brightness;

		float lum = 0.2126f * r + 0.7152f * g + 0.0722f * b;
		r = lum + (r - lum) * saturation;
		g = lum + (g - lum) * saturation;
		b = lum + (b - lum) * saturation;

		r = (r - 0.5f) * contrast + 0.5f;
		g = (g - 0.5f) * contrast + 0.5f;
		b = (b - 0.5f) * contrast + 0.5f;

		decal.Modulate = new Color(Mathf.Clamp(r, 0f, 4f), Mathf.Clamp(g, 0f, 4f), Mathf.Clamp(b, 0f, 4f), Mathf.Clamp(opacity, 0f, 1f));

		// 2. ORM (Roughness and Metallic)
		if (roughness >= 0.99f && metallic <= 0.01f)
		{
			decal.TextureOrm = null;
		}
		else
		{
			decal.TextureOrm = GetOrCreateOrmTexture(roughness, metallic);
		}

		// 3. Normal Depth
		if (normalStrength > 0.01f)
		{
			if (decal is Decal3D d3d) d3d.NormalEnabled = true;
			if (decal.TextureNormal == null && decal.TextureAlbedo != null)
			{
				decal.TextureNormal = GetOrCreateNormalTexture(decal.TextureAlbedo, normalStrength, decalKey);
			}
		}
		else
		{
			if (decal is Decal3D d3d) d3d.NormalEnabled = false;
			decal.TextureNormal = null;
		}

		// 4. Blend Mode & Albedo Mix
		switch (blendMode?.ToLowerInvariant())
		{
			case "additive":
				decal.AlbedoMix = 0.0f;
				decal.TextureEmission = decal.TextureAlbedo;
				decal.EmissionEnergy = brightness * albedoMix * 2.0f;
				break;
			case "screen":
				decal.AlbedoMix = albedoMix * 0.4f;
				decal.TextureEmission = decal.TextureAlbedo;
				decal.EmissionEnergy = brightness * albedoMix * 1.5f;
				break;
			case "multiply":
				decal.AlbedoMix = albedoMix;
				decal.TextureEmission = null;
				decal.EmissionEnergy = 0.0f;
				break;
			case "mix":
			default:
				decal.AlbedoMix = albedoMix;
				decal.TextureEmission = null;
				decal.EmissionEnergy = 0.0f;
				break;
		}

		if (decal is Decal3D decal3DNode)
		{
			decal3DNode.SetBaseProperties(decal.Modulate, decal.EmissionEnergy);
		}
	}

	public void ApplyDecalPropertiesFromMetadata(Decal decal, string decalId)
	{
		if (decal == null || !GodotObject.IsInstanceValid(decal) || string.IsNullOrEmpty(decalId)) return;
		try
		{
			var assetData = LoadDecalAsset(decalId);
			decal.TextureAlbedo = assetData.PrimaryTexture;
			if (assetData.PrimaryNormal != null) decal.TextureNormal = assetData.PrimaryNormal;

			string wsPath = MapWorkspaceService.GetActiveWorkspacePath();
			var assetsObj = Realm.Godot.Utils.MapAssetHelper.LoadAssets(wsPath);
			var decalsObj = assetsObj?["decals"] as System.Text.Json.Nodes.JsonObject;

			string key = System.IO.Path.GetFileName(decalId);
			string baseKey = System.IO.Path.GetFileNameWithoutExtension(decalId);

			System.Text.Json.Nodes.JsonObject? meta = null;
			if (decalsObj != null)
			{
				if (decalsObj.TryGetPropertyValue(decalId, out var n0) && n0 is System.Text.Json.Nodes.JsonObject o0) meta = o0;
				else if (decalsObj.TryGetPropertyValue(key, out var n1) && n1 is System.Text.Json.Nodes.JsonObject o1) meta = o1;
				else if (decalsObj.TryGetPropertyValue(baseKey, out var n2) && n2 is System.Text.Json.Nodes.JsonObject o2) meta = o2;
				else if (decalsObj.TryGetPropertyValue($"{baseKey}.rtex", out var n3) && n3 is System.Text.Json.Nodes.JsonObject o3) meta = o3;
				else if (decalsObj.TryGetPropertyValue($"{baseKey}.png", out var n4) && n4 is System.Text.Json.Nodes.JsonObject o4) meta = o4;
			}

			float brightness = meta != null && meta.TryGetPropertyValue("brightness", out var bNode) && float.TryParse(bNode?.ToString(), out float b) ? b : 1.0f;
			Color tint = Colors.White;
			if (meta != null && meta.TryGetPropertyValue("tint", out var tNode) && tNode != null)
			{
				string tStr = tNode.ToString();
				if (tStr.StartsWith("#")) tint = Color.FromHtml(tStr);
			}
			float contrast = meta != null && meta.TryGetPropertyValue("contrast", out var cNode) && float.TryParse(cNode?.ToString(), out float c) ? c : 1.0f;
			float saturation = meta != null && meta.TryGetPropertyValue("saturation", out var sNode) && float.TryParse(sNode?.ToString(), out float s) ? s : 1.0f;
			float opacity = meta != null && meta.TryGetPropertyValue("opacity", out var oNode) && float.TryParse(oNode?.ToString(), out float o) ? o : 1.0f;
			float albedoMix = meta != null && meta.TryGetPropertyValue("albedo_mix", out var mNode) && float.TryParse(mNode?.ToString(), out float m) ? m : 1.0f;
			float normalStrength = meta != null && meta.TryGetPropertyValue("normal_strength", out var nNode) && float.TryParse(nNode?.ToString(), out float n) ? n : (assetData.PrimaryNormal != null ? 1.0f : 0.0f);
			float roughness = meta != null && meta.TryGetPropertyValue("roughness", out var rNode) && float.TryParse(rNode?.ToString(), out float r) ? r : 1.0f;
			float metallic = meta != null && meta.TryGetPropertyValue("metallic", out var metNode) && float.TryParse(metNode?.ToString(), out float met) ? met : 0.0f;
			string blendMode = meta != null && meta.TryGetPropertyValue("blend_mode", out var bmNode) ? bmNode?.ToString() ?? "Mix" : "Mix";

			bool animateOpacity = meta != null && meta.TryGetPropertyValue("animate_opacity", out var aoNode) && bool.TryParse(aoNode?.ToString(), out bool ao) && ao;
			float opacityPulseSpeed = meta != null && meta.TryGetPropertyValue("opacity_pulse_speed", out var opsNode) && float.TryParse(opsNode?.ToString(), out float ops) ? ops : 1.0f;
			float minOpacity = meta != null && meta.TryGetPropertyValue("min_opacity", out var minONode) && float.TryParse(minONode?.ToString(), out float minO) ? minO : 0.2f;
			float maxOpacity = meta != null && meta.TryGetPropertyValue("max_opacity", out var maxONode) && float.TryParse(maxONode?.ToString(), out float maxO) ? maxO : 1.0f;

			bool animateEmission = meta != null && meta.TryGetPropertyValue("animate_emission", out var aeNode) && bool.TryParse(aeNode?.ToString(), out bool ae) && ae;
			float emissionPulseSpeed = meta != null && meta.TryGetPropertyValue("emission_pulse_speed", out var epsNode) && float.TryParse(epsNode?.ToString(), out float eps) ? eps : 1.0f;
			float minEmission = meta != null && meta.TryGetPropertyValue("min_emission", out var minENode) && float.TryParse(minENode?.ToString(), out float minE) ? minE : 0.0f;
			float maxEmission = meta != null && meta.TryGetPropertyValue("max_emission", out var maxENode) && float.TryParse(maxENode?.ToString(), out float maxE) ? maxE : 2.0f;

			bool animateScale = meta != null && meta.TryGetPropertyValue("animate_scale", out var asNode) && bool.TryParse(asNode?.ToString(), out bool aSc) && aSc;
			float scalePulseSpeed = meta != null && meta.TryGetPropertyValue("scale_pulse_speed", out var scpsNode) && float.TryParse(scpsNode?.ToString(), out float scps) ? scps : 1.0f;
			float minScaleRatio = meta != null && meta.TryGetPropertyValue("min_scale_ratio", out var minScNode) && float.TryParse(minScNode?.ToString(), out float minSc) ? minSc : 0.8f;
			float maxScaleRatio = meta != null && meta.TryGetPropertyValue("max_scale_ratio", out var maxScNode) && float.TryParse(maxScNode?.ToString(), out float maxSc) ? maxSc : 1.2f;

			float upperFade = meta != null && meta.TryGetPropertyValue("upper_fade", out var ufNode) && float.TryParse(ufNode?.ToString(), out float uf) ? uf : 0.3f;
			float lowerFade = meta != null && meta.TryGetPropertyValue("lower_fade", out var lfNode) && float.TryParse(lfNode?.ToString(), out float lf) ? lf : 0.3f;

			ApplyDecalRenderingProperties(
				decal,
				brightness,
				tint,
				contrast,
				saturation,
				opacity,
				albedoMix,
				normalStrength,
				roughness,
				metallic,
				blendMode,
				decalId
			);

			if (decal is Decal3D d3d)
			{
				d3d.SetPropertyAnimation(
					animateOpacity, opacityPulseSpeed, minOpacity, maxOpacity,
					animateEmission, emissionPulseSpeed, minEmission, maxEmission,
					animateScale, scalePulseSpeed, minScaleRatio, maxScaleRatio,
					upperFade, lowerFade
				);
			}
			else
			{
				decal.UpperFade = upperFade;
				decal.LowerFade = lowerFade;
			}
		}
		catch { }
	}

	public void RefreshDecalsLive(
		string decalKey,
		float brightness,
		Color tint,
		float contrast,
		float saturation,
		float opacity,
		float albedoMix,
		float normalStrength,
		float roughness,
		float metallic,
		string blendMode,
		bool animateOpacity = false,
		float opacityPulseSpeed = 1.0f,
		float minOpacity = 0.2f,
		float maxOpacity = 1.0f,
		bool animateEmission = false,
		float emissionPulseSpeed = 1.0f,
		float minEmission = 0.0f,
		float maxEmission = 2.0f,
		bool animateScale = false,
		float scalePulseSpeed = 1.0f,
		float minScaleRatio = 0.8f,
		float maxScaleRatio = 1.2f,
		float upperFade = 0.3f,
		float lowerFade = 0.3f)
	{
		if (string.IsNullOrEmpty(decalKey) || AllDecals == null) return;
		string baseKey = System.IO.Path.GetFileNameWithoutExtension(decalKey);

		foreach (var decal in AllDecals)
		{
			if (decal != null && GodotObject.IsInstanceValid(decal))
			{
				string dId = decal is Decal3D d3d ? d3d.DecalId : "";
				string dBase = System.IO.Path.GetFileNameWithoutExtension(dId);
				if (dId.Equals(decalKey, StringComparison.OrdinalIgnoreCase) || dBase.Equals(baseKey, StringComparison.OrdinalIgnoreCase))
				{
					ApplyDecalRenderingProperties(
						decal,
						brightness,
						tint,
						contrast,
						saturation,
						opacity,
						albedoMix,
						normalStrength,
						roughness,
						metallic,
						blendMode,
						decalKey
					);

					if (decal is Decal3D targetD3d)
					{
						targetD3d.SetPropertyAnimation(
							animateOpacity, opacityPulseSpeed, minOpacity, maxOpacity,
							animateEmission, emissionPulseSpeed, minEmission, maxEmission,
							animateScale, scalePulseSpeed, minScaleRatio, maxScaleRatio,
							upperFade, lowerFade
						);
					}
					else
					{
						decal.UpperFade = upperFade;
						decal.LowerFade = lowerFade;
					}
				}
			}
		}
	}

	public void DeleteNodeExternal(Node node)
	{
		if (node == null || !GodotObject.IsInstanceValid(node))
		{
			MapEditorHUD.Instance?.ShowFeedbackExternal("[Debug] DeleteNodeExternal: node is NULL or invalid");
			return;
		}

		var unit = (node as Unit3D) ?? FindUnit3DInParentChain(node);
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
			return;
		}
		var prop = (node as Prop3D) ?? FindProp3DInParentChain(node);
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
			PropMultiMeshManager.Instance?.MarkDirty(propId);
			return;
		}
		var decal = (node as Decal) ?? FindDecalInParentChain(node);
		if (decal != null && GodotObject.IsInstanceValid(decal))
		{
			if (decal == _selectedEditorObject || FindDecalInParentChain(_selectedEditorObject) == decal)
			{
				SelectedEditorObject = null;
			}
			AllDecals.Remove(decal);
			if (decal is Decal3D decal3D && EcsWorld.IsAlive(decal3D.Entity))
			{
				EcsWorld.Destroy(decal3D.Entity);
			}
			decal.QueueFree();
			return;
		}
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
			return;
		}

		if (_selectedEditorObject == node)
		{
			SelectedEditorObject = null;
		}
		node.QueueFree();
	}

	public IEditorAction DeleteObjectAtWithUndo(Node collider, Vector3 hitPos)
	{
		if (collider == null || !GodotObject.IsInstanceValid(collider))
		{
			MapEditorHUD.Instance?.ShowFeedbackExternal("[Debug] DeleteObjectAtWithUndo: collider is NULL or invalid");
			return null;
		}

		// 1. Direct Parent Chain Check
		var unit = (collider as Unit3D) ?? FindUnit3DInParentChain(collider);
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

		var prop = (collider as Prop3D) ?? FindProp3DInParentChain(collider);
		if (prop != null && GodotObject.IsInstanceValid(prop))
		{
			if (prop == _selectedEditorObject || FindProp3DInParentChain(_selectedEditorObject) == prop) SelectedEditorObject = null;
			string propId = prop.PropId;
			var action = new ObjectDeleteAction("prop", propId, prop.Position, prop.RotationDegrees.Y, prop.Scale.X, false, prop);
			AllProps.Remove(prop);
			EntityToProp3D.Remove(prop.Entity);
			if (EcsWorld.IsAlive(prop.Entity)) EcsWorld.Destroy(prop.Entity);
			prop.QueueFree();
			PropMultiMeshManager.Instance?.MarkDirty(propId);
			return action;
		}

		var decal = (collider as Decal) ?? FindDecalInParentChain(collider);
		if (decal != null && GodotObject.IsInstanceValid(decal))
		{
			if (decal == _selectedEditorObject || FindDecalInParentChain(_selectedEditorObject) == decal) SelectedEditorObject = null;
			string decalId = decal is Decal3D d3d ? d3d.DecalId : "logo";
			var action = new ObjectDeleteAction("decal", decalId, decal.Position, decal.RotationDegrees.Y, decal.Scale.X, false, decal);
			AllDecals.Remove(decal);
			if (decal is Decal3D d3 && EcsWorld.IsAlive(d3.Entity)) EcsWorld.Destroy(d3.Entity);
			decal.QueueFree();
			return action;
		}

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

		// 2. Proximity Search (if clicking terrain ground near object)
		Unit3D closestUnit = null;
		float closestUnitDist = 2.0f;
		foreach (var u in AllUnits)
		{
			if (GodotObject.IsInstanceValid(u))
			{
				float d = u.Position.DistanceTo(hitPos);
				if (d < closestUnitDist)
				{
					closestUnitDist = d;
					closestUnit = u;
				}
			}
		}

		Prop3D closestProp = null;
		float closestPropDist = 2.0f;
		foreach (var p in AllProps)
		{
			if (GodotObject.IsInstanceValid(p))
			{
				float d = p.Position.DistanceTo(hitPos);
				if (d < closestPropDist)
				{
					closestPropDist = d;
					closestProp = p;
				}
			}
		}

		Entity closestStaticPropEntity = Entity.Null;
		string closestStaticPropId = null;
		Vector3 closestStaticPropPos = Vector3.Zero;
		float closestStaticPropRotY = 0f;
		float closestStaticPropScale = 1f;
		float closestStaticPropDist = 2.0f;

		if (EcsWorld != null)
		{
			var propQuery = Realm.Ecs.Common.QueryCache.AllPropIdentityAndPositionQuery;
			EcsWorld.Query(in propQuery, (Entity entity, ref PropIdentity pId, ref Position pPos) =>
			{
				if (EntityToProp3D.ContainsKey(entity)) return;
				Vector3 wPos = new Vector3(pPos.Value.X, pPos.Value.Y, pPos.Value.Z);
				float d = wPos.DistanceTo(hitPos);
				if (d < closestStaticPropDist)
				{
					closestStaticPropDist = d;
					closestStaticPropEntity = entity;
					closestStaticPropId = pId.PropId;
					closestStaticPropPos = wPos;
					closestStaticPropRotY = EcsWorld.Has<RotationY>(entity) ? EcsWorld.Get<RotationY>(entity).Value : 0f;
					closestStaticPropScale = EcsWorld.Has<ModelScale>(entity) ? EcsWorld.Get<ModelScale>(entity).Value : 1f;
				}
			});
		}

		Decal closestDecal = null;
		float closestDecalDist = 2.0f;
		foreach (var decalObj in AllDecals)
		{
			if (GodotObject.IsInstanceValid(decalObj))
			{
				float d = decalObj.GlobalPosition.DistanceTo(hitPos);
				if (d < closestDecalDist)
				{
					closestDecalDist = d;
					closestDecal = decalObj;
				}
			}
		}

		ProceduralVfxInstance3D closestVfx = null;
		float closestVfxDist = 2.0f;
		foreach (var vfxObj in AllVfx)
		{
			if (GodotObject.IsInstanceValid(vfxObj))
			{
				float d = vfxObj.GlobalPosition.DistanceTo(hitPos);
				if (d < closestVfxDist)
				{
					closestVfxDist = d;
					closestVfx = vfxObj;
				}
			}
		}

		float minDistance = Mathf.Min(closestUnitDist, Mathf.Min(closestPropDist, Mathf.Min(closestStaticPropDist, Mathf.Min(closestDecalDist, closestVfxDist))));
		if (minDistance < 2.0f)
		{
			if (closestUnit != null && minDistance == closestUnitDist)
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
			else if (closestProp != null && minDistance == closestPropDist)
			{
				if (closestProp == _selectedEditorObject) SelectedEditorObject = null;
				string propId = closestProp.PropId;
				var action = new ObjectDeleteAction("prop", propId, closestProp.Position, closestProp.RotationDegrees.Y, closestProp.Scale.X, false, closestProp);
				AllProps.Remove(closestProp);
				EntityToProp3D.Remove(closestProp.Entity);
				if (EcsWorld.IsAlive(closestProp.Entity)) EcsWorld.Destroy(closestProp.Entity);
				closestProp.QueueFree();
				PropMultiMeshManager.Instance?.MarkDirty(propId);
				return action;
			}
			else if (closestStaticPropEntity != Entity.Null && minDistance == closestStaticPropDist)
			{
				var action = new ObjectDeleteAction("prop", closestStaticPropId, closestStaticPropPos, closestStaticPropRotY, closestStaticPropScale, false, null);
				EcsWorld.Destroy(closestStaticPropEntity);
				PropMultiMeshManager.Instance?.MarkDirty(closestStaticPropId);
				return action;
			}
			else if (closestDecal != null && minDistance == closestDecalDist)
			{
				if (closestDecal == _selectedEditorObject) SelectedEditorObject = null;
				string decalId = closestDecal is Decal3D d3d ? d3d.DecalId : "logo";
				var action = new ObjectDeleteAction("decal", decalId, closestDecal.Position, closestDecal.RotationDegrees.Y, closestDecal.Scale.X, false, closestDecal);
				AllDecals.Remove(closestDecal);
				if (closestDecal is Decal3D d3 && EcsWorld.IsAlive(d3.Entity)) EcsWorld.Destroy(d3.Entity);
				closestDecal.QueueFree();
				return action;
			}
			else if (closestVfx != null && minDistance == closestVfxDist)
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
		}

		MapEditorHUD.Instance?.ShowFeedbackExternal("[Debug] DeleteObjectAtWithUndo returned NULL (no direct or proximity match)");
		return null;
	}

	public void AlignAllEntitiesToTerrainExternal(Rect2I? affectedRegion = null)
	{
		AlignAllEntitiesToTerrain(affectedRegion);
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

		if (_editorPreviewNode == null || !GodotObject.IsInstanceValid(_editorPreviewNode) || _editorPreviewType != reqType || _editorPreviewId != reqId || _editorPreviewIsEnemy != reqIsEnemy)
		{
			ClearEditorPreview();
			
			_editorPreviewType = reqType;
			_editorPreviewId = reqId;
			_editorPreviewIsEnemy = reqIsEnemy;

			if (ActiveEditorTool == EditorTool.PlaceUnit)
			{
				if (!UnitRegistry.ContainsKey(reqId) && !BuildingRegistry.ContainsKey(reqId))
				{
					LoadUnitMetadata(!string.IsNullOrEmpty(CurrentMapDirectory) ? CurrentMapDirectory : Godot.ProjectSettings.GlobalizePath(MapEditorHUD.TempWorkspaceGodotPath));
				}

				bool isBuilding = false;
				UnitMetadata meta;
				if (!UnitRegistry.TryGetValue(reqId, out meta) && BuildingRegistry.TryGetValue(reqId, out meta))
					isBuilding = true;

				if (meta.TemplateID != null)
				{
					string targetModel = !string.IsNullOrEmpty(meta.ModelPath) ? meta.ModelPath : reqId;
					string modelPath = GetFallbackModelPath(targetModel, isBuilding);

					var previewUnit = new Unit3D();
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
			else if (ActiveEditorTool == EditorTool.PlaceProp)
			{
				string cleanReqId = System.IO.Path.GetFileNameWithoutExtension(reqId);
				bool hasReq = PropRegistry.ContainsKey(reqId) || ResourceRegistry.ContainsKey(reqId) ||
					(!string.IsNullOrEmpty(cleanReqId) && (PropRegistry.ContainsKey(cleanReqId) || ResourceRegistry.ContainsKey(cleanReqId)));

				if (!hasReq)
				{
					LoadUnitMetadata(!string.IsNullOrEmpty(CurrentMapDirectory) ? CurrentMapDirectory : Godot.ProjectSettings.GlobalizePath(MapEditorHUD.TempWorkspaceGodotPath));
					hasReq = PropRegistry.ContainsKey(reqId) || ResourceRegistry.ContainsKey(reqId) ||
						(!string.IsNullOrEmpty(cleanReqId) && (PropRegistry.ContainsKey(cleanReqId) || ResourceRegistry.ContainsKey(cleanReqId)));
				}

				if (hasReq)
				{
					var previewProp = new Prop3D();
					previewProp.PropId = reqId;
					previewProp.IsPreview = true;
					AddChild(previewProp);

					Color color = new Color(0.95f, 0.82f, 0.15f);
					MakeHologramRecursive(previewProp, color);
					_editorPreviewNode = previewProp;
				}
			}
			else if (ActiveEditorTool == EditorTool.PlaceDecal)
			{
				var previewDecal = new Decal3D();
				previewDecal.CullMask = RuntimeTerrain.TerrainDecalCullMask;
				previewDecal.DecalId = string.IsNullOrEmpty(reqId) ? "logo" : reqId;
				AddChild(previewDecal);
				ApplyDecalPropertiesFromMetadata(previewDecal, reqId);
				previewDecal.Size = new Vector3(6.0f, 20.0f, 6.0f) * EditorPlacementScale;
				previewDecal.AlbedoMix = 0.5f;
				_editorPreviewNode = previewDecal;
			}
			else if (ActiveEditorTool == EditorTool.PlaceVfx)
			{
				VfxAttachmentConfig config;
				if (VfxRegistry.TryGetValue(reqId, out var regCfg))
				{
					config = regCfg.Clone();
				}
				else if (Enum.TryParse<VfxPrimitiveType>(reqId, true, out var primType))
				{
					config = new VfxAttachmentConfig { VfxId = reqId, PrimitiveType = primType };
				}
				else
				{
					config = new VfxAttachmentConfig { VfxId = reqId, Name = reqId };
				}

				var previewVfx = new ProceduralVfxInstance3D(config);
				AddChild(previewVfx);
				_editorPreviewNode = previewVfx;
			}
		}

		if (_editorPreviewNode != null && GodotObject.IsInstanceValid(_editorPreviewNode))
		{
			ApplyAllGlobalOverridesToObject(_editorPreviewNode);

			if (EditorClumpMode || ActiveEditorTool == EditorTool.PlacePropClump)
			{
				_editorPreviewNode.Visible = false;
				return;
			}

			if (!_editorService.HasCachedRandom) _editorService.GenerateNewRandomPlacementRotationAndScale();
			float previewRot = (EditorRandomRotation && !_editorService.IsPastingObject) ? _editorService.CachedRandomRotation : EditorPlacementRotation;
			float previewScaleVal = (EditorRandomScale && !_editorService.IsPastingObject) ? _editorService.CachedRandomScale : EditorPlacementScale;

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

			float safePreviewScale = previewScaleVal <= 0.001f ? 1.0f : previewScaleVal;
			if (_editorPreviewNode is ProceduralVfxInstance3D previewVfxNode)
			{
				float normalOffset = previewVfxNode.Config?.SurfaceNormalOffset ?? 0.02f;
				if (previewVfxNode.Config?.PlacementMode == VfxPlacementMode.SurfaceSnap)
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
			else if (_editorPreviewNode is Decal previewDecal)
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
			else
			{
				_editorPreviewNode.Position = previewPos;
				_editorPreviewNode.RotationDegrees = new Vector3(0.0f, previewRot, 0.0f);
				_editorPreviewNode.Scale = Vector3.One * safePreviewScale;
			}
			_editorPreviewNode.Visible = true;
		}
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

	public void SetUnitPlayerExternal(Unit3D unit, int playerIndex)
	{
		if (GodotObject.IsInstanceValid(unit) && EcsWorld.IsAlive(unit.Entity))
		{
			bool isEnemy = NetworkService.ArePlayerIndicesEnemies(LocalPlayerIndex, playerIndex);
			var playerOwner = GetPlayerEntityForPlayerIndex(playerIndex).AsPlayerEntity(EcsWorld);
			EcsWorld.Set(unit.Entity, new Owner(playerOwner));
			
			EcsWorld.SetOrAdd(unit.Entity, new UnitOwnerPlayer(playerIndex));

			EcsWorld.SetOrAdd(unit.Entity, new UnitFaction(isEnemy));

			if (TryGetUnitOrBuildingMetadata(unit.UnitId, out var meta))
			{
				string name = meta.Name;
				EcsWorld.Set(unit.Entity, new Name(name));
			}
			unit.Player = playerIndex;
			unit.IsEnemy = isEnemy;
			unit.UpdatePlayerColorVisual();
			unit.IsSelected = unit.IsSelected;
		}
	}

	public void SetUnitTeamExternal(Unit3D unit, bool isEnemy)
	{
		int targetPlayer = isEnemy ? 1 : 0;
		if (LobbyManager.Instance != null && LobbyManager.Instance.PlayerList.Count > 0)
		{
			var enemyPlayer = LobbyManager.Instance.PlayerList.Find(p => NetworkService.ArePlayerIndicesEnemies(LocalPlayerIndex, p.Slot));
			if (isEnemy && enemyPlayer != null)
			{
				targetPlayer = enemyPlayer.Slot;
			}
		}
		SetUnitPlayerExternal(unit, targetPlayer);
	}

	private void UpdateDecalSelectionRing(Decal decal, bool selected)
	{
		if (!GodotObject.IsInstanceValid(decal)) return;
		var existingNodes = decal.GetChildren()
			.Where(child => child is MeshInstance3D mesh && (mesh.Name == "_selection_ring_decal" || mesh.Name == "EditorSelectionRing"))
			.ToList();
		foreach (var node in existingNodes)
		{
			decal.RemoveChild(node);
			node.QueueFree();
		}
		if (selected)
		{
			UpdateDecalHoverRing(decal, false);
			var ring = new MeshInstance3D();
			ring.Name = "_selection_ring_decal";
			ring.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
			ring.GIMode = GeometryInstance3D.GIModeEnum.Disabled;
			var torusMesh = new TorusMesh();
			torusMesh.InnerRadius = 2.5f;
			torusMesh.OuterRadius = 2.8f;
			ring.Mesh = torusMesh;
			ring.Position = new Vector3(0, 0.05f, 0);
			var material = new StandardMaterial3D();
			material.AlbedoColor = new Color(0.22f, 0.54f, 0.26f);
			material.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
			material.DisableReceiveShadows = true;
			material.EmissionEnabled = false;
			material.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
			ring.MaterialOverride = material;
			decal.AddChild(ring);
		}
	}

	private void UpdateDecalHoverRing(Decal decal, bool hovered)
	{
		if (!GodotObject.IsInstanceValid(decal)) return;
		var existingNodes = decal.GetChildren()
			.Where(child => child is MeshInstance3D mesh && mesh.Name == "_hover_ring_decal")
			.ToList();
		foreach (var node in existingNodes)
		{
			decal.RemoveChild(node);
			node.QueueFree();
		}
		if (hovered && SelectedEditorObject != decal)
		{
			var ring = new MeshInstance3D();
			ring.Name = "_hover_ring_decal";
			ring.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
			ring.GIMode = GeometryInstance3D.GIModeEnum.Disabled;
			var torusMesh = new TorusMesh();
			torusMesh.InnerRadius = 2.5f;
			torusMesh.OuterRadius = 2.8f;
			ring.Mesh = torusMesh;
			ring.Position = new Vector3(0, 0.05f, 0);
			var material = new StandardMaterial3D();
			material.AlbedoColor = new Color(0.88f, 0.88f, 0.88f, 0.22f);
			material.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
			material.DisableReceiveShadows = true;
			material.EmissionEnabled = false;
			material.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
			ring.MaterialOverride = material;
			decal.AddChild(ring);
		}
	}

	private Node3D? _editorCoverageOverlayRoot;

	/// <summary>
	///     When enabled, the editor draws vision/attack range rings around the selected unit.
	///     Off by default so the overlay only appears on demand.
	/// </summary>
	public bool EditorCoverageOverlayEnabled { get; set; } = false;

	internal void UpdateEditorCoverageOverlay()
	{
		if (_editorCoverageOverlayRoot == null)
		{
			_editorCoverageOverlayRoot = new Node3D();
			_editorCoverageOverlayRoot.Name = "EditorCoverageOverlay";
			AddChild(_editorCoverageOverlayRoot);
		}
		foreach (Node child in _editorCoverageOverlayRoot.GetChildren())
		{
			child.QueueFree();
		}

		if (!EditorCoverageOverlayEnabled)
		{
			_editorCoverageOverlayRoot.Visible = false;
			return;
		}

		if (!(_selectedEditorObject is Unit3D unit) || !EcsWorld.IsAlive(unit.Entity))
		{
			_editorCoverageOverlayRoot.Visible = false;
			return;
		}

		float scanRadius = EcsWorld.Has<ScanRadius>(unit.Entity) ? EcsWorld.Get<ScanRadius>(unit.Entity).Value : 0f;
		float range = EcsWorld.Has<Attack>(unit.Entity) ? EcsWorld.Get<Attack>(unit.Entity).Range : 0f;

		_editorCoverageOverlayRoot.Visible = scanRadius > 0f || range > 0f;
		if (!_editorCoverageOverlayRoot.Visible) return;

		_editorCoverageOverlayRoot.Position = unit.Position;
		if (scanRadius > 0f) CreateCoverageRing(scanRadius, new Color(0.3f, 0.7f, 1.0f, 0.6f));
		if (range > 0f) CreateCoverageRing(range, new Color(1.0f, 0.5f, 0.1f, 0.7f));
	}

	private void CreateCoverageRing(float radius, Color color)
	{
		var meshInstance = new MeshInstance3D();
		meshInstance.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
		meshInstance.GIMode = GeometryInstance3D.GIModeEnum.Disabled;
		var torusMesh = new TorusMesh();
		torusMesh.InnerRadius = Mathf.Max(radius - 0.25f, 0.05f);
		torusMesh.OuterRadius = radius + 0.25f;
		torusMesh.Rings = 32;
		meshInstance.Mesh = torusMesh;
		meshInstance.Position = new Vector3(0, 0.3f, 0);
		meshInstance.Scale = new Vector3(1f, 0.04f, 1f);

		var material = new StandardMaterial3D();
		material.AlbedoColor = color;
		material.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
		material.DisableReceiveShadows = true;
		material.EmissionEnabled = true;
		material.Emission = new Color(color.R, color.G, color.B);
		material.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
		meshInstance.MaterialOverride = material;

		_editorCoverageOverlayRoot.AddChild(meshInstance);
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
			if (GroundTerrain != null)
			{
				if (ActiveEditorTool == EditorTool.SelectArea && _editorService.IsSelectingArea && _editorService.SelectionStart != null)
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
				else if (ActiveEditorTool == EditorTool.DrawCoordinate && _editorService.IsSelectingArea && _editorService.SelectionStart != null)
				{
					var (rcx, rcz) = _editorService.WorldPosToCellCoords(hitPos);
					_editorService.SetSelectionEnd(new Vector2I(rcx, rcz));
					int rMinX = Mathf.Min(_editorService.SelectionStart.Value.X, rcx);
					int rMinZ = Mathf.Min(_editorService.SelectionStart.Value.Y, rcz);
					int rMaxX = Mathf.Max(_editorService.SelectionStart.Value.X, rcx);
					int rMaxZ = Mathf.Max(_editorService.SelectionStart.Value.Y, rcz);
					UpdateCoordinatePreviewMesh(rMinX, rMinZ, rMaxX, rMaxZ);
				}
				else if (ActiveEditorTool == EditorTool.PasteArea && _editorService.HasCopiedArea)
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

					MapEditorHUD.Instance?.UpdatePasteTelemetry(deltaX, deltaZ, distFromPivot / quadSize, distFromPivot, angleFromPivot);
				}
				else if (ActiveEditorTool == EditorTool.Measure)
				{
					if (EditorTapeMeasureActive && EditorTapeMeasureStart.HasValue)
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

						MapEditorHUD.Instance?.UpdateMeasureTelemetry(eucTiles, eucWorld, manhattanTiles, dx, dz, dy, angleDeg, slopePct);
					}
				}
			}
			
			bool canHover = (ActiveEditorTool == EditorTool.SelectMove && !_isDraggingObject) ||
							ActiveEditorTool == EditorTool.DeleteObject ||
							ActiveEditorTool == EditorTool.Eyedropper;
			Node newHovered = null;
			if (canHover && !IsMouseOverUI())
			{
				var objectHit = RaycastFromMouse(mousePos);
				var collider = (objectHit != null && objectHit.ContainsKey("collider")) ? objectHit["collider"].As<Node>() : null;
				if (collider != null)
				{
					newHovered = FindUnit3DInParentChain(collider);
					if (newHovered == null)
					{
						newHovered = FindProp3DInParentChain(collider);
					}
				}
				if (newHovered == null)
				{
					newHovered = FindDecal3DInParentChain(collider);
				}
				if (newHovered == null)
				{
					newHovered = FindVfxInParentChain(collider);
				}
			}

			if (_hoveredEditorObject != newHovered)
			{
				if (GodotObject.IsInstanceValid(_hoveredEditorObject))
				{
					if (_hoveredEditorObject is Unit3D u) u.IsHovered = false;
					else if (_hoveredEditorObject is Prop3D p) p.IsHovered = false;
					else if (_hoveredEditorObject is Decal d) UpdateDecalHoverRing(d, false);
					else if (_hoveredEditorObject is ProceduralVfxInstance3D v) v.IsHovered = false;
				}
				_hoveredEditorObject = newHovered;
				if (GodotObject.IsInstanceValid(_hoveredEditorObject))
				{
					if (_hoveredEditorObject is Unit3D u) u.IsHovered = true;
					else if (_hoveredEditorObject is Prop3D p) p.IsHovered = true;
					else if (_hoveredEditorObject is Decal d) UpdateDecalHoverRing(d, true);
					else if (_hoveredEditorObject is ProceduralVfxInstance3D v) v.IsHovered = true;
				}
			}

			if (Input.IsMouseButtonPressed(MouseButton.Left) && !_leftClickInitiatedOverUI && !FloatingDialogBase.HasAnyDialogOpen)
			{
				if (_is3DLeftClickDown && !_is3DDragOperationActive && mousePos.DistanceTo(_leftClick3DStartPos) > 3.0f)
				{
					_is3DDragOperationActive = true;
					MapEditorHUD.Instance?.Set3DInteractionActive(true);
				}

				if (!IsMouseOverUI())
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

					if (ActiveEditorTool == EditorTool.SelectMove && _isDraggingObject && GodotObject.IsInstanceValid(SelectedEditorObject))
					{
						float mouseDistPx = mousePos.DistanceTo(_dragStartMousePos);
						if (!_dragObjectHasMoved && mouseDistPx > 4.0f)
						{
							_dragObjectHasMoved = true;
							if (!_is3DDragOperationActive)
							{
								_is3DDragOperationActive = true;
								MapEditorHUD.Instance?.Set3DInteractionActive(true);
							}
						}

						if (_dragObjectHasMoved)
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
							if (SelectedEditorObject is Unit3D unit && EcsWorld.IsAlive(unit.Entity))
							{
								EcsWorld.Set(unit.Entity, new Position(new System.Numerics.Vector3(dragPos.X, dragPos.Y, dragPos.Z)));
								UpdateEditorCoverageOverlay();
							}
							else if (SelectedEditorObject is Prop3D prop)
							{
								if (EcsWorld.IsAlive(prop.Entity))
								{
									EcsWorld.Set(prop.Entity, new Position(new System.Numerics.Vector3(dragPos.X, dragPos.Y, dragPos.Z)));
								}
								PropMultiMeshManager.Instance?.MarkDirty(prop.PropId);
							}
							else if (SelectedEditorObject is Decal3D decal3D && EcsWorld.IsAlive(decal3D.Entity))
							{
								EcsWorld.Set(decal3D.Entity, new Position(new System.Numerics.Vector3(dragPos.X, dragPos.Y, dragPos.Z)));
							}
							else if (SelectedEditorObject is ProceduralVfxInstance3D vfxInst && EcsWorld.IsAlive(vfxInst.Entity))
							{
								EcsWorld.Set(vfxInst.Entity, new Position(new System.Numerics.Vector3(dragPos.X, dragPos.Y, dragPos.Z)));
							}
							MapEditorHUD.Instance?.UpdateSelectedObjectInfo();
						}
					}

					bool isTerrainTool = ActiveEditorTool == EditorTool.Raise ||
										 ActiveEditorTool == EditorTool.Lower ||
										 ActiveEditorTool == EditorTool.Height ||
										 ActiveEditorTool == EditorTool.Smooth ||
										 ActiveEditorTool == EditorTool.Plateau ||
										 ActiveEditorTool == EditorTool.PaintTexture ||
										 ActiveEditorTool == EditorTool.Noise ||
										 ActiveEditorTool == EditorTool.PaintPathing;

					bool firstClick = false;
					if (isTerrainTool && !_editorService.IsDrawingTerrain && GroundTerrain != null)
					{
						firstClick = true;
						float targetBlockHeight = ActiveEditorTool == EditorTool.Height ? EditorExactHeight : EditorBlockLevelHeight;
						_editorService.BeginTerrainDraw(
							hitPos,
							ActiveEditorTool,
							EditorBlockMode,
							targetBlockHeight,
							null,
							GroundTerrain.SplatMap,
							GroundTerrain.PathingCodes,
							GroundTerrain.CliffSplatMap);
					}

					ApplyContinuousTerrainEditing(hitPos, fDelta, firstClick);
				}
			}
			else
			{
				if (_is3DDragOperationActive)
				{
					_is3DDragOperationActive = false;
					_is3DLeftClickDown = false;
					MapEditorHUD.Instance?.Set3DInteractionActive(false);
				}
				if (_editorService.IsDrawingClump)
				{
					var composite = _editorService.EndClumpSession();
					if (composite != null)
					{
						EditorHistoryManager.RecordAction(composite);
						EditorHasUnsavedChanges = true;
					}
				}

				_editorService.ResetDrawState();

				if (_isDraggingObject)
				{
					_isDraggingObject = false;
					if (GodotObject.IsInstanceValid(SelectedEditorObject))
					{
						var node3D = SelectedEditorObject as Node3D;
						bool isUnit = SelectedEditorObject is Unit3D;
						bool isEnemy = isUnit ? (SelectedEditorObject as Unit3D).IsEnemy : false;
						if (SelectedEditorObject is Prop3D prop && SelectedEditorObject is not Unit3D)
						{
							PropMultiMeshManager.Instance?.MarkDirty(prop.PropId);
						}
						if (node3D.Position.DistanceTo(_dragObjectStartPos) > 0.05f)
						{
							var action = new ObjectTransformAction(
								node3D,
								_dragObjectStartPos, node3D.Position,
								_dragObjectStartRot, node3D.RotationDegrees,
								_dragObjectStartScale, node3D.Scale,
								_dragObjectStartIsEnemy, isEnemy
							);
							EditorHistoryManager.RecordAction(action);
							MapEditorHUD.Instance?.ShowFeedbackExternal("Moved Object");
							EditorHasUnsavedChanges = true;
						}
					}
				}
				if (_editorService.IsSelectingArea)
				{
					_editorService.SetIsSelectingArea(false);
				}
				if (_editorService.IsDrawingTerrain)
				{
					if (GroundTerrain != null && GroundTerrain.Cells != null && GroundTerrain.SplatMap != null && GroundTerrain.PathingCodes != null)
					{
						var action = _editorService.EndTerrainDraw(
							null,
							GroundTerrain.SplatMap,
							GroundTerrain.PathingCodes,
							GroundTerrain.CliffSplatMap);

						EditorHistoryManager.RecordAction(action);
						bool isHeightsTool = ActiveEditorTool == EditorTool.Raise ||
											 ActiveEditorTool == EditorTool.Lower ||
											 ActiveEditorTool == EditorTool.Height ||
											 ActiveEditorTool == EditorTool.Smooth ||
											 ActiveEditorTool == EditorTool.Plateau ||
											 ActiveEditorTool == EditorTool.Noise ||
											 ActiveEditorTool == EditorTool.PaintPathing;
						if (isHeightsTool)
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
			}
		}
		else
		{
			if (_is3DDragOperationActive)
			{
				_is3DDragOperationActive = false;
				_is3DLeftClickDown = false;
				MapEditorHUD.Instance?.Set3DInteractionActive(false);
			}

			if (_brushIndicatorMesh != null)
				_brushIndicatorMesh.Visible = false;
			ClearEditorPreview();
			if (_isDraggingObject)
			{
				_isDraggingObject = false;
				if (GodotObject.IsInstanceValid(SelectedEditorObject))
				{
					var node3D = SelectedEditorObject as Node3D;
					bool isUnit = SelectedEditorObject is Unit3D;
					bool isEnemy = isUnit ? (SelectedEditorObject as Unit3D).IsEnemy : false;
					if (node3D.Position.DistanceTo(_dragObjectStartPos) > 0.05f)
					{
						var action = new ObjectTransformAction(
							node3D,
							_dragObjectStartPos, node3D.Position,
							_dragObjectStartRot, node3D.RotationDegrees,
							_dragObjectStartScale, node3D.Scale,
							_dragObjectStartIsEnemy, isEnemy
						);
						EditorHistoryManager.RecordAction(action);
						EditorHasUnsavedChanges = true;
					}
				}
			}
			if (_editorService.IsDrawingClump)
			{
				var composite = _editorService.EndClumpSession();
				if (composite != null)
				{
					EditorHistoryManager.RecordAction(composite);
					EditorHasUnsavedChanges = true;
				}
			}
			if (_editorService.IsSelectingArea)
			{
				_editorService.SetIsSelectingArea(false);
			}
			if (_editorService.IsDrawingTerrain)
			{
				if (GroundTerrain != null && GroundTerrain.Cells != null && GroundTerrain.SplatMap != null && GroundTerrain.PathingCodes != null)
				{
					var action = _editorService.EndTerrainDraw(
						null,
						GroundTerrain.SplatMap,
						GroundTerrain.PathingCodes,
						GroundTerrain.CliffSplatMap);

					EditorHistoryManager.RecordAction(action);
					bool isHeightsTool = ActiveEditorTool == EditorTool.Raise ||
										 ActiveEditorTool == EditorTool.Lower ||
										 ActiveEditorTool == EditorTool.Height ||
										 ActiveEditorTool == EditorTool.Smooth ||
										 ActiveEditorTool == EditorTool.Plateau ||
										 ActiveEditorTool == EditorTool.Noise ||
										 ActiveEditorTool == EditorTool.PaintPathing;
					if (isHeightsTool)
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
		}
		
		ProcessMapEditorPhysics(fDelta);
	}

	public void StartMapEditorMode()
	{
		Realm.Godot.ReplaySystem.ReplayPlaybackManager.Instance.StopReplay();
		IsMapEditorMode = true;

		string wsPath = Godot.ProjectSettings.GlobalizePath(MapEditorHUD.TempWorkspaceGodotPath);
		try
		{
			System.IO.Directory.CreateDirectory(wsPath);
			MapWorkspaceService.SetupWorkspace(wsPath, "MapScript");
		}
		catch (Exception ex)
		{
			GD.PrintErr($"Failed setting up map workspace: {ex.Message}");
		}

		ActiveEditorTool = EditorTool.None;
		EditorHistoryManager.Clear();

		if (MapEditorHUD.ReturningFromTest)
		{
			try
			{
				LoadMapFromFile(MapEditorHUD.TempWorkspaceGodotPath + "/terrain.json");

				EditorGridMode = MapEditorHUD.SavedGridMode;
				EditorCameraBoundsVisible = MapEditorHUD.SavedCameraBoundsVisible;
				EditorDisableShadows = MapEditorHUD.SavedDisableShadows;

				var camera = MainCamera as CameraControl;
				if (camera != null)
				{
					camera.Position = MapEditorHUD.SavedCameraPosition;
					if (EcsWorld != null && EcsWorld.IsAlive(WorldEntity) && EcsWorld.Has<CameraState>(WorldEntity))
					{
						ref var state = ref EcsWorld.Get<CameraState>(WorldEntity);
						state.TargetHeight = MapEditorHUD.SavedTargetHeight;
						state.CurrentHeight = MapEditorHUD.SavedTargetHeight;
						state.TargetYaw = MapEditorHUD.SavedTargetYaw;
						state.CurrentYaw = MapEditorHUD.SavedTargetYaw;
						state.TargetPitch = MapEditorHUD.SavedTargetPitch;
						state.CurrentPitch = MapEditorHUD.SavedTargetPitch;
						state.IsTopDown = MapEditorHUD.SavedIsTopDown;
						state.YawSwing = MapEditorHUD.SavedYawSwing;
						state.PitchSwing = MapEditorHUD.SavedPitchSwing;
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
	}

	public void ExitMapEditorMode()
	{
		IsMapEditorMode = false;
		ActiveEditorTool = EditorTool.None;
		EditorHistoryManager.Clear();
		ClearEditorPreview();
		UpdateEditorShadows();
		
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

	private void ClearAllUnits()
	{
		SelectedUnits.Clear();
		foreach (var unit in AllUnits)
		{
			if (GodotObject.IsInstanceValid(unit))
			{
				unit.QueueFree();
			}
		}
		AllUnits.Clear();
		_castlesList.Clear();
		CurrentPopulation = 0;
		MaxPopulation = 0;

		foreach (var child in GetChildren())
		{
			if (child is Prop3D prop)
			{
				prop.QueueFree();
			}
			else if (child is Decal decal)
			{
				decal.QueueFree();
			}
			else if (child is ProceduralVfxInstance3D vfx)
			{
				vfx.QueueFree();
			}
		}
		AllProps.Clear();
		AllDecals.Clear();
		AllVfx.Clear();
		EntityToUnit3D.Clear();
		EntityToProp3D.Clear();
		
		ReinitializeEcsAndServices();
		
		_playerEntity = EcsWorld.Create();
		EcsWorld.Add(_playerEntity, new Player());
		EcsWorld.Add(_playerEntity, new Name("Horaid_Topa"));
		InitializePlayerResources(_playerEntity);
		SetupPlayerEntityComponents(_playerEntity);

		_enemyPlayerEntity = EcsWorld.Create();
		EcsWorld.Add(_enemyPlayerEntity, new Player());
		EcsWorld.Add(_enemyPlayerEntity, new Name("Enemy_AI"));
		InitializePlayerResources(_enemyPlayerEntity);
		SetupPlayerEntityComponents(_enemyPlayerEntity);
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
			plane.Size = new Vector2(EditableTerrain.DefaultQuadSize, EditableTerrain.DefaultQuadSize);
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
		
		bool isVertexTool = ActiveEditorTool == EditorTool.Raise ||
							ActiveEditorTool == EditorTool.Lower ||
							ActiveEditorTool == EditorTool.Height ||
							ActiveEditorTool == EditorTool.Smooth ||
							ActiveEditorTool == EditorTool.Plateau ||
							ActiveEditorTool == EditorTool.PaintTexture ||
							ActiveEditorTool == EditorTool.Noise ||
							ActiveEditorTool == EditorTool.Ramp;

		Vector3 targetPos = position;
		if (isVertexTool && GroundTerrain != null)
		{
			targetPos = _editorService.SnapToVertex(position);
		}
		else if (EditorSnapToGrid && GroundTerrain != null)
		{
			targetPos = _editorService.SnapToGrid(position);
		}
		
		_brushIndicatorMesh.Position = new Vector3(targetPos.X, targetPos.Y + 0.1f, targetPos.Z);
		_brushIndicatorMesh.Scale = new Vector3(EditorBrushRadius, 0.1f, EditorBrushRadius);
		
		bool isTerrainTool = isVertexTool ||
							 ActiveEditorTool == EditorTool.PlacePropClump ||
							 ActiveEditorTool == EditorTool.PaintPathing ||
							 ((ActiveEditorTool == EditorTool.PlaceUnit || ActiveEditorTool == EditorTool.PlaceProp || ActiveEditorTool == EditorTool.PlaceDecal) && EditorClumpMode);
							 
		_brushIndicatorMesh.Visible = isTerrainTool;
	}

	private MeshInstance3D _measureMeshInstance;
	private ImmediateMesh _measureImmediateMesh;

	public void UpdateMeasureVisuals(Vector3 start, Vector3 end)
	{
		if (_measureMeshInstance == null)
		{
			_measureMeshInstance = new MeshInstance3D();
			_measureMeshInstance.Name = "TapeMeasureIndicator";
			_measureImmediateMesh = new ImmediateMesh();
			_measureMeshInstance.Mesh = _measureImmediateMesh;
			var mat = new StandardMaterial3D
			{
				ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
				AlbedoColor = new Color(1.0f, 0.85f, 0.1f, 0.95f),
				Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
				CullMode = BaseMaterial3D.CullModeEnum.Disabled
			};
			_measureMeshInstance.MaterialOverride = mat;
			AddChild(_measureMeshInstance);
		}

		_measureImmediateMesh.ClearSurfaces();
		_measureImmediateMesh.SurfaceBegin(Mesh.PrimitiveType.Lines);
		_measureImmediateMesh.SurfaceAddVertex(start + new Vector3(0, 0.2f, 0));
		_measureImmediateMesh.SurfaceAddVertex(end + new Vector3(0, 0.2f, 0));
		
		float markerSize = 0.5f;
		_measureImmediateMesh.SurfaceAddVertex(start + new Vector3(-markerSize, 0.2f, 0));
		_measureImmediateMesh.SurfaceAddVertex(start + new Vector3(markerSize, 0.2f, 0));
		_measureImmediateMesh.SurfaceAddVertex(start + new Vector3(0, 0.2f, -markerSize));
		_measureImmediateMesh.SurfaceAddVertex(start + new Vector3(0, 0.2f, markerSize));

		_measureImmediateMesh.SurfaceAddVertex(end + new Vector3(-markerSize, 0.2f, 0));
		_measureImmediateMesh.SurfaceAddVertex(end + new Vector3(markerSize, 0.2f, 0));
		_measureImmediateMesh.SurfaceAddVertex(end + new Vector3(0, 0.2f, -markerSize));
		_measureImmediateMesh.SurfaceAddVertex(end + new Vector3(0, 0.2f, markerSize));
		
		_measureImmediateMesh.SurfaceEnd();
		_measureMeshInstance.Visible = true;
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

	private MeshInstance3D _symmetryPivotMarkerMesh;

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

	public MeshInstance3D BrushIndicatorMesh => _brushIndicatorMesh;
	public MeshInstance3D? GridOverlayMesh => null;
	public MeshInstance3D? PathingOverlayMesh => null;

	public void ClearRampStartPosExternal()
	{
		_editorService.SetRampStartPos(null);
	}

	public struct MirroredTransform
	{
		public Vector3 Position;
		public float Rotation;
	}

	public List<MirroredTransform> GetMirroredTransforms(Vector3 pos, float rotation)
	{
		return _editorService.GetMirroredTransforms(pos, rotation, EditorMirrorMode);
	}

	private Node3D FindObjectNearPosition(Vector3 position, float searchRadius = 1.5f)
	{
		foreach (var child in GetChildren())
		{
			if (child is Node3D n3d && GodotObject.IsInstanceValid(n3d))
			{
				if (n3d is Unit3D || n3d is Prop3D || n3d is Decal)
				{
					float dist = new Vector2(n3d.GlobalPosition.X - position.X, n3d.GlobalPosition.Z - position.Z).Length();
					if (dist <= searchRadius)
					{
						return n3d;
					}
				}
			}
		}
		return null;
	}

	private bool ApplyRampInternal(Vector3 start, Vector3 end)
	{
		return _editorService.ApplyRamp(start, end, EditorBrushRadius, EditorBlockMode, EditorBlockLevelHeight);
	}

	private float GetMinHeightInBrushBounds(Vector3 worldPos)
	{
		return _editorService.GetMinHeightInBrushBounds(worldPos, EditorBrushRadius, EditorBrushIsSquare);
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

	public void SwapTexturesExternal(int indexA, int indexB)
	{
		if (GroundTerrain == null || GroundTerrain.SplatMap == null) return;
		if (indexA == indexB) return;

		int splatW = GroundTerrain.SplatMap.GetLength(0);
		int splatD = GroundTerrain.SplatMap.GetLength(1);

		TerrainSplatWeights[,] splatBefore = (TerrainSplatWeights[,])GroundTerrain.SplatMap.Clone();
		TerrainSplatWeights[,] cliffBefore = GroundTerrain.CliffSplatMap != null ? (TerrainSplatWeights[,])GroundTerrain.CliffSplatMap.Clone() : null;

		bool anyChanged = false;

		for (int z = 0; z < splatD; z++)
		{
			for (int x = 0; x < splatW; x++)
			{
				var s = GroundTerrain.SplatMap[x, z];
				if (s.Index0 == indexA || s.Index0 == indexB ||
					s.Index1 == indexA || s.Index1 == indexB ||
					s.Index2 == indexA || s.Index2 == indexB ||
					s.Index3 == indexA || s.Index3 == indexB)
				{
					GroundTerrain.SplatMap[x, z] = new TerrainSplatWeights
					{
						Index0 = s.Index0 == indexA ? indexB : (s.Index0 == indexB ? indexA : s.Index0),
						Index1 = s.Index1 == indexA ? indexB : (s.Index1 == indexB ? indexA : s.Index1),
						Index2 = s.Index2 == indexA ? indexB : (s.Index2 == indexB ? indexA : s.Index2),
						Index3 = s.Index3 == indexA ? indexB : (s.Index3 == indexB ? indexA : s.Index3),
						Weight0 = s.Weight0,
						Weight1 = s.Weight1,
						Weight2 = s.Weight2,
						Weight3 = s.Weight3
					};
					anyChanged = true;
				}

				if (GroundTerrain.CliffSplatMap != null && x < GroundTerrain.CliffSplatMap.GetLength(0) && z < GroundTerrain.CliffSplatMap.GetLength(1))
				{
					var c = GroundTerrain.CliffSplatMap[x, z];
					if (c.Index0 == indexA || c.Index0 == indexB ||
						c.Index1 == indexA || c.Index1 == indexB ||
						c.Index2 == indexA || c.Index2 == indexB ||
						c.Index3 == indexA || c.Index3 == indexB)
					{
						GroundTerrain.CliffSplatMap[x, z] = new TerrainSplatWeights
						{
							Index0 = c.Index0 == indexA ? indexB : (c.Index0 == indexB ? indexA : c.Index0),
							Index1 = c.Index1 == indexA ? indexB : (c.Index1 == indexB ? indexA : c.Index1),
							Index2 = c.Index2 == indexA ? indexB : (c.Index2 == indexB ? indexA : c.Index2),
							Index3 = c.Index3 == indexA ? indexB : (c.Index3 == indexB ? indexA : c.Index3),
							Weight0 = c.Weight0,
							Weight1 = c.Weight1,
							Weight2 = c.Weight2,
							Weight3 = c.Weight3
						};
						anyChanged = true;
					}
				}
			}
		}

		if (anyChanged)
		{
			TerrainSplatWeights[,] splatAfter = (TerrainSplatWeights[,])GroundTerrain.SplatMap.Clone();
			TerrainSplatWeights[,] cliffAfter = GroundTerrain.CliffSplatMap != null ? (TerrainSplatWeights[,])GroundTerrain.CliffSplatMap.Clone() : null;
			
			var action = new TerrainModifyAction((Realm.Ecs.Components.Terrain.TerrainCell[,])null, (Realm.Ecs.Components.Terrain.TerrainCell[,])null, splatBefore, splatAfter, null, null, cliffBefore, cliffAfter);
			EditorHistoryManager.RecordAction(action);
			EditorHasUnsavedChanges = true;
			
			GroundTerrain.UpdateMeshAndPhysics(false, false);
			MapEditorHUD.Instance?.ShowFeedbackExternal("Textures swapped successfully!");
		}
	}

	public void AlignTerrainSplatMapExternal()
	{
		if (GroundTerrain != null)
		{
			_editorService.SetTerrainSplatMap(GroundTerrain.SplatMap, GroundTerrain.CliffSplatMap);
		}
	}

	public void ResizeMapExternal(int newWidth, int newDepth)
	{
		if (GroundTerrain == null) return;

		newWidth = Math.Clamp((int)Math.Round(newWidth / 32.0) * 32, 32, 512);
		newDepth = Math.Clamp((int)Math.Round(newDepth / 32.0) * 32, 32, 512);

		var before = MapStateSnapshot.CreateSnapshot();

		int oldWidth = GroundTerrain.Width;
		int oldDepth = GroundTerrain.Depth;

		float diffWidth = (newWidth - oldWidth) * GroundTerrain.QuadSize;
		float diffDepth = (newDepth - oldDepth) * GroundTerrain.QuadSize;

		EditorCameraBoundsLeft -= diffWidth / 2.0f;
		EditorCameraBoundsRight += diffWidth / 2.0f;
		EditorCameraBoundsTop -= diffDepth / 2.0f;
		EditorCameraBoundsBottom += diffDepth / 2.0f;

		GroundTerrain.ResizeTerrain(newWidth, newDepth);

		_editorService.SetTerrainSplatMap(GroundTerrain.SplatMap, GroundTerrain.CliffSplatMap);
		DeleteEntitiesOutsideBounds();
		PropMultiMeshManager.Instance?.RebuildAll();

		RebuildCameraBoundsOverlay();
		MapEditorHUD.Instance?.UpdateCameraBoundsUI();
		MapEditorHUD.Instance?.RegenerateMinimap();

		string activeWsPath = MapWorkspaceService.GetActiveWorkspacePath();
		string metaPath = MetadataService.ResolveMetadataPath(activeWsPath);
		if (System.IO.File.Exists(metaPath))
		{
			try
			{
				MetadataService.Instance.UpdateMetadata(activeWsPath, meta =>
				{
					meta.MapProperties.MapWidth = newWidth;
					meta.MapProperties.MapHeight = newDepth;
				});
			}
			catch (Exception ex)
			{
				GD.PrintErr($"Failed to update metadata.json map dimensions: {ex.Message}");
			}
		}

		EditorHasUnsavedChanges = true;
		MapEditorHUD.Instance?.ShowFeedbackExternal($"Map resized to {newWidth}x{newDepth}");

		var after = MapStateSnapshot.CreateSnapshot();
		EditorHistoryManager.RecordAction(new MapResizeAction(before, after));
	}

	public void ScaleMapExternal(int newWidth, int newDepth)
	{
		if (GroundTerrain == null) return;

		newWidth = Math.Clamp((int)Math.Round(newWidth / 32.0) * 32, 32, 512);
		newDepth = Math.Clamp((int)Math.Round(newDepth / 32.0) * 32, 32, 512);

		var before = MapStateSnapshot.CreateSnapshot();

		int oldWidth = GroundTerrain.Width;
		int oldDepth = GroundTerrain.Depth;
		float quadSize = GroundTerrain.QuadSize;

		float oldHalfW = oldWidth / 2.0f * quadSize;
		float oldHalfD = oldDepth / 2.0f * quadSize;
		float newHalfW = newWidth / 2.0f * quadSize;
		float newHalfD = newDepth / 2.0f * quadSize;
		float scaleX = oldHalfW > 0f ? newHalfW / oldHalfW : 1f;
		float scaleZ = oldHalfD > 0f ? newHalfD / oldHalfD : 1f;

		GroundTerrain.ScaleTerrainData(newWidth, newDepth);

		foreach (var unit in AllUnits)
		{
			if (GodotObject.IsInstanceValid(unit))
			{
				unit.Position = new Godot.Vector3(unit.Position.X * scaleX, unit.Position.Y, unit.Position.Z * scaleZ);
				if (EcsWorld != null && EcsWorld.IsAlive(unit.Entity) && EcsWorld.Has<Realm.Ecs.Components.Core.Position>(unit.Entity))
				{
					EcsWorld.Set(unit.Entity, new Realm.Ecs.Components.Core.Position(new System.Numerics.Vector3(unit.Position.X, unit.Position.Y, unit.Position.Z)));
				}
			}
		}

		foreach (var prop in AllProps)
		{
			if (GodotObject.IsInstanceValid(prop))
			{
				prop.Position = new Godot.Vector3(prop.Position.X * scaleX, prop.Position.Y, prop.Position.Z * scaleZ);
				if (EcsWorld != null && EcsWorld.IsAlive(prop.Entity) && EcsWorld.Has<Realm.Ecs.Components.Core.Position>(prop.Entity))
				{
					EcsWorld.Set(prop.Entity, new Realm.Ecs.Components.Core.Position(new System.Numerics.Vector3(prop.Position.X, prop.Position.Y, prop.Position.Z)));
				}
			}
		}

		if (EcsWorld != null)
		{
			var allPropEntitiesQuery = Realm.Ecs.Common.QueryCache.AllPropIdentityAndPositionQuery;
			EcsWorld.Query(in allPropEntitiesQuery, (Arch.Core.Entity entity, ref Realm.Ecs.Components.Core.Position posComp) =>
			{
				if (!EntityToProp3D.ContainsKey(entity))
				{
					EcsWorld.Set(entity, new Realm.Ecs.Components.Core.Position(new System.Numerics.Vector3(posComp.Value.X * scaleX, posComp.Value.Y, posComp.Value.Z * scaleZ)));
				}
			});
		}

		foreach (var decal in AllDecals)
		{
			if (GodotObject.IsInstanceValid(decal))
			{
				decal.Position = new Godot.Vector3(decal.Position.X * scaleX, decal.Position.Y, decal.Position.Z * scaleZ);
				if (decal is Decal3D decal3D && EcsWorld != null && EcsWorld.IsAlive(decal3D.Entity) && EcsWorld.Has<Realm.Ecs.Components.Core.Position>(decal3D.Entity))
				{
					EcsWorld.Set(decal3D.Entity, new Realm.Ecs.Components.Core.Position(new System.Numerics.Vector3(decal.Position.X, decal.Position.Y, decal.Position.Z)));
				}
			}
		}

		if (AllVfx != null)
		{
			foreach (var vfx in AllVfx)
			{
				if (vfx != null && GodotObject.IsInstanceValid(vfx))
				{
					vfx.Position = new Godot.Vector3(vfx.Position.X * scaleX, vfx.Position.Y, vfx.Position.Z * scaleZ);
				}
			}
		}

		for (int i = 0; i < EditorCoordinates.Count; i++)
		{
			var coord = EditorCoordinates[i];
			coord.MinX *= scaleX;
			coord.MaxX *= scaleX;
			coord.MinZ *= scaleZ;
			coord.MaxZ *= scaleZ;
		}
		RebuildAllCoordinatePersistentMeshes();
		MapEditorHUD.Instance?.RefreshCoordinateListExternal();

		EditorCameraBoundsLeft *= scaleX;
		EditorCameraBoundsRight *= scaleX;
		EditorCameraBoundsTop *= scaleZ;
		EditorCameraBoundsBottom *= scaleZ;

		DeleteEntitiesOutsideBounds();
		PropMultiMeshManager.Instance?.RebuildAll();

		_editorService.SetTerrainSplatMap(GroundTerrain.SplatMap, GroundTerrain.CliffSplatMap);

		string scaleWsPath = MapWorkspaceService.GetActiveWorkspacePath();
		string scaleMetaPath = MetadataService.ResolveMetadataPath(scaleWsPath);
		if (System.IO.File.Exists(scaleMetaPath))
		{
			try
			{
				MetadataService.Instance.UpdateMetadata(scaleWsPath, meta =>
				{
					meta.MapProperties.MapWidth = newWidth;
					meta.MapProperties.MapHeight = newDepth;
				});
			}
			catch (Exception ex)
			{
				GD.PrintErr($"Failed to update metadata.json map dimensions during scale: {ex.Message}");
			}
		}
		RebuildCameraBoundsOverlay();
		MapEditorHUD.Instance?.UpdateCameraBoundsUI();
		MapEditorHUD.Instance?.RegenerateMinimap();

		EditorHasUnsavedChanges = true;
		MapEditorHUD.Instance?.ShowFeedbackExternal($"Map scaled to {newWidth}x{newDepth}");

		var after = MapStateSnapshot.CreateSnapshot();
		EditorHistoryManager.RecordAction(new MapResizeAction(before, after));
	}

	private void DeleteEntitiesOutsideBounds()
	{
		if (GroundTerrain == null) return;

		float halfW = GroundTerrain.Width / 2.0f * GroundTerrain.QuadSize;
		float halfD = GroundTerrain.Depth / 2.0f * GroundTerrain.QuadSize;

		var unitsToDelete = new List<Unit3D>();
		foreach (var unit in AllUnits)
		{
			if (GodotObject.IsInstanceValid(unit))
			{
				var pos = unit.Position;
				if (pos.X < -halfW || pos.X > halfW || pos.Z < -halfD || pos.Z > halfD)
				{
					unitsToDelete.Add(unit);
				}
			}
		}
		foreach (var unit in unitsToDelete)
		{
			DeleteNodeExternal(unit);
		}

		var propsToDelete = new List<Prop3D>();
		foreach (var prop in AllProps)
		{
			if (GodotObject.IsInstanceValid(prop))
			{
				var pos = prop.Position;
				if (pos.X < -halfW || pos.X > halfW || pos.Z < -halfD || pos.Z > halfD)
				{
					propsToDelete.Add(prop);
				}
			}
		}
		foreach (var prop in propsToDelete)
		{
			DeleteNodeExternal(prop);
		}

		var decalsToDelete = new List<Decal>();
		foreach (var decal in AllDecals)
		{
			if (GodotObject.IsInstanceValid(decal))
			{
				var pos = decal.Position;
				if (pos.X < -halfW || pos.X > halfW || pos.Z < -halfD || pos.Z > halfD)
				{
					decalsToDelete.Add(decal);
				}
			}
		}
		foreach (var decal in decalsToDelete)
		{
			DeleteNodeExternal(decal);
		}

		if (AllVfx != null)
		{
			var vfxToDelete = new List<ProceduralVfxInstance3D>();
			foreach (var vfx in AllVfx)
			{
				if (vfx != null && GodotObject.IsInstanceValid(vfx))
				{
					var pos = vfx.Position;
					if (pos.X < -halfW || pos.X > halfW || pos.Z < -halfD || pos.Z > halfD)
					{
						vfxToDelete.Add(vfx);
					}
				}
			}
			foreach (var vfx in vfxToDelete)
			{
				DeleteNodeExternal(vfx);
			}
		}
	}

	private MeshInstance3D _scaleMapSilhouetteMesh;

	public void ShowScaleMapSilhouette(int previewWidth, int previewDepth)
	{
		if (GroundTerrain == null) return;

		float targetWidthSize = previewWidth * GroundTerrain.QuadSize;
		float targetDepthSize = previewDepth * GroundTerrain.QuadSize;

		if (_scaleMapSilhouetteMesh != null && GodotObject.IsInstanceValid(_scaleMapSilhouetteMesh))
		{
			if (_scaleMapSilhouetteMesh.Mesh is PlaneMesh existingPlane)
			{
				existingPlane.Size = new Godot.Vector2(targetWidthSize, targetDepthSize);
				return;
			}
		}

		HideScaleMapSilhouette();

		_scaleMapSilhouetteMesh = new MeshInstance3D();
		_scaleMapSilhouetteMesh.Name = "ScaleMapSilhouette";

		var plane = new PlaneMesh();
		plane.Size = new Godot.Vector2(targetWidthSize, targetDepthSize);
		plane.SubdivideWidth = 0;
		plane.SubdivideDepth = 0;
		_scaleMapSilhouetteMesh.Mesh = plane;

		var mat = new StandardMaterial3D();
		mat.AlbedoColor = new Color(0.8f, 0.5f, 0.05f, 0.25f);
		mat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
		mat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
		mat.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
		mat.NoDepthTest = true;
		mat.RenderPriority = 10;
		mat.EmissionEnabled = true;
		mat.Emission = new Color(1.0f, 0.6f, 0.1f) * 0.4f;
		_scaleMapSilhouetteMesh.MaterialOverride = mat;

		_scaleMapSilhouetteMesh.Position = new Godot.Vector3(0f, 1.0f, 0f);

		AddChild(_scaleMapSilhouetteMesh);
	}

	public void HideScaleMapSilhouette()
	{
		if (_scaleMapSilhouetteMesh != null)
		{
			_scaleMapSilhouetteMesh.QueueFree();
			_scaleMapSilhouetteMesh = null;
		}
	}

	public void RebuildCameraBoundsOverlay()
	{
		if (_cameraBoundsOverlayMesh == null || GroundTerrain == null || GroundTerrain.Cells == null) return;
		if (!EditorCameraBoundsVisible) return;

		int width = GroundTerrain.Width;
		int depth = GroundTerrain.Depth;
		float quadSize = GroundTerrain.QuadSize;

		float halfW = width / 2.0f;
		float halfD = depth / 2.0f;

		float minWorldX = -halfW * quadSize;
		float maxWorldX = halfW * quadSize;
		float minWorldZ = -halfD * quadSize;
		float maxWorldZ = halfD * quadSize;

		float left = Mathf.Clamp(EditorCameraBoundsLeft, minWorldX, maxWorldX);
		float right = Mathf.Clamp(EditorCameraBoundsRight, minWorldX, maxWorldX);
		float top = Mathf.Clamp(EditorCameraBoundsTop, minWorldZ, maxWorldZ);
		float bottom = Mathf.Clamp(EditorCameraBoundsBottom, minWorldZ, maxWorldZ);

		var linePoints = new List<Vector3>();

		float GetTerrainHeightAtCoord(float worldX, float worldZ)
		{
			if (GroundTerrain == null || GroundTerrain.Cells == null) return 0f;
			float gridX = worldX / quadSize + halfW;
			float gridZ = worldZ / quadSize + halfD;
			int x0 = Mathf.Clamp((int)Mathf.Floor(gridX), 0, width - 1);
			int x1 = Mathf.Clamp(x0 + 1, 0, width - 1);
			int z0 = Mathf.Clamp((int)Mathf.Floor(gridZ), 0, depth - 1);
			int z1 = Mathf.Clamp(z0 + 1, 0, depth - 1);
			
			float tx = gridX - x0;
			float tz = gridZ - z0;
			
			var cells = GroundTerrain.Cells;
			float h00 = EditableTerrain.GetGridNodeHeight(x0, z0, cells, width, depth);
			float h10 = EditableTerrain.GetGridNodeHeight(x1, z0, cells, width, depth);
			float h01 = EditableTerrain.GetGridNodeHeight(x0, z1, cells, width, depth);
			float h11 = EditableTerrain.GetGridNodeHeight(x1, z1, cells, width, depth);
			
			float h0 = Mathf.Lerp(h00, h10, tx);
			float h1 = Mathf.Lerp(h01, h11, tx);
			return Mathf.Lerp(h0, h1, tz);
		}

		void AddSegmentedLine(float x1, float z1, float x2, float z2)
		{
			float dist = Mathf.Sqrt((x2 - x1) * (x2 - x1) + (z2 - z1) * (z2 - z1));
			int segments = Mathf.Max(1, (int)Mathf.Ceil(dist / quadSize));
			for (int i = 0; i < segments; i++)
			{
				float t1 = (float)i / segments;
				float t2 = (float)(i + 1) / segments;
				
				float lx1 = Mathf.Lerp(x1, x2, t1);
				float lz1 = Mathf.Lerp(z1, z2, t1);
				float lx2 = Mathf.Lerp(x1, x2, t2);
				float lz2 = Mathf.Lerp(z1, z2, t2);
				
				float y1 = GetTerrainHeightAtCoord(lx1, lz1) + 0.2f;
				float y2 = GetTerrainHeightAtCoord(lx2, lz2) + 0.2f;
				
				linePoints.Add(new Vector3(lx1, y1, lz1));
				linePoints.Add(new Vector3(lx2, y2, lz2));
			}
		}

		AddSegmentedLine(left, top, right, top);
		AddSegmentedLine(right, top, right, bottom);
		AddSegmentedLine(right, bottom, left, bottom);
		AddSegmentedLine(left, bottom, left, top);

		int totalVertices = linePoints.Count * 3;
		var vertices = new Vector3[totalVertices];
		var colors = new Color[totalVertices];
		int idx = 0;

		Color boundsColor = new Color(0.9f, 0.1f, 0.8f, 0.95f);

		for (int i = 0; i < linePoints.Count; i += 2)
		{
			Vector3 p1 = linePoints[i];
			Vector3 p2 = linePoints[i + 1];

			vertices[idx] = p1;
			colors[idx] = boundsColor;
			idx++;
			vertices[idx] = p2;
			colors[idx] = boundsColor;
			idx++;

			Vector3 dir = (p2 - p1).Normalized();
			Vector3 ortho = new Vector3(-dir.Z, 0, dir.X) * 0.08f;

			vertices[idx] = p1 + ortho;
			colors[idx] = boundsColor;
			idx++;
			vertices[idx] = p2 + ortho;
			colors[idx] = boundsColor;
			idx++;

			vertices[idx] = p1 - ortho;
			colors[idx] = boundsColor;
			idx++;
			vertices[idx] = p2 - ortho;
			colors[idx] = boundsColor;
			idx++;
		}

		var arrays = new Godot.Collections.Array();
		arrays.Resize((int)Mesh.ArrayType.Max);
		arrays[(int)Mesh.ArrayType.Vertex] = vertices;
		arrays[(int)Mesh.ArrayType.Color] = colors;

		var arrayMesh = new ArrayMesh();
		arrayMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Lines, arrays);
		_cameraBoundsOverlayMesh.Mesh = arrayMesh;
	}

	private void PerformCopyArea()
	{
		if (GroundTerrain == null || _editorService.SelectionStart == null || _editorService.SelectionEnd == null) return;

		var (minX, minZ, maxX, maxZ) = _editorService.GetCurrentSelectionBounds();

		var node3Ds = new List<Node3D>();
		foreach (var child in GetChildren())
		{
			if (child is Node3D n3d) node3Ds.Add(n3d);
		}

		var entities = _editorService.BuildCopiedEntityList(minX, minZ, maxX, maxZ, node3Ds, EditorBrushIsSquare);
		_editorService.CopyArea(minX, minZ, maxX, maxZ, entities, EditorBrushIsSquare);

		int selWidth = maxX - minX + 1;
		int selDepth = maxZ - minZ + 1;
		MapEditorHUD.Instance?.ShowFeedbackExternal($"Copied Area: {selWidth}x{selDepth} tiles, {entities.Count} entities");
	}

	private void InitializeCameraBoundsOverlay()
	{
		if (_cameraBoundsOverlayMesh != null) return;

		_cameraBoundsOverlayMesh = new MeshInstance3D();
		_cameraBoundsOverlayMesh.Name = "CameraBoundsOverlay";

		var mat = new StandardMaterial3D();
		mat.AlbedoColor = new Color(1.0f, 1.0f, 1.0f, 1.0f);
		mat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
		mat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
		mat.NoDepthTest = false;
		mat.VertexColorUseAsAlbedo = true;
		_cameraBoundsOverlayMesh.MaterialOverride = mat;

		AddChild(_cameraBoundsOverlayMesh);
		_cameraBoundsOverlayMesh.Visible = false;
	}

	private void RebuildPathingOverlay()
	{
		if (GroundTerrain == null || GroundTerrain.PathingCodes == null || GroundTerrain.Cells == null) return;
		GroundTerrain.UpdatePathingTexture();
	}

	public void RebuildGridOverlayMeshExternal()
	{
		// No longer needed, grid is on shader
	}

	public void UpdateGridOverlayVisibility()
	{
		if (GroundTerrain != null)
		{
			bool gridVisible = IsMapEditorMode && (EditorGridMode == GridOverlayMode.Grid || EditorGridMode == GridOverlayMode.Both);
			GroundTerrain.SetGridVisible(gridVisible);
			bool polarVisible = IsMapEditorMode && (EditorMirrorMode == MirrorMode.Rotational || EditorGridMode == GridOverlayMode.Polar || EditorGridMode == GridOverlayMode.Both);
			GroundTerrain.SetPolarOverlayVisible(polarVisible);
			EditorPolarOverlayVisible = polarVisible;
		}
		UpdateSymmetryPivotVisuals();
	}

	public void UpdatePolarOverlayVisibility()
	{
		UpdateGridOverlayVisibility();
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
		MapEditorHUD.Instance?.ShowFeedbackExternal(result.IsCliff ? "Flood filled cliff face area" : "Flood filled terrain area");
	}

	public void PerformFloodFillPathing(Vector3 clickPos, int pathingMask, bool pathingAdd)
	{
		if (GroundTerrain == null || GroundTerrain.PathingCodes == null) return;

		var result = _editorService.PerformFloodFillPathing(clickPos, pathingMask, pathingAdd, EditorMirrorMode);

		if (result.Before != null && result.After != null)
		{
			GroundTerrain.UpdateMeshAndPhysics(false, false);
			var action = new TerrainModifyAction((Realm.Ecs.Components.Terrain.TerrainCell[,])null, (Realm.Ecs.Components.Terrain.TerrainCell[,])null, null, null, result.Before, result.After);
			EditorHistoryManager.RecordAction(action);
			EditorHasUnsavedChanges = true;
			MapEditorHUD.Instance?.ShowFeedbackExternal("Flood filled pathing area");
			UpdatePathingOverlay();
		}
	}

	public void PerformWaterFloodFill(Vector3 clickPos, bool isRemoveAction = false)
	{
		if (GroundTerrain == null || GroundTerrain.Cells == null) return;

		WaterType activeMode = EditorWaterMode;
		byte activeProfile = ActiveWaterProfileIndex;
		float waterHeight = EditorWaterHeight;

		var result = _editorService.PerformWaterFloodFill(clickPos, activeMode, activeProfile, waterHeight, EditorMirrorMode, isRemoveAction);
		if (result.BeforeCells == null || result.AfterCells == null) return;

		GroundTerrain.UpdateMeshAndPhysics(rebuildPhysics: false, rebuildNavMesh: true, affectedRegions: null, rebuildWater: true);

		var action = new TerrainModifyAction(result.BeforeCells, result.AfterCells, null, null, result.BeforePathing, result.AfterPathing);
		EditorHistoryManager.RecordAction(action);
		EditorHasUnsavedChanges = true;

		string statusMsg = result.WasAdded ? "Water added via flood fill" : "Water removed via flood fill";
		MapEditorHUD.Instance?.ShowFeedbackExternal(statusMsg);
	}

	private readonly List<MeshInstance3D> _symmetryHighlightMeshes = new();

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

	private MeshInstance3D GetOrCreateSymmetryHighlightMesh(int index)
	{
		while (_symmetryHighlightMeshes.Count <= index)
		{
			var meshInst = new MeshInstance3D();
			meshInst.Name = $"SymmetrySelectionHighlight_{_symmetryHighlightMeshes.Count}";
			var mat = new StandardMaterial3D();
			mat.AlbedoColor = new Color(0.0f, 0.6f, 1.0f, 0.35f);
			mat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
			mat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
			mat.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
			meshInst.MaterialOverride = mat;
			AddChild(meshInst);
			meshInst.Visible = false;
			_symmetryHighlightMeshes.Add(meshInst);
		}
		return _symmetryHighlightMeshes[index];
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

	private void CreateSelectionHighlight()
	{
		if (_selectionHighlightMesh != null) return;
		_selectionHighlightMesh = new MeshInstance3D();
		_selectionHighlightMesh.Name = "SelectionHighlight";
		var mat = new StandardMaterial3D();
		mat.AlbedoColor = new Color(0.0f, 0.6f, 1.0f, 0.35f);
		mat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
		mat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
		mat.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
		_selectionHighlightMesh.MaterialOverride = mat;
		AddChild(_selectionHighlightMesh);
		_selectionHighlightMesh.Visible = false;
	}

	private int _lastSelectionMinX = -1;
	private int _lastSelectionMinZ = -1;
	private int _lastSelectionMaxX = -1;
	private int _lastSelectionMaxZ = -1;

	public void InvalidateSelectionHighlightMesh()
	{
		_lastSelectionMinX = -1;
		_lastSelectionMinZ = -1;
		_lastSelectionMaxX = -1;
		_lastSelectionMaxZ = -1;
	}

	private bool _lastSelectionBrushIsSquare = true;

	public void RebuildSelectionHighlightMeshExternal(int minX, int minZ, int maxX, int maxZ)
	{
		_lastSelectionMinX = -1;
		if (ActiveEditorTool == EditorTool.PasteArea && _editorService.HasCopiedArea)
		{
			int w = maxX - minX + 1;
			int d = maxZ - minZ + 1;
			UpdatePasteSelectionHighlights(minX, minZ, w, d);
		}
		else
		{
			RebuildSelectionHighlightMesh(minX, minZ, maxX, maxZ);
		}
	}

	private void BuildHighlightMeshForBounds(MeshInstance3D meshInst, int minX, int minZ, int maxX, int maxZ)
	{
		if (meshInst == null || GroundTerrain == null || GroundTerrain.Cells == null) return;
		int selWidth = maxX - minX + 1;
		int selDepth = maxZ - minZ + 1;
		if (selWidth < 2 || selDepth < 2)
		{
			meshInst.Visible = false;
			return;
		}

		int vertexCount = selWidth * selDepth;
		var vertices = new Vector3[vertexCount];
		int width = GroundTerrain.Width;
		int depth = GroundTerrain.Depth;
		float quadSize = GroundTerrain.QuadSize;
		var cells = GroundTerrain.Cells;
		float halfW = width * 0.5f;
		float halfD = depth * 0.5f;

		for (int sz = 0; sz < selDepth; sz++)
		{
			int mapZ = minZ + sz;
			float lz = (mapZ - halfD) * quadSize;
			int rowOffset = sz * selWidth;
			for (int sx = 0; sx < selWidth; sx++)
			{
				int mapX = minX + sx;
				int idx = rowOffset + sx;
				float lx = (mapX - halfW) * quadSize;
				float h = EditableTerrain.GetGridNodeHeight(mapX, mapZ, cells, width, depth);
				vertices[idx] = new Vector3(lx, h + 0.05f, lz);
			}
		}
		int cellWidth = selWidth - 1;
		int cellDepth = selDepth - 1;
		int indexCount = cellWidth * cellDepth * 6;
		var indices = new int[indexCount];
		int iIdx = 0;

		float selCenterX = (minX + maxX) * 0.5f;
		float selCenterZ = (minZ + maxZ) * 0.5f;
		float rx = Math.Max(0.5f, (maxX - minX) * 0.5f);
		float rz = Math.Max(0.5f, (maxZ - minZ) * 0.5f);

		for (int sz = 0; sz < cellDepth; sz++)
		{
			int row0 = sz * selWidth;
			int row1 = (sz + 1) * selWidth;
			for (int sx = 0; sx < cellWidth; sx++)
			{
				bool includeQuad = true;
				if (ActiveEditorTool == EditorTool.SelectArea)
				{
					if (!EditorBrushIsSquare)
					{
						float cellCenterX = minX + sx + 0.5f;
						float cellCenterZ = minZ + sz + 0.5f;
						float ndx = (cellCenterX - selCenterX) / rx;
						float ndz = (cellCenterZ - selCenterZ) / rz;
						includeQuad = (ndx * ndx + ndz * ndz <= 1.05f);
					}
				}
				else if (ActiveEditorTool == EditorTool.PasteArea)
				{
					if (_editorService.HasCopiedArea && _editorService.HasCopiedAreaMask)
					{
						int rotX = sx;
						int rotZ = sz;
						float r = EditorPasteRotation % 360.0f;
						if (r < 0) r += 360.0f;
						int rotSteps = (int)Math.Round(r / 90.0f) % 4;
						int pasteWidth = _editorService.CopiedAreaWidth;
						int pasteDepth = _editorService.CopiedAreaDepth;

						int origSx = sx;
						int origSz = sz;
						if (rotSteps == 1)
						{
							origSx = sz;
							origSz = pasteDepth - 1 - sx;
						}
						else if (rotSteps == 2)
						{
							origSx = pasteWidth - 1 - sx;
							origSz = pasteDepth - 1 - sz;
						}
						else if (rotSteps == 3)
						{
							origSx = pasteWidth - 1 - sz;
							origSz = sx;
						}

						int srcX = origSx;
						int srcZ = origSz;
						if (EditorPasteReflection == PasteReflection.Horizontal)
						{
							srcX = pasteWidth - 1 - origSx;
						}
						else if (EditorPasteReflection == PasteReflection.Vertical)
						{
							srcZ = pasteDepth - 1 - origSz;
						}

						includeQuad = _editorService.IsCopiedCellMasked(srcX, srcZ);
					}
					else if (!EditorBrushIsSquare && !_editorService.HasCopiedArea)
					{
						float cellCenterX = minX + sx + 0.5f;
						float cellCenterZ = minZ + sz + 0.5f;
						float ndx = (cellCenterX - selCenterX) / rx;
						float ndz = (cellCenterZ - selCenterZ) / rz;
						includeQuad = (ndx * ndx + ndz * ndz <= 1.05f);
					}
				}

				if (includeQuad)
				{
					int v00 = row0 + sx;
					int v10 = row0 + (sx + 1);
					int v01 = row1 + sx;
					int v11 = row1 + (sx + 1);
					indices[iIdx++] = v00;
					indices[iIdx++] = v10;
					indices[iIdx++] = v01;
					indices[iIdx++] = v10;
					indices[iIdx++] = v11;
					indices[iIdx++] = v01;
				}
			}
		}
		if (iIdx < indexCount)
		{
			System.Array.Resize(ref indices, iIdx);
		}
		var arrays = new Godot.Collections.Array();
		arrays.Resize((int)Mesh.ArrayType.Max);
		arrays[(int)Mesh.ArrayType.Vertex] = vertices;
		arrays[(int)Mesh.ArrayType.Index] = indices;
		var arrayMesh = new ArrayMesh();
		arrayMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
		meshInst.Mesh = arrayMesh;
		meshInst.Visible = true;
	}

	private void RebuildSelectionHighlightMesh(int minX, int minZ, int maxX, int maxZ)
	{
		if (_selectionHighlightMesh == null || GroundTerrain == null || GroundTerrain.Cells == null) return;
		int selWidth = maxX - minX + 1;
		int selDepth = maxZ - minZ + 1;
		if (selWidth < 2 || selDepth < 2)
		{
			_selectionHighlightMesh.Visible = false;
			_lastSelectionMinX = -1;
			return;
		}

		if (minX == _lastSelectionMinX && minZ == _lastSelectionMinZ && maxX == _lastSelectionMaxX && maxZ == _lastSelectionMaxZ && _lastSelectionBrushIsSquare == EditorBrushIsSquare && _selectionHighlightMesh.Visible)
		{
			return;
		}
		_lastSelectionMinX = minX;
		_lastSelectionMinZ = minZ;
		_lastSelectionMaxX = maxX;
		_lastSelectionMaxZ = maxZ;
		_lastSelectionBrushIsSquare = EditorBrushIsSquare;

		BuildHighlightMeshForBounds(_selectionHighlightMesh, minX, minZ, maxX, maxZ);
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

	public void HideCoordinatePreviewMesh()
	{
		if (_coordinatePreviewMesh != null) _coordinatePreviewMesh.Visible = false;
	}

	private void RebuildCoordinateMeshInstance(MeshInstance3D meshInstance, int minX, int minZ, int maxX, int maxZ, Color color, float yOffset = 0.15f)
	{
		if (meshInstance == null || GroundTerrain == null || GroundTerrain.Cells == null) return;
		int selWidth = maxX - minX + 1;
		int selDepth = maxZ - minZ + 1;
		if (selWidth < 2 || selDepth < 2)
		{
			meshInstance.Visible = false;
			return;
		}
		int vertexCount = selWidth * selDepth;
		var vertices = new Vector3[vertexCount];
		int width = GroundTerrain.Width;
		int depth = GroundTerrain.Depth;
		float quadSize = GroundTerrain.QuadSize;
		var cells = GroundTerrain.Cells;
		float halfW = width * 0.5f;
		float halfD = depth * 0.5f;

		for (int sz = 0; sz < selDepth; sz++)
		{
			int mapZ = minZ + sz;
			float lz = (mapZ - halfD) * quadSize;
			int rowOffset = sz * selWidth;
			for (int sx = 0; sx < selWidth; sx++)
			{
				int mapX = minX + sx;
				int idx = rowOffset + sx;
				float lx = (mapX - halfW) * quadSize;
				float h = EditableTerrain.GetGridNodeHeight(mapX, mapZ, cells, width, depth);
				vertices[idx] = new Vector3(lx, h + yOffset, lz);
			}
		}
		int cellWidth = selWidth - 1;
		int cellDepth = selDepth - 1;
		int indexCount = cellWidth * cellDepth * 6;
		var indices = new int[indexCount];
		var colors = new Color[vertexCount];
		for (int i = 0; i < vertexCount; i++) colors[i] = color;
		int iIdx = 0;
		for (int sz = 0; sz < cellDepth; sz++)
		{
			int row0 = sz * selWidth;
			int row1 = (sz + 1) * selWidth;
			for (int sx = 0; sx < cellWidth; sx++)
			{
				int v00 = row0 + sx;
				int v10 = row0 + (sx + 1);
				int v01 = row1 + sx;
				int v11 = row1 + (sx + 1);
				indices[iIdx++] = v00;
				indices[iIdx++] = v10;
				indices[iIdx++] = v01;
				indices[iIdx++] = v10;
				indices[iIdx++] = v11;
				indices[iIdx++] = v01;
			}
		}
		var arrays = new Godot.Collections.Array();
		arrays.Resize((int)Mesh.ArrayType.Max);
		arrays[(int)Mesh.ArrayType.Vertex] = vertices;
		arrays[(int)Mesh.ArrayType.Index] = indices;
		arrays[(int)Mesh.ArrayType.Color] = colors;
		var arrayMesh = new ArrayMesh();
		arrayMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
		meshInstance.Mesh = arrayMesh;
		meshInstance.Visible = true;
	}

	public bool CommitCoordinateExternal(string coordinateName, int minX, int minZ, int maxX, int maxZ)
	{
		if (GroundTerrain == null) return false;
		string safeName = coordinateName.Trim();
		if (string.IsNullOrEmpty(safeName)) return false;

		var oldCoordinates = new List<EditorCoordinate>(EditorCoordinates);

		int width = GroundTerrain.Width;
		int depth = GroundTerrain.Depth;
		float quadSize = GroundTerrain.QuadSize;

		float worldMinX = (minX - width / 2.0f) * quadSize;
		float worldMinZ = (minZ - depth / 2.0f) * quadSize;
		float worldMaxX = (maxX - width / 2.0f) * quadSize;
		float worldMaxZ = (maxZ - depth / 2.0f) * quadSize;

		bool committed = false;
		for (int i = 0; i < EditorCoordinates.Count; i++)
		{
			if (EditorCoordinates[i].Name == safeName)
			{
				EditorCoordinates[i] = new EditorCoordinate { Name = safeName, MinX = worldMinX, MinZ = worldMinZ, MaxX = worldMaxX, MaxZ = worldMaxZ };
				committed = true;
				break;
			}
		}

		if (!committed)
		{
			EditorCoordinates.Add(new EditorCoordinate { Name = safeName, MinX = worldMinX, MinZ = worldMinZ, MaxX = worldMaxX, MaxZ = worldMaxZ });
		}

		RebuildAllCoordinatePersistentMeshes();

		var newCoordinates = new List<EditorCoordinate>(EditorCoordinates);
		var action = new CoordinateAction(oldCoordinates, newCoordinates);
		EditorHistoryManager.RecordAction(action);
		EditorHasUnsavedChanges = true;

		return true;
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
		if (string.IsNullOrEmpty(coordinateName))
		{
			HideCoordinateSelectionOutline();
			return;
		}

		if (GroundTerrain == null || GroundTerrain.Cells == null) return;

		EditorCoordinate? found = null;
		foreach (var r in EditorCoordinates)
		{
			if (string.Equals(r.Name, coordinateName, StringComparison.OrdinalIgnoreCase))
			{
				found = r;
				break;
			}
		}

		if (found == null)
		{
			HideCoordinateSelectionOutline();
			return;
		}

		var coord = found.Value;

		if (_coordinateSelectionOutlineMesh == null)
		{
			_coordinateSelectionOutlineMesh = new MeshInstance3D();
			_coordinateSelectionOutlineMesh.Name = "CoordinateSelectionOutline";
			var mat = new StandardMaterial3D();
			mat.AlbedoColor = new Color(1.0f, 0.6f, 0.0f, 0.45f);
			mat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
			mat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
			mat.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
			_coordinateSelectionOutlineMesh.MaterialOverride = mat;
			AddChild(_coordinateSelectionOutlineMesh);
		}

		int width = GroundTerrain.Width;
		int depth = GroundTerrain.Depth;
		float quadSize = GroundTerrain.QuadSize;

		int minX = Mathf.Clamp((int)Mathf.Round(coord.MinX / quadSize + width / 2.0f), 0, width);
		int minZ = Mathf.Clamp((int)Mathf.Round(coord.MinZ / quadSize + depth / 2.0f), 0, depth);
		int maxX = Mathf.Clamp((int)Mathf.Round(coord.MaxX / quadSize + width / 2.0f), 0, width);
		int maxZ = Mathf.Clamp((int)Mathf.Round(coord.MaxZ / quadSize + depth / 2.0f), 0, depth);

		_coordinateSelectionOutlineMesh.Visible = true;
		RebuildCoordinateMeshInstance(_coordinateSelectionOutlineMesh, minX, minZ, maxX, maxZ, new Color(1.0f, 0.6f, 0.0f, 0.45f), 0.25f);

		float centerX = (coord.MinX + coord.MaxX) / 2.0f;
		float centerZ = (coord.MinZ + coord.MaxZ) / 2.0f;
		float centerY = GetTerrainHeightAt(new Vector3(centerX, 0f, centerZ));

		var camera = (MainCamera ?? GetViewport()?.GetCamera3D()) as CameraControl;
		if (camera != null)
		{
			camera.FocusOnPosition(new Vector3(centerX, centerY, centerZ));
		}
	}

	public void HideCoordinateSelectionOutline()
	{
		if (_coordinateSelectionOutlineMesh != null && GodotObject.IsInstanceValid(_coordinateSelectionOutlineMesh))
		{
			_coordinateSelectionOutlineMesh.Visible = false;
			_coordinateSelectionOutlineMesh.Mesh = null;
		}
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

	public void PerformCopyAreaExternal()
	{
		if (GroundTerrain == null || _editorService.SelectionStart == null || _editorService.SelectionEnd == null)
		{
			MapEditorHUD.Instance?.ShowFeedbackExternal("Nothing to Copy (select an area first)");
			return;
		}

		PerformCopyArea();
		MapEditorHUD.Instance?.ShowFeedbackExternal("Area Copied");
	}

	public void PerformCutAreaExternal()
	{
		if (GroundTerrain == null || _editorService.SelectionStart == null || _editorService.SelectionEnd == null)
		{
			MapEditorHUD.Instance?.ShowFeedbackExternal("Nothing to Cut (select an area first)");
			return;
		}

		PerformCopyArea();
		PerformEraseArea();

		var (minX, minZ, maxX, maxZ) = _editorService.GetCurrentSelectionBounds();
		if (_selectionHighlightMesh != null && _selectionHighlightMesh.Visible)
		{
			RebuildSelectionHighlightMesh(minX, minZ, maxX, maxZ);
		}

		MapEditorHUD.Instance?.ShowFeedbackExternal("Area Cut");
	}

	private List<IEditorAction> PerformEraseArea(bool recordToHistory = true)
	{
		if (GroundTerrain == null || GroundTerrain.Cells == null || GroundTerrain.SplatMap == null || _editorService.SelectionStart == null || _editorService.SelectionEnd == null)
		{
			MapEditorHUD.Instance?.ShowFeedbackExternal("Nothing to Erase (select an area first)");
			return new List<IEditorAction>();
		}

		var (minX, minZ, maxX, maxZ) = _editorService.GetCurrentSelectionBounds();

		var cellsBefore = (Realm.Ecs.Components.Terrain.TerrainCell[,])GroundTerrain.Cells.Clone();
		var splatBefore = (TerrainSplatWeights[,])GroundTerrain.SplatMap.Clone();
		var cliffBefore = GroundTerrain.CliffSplatMap != null ? (TerrainSplatWeights[,])GroundTerrain.CliffSplatMap.Clone() : null;
		var pathingBefore = (int[,])GroundTerrain.PathingCodes.Clone();

		var node3Ds = new List<Node3D>();
		foreach (var child in GetChildren())
		{
			if (child is Node3D n3d) node3Ds.Add(n3d);
		}

		var eraseResult = _editorService.BuildEraseAreaResult(
			minX, minZ, maxX, maxZ,
			PasteOptionHeights, PasteOptionTextures, PasteOptionEntities, PasteOptionPathing,
			node3Ds, _editorPreviewNode as Node3D, EditorBrushIsSquare);

		if (eraseResult.TerrainModified)
		{
			Rect2I affected = new Rect2I(minX - 2, minZ - 2, maxX - minX + 4, maxZ - minZ + 4);
			if (eraseResult.HeightsModified)
			{
				AlignAllEntitiesToTerrain(affected);
			}
			GroundTerrain.UpdateMeshAndPhysics(eraseResult.HeightsModified, false, affected, eraseResult.HeightsModified);
			if (eraseResult.PathingModified)
			{
				UpdatePathingOverlay();
			}
		}

		var deleteActions = new List<IEditorAction>();
		foreach (var node in eraseResult.NodesToDelete)
		{
			var act = DeleteObjectAtWithUndo(node, node.Position);
			if (act != null) deleteActions.Add(act);
		}

		var cellsAfter = (Realm.Ecs.Components.Terrain.TerrainCell[,])GroundTerrain.Cells.Clone();
		var splatAfter = (TerrainSplatWeights[,])GroundTerrain.SplatMap.Clone();
		var cliffAfter = GroundTerrain.CliffSplatMap != null ? (TerrainSplatWeights[,])GroundTerrain.CliffSplatMap.Clone() : null;
		var pathingAfter = (int[,])GroundTerrain.PathingCodes.Clone();
		var actions = new List<IEditorAction>();
		if (eraseResult.TerrainModified)
		{
			actions.Add(new TerrainModifyAction(cellsBefore, cellsAfter, splatBefore, splatAfter, pathingBefore, pathingAfter, cliffBefore, cliffAfter));
		}
		if (deleteActions.Count > 0)
		{
			actions.AddRange(deleteActions);
		}

		if (actions.Count > 0)
		{
			if (recordToHistory)
			{
				var composite = new CompositeAction(actions);
				EditorHistoryManager.RecordAction(composite);
				EditorHasUnsavedChanges = true;
				MapEditorHUD.Instance?.ShowFeedbackExternal("Area Erased");
			}
		}
		return actions;
	}

	private List<IEditorAction> PerformPasteArea(int startX, int startZ, float rotationDegrees, PasteReflection reflection = PasteReflection.None, bool recordToHistory = true)
	{
		if (GroundTerrain == null || GroundTerrain.Cells == null || GroundTerrain.SplatMap == null || !_editorService.HasCopiedArea) return new List<IEditorAction>();

		var cellsBefore = (Realm.Ecs.Components.Terrain.TerrainCell[,])GroundTerrain.Cells.Clone();
		var splatBefore = (TerrainSplatWeights[,])GroundTerrain.SplatMap.Clone();
		var cliffBefore = GroundTerrain.CliffSplatMap != null ? (TerrainSplatWeights[,])GroundTerrain.CliffSplatMap.Clone() : null;
		var pathingBefore = (int[,])GroundTerrain.PathingCodes.Clone();

		var pasteResult = _editorService.BuildPasteAreaResult(
			startX, startZ,
			PasteOptionHeights, PasteOptionTextures, PasteOptionEntities, PasteOptionPathing,
			EditorMirrorMode,
			rotationDegrees,
			reflection);

		if (pasteResult.TerrainModified)
		{
			int pasteW = Math.Max(_editorService.CopiedAreaWidth, _editorService.CopiedAreaDepth);
			int pasteD = pasteW;
			var affectedRegions = new List<Rect2I>();
			affectedRegions.Add(new Rect2I(startX - 2, startZ - 2, pasteW + 4, pasteD + 4));

			int width = GroundTerrain.Width;
			int depth = GroundTerrain.Depth;
			if (EditorMirrorMode != MirrorMode.None)
			{
				float quadSize = GroundTerrain.QuadSize;
				Vector3 centerPos = new Vector3((startX + pasteW / 2.0f - width / 2.0f) * quadSize, 0, (startZ + pasteD / 2.0f - depth / 2.0f) * quadSize);
				var transforms = _editorService.GetMirroredTransforms(centerPos, 0.0f, EditorMirrorMode);
				foreach (var t in transforms)
				{
					var (rcx, rcz) = _editorService.WorldPosToCellCoords(t.Position);
					affectedRegions.Add(new Rect2I(rcx - pasteW / 2 - 2, rcz - pasteD / 2 - 2, pasteW + 4, pasteD + 4));
				}
			}

			if (pasteResult.HeightsModified)
			{
				foreach (var aff in affectedRegions)
				{
					AlignAllEntitiesToTerrain(aff);
				}
			}
			GroundTerrain.UpdateMeshAndPhysics(pasteResult.HeightsModified, false, affectedRegions, pasteResult.HeightsModified);
			if (pasteResult.PathingModified)
			{
				UpdatePathingOverlay();
			}
		}

		var spawnActions = new List<IEditorAction>();
		foreach (var req in pasteResult.SpawnRequests)
		{
			Node pastedNode = null;
			if (req.Type == "unit")
				pastedNode = SpawnUnitExternal(req.Id, req.Position, req.IsEnemy, req.Rotation, req.Scale);
			else if (req.Type == "prop")
				pastedNode = SpawnPropExternalWithParams(req.Id, req.Position, req.Rotation, req.Scale);
			else if (req.Type == "decal")
				pastedNode = SpawnDecalExternalWithParams(req.Id, req.Position, req.Rotation, req.Scale);

			if (pastedNode != null)
			{
				spawnActions.Add(new ObjectSpawnAction(req.Type, req.Id, req.Position, req.Rotation, req.Scale, req.IsEnemy, pastedNode));
			}
		}

		var cellsAfter = (Realm.Ecs.Components.Terrain.TerrainCell[,])GroundTerrain.Cells.Clone();
		var splatAfter = (TerrainSplatWeights[,])GroundTerrain.SplatMap.Clone();
		var cliffAfter = GroundTerrain.CliffSplatMap != null ? (TerrainSplatWeights[,])GroundTerrain.CliffSplatMap.Clone() : null;
		var pathingAfter = (int[,])GroundTerrain.PathingCodes.Clone();
		var actions = new List<IEditorAction>();
		if (pasteResult.TerrainModified)
		{
			actions.Add(new TerrainModifyAction(cellsBefore, cellsAfter, splatBefore, splatAfter, pathingBefore, pathingAfter, cliffBefore, cliffAfter));
		}
		if (spawnActions.Count > 0)
		{
			actions.AddRange(spawnActions);
		}
		if (actions.Count > 0)
		{
			if (recordToHistory)
			{
				var composite = new CompositeAction(actions);
				EditorHistoryManager.RecordAction(composite);
				EditorHasUnsavedChanges = true;
				MapEditorHUD.Instance?.ShowFeedbackExternal("Pasted Area");
			}
		}
		return actions;
	}

	public void UpdateCameraBoundsOverlayVisibility()
	{
		if (_cameraBoundsOverlayMesh == null) return;
		_cameraBoundsOverlayMesh.Visible = IsMapEditorMode && EditorCameraBoundsVisible;
		if (_cameraBoundsOverlayMesh.Visible)
		{
			RebuildCameraBoundsOverlay();
		}
	}

	public void UpdatePathingOverlay()
	{
		bool isPathingTool = ActiveEditorTool == EditorTool.PaintPathing || ActiveEditorTool == EditorTool.FloodFillPathing;
		bool isClipboardTool = ActiveEditorTool == EditorTool.SelectArea || ActiveEditorTool == EditorTool.PasteArea;

		bool shouldBeVisible = IsMapEditorMode && PathingOverlayVisible && (isPathingTool || (isClipboardTool && PasteOptionPathing));
		
		if (GroundTerrain != null)
		{
			GroundTerrain.SetPathingVisible(shouldBeVisible);
			if (shouldBeVisible)
			{
				RebuildPathingOverlay();
			}
		}
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

	private bool _wasSelectionHighlightVisible;
	private readonly List<bool> _wasSymmetryHighlightsVisible = new();
	private bool _wasCameraBoundsVisible;
	private bool _wasMeasureMeshVisible;
	private bool _wasSymmetryPivotVisible;
	private bool _wasCoordinatePreviewVisible;
	private bool _wasCoordinateOutlineVisible;
	private bool _wasScaleSilhouetteVisible;
	private bool _wasCoverageOverlayVisible;

	public void BeginMinimapCapture()
	{
		_wasSelectionHighlightVisible = _selectionHighlightMesh != null && GodotObject.IsInstanceValid(_selectionHighlightMesh) && _selectionHighlightMesh.Visible;
		if (_selectionHighlightMesh != null && GodotObject.IsInstanceValid(_selectionHighlightMesh))
		{
			_selectionHighlightMesh.Visible = false;
		}

		_wasSymmetryHighlightsVisible.Clear();
		foreach (var symMesh in _symmetryHighlightMeshes)
		{
			bool vis = symMesh != null && GodotObject.IsInstanceValid(symMesh) && symMesh.Visible;
			_wasSymmetryHighlightsVisible.Add(vis);
			if (vis)
			{
				symMesh.Visible = false;
			}
		}

		_wasCameraBoundsVisible = _cameraBoundsOverlayMesh != null && GodotObject.IsInstanceValid(_cameraBoundsOverlayMesh) && _cameraBoundsOverlayMesh.Visible;
		if (_cameraBoundsOverlayMesh != null && GodotObject.IsInstanceValid(_cameraBoundsOverlayMesh))
		{
			_cameraBoundsOverlayMesh.Visible = false;
		}

		_wasMeasureMeshVisible = _measureMeshInstance != null && GodotObject.IsInstanceValid(_measureMeshInstance) && _measureMeshInstance.Visible;
		if (_measureMeshInstance != null && GodotObject.IsInstanceValid(_measureMeshInstance))
		{
			_measureMeshInstance.Visible = false;
		}

		_wasSymmetryPivotVisible = _symmetryPivotMarkerMesh != null && GodotObject.IsInstanceValid(_symmetryPivotMarkerMesh) && _symmetryPivotMarkerMesh.Visible;
		if (_symmetryPivotMarkerMesh != null && GodotObject.IsInstanceValid(_symmetryPivotMarkerMesh))
		{
			_symmetryPivotMarkerMesh.Visible = false;
		}

		_wasCoordinatePreviewVisible = _coordinatePreviewMesh != null && GodotObject.IsInstanceValid(_coordinatePreviewMesh) && _coordinatePreviewMesh.Visible;
		if (_coordinatePreviewMesh != null && GodotObject.IsInstanceValid(_coordinatePreviewMesh))
		{
			_coordinatePreviewMesh.Visible = false;
		}

		_wasCoordinateOutlineVisible = _coordinateSelectionOutlineMesh != null && GodotObject.IsInstanceValid(_coordinateSelectionOutlineMesh) && _coordinateSelectionOutlineMesh.Visible;
		if (_coordinateSelectionOutlineMesh != null && GodotObject.IsInstanceValid(_coordinateSelectionOutlineMesh))
		{
			_coordinateSelectionOutlineMesh.Visible = false;
		}

		_wasScaleSilhouetteVisible = _scaleMapSilhouetteMesh != null && GodotObject.IsInstanceValid(_scaleMapSilhouetteMesh) && _scaleMapSilhouetteMesh.Visible;
		if (_scaleMapSilhouetteMesh != null && GodotObject.IsInstanceValid(_scaleMapSilhouetteMesh))
		{
			_scaleMapSilhouetteMesh.Visible = false;
		}

		_wasCoverageOverlayVisible = _editorCoverageOverlayRoot != null && GodotObject.IsInstanceValid(_editorCoverageOverlayRoot) && _editorCoverageOverlayRoot.Visible;
		if (_editorCoverageOverlayRoot != null && GodotObject.IsInstanceValid(_editorCoverageOverlayRoot))
		{
			_editorCoverageOverlayRoot.Visible = false;
		}
	}

	public void EndMinimapCapture()
	{
		if (_selectionHighlightMesh != null && GodotObject.IsInstanceValid(_selectionHighlightMesh))
		{
			_selectionHighlightMesh.Visible = _wasSelectionHighlightVisible;
		}

		for (int i = 0; i < _wasSymmetryHighlightsVisible.Count && i < _symmetryHighlightMeshes.Count; i++)
		{
			var symMesh = _symmetryHighlightMeshes[i];
			if (symMesh != null && GodotObject.IsInstanceValid(symMesh))
			{
				symMesh.Visible = _wasSymmetryHighlightsVisible[i];
			}
		}

		if (_cameraBoundsOverlayMesh != null && GodotObject.IsInstanceValid(_cameraBoundsOverlayMesh))
		{
			_cameraBoundsOverlayMesh.Visible = _wasCameraBoundsVisible;
		}

		if (_measureMeshInstance != null && GodotObject.IsInstanceValid(_measureMeshInstance))
		{
			_measureMeshInstance.Visible = _wasMeasureMeshVisible;
		}

		if (_symmetryPivotMarkerMesh != null && GodotObject.IsInstanceValid(_symmetryPivotMarkerMesh))
		{
			_symmetryPivotMarkerMesh.Visible = _wasSymmetryPivotVisible;
		}

		if (_coordinatePreviewMesh != null && GodotObject.IsInstanceValid(_coordinatePreviewMesh))
		{
			_coordinatePreviewMesh.Visible = _wasCoordinatePreviewVisible;
		}

		if (_coordinateSelectionOutlineMesh != null && GodotObject.IsInstanceValid(_coordinateSelectionOutlineMesh))
		{
			_coordinateSelectionOutlineMesh.Visible = _wasCoordinateOutlineVisible;
		}

		if (_scaleMapSilhouetteMesh != null && GodotObject.IsInstanceValid(_scaleMapSilhouetteMesh))
		{
			_scaleMapSilhouetteMesh.Visible = _wasScaleSilhouetteVisible;
		}

		if (_editorCoverageOverlayRoot != null && GodotObject.IsInstanceValid(_editorCoverageOverlayRoot))
		{
			_editorCoverageOverlayRoot.Visible = _wasCoverageOverlayVisible;
		}
	}
}
