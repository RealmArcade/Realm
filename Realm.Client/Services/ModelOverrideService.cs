using System;
using System.Collections.Generic;
using Arch.Core;
using Godot;
using Realm.Client.Core;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Services;

namespace Realm.Client.Services;

public class ModelOverrideService
{
	public float GetModelCollisionCircleRatio(string primaryKey, string assetKey, Func<string, string> normalizeAssetKeyFunc)
	{
		string normPrimary = normalizeAssetKeyFunc(primaryKey);
		if (!string.IsNullOrEmpty(normPrimary) && ModelCollisionCircleRatios.TryGetValue(normPrimary, out float val1))
			return val1;

		string normAsset = normalizeAssetKeyFunc(assetKey);
		if (!string.IsNullOrEmpty(normAsset) && ModelCollisionCircleRatios.TryGetValue(normAsset, out float val2))
			return val2;

		return GameContext.RegistryService.GetRegistryCollisionCircleRatio(primaryKey);
	}

	public void UpdateMetadataModelOverrides(MapMetadata meta)
	{
		if (meta.Models == null) meta.Models = new Dictionary<string, ModelMetadata>(StringComparer.OrdinalIgnoreCase);

		var allKeys = CollectAllModelKeys();
		foreach (var key in allKeys)
		{
			UpdateSingleModelMetadata(meta.Models, key);
		}
	}

	private HashSet<string> CollectAllModelKeys()
	{
		var allKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		allKeys.UnionWith(ModelYOffsets.Keys);
		allKeys.UnionWith(ModelScales.Keys);
		allKeys.UnionWith(ModelCollisionCircleRatios.Keys);
		allKeys.UnionWith(ModelObstacleRadii.Keys);
		allKeys.UnionWith(ModelBrightness.Keys);
		allKeys.UnionWith(ModelColorTint.Keys);
		allKeys.UnionWith(ModelDespillPlayerColor.Keys);
		allKeys.UnionWith(ModelNormalizeLuminance.Keys);
		allKeys.UnionWith(ModelIgnorePlayerColor.Keys);
		allKeys.UnionWith(ModelSpawnShaders.Keys);
		allKeys.UnionWith(ModelDeathShaders.Keys);
		allKeys.UnionWith(ModelProceduralAnimations.Keys);
		allKeys.UnionWith(ModelEnableProceduralAnimations.Keys);
		return allKeys;
	}

	private void UpdateSingleModelMetadata(Dictionary<string, ModelMetadata> models, string key)
	{
		if (!models.TryGetValue(key, out var modelMeta) || modelMeta == null)
		{
			modelMeta = new ModelMetadata();
			models[key] = modelMeta;
		}

		UpdateModelMetricsMetadata(modelMeta, key);
		UpdateModelVisualMetadata(modelMeta, key);
		UpdateModelShaderMetadata(modelMeta, key);
	}

	private void UpdateModelMetricsMetadata(ModelMetadata meta, string key)
	{
		if (ModelYOffsets.TryGetValue(key, out float yVal)) meta.Offsets = yVal;
		if (ModelScales.TryGetValue(key, out float sVal)) meta.Scales = sVal;
		if (ModelCollisionCircleRatios.TryGetValue(key, out float cVal)) meta.CollisionCircleRatios = cVal;
		if (ModelObstacleRadii.TryGetValue(key, out float rVal)) meta.ObstacleRadii = rVal;
	}

	private void UpdateModelVisualMetadata(ModelMetadata meta, string key)
	{
		if (ModelBrightness.TryGetValue(key, out float bVal)) meta.Brightness = bVal;
		if (ModelColorTint.TryGetValue(key, out Color tVal)) meta.ColorTint = $"#{tVal.ToHtml(false)}";
		if (ModelDespillPlayerColor.TryGetValue(key, out bool dVal)) meta.DespillPlayerColor = dVal;
		if (ModelNormalizeLuminance.TryGetValue(key, out bool nVal)) meta.NormalizeLuminance = nVal;
		if (ModelIgnorePlayerColor.TryGetValue(key, out bool iVal)) meta.IgnorePlayerColor = iVal;
	}

	private void UpdateModelShaderMetadata(ModelMetadata meta, string key)
	{
		if (ModelSpawnShaders.TryGetValue(key, out string ssVal) && !string.IsNullOrWhiteSpace(ssVal)) meta.SpawnShaders = ssVal;
		if (ModelDeathShaders.TryGetValue(key, out string dsVal) && !string.IsNullOrWhiteSpace(dsVal)) meta.DeathShaders = dsVal;
		if (ModelProceduralAnimations.TryGetValue(key, out string paVal) && !string.IsNullOrWhiteSpace(paVal)) meta.ProceduralAnimation = paVal;
		if (ModelEnableProceduralAnimations.TryGetValue(key, out bool epVal)) meta.EnableProceduralAnimation = epVal;
	}

