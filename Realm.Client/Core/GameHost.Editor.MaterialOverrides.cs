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
			if (prop.Entity != Entity.Null && GameContext.World is { } world && world.IsAlive(prop.Entity))
			{
				world.SetOrAdd(prop.Entity, new CollisionRadius(ResolveTemplateCollisionRadius(prop.PropId)));
			}
		}
	}

	private void UpdateUnitCollisionRadii(string normAssetKey, float ratio)
	{
		foreach (var unit in AllUnits)
		{
			if (!GodotObject.IsInstanceValid(unit) || !MatchesEntityOrAssetKey(unit, normAssetKey)) continue;

			unit.UpdateCollisionCircleScale(ratio);
			if (unit.Entity != Entity.Null && GameContext.World is { } world && world.IsAlive(unit.Entity))
			{
				world.SetOrAdd(unit.Entity, new CollisionRadius(ResolveTemplateCollisionRadius(unit.UnitId)));
			}
		}
	}

}
