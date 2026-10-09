using Godot;
using System;
using System.Collections.Generic;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;

public partial class PropMultiMeshManager : Node3D
{
	public static PropMultiMeshManager Instance { get; private set; }

	private struct PropData
	{
		public string PropId;
		public Vector3 Position;
		public float RotationY;
		public float Scale;
	}

	private class MeshSubInfo
	{
		public Mesh Mesh;
		public Transform3D RelativeTransform;
		public Material[] SurfaceMaterials;
		public Material MaterialOverride;
		public GeometryInstance3D.ShadowCastingSetting CastShadow = GeometryInstance3D.ShadowCastingSetting.On;
		public float VisibilityRangeBegin = 0f;
		public float VisibilityRangeEnd = 0f;
		public float VisibilityRangeBeginMargin = 2.0f;
		public float VisibilityRangeEndMargin = 2.0f;
		public GeometryInstance3D.VisibilityRangeFadeModeEnum VisibilityRangeFadeMode = GeometryInstance3D.VisibilityRangeFadeModeEnum.Disabled;
	}

	private class PropChunkGroup
	{
		public Vector2I ChunkIndex;
		public Aabb Bounds;
		public List<MultiMeshInstance3D> MultiMeshNodes = new();
		public int LastInstanceCount = -1;
		public ulong LastDataHash = 0;
	}

	private class PropModelGroup
	{
		public string AssetKey;
		public List<MeshSubInfo> SubMeshes = new();
		public Dictionary<Vector2I, PropChunkGroup> ChunkGroups = new();
	}

	private static readonly StringName _snModelBrightness = new("model_brightness");
	private static readonly StringName _snModelColorTint = new("model_color_tint");
	private static readonly StringName _snIgnorePlayerColor = new("ignore_player_color");
	private static readonly StringName _snUnitAmbientBoost = new("unit_ambient_boost");
	private static readonly StringName _snUnitRimIntensity = new("unit_rim_intensity");
	private static readonly StringName _snHideInShroud = new("hide_in_shroud");

	private readonly Dictionary<string, PropModelGroup> _groups = new(StringComparer.OrdinalIgnoreCase);
	private readonly HashSet<string> _dirtyAssetKeys = new(StringComparer.OrdinalIgnoreCase);
	private readonly List<PropData> _reusableMatchingPropsData = new();
	private readonly Dictionary<Vector2I, List<PropData>> _reusableChunkBucketsData = new();
	private readonly HashSet<string> _reusableKeysToRebuild = new(StringComparer.OrdinalIgnoreCase);
	private bool _allDirty = false;

