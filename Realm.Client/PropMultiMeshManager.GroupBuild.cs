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
	private PropModelGroup? CreateGroupForAsset(string normAssetKey)
	{
		string modelPath = PropModelPathResolver.ResolveRegistered(normAssetKey);
		if (string.IsNullOrEmpty(modelPath)) return null;
		Node? prototype = ModelCache.GetModel(modelPath);
		if (prototype == null) return null;

		try
		{
			float globalModelScale = GameContext.Host != null ? GameContext.Host.GetModelScale(normAssetKey) : 1.0f;
			float scaleMultiplier = Math.Max(0.01f, globalModelScale);
			GltfDocumentExtensionMsftLod.UpdateLodVisibilityRanges(prototype, scaleMultiplier);

			var meshNodes = new List<MeshInstance3D>();
			FindMeshInstancesRecursive(prototype, meshNodes);

			if (meshNodes.Count == 0)
			{
				return null;
			}

			var group = new PropModelGroup { AssetKey = normAssetKey };
			ProceduralAnimationConfig? proceduralAnimation = ResolveProceduralAnimation(normAssetKey);
			ProceduralSwayRegionBinder? swayRegionBinder = proceduralAnimation != null && prototype is Node3D prototypeRoot
				? new ProceduralSwayRegionBinder(prototypeRoot, proceduralAnimation, normAssetKey)
				: null;
			PopulateSubMeshes(group, meshNodes, prototype, proceduralAnimation, swayRegionBinder);
			ConfigureVisibilityRanges(group);

			return group.SubMeshes.Count > 0 ? group : null;
		}
		finally
		{
			prototype.QueueFree();
		}
	}

	private static void PopulateSubMeshes(PropModelGroup group, List<MeshInstance3D> meshNodes, Node prototype, ProceduralAnimationConfig? proceduralAnimation, ProceduralSwayRegionBinder? swayRegionBinder)
	{
		ProceduralShaderParameters proceduralParameters = ProceduralAnimationManager.BuildShaderParameters(proceduralAnimation);

		foreach (var mi in meshNodes)
		{
			if (mi.Mesh == null) continue;
			string nameStr = mi.Name.ToString();
			if (nameStr.StartsWith("_selection") || nameStr.StartsWith("_hover")) continue;

			var subInfo = new MeshSubInfo
			{
				Mesh = mi.Mesh,
				RelativeTransform = GetRelativeTransform(mi, prototype),
				MaterialOverride = mi.MaterialOverride,
				CastShadow = mi.CastShadow,
				VisibilityRangeBegin = mi.VisibilityRangeBegin,
				VisibilityRangeEnd = mi.VisibilityRangeEnd,
				VisibilityRangeBeginMargin = mi.VisibilityRangeBeginMargin,
				VisibilityRangeEndMargin = mi.VisibilityRangeEndMargin,
				VisibilityRangeFadeMode = mi.VisibilityRangeFadeMode
			};

			ApplySubMeshProceduralAnimation(subInfo, mi, proceduralAnimation, proceduralParameters, swayRegionBinder);

			int surfaceCount = mi.Mesh.GetSurfaceCount();
			subInfo.SurfaceMaterials = new Material[surfaceCount];
			for (int s = 0; s < surfaceCount; s++)
			{
				subInfo.SurfaceMaterials[s] = mi.GetSurfaceOverrideMaterial(s) ?? mi.Mesh.SurfaceGetMaterial(s);
			}

			group.SubMeshes.Add(subInfo);
		}
	}

	private static void ApplySubMeshProceduralAnimation(MeshSubInfo subInfo, MeshInstance3D meshInstance, ProceduralAnimationConfig? proceduralAnimation, ProceduralShaderParameters proceduralParameters, ProceduralSwayRegionBinder? swayRegionBinder)
	{
		if (proceduralAnimation == null)
		{
			subInfo.HasProceduralAnimation = false;
			subInfo.ProceduralParameters1 = Vector4.Zero;
			subInfo.ProceduralParameters2 = Vector4.Zero;
			subInfo.ProceduralParameters3 = Vector4.Zero;
			subInfo.ProceduralRegionMin = Vector3.Zero;
			subInfo.ProceduralRegionMax = Vector3.Zero;
			return;
		}

		Vector4 parameters1 = proceduralParameters.Parameters1;
		Vector4 parameters2 = proceduralParameters.Parameters2;
		Vector3 regionMin = new(proceduralAnimation.MaskRegionMin.X, proceduralAnimation.MaskRegionMin.Y, proceduralAnimation.MaskRegionMin.Z);
		Vector3 regionMax = new(proceduralAnimation.MaskRegionMax.X, proceduralAnimation.MaskRegionMax.Y, proceduralAnimation.MaskRegionMax.Z);

		if (swayRegionBinder != null)
		{
			ProceduralSwayMeshMask meshMask = swayRegionBinder.BuildMeshMask(meshInstance);
			parameters1 = new Vector4(parameters1.X, (float)meshMask.MaskMode, meshMask.MaskMin, meshMask.MaskMax);
			parameters2 = new Vector4(meshMask.SignedMaskPower, parameters2.Y, parameters2.Z, parameters2.W);
			regionMin = meshMask.RegionMin;
			regionMax = meshMask.RegionMax;
		}

		subInfo.HasProceduralAnimation = true;
		subInfo.ProceduralParameters1 = parameters1;
		subInfo.ProceduralParameters2 = parameters2;
		subInfo.ProceduralParameters3 = proceduralParameters.Parameters3;
		subInfo.ProceduralRegionMin = regionMin;
		subInfo.ProceduralRegionMax = regionMax;
	}

	private static ProceduralAnimationConfig? ResolveProceduralAnimation(string normAssetKey)
	{
		if (GameContext.Host == null) return null;
		if (!GameContext.Host.GetModelEnableProceduralAnimation(normAssetKey)) return null;

		string animationId = GameContext.Host.GetModelProceduralAnimation(normAssetKey);
		if (string.IsNullOrWhiteSpace(animationId)) return null;

		return ProceduralAnimationManager.GetConfig(animationId);
	}

	private static void ConfigureVisibilityRanges(PropModelGroup group)
	{
		if (group.SubMeshes.Count == 1)
		{
			group.SubMeshes[0].VisibilityRangeBegin = 0f;
			group.SubMeshes[0].VisibilityRangeEnd = 0f;
		}
		else if (group.SubMeshes.Count > 1)
		{
			group.SubMeshes[^1].VisibilityRangeEnd = 0f;
		}
	}

}
