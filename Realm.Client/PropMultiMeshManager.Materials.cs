using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using Realm.Ecs;
using Godot;
using Realm.Client.Core;
using Realm.Client.Services;
using Realm.Client.Services.ModelOptimization;
using Realm.Client.UI;
using Realm.Client.Utils;
using Realm.Shared.Metadata;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Services;

namespace Realm.Client;


public partial class PropMultiMeshManager : Node3D
{
	public bool HasGroups => _groups.Count > 0;

	public void UpdateMaterialOverrides(string normAssetKey)
	{
		UpdateMaterialOverridesForAsset(normAssetKey);
	}

	public void UpdateAllMaterialOverrides()
	{
		if (GameContext.Host == null || _groups.Count == 0) return;

		foreach (var group in _groups.Values)
		{
			if (group == null) continue;
			UpdateGroupMaterialOverrides(group);
		}
	}

	public void UpdateMaterialOverridesForAsset(string normAssetKey)
	{
		if (GameContext.Host == null) return;

		foreach (var group in _groups.Values)
		{
			if (group == null) continue;
			if (!IsGroupMatchingAssetKey(group, normAssetKey)) continue;

			UpdateGroupMaterialOverrides(group);
		}
	}

	private static bool IsGroupMatchingAssetKey(PropModelGroup group, string normAssetKey)
	{
		if (string.Equals(group.AssetKey, normAssetKey, StringComparison.OrdinalIgnoreCase)) return true;
		if (string.Equals(GameContext.EditorService.NormalizeModelAssetKey(group.AssetKey), GameContext.EditorService.NormalizeModelAssetKey(normAssetKey), StringComparison.OrdinalIgnoreCase)) return true;
		if (GameContext.Host?.MatchesEntityOrAssetKey(group.AssetKey, normAssetKey) == true) return true;
		return false;
	}

	private void UpdateGroupMaterialOverrides(PropModelGroup group)
	{
		if (GameContext.Host is not { } host) return;

		float brightness = host.GetModelBrightness(group.AssetKey);
		Color tint = host.GetModelColorTint(group.AssetKey);
		bool ignorePlayerColor = host.GetModelIgnorePlayerColor(group.AssetKey);
		bool normalizeLuminance = host.GetModelNormalizeLuminance(group.AssetKey);

		foreach (var chunkGroup in group.ChunkGroups.Values)
		{
			UpdateChunkGroupMaterialOverrides(group, chunkGroup, brightness, tint, ignorePlayerColor, normalizeLuminance);
		}
	}

	private void UpdateChunkGroupMaterialOverrides(PropModelGroup group, PropChunkGroup chunkGroup, float brightness, Color tint, bool ignorePlayerColor, bool normalizeLuminance)
	{
		for (int i = 0; i < group.SubMeshes.Count && i < chunkGroup.MultiMeshNodes.Count; i++)
		{
			var subInfo = group.SubMeshes[i];
			var mmNode = chunkGroup.MultiMeshNodes[i];

			if (subInfo.Mesh != null && mmNode.Multimesh != null && mmNode.Multimesh.Mesh != subInfo.Mesh)
			{
				mmNode.Multimesh.Mesh = subInfo.Mesh;
			}

			Material baseMatToUse = subInfo.MaterialOverride;
			if (baseMatToUse == null && subInfo.SurfaceMaterials != null && subInfo.SurfaceMaterials.Length > 0)
			{
				baseMatToUse = subInfo.SurfaceMaterials[0];
			}

			if (baseMatToUse != null)
			{
				ApplyShaderMaterial(mmNode, baseMatToUse, brightness, tint, ignorePlayerColor, normalizeLuminance);
			}

			ApplyProceduralAnimationParameters(mmNode, subInfo);
		}
	}

	private static void ApplyProceduralAnimationParameters(MultiMeshInstance3D mmNode, MeshSubInfo subInfo)
	{
		if (!GodotObject.IsInstanceValid(mmNode)) return;

		mmNode.SetInstanceShaderParameter(_snProcAnimParams1, subInfo.ProceduralParameters1);
		mmNode.SetInstanceShaderParameter(_snProcAnimParams2, subInfo.ProceduralParameters2);
		mmNode.SetInstanceShaderParameter(_snProcAnimParams3, subInfo.ProceduralParameters3);
		mmNode.SetInstanceShaderParameter(_snProcAnimImpulse, Vector4.Zero);
		mmNode.SetInstanceShaderParameter(_snProcAnimVelocity, Vector3.Zero);
		mmNode.SetInstanceShaderParameter(_snProcAnimRegionMin, subInfo.ProceduralRegionMin);
		mmNode.SetInstanceShaderParameter(_snProcAnimRegionMax, subInfo.ProceduralRegionMax);
	}

	private void ApplyShaderMaterial(MultiMeshInstance3D mmNode, Material baseMatToUse, float brightness, Color tint, bool ignorePlayerColor, bool normalizeLuminance)
	{
		var shaderMat = ModelShaderManager.GetOrCreateShaderMaterial(baseMatToUse, normalizeLuminance);
		mmNode.MaterialOverride = shaderMat;
		mmNode.SetInstanceShaderParameter(_snModelBrightness, brightness);
		mmNode.SetInstanceShaderParameter(_snModelColorTint, tint);
		mmNode.SetInstanceShaderParameter(_snIgnorePlayerColor, ignorePlayerColor ? 1.0f : 0.0f);
		mmNode.SetInstanceShaderParameter(_snUnitAmbientBoost, 0.0f);
		mmNode.SetInstanceShaderParameter(_snUnitRimIntensity, 0.0f);
		mmNode.SetInstanceShaderParameter(_snHideInShroud, 1.0f);
	}

}
