using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using Realm.Client.Network;
using Realm.Client.Services;
using Realm.Client.UI;
using Realm.Client.Utils;
using Realm.Client.VFX;
using Realm.Ecs;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Tags;
using Realm.Ecs.Components.Terrain;
using Realm.Ecs.Services;

namespace Realm.Client.Core;

public partial class GameHost
{
	public Node? SpawnObjectExternal(string objectType, string objectId, Vector3 position, float rotationY, Vector3 rotationDegrees, float scale, int player)
	{
		if (objectType == "unit")
		{
			return SpawnUnitExternal(objectId, position, rotationY, scale, player);
		}
		else if (objectType == "prop")
		{
			return SpawnPropExternalWithParams(objectId, position, rotationY, scale);
		}
		else if (objectType == "decal")
		{
			return SpawnDecalExternalWithParams(objectId, position, rotationDegrees, scale);
		}
		else if (objectType == "vfx")
		{
			return SpawnVfxExternalWithParams(objectId, position, rotationDegrees, Vector3.One * (scale <= 0f ? 1.0f : scale));
		}
		else if (objectType == "item")
		{
			return SpawnItemExternal(objectId, position);
		}
		return null;
	}

	private bool MatchesUnit3DKey(Unit3D unit, string normTarget)
	{
		return GameContext.RegistryService.MatchesUnitKey(unit.UnitId, unit.ModelPath, normTarget, IsMatch);
	}

	private bool CheckPropRegistryForMatch(string id, string cleanId, string normTarget)
	{
		return GameContext.RegistryService.CheckPropRegistryForMatch(id, cleanId, normTarget, IsMatch);
	}

	private bool CheckBuildingRegistryForMatch(string id, string cleanId, string normTarget)
	{
		return GameContext.RegistryService.CheckBuildingRegistryForMatch(id, cleanId, normTarget, IsMatch);
	}

	public float GetModelYOffset(object objOrId)
	{
		if (objOrId == null) return 0f;
		string primaryKey = GetSelectedEntityOrAssetKey(objOrId);
		string assetKey = GetModelAssetKey(objOrId);
		return GameContext.ModelOverrideService.GetModelYOffset(primaryKey, assetKey, GameContext.EditorService.NormalizeModelAssetKey);
	}

	private bool TryGetModelYOffset(string key, out float offset)
	{
		return GameContext.ModelOverrideService.TryGetModelYOffset(key, GameContext.EditorService.NormalizeModelAssetKey, out offset);
	}

	private bool TryGetRegistryYOffset(string key, out float offset)
	{
		return GameContext.RegistryService.TryGetRegistryYOffset(key, out offset);
	}


	public float GetModelScale(object objOrId)
	{
		if (objOrId == null) return 1.0f;

		string primaryKey = GetSelectedEntityOrAssetKey(objOrId);
		string assetKey = GetModelAssetKey(objOrId);

		if (GameContext.ModelOverrideService.TryGetScaleFromKeys(primaryKey, assetKey, GameContext.EditorService.NormalizeModelAssetKey, out float scale))
		{
			return scale;
		}

		return GetFallbackScale(objOrId, primaryKey, GameContext.EditorService.NormalizeModelAssetKey(assetKey));
	}

	private bool TryGetModelScale(string key, out float scale)
	{
		return GameContext.ModelOverrideService.TryGetModelScale(key, GameContext.EditorService.NormalizeModelAssetKey, out scale);
	}

	private bool TryGetRegistryScale(string key, out float scale)
	{
		return GameContext.RegistryService.TryGetRegistryScale(key, out scale);
	}

	private float GetFallbackScale(object objOrId, string primaryKey, string normAsset)
	{
		if (objOrId is Unit3D unit && GodotObject.IsInstanceValid(unit))
		{
			if (unit.IsResource) return 2.75f;
			if (unit.IsBuilding) return 1.5f;
			return 1.0f;
		}

		float scale = GameContext.RegistryService.GetScaleFromRegistryKey(primaryKey);
		if (scale > 0f) return scale;

		scale = GameContext.RegistryService.GetScaleFromRegistryKey(normAsset);
		if (scale > 0f) return scale;

		return 1.0f;
	}

