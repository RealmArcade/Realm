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
	public void RefreshLodVisibilityDistances()
	{
		float targetDistanceScale = GameSettings.LodDistanceScale;
		if (_appliedLodDistanceScale <= 0f)
		{
			_appliedLodDistanceScale = targetDistanceScale;
			return;
		}

		if (MathF.Abs(targetDistanceScale - _appliedLodDistanceScale) <= 0.0005f)
		{
			return;
		}

		float distanceRatio = targetDistanceScale / _appliedLodDistanceScale;
		_appliedLodDistanceScale = targetDistanceScale;

		foreach (var group in _groups.Values)
		{
			if (group == null) continue;
			ScaleGroupVisibilityRanges(group, distanceRatio);
		}
	}

	private static void ScaleGroupVisibilityRanges(PropModelGroup group, float distanceRatio)
	{
		for (int subIdx = 0; subIdx < group.SubMeshes.Count; subIdx++)
		{
			MeshSubInfo subInfo = group.SubMeshes[subIdx];
			subInfo.VisibilityRangeBegin *= distanceRatio;
			subInfo.VisibilityRangeEnd *= distanceRatio;
		}

		foreach (var chunkGroup in group.ChunkGroups.Values)
		{
			for (int subIdx = 0; subIdx < group.SubMeshes.Count && subIdx < chunkGroup.MultiMeshNodes.Count; subIdx++)
			{
				MultiMeshInstance3D node = chunkGroup.MultiMeshNodes[subIdx];
				if (node == null || !GodotObject.IsInstanceValid(node)) continue;

				MeshSubInfo subInfo = group.SubMeshes[subIdx];
				node.VisibilityRangeBegin = subInfo.VisibilityRangeBegin;
				node.VisibilityRangeEnd = subInfo.VisibilityRangeEnd;
			}
		}
	}

	public void UpdateInstanceTransform(Entity entity, Vector3 position, float rotationY, float scale)
	{
		if (_entityIndexMap.TryGetValue(entity, out var info))
		{
			float yOffset = GameContext.Host != null ? GameContext.Host.GetModelYOffset(ResolveEntityPropId(entity, info.ModelGroup.AssetKey)) : 0f;
			float globalModelScale = GameContext.Host != null ? GameContext.Host.GetModelScale(info.ModelGroup.AssetKey) : 1f;

			Vector3 pos = position;
			pos.Y += yOffset * scale;

			float propScale = Mathf.Max(0.01f, scale * globalModelScale);
			Basis basis = Basis.Identity.Rotated(Vector3.Up, Mathf.DegToRad(rotationY)).Scaled(Vector3.One * propScale);

			for (int subIdx = 0; subIdx < info.ModelGroup.SubMeshes.Count && subIdx < info.ChunkGroup.MultiMeshNodes.Count; subIdx++)
			{
				var mmNode = info.ChunkGroup.MultiMeshNodes[subIdx];
				if (mmNode != null && mmNode.Multimesh != null)
				{
					var subInfo = info.ModelGroup.SubMeshes[subIdx];
					mmNode.Multimesh.SetInstanceTransform(info.Index, new Transform3D(basis, pos) * subInfo.RelativeTransform);
				}
			}
		}
		else if (GameContext.Host != null && GameContext.World != null && GameContext.World.IsAlive(entity) && GameContext.World.Has<PropIdentity>(entity))
		{
			string propId = GameContext.World.Get<PropIdentity>(entity).PropId;
			MarkDirty(propId);
		}
	}

	private static string ResolveEntityPropId(Entity entity, string fallbackAssetKey)
	{
		if (GameContext.World != null && GameContext.World.IsAlive(entity) && GameContext.World.Has<PropIdentity>(entity))
		{
			string propId = GameContext.World.Get<PropIdentity>(entity).PropId;
			if (!string.IsNullOrEmpty(propId)) return propId;
		}

		return fallbackAssetKey;
	}

	public void SetInstanceCustomData(Entity entity, Color customData)
	{
		if (_entityIndexMap.TryGetValue(entity, out var info))
		{
			for (int subIdx = 0; subIdx < info.ModelGroup.SubMeshes.Count && subIdx < info.ChunkGroup.MultiMeshNodes.Count; subIdx++)
			{
				var mmNode = info.ChunkGroup.MultiMeshNodes[subIdx];
				if (mmNode != null && mmNode.Multimesh != null)
				{
					mmNode.Multimesh.SetInstanceCustomData(info.Index, customData);
				}
			}
		}
	}

	/// <summary>
	///     Reads the world bounds every mesh of one placed prop covers. A prop is drawn as instances inside a shared
	///     multi mesh, so its bounds are the merge of each sub mesh's local box under that instance's transform.
	/// </summary>
	/// <param name="entity">The prop whose placed instance is read.</param>
	/// <param name="bounds">The world bounds the prop covers, when it is placed.</param>
	/// <returns>Whether the prop is currently drawn by a chunk group.</returns>
	public bool TryGetPropWorldBounds(Entity entity, out Aabb bounds)
	{
		bounds = default;
		if (!_entityIndexMap.TryGetValue(entity, out var info)) return false;

		bool hasBounds = false;
		for (int subIdx = 0; subIdx < info.ChunkGroup.MultiMeshNodes.Count; subIdx++)
		{
			MultiMeshInstance3D? node = info.ChunkGroup.MultiMeshNodes[subIdx];
			if (node == null || !GodotObject.IsInstanceValid(node) || node.Multimesh == null) continue;

			MultiMesh multiMesh = node.Multimesh;
			if (multiMesh.Mesh == null || info.Index >= multiMesh.VisibleInstanceCount) continue;

			Aabb instanceBounds = TransformBounds(multiMesh.GetInstanceTransform(info.Index), multiMesh.Mesh.GetAabb());
			bounds = hasBounds ? bounds.Merge(instanceBounds) : instanceBounds;
			hasBounds = true;
		}

		return hasBounds;
	}

	private static Aabb TransformBounds(Transform3D transform, Aabb localBounds)
	{
		Vector3 minimum = new(float.MaxValue, float.MaxValue, float.MaxValue);
		Vector3 maximum = new(float.MinValue, float.MinValue, float.MinValue);
		for (int cornerIndex = 0; cornerIndex < 8; cornerIndex++)
		{
			Vector3 corner = transform * localBounds.GetEndpoint(cornerIndex);
			minimum = minimum.Min(corner);
			maximum = maximum.Max(corner);
		}

		return new Aabb(minimum, maximum - minimum);
	}

}