	public Color GetModelColorTint(string primaryKey, string assetKey, Func<string, string> normalizeAssetKeyFunc)
	{
		if (TryGetModelColorTint(primaryKey, normalizeAssetKeyFunc, out Color c1)) return c1;
		if (TryGetModelColorTint(assetKey, normalizeAssetKeyFunc, out Color c2)) return c2;
		if (string.IsNullOrEmpty(primaryKey)) return new Color(1.0f, 1.0f, 1.0f);
		if (GameContext.RegistryService.TryGetRegistryColorTint(primaryKey, out Color c3)) return c3;
		return new Color(1.0f, 1.0f, 1.0f);
	}

	public float GetModelBrightness(string primaryKey, string assetKey, Func<string, string> normalizeAssetKeyFunc)
	{
		if (TryGetModelBrightness(primaryKey, normalizeAssetKeyFunc, out float val1)) return Mathf.Clamp(val1, 0.10f, 1.75f);
		if (TryGetModelBrightness(assetKey, normalizeAssetKeyFunc, out float val2)) return Mathf.Clamp(val2, 0.10f, 1.75f);
		if (string.IsNullOrEmpty(primaryKey)) return 0.5f;
		if (GameContext.RegistryService.TryGetRegistryBrightness(primaryKey, out float val3)) return Mathf.Clamp(val3, 0.10f, 2.0f);
		return 0.5f;
	}

	public bool TryGetModelBrightness(string key, Func<string, string> normalizeAssetKeyFunc, out float brightness)
	{
		brightness = 0f;
		string norm = normalizeAssetKeyFunc(key);
		return !string.IsNullOrEmpty(norm) && ModelBrightness.TryGetValue(norm, out brightness);
	}

	public bool TryGetModelColorTint(string key, Func<string, string> normalizeAssetKeyFunc, out Color color)
	{
		color = default;
		string norm = normalizeAssetKeyFunc(key);
		return !string.IsNullOrEmpty(norm) && ModelColorTint.TryGetValue(norm, out color);
	}

	public bool GetModelDespillPlayerColor(string primaryKey, string assetKey, Func<string, string> normalizeAssetKeyFunc)
	{
		string normPrimary = normalizeAssetKeyFunc(primaryKey);
		if (!string.IsNullOrEmpty(normPrimary) && ModelDespillPlayerColor.TryGetValue(normPrimary, out bool b1)) return b1;

		string normAsset = normalizeAssetKeyFunc(assetKey);
		if (!string.IsNullOrEmpty(normAsset) && ModelDespillPlayerColor.TryGetValue(normAsset, out bool b2)) return b2;

		if (!string.IsNullOrEmpty(primaryKey))
		{
			var registry = GameContext.RegistryService;
			if (registry.UnitRegistry.TryGetValue(primaryKey, out var meta)) return meta.DespillPlayerColor;
			if (registry.ResourceRegistry.TryGetValue(primaryKey, out var resMeta)) return resMeta.DespillPlayerColor;
			if (registry.PropRegistry.TryGetValue(primaryKey, out var propMeta)) return propMeta.DespillPlayerColor;
		}

		return false;
	}

	public bool GetModelNormalizeLuminance(string primaryKey, string assetKey, Func<string, string> normalizeAssetKeyFunc)
	{
		string normPrimary = normalizeAssetKeyFunc(primaryKey);
		if (!string.IsNullOrEmpty(normPrimary) && ModelNormalizeLuminance.TryGetValue(normPrimary, out bool b1)) return b1;

		string normAsset = normalizeAssetKeyFunc(assetKey);
		if (!string.IsNullOrEmpty(normAsset) && ModelNormalizeLuminance.TryGetValue(normAsset, out bool b2)) return b2;

		if (!string.IsNullOrEmpty(primaryKey))
		{
			var registry = GameContext.RegistryService;
			if (registry.UnitRegistry.TryGetValue(primaryKey, out var meta)) return meta.NormalizeLuminance;
			if (registry.ResourceRegistry.TryGetValue(primaryKey, out var resMeta)) return resMeta.NormalizeLuminance;
			if (registry.PropRegistry.TryGetValue(primaryKey, out var propMeta)) return propMeta.NormalizeLuminance;
		}

		return true;
	}