	private float GetScaleFromRegistryKey(string key)
	{
		return GameContext.RegistryService.GetScaleFromRegistryKey(key);
	}

	public float GetModelCollisionCircleRatio(object objOrId)
	{
		if (objOrId == null) return 1.0f;
		string primaryKey = GetSelectedEntityOrAssetKey(objOrId);
		string assetKey = GetModelAssetKey(objOrId);
		return GameContext.ModelOverrideService.GetModelCollisionCircleRatio(primaryKey, assetKey, GameContext.EditorService.NormalizeModelAssetKey);
	}

	public float ResolveTemplateCollisionRadius(string templateId)
	{
		if (string.IsNullOrEmpty(templateId)) return GameplayConstants.DefaultCollisionRadius;

		float authoredCollisionRadius = GetModelCollisionCircleRatio(templateId);

		if (UnitRegistry.ContainsKey(templateId)) return GameplayConstants.ResolveContactRadius(authoredCollisionRadius, 1f, GameplayConstants.DefaultCollisionRadius);
		if (BuildingRegistry.ContainsKey(templateId)) return GameplayConstants.ResolveContactRadius(authoredCollisionRadius, 1f, GameplayConstants.DefaultBuildingCollisionRadius);

		return GameplayConstants.ResolveContactRadius(authoredCollisionRadius, 1f, GameplayConstants.DefaultPropCollisionRadius);
	}

	/// <summary>
	///     The body a unit occupies: the replicated radius a screen in a match was told, or what its template authors
	///     where nothing has been stated yet. The model scale is deliberately not part of that fallback - it makes the
	///     mesh larger, and multiplying by it here gave a body the match did not have.
	/// </summary>
	public float ResolveEntityBodyRadius(Entity entity, string templateId)
	{
		if (GameContext.World != null && entity != Entity.Null && GameContext.World.IsAlive(entity) && GameContext.World.Has<CollisionRadius>(entity))
		{
			return GameplayConstants.ResolveBodyRadius(GameContext.World, entity, GameplayConstants.DefaultCollisionRadius);
		}

		return ResolveTemplateCollisionRadius(templateId);
	}

	private float GetRegistryCollisionCircleRatio(string primaryKey)
	{
		return GameContext.RegistryService.GetRegistryCollisionCircleRatio(primaryKey);
	}

	public float GetModelBrightness(object objOrId)
	{
		if (objOrId == null) return 1.0f;
		string primaryKey = GetSelectedEntityOrAssetKey(objOrId);
		string assetKey = GetModelAssetKey(objOrId);
		return GameContext.ModelOverrideService.GetModelBrightness(primaryKey, assetKey, GameContext.EditorService.NormalizeModelAssetKey);
	}

	private bool TryGetModelBrightness(string key, out float brightness)
	{
		return GameContext.ModelOverrideService.TryGetModelBrightness(key, GameContext.EditorService.NormalizeModelAssetKey, out brightness);
	}

	private bool TryGetRegistryBrightness(string key, out float brightness) => GameContext.RegistryService.TryGetRegistryBrightness(key, out brightness);


	public Color GetModelColorTint(object objOrId)
	{
		if (objOrId == null) return new Color(1.0f, 1.0f, 1.0f);
		string primaryKey = GetSelectedEntityOrAssetKey(objOrId);
		string assetKey = GetModelAssetKey(objOrId);
		return GameContext.ModelOverrideService.GetModelColorTint(primaryKey, assetKey, GameContext.EditorService.NormalizeModelAssetKey);
	}

	private bool TryGetModelColorTint(string key, out Color color) => GameContext.ModelOverrideService.TryGetModelColorTint(key, GameContext.EditorService.NormalizeModelAssetKey, out color);

	private bool TryGetRegistryColorTint(string key, out Color color) => GameContext.RegistryService.TryGetRegistryColorTint(key, out color);


