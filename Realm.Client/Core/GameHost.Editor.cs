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

	/// <summary>
	///     The body radius a template declares in metadata.json, or the shared default for its kind when it declares
	///     none. The authoritative process builds units, buildings, props and resource nodes through the same resolvers,
	///     so a screen and the match it is watching agree on how much ground a body occupies instead of one measuring a
	///     model the other cannot see.
	/// </summary>
	public float ResolveTemplateCollisionRadius(string templateId)
	{
		if (string.IsNullOrEmpty(templateId)) return GameplayConstants.DefaultCollisionRadius;

		float authoredCollisionRadius = GetModelCollisionCircleRatio(templateId);

		if (UnitRegistry.ContainsKey(templateId)) return GameplayConstants.ResolveContactRadius(authoredCollisionRadius, 1f, GameplayConstants.DefaultCollisionRadius);
		if (BuildingRegistry.ContainsKey(templateId)) return GameplayConstants.ResolveContactRadius(authoredCollisionRadius, 1f, GameplayConstants.DefaultBuildingCollisionRadius);

		return GameplayConstants.ResolveContactRadius(authoredCollisionRadius, 1f, GameplayConstants.DefaultPropCollisionRadius);
	}

	/// <summary>
	///     The body radius an entity actually has: the number the simulation separates and paths around when the entity
	///     carries one, which is what a replicated unit, building or prop always does. A preview node that has no entity
	///     yet falls back to what its template declares, scaled by its placed model scale.
	/// </summary>
	public float ResolveEntityBodyRadius(Entity entity, string templateId)
	{
		if (GameContext.World != null && entity != Entity.Null && GameContext.World.IsAlive(entity) && GameContext.World.Has<CollisionRadius>(entity))
		{
			return GameplayConstants.ResolveBodyRadius(GameContext.World, entity, GameplayConstants.DefaultCollisionRadius);
		}

		// A body that has not been told its size yet falls back to what its template authors. The model scale is not part
		// of that number: it makes the mesh larger, and multiplying by it here drew a ring around a fire element that
		// was the size of its template times three while the same building on another screen drew the template's own
		// radius.
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

	private bool TryGetModelDeathShader(string key, out string shader) => GameContext.ModelOverrideService.TryGetModelDeathShader(key, GameContext.EditorService.NormalizeModelAssetKey, out shader);

	private bool TryGetRegistryDeathShader(string key, out string shader) => GameContext.RegistryService.TryGetRegistryDeathShader(key, out shader);


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
		string resolved = ModelCache.ResolveModelPath(norm);
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

		string resolved = ModelCache.ResolveModelPath(norm);
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

		if (_editorPreviewNode != null)
		{
			ApplyOverridesToNode(_editorPreviewNode, _editorPreviewNode is Unit3D);
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

		float baseRadius = ResolveTemplateCollisionRadius(unit.UnitId);
		GameContext.ModelOverrideService.UpdateCollisionRadius(unit.Entity, baseRadius);

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
		bool isDefaultColor = MathF.Abs(brightness - 1.0f) < 0.001f && tint == new Color(1.0f, 1.0f, 1.0f);

		ApplyNodeShaderSettings(node, brightness, tint, normalizeLuminance, isUnit, ignorePlayerColor, isDefaultColor);
		ApplyMeshOverrides(node, brightness, tint, isUnit, ignorePlayerColor, isDefaultColor);
	}

	private static void ApplyNodeShaderSettings(Node node, float brightness, Color tint, bool normalizeLuminance, bool isUnit, bool? ignorePlayerColor, bool isDefaultColor)
	{
		if (!isDefaultColor)
		{
			ModelShaderManager.SetBrightnessAndTint(node, brightness, tint);
		}

		ModelShaderManager.RefreshShaderMaterialsForNode(node, normalizeLuminance);
		ModelShaderManager.SetUnitReadability(node, isUnit);

		if (ignorePlayerColor.HasValue)
		{
			ModelShaderManager.SetIgnorePlayerColor(node, ignorePlayerColor.Value);
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
			if (prop.Entity != Entity.Null && GameContext.World.IsAlive(prop.Entity))
			{
				GameContext.World.SetOrAdd(prop.Entity, new CollisionRadius(ResolveTemplateCollisionRadius(prop.PropId)));
			}
		}
	}

	private void UpdateUnitCollisionRadii(string normAssetKey, float ratio)
	{
		foreach (var unit in AllUnits)
		{
			if (!GodotObject.IsInstanceValid(unit) || !MatchesEntityOrAssetKey(unit, normAssetKey)) continue;

			unit.UpdateCollisionCircleScale(ratio);
			if (unit.Entity != Entity.Null && GameContext.World.IsAlive(unit.Entity))
			{
				GameContext.World.SetOrAdd(unit.Entity, new CollisionRadius(ResolveTemplateCollisionRadius(unit.UnitId)));
			}
		}
	}

	private float MaxSafeModelYOffset { get; set; } = ModelOverrideService.MaxSafeModelYOffset;
	private float MinSafeModelCollisionRatio { get; set; } = ModelOverrideService.MinSafeModelCollisionRatio;
	private float MaxSafeModelCollisionRatio { get; set; } = ModelOverrideService.MaxSafeModelCollisionRatio;
	private float MinSafeModelScale { get; set; } = 0.01f;
	private float MaxSafeModelScale { get; set; } = 20f;

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
			Prop3D.InvalidateModelPathCache(targetId);
		}
		else
		{
			ModelCache.Clear();
			Prop3D.ClearModelPathCache();
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
		return GameContext.EditorService.IsTargetUnit(unitId, targetId);
	}

	private void RefreshSingleUnitModel(Unit3D unit)
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
		return GameContext.RegistryService.GetUnitTargetModel(unitId, ref isBuilding);
	}

	private void RefreshPropsModels(string targetId)
	{
		foreach (var prop in AllProps)
		{
			if (!GodotObject.IsInstanceValid(prop)) continue;
			if (GameContext.EditorService.IsTargetProp(prop.PropId, targetId))
			{
				prop.RefreshPropVisual();
			}
		}

		if (!string.IsNullOrEmpty(targetId))
		{
			GameContext.PropMultiMesh?.MarkDirty(targetId);
		}
	}


	private void UpdateMetadataModelOverrides(MapMetadata meta)
	{
		if (meta.Templates != null)
		{
			UpdateEntityOverrides(meta.Templates.Units);
			UpdateEntityOverrides(meta.Templates.Buildings);
			UpdateResourceOverrides(meta.Templates.Resources);
			UpdatePropOverrides(meta.Templates.Props);
		}
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
			(entity.ProceduralAnimation, entity.EnableProceduralAnimation) = ResolveProceduralAnimationOverrides(entity.TemplateID, entity.ProceduralAnimation, entity.EnableProceduralAnimation);

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
			(res.ProceduralAnimation, res.EnableProceduralAnimation) = ResolveProceduralAnimationOverrides(res.TemplateID, res.ProceduralAnimation, res.EnableProceduralAnimation);

			resources[i] = res;
		}
	}

	private (string? AnimationId, bool? Enabled) ResolveProceduralAnimationOverrides(string templateId, string? animationId, bool? enabled)
	{
		string norm = GameContext.EditorService.NormalizeModelAssetKey(templateId);
		if (string.IsNullOrEmpty(norm)) return (animationId, enabled);

		if (GameContext.ModelOverrideService.ModelProceduralAnimations.TryGetValue(norm, out string configuredAnimationId))
		{
			animationId = string.IsNullOrWhiteSpace(configuredAnimationId) ? null : configuredAnimationId;
		}

		if (GameContext.ModelOverrideService.ModelEnableProceduralAnimations.TryGetValue(norm, out bool configuredEnabled))
		{
			enabled = configuredEnabled;
		}

		return (animationId, enabled);
	}

	private void ApplyVisualOverrides(string templateId, string modelPath, ref bool despill, ref bool normLuma, ref bool ignoreColor, ref string spawnShader, ref string deathShader)
	{
		string normKey = GameContext.EditorService.NormalizeModelAssetKey(templateId);
		string normModel = !string.IsNullOrEmpty(modelPath) ? GameContext.EditorService.NormalizeModelAssetKey(modelPath) : "";

		despill = GameContext.ModelOverrideService.GetOverrideValue(normKey, normModel, GameContext.ModelOverrideService.ModelDespillPlayerColor, despill);
		normLuma = GameContext.ModelOverrideService.GetOverrideValue(normKey, normModel, GameContext.ModelOverrideService.ModelNormalizeLuminance, normLuma);
		ignoreColor = GameContext.ModelOverrideService.GetOverrideValue(normKey, normModel, GameContext.ModelOverrideService.ModelIgnorePlayerColor, ignoreColor);

		spawnShader = GameContext.ModelOverrideService.GetShaderOverride(normKey, normModel, GameContext.ModelOverrideService.ModelSpawnShaders) ?? GetModelSpawnShader(templateId);
		if (string.IsNullOrWhiteSpace(spawnShader)) spawnShader = null;

		deathShader = GameContext.ModelOverrideService.GetShaderOverride(normKey, normModel, GameContext.ModelOverrideService.ModelDeathShaders) ?? GetModelDeathShader(templateId);
		if (string.IsNullOrWhiteSpace(deathShader)) deathShader = null;
	}


	public void ClearMapEntirely()
	{
		if (GroundTerrain == null) return;

		ClearAllEntities();
		ClearAllNodesAndTerrain();

		_editorObjectSelection.Clear();
		GameContext.EditorService?.ResetAllState();
		HideSelectionHighlight();

		bool temp = false;
		GameContext.EditorService.ClearHistoryAndState(ref temp);
		EditorHasUnsavedChanges = temp;

		MapCameraBounds resetCameraBounds = GroundTerrain != null
			? MapCameraBounds.FromTerrain(GroundTerrain.Width, GroundTerrain.Depth, GroundTerrain.QuadSize)
			: MapCameraBounds.LegacyDefaults;
		GameContext.EditorService.SetCameraBounds(GameContext.Simulation.WorldEntity, resetCameraBounds);
		GameContext.MapEditorHud?.UpdateCameraBoundsUI();
		RebuildCameraBoundsOverlay();

		GameContext.EditorService.EditorCoordinates.Clear();
		RebuildAllCoordinatePersistentMeshes();
		HideCoordinateSelectionOutline();

		RefreshUIAndExternalState();
	}

	private void ClearAllEntities()
	{
		var unitsCopy = new List<Unit3D>(AllUnits);
		foreach (var unit in unitsCopy)
		{
			if (GodotObject.IsInstanceValid(unit)) DeleteNodeExternal(unit);
		}

		SelectedUnits.Clear();
		AllUnits.Clear();
		ClearAllBuildQueueGhosts();
		AllProps.Clear();
		GameContext.PropMultiMesh?.Clear();
		AllDecals.Clear();
		AllVfx.Clear();
		GameContext.FXService.ClearActivePings();
		EntityToUnit3D.Clear();
		EntityIdToUnit3D.Clear();
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
			if ((child is Prop3D prop && GodotObject.IsInstanceValid(prop)) ||
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
		TerrainCell[,] cells = GroundTerrain.Cells;
		if (cells == null || cells.GetLength(0) != width || cells.GetLength(1) != depth)
			GroundTerrain.Cells = new TerrainCell[width, depth];

		TerrainSplatWeights[,] splatMap = GroundTerrain.SplatMap;
		if (splatMap == null || splatMap.GetLength(0) < width + 1 || splatMap.GetLength(1) < depth + 1)
			GroundTerrain.SplatMap = new TerrainSplatWeights[width + 1, depth + 1];

		TerrainSplatWeights[,] cliffSplatMap = GroundTerrain.CliffSplatMap;
		if (cliffSplatMap == null || cliffSplatMap.GetLength(0) < width + 1 || cliffSplatMap.GetLength(1) < depth + 1)
			GroundTerrain.CliffSplatMap = new TerrainSplatWeights[width + 1, depth + 1];

		int[,] pathingCodes = GroundTerrain.PathingCodes;
		if (pathingCodes == null || pathingCodes.GetLength(0) != width || pathingCodes.GetLength(1) != depth)
			GroundTerrain.PathingCodes = new int[width, depth];
	}

	private void PopulateTerrainCells(int width, int depth)
	{
		int defaultPathingCode = EditableTerrain.GetDefaultPathingCode(WaterType.None);
		for (int z = 0; z < depth; z++)
		{
			for (int x = 0; x < width; x++)
			{
				GroundTerrain.Cells[x, z] = new TerrainCell(0.0f);
				GroundTerrain.PathingCodes[x, z] = defaultPathingCode;
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
		if (GameContext.World != null && GameContext.World.IsAlive(GameContext.Simulation.WorldEntity) && GameContext.World.Has<TerrainState>(GameContext.Simulation.WorldEntity))
		{
			ref var ts = ref GameContext.World.Get<TerrainState>(GameContext.Simulation.WorldEntity);
			ts.Cells = GroundTerrain.Cells;
			ts.PathingCodes = GroundTerrain.PathingCodes;
			GameContext.World.Set(GameContext.Simulation.WorldEntity, ts);
		}
	}

	private void RefreshUIAndExternalState()
	{
		GameContext.MapEditorHud?.RefreshCoordinateListExternal();
		MapEditorHUD.ResetFolderLocations();
		GameContext.MapEditorHud?.ClearTempWorkspaceExternal();
		GameContext.MapEditorHud?.GenerateVSCodeFilesExternal();
		GameContext.MapEditorHud?.ReadMetadataAndRefreshTextures();
		GameContext.MapEditorHud?.ViewLoadMapProperties();
		GameContext.MapEditorHud?.UpdateMapNameHeader();
		GameContext.MapEditorHud?.SaveCurrentDirectoryBlake3();
		GameContext.MapEditorHud?.ShowFeedbackExternal("Map reset: cleared all entities & terrain");
		GameContext.MapEditorHud?.RegenerateMinimap();
	}


	private void DeleteObjectAt(Node collider, Vector3 hitPos)
	{
		var unit = FindUnit3DInParentChain(collider);
		if (unit != null)
		{
			SelectedUnits.Remove(unit);
			AllUnits.Remove(unit);
			EntityToUnit3D.Remove(unit.Entity);
			EntityIdToUnit3D.Remove(unit.Entity.Id);
			if (GameContext.World.IsAlive(unit.Entity))
			{
				GameContext.World.Destroy(unit.Entity);
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
				if (GameContext.World.IsAlive(prop.Entity))
				{
					GameContext.World.Destroy(prop.Entity);
				}

				GameContext.PropMultiMesh?.MarkDirty(prop.PropId);
				prop.QueueFree();
				return;
			}

			current = current.GetParent();
		}


		var decal = FindDecal3DInParentChain(collider);
		if (decal != null)
		{
			if (GameContext.World.IsAlive(decal.Entity))
			{
				GameContext.World.Destroy(decal.Entity);
			}

			decal.QueueFree();
		}
	}

	public Unit3D SpawnUnitExternal(string unitId, Vector3 position, float rotationY, float scale, int player = 0)
	{
		// Preserve the authored Y (saved maps, pasted/cloned/undone objects). Placement paths
		// that want feet-on-terrain snap the Y to the terrain before calling this method.
		int playerIndex = player >= 0 ? player : 0;
		bool isBuilding = false;
		if (!UnitRegistry.ContainsKey(unitId) && !BuildingRegistry.ContainsKey(unitId))
		{
			LoadUnitMetadata(!string.IsNullOrEmpty(GameContext.MapStorageService.CurrentMapDirectory) ? GameContext.MapStorageService.CurrentMapDirectory : ProjectSettings.GlobalizePath(GameContext.EditorService.TempWorkspaceGodotPath));
		}

		UnitMetadata meta;
		if (!UnitRegistry.TryGetValue(unitId, out meta))
		{
			if (BuildingRegistry.TryGetValue(unitId, out meta))
				isBuilding = true;
			else
				return null;
		}

		var playerOwner = GetPlayerEntityForPlayerIndex(playerIndex).AsPlayerEntity(GameContext.World);

		if (string.IsNullOrEmpty(meta.ModelPath))
		{
			return null;
		}

		string modelPath = GetFallbackModelPath(meta.ModelPath, isBuilding);

		string name = meta.Name;
		var entity = CreateEcsUnit(unitId, name, meta.MaxHp, meta.Damage, meta.Range, meta.Armor, meta.Speed, position, playerOwner);

		var unit3D = SpawnUnit3D(entity, unitId, modelPath, position, isBuilding, playerIndex);
		unit3D.RotationDegrees = new Vector3(0.0f, rotationY, 0.0f);
		unit3D.Scale = Vector3.One * (scale <= 0.001f ? 1.0f : scale);
		unit3D.Visible = true;
		unit3D.UpdateLodVisibility();

		GameContext.World.SetOrAdd(entity, new CollisionScale(scale));

		GameContext.World.SetOrAdd(entity, new RotationY(rotationY));

		GameContext.World.SetOrAdd(entity, new ModelScale(scale));

		return unit3D;
	}

	private bool TryResolvePropExternalId(ref string propId, ref string cleanId)
	{
		bool inProp = CheckIfInRegistry(PropRegistry, propId, cleanId);
		bool inRes = CheckIfInRegistry(ResourceRegistry, propId, cleanId);
		if (inProp || inRes) return true;

		LoadUnitMetadata(!string.IsNullOrEmpty(GameContext.MapStorageService.CurrentMapDirectory) ? GameContext.MapStorageService.CurrentMapDirectory : ProjectSettings.GlobalizePath(GameContext.EditorService.TempWorkspaceGodotPath));
		inProp = CheckIfInRegistry(PropRegistry, propId, cleanId);
		inRes = CheckIfInRegistry(ResourceRegistry, propId, cleanId);
		if (inProp || inRes) return true;

		inProp = TryResolvePropIdByModelPath(ref propId, ref cleanId);
		if (inProp) return true;

		return TryResolveResourceIdByModelPath(ref propId, ref cleanId);
	}

	public Prop3D SpawnPropExternalWithParams(string propId, Vector3 position, float rotationY, float scale)
	{
		if (string.IsNullOrEmpty(propId)) return null;

		string cleanId = Path.GetFileNameWithoutExtension(propId);
		if (!TryResolvePropExternalId(ref propId, ref cleanId)) return null;

		float defaultAmount = GetDefaultResourceAmount(propId);
		position.Y = GetTerrainHeightAt(position);

		float baseRadius = ResolveTemplateCollisionRadius(propId);

		var entity = GameContext.EditorService.SpawnPropDomain(propId, new System.Numerics.Vector3(position.X, position.Y, position.Z), rotationY, scale, cleanId, defaultAmount, position.Y, baseRadius);

		if (!GameContext.SessionState.IsMapEditorMode && IsStaticPropAsset(propId))
		{
			GameContext.PropMultiMesh?.MarkDirty(propId);
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
		GameContext.PropMultiMesh?.MarkDirty(propId);

		EntityToProp3D[entity] = prop;

		return prop;
	}

	private bool CheckIfInRegistry<T>(Dictionary<StringName, T> registry, string id, string cleanId)
	{
		return registry.ContainsKey(id) || (!string.IsNullOrEmpty(cleanId) && registry.ContainsKey(cleanId));
	}

	private bool TryResolvePropIdByModelPath(ref string propId, ref string cleanId)
	{
		foreach (var kvp in PropRegistry)
		{
			if (string.Equals(kvp.Value.ModelPath, propId, StringComparison.OrdinalIgnoreCase) ||
			    (!string.IsNullOrEmpty(cleanId) && string.Equals(Path.GetFileNameWithoutExtension(kvp.Value.ModelPath), cleanId, StringComparison.OrdinalIgnoreCase)))
			{
				propId = kvp.Key;
				cleanId = Path.GetFileNameWithoutExtension(propId);
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
			    (!string.IsNullOrEmpty(cleanId) && string.Equals(Path.GetFileNameWithoutExtension(kvp.Value.ModelPath), cleanId, StringComparison.OrdinalIgnoreCase)))
			{
				propId = kvp.Key;
				cleanId = Path.GetFileNameWithoutExtension(propId);
				return true;
			}
		}

		return false;
	}

	private float GetDefaultResourceAmount(string propId) => DefinitionCatalog.DefaultResourceAmount(propId);

	public ProceduralVfxInstance3D SpawnVfxExternalWithParams(
		string vfxId,
		Vector3 position,
		Vector3 rotationDegrees,
		Vector3 scale,
		float normalOffset = 0f,
		VfxAttachmentConfig customConfig = null,
		int ownerPlayerIndex = 0)
	{
		var entity = GameContext.World.Create();
		var config = customConfig != null ? customConfig.Clone() : ResolveVfxConfig(vfxId);

		if (normalOffset != 0f) config.SurfaceNormalOffset = normalOffset;

		Vector3 safeScale = scale;
		if (Mathf.IsZeroApprox(safeScale.X)) safeScale.X = 1f;
		if (Mathf.IsZeroApprox(safeScale.Y)) safeScale.Y = 1f;
		if (Mathf.IsZeroApprox(safeScale.Z)) safeScale.Z = 1f;

		var vfx = new ProceduralVfxInstance3D(config)
		{
			Entity = entity,
			PlayerIndex = ownerPlayerIndex,
			Position = position,
			RotationDegrees = rotationDegrees,
			Scale = safeScale
		};

		AddChild(vfx);
		AllVfx.Add(vfx);
		EntityToVfx3D[entity] = vfx;

		GameContext.World.Add(entity, new Position(new System.Numerics.Vector3(position.X, position.Y, position.Z)));
		GameContext.World.Add(entity, new RotationY(rotationDegrees.Y));
		GameContext.World.Add(entity, new ModelScale(safeScale.X));

		return vfx;
	}

	public void RefreshVfxTemplateInstances(string previousTemplateId, VfxAttachmentConfig updatedConfig)
	{
		if (updatedConfig == null || AllVfx == null) return;

		string previousSlug = TemplateIDHelper.ParseTemplateID(previousTemplateId).Slug;
		string updatedSlug = TemplateIDHelper.ParseTemplateID(updatedConfig.VfxId).Slug;

		for (int index = 0; index < AllVfx.Count; index++)
		{
			var instance = AllVfx[index];
			if (instance == null || !GodotObject.IsInstanceValid(instance)) continue;

			string instanceSlug = TemplateIDHelper.ParseTemplateID(instance.VfxId).Slug;
			bool matchesPrevious = !string.IsNullOrEmpty(previousSlug) && string.Equals(instanceSlug, previousSlug, StringComparison.OrdinalIgnoreCase);
			bool matchesUpdated = !string.IsNullOrEmpty(updatedSlug) && string.Equals(instanceSlug, updatedSlug, StringComparison.OrdinalIgnoreCase);
			if (!matchesPrevious && !matchesUpdated) continue;

			instance.UpdateConfig(updatedConfig);
		}
	}

	private VfxAttachmentConfig ResolveVfxConfig(string vfxId)
	{
		string cleanId = vfxId ?? string.Empty;
		if (cleanId.StartsWith("vfx:", StringComparison.OrdinalIgnoreCase)) cleanId = cleanId.Substring(4);

		if (GameContext.RegistryService.TryGetVfxConfig(vfxId, out var regCfg)) return regCfg.Clone();

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


	public void SetUnitPlayerExternal(Unit3D unit, int playerIndex)
	{
		if (GodotObject.IsInstanceValid(unit) && GameContext.World.IsAlive(unit.Entity))
		{
			var playerOwner = GetPlayerEntityForPlayerIndex(playerIndex).AsPlayerEntity(GameContext.World);
			GameContext.World.Set(unit.Entity, new Owner(playerOwner));

			GameContext.World.SetOrAdd(unit.Entity, new UnitOwnerPlayer(playerIndex));

			if (TryGetUnitOrBuildingMetadata(unit.UnitId, out var meta))
			{
				string name = meta.Name;
				GameContext.World.Set(unit.Entity, new Name(name));
			}

			unit.Player = playerIndex;
			unit.UpdatePlayerColorVisual();
			unit.IsSelected = unit.IsSelected;
		}
	}

	public void SetUnitTeamExternal(Unit3D unit, bool isEnemy)
	{
		int targetPlayer = isEnemy ? 1 : 0;
		if (GameContext.Lobby != null && GameContext.Lobby.PlayerList.Count > 0)
		{
			int localPlayerIndex = GameContext.PerspectiveFilter.ControlledPlayerIndex;
			var enemyPlayer = GameContext.Lobby.PlayerList.Find(p => NetworkService.ArePlayerIndicesEnemies(localPlayerIndex, p.Slot));
			if (isEnemy && enemyPlayer != null)
			{
				targetPlayer = enemyPlayer.Slot;
			}
		}

		SetUnitPlayerExternal(unit, targetPlayer);
	}

	private Node3D? _editorCoverageOverlayRoot { get; set; }

	/// <summary>
	///     When enabled, the editor draws vision/attack range rings around the selected unit.
	///     Off by default so the overlay only appears on demand.
	/// </summary>
	public bool EditorCoverageOverlayEnabled { get; set; } = false;

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
		SetCurrentPopulation(GameContext.PerspectiveFilter.ControlledPlayerIndex, 0);
		SetMaxPopulation(GameContext.PerspectiveFilter.ControlledPlayerIndex, 0);

		foreach (var child in GetChildren())
		{
			if (child is Unit3D unit)
			{
				unit.QueueFree();
			}
			else if (child is Prop3D prop)
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
		EntityIdToUnit3D.Clear();
		EntityToProp3D.Clear();

		ReinitializeEcsAndServices();
	}

	private MeshInstance3D _measureMeshInstance { get; set; }
	private ImmediateMesh _measureImmediateMesh { get; set; }

	private MeshInstance3D _symmetryPivotMarkerMesh { get; set; }

	public MeshInstance3D? GridOverlayMesh => null;
	public MeshInstance3D? PathingOverlayMesh => null;

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

		MapCameraBounds oldMapBounds = MapCameraBounds.FromTerrain(oldWidth, oldDepth, quadSize);
		MapCameraBounds newMapBounds = MapCameraBounds.FromTerrain(newWidth, newDepth, quadSize);
		MapCameraBounds scaledCameraBounds = new MapCameraBounds(EditorCameraBoundsLeft, EditorCameraBoundsRight, EditorCameraBoundsTop, EditorCameraBoundsBottom).MapTo(oldMapBounds, newMapBounds);
		GameContext.EditorService.SetCameraBounds(GameContext.Simulation.WorldEntity, scaledCameraBounds);

		DeleteEntitiesOutsideBounds();
		GameContext.PropMultiMesh?.RebuildAll();

		GameContext.EditorService.SetTerrainSplatMap(GroundTerrain.SplatMap, GroundTerrain.CliffSplatMap);

		UpdateScaleMetadata(newWidth, newDepth, scaledCameraBounds);
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
			entity.Position = new Vector3(pos.X * scaleX, pos.Y, pos.Z * scaleZ);

			UpdateEntityEcsPosition(entity);
		}
	}

	private void UpdateEntityEcsPosition(Node3D entity)
	{
		if (entity is Unit3D u)
		{
			GameContext.EditorService.UpdateEntityEcsPosition(u.Entity, new System.Numerics.Vector3(u.Position.X, u.Position.Y, u.Position.Z));
		}
		else if (entity is Prop3D p)
		{
			GameContext.EditorService.UpdateEntityEcsPosition(p.Entity, new System.Numerics.Vector3(p.Position.X, p.Position.Y, p.Position.Z));
		}
	}

	private void ScalePropEntitiesOnly(float scaleX, float scaleZ)
	{
		GameContext.EditorService.ScalePropEntitiesOnly(scaleX, scaleZ, ent => EntityToProp3D.ContainsKey(ent));
	}

	private void ScaleDecals(float scaleX, float scaleZ)
	{
		foreach (var decal in AllDecals)
		{
			if (GodotObject.IsInstanceValid(decal))
			{
				decal.Position = new Vector3(decal.Position.X * scaleX, decal.Position.Y, decal.Position.Z * scaleZ);
				if (decal is Decal3D decal3D)
				{
					GameContext.EditorService.UpdateEntityEcsPosition(decal3D.Entity, new System.Numerics.Vector3(decal.Position.X, decal.Position.Y, decal.Position.Z));
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
					vfx.Position = new Vector3(vfx.Position.X * scaleX, vfx.Position.Y, vfx.Position.Z * scaleZ);
				}
			}
		}
	}

	private void ScaleCoordinateBounds(float scaleX, float scaleZ)
	{
		GameContext.EditorService.ScaleCoordinateBounds(scaleX, scaleZ);
	}

	private void UpdateScaleMetadata(int newWidth, int newDepth, MapCameraBounds cameraBounds)
	{
		GameContext.EditorService.UpdateScaleMetadata(newWidth, newDepth, cameraBounds);
	}

	private void RefreshUIForMapScale(int newWidth, int newDepth)
	{
		RebuildAllCoordinatePersistentMeshes();
		GameContext.MapEditorHud?.RefreshCoordinateListExternal();
		RebuildCameraBoundsOverlay();
		GameContext.MapEditorHud?.UpdateCameraBoundsUI();
		GameContext.MapEditorHud?.RegenerateMinimap();

		EditorHasUnsavedChanges = true;
		GameContext.MapEditorHud?.ShowFeedbackExternal($"Map scaled to {newWidth}x{newDepth}");
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


	private MeshInstance3D _scaleMapSilhouetteMesh { get; set; }

	private List<MeshInstance3D> _symmetryHighlightMeshes { get; set; } = new();


	private bool _wasSelectionHighlightVisible { get; set; }
	private List<bool> _wasSymmetryHighlightsVisible { get; set; } = new();
	private bool _wasCameraBoundsVisible { get; set; }
	private bool _wasMeasureMeshVisible { get; set; }
	private bool _wasSymmetryPivotVisible { get; set; }
	private bool _wasCoordinatePreviewVisible { get; set; }
	private bool _wasCoordinateOutlineVisible { get; set; }
	private bool _wasScaleSilhouetteVisible { get; set; }
	private bool _wasCoverageOverlayVisible { get; set; }
}