	public override void _Ready()
	{
		Instance = this;
		Name = "PropMultiMeshManager";
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

	public void Clear()
	{
		_dirtyAssetKeys.Clear();
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
		if (GameHost.Instance.EcsWorld == null) return;
		var query = Realm.Ecs.Common.QueryCache.AllPropIdentityAndPositionQuery;
		GameHost.Instance.EcsWorld.Query(in query, (ref PropIdentity propIdComp) =>
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
		if (GameHost.Instance.EcsWorld == null) return;

		var query = Realm.Ecs.Common.QueryCache.AllPropIdentityAndPositionQuery;
		GameHost.Instance.EcsWorld.Query(in query, (Arch.Core.Entity entity, ref PropIdentity propIdComp, ref Position posComp) =>
		{
			if (GameHost.EntityToProp3D.ContainsKey(entity)) return;

			string key = GameHost.Instance.GetModelAssetKey(propIdComp.PropId);
			if (key != normAssetKey) return;

			float rotY = GameHost.Instance.EcsWorld.Has<RotationY>(entity) ? GameHost.Instance.EcsWorld.Get<RotationY>(entity).Value : 0f;
			float scale = GameHost.Instance.EcsWorld.Has<ModelScale>(entity) ? GameHost.Instance.EcsWorld.Get<ModelScale>(entity).Value : 1f;

			_reusableMatchingPropsData.Add(new PropData
			{
				PropId = propIdComp.PropId,
				Position = new Vector3(posComp.Value.X, posComp.Value.Y, posComp.Value.Z),
				RotationY = rotY,
				Scale = scale
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
				Scale = prop.Scale.X
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
		float yOffset = GameHost.Instance.GetModelYOffset(normAssetKey);
		float globalModelScale = GameHost.Instance.GetModelScale(normAssetKey);

		foreach (var kvp in _reusableChunkBucketsData)
		{
			if (kvp.Value.Count == 0) continue;
			RebuildChunkGroup(group, kvp.Key, kvp.Value, normAssetKey, yOffset, globalModelScale);
		}
	}

	private void RebuildChunkGroup(PropModelGroup group, Vector2I chunkKey, List<PropData> chunkProps, string normAssetKey, float yOffset, float globalModelScale)
	{
		if (!group.ChunkGroups.TryGetValue(chunkKey, out var chunkGroup))
		{
			chunkGroup = new PropChunkGroup { ChunkIndex = chunkKey };
			group.ChunkGroups[chunkKey] = chunkGroup;
		}

		int instanceCount = chunkProps.Count;
		ulong dataHash = CalculateChunkDataHash(chunkProps, yOffset, globalModelScale);

		if (CheckMeshesMatch(group, chunkGroup, instanceCount) && chunkGroup.LastInstanceCount == instanceCount && chunkGroup.LastDataHash == dataHash) return;

		chunkGroup.LastInstanceCount = instanceCount;
		chunkGroup.LastDataHash = dataHash;

		CalculateChunkBounds(group, chunkGroup, chunkProps, yOffset, globalModelScale);
		UpdateMultiMeshInstances(group, chunkGroup, chunkProps, normAssetKey, yOffset, globalModelScale);
	}

	private static ulong CalculateChunkDataHash(List<PropData> chunkProps, float yOffset, float globalModelScale)
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
		}
		dataHash ^= (ulong)yOffset.GetHashCode();
		dataHash *= 1099511628211UL;
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

	private static void CalculateChunkBounds(PropModelGroup group, PropChunkGroup chunkGroup, List<PropData> chunkProps, float yOffset, float globalModelScale)
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
				pos.Y += yOffset * prop.Scale;

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

	private void UpdateMultiMeshInstances(PropModelGroup group, PropChunkGroup chunkGroup, List<PropData> chunkProps, string normAssetKey, float yOffset, float globalModelScale)
	{
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
			SetMultiMeshTransforms(node.Multimesh, chunkProps, subInfo.RelativeTransform, yOffset, globalModelScale);
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

	private static void SetMultiMeshTransforms(MultiMesh mm, List<PropData> chunkProps, Transform3D relativeTransform, float yOffset, float globalModelScale)
	{
		for (int i = 0; i < chunkProps.Count; i++)
		{
			var prop = chunkProps[i];
			Vector3 pos = prop.Position;
			pos.Y += yOffset * prop.Scale;

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
		Node prototype = Realm.Godot.Utils.ModelCache.GetModel(modelPath);
		if (prototype == null) return null;

		float globalModelScale = GameHost.Instance != null ? GameHost.Instance.GetModelScale(normAssetKey) : 1.0f;
		float scaleMultiplier = Math.Max(0.01f, globalModelScale);
		Realm.Godot.Services.ModelOptimization.GltfDocumentExtensionMsftLod.UpdateLodVisibilityRanges(prototype, scaleMultiplier);

		var meshNodes = new List<MeshInstance3D>();
		FindMeshInstancesRecursive(prototype, meshNodes);

		if (meshNodes.Count == 0)
		{
			prototype.QueueFree();
			return null;
		}

		var group = new PropModelGroup { AssetKey = normAssetKey };
		PopulateSubMeshes(group, meshNodes, prototype);
		ConfigureVisibilityRanges(group);

		prototype.QueueFree();
		return group.SubMeshes.Count > 0 ? group : null;
	}

	private static void PopulateSubMeshes(PropModelGroup group, List<MeshInstance3D> meshNodes, Node prototype)
	{
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

			int surfaceCount = mi.Mesh.GetSurfaceCount();
			subInfo.SurfaceMaterials = new Material[surfaceCount];
			for (int s = 0; s < surfaceCount; s++)
			{
				subInfo.SurfaceMaterials[s] = mi.GetSurfaceOverrideMaterial(s) ?? mi.Mesh.SurfaceGetMaterial(s);
			}

			group.SubMeshes.Add(subInfo);
		}
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
		if (string.Equals(GameHost.Instance.NormalizeModelAssetKey(group.AssetKey), GameHost.Instance.NormalizeModelAssetKey(normAssetKey), StringComparison.OrdinalIgnoreCase)) return true;
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
		}
	}

	private void ApplyShaderMaterial(MultiMeshInstance3D mmNode, Material baseMatToUse, float brightness, Color tint, bool ignorePlayerColor, bool normalizeLuminance)
	{
		var shaderMat = Realm.Godot.Utils.ModelShaderManager.GetOrCreateShaderMaterial(baseMatToUse, normalizeLuminance);
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

		string cleanId = System.IO.Path.GetFileNameWithoutExtension(normAssetKey);
		string targetModel = GetModelPathFromRegistries(normAssetKey, cleanId);

		if (string.IsNullOrEmpty(targetModel))
			return string.Empty;

		string resolvedPath = Realm.Godot.Utils.ModelCache.ResolveModelPath(targetModel);
		if (!string.IsNullOrEmpty(resolvedPath) && (resolvedPath.StartsWith("res://") || System.IO.File.Exists(resolvedPath)))
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

	private static bool TryGetModelPath<T>(System.Collections.Generic.Dictionary<Godot.StringName, T> registry, string normAssetKey, string cleanId, out string modelPath)
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
		if (meta is Realm.Shared.Metadata.PropMetadata propMeta && !string.IsNullOrEmpty(propMeta.ModelPath))
		{
			modelPath = propMeta.ModelPath;
			return true;
		}
		if (meta is Realm.Shared.Metadata.ResourceMetadata resMeta && !string.IsNullOrEmpty(resMeta.ModelPath))
		{
			modelPath = resMeta.ModelPath;
			return true;
		}
		if (meta is Realm.Shared.Metadata.UnitMetadata unitMeta && !string.IsNullOrEmpty(unitMeta.ModelPath))
		{
			modelPath = unitMeta.ModelPath;
			return true;
		}
		return false;
	}
}
