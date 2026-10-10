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

namespace Realm.Client.Core;

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

    private bool MatchesUnit3DKey(Realm.Client.Unit3D unit, string normTarget)
    {
        if (IsMatch(unit.UnitId, normTarget)) return true;
        if (IsMatch(unit.ModelPath, normTarget)) return true;
        if (UnitRegistry.TryGetValue(unit.UnitId, out var meta) && IsMatch(meta.ModelPath, normTarget)) return true;
        if (BuildingRegistry.TryGetValue(unit.UnitId, out var bldMeta) && IsMatch(bldMeta.ModelPath, normTarget)) return true;
        return false;
    }

    private bool CheckPropRegistryForMatch(string id, string cleanId, string normTarget)
    {
        if (PropRegistry.TryGetValue(id, out var meta) && IsMatch(meta.ModelPath, normTarget)) return true;
        if (!string.IsNullOrEmpty(cleanId) && PropRegistry.TryGetValue(cleanId, out meta) && IsMatch(meta.ModelPath, normTarget)) return true;
        return false;
    }

    private bool CheckBuildingRegistryForMatch(string id, string cleanId, string normTarget)
    {
        if (BuildingRegistry.TryGetValue(id, out var meta) && IsMatch(meta.ModelPath, normTarget)) return true;
        if (!string.IsNullOrEmpty(cleanId) && BuildingRegistry.TryGetValue(cleanId, out meta) && IsMatch(meta.ModelPath, normTarget)) return true;
        return false;
    }

    public float GetModelYOffset(object objOrId)
    {
        if (objOrId == null) return 0f;
        
        string primaryKey = GetSelectedEntityOrAssetKey(objOrId);
        if (TryGetModelYOffset(primaryKey, out float val1)) return val1;

        string assetKey = GetModelAssetKey(objOrId);
        if (TryGetModelYOffset(assetKey, out float val2)) return val2;

        if (string.IsNullOrEmpty(primaryKey)) return 0f;

        if (TryGetRegistryYOffset(primaryKey, out float val3)) return val3;

        return 0f;
    }

    private bool TryGetModelYOffset(string key, out float offset)
    {
        offset = 0f;
        string norm = NormalizeModelAssetKey(key);
        return !string.IsNullOrEmpty(norm) && ModelYOffsets.TryGetValue(norm, out offset);
    }

    private bool TryGetRegistryYOffset(string key, out float offset)
    {
        offset = 0f;
        if (UnitRegistry.TryGetValue(key, out var meta) && meta.YOffset != 0f) { offset = meta.YOffset; return true; }
        if (BuildingRegistry.TryGetValue(key, out var bldMeta) && bldMeta.YOffset != 0f) { offset = bldMeta.YOffset; return true; }
        if (ResourceRegistry.TryGetValue(key, out var resMeta) && resMeta.YOffset != 0f) { offset = resMeta.YOffset; return true; }
        if (PropRegistry.TryGetValue(key, out var propMeta) && propMeta.YOffset != 0f) { offset = propMeta.YOffset; return true; }
        return false;
    }


    public float GetModelScale(object objOrId)
    {
        if (objOrId == null) return 1.0f;
        
        string primaryKey = GetSelectedEntityOrAssetKey(objOrId);
        if (TryGetModelScale(primaryKey, out float val1)) return val1;

        string assetKey = GetModelAssetKey(objOrId);
        if (TryGetModelScale(assetKey, out float val2)) return val2;

        if (!string.IsNullOrEmpty(primaryKey) && TryGetRegistryScale(primaryKey, out float s1)) return s1;
        if (!string.IsNullOrEmpty(NormalizeModelAssetKey(assetKey)) && TryGetRegistryScale(NormalizeModelAssetKey(assetKey), out float s2)) return s2;

        return GetFallbackScale(objOrId, primaryKey, NormalizeModelAssetKey(assetKey));
    }

    private bool TryGetModelScale(string key, out float scale)
    {
        scale = 1.0f;
        string norm = NormalizeModelAssetKey(key);
        return !string.IsNullOrEmpty(norm) && ModelScales.TryGetValue(norm, out scale) && scale > 0f;
    }

    private bool TryGetRegistryScale(string key, out float scale)
    {
        scale = 1.0f;
        if (UnitRegistry.TryGetValue(key, out var meta) && meta.Scale > 0f) { scale = meta.Scale; return true; }
        if (BuildingRegistry.TryGetValue(key, out var bldMeta) && bldMeta.Scale > 0f) { scale = bldMeta.Scale; return true; }
        if (ResourceRegistry.TryGetValue(key, out var resMeta) && resMeta.Scale > 0f) { scale = resMeta.Scale; return true; }
        if (PropRegistry.TryGetValue(key, out var propMeta) && propMeta.Scale > 0f) { scale = propMeta.Scale; return true; }
        return false;
    }
    
    private float GetFallbackScale(object objOrId, string primaryKey, string normAsset)
    {
        if (objOrId is Realm.Client.Unit3D unit && GodotObject.IsInstanceValid(unit))
        {
            if (unit.IsResource) return 2.75f;
            if (unit.IsBuilding) return 1.5f;
            return 1.0f;
        }

        float scale = GetScaleFromRegistryKey(primaryKey);
        if (scale > 0f) return scale;

        scale = GetScaleFromRegistryKey(normAsset);
        if (scale > 0f) return scale;

        return 1.0f;
    }

    private float GetScaleFromRegistryKey(string key)
    {
        if (string.IsNullOrEmpty(key)) return -1f;
        
        if (ResourceRegistry.ContainsKey(key)) return 2.75f;
        if (BuildingRegistry.ContainsKey(key)) return 1.5f;
        if (PropRegistry.ContainsKey(key)) return 1.25f;
        if (UnitRegistry.ContainsKey(key)) return 1.0f;
        
        return -1f;
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

        return GetRegistryCollisionCircleRatio(primaryKey);
    }

    private float GetRegistryCollisionCircleRatio(string primaryKey)
    {
        if (string.IsNullOrEmpty(primaryKey)) return 1.0f;

        if (UnitRegistry.TryGetValue(primaryKey, out var meta) && meta.CollisionCircle > 0f) return meta.CollisionCircle;
        if (ResourceRegistry.TryGetValue(primaryKey, out var resMeta) && resMeta.CollisionCircle > 0f) return resMeta.CollisionCircle;
        if (PropRegistry.TryGetValue(primaryKey, out var propMeta) && propMeta.CollisionCircle > 0f) return propMeta.CollisionCircle;

        return 1.0f;
    }

    public float GetModelBrightness(object objOrId)
    {
        if (objOrId == null) return 1.0f;
        
        string primaryKey = GetSelectedEntityOrAssetKey(objOrId);
        if (TryGetModelBrightness(primaryKey, out float val1)) return Mathf.Clamp(val1, 0.10f, 1.75f);

        string assetKey = GetModelAssetKey(objOrId);
        if (TryGetModelBrightness(assetKey, out float val2)) return Mathf.Clamp(val2, 0.10f, 1.75f);

        if (string.IsNullOrEmpty(primaryKey)) return 0.5f;

        if (TryGetRegistryBrightness(primaryKey, out float val3)) return Mathf.Clamp(val3, 0.10f, 2.0f);

        return 0.5f;
    }

    private bool TryGetModelBrightness(string key, out float brightness)
    {
        brightness = 0f;
        string norm = NormalizeModelAssetKey(key);
        return !string.IsNullOrEmpty(norm) && ModelBrightness.TryGetValue(norm, out brightness);
    }

    private bool TryGetRegistryBrightness(string key, out float brightness)
    {
        brightness = 0f;
        if (UnitRegistry.TryGetValue(key, out var meta) && meta.Brightness > 0f) { brightness = meta.Brightness; return true; }
        if (ResourceRegistry.TryGetValue(key, out var resMeta) && resMeta.Brightness > 0f) { brightness = resMeta.Brightness; return true; }
        if (PropRegistry.TryGetValue(key, out var propMeta) && propMeta.Brightness > 0f) { brightness = propMeta.Brightness; return true; }
        return false;
    }


    public Color GetModelColorTint(object objOrId)
    {
        if (objOrId == null) return new Color(1.0f, 1.0f, 1.0f);
        
        string primaryKey = GetSelectedEntityOrAssetKey(objOrId);
        if (TryGetModelColorTint(primaryKey, out Color c1)) return c1;

        string assetKey = GetModelAssetKey(objOrId);
        if (TryGetModelColorTint(assetKey, out Color c2)) return c2;

        if (string.IsNullOrEmpty(primaryKey)) return new Color(1.0f, 1.0f, 1.0f);

        if (TryGetRegistryColorTint(primaryKey, out Color c3)) return c3;

        return new Color(1.0f, 1.0f, 1.0f);
    }

    private bool TryGetModelColorTint(string key, out Color color)
    {
        color = default;
        string norm = NormalizeModelAssetKey(key);
        return !string.IsNullOrEmpty(norm) && ModelColorTint.TryGetValue(norm, out color);
    }

    private bool TryGetRegistryColorTint(string key, out Color color)
    {
        color = default;
        if (UnitRegistry.TryGetValue(key, out var meta) && !string.IsNullOrEmpty(meta.Tint) && Color.HtmlIsValid(meta.Tint)) { color = Color.FromHtml(meta.Tint); return true; }
        if (ResourceRegistry.TryGetValue(key, out var resMeta) && !string.IsNullOrEmpty(resMeta.Tint) && Color.HtmlIsValid(resMeta.Tint)) { color = Color.FromHtml(resMeta.Tint); return true; }
        if (PropRegistry.TryGetValue(key, out var propMeta) && !string.IsNullOrEmpty(propMeta.Tint) && Color.HtmlIsValid(propMeta.Tint)) { color = Color.FromHtml(propMeta.Tint); return true; }
        return false;
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

        UpdateLuminanceInRegistries(assetKey, normalizeLuminance);

        UpdateMaterialOverridesForAsset(norm);

        _modelYOffsetSavePending = true;
        EditorHasUnsavedChanges = true;
    }
    
    private void UpdateLuminanceInRegistries(string assetKey, bool normalizeLuminance)
    {
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
    }


    public string GetModelDeathShader(object objOrId)
    {
        if (objOrId == null) return "";
        
        string primaryKey = GetSelectedEntityOrAssetKey(objOrId);
        if (TryGetModelDeathShader(primaryKey, out string d1)) return d1;

        string assetKey = GetModelAssetKey(objOrId);
        if (TryGetModelDeathShader(assetKey, out string d2)) return d2;

        if (string.IsNullOrEmpty(primaryKey)) return "";

        if (TryGetRegistryDeathShader(primaryKey, out string d3)) return d3;

        return "";
    }

    private bool TryGetModelDeathShader(string key, out string shader)
    {
        shader = "";
        string norm = NormalizeModelAssetKey(key);
        return !string.IsNullOrEmpty(norm) && ModelDeathShaders.TryGetValue(norm, out shader);
    }

    private bool TryGetRegistryDeathShader(string key, out string shader)
    {
        shader = "";
        if (UnitRegistry.TryGetValue(key, out var meta) && !string.IsNullOrWhiteSpace(meta.DeathShader)) { shader = meta.DeathShader; return true; }
        if (BuildingRegistry.TryGetValue(key, out var bldMeta) && !string.IsNullOrWhiteSpace(bldMeta.DeathShader)) { shader = bldMeta.DeathShader; return true; }
        if (ResourceRegistry.TryGetValue(key, out var resMeta) && !string.IsNullOrWhiteSpace(resMeta.DeathShader)) { shader = resMeta.DeathShader; return true; }
        if (PropRegistry.TryGetValue(key, out var propMeta) && !string.IsNullOrWhiteSpace(propMeta.DeathShader)) { shader = propMeta.DeathShader; return true; }
        return false;
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

        if (HasMatchingPropOrResourceMetadata(norm)) return true;
        if (MatchesPropOrResourcePathPattern(key) || MatchesPropOrResourcePathPattern(norm)) return true;

        string resolved = ModelCache.ResolveModelPath(norm);
        return !string.IsNullOrEmpty(resolved) && MatchesPropOrResourcePathPattern(resolved);
    }
    
    private bool HasMatchingPropOrResourceMetadata(string norm)
    {
        return HasMatchingPropMetadata(norm) || HasMatchingResourceMetadata(norm);
    }

    private bool HasMatchingPropMetadata(string norm)
    {
        foreach (var propMeta in PropRegistry.Values)
        {
            if (!string.IsNullOrEmpty(propMeta.ModelPath) && NormalizeModelAssetKey(propMeta.ModelPath) == norm) return true;
            if (!string.IsNullOrEmpty(propMeta.TemplateID) && NormalizeModelAssetKey(propMeta.TemplateID) == norm) return true;
        }
        return false;
    }

    private bool HasMatchingResourceMetadata(string norm)
    {
        foreach (var resMeta in ResourceRegistry.Values)
        {
            if (!string.IsNullOrEmpty(resMeta.ModelPath) && NormalizeModelAssetKey(resMeta.ModelPath) == norm) return true;
            if (!string.IsNullOrEmpty(resMeta.TemplateID) && NormalizeModelAssetKey(resMeta.TemplateID) == norm) return true;
        }
        return false;
    }
    
    private bool MatchesPropOrResourcePathPattern(string path)
    {
        return path.Contains("/props/", StringComparison.OrdinalIgnoreCase) || 
               path.Contains("\\props\\", StringComparison.OrdinalIgnoreCase) || 
               path.StartsWith("props/", StringComparison.OrdinalIgnoreCase) ||
               path.Contains("/resources/", StringComparison.OrdinalIgnoreCase) || 
               path.Contains("\\resources\\", StringComparison.OrdinalIgnoreCase) || 
               path.StartsWith("resources/", StringComparison.OrdinalIgnoreCase);
    }


    public bool IsAttachmentKey(string key)
    {
        if (string.IsNullOrEmpty(key)) return false;
        string norm = NormalizeModelAssetKey(key);

        if (AttachmentRegistry.ContainsKey(key) || AttachmentRegistry.ContainsKey(norm)) return true;
        if (HasMatchingAttachmentMetadata(norm)) return true;

        if (ContainsAttachmentKeyword(key) || ContainsAttachmentKeyword(norm)) return true;

        string resolved = ModelCache.ResolveModelPath(norm);
        if (!string.IsNullOrEmpty(resolved) && ContainsAttachmentKeyword(resolved)) return true;

        return false;
    }

    private bool HasMatchingAttachmentMetadata(string norm)
    {
        foreach (var attMeta in AttachmentRegistry.Values)
        {
            if (!string.IsNullOrEmpty(attMeta.ModelPath) && NormalizeModelAssetKey(attMeta.ModelPath) == norm) return true;
            if (!string.IsNullOrEmpty(attMeta.AttachmentId) && NormalizeModelAssetKey(attMeta.AttachmentId) == norm) return true;
        }
        return false;
    }

    private bool ContainsAttachmentKeyword(string str)
    {
        return str.Contains("/attachments/", StringComparison.OrdinalIgnoreCase) || 
               str.Contains("\\attachments\\", StringComparison.OrdinalIgnoreCase) || 
               str.StartsWith("attachments/", StringComparison.OrdinalIgnoreCase) ||
               str.Contains("/weapons/", StringComparison.OrdinalIgnoreCase) || 
               str.Contains("\\weapons\\", StringComparison.OrdinalIgnoreCase) || 
               str.StartsWith("weapons/", StringComparison.OrdinalIgnoreCase) ||
               str.Contains("/items/", StringComparison.OrdinalIgnoreCase) || 
               str.Contains("\\items\\", StringComparison.OrdinalIgnoreCase) || 
               str.StartsWith("items/", StringComparison.OrdinalIgnoreCase);
    }
    public bool GetModelIgnorePlayerColor(object objOrId)
    {
        if (objOrId == null) return false;

        string primaryKey = GetSelectedEntityOrAssetKey(objOrId);
        string normPrimary = NormalizeModelAssetKey(primaryKey);
        if (!string.IsNullOrEmpty(normPrimary) && ModelIgnorePlayerColor.TryGetValue(normPrimary, out bool b1)) return b1;

        string assetKey = GetModelAssetKey(objOrId);
        string normAsset = NormalizeModelAssetKey(assetKey);
        if (!string.IsNullOrEmpty(normAsset) && ModelIgnorePlayerColor.TryGetValue(normAsset, out bool b2)) return b2;

        if (CheckRegistryIgnorePlayerColor(primaryKey)) return true;
        if (CheckRegistryIgnorePlayerColorForAsset(normAsset)) return true;

        if (IsAlwaysIgnoredType(objOrId, normPrimary, normAsset)) return true;

        return CheckModelCacheIgnorePlayerColor(normPrimary, normAsset);
    }

    private bool CheckRegistryIgnorePlayerColorForAsset(string normAsset)
    {
        if (string.IsNullOrEmpty(normAsset)) return false;
        if (ResourceRegistry.TryGetValue(normAsset, out var resMeta2)) return resMeta2.IgnorePlayerColor;
        if (PropRegistry.TryGetValue(normAsset, out var propMeta2)) return propMeta2.IgnorePlayerColor;
        if (AttachmentRegistry.TryGetValue(normAsset, out _)) return true;
        return false;
    }

    private bool IsAlwaysIgnoredType(object objOrId, string normPrimary, string normAsset)
    {
        if (objOrId is Realm.Client.Prop3D) return true;
        if (IsPropOrResourceKey(normPrimary)) return true;
        if (!string.IsNullOrEmpty(normAsset) && !string.Equals(normAsset, normPrimary, StringComparison.OrdinalIgnoreCase) && IsPropOrResourceKey(normAsset)) return true;
        if (IsAttachmentKey(normPrimary)) return true;
        if (!string.IsNullOrEmpty(normAsset) && !string.Equals(normAsset, normPrimary, StringComparison.OrdinalIgnoreCase) && IsAttachmentKey(normAsset)) return true;
        return false;
    }

    public void UpdateMaterialOverridesForAsset(string normAssetKey)
    {
        if (ShouldSkipMaterialOverridesUpdate(normAssetKey)) return;

        float brightness = GetModelBrightness(normAssetKey);
        Color tint = GetModelColorTint(normAssetKey);
        bool ignorePlayerColor = GetModelIgnorePlayerColor(normAssetKey);
        bool normalizeLuminance = GetModelNormalizeLuminance(normAssetKey);

        ApplyMaterialOverridesToProps(normAssetKey, brightness, tint, normalizeLuminance, ignorePlayerColor);
        ApplyMaterialOverridesToUnits(normAssetKey, brightness, tint, normalizeLuminance, ignorePlayerColor);
        ApplyMaterialOverridesToPreviewNode(normAssetKey, brightness, tint, normalizeLuminance, ignorePlayerColor);

        Realm.Client.PropMultiMeshManager.Instance?.UpdateMaterialOverrides(normAssetKey);
        Realm.Client.PropMultiMeshManager.Instance?.MarkDirty(normAssetKey);
    }

    private bool ShouldSkipMaterialOverridesUpdate(string normAssetKey)
    {
        if (string.IsNullOrEmpty(normAssetKey)) return true;
        
        bool hasProps = AllProps.Count > 0;
        bool hasUnits = AllUnits.Count > 0;
        bool hasPreview = _editorPreviewNode != null;
        bool hasMultiMesh = Realm.Client.PropMultiMeshManager.Instance != null && Realm.Client.PropMultiMeshManager.Instance.HasGroups;
        
        return !hasProps && !hasUnits && !hasPreview && !hasMultiMesh;
    }

    private void ApplyMaterialOverridesToProps(string normAssetKey, float brightness, Color tint, bool normalizeLuminance, bool ignorePlayerColor)
    {
        foreach (var prop in AllProps)
        {
            if (GodotObject.IsInstanceValid(prop) && MatchesEntityOrAssetKey(prop, normAssetKey))
            {
                ApplyMaterialOverridesToNode(prop, brightness, tint, normalizeLuminance, ignorePlayerColor, false);
            }
        }
    }

    private void ApplyMaterialOverridesToUnits(string normAssetKey, float brightness, Color tint, bool normalizeLuminance, bool ignorePlayerColor)
    {
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
    }

    private void ApplyMaterialOverridesToPreviewNode(string normAssetKey, float brightness, Color tint, bool normalizeLuminance, bool ignorePlayerColor)
    {
        if (_editorPreviewNode != null && GodotObject.IsInstanceValid(_editorPreviewNode) && MatchesEntityOrAssetKey(_editorPreviewNode, normAssetKey))
        {
            ApplyMaterialOverridesToNode(_editorPreviewNode, brightness, tint, normalizeLuminance, ignorePlayerColor, _editorPreviewNode is Realm.Client.Unit3D);
        }
    }
    public void UpdateAllMaterialOverrides()
    {
        if (AllProps.Count == 0 && AllUnits.Count == 0 && _editorPreviewNode == null && (Realm.Client.PropMultiMeshManager.Instance == null || !Realm.Client.PropMultiMeshManager.Instance.HasGroups))
        {
            return;
        }

        foreach (var prop in AllProps)
        {
            ApplyOverridesToNode(prop, false);
        }

        foreach (var unit in AllUnits)
        {
            ApplyOverridesToNode(unit, true);
        }

        if (_editorPreviewNode != null)
        {
            ApplyOverridesToNode(_editorPreviewNode, _editorPreviewNode is Realm.Client.Unit3D);
        }

        Realm.Client.PropMultiMeshManager.Instance?.UpdateAllMaterialOverrides();
    }

    private void ApplyOverridesToNode(Node3D node, bool isUnit)
    {
        if (!GodotObject.IsInstanceValid(node)) return;

        string assetKey = GetModelAssetKey(node);
        float brightness = GetModelBrightness(assetKey);
        Color tint = GetModelColorTint(assetKey);
        bool ignorePlayerColor = GetModelIgnorePlayerColor(assetKey);
        bool normalizeLuminance = GetModelNormalizeLuminance(assetKey);

        ApplyMaterialOverridesToNode(node, brightness, tint, normalizeLuminance, ignorePlayerColor, isUnit);

        if (isUnit && !ignorePlayerColor && node is Realm.Client.Unit3D unit)
        {
            unit.UpdatePlayerColorVisual();
        }
    }

    public void ApplyAllGlobalOverridesToObject(object objOrNode)
    {
        if (objOrNode == null) return;

        if (objOrNode is Realm.Client.Unit3D unit && GodotObject.IsInstanceValid(unit))
        {
            ApplyGlobalOverridesToUnit(unit);
        }
        else if (objOrNode is Realm.Client.Prop3D prop && GodotObject.IsInstanceValid(prop))
        {
            ApplyGlobalOverridesToProp(prop);
        }
    }

    private void ApplyGlobalOverridesToUnit(Realm.Client.Unit3D unit)
    {
        unit.UpdateModelScale(GetModelScale(unit));
        unit.UpdateModelYOffset(GetModelYOffset(unit));
        
        float circleRatio = GetModelCollisionCircleRatio(unit);
        unit.UpdateCollisionCircleScale(circleRatio);
        
        if (unit.Entity != default && EcsWorld.IsAlive(unit.Entity))
        {
            float baseRadius = GetOrCalculateObstacleRadius(unit.UnitId, unit, unit.IsBuilding) * circleRatio;
            EcsWorld.SetOrAdd(unit.Entity, new Realm.Ecs.Components.Core.CollisionRadius(baseRadius));
        }

        if (unit.IsPreview) return;

        ApplyMaterialOverridesToUnit(unit);
        ApplyProceduralAnimation(unit, GetModelEnableProceduralAnimation(unit), GetModelProceduralAnimation(unit));
    }

    private void ApplyMaterialOverridesToUnit(Realm.Client.Unit3D unit)
    {
        float brightness = GetModelBrightness(unit);
        Color tint = GetModelColorTint(unit);
        bool ignorePlayerColor = GetModelIgnorePlayerColor(unit);
        bool normalizeLuminance = GetModelNormalizeLuminance(unit);
        
        ApplyMaterialOverridesToNode(unit, brightness, tint, normalizeLuminance, ignorePlayerColor, true);
        if (!ignorePlayerColor) unit.UpdatePlayerColorVisual();
    }

    private void ApplyGlobalOverridesToProp(Realm.Client.Prop3D prop)
    {
        prop.UpdateVisualScale(GetModelScale(prop));
        prop.UpdateVisualYOffset(GetModelYOffset(prop));
        
        float circleRatio = GetModelCollisionCircleRatio(prop);
        prop.UpdateCollisionCircleScale(circleRatio);
        
        if (prop.Entity != default && EcsWorld.IsAlive(prop.Entity))
        {
            float baseRadius = GetOrCalculateObstacleRadius(prop.PropId, prop) * circleRatio;
            EcsWorld.SetOrAdd(prop.Entity, new Realm.Ecs.Components.Core.CollisionRadius(baseRadius));
        }

        if (prop.IsPreview) return;

        ApplyMaterialOverridesToProp(prop);
        ApplyProceduralAnimation(prop, GetModelEnableProceduralAnimation(prop), GetModelProceduralAnimation(prop));
    }

    private void ApplyMaterialOverridesToProp(Realm.Client.Prop3D prop)
    {
        float brightness = GetModelBrightness(prop);
        Color tint = GetModelColorTint(prop);
        bool ignorePlayerColor = GetModelIgnorePlayerColor(prop);
        bool normalizeLuminance = GetModelNormalizeLuminance(prop);
        
        ApplyMaterialOverridesToNode(prop, brightness, tint, normalizeLuminance, ignorePlayerColor, false);
    }

    private void ApplyProceduralAnimation(Node3D node, bool enable, string procAnimId)
    {
        if (enable && !string.IsNullOrEmpty(procAnimId))
        {
            var cfg = ProceduralAnimationManager.GetConfig(procAnimId);
            if (cfg != null)
            {
                ModelShaderManager.SetProceduralAnimation(node, cfg);
                return;
            }
        }
        ModelShaderManager.DisableProceduralAnimation(node);
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

        bool isUnit = isUnitOrBuilding ?? (node is Realm.Client.Unit3D || node.GetParent() is Realm.Client.Unit3D || node.Owner is Realm.Client.Unit3D);
        Color tint = colorTint ?? new Color(1.0f, 1.0f, 1.0f);
        bool isDefaultColor = MathF.Abs(brightness - 1.0f) < 0.001f && tint == new Color(1.0f, 1.0f, 1.0f);

        ApplyNodeShaderSettings(node, brightness, tint, normalizeLuminance, isUnit, ignorePlayerColor, isDefaultColor);
        ApplyMeshOverrides(node, brightness, tint, isUnit, ignorePlayerColor, isDefaultColor);
    }

    private static void ApplyNodeShaderSettings(Node node, float brightness, Color tint, bool normalizeLuminance, bool isUnit, bool? ignorePlayerColor, bool isDefaultColor)
    {
        if (!isDefaultColor)
        {
            Realm.Client.Utils.ModelShaderManager.SetBrightnessAndTint(node, brightness, tint);
        }

        Realm.Client.Utils.ModelShaderManager.RefreshShaderMaterialsForNode(node, normalizeLuminance);
        Realm.Client.Utils.ModelShaderManager.SetUnitReadability(node, isUnit);
        
        if (ignorePlayerColor.HasValue)
        {
            Realm.Client.Utils.ModelShaderManager.SetIgnorePlayerColor(node, ignorePlayerColor.Value);
        }
    }

    private static void ApplyMeshOverrides(Node node, float brightness, Color tint, bool isUnit, bool? ignorePlayerColor, bool isDefaultColor)
    {
        float multR = brightness * tint.R;
        float multG = brightness * tint.G;
        float multB = brightness * tint.B;
        
        var meshNodes = FindMeshInstancesRecursive(node);
        foreach (var meshInst in meshNodes)
        {
            if (ShouldSkipMeshInstance(meshInst)) continue;
            
            ApplyMeshOriginalMeshFallback(meshInst);
            ApplyMeshShaderParameters(meshInst, isUnit, ignorePlayerColor);

            if (!isDefaultColor)
            {
                ApplyMeshAlbedoOverride(meshInst, multR, multG, multB);
            }
        }
    }

    private static bool ShouldSkipMeshInstance(MeshInstance3D meshInst)
    {
        string nameStr = meshInst.Name.ToString();
        return nameStr.StartsWith("_selection", StringComparison.OrdinalIgnoreCase)
            || nameStr.StartsWith("Selection", StringComparison.OrdinalIgnoreCase)
            || nameStr.StartsWith("_hover", StringComparison.OrdinalIgnoreCase)
            || nameStr.StartsWith("Hover", StringComparison.OrdinalIgnoreCase)
            || nameStr.StartsWith("BrushIndicator", StringComparison.OrdinalIgnoreCase)
            || nameStr.StartsWith("DropShadow", StringComparison.OrdinalIgnoreCase)
            || nameStr.Contains("SelectionRing", StringComparison.OrdinalIgnoreCase)
            || nameStr.Contains("HoverRing", StringComparison.OrdinalIgnoreCase);
    }

    private static void ApplyMeshOriginalMeshFallback(MeshInstance3D meshInst)
    {
        if (meshInst.HasMeta("original_mesh"))
        {
            Mesh baseMesh = meshInst.GetMeta("original_mesh").As<Mesh>();
            if (baseMesh != null && meshInst.Mesh != baseMesh)
            {
                meshInst.Mesh = baseMesh;
            }
        }
    }

    private static void ApplyMeshShaderParameters(MeshInstance3D meshInst, bool isUnit, bool? ignorePlayerColor)
    {
        meshInst.SetInstanceShaderParameter(new StringName("unit_ambient_boost"), isUnit ? 0.10f : 0.0f);
        meshInst.SetInstanceShaderParameter(new StringName("unit_rim_intensity"), isUnit ? 0.25f : 0.0f);
        if (ignorePlayerColor.HasValue)
        {
            meshInst.SetInstanceShaderParameter(new StringName("ignore_player_color"), ignorePlayerColor.Value ? 1.0f : 0.0f);
        }
    }

    private static void ApplyMeshAlbedoOverride(MeshInstance3D meshInst, float multR, float multG, float multB)
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

    public void UpdateCollisionRadiiForAsset(string normAssetKey)
    {
        float ratio = GetModelCollisionCircleRatio(normAssetKey);

        UpdatePropCollisionRadii(normAssetKey, ratio);
        UpdateUnitCollisionRadii(normAssetKey, ratio);
        UpdatePreviewNodeCollisionRadii(normAssetKey, ratio);
    }

    private void UpdatePropCollisionRadii(string normAssetKey, float ratio)
    {
        foreach (var prop in AllProps)
        {
            if (!GodotObject.IsInstanceValid(prop) || !MatchesEntityOrAssetKey(prop, normAssetKey)) continue;

            prop.UpdateCollisionCircleScale(ratio);
            if (prop.Entity != default && EcsWorld.IsAlive(prop.Entity))
            {
                float baseRadius = GetOrCalculateObstacleRadius(prop.PropId, prop) * ratio;
                EcsWorld.SetOrAdd(prop.Entity, new Realm.Ecs.Components.Core.CollisionRadius(baseRadius));
            }
        }
    }

    private void UpdateUnitCollisionRadii(string normAssetKey, float ratio)
    {
        foreach (var unit in AllUnits)
        {
            if (!GodotObject.IsInstanceValid(unit) || !MatchesEntityOrAssetKey(unit, normAssetKey)) continue;

            unit.UpdateCollisionCircleScale(ratio);
            if (unit.Entity != default && EcsWorld.IsAlive(unit.Entity))
            {
                float baseRadius = GetOrCalculateObstacleRadius(unit.UnitId, unit) * ratio;
                EcsWorld.SetOrAdd(unit.Entity, new Realm.Ecs.Components.Core.CollisionRadius(baseRadius));
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

    private bool IsValidModelCollisionRatio(string assetKey, float val)
    {
        if (!float.IsFinite(val) || val < MinSafeModelCollisionRatio || val > MaxSafeModelCollisionRatio)
        {
            GD.PushWarning($"Ignoring invalid collision_circle_ratio {val} for model '{assetKey}' (expected {MinSafeModelCollisionRatio}..{MaxSafeModelCollisionRatio}).");
            return false;
        }
        return true;
    }

    public void RefreshAllPlacedObjectModels(string targetId = null)
    {
        if (!string.IsNullOrEmpty(targetId))
        {
            ModelCache.InvalidateModelPath(targetId);
            Realm.Client.Prop3D.InvalidateModelPathCache(targetId);
        }
        else
        {
            ModelCache.Clear();
            Realm.Client.Prop3D.ClearModelPathCache();
        }

        RefreshUnitsModels(targetId);
        RefreshPropsModels(targetId);
    }

    private void RefreshUnitsModels(string targetId)
    {
        foreach (var unit in AllUnits)
        {
            if (!GodotObject.IsInstanceValid(unit)) continue;
            
            if (IsTargetUnit(unit.UnitId, targetId))
            {
                RefreshSingleUnitModel(unit);
            }
        }
    }

    private bool IsTargetUnit(string unitId, string targetId)
    {
        if (string.IsNullOrEmpty(targetId)) return true;
        if (string.Equals(unitId, targetId, StringComparison.OrdinalIgnoreCase)) return true;
        
        string unitFileName = System.IO.Path.GetFileNameWithoutExtension(unitId);
        string targetFileName = System.IO.Path.GetFileNameWithoutExtension(targetId);
        
        return string.Equals(unitFileName, targetId, StringComparison.OrdinalIgnoreCase) || 
               string.Equals(unitId, targetFileName, StringComparison.OrdinalIgnoreCase);
    }

    private void RefreshSingleUnitModel(Realm.Client.Unit3D unit)
    {
        bool isBuilding = unit.IsBuilding;
        string targetModel = GetUnitTargetModel(unit.UnitId, ref isBuilding);
        
        if (string.IsNullOrEmpty(targetModel) && !string.IsNullOrEmpty(unit.ModelPath))
        {
            targetModel = unit.ModelPath;
        }

        if (!string.IsNullOrEmpty(targetModel))
        {
            string modelPath = GetFallbackModelPath(targetModel, isBuilding);
            unit.LoadModel(modelPath);
        }
    }

    private string GetUnitTargetModel(string unitId, ref bool isBuilding)
    {
        if (UnitRegistry.TryGetValue(unitId, out var meta) && !string.IsNullOrEmpty(meta.ModelPath)) return meta.ModelPath;
        
        if (BuildingRegistry.TryGetValue(unitId, out var bldMeta) && !string.IsNullOrEmpty(bldMeta.ModelPath))
        {
            isBuilding = true;
            return bldMeta.ModelPath;
        }
        
        if (ResourceRegistry.TryGetValue(unitId, out var resMeta) && !string.IsNullOrEmpty(resMeta.ModelPath)) return resMeta.ModelPath;
        if (PropRegistry.TryGetValue(unitId, out var propMeta) && !string.IsNullOrEmpty(propMeta.ModelPath)) return propMeta.ModelPath;
        
        return null;
    }
    
    private void RefreshPropsModels(string targetId)
    {
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
            Realm.Client.PropMultiMeshManager.Instance?.MarkDirty(targetId);
        }
    }


    private void UpdateMetadataModelOverrides(MapMetadata meta)
    {
        if (meta.Models == null) meta.Models = new Dictionary<string, ModelMetadata>(StringComparer.OrdinalIgnoreCase);

        var allKeys = CollectAllModelKeys();
        foreach (var key in allKeys)
        {
            UpdateSingleModelMetadata(meta.Models, key);
        }

        if (meta.Templates != null)
        {
            UpdateEntityOverrides(meta.Templates.Units);
            UpdateEntityOverrides(meta.Templates.Buildings);
            UpdateResourceOverrides(meta.Templates.Resources);
            UpdatePropOverrides(meta.Templates.Props);
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
    private void UpdateEntityOverrides(List<UnitMetadata> entities)
    {
        if (entities == null) return;
        for (int i = 0; i < entities.Count; i++)
        {
            var entity = entities[i];
            if (string.IsNullOrEmpty(entity.TemplateID)) continue;

            bool despill = entity.DespillPlayerColor;
            bool normLuma = entity.NormalizeLuminance;
            bool ignoreColor = entity.IgnorePlayerColor;
            string spawnShader = entity.SpawnShader;
            string deathShader = entity.DeathShader;

            ApplyVisualOverrides(entity.TemplateID, entity.ModelPath, ref despill, ref normLuma, ref ignoreColor, ref spawnShader, ref deathShader);

            entity.DespillPlayerColor = despill;
            entity.NormalizeLuminance = normLuma;
            entity.IgnorePlayerColor = ignoreColor;
            entity.SpawnShader = spawnShader;
            entity.DeathShader = deathShader;

            entities[i] = entity;
        }
    }
    private void UpdateResourceOverrides(List<ResourceMetadata> resources)
    {
        if (resources == null) return;
        for (int i = 0; i < resources.Count; i++)
        {
            var res = resources[i];
            if (string.IsNullOrEmpty(res.TemplateID)) continue;

            bool despill = res.DespillPlayerColor;
            bool normLuma = res.NormalizeLuminance;
            bool ignoreColor = res.IgnorePlayerColor;
            string spawnShader = res.SpawnShader;
            string deathShader = res.DeathShader;

            ApplyVisualOverrides(res.TemplateID, res.ModelPath, ref despill, ref normLuma, ref ignoreColor, ref spawnShader, ref deathShader);

            res.DespillPlayerColor = despill;
            res.NormalizeLuminance = normLuma;
            res.IgnorePlayerColor = ignoreColor;
            res.SpawnShader = spawnShader;
            res.DeathShader = deathShader;

            resources[i] = res;
        }
    }
    private void ApplyVisualOverrides(string templateId, string modelPath, ref bool despill, ref bool normLuma, ref bool ignoreColor, ref string spawnShader, ref string deathShader)
    {
        string normKey = NormalizeModelAssetKey(templateId);
        string normModel = !string.IsNullOrEmpty(modelPath) ? NormalizeModelAssetKey(modelPath) : "";

        despill = GetOverrideValue(normKey, normModel, ModelDespillPlayerColor, despill);
        normLuma = GetOverrideValue(normKey, normModel, ModelNormalizeLuminance, normLuma);
        ignoreColor = GetOverrideValue(normKey, normModel, ModelIgnorePlayerColor, ignoreColor);

        spawnShader = GetShaderOverride(normKey, normModel, ModelSpawnShaders) ?? GetModelSpawnShader(templateId);
        if (string.IsNullOrWhiteSpace(spawnShader)) spawnShader = null;

        deathShader = GetShaderOverride(normKey, normModel, ModelDeathShaders) ?? GetModelDeathShader(templateId);
        if (string.IsNullOrWhiteSpace(deathShader)) deathShader = null;
    }

    private T GetOverrideValue<T>(string normKey, string normModel, Dictionary<string, T> dict, T defaultValue)
    {
        if (dict.TryGetValue(normKey, out T val1)) return val1;
        if (!string.IsNullOrEmpty(normModel) && dict.TryGetValue(normModel, out T val2)) return val2;
        return defaultValue;
    }

    private string GetShaderOverride(string normKey, string normModel, Dictionary<string, string> dict)
    {
        if (!string.IsNullOrEmpty(normKey) && dict.TryGetValue(normKey, out string val1)) return val1;
        if (!string.IsNullOrEmpty(normModel) && dict.TryGetValue(normModel, out string val2)) return val2;
        return null;
    }

    public void ClearMapEntirely()
    {
        if (GroundTerrain == null) return;

        ClearAllEntities();
        ClearAllNodesAndTerrain();

        _editorService?.ResetAllState();
        HideSelectionHighlight();

        EditorHistoryManager.Clear();
        EditorHasUnsavedChanges = false;
        RebuildGridOverlayMeshExternal();

        EditorCameraBoundsLeft = -95.0f;
        EditorCameraBoundsRight = 95.0f;
        EditorCameraBoundsTop = -95.0f;
        EditorCameraBoundsBottom = 125.0f;
        Realm.Client.UI.MapEditorHUD.Instance?.UpdateCameraBoundsUI();
        RebuildCameraBoundsOverlay();

        EditorCoordinates.Clear();
        RebuildAllCoordinatePersistentMeshes();
        HideCoordinateSelectionOutline();
        
        RefreshUIAndExternalState();
    }

    private void ClearAllEntities()
    {
        var unitsCopy = new List<Realm.Client.Unit3D>(AllUnits);
        foreach (var unit in unitsCopy)
        {
            if (GodotObject.IsInstanceValid(unit)) DeleteNodeExternal(unit);
        }
        
        SelectedUnits.Clear();
        AllUnits.Clear();
        ClearAllBuildQueueGhosts();
        AllProps.Clear();
        Realm.Client.PropMultiMeshManager.Instance?.Clear();
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
    }
    
    private void ClearAllNodesAndTerrain()
    {
        var childrenCopy = new List<Node>(GetChildren());
        foreach (var child in childrenCopy)
        {
            if ((child is Realm.Client.Prop3D prop && GodotObject.IsInstanceValid(prop)) ||
                (child is Decal decal && GodotObject.IsInstanceValid(decal)) ||
                (child is ProceduralVfxInstance3D vfx && GodotObject.IsInstanceValid(vfx)))
            {
                DeleteNodeExternal(child);
            }
        }

        if (GroundTerrain != null)
        {
            ResetTerrainData();
        }
    }
    
    private void ResetTerrainData()
    {
        int width = GroundTerrain.Width;
        int depth = GroundTerrain.Depth;

        InitializeTerrainArrays(width, depth);
        PopulateTerrainCells(width, depth);
        PopulateSplatMaps(width, depth);
        UpdateEcsTerrainState();

        GroundTerrain.UpdateMeshAndPhysics(true, true);
        GroundTerrain.UpdatePathingTexture();
        UpdatePathingOverlay();
    }

    private void InitializeTerrainArrays(int width, int depth)
    {
        if (NeedsTerrainCellArrayRecreation(GroundTerrain.Cells, width, depth))
            GroundTerrain.Cells = new Realm.Ecs.Components.Terrain.TerrainCell[width, depth];
            
        if (NeedsSplatMapArrayRecreation(GroundTerrain.SplatMap, width, depth))
            GroundTerrain.SplatMap = new TerrainSplatWeights[width + 1, depth + 1];
            
        if (NeedsSplatMapArrayRecreation(GroundTerrain.CliffSplatMap, width, depth))
            GroundTerrain.CliffSplatMap = new TerrainSplatWeights[width + 1, depth + 1];

        if (NeedsPathingArrayRecreation(GroundTerrain.PathingCodes, width, depth))
            GroundTerrain.PathingCodes = new int[width, depth];
    }

    private bool NeedsTerrainCellArrayRecreation(Realm.Ecs.Components.Terrain.TerrainCell[,] array, int width, int depth)
    {
        return array == null || array.GetLength(0) != width || array.GetLength(1) != depth;
    }

    private bool NeedsSplatMapArrayRecreation(TerrainSplatWeights[,] array, int width, int depth)
    {
        return array == null || array.GetLength(0) < width + 1 || array.GetLength(1) < depth + 1;
    }

    private bool NeedsPathingArrayRecreation(int[,] array, int width, int depth)
    {
        return array == null || array.GetLength(0) != width || array.GetLength(1) != depth;
    }

    private void PopulateTerrainCells(int width, int depth)
    {
        int defaultPathing = Realm.Client.EditableTerrain.GetDefaultPathingCode(Realm.Ecs.Components.Terrain.WaterType.None);
        for (int z = 0; z < depth; z++)
        {
            for (int x = 0; x < width; x++)
            {
                GroundTerrain.Cells[x, z] = new Realm.Ecs.Components.Terrain.TerrainCell(0.0f);
                GroundTerrain.PathingCodes[x, z] = defaultPathing;
            }
        }
    }

    private void PopulateSplatMaps(int width, int depth)
    {
        for (int z = 0; z <= depth; z++)
        {
            for (int x = 0; x <= width; x++)
            {
                GroundTerrain.SplatMap[x, z] = TerrainSplatWeights.CreateSolid(0);
                GroundTerrain.CliffSplatMap[x, z] = TerrainSplatWeights.CreateSolid(1);
            }
        }
    }

    private void UpdateEcsTerrainState()
    {
        if (EcsWorld != null && EcsWorld.IsAlive(WorldEntity) && EcsWorld.Has<Realm.Ecs.Components.Terrain.TerrainState>(WorldEntity))
        {
            ref var ts = ref EcsWorld.Get<Realm.Ecs.Components.Terrain.TerrainState>(WorldEntity);
            ts.Cells = GroundTerrain.Cells;
            ts.PathingCodes = GroundTerrain.PathingCodes;
            EcsWorld.Set(WorldEntity, ts);
        }
    }
    
    private void RefreshUIAndExternalState()
    {
        Realm.Client.UI.MapEditorHUD.Instance?.RefreshCoordinateListExternal();
        Realm.Client.UI.MapEditorHUD.ResetFolderLocations();
        Realm.Client.UI.MapEditorHUD.Instance?.ClearTempWorkspaceExternal();
        Realm.Client.UI.MapEditorHUD.Instance?.GenerateVSCodeFilesExternal();
        Realm.Client.UI.MapEditorHUD.Instance?.ReadMetadataAndRefreshTextures();
        Realm.Client.UI.MapEditorHUD.Instance?.LoadMapProperties();
        Realm.Client.UI.MapEditorHUD.Instance?.UpdateMapNameHeader();
        Realm.Client.UI.MapEditorHUD.Instance?.SaveCurrentDirectoryBlake3();
        Realm.Client.UI.MapEditorHUD.Instance?.ShowFeedbackExternal("Map reset: cleared all entities & terrain");
        Realm.Client.UI.MapEditorHUD.Instance?.RegenerateMinimap();
    }


    private long _lastTerrainMeshRebuildMs = long.MinValue;
    private Rect2I? _terrainFlushRegion;
    private bool _terrainGeometryDirty;
    private bool _terrainHeightsDirty;
    private bool _terrainPathingDirty;
    private const float TerrainMeshRebuildPeriodMs = 33.3f;

    public class DecalAssetData
    {
        public string DecalId = "";
        public string TexturePath = "";
        public Texture2D PrimaryTexture;
        public Texture2D? PrimaryNormal;
        public Texture2D[]? AlbedoFrames;
        public Texture2D[]? NormalFrames;
        public int Columns = 1;
        public int Rows = 1;
        public float Fps = 12.0f;
        public bool SubframeBlend = true;
        public bool IsAnimated => Columns > 1 || Rows > 1;
    }

    private readonly System.Collections.Generic.Dictionary<string, DecalAssetData> _decalAssetCache = new();

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
            if (current is Realm.Client.Prop3D prop)
            {
                AllProps.Remove(prop);
                EntityToProp3D.Remove(prop.Entity);
                if (EcsWorld.IsAlive(prop.Entity))
                {
                    EcsWorld.Destroy(prop.Entity);
                }
                Realm.Client.PropMultiMeshManager.Instance?.MarkDirty(prop.PropId);
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

    public Realm.Client.Unit3D SpawnUnitExternal(string unitId, Vector3 position, bool isEnemy, float rotationY, float scale, int player = -1)
    {
        // Preserve the authored Y (saved maps, pasted/cloned/undone objects). Placement paths
        // that want feet-on-terrain snap the Y to the terrain before calling this method.
        int playerIndex = player >= 0 ? player : 0;
        bool actualIsEnemy = player >= 0 ? NetworkService.ArePlayerIndicesEnemies(LocalPlayerIndex, playerIndex) : isEnemy;
        bool isBuilding = false;
        if (!UnitRegistry.ContainsKey(unitId) && !BuildingRegistry.ContainsKey(unitId))
        {
            LoadUnitMetadata(!string.IsNullOrEmpty(CurrentMapDirectory) ? CurrentMapDirectory : Godot.ProjectSettings.GlobalizePath(Realm.Client.UI.MapEditorHUD.TempWorkspaceGodotPath));
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
        unit3D.Visible = true;
        unit3D.UpdateLodVisibility();

        EcsWorld.SetOrAdd(entity, new CollisionScale(scale));

        EcsWorld.SetOrAdd(entity, new RotationY(rotationY));

        EcsWorld.SetOrAdd(entity, new ModelScale(scale));

        return unit3D;
    }

    private bool TryResolvePropExternalId(ref string propId, ref string cleanId)
    {
        bool inProp = CheckIfInRegistry(PropRegistry, propId, cleanId);
        bool inRes = CheckIfInRegistry(ResourceRegistry, propId, cleanId);
        if (inProp || inRes) return true;

        LoadUnitMetadata(!string.IsNullOrEmpty(CurrentMapDirectory) ? CurrentMapDirectory : Godot.ProjectSettings.GlobalizePath(Realm.Client.UI.MapEditorHUD.TempWorkspaceGodotPath));
        inProp = CheckIfInRegistry(PropRegistry, propId, cleanId);
        inRes = CheckIfInRegistry(ResourceRegistry, propId, cleanId);
        if (inProp || inRes) return true;

        inProp = TryResolvePropIdByModelPath(ref propId, ref cleanId);
        if (inProp) return true;

        return TryResolveResourceIdByModelPath(ref propId, ref cleanId);
    }

    public Realm.Client.Prop3D SpawnPropExternalWithParams(string propId, Vector3 position, float rotationY, float scale)
    {
        if (string.IsNullOrEmpty(propId)) return null;

        string cleanId = System.IO.Path.GetFileNameWithoutExtension(propId);
        if (!TryResolvePropExternalId(ref propId, ref cleanId)) return null;

        float defaultAmount = GetDefaultResourceAmount(propId);

        var entity = EcsWorld.Create();
        EcsWorld.Add(entity, new PropIdentity(propId));
        TryAddResourceNodeComponent(entity, propId, cleanId, defaultAmount);

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
            Realm.Client.PropMultiMeshManager.Instance?.MarkDirty(propId);
            return null;
        }

        var prop = new Realm.Client.Prop3D();
        prop.Entity = entity;
        prop.PropId = propId;
        prop.Position = position;
        prop.RotationDegrees = new Vector3(0.0f, rotationY, 0.0f);
        prop.Scale = Vector3.One * (scale <= 0.001f ? 1.0f : scale);
        AddChild(prop);
        AllProps.Add(prop);
        Realm.Client.PropMultiMeshManager.Instance?.MarkDirty(propId);

        EntityToProp3D[entity] = prop;

        return prop;
    }

    private bool CheckIfInRegistry<T>(System.Collections.Generic.Dictionary<StringName, T> registry, string id, string cleanId)
    {
        return registry.ContainsKey(id) || (!string.IsNullOrEmpty(cleanId) && registry.ContainsKey(cleanId));
    }

    private bool TryResolvePropIdByModelPath(ref string propId, ref string cleanId)
    {
        foreach (var kvp in PropRegistry)
        {
            if (string.Equals(kvp.Value.ModelPath, propId, StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrEmpty(cleanId) && string.Equals(System.IO.Path.GetFileNameWithoutExtension(kvp.Value.ModelPath), cleanId, StringComparison.OrdinalIgnoreCase)))
            {
                propId = kvp.Key;
                cleanId = System.IO.Path.GetFileNameWithoutExtension(propId);
                return true;
            }
        }
        return false;
    }

    private bool TryResolveResourceIdByModelPath(ref string propId, ref string cleanId)
    {
        foreach (var kvp in ResourceRegistry)
        {
            if (string.Equals(kvp.Value.ModelPath, propId, StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrEmpty(cleanId) && string.Equals(System.IO.Path.GetFileNameWithoutExtension(kvp.Value.ModelPath), cleanId, StringComparison.OrdinalIgnoreCase)))
            {
                propId = kvp.Key;
                cleanId = System.IO.Path.GetFileNameWithoutExtension(propId);
                return true;
            }
        }
        return false;
    }

    private float GetDefaultResourceAmount(string propId)
    {
        return propId switch
        {
            "goldmine" => 2000f,
            "rock" => 1000f,
            "tree" => 500f,
            _ => 0f
        };
    }

    private void TryAddResourceNodeComponent(Arch.Core.Entity entity, string propId, string cleanId, float defaultAmount)
    {
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
        var config = customConfig != null ? customConfig.Clone() : ResolveVfxConfig(vfxId);

        if (normalOffset != 0f) config.SurfaceNormalOffset = normalOffset;

        Vector3 safeScale = scale;
        if (Mathf.IsZeroApprox(safeScale.X)) safeScale.X = 1f;
        if (Mathf.IsZeroApprox(safeScale.Y)) safeScale.Y = 1f;
        if (Mathf.IsZeroApprox(safeScale.Z)) safeScale.Z = 1f;

        var vfx = new ProceduralVfxInstance3D(config)
        {
            Entity = entity,
            Position = position,
            RotationDegrees = rotationDegrees,
            Scale = safeScale
        };

        AddChild(vfx);
        AllVfx.Add(vfx);
        EntityToVfx3D[entity] = vfx;

        EcsWorld.Add(entity, new Realm.Ecs.Components.Core.Position(new System.Numerics.Vector3(position.X, position.Y, position.Z)));
        EcsWorld.Add(entity, new RotationY(rotationDegrees.Y));
        EcsWorld.Add(entity, new ModelScale(safeScale.X));

        return vfx;
    }

    private VfxAttachmentConfig ResolveVfxConfig(string vfxId)
    {
        string cleanId = vfxId ?? string.Empty;
        if (cleanId.StartsWith("vfx:", StringComparison.OrdinalIgnoreCase)) cleanId = cleanId.Substring(4);

        if (!string.IsNullOrEmpty(vfxId) && VfxRegistry.TryGetValue(vfxId, out var regCfg)) return regCfg.Clone();
        if (!string.IsNullOrEmpty(cleanId) && VfxRegistry.TryGetValue(cleanId, out var regCfg2)) return regCfg2.Clone();

        var cfgFromCleanId = TryCreateVfxConfigFromPrimitive(cleanId);
        if (cfgFromCleanId != null) return cfgFromCleanId;

        var cfgFromVfxId = TryCreateVfxConfigFromPrimitive(vfxId);
        if (cfgFromVfxId != null) return cfgFromVfxId;

        return CreateFallbackVfxConfig(vfxId, cleanId);
    }
    
    private VfxAttachmentConfig TryCreateVfxConfigFromPrimitive(string id)
    {
        if (!string.IsNullOrEmpty(id) && Enum.TryParse<VfxPrimitiveType>(id, true, out var primType))
        {
            var cfg = new VfxAttachmentConfig { VfxId = id, PrimitiveType = primType };
            if (primType == VfxPrimitiveType.ParticleSystem) cfg.ParticleConfig = SpellParticleConfig.CreatePreset(id);
            return cfg;
        }
        return null;
    }


    private VfxAttachmentConfig CreateFallbackVfxConfig(string vfxId, string cleanId)
    {
        var particlePresets = SpellParticleConfig.GetAllPresets();
        if (particlePresets.ContainsKey(cleanId) || cleanId.StartsWith("particle_", StringComparison.OrdinalIgnoreCase))
        {
            return new VfxAttachmentConfig
            {
                VfxId = cleanId,
                Name = cleanId,
                PrimitiveType = VfxPrimitiveType.ParticleSystem,
                ParticleConfig = SpellParticleConfig.CreatePreset(cleanId)
            };
        }

        return new VfxAttachmentConfig
        {
            VfxId = vfxId,
            Name = cleanId,
            BaseTexture = cleanId,
            PrimitiveType = VfxPrimitiveType.VortexDisc
        };
    }

    public static Basis CreateAlignedBasis(Vector3 normal)
    {
        Vector3 up = normal.Normalized();
        Vector3 tangent = (Mathf.Abs(up.Dot(Vector3.Up)) > 0.9f) ? Vector3.Right : Vector3.Up;
        Vector3 right = tangent.Cross(up).Normalized();
        Vector3 forward = up.Cross(right).Normalized();
        return new Basis(right, up, forward);
    }

    private readonly System.Collections.Generic.Dictionary<(int, int), ImageTexture> _decalOrmCache = new();
    private readonly System.Collections.Generic.Dictionary<string, ImageTexture> _decalNormalCache = new();

    public void SetUnitPlayerExternal(Realm.Client.Unit3D unit, int playerIndex)
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

    public void SetUnitTeamExternal(Realm.Client.Unit3D unit, bool isEnemy)
    {
        int targetPlayer = isEnemy ? 1 : 0;
        if (Realm.Client.Network.LobbyManager.Instance != null && Realm.Client.Network.LobbyManager.Instance.PlayerList.Count > 0)
        {
            var enemyPlayer = Realm.Client.Network.LobbyManager.Instance.PlayerList.Find(p => NetworkService.ArePlayerIndicesEnemies(LocalPlayerIndex, p.Slot));
            if (isEnemy && enemyPlayer != null)
            {
                targetPlayer = enemyPlayer.Slot;
            }
        }
        SetUnitPlayerExternal(unit, targetPlayer);
    }

    private Node3D? _editorCoverageOverlayRoot;

    /// <summary>
    ///     When enabled, the editor draws vision/attack range rings around the selected unit.
    ///     Off by default so the overlay only appears on demand.
    /// </summary>
    public bool EditorCoverageOverlayEnabled = false;

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
            if (child is Realm.Client.Prop3D prop)
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

    private MeshInstance3D _measureMeshInstance;
    private ImmediateMesh _measureImmediateMesh;

    private MeshInstance3D _symmetryPivotMarkerMesh;

    public MeshInstance3D BrushIndicatorMesh => _brushIndicatorMesh;
    public MeshInstance3D? GridOverlayMesh => null;
    public MeshInstance3D? PathingOverlayMesh => null;

    public struct MirroredTransform
    {
        public Vector3 Position;
        public float Rotation;
    }

    private Node3D FindObjectNearPosition(Vector3 position, float searchRadius = 1.5f)
    {
        foreach (var child in GetChildren())
        {
            if (child is Node3D n3d && GodotObject.IsInstanceValid(n3d))
            {
                if (n3d is Realm.Client.Unit3D || n3d is Realm.Client.Prop3D || n3d is Decal)
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

        ScaleEntityPositions(AllUnits, scaleX, scaleZ);
        ScaleEntityPositions(AllProps, scaleX, scaleZ);
        ScaleDecals(scaleX, scaleZ);
        ScaleVfx(scaleX, scaleZ);

        ScalePropEntitiesOnly(scaleX, scaleZ);

        ScaleCoordinateBounds(scaleX, scaleZ);

        EditorCameraBoundsLeft *= scaleX;
        EditorCameraBoundsRight *= scaleX;
        EditorCameraBoundsTop *= scaleZ;
        EditorCameraBoundsBottom *= scaleZ;

        DeleteEntitiesOutsideBounds();
        Realm.Client.PropMultiMeshManager.Instance?.RebuildAll();

        _editorService.SetTerrainSplatMap(GroundTerrain.SplatMap, GroundTerrain.CliffSplatMap);

        UpdateScaleMetadata(newWidth, newDepth);
        RefreshUIForMapScale(newWidth, newDepth);

        var after = MapStateSnapshot.CreateSnapshot();
        EditorHistoryManager.RecordAction(new MapResizeAction(before, after));
    }
    
    private void ScaleEntityPositions<T>(IEnumerable<T> entities, float scaleX, float scaleZ) where T : Node3D
    {
        foreach (var entity in entities)
        {
            if (entity == null || !GodotObject.IsInstanceValid(entity)) continue;
            
            var pos = entity.Position;
            entity.Position = new Godot.Vector3(pos.X * scaleX, pos.Y, pos.Z * scaleZ);
            
            UpdateEntityEcsPosition(entity);
        }
    }

    private void UpdateEntityEcsPosition(Node3D entity)
    {
        if (EcsWorld == null) return;
        
        if (entity is Realm.Client.Unit3D u && EcsWorld.IsAlive(u.Entity) && EcsWorld.Has<Realm.Ecs.Components.Core.Position>(u.Entity))
        {
            EcsWorld.Set(u.Entity, new Realm.Ecs.Components.Core.Position(new System.Numerics.Vector3(u.Position.X, u.Position.Y, u.Position.Z)));
        }
        else if (entity is Realm.Client.Prop3D p && EcsWorld.IsAlive(p.Entity) && EcsWorld.Has<Realm.Ecs.Components.Core.Position>(p.Entity))
        {
            EcsWorld.Set(p.Entity, new Realm.Ecs.Components.Core.Position(new System.Numerics.Vector3(p.Position.X, p.Position.Y, p.Position.Z)));
        }
    }
    
    private void ScalePropEntitiesOnly(float scaleX, float scaleZ)
    {
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
    }
    
    private void ScaleDecals(float scaleX, float scaleZ)
    {
        foreach (var decal in AllDecals)
        {
            if (GodotObject.IsInstanceValid(decal))
            {
                decal.Position = new Godot.Vector3(decal.Position.X * scaleX, decal.Position.Y, decal.Position.Z * scaleZ);
                if (decal is Realm.Client.Decal3D decal3D && EcsWorld != null && EcsWorld.IsAlive(decal3D.Entity) && EcsWorld.Has<Realm.Ecs.Components.Core.Position>(decal3D.Entity))
                {
                    EcsWorld.Set(decal3D.Entity, new Realm.Ecs.Components.Core.Position(new System.Numerics.Vector3(decal.Position.X, decal.Position.Y, decal.Position.Z)));
                }
            }
        }
    }
    
    private void ScaleVfx(float scaleX, float scaleZ)
    {
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
    }
    
    private void ScaleCoordinateBounds(float scaleX, float scaleZ)
    {
        for (int i = 0; i < EditorCoordinates.Count; i++)
        {
            var coord = EditorCoordinates[i];
            coord.MinX *= scaleX;
            coord.MaxX *= scaleX;
            coord.MinZ *= scaleZ;
            coord.MaxZ *= scaleZ;
        }
    }
    
    private void UpdateScaleMetadata(int newWidth, int newDepth)
    {
        string scaleWsPath = Realm.Client.Services.MapWorkspaceService.GetActiveWorkspacePath();
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
    }
    
    private void RefreshUIForMapScale(int newWidth, int newDepth)
    {
        RebuildAllCoordinatePersistentMeshes();
        Realm.Client.UI.MapEditorHUD.Instance?.RefreshCoordinateListExternal();
        RebuildCameraBoundsOverlay();
        Realm.Client.UI.MapEditorHUD.Instance?.UpdateCameraBoundsUI();
        Realm.Client.UI.MapEditorHUD.Instance?.RegenerateMinimap();

        EditorHasUnsavedChanges = true;
        Realm.Client.UI.MapEditorHUD.Instance?.ShowFeedbackExternal($"Map scaled to {newWidth}x{newDepth}");
    }

    private void DeleteEntitiesOutsideBounds()
    {
        if (GroundTerrain == null) return;

        float halfW = GroundTerrain.Width / 2.0f * GroundTerrain.QuadSize;
        float halfD = GroundTerrain.Depth / 2.0f * GroundTerrain.QuadSize;

        DeleteNodesOutsideBounds(AllUnits, halfW, halfD);
        DeleteNodesOutsideBounds(AllProps, halfW, halfD);
        DeleteNodesOutsideBounds(AllDecals, halfW, halfD);
        if (AllVfx != null)
        {
            DeleteNodesOutsideBounds(AllVfx, halfW, halfD);
        }
    }

    private void DeleteNodesOutsideBounds<T>(IEnumerable<T> nodes, float halfW, float halfD) where T : Node3D
    {
        var toDelete = new List<T>();
        foreach (var node in nodes)
        {
            if (node == null || !GodotObject.IsInstanceValid(node)) continue;
            var pos = node.Position;
            if (pos.X < -halfW || pos.X > halfW || pos.Z < -halfD || pos.Z > halfD)
            {
                toDelete.Add(node);
            }
        }
        foreach (var node in toDelete)
        {
            DeleteNodeExternal(node);
        }
    }


    private MeshInstance3D _scaleMapSilhouetteMesh;

    private readonly List<MeshInstance3D> _symmetryHighlightMeshes = new();

    private int _lastSelectionMinX = -1;
    private int _lastSelectionMinZ = -1;
    private int _lastSelectionMaxX = -1;
    private int _lastSelectionMaxZ = -1;

    private bool _lastSelectionBrushIsSquare = true;

    private bool _wasSelectionHighlightVisible;
    private readonly List<bool> _wasSymmetryHighlightsVisible = new();
    private bool _wasCameraBoundsVisible;
    private bool _wasMeasureMeshVisible;
    private bool _wasSymmetryPivotVisible;
    private bool _wasCoordinatePreviewVisible;
    private bool _wasCoordinateOutlineVisible;
    private bool _wasScaleSilhouetteVisible;
    private bool _wasCoverageOverlayVisible;
}
