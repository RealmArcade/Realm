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
	public void RebuildAll()
	{
		RebuildAllInternal();
	}

	private void RebuildAllInternal()
	{
		if (GameContext.Host == null) return;
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
		if (GameContext.World == null || GameContext.Host is not { } host) return;
		var query = QueryCache.AllPropIdentityAndPositionNoneDeadQuery;
		GameContext.World.Query(in query, (Entity _, ref PropIdentity propIdComp) =>
		{
			string key = host.GetModelAssetKey(propIdComp.PropId);
			if (!string.IsNullOrEmpty(key))
			{
				_reusableKeysToRebuild.Add(key);
			}
		});
	}

	private void CollectReusableKeysFromProps()
	{
		if (GameContext.Host is not { } host) return;

		foreach (var prop in host.AllProps)
		{
			if (!GodotObject.IsInstanceValid(prop) || prop.IsPreview || prop.GetNodeOrNull<Node3D>("VisualModel") != null) continue;

			string key = host.GetModelAssetKey(prop);
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
		if (GameContext.Host == null || string.IsNullOrEmpty(normAssetKey)) return;

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
		if (GameContext.World == null || GameContext.Host is not { } host) return;

		var query = QueryCache.AllPropIdentityAndPositionNoneDeadQuery;
		GameContext.World.Query(in query, (Entity entity, ref PropIdentity propIdComp, ref Position posComp) =>
		{
			if (GameHost.EntityToProp3D.ContainsKey(entity)) return;

			string key = host.GetModelAssetKey(propIdComp.PropId);
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
				YOffset = host.GetModelYOffset(propIdComp.PropId)
			});
		});
	}

	private void CollectMatchingPropsFromNodes(string normAssetKey)
	{
		if (GameContext.Host is not { } host) return;

		foreach (var prop in host.AllProps)
		{
			if (!GodotObject.IsInstanceValid(prop) || prop.IsPreview || !prop.Visible || prop.GetNodeOrNull<Node3D>("VisualModel") != null) continue;
			if (host.GetModelAssetKey(prop) != normAssetKey) continue;

			_reusableMatchingPropsData.Add(new PropData
			{
				PropId = prop.PropId,
				Position = prop.Position,
				RotationY = prop.RotationDegrees.Y,
				Scale = prop.Scale.X,
				YOffset = host.GetModelYOffset(prop.PropId)
			});
		}
	}

	private void CalculateChunkBuckets()
	{
		float quadSize = 1.0f;
		float halfW = 64.0f;
		float halfD = 64.0f;
		var terrain = GameContext.Host?.GroundTerrain;
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
		if (GameContext.Host is not { } host) return;

		float globalModelScale = host.GetModelScale(normAssetKey);

		foreach (var kvp in _reusableChunkBucketsData)
		{
			if (kvp.Value.Count == 0) continue;
			RebuildChunkGroup(group, kvp.Key, kvp.Value, normAssetKey, globalModelScale);
		}
	}

}