	public bool GetModelDespillPlayerColor(object objOrId)
	{
		if (objOrId == null) return false;
		string primaryKey = GetSelectedEntityOrAssetKey(objOrId);
		string assetKey = GetModelAssetKey(objOrId);
		return GameContext.ModelOverrideService.GetModelDespillPlayerColor(primaryKey, assetKey, GameContext.EditorService.NormalizeModelAssetKey);
	}

	public bool GetModelNormalizeLuminance(object objOrId)
	{
		if (objOrId == null) return true;
		string primaryKey = GetSelectedEntityOrAssetKey(objOrId);
		string assetKey = GetModelAssetKey(objOrId);
		return GameContext.ModelOverrideService.GetModelNormalizeLuminance(primaryKey, assetKey, GameContext.EditorService.NormalizeModelAssetKey);
	}

	public void SetModelNormalizeLuminance(string assetKey, bool normalizeLuminance)
	{
		string norm = GameContext.EditorService.NormalizeModelAssetKey(assetKey);
		if (string.IsNullOrEmpty(norm)) return;

		string modelAsset = GetModelAssetKey(assetKey);
		string primaryKey = GetSelectedEntityOrAssetKey(assetKey);

		GameContext.ModelOverrideService.SetModelNormalizeLuminance(assetKey, normalizeLuminance, modelAsset, primaryKey, GameContext.EditorService.NormalizeModelAssetKey);

		UpdateMaterialOverridesForAsset(norm);

		GameContext.EditorService.ModelYOffsetSavePending = true;
		EditorHasUnsavedChanges = true;
	}

	private void UpdateLuminanceInRegistries(string assetKey, bool normalizeLuminance) => GameContext.RegistryService.UpdateLuminanceInRegistries(assetKey, normalizeLuminance);


	public string GetModelDeathShader(object objOrId)
	{
		if (objOrId == null) return "";
		string primaryKey = GetSelectedEntityOrAssetKey(objOrId);
		string assetKey = GetModelAssetKey(objOrId);
		return GameContext.ModelOverrideService.GetModelDeathShader(primaryKey, assetKey, GameContext.EditorService.NormalizeModelAssetKey);
	}


	public bool GetModelEnableProceduralAnimation(object objOrId)
	{
		if (objOrId == null) return false;
		string primaryKey = GetSelectedEntityOrAssetKey(objOrId);
		string assetKey = GetModelAssetKey(objOrId);
		return GameContext.ModelOverrideService.GetModelEnableProceduralAnimation(primaryKey, assetKey, GameContext.EditorService.NormalizeModelAssetKey);
	}

	public void SetModelEnableProceduralAnimation(string assetKey, bool enable)
	{
		string modelAsset = GetModelAssetKey(assetKey);
		GameContext.ModelOverrideService.SetModelEnableProceduralAnimation(assetKey, enable, modelAsset, GameContext.EditorService.NormalizeModelAssetKey, () =>
		{
			GameContext.EditorService.ModelYOffsetSavePending = true;
			EditorHasUnsavedChanges = true;
			GameContext.PropMultiMesh?.RefreshProceduralAnimation(assetKey);
		});
	}

	public bool IsPropOrResourceKey(string key)
	{
		if (string.IsNullOrEmpty(key)) return false;
		string norm = GameContext.EditorService.NormalizeModelAssetKey(key);
		string? resolved = ModelCache.ResolveModelPath(norm);
		return GameContext.RegistryService.IsPropOrResourceKey(key, norm, resolved, GameContext.EditorService.NormalizeModelAssetKey);
	}

	private bool HasMatchingPropOrResourceMetadata(string norm)
	{
		return GameContext.RegistryService.HasMatchingPropOrResourceMetadata(norm, GameContext.EditorService.NormalizeModelAssetKey);
	}

	private bool HasMatchingPropMetadata(string norm)
	{
		return GameContext.RegistryService.HasMatchingPropMetadata(norm, GameContext.EditorService.NormalizeModelAssetKey);
	}

	private bool HasMatchingResourceMetadata(string norm)
	{
		return GameContext.RegistryService.HasMatchingResourceMetadata(norm, GameContext.EditorService.NormalizeModelAssetKey);
	}