	public void SetModelNormalizeLuminance(string assetKey, bool normalizeLuminance, string modelAsset, string primaryKey, Func<string, string> normalizeAssetKeyFunc)
	{
		string norm = normalizeAssetKeyFunc(assetKey);
		if (string.IsNullOrEmpty(norm)) return;

		ModelNormalizeLuminance[norm] = normalizeLuminance;

		string normModel = !string.IsNullOrEmpty(modelAsset) ? normalizeAssetKeyFunc(modelAsset) : null;
		if (!string.IsNullOrEmpty(normModel) && !normModel.Equals(norm, StringComparison.OrdinalIgnoreCase))
		{
			ModelNormalizeLuminance[normModel] = normalizeLuminance;
		}

		string normPrimary = !string.IsNullOrEmpty(primaryKey) ? normalizeAssetKeyFunc(primaryKey) : null;
		if (!string.IsNullOrEmpty(normPrimary) && !normPrimary.Equals(norm, StringComparison.OrdinalIgnoreCase))
		{
			ModelNormalizeLuminance[normPrimary] = normalizeLuminance;
		}

		GameContext.RegistryService.UpdateLuminanceInRegistries(assetKey, normalizeLuminance);
	}

	public string GetModelDeathShader(string primaryKey, string assetKey, Func<string, string> normalizeAssetKeyFunc)
	{
		if (TryGetModelDeathShader(primaryKey, normalizeAssetKeyFunc, out string d1)) return d1;
		if (TryGetModelDeathShader(assetKey, normalizeAssetKeyFunc, out string d2)) return d2;
		if (string.IsNullOrEmpty(primaryKey)) return "";

		var registry = GameContext.RegistryService;
		if (registry.TryGetRegistryDeathShader(primaryKey, out string d3)) return d3;

		return "";
	}

	public bool TryGetModelDeathShader(string key, Func<string, string> normalizeAssetKeyFunc, out string shader)
	{
		shader = "";
		string norm = normalizeAssetKeyFunc(key);
		return !string.IsNullOrEmpty(norm) && ModelDeathShaders.TryGetValue(norm, out shader);
	}

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

	public readonly Dictionary<string, float> ModelYOffsets = new(StringComparer.OrdinalIgnoreCase);
	public readonly Dictionary<string, string> ModelProceduralAnimations = new(StringComparer.OrdinalIgnoreCase);
	public readonly Dictionary<string, bool> ModelEnableProceduralAnimations = new(StringComparer.OrdinalIgnoreCase);

	public const float MaxSafeModelYOffset = 50f;
	public const float MinSafeModelCollisionRatio = 0.1f;
	public const float MaxSafeModelCollisionRatio = 10f;

	public void UpdateCollisionRadius(Entity entity, float baseRadius)
	{
		if (entity != default && GameContext.World.IsAlive(entity))
		{
			WorldExtensions.SetOrAdd(GameContext.World, entity, new CollisionRadius(baseRadius));
		}
	}

	public bool TryGetModelYOffset(string key, Func<string, string> normalizeFunc, out float offset)
	{
		offset = 0f;
		string norm = normalizeFunc(key);
		return !string.IsNullOrEmpty(norm) && ModelYOffsets.TryGetValue(norm, out offset);
	}

	public float GetModelYOffset(string primaryKey, string assetKey, Func<string, string> normalizeFunc)
	{
		if (TryGetModelYOffset(primaryKey, normalizeFunc, out float val1)) return val1;
		if (TryGetModelYOffset(assetKey, normalizeFunc, out float val2)) return val2;
		if (string.IsNullOrEmpty(primaryKey)) return 0f;
		if (GameContext.RegistryService.TryGetRegistryYOffset(primaryKey, out float val3)) return val3;
		return 0f;
	}

	public bool GetModelEnableProceduralAnimation(string primaryKey, string assetKey, Func<string, string> normalizeAssetKeyFunc)
	{
		string normPrimary = normalizeAssetKeyFunc(primaryKey);
		if (!string.IsNullOrEmpty(normPrimary) && ModelEnableProceduralAnimations.TryGetValue(normPrimary, out bool ep1))
			return ep1;

		string normAsset = normalizeAssetKeyFunc(assetKey);
		if (!string.IsNullOrEmpty(normAsset) && ModelEnableProceduralAnimations.TryGetValue(normAsset, out bool ep2))
			return ep2;

		return false;
	}

	public void SetModelEnableProceduralAnimation(string assetKey, bool enable, string modelAsset, Func<string, string> normalizeAssetKeyFunc, Action onChange)
	{
		string norm = normalizeAssetKeyFunc(assetKey);
		if (string.IsNullOrEmpty(norm)) return;

		string normModel = !string.IsNullOrEmpty(modelAsset) ? normalizeAssetKeyFunc(modelAsset) : null;

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

		onChange?.Invoke();
	}

