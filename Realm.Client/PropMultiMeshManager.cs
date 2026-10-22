using System;
using System.Collections.Generic;
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

[GlobalClass]
public partial class PropMultiMeshManager : Node3D
{
	public static PropMultiMeshManager Instance { get; private set; }


	private static readonly StringName _snModelBrightness = new("model_brightness");
	private static readonly StringName _snModelColorTint = new("model_color_tint");
	private static readonly StringName _snIgnorePlayerColor = new("ignore_player_color");
	private static readonly StringName _snUnitAmbientBoost = new("unit_ambient_boost");
	private static readonly StringName _snUnitRimIntensity = new("unit_rim_intensity");
	private static readonly StringName _snHideInShroud = new("hide_in_shroud");
	private static readonly StringName _snProcAnimParams1 = new("proc_anim_params1");
	private static readonly StringName _snProcAnimParams2 = new("proc_anim_params2");
	private static readonly StringName _snProcAnimParams3 = new("proc_anim_params3");
	private static readonly StringName _snProcAnimImpulse = new("proc_anim_impulse");
	private static readonly StringName _snProcAnimVelocity = new("proc_anim_velocity");
	private static readonly StringName _snProcAnimRegionMin = new("proc_anim_region_min");
	private static readonly StringName _snProcAnimRegionMax = new("proc_anim_region_max");

	private readonly Dictionary<string, PropModelGroup> _groups = new(StringComparer.OrdinalIgnoreCase);
	private readonly HashSet<string> _dirtyAssetKeys = new(StringComparer.OrdinalIgnoreCase);
	private readonly List<PropData> _reusableMatchingPropsData = new();
	private readonly Dictionary<Vector2I, List<PropData>> _reusableChunkBucketsData = new();
	private readonly HashSet<string> _reusableKeysToRebuild = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<Entity, (PropChunkGroup ChunkGroup, int Index, PropModelGroup ModelGroup)> _entityIndexMap = new();
	private readonly List<Entity> _reusableStaleEntities = new();
	private bool _allDirty = false;
	private float _appliedLodDistanceScale = 0f;

	public override void _Ready()
	{
		Instance = this;
		Name = "PropMultiMeshManager";
		_appliedLodDistanceScale = GameSettings.LodDistanceScale;
	}

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

	public override void _ExitTree()
	{
		if (Instance == this)
		{
			Instance = null;
		}
	}

	public void MarkDirty(string assetKeyOrPropId)
	{
		if (string.IsNullOrEmpty(assetKeyOrPropId)) return;
		string normKey = GameHost.Instance != null ? GameHost.Instance.GetModelAssetKey(assetKeyOrPropId) : assetKeyOrPropId.ToLowerInvariant();
		if (!string.IsNullOrEmpty(normKey))
		{
			_dirtyAssetKeys.Add(normKey);
		}
	}

	public void MarkAllDirty()
	{
		_allDirty = true;
	}

	public void RefreshProceduralAnimation(string assetKeyOrPropId)
	{
		if (string.IsNullOrEmpty(assetKeyOrPropId)) return;
		string normKey = GameHost.Instance != null ? GameHost.Instance.GetModelAssetKey(assetKeyOrPropId) : assetKeyOrPropId.ToLowerInvariant();
		if (string.IsNullOrEmpty(normKey)) return;

		if (_groups.TryGetValue(normKey, out var group))
		{
			DisposeGroup(group);
			_groups.Remove(normKey);
		}

		_dirtyAssetKeys.Add(normKey);
	}

	private void DisposeGroup(PropModelGroup group)
	{
		foreach (var chunkGroup in group.ChunkGroups.Values)
		{
			foreach (var node in chunkGroup.MultiMeshNodes)
			{
				if (GodotObject.IsInstanceValid(node)) node.QueueFree();
			}
		}

		_reusableStaleEntities.Clear();
		foreach (var entry in _entityIndexMap)
		{
			if (ReferenceEquals(entry.Value.ModelGroup, group)) _reusableStaleEntities.Add(entry.Key);
		}

		for (int i = 0; i < _reusableStaleEntities.Count; i++)
		{
			_entityIndexMap.Remove(_reusableStaleEntities[i]);
		}
	}

	public void Clear()
	{
		_dirtyAssetKeys.Clear();
		_entityIndexMap.Clear();
		_allDirty = false;
		foreach (var group in _groups.Values)
		{
			foreach (var chunkGroup in group.ChunkGroups.Values)
			{
				foreach (var node in chunkGroup.MultiMeshNodes)
				{
					if (GodotObject.IsInstanceValid(node))
					{
						node.QueueFree();
					}
				}
			}
		}

		_groups.Clear();
	}

