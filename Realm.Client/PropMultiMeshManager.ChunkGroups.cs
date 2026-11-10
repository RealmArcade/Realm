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
	private void RebuildChunkGroup(PropModelGroup group, Vector2I chunkKey, List<PropData> chunkProps, string normAssetKey, float globalModelScale)
	{
		if (!group.ChunkGroups.TryGetValue(chunkKey, out var chunkGroup))
		{
			chunkGroup = new PropChunkGroup { ChunkIndex = chunkKey };
			group.ChunkGroups[chunkKey] = chunkGroup;
		}

		int instanceCount = chunkProps.Count;
		ulong dataHash = CalculateChunkDataHash(chunkProps, globalModelScale);

		if (CheckMeshesMatch(group, chunkGroup, instanceCount) && chunkGroup.LastInstanceCount == instanceCount && chunkGroup.LastDataHash == dataHash) return;

		chunkGroup.LastInstanceCount = instanceCount;
		chunkGroup.LastDataHash = dataHash;

		CalculateChunkBounds(group, chunkGroup, chunkProps, globalModelScale);
		UpdateMultiMeshInstances(group, chunkGroup, chunkProps, normAssetKey, globalModelScale);
	}

	private static ulong CalculateChunkDataHash(List<PropData> chunkProps, float globalModelScale)
	{
		ulong dataHash = 14695981039346656037UL;
		for (int i = 0; i < chunkProps.Count; i++)
		{
			var prop = chunkProps[i];
			dataHash ^= (ulong)prop.Position.X.GetHashCode();
			dataHash *= 1099511628211UL;
			dataHash ^= (ulong)prop.Position.Y.GetHashCode();
			dataHash *= 1099511628211UL;
			dataHash ^= (ulong)prop.Position.Z.GetHashCode();
			dataHash *= 1099511628211UL;
			dataHash ^= (ulong)prop.RotationY.GetHashCode();
			dataHash *= 1099511628211UL;
			dataHash ^= (ulong)prop.Scale.GetHashCode();
			dataHash *= 1099511628211UL;
			dataHash ^= (ulong)prop.YOffset.GetHashCode();
			dataHash *= 1099511628211UL;
		}

		dataHash ^= (ulong)globalModelScale.GetHashCode();
		return dataHash;
	}

	private static bool CheckMeshesMatch(PropModelGroup group, PropChunkGroup chunkGroup, int instanceCount)
	{
		if (chunkGroup.MultiMeshNodes.Count < group.SubMeshes.Count) return false;

		for (int subIdx = 0; subIdx < group.SubMeshes.Count; subIdx++)
		{
			var node = chunkGroup.MultiMeshNodes[subIdx];
			if (node.Multimesh == null || node.Multimesh.Mesh != group.SubMeshes[subIdx].Mesh || node.Multimesh.VisibleInstanceCount != instanceCount)
			{
				return false;
			}
		}

		return true;
	}

	private static void CalculateChunkBounds(PropModelGroup group, PropChunkGroup chunkGroup, List<PropData> chunkProps, float globalModelScale)
	{
		Aabb totalAabb = new Aabb();
		bool hasAabb = false;

		for (int subIdx = 0; subIdx < group.SubMeshes.Count; subIdx++)
		{
			var subInfo = group.SubMeshes[subIdx];
			if (subInfo.Mesh == null) continue;
			Aabb meshAabb = subInfo.Mesh.GetAabb();

			for (int i = 0; i < chunkProps.Count; i++)
			{
				var prop = chunkProps[i];
				Vector3 pos = prop.Position;
				pos.Y += prop.YOffset * prop.Scale;

				float propScale = Mathf.Max(0.01f, prop.Scale * globalModelScale);
				Basis basis = Basis.Identity.Rotated(Vector3.Up, Mathf.DegToRad(prop.RotationY)).Scaled(Vector3.One * propScale);
				Transform3D finalXform = new Transform3D(basis, pos) * subInfo.RelativeTransform;

				Vector3 aMin = meshAabb.Position;
				Vector3 aMax = meshAabb.End;
				Vector3 p0 = finalXform * new Vector3(aMin.X, aMin.Y, aMin.Z);
				Vector3 p1 = finalXform * new Vector3(aMax.X, aMin.Y, aMin.Z);
				Vector3 p2 = finalXform * new Vector3(aMin.X, aMax.Y, aMin.Z);
				Vector3 p3 = finalXform * new Vector3(aMax.X, aMax.Y, aMin.Z);
				Vector3 p4 = finalXform * new Vector3(aMin.X, aMin.Y, aMax.Z);
				Vector3 p5 = finalXform * new Vector3(aMax.X, aMin.Y, aMax.Z);
				Vector3 p6 = finalXform * new Vector3(aMin.X, aMax.Y, aMax.Z);
				Vector3 p7 = finalXform * new Vector3(aMax.X, aMax.Y, aMax.Z);

				Vector3 instMin = p0.Min(p1).Min(p2).Min(p3).Min(p4).Min(p5).Min(p6).Min(p7);
				Vector3 instMax = p0.Max(p1).Max(p2).Max(p3).Max(p4).Max(p5).Max(p6).Max(p7);
				Aabb instanceAabb = new Aabb(instMin, instMax - instMin);

				if (!hasAabb)
				{
					totalAabb = instanceAabb;
					hasAabb = true;
				}
				else
				{
					totalAabb = totalAabb.Merge(instanceAabb);
				}
			}
		}

		if (!hasAabb)
		{
			Vector3 fallbackPos = chunkProps.Count > 0 ? chunkProps[0].Position : Vector3.Zero;
			totalAabb = new Aabb(fallbackPos - new Vector3(4f, 4f, 4f), new Vector3(8f, 8f, 8f));
		}

		chunkGroup.Bounds = totalAabb.Grow(2.0f);
	}

	private void UpdateMultiMeshInstances(PropModelGroup group, PropChunkGroup chunkGroup, List<PropData> chunkProps, string normAssetKey, float globalModelScale)
	{
		for (int i = 0; i < chunkProps.Count; i++)
		{
			if (chunkProps[i].Entity != Entity.Null)
			{
				_entityIndexMap[chunkProps[i].Entity] = (chunkGroup, i, group);
			}
		}

		for (int subIdx = 0; subIdx < group.SubMeshes.Count; subIdx++)
		{
			var subInfo = group.SubMeshes[subIdx];
			EnsureMultiMeshNodeExists(chunkGroup, subIdx, normAssetKey);

			var node = chunkGroup.MultiMeshNodes[subIdx];
			ConfigureMultiMeshNode(node, subInfo, chunkGroup.Bounds);

			if (chunkProps.Count == 0)
			{
				if (node.Multimesh != null) node.Multimesh.VisibleInstanceCount = 0;
				node.CustomAabb = new Aabb();
				node.Visible = false;
				continue;
			}

			EnsureMultiMeshAllocated(node, subInfo.Mesh, chunkProps.Count);
			SetMultiMeshTransforms(node.Multimesh, chunkProps, subInfo.RelativeTransform, globalModelScale);
		}

		HideExtraMultiMeshNodes(group, chunkGroup);
	}

	private void EnsureMultiMeshNodeExists(PropChunkGroup chunkGroup, int subIdx, string normAssetKey)
	{
		while (chunkGroup.MultiMeshNodes.Count <= subIdx)
		{
			var mmNode = new MultiMeshInstance3D();
			mmNode.Name = $"MultiMesh_{normAssetKey}_C{chunkGroup.ChunkIndex.X}_{chunkGroup.ChunkIndex.Y}_{chunkGroup.MultiMeshNodes.Count}";
			AddChild(mmNode);
			chunkGroup.MultiMeshNodes.Add(mmNode);
		}
	}

	private static void ConfigureMultiMeshNode(MultiMeshInstance3D node, MeshSubInfo subInfo, Aabb bounds)
	{
		node.CastShadow = subInfo.CastShadow;
		node.VisibilityRangeBegin = subInfo.VisibilityRangeBegin;
		node.VisibilityRangeEnd = subInfo.VisibilityRangeEnd;
		node.VisibilityRangeFadeMode = subInfo.VisibilityRangeFadeMode;
		node.VisibilityRangeBeginMargin = subInfo.VisibilityRangeBeginMargin;
		node.VisibilityRangeEndMargin = subInfo.VisibilityRangeEndMargin;
		node.CustomAabb = bounds;
		node.Visible = true;
	}

	private static void EnsureMultiMeshAllocated(MultiMeshInstance3D node, Mesh targetMesh, int instanceCount)
	{
		if (node.Multimesh == null || node.Multimesh.InstanceCount < instanceCount || node.Multimesh.Mesh != targetMesh)
		{
			node.Multimesh = new MultiMesh
			{
				TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
				Mesh = targetMesh,
				InstanceCount = Math.Max(instanceCount + 16, 16)
			};
		}

		node.Multimesh.VisibleInstanceCount = instanceCount;
	}

	private static void SetMultiMeshTransforms(MultiMesh mm, List<PropData> chunkProps, Transform3D relativeTransform, float globalModelScale)
	{
		for (int i = 0; i < chunkProps.Count; i++)
		{
			var prop = chunkProps[i];
			Vector3 pos = prop.Position;
			pos.Y += prop.YOffset * prop.Scale;

			float propScale = Mathf.Max(0.01f, prop.Scale * globalModelScale);
			Basis basis = Basis.Identity.Rotated(Vector3.Up, Mathf.DegToRad(prop.RotationY)).Scaled(Vector3.One * propScale);
			mm.SetInstanceTransform(i, new Transform3D(basis, pos) * relativeTransform);
		}
	}

	private static void HideExtraMultiMeshNodes(PropModelGroup group, PropChunkGroup chunkGroup)
	{
		for (int extra = group.SubMeshes.Count; extra < chunkGroup.MultiMeshNodes.Count; extra++)
		{
			var extraNode = chunkGroup.MultiMeshNodes[extra];
			if (extraNode.Multimesh != null)
			{
				extraNode.Multimesh.VisibleInstanceCount = 0;
			}

			extraNode.CustomAabb = new Aabb();
			extraNode.Visible = false;
		}
	}

}
