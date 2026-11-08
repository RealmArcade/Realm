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

[GlobalClass]

public partial class PropMultiMeshManager : Node3D
{
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
		GameContext.RegisterPropMultiMesh(this);
		Name = "PropMultiMeshManager";
		_appliedLodDistanceScale = GameSettings.LodDistanceScale;
	}

	public override void _ExitTree()
	{
		GameContext.UnregisterPropMultiMesh(this);
	}

	public void MarkDirty(string assetKeyOrPropId)
	{
		if (string.IsNullOrEmpty(assetKeyOrPropId)) return;
		string normKey = GameContext.Host != null ? GameContext.Host.GetModelAssetKey(assetKeyOrPropId) : assetKeyOrPropId.ToLowerInvariant();
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
		string normKey = GameContext.Host != null ? GameContext.Host.GetModelAssetKey(assetKeyOrPropId) : assetKeyOrPropId.ToLowerInvariant();
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
}