	public bool TryGetModelScale(string key, Func<string, string> normalizeFunc, out float scale)
	{
		scale = 1.0f;
		string norm = normalizeFunc(key);
		return !string.IsNullOrEmpty(norm) && ModelScales.TryGetValue(norm, out scale) && scale > 0f;
	}

	public bool TryGetScaleFromKeys(string primaryKey, string assetKey, Func<string, string> normalizeFunc, out float scale)
	{
		if (TryGetModelScale(primaryKey, normalizeFunc, out scale)) return true;
		if (TryGetModelScale(assetKey, normalizeFunc, out scale)) return true;

		var regService = GameContext.RegistryService;
		if (!string.IsNullOrEmpty(primaryKey) && regService.TryGetRegistryScale(primaryKey, out scale)) return true;
		string normAsset = normalizeFunc(assetKey);
		if (!string.IsNullOrEmpty(normAsset) && regService.TryGetRegistryScale(normAsset, out scale)) return true;

		scale = 1.0f;
		return false;
	}


	public T GetOverrideValue<T>(string normKey, string normModel, Dictionary<string, T> dict, T defaultValue)
	{
		if (dict.TryGetValue(normKey, out T val1)) return val1;
		if (!string.IsNullOrEmpty(normModel) && dict.TryGetValue(normModel, out T val2)) return val2;
		return defaultValue;
	}

	public string GetShaderOverride(string normKey, string normModel, Dictionary<string, string> dict)
	{
		if (!string.IsNullOrEmpty(normKey) && dict.TryGetValue(normKey, out string val1)) return val1;
		if (!string.IsNullOrEmpty(normModel) && dict.TryGetValue(normModel, out string val2)) return val2;
		return null;
	}

	
	public void LoadOverridesFromMetadata(Realm.Shared.Metadata.MapMetadata metadata, Func<string, string> normalizeModelAssetKey)
	{
		ModelYOffsets.Clear();
		ClearAll();

		if (metadata.Templates != null)
		{
			ProcessMetadataEntities(metadata.Templates.Resources, 2.75f, r => r.TemplateID, r => r.ModelPath, r => r.YOffset, r => r.Scale, r => r.CollisionCircle, r => r.Brightness, r => r.Tint, r => r.DespillPlayerColor, r => r.NormalizeLuminance, normalizeModelAssetKey);
			ProcessMetadataEntities(metadata.Templates.Buildings, 1.2f, b => b.TemplateID, b => b.ModelPath, b => b.YOffset, b => b.Scale, b => b.CollisionCircle, b => b.Brightness, b => b.Tint, b => b.DespillPlayerColor, b => b.NormalizeLuminance, normalizeModelAssetKey);
			ProcessMetadataEntities(metadata.Templates.Props, 1.0f, p => p.TemplateID, p => p.ModelPath, p => p.YOffset, p => p.Scale, p => p.CollisionCircle, p => p.Brightness, p => p.Tint, p => p.DespillPlayerColor, p => p.NormalizeLuminance, normalizeModelAssetKey);
			ProcessMetadataEntities(metadata.Templates.Units, 1.5f, u => u.TemplateID, u => u.ModelPath, u => u.YOffset, u => u.Scale, u => u.CollisionCircle, u => u.Brightness, u => u.Tint, u => u.DespillPlayerColor, u => u.NormalizeLuminance, normalizeModelAssetKey);
		}

		if (metadata.Models != null)
		{
			foreach (var kvp in metadata.Models)
			{
				ProcessModelMetadata(kvp.Key, kvp.Value, normalizeModelAssetKey);
			}
		}
	}