	public void UpdateInstanceTransform(Entity entity, Vector3 position, float rotationY, float scale)
	{
		if (_entityIndexMap.TryGetValue(entity, out var info))
		{
			float yOffset = GameHost.Instance != null ? GameHost.Instance.GetModelYOffset(ResolveEntityPropId(entity, info.ModelGroup.AssetKey)) : 0f;
			float globalModelScale = GameHost.Instance != null ? GameHost.Instance.GetModelScale(info.ModelGroup.AssetKey) : 1f;

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
		else if (GameHost.Instance != null && GameContext.World != null && GameContext.World.IsAlive(entity) && GameContext.World.Has<PropIdentity>(entity))
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

	public override void _Process(double delta)
	{
		if (_allDirty)
		{
			RebuildAllInternal();
			_allDirty = false;
			_dirtyAssetKeys.Clear();
		}
		else if (_dirtyAssetKeys.Count > 0)
		{
			foreach (var key in _dirtyAssetKeys)
			{
				RebuildGroupInternal(key);
			}

			_dirtyAssetKeys.Clear();
		}
	}

	public void SetAllNodesVisible(bool visible)
	{
		foreach (var group in _groups.Values)
		{
			foreach (var chunkGroup in group.ChunkGroups.Values)
			{
				foreach (var node in chunkGroup.MultiMeshNodes)
				{
					if (GodotObject.IsInstanceValid(node))
					{
						node.Visible = visible;
					}
				}
			}
		}
	}

	public void RebuildAll()
	{
		RebuildAllInternal();
	}

	private void RebuildAllInternal()
	{
		if (GameHost.Instance == null) return;
		_reusableKeysToRebuild.Clear();

		CollectReusableKeysFromEcs();
		CollectReusableKeysFromProps();
		CollectReusableKeysFromGroups();

		foreach (var key in _reusableKeysToRebuild)
		{
			RebuildGroupInternal(key);
		}
	}

	private void CollectReusableKeysFromEcs()
	{
		if (GameContext.World == null) return;
		var query = QueryCache.AllPropIdentityAndPositionQuery;
		GameContext.World.Query(in query, (Entity _, ref PropIdentity propIdComp) =>
		{
			string key = GameHost.Instance.GetModelAssetKey(propIdComp.PropId);
			if (!string.IsNullOrEmpty(key))
			{
				_reusableKeysToRebuild.Add(key);
			}
		});
	}

	private void CollectReusableKeysFromProps()
	{
		foreach (var prop in GameHost.Instance.AllProps)
		{
			if (!GodotObject.IsInstanceValid(prop) || prop.IsPreview || prop.GetNodeOrNull<Node3D>("VisualModel") != null) continue;

			string key = GameHost.Instance.GetModelAssetKey(prop);
			if (!string.IsNullOrEmpty(key))
			{
				_reusableKeysToRebuild.Add(key);
			}
		}
	}

	private void CollectReusableKeysFromGroups()
	{
		foreach (var existingKey in _groups.Keys)
		{
			_reusableKeysToRebuild.Add(existingKey);
		}
	}

	private void RebuildGroupInternal(string normAssetKey)
	{
		if (GameHost.Instance == null || string.IsNullOrEmpty(normAssetKey)) return;

		_reusableMatchingPropsData.Clear();
		CollectMatchingPropsFromEcs(normAssetKey);
		CollectMatchingPropsFromNodes(normAssetKey);

		if (!_groups.TryGetValue(normAssetKey, out var group))
		{
			if (_reusableMatchingPropsData.Count == 0) return;
			group = CreateGroupForAsset(normAssetKey);
			if (group == null) return;
			_groups[normAssetKey] = group;
		}

		CalculateChunkBuckets();
		HideEmptyChunkGroups(group);
		RebuildActiveChunkGroups(group, normAssetKey);
		UpdateMaterialOverridesForAsset(normAssetKey);
	}

	private void CollectMatchingPropsFromEcs(string normAssetKey)
	{
		if (GameContext.World == null) return;

		var query = QueryCache.AllPropIdentityAndPositionQuery;
		GameContext.World.Query(in query, (Entity entity, ref PropIdentity propIdComp, ref Position posComp) =>
		{
			if (GameHost.EntityToProp3D.ContainsKey(entity)) return;

			string key = GameHost.Instance.GetModelAssetKey(propIdComp.PropId);
			if (key != normAssetKey) return;

			float rotY = GameContext.World.Has<RotationY>(entity) ? GameContext.World.Get<RotationY>(entity).Value : 0f;
			float scale = GameContext.World.Has<ModelScale>(entity) ? GameContext.World.Get<ModelScale>(entity).Value : 1f;

			_reusableMatchingPropsData.Add(new PropData
			{
				Entity = entity,
				PropId = propIdComp.PropId,
				Position = new Vector3(posComp.Value.X, posComp.Value.Y, posComp.Value.Z),
				RotationY = rotY,
				Scale = scale,
				YOffset = GameHost.Instance.GetModelYOffset(propIdComp.PropId)
			});
		});
	}

	private void CollectMatchingPropsFromNodes(string normAssetKey)
	{
		foreach (var prop in GameHost.Instance.AllProps)
		{
			if (!GodotObject.IsInstanceValid(prop) || prop.IsPreview || !prop.Visible || prop.GetNodeOrNull<Node3D>("VisualModel") != null) continue;
			if (GameHost.Instance.GetModelAssetKey(prop) != normAssetKey) continue;

			_reusableMatchingPropsData.Add(new PropData
			{
				PropId = prop.PropId,
				Position = prop.Position,
				RotationY = prop.RotationDegrees.Y,
				Scale = prop.Scale.X,
				YOffset = GameHost.Instance.GetModelYOffset(prop.PropId)
			});
		}
	}

	private void CalculateChunkBuckets()
	{
		float quadSize = 1.0f;
		float halfW = 64.0f;
		float halfD = 64.0f;
		var terrain = GameHost.Instance.GroundTerrain;
		if (terrain != null)
		{
			quadSize = terrain.QuadSize;
			halfW = (terrain.Width / 2.0f) * quadSize;
			halfD = (terrain.Depth / 2.0f) * quadSize;
		}

		float chunkSizeUnits = 16.0f * quadSize;

		foreach (var bucket in _reusableChunkBucketsData.Values)
		{
			bucket.Clear();
		}

		for (int i = 0; i < _reusableMatchingPropsData.Count; i++)
		{
			var prop = _reusableMatchingPropsData[i];
			int cx = (int)Math.Floor((prop.Position.X + halfW) / chunkSizeUnits);
			int cz = (int)Math.Floor((prop.Position.Z + halfD) / chunkSizeUnits);
			var cKey = new Vector2I(cx, cz);

			if (!_reusableChunkBucketsData.TryGetValue(cKey, out var list))
			{
				list = new List<PropData>();
				_reusableChunkBucketsData[cKey] = list;
			}

			list.Add(prop);
		}
	}

	private void HideEmptyChunkGroups(PropModelGroup group)
	{
		foreach (var kvp in group.ChunkGroups)
		{
			if (_reusableChunkBucketsData.TryGetValue(kvp.Key, out var chunkList) && chunkList.Count > 0) continue;
			if (kvp.Value.LastInstanceCount == 0) continue;

			kvp.Value.LastInstanceCount = 0;
			kvp.Value.LastDataHash = 0;
			foreach (var mmNode in kvp.Value.MultiMeshNodes)
			{
				if (mmNode.Multimesh != null)
				{
					mmNode.Multimesh.VisibleInstanceCount = 0;
				}

				mmNode.CustomAabb = new Aabb();
			}
		}
	}

	private void RebuildActiveChunkGroups(PropModelGroup group, string normAssetKey)
	{
		float globalModelScale = GameHost.Instance.GetModelScale(normAssetKey);

		foreach (var kvp in _reusableChunkBucketsData)
		{
			if (kvp.Value.Count == 0) continue;
			RebuildChunkGroup(group, kvp.Key, kvp.Value, normAssetKey, globalModelScale);
		}
	}

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

	private PropModelGroup CreateGroupForAsset(string normAssetKey)
	{
		string modelPath = ResolveModelPathForAssetKey(normAssetKey);
		if (string.IsNullOrEmpty(modelPath)) return null;
		Node prototype = ModelCache.GetModel(modelPath);
		if (prototype == null) return null;

		try
		{
			float globalModelScale = GameHost.Instance != null ? GameHost.Instance.GetModelScale(normAssetKey) : 1.0f;
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

		if (swayRegionBinder != null && !swayRegionBinder.MatchesMesh(meshInstance))
		{
			subInfo.HasProceduralAnimation = false;
			subInfo.ProceduralParameters1 = Vector4.Zero;
			subInfo.ProceduralParameters2 = proceduralParameters.Parameters2;
			subInfo.ProceduralParameters3 = proceduralParameters.Parameters3;
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
		if (GameHost.Instance == null) return null;
		if (!GameHost.Instance.GetModelEnableProceduralAnimation(normAssetKey)) return null;

		string animationId = GameHost.Instance.GetModelProceduralAnimation(normAssetKey);
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

	public bool HasGroups => _groups.Count > 0;

	public void UpdateMaterialOverrides(string normAssetKey)
	{
		UpdateMaterialOverridesForAsset(normAssetKey);
	}

	public void UpdateAllMaterialOverrides()
	{
		if (GameHost.Instance == null || _groups.Count == 0) return;

		foreach (var group in _groups.Values)
		{
			if (group == null) continue;
			UpdateGroupMaterialOverrides(group);
		}
	}

	public void UpdateMaterialOverridesForAsset(string normAssetKey)
	{
		if (GameHost.Instance == null) return;

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
		if (GameHost.Instance.MatchesEntityOrAssetKey(group.AssetKey, normAssetKey)) return true;
		return false;
	}

	private void UpdateGroupMaterialOverrides(PropModelGroup group)
	{
		float brightness = GameHost.Instance.GetModelBrightness(group.AssetKey);
		Color tint = GameHost.Instance.GetModelColorTint(group.AssetKey);
		bool ignorePlayerColor = GameHost.Instance.GetModelIgnorePlayerColor(group.AssetKey);
		bool normalizeLuminance = GameHost.Instance.GetModelNormalizeLuminance(group.AssetKey);

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

	private static Transform3D GetRelativeTransform(Node node, Node root)
	{
		Transform3D xform = Transform3D.Identity;
		Node curr = node;
		while (curr != null && curr != root)
		{
			if (curr is Node3D n3d)
			{
				xform = n3d.Transform * xform;
			}

			curr = curr.GetParent();
		}

		return xform;
	}

	private static void FindMeshInstancesRecursive(Node parent, List<MeshInstance3D> result)
	{
		if (parent == null) return;
		if (parent is MeshInstance3D mi)
		{
			result.Add(mi);
		}

		int childCount = parent.GetChildCount();
		for (int i = 0; i < childCount; i++)
		{
			FindMeshInstancesRecursive(parent.GetChild(i), result);
		}
	}

	private static string ResolveModelPathForAssetKey(string normAssetKey)
	{
		if (string.IsNullOrEmpty(normAssetKey))
			return string.Empty;

		string cleanId = Path.GetFileNameWithoutExtension(normAssetKey);
		string targetModel = GetModelPathFromRegistries(normAssetKey, cleanId);

		if (string.IsNullOrEmpty(targetModel))
			return string.Empty;

		string resolvedPath = ModelCache.ResolveModelPath(targetModel);
		if (!string.IsNullOrEmpty(resolvedPath) && (resolvedPath.StartsWith("res://") || File.Exists(resolvedPath)))
		{
			return resolvedPath;
		}

		return targetModel;
	}

	private static string GetModelPathFromRegistries(string normAssetKey, string cleanId)
	{
		if (GameHost.PropRegistry != null && TryGetModelPath(GameHost.PropRegistry, normAssetKey, cleanId, out string propPath)) return propPath;
		if (GameHost.ResourceRegistry != null && TryGetModelPath(GameHost.ResourceRegistry, normAssetKey, cleanId, out string resPath)) return resPath;
		if (GameHost.UnitRegistry != null && TryGetModelPath(GameHost.UnitRegistry, normAssetKey, cleanId, out string unitPath)) return unitPath;
		if (GameHost.BuildingRegistry != null && TryGetModelPath(GameHost.BuildingRegistry, normAssetKey, cleanId, out string bldPath)) return bldPath;

		return normAssetKey;
	}

	private static bool TryGetModelPath<T>(Dictionary<StringName, T> registry, string normAssetKey, string cleanId, out string modelPath)
	{
		modelPath = null;

		if (registry.TryGetValue(normAssetKey, out var meta) && TryGetModelPathFromMeta(meta, out modelPath))
		{
			return true;
		}

		if (!string.IsNullOrEmpty(cleanId) && registry.TryGetValue(cleanId, out meta) && TryGetModelPathFromMeta(meta, out modelPath))
		{
			return true;
		}

		return false;
	}

	private static bool TryGetModelPathFromMeta(object meta, out string modelPath)
	{
		modelPath = null;
		if (meta is PropMetadata propMeta && !string.IsNullOrEmpty(propMeta.ModelPath))
		{
			modelPath = propMeta.ModelPath;
			return true;
		}

		if (meta is ResourceMetadata resMeta && !string.IsNullOrEmpty(resMeta.ModelPath))
		{
			modelPath = resMeta.ModelPath;
			return true;
		}

		if (meta is UnitMetadata unitMeta && !string.IsNullOrEmpty(unitMeta.ModelPath))
		{
			modelPath = unitMeta.ModelPath;
			return true;
		}

		return false;
	}
}