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

	public void RefreshAllPlacedObjectModels(string? targetId = null)
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
		RefreshLootItemModels(targetId);
	}

	/// <summary>
	///     Redraws placed drops from their item templates, which is what makes a model the map author has just set show on
	///     the ground. A drop reads its model from the item registry on every rebuild, so reloading the registry first is
	///     what the caller owes this pass.
	/// </summary>
	/// <param name="targetId">The template whose drops are redrawn, or nothing to redraw every drop.</param>
	private void RefreshLootItemModels(string? targetId)
	{
		foreach (LootItem3D lootItem in AllLootItems)
		{
			if (lootItem == null || !GodotObject.IsInstanceValid(lootItem)) continue;
			if (string.IsNullOrEmpty(targetId) || GameContext.EditorService.IsTargetUnit(lootItem.ItemTypeId, targetId))
			{
				lootItem.RebuildVisuals();
			}
		}
	}

	private void RefreshUnitsModels(string? targetId)
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

	private bool IsTargetUnit(string unitId, string? targetId)
	{
		return GameContext.EditorService.IsTargetUnit(unitId, targetId);
	}

	private void RefreshSingleUnitModel(Unit3D unit)
	{
		bool isBuilding = unit.IsBuilding;
		string? targetModel = GetUnitTargetModel(unit.UnitId, ref isBuilding);

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

	private string? GetUnitTargetModel(string unitId, ref bool isBuilding)
	{
		return GameContext.RegistryService.GetUnitTargetModel(unitId, ref isBuilding);
	}

	private void RefreshPropsModels(string? targetId)
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
			string? spawnShader = entity.SpawnShader;
			string? deathShader = entity.DeathShader;

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
			string? spawnShader = res.SpawnShader;
			string? deathShader = res.DeathShader;

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

		if (GameContext.ModelOverrideService.ModelProceduralAnimations.TryGetValue(norm, out string? configuredAnimationId))
		{
			animationId = string.IsNullOrWhiteSpace(configuredAnimationId) ? null : configuredAnimationId;
		}

		if (GameContext.ModelOverrideService.ModelEnableProceduralAnimations.TryGetValue(norm, out bool configuredEnabled))
		{
			enabled = configuredEnabled;
		}

		return (animationId, enabled);
	}

	private void ApplyVisualOverrides(string templateId, string? modelPath, ref bool despill, ref bool normLuma, ref bool ignoreColor, ref string? spawnShader, ref string? deathShader)
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


}