	private bool MatchesPropOrResourcePathPattern(string path)
	{
		return GameContext.RegistryService.MatchesPropOrResourcePathPattern(path);
	}


	public bool IsAttachmentKey(string key)
	{
		if (string.IsNullOrEmpty(key)) return false;
		string norm = GameContext.EditorService.NormalizeModelAssetKey(key);

		if (AttachmentRegistry.ContainsKey(key) || AttachmentRegistry.ContainsKey(norm)) return true;
		if (HasMatchingAttachmentMetadata(norm)) return true;

		if (ContainsAttachmentKeyword(key) || ContainsAttachmentKeyword(norm)) return true;

		string? resolved = ModelCache.ResolveModelPath(norm);
		if (!string.IsNullOrEmpty(resolved) && ContainsAttachmentKeyword(resolved)) return true;

		return false;
	}

	private bool HasMatchingAttachmentMetadata(string norm)
	{
		foreach (var attMeta in AttachmentRegistry.Values)
		{
			if (!string.IsNullOrEmpty(attMeta.ModelPath) && GameContext.EditorService.NormalizeModelAssetKey(attMeta.ModelPath) == norm) return true;
			if (!string.IsNullOrEmpty(attMeta.AttachmentId) && GameContext.EditorService.NormalizeModelAssetKey(attMeta.AttachmentId) == norm) return true;
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
		string normPrimary = GameContext.EditorService.NormalizeModelAssetKey(primaryKey);
		if (!string.IsNullOrEmpty(normPrimary) && GameContext.ModelOverrideService.ModelIgnorePlayerColor.TryGetValue(normPrimary, out bool b1)) return b1;

		string assetKey = GetModelAssetKey(objOrId);
		string normAsset = GameContext.EditorService.NormalizeModelAssetKey(assetKey);
		if (!string.IsNullOrEmpty(normAsset) && GameContext.ModelOverrideService.ModelIgnorePlayerColor.TryGetValue(normAsset, out bool b2)) return b2;

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
		if (AttachmentRegistry.TryGetValue(normAsset, out var attachmentMeta)) return attachmentMeta.IgnorePlayerColor;
		return false;
	}

	private bool IsAlwaysIgnoredType(object objOrId, string normPrimary, string normAsset)
	{
		if (objOrId is Prop3D) return true;
		if (IsPropOrResourceKey(normPrimary)) return true;
		if (!string.IsNullOrEmpty(normAsset) && !string.Equals(normAsset, normPrimary, StringComparison.OrdinalIgnoreCase) && IsPropOrResourceKey(normAsset)) return true;
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

		GameContext.PropMultiMesh?.UpdateMaterialOverrides(normAssetKey);
		GameContext.PropMultiMesh?.MarkDirty(normAssetKey);
	}

	private bool ShouldSkipMaterialOverridesUpdate(string normAssetKey)
	{
		if (string.IsNullOrEmpty(normAssetKey)) return true;

		bool hasProps = AllProps.Count > 0;
		bool hasUnits = AllUnits.Count > 0;
		bool hasPreview = _editorPreviewNode != null;
		bool hasMultiMesh = GameContext.PropMultiMesh != null && GameContext.PropMultiMesh.HasGroups;

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
			ApplyMaterialOverridesToNode(_editorPreviewNode, brightness, tint, normalizeLuminance, ignorePlayerColor, _editorPreviewNode is Unit3D);
		}
	}

	public void UpdateAllMaterialOverrides()
	{
		if (AllProps.Count == 0 && AllUnits.Count == 0 && _editorPreviewNode == null && (GameContext.PropMultiMesh == null || !GameContext.PropMultiMesh.HasGroups))
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

		if (_editorPreviewNode is { } previewNode)
		{
			ApplyOverridesToNode(previewNode, previewNode is Unit3D);
		}

		GameContext.PropMultiMesh?.UpdateAllMaterialOverrides();
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

		if (isUnit && !ignorePlayerColor && node is Unit3D unit)
		{
			unit.UpdatePlayerColorVisual();
		}
	}