	public void ProcessMetadataEntities<T>(IEnumerable<T> items, float defaultScale, Func<T, string> getId, Func<T, string> getModelPath, Func<T, float> getYOffset, Func<T, float> getScale, Func<T, float> getCollisionCircle, Func<T, float> getBrightness, Func<T, string> getTint, Func<T, bool> getDespillPlayerColor, Func<T, bool> getNormalizeLuminance, Func<string, string> normalizeModelAssetKey)
	{
		if (items == null) return;
		foreach (var item in items)
		{
			string uId = getId(item);
			if (string.IsNullOrEmpty(uId)) continue;
			string normKey = normalizeModelAssetKey(uId);

			ModelYOffsets[normKey] = getYOffset(item);

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
				string normModel = normalizeModelAssetKey(mPath);
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
				ModelColorTint[normKey] = Godot.Color.FromString(tintStr, new Godot.Color(1, 1, 1));
			}

			ModelDespillPlayerColor[normKey] = getDespillPlayerColor(item);
			ModelNormalizeLuminance[normKey] = getNormalizeLuminance(item);
		}
	}

	public void ProcessModelPhysicalMetadata(string key, string normKey, Realm.Shared.Metadata.ModelMetadata model)
	{
		TrySetModelYOffset(key, normKey, model);
		TrySetModelScale(key, normKey, model);
		TrySetModelCollisionCircleRatio(key, normKey, model);
		TrySetModelObstacleRadius(normKey, model);
		TrySetModelProceduralAnimation(normKey, model);
	}

	private void TrySetModelYOffset(string key, string normKey, Realm.Shared.Metadata.ModelMetadata model)
	{
		if (model.Offsets.HasValue && IsValidModelYOffset(key, model.Offsets.Value))
			ModelYOffsets[normKey] = model.Offsets.Value;
	}

	private void TrySetModelScale(string key, string normKey, Realm.Shared.Metadata.ModelMetadata model)
	{
		if (model.Scales.HasValue && IsValidModelScale(key, model.Scales.Value))
			ModelScales[normKey] = model.Scales.Value;
	}

	private void TrySetModelCollisionCircleRatio(string key, string normKey, Realm.Shared.Metadata.ModelMetadata model)
	{
		if (model.CollisionCircleRatios.HasValue && IsValidModelCollisionRatio(key, model.CollisionCircleRatios.Value))
			ModelCollisionCircleRatios[normKey] = model.CollisionCircleRatios.Value;
	}

	private void TrySetModelObstacleRadius(string normKey, Realm.Shared.Metadata.ModelMetadata model)
	{
		if (model.ObstacleRadii.HasValue && model.ObstacleRadii.Value > 0f)
			ModelObstacleRadii[normKey] = model.ObstacleRadii.Value;
	}

	private void TrySetModelProceduralAnimation(string normKey, Realm.Shared.Metadata.ModelMetadata model)
	{
		if (!string.IsNullOrWhiteSpace(model.ProceduralAnimation))
			ModelProceduralAnimations[normKey] = model.ProceduralAnimation.Trim();

		if (model.EnableProceduralAnimation.HasValue)
			ModelEnableProceduralAnimations[normKey] = model.EnableProceduralAnimation.Value;
	}

	public void ProcessModelVisualMetadata(string normKey, Realm.Shared.Metadata.ModelMetadata model)
	{
		if (!string.IsNullOrWhiteSpace(model.SpawnShaders)) ModelSpawnShaders[normKey] = model.SpawnShaders.Trim();
		if (!string.IsNullOrWhiteSpace(model.DeathShaders)) ModelDeathShaders[normKey] = model.DeathShaders.Trim();
		if (model.Brightness.HasValue) ModelBrightness[normKey] = model.Brightness.Value;
		if (!string.IsNullOrWhiteSpace(model.ColorTint) && Godot.Color.HtmlIsValid(model.ColorTint)) ModelColorTint[normKey] = Godot.Color.FromHtml(model.ColorTint);
		if (model.DespillPlayerColor.HasValue) ModelDespillPlayerColor[normKey] = model.DespillPlayerColor.Value;
		if (model.NormalizeLuminance.HasValue) ModelNormalizeLuminance[normKey] = model.NormalizeLuminance.Value;
		if (model.IgnorePlayerColor.HasValue) ModelIgnorePlayerColor[normKey] = model.IgnorePlayerColor.Value;
	}

	public void ProcessModelMetadata(string key, Realm.Shared.Metadata.ModelMetadata model, Func<string, string> normalizeModelAssetKey)
	{
		if (model == null) return;
		string normKey = normalizeModelAssetKey(key);

		ProcessModelPhysicalMetadata(key, normKey, model);
		ProcessModelVisualMetadata(normKey, model);
	}

	private bool IsValidModelYOffset(string key, float offset)
	{
		return offset >= -MaxSafeModelYOffset && offset <= MaxSafeModelYOffset;
	}

	private bool IsValidModelScale(string key, float scale)
	{
		return scale > 0f && scale <= 100f;
	}

	private bool IsValidModelCollisionRatio(string key, float ratio)
	{
		return ratio >= MinSafeModelCollisionRatio && ratio <= MaxSafeModelCollisionRatio;
	}

	public void ClearAll()
	{
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
		ModelYOffsets.Clear();
		ModelProceduralAnimations.Clear();
		ModelEnableProceduralAnimations.Clear();
	}
}