	public void ApplyAllGlobalOverridesToObject(object objOrNode)
	{
		if (objOrNode == null) return;

		if (objOrNode is Unit3D unit && GodotObject.IsInstanceValid(unit))
		{
			ApplyGlobalOverridesToUnit(unit);
		}
		else if (objOrNode is Prop3D prop && GodotObject.IsInstanceValid(prop))
		{
			ApplyGlobalOverridesToProp(prop);
		}
	}

	private void ApplyGlobalOverridesToUnit(Unit3D unit)
	{
		unit.UpdateModelScale(GetModelScale(unit));
		unit.UpdateModelYOffset(GetModelYOffset(unit));

		float circleRatio = GetModelCollisionCircleRatio(unit);
		unit.UpdateCollisionCircleScale(circleRatio);

		if (GameContext.ServerSession == null)
		{
			float baseRadius = ResolveTemplateCollisionRadius(unit.UnitId);
			GameContext.ModelOverrideService.UpdateCollisionRadius(unit.Entity, baseRadius);
		}

		if (unit.IsPreview) return;

		ApplyMaterialOverridesToUnit(unit);
		ApplyProceduralAnimation(unit, GetModelEnableProceduralAnimation(unit), GetModelProceduralAnimation(unit));
	}

	private void ApplyMaterialOverridesToUnit(Unit3D unit)
	{
		float brightness = GetModelBrightness(unit);
		Color tint = GetModelColorTint(unit);
		bool ignorePlayerColor = GetModelIgnorePlayerColor(unit);
		bool normalizeLuminance = GetModelNormalizeLuminance(unit);

		ApplyMaterialOverridesToNode(unit, brightness, tint, normalizeLuminance, ignorePlayerColor, true);
		if (!ignorePlayerColor) unit.UpdatePlayerColorVisual();
	}

	private void ApplyGlobalOverridesToProp(Prop3D prop)
	{
		prop.UpdateVisualScale(GetModelScale(prop));
		prop.UpdateVisualYOffset(GetModelYOffset(prop));

		float circleRatio = GetModelCollisionCircleRatio(prop);
		prop.UpdateCollisionCircleScale(circleRatio);

		float baseRadius = ResolveTemplateCollisionRadius(prop.PropId);
		GameContext.ModelOverrideService.UpdateCollisionRadius(prop.Entity, baseRadius);

		if (prop.IsPreview) return;

		ApplyMaterialOverridesToProp(prop);
		ApplyProceduralAnimation(prop, GetModelEnableProceduralAnimation(prop), GetModelProceduralAnimation(prop));
	}

	private void ApplyMaterialOverridesToProp(Prop3D prop)
	{
		float brightness = GetModelBrightness(prop);
		Color tint = GetModelColorTint(prop);
		bool ignorePlayerColor = GetModelIgnorePlayerColor(prop);
		bool normalizeLuminance = GetModelNormalizeLuminance(prop);

		ApplyMaterialOverridesToNode(prop, brightness, tint, normalizeLuminance, ignorePlayerColor, false);
	}

	public void ReapplyProceduralAnimation(Unit3D unit) =>
		ApplyProceduralAnimation(unit, GetModelEnableProceduralAnimation(unit), GetModelProceduralAnimation(unit));

	public void SuspendProceduralAnimation(Node3D node) =>
		ModelShaderManager.DisableProceduralAnimation(node);

	private void ApplyProceduralAnimation(Node3D node, bool enable, string procAnimId)
	{
		if (enable && !string.IsNullOrEmpty(procAnimId))
		{
			var cfg = ProceduralAnimationManager.GetConfig(procAnimId);
			if (cfg != null)
			{
				if (node is Prop3D propWithAnim) propWithAnim.HasProceduralAnimation = true;
				ModelShaderManager.SetProceduralAnimation(node, cfg, modelKey: GetModelAssetKey(node));
				return;
			}
		}

		if (node is Prop3D propNoAnim)
		{
			if (!propNoAnim.HasProceduralAnimation) return;
			propNoAnim.HasProceduralAnimation = false;
		}
		ModelShaderManager.DisableProceduralAnimation(node);
	}

}