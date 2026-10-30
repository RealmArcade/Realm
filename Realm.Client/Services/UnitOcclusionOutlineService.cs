using System;
using System.Collections.Generic;
using Godot;
using Realm.Client.Core;
using Realm.Client.UI;
using Realm.Client.Utils;
using Realm.Ecs.Components.Tags;

namespace Realm.Client.Services;

[GlobalClass]
public partial class UnitOcclusionOutlineService : Node3D
{
	private const string ShaderPath = "res://Assets/shaders/unit_occlusion_outline.gdshader";
	private const int TerrainOcclusionSamples = 10;
	private const float TerrainOcclusionMargin = 0.25f;
	private const float MaximumOcclusionDistance = 220.0f;
	private const float BuildingOccluderRefreshSeconds = 1.0f;
	private const float SilhouetteFadeInSeconds = 0.15f;
	private const float SilhouetteFadeOutSeconds = 0.35f;
	private const float SilhouetteOcclusionHoldSeconds = 0.25f;
	private const float OcclusionSampleHeight = 1.0f;
	private const float MinimumBuildingHalfExtent = 0.15f;
	private const float MinimumBuildingHalfHeight = 0.2f;
	private const float SilhouetteAlphaUpdateThreshold = 0.01f;

	private static readonly StringName SilhouetteColorParameter = new("silhouette_color");
	private static readonly StringName SilhouetteAlphaParameter = new("silhouette_alpha");

	private readonly List<BuildingOccluder> _buildingOccluders = new();
	private readonly Dictionary<ulong, SilhouetteState> _silhouetteStates = new();
	private readonly List<ulong> _staleSilhouetteIds = new();

	private ShaderMaterial _silhouetteMaterial;
	private float _buildingRefreshTimer;
	private bool _buildingOccludersDirty = true;
	private long _tickIndex;

	private struct BuildingOccluder
	{
		public Vector3 Center;
		public Vector3 Extents;
		public float BoundingRadius;
		public float BoundingRadiusSquared;
	}

	private class SilhouetteState
	{
		public Unit3D Unit;
		public ulong ModelInstanceId;
		public List<GeometryInstance3D> Meshes = new();
		public bool OverlayApplied;
		public Color AppliedColor;
		public float AppliedAlpha;
		public float Alpha;
		public float HoldRemaining;
		public long LastSeenTick;
	}

	public override void _ExitTree()
	{
		ClearAllOverlays();
	}

	public void Tick(float delta, Camera3D camera, IReadOnlyList<Unit3D> units)
	{
		bool isEnabled = GameSettings.OccludedUnitOutlines
		                 && GameSettings.QualityIdx != GraphicsQuality.Low
		                 && !GameContext.SessionState.IsMapEditorMode;
		if (!isEnabled || camera == null || !GodotObject.IsInstanceValid(camera) || units == null)
		{
			ClearAllOverlays();
			return;
		}

		EnsureMaterial();
		_tickIndex++;

		_buildingRefreshTimer += delta;
		if (_buildingOccludersDirty || _buildingRefreshTimer >= BuildingOccluderRefreshSeconds)
		{
			_buildingRefreshTimer = 0.0f;
			_buildingOccludersDirty = false;
			RebuildBuildingOccluders(units, camera.GlobalPosition);
		}

		Vector3 eye = camera.GlobalPosition;

		for (int index = 0; index < units.Count; index++)
		{
			Unit3D unit = units[index];
			if (!IsEligibleUnit(unit)) continue;

			bool isOccluded = IsOccluded(eye, unit);
			ulong unitInstanceId = unit.GetInstanceId();

			if (_silhouetteStates.TryGetValue(unitInstanceId, out SilhouetteState existingState))
			{
				existingState.LastSeenTick = _tickIndex;
				UpdateSilhouette(existingState, isOccluded, delta);
				continue;
			}

			if (!isOccluded) continue;

			SilhouetteState state = new SilhouetteState
			{
				Unit = unit,
				LastSeenTick = _tickIndex
			};

			_silhouetteStates[unitInstanceId] = state;
			UpdateSilhouette(state, true, delta);
		}

		ReleaseStaleStates();
	}

	public void HideOutlines()
	{
		ClearAllOverlays();
	}

	private void EnsureMaterial()
	{
		if (_silhouetteMaterial != null && GodotObject.IsInstanceValid(_silhouetteMaterial)) return;

		_silhouetteMaterial = new ShaderMaterial
		{
			Shader = GD.Load<Shader>(ShaderPath),
			RenderPriority = 1
		};
	}

	private bool IsOccluded(Vector3 eye, Unit3D unit)
	{
		Vector3 samplePoint = unit.GlobalPosition + new Vector3(0.0f, OcclusionSampleHeight, 0.0f);
		Vector3 toUnit = samplePoint - eye;
		float distance = toUnit.Length();
		if (distance < 1.0f || distance > MaximumOcclusionDistance) return false;

		return IsOccludedByTerrain(eye, toUnit, distance) || IsOccludedByBuilding(eye, samplePoint, toUnit, distance);
	}

	private static bool IsEligibleUnit(Unit3D unit)
	{
		if (unit == null || !GodotObject.IsInstanceValid(unit)) return false;
		if (unit.IsPreview || !unit.Visible) return false;

		var world = GameContext.World;
		if (world == null || unit.Entity == Realm.Ecs.Entity.Null || !world.IsAlive(unit.Entity)) return false;
		if (world.Has<Dead>(unit.Entity)) return false;
		if (unit.IsBuilding) return false;

		ShroudService shroud = GameContext.ShroudService;
		return shroud == null || shroud.IsUnitVisibleOnMinimap(unit);
	}

	private static bool IsOccludedByTerrain(Vector3 eye, Vector3 toUnit, float distance)
	{
		RuntimeTerrain terrain = GameContext.Host?.GroundTerrain;
		if (terrain == null) return false;

		float travelFraction = MathF.Min(1.0f, MathF.Max(0.1f, (distance - 2.0f) / distance));
		for (int sampleIndex = 1; sampleIndex <= TerrainOcclusionSamples; sampleIndex++)
		{
			float fraction = travelFraction * sampleIndex / TerrainOcclusionSamples;
			Vector3 samplePoint = eye + toUnit * fraction;
			if (!terrain.TrySampleHeight(samplePoint.X, samplePoint.Z, out float terrainHeight)) return false;
			if (terrainHeight > samplePoint.Y + TerrainOcclusionMargin) return true;
		}

		return false;
	}

	private bool IsOccludedByBuilding(Vector3 eye, Vector3 target, Vector3 toTarget, float distance)
	{
		Vector3 direction = toTarget / distance;

		for (int index = 0; index < _buildingOccluders.Count; index++)
		{
			BuildingOccluder occluder = _buildingOccluders[index];
			Vector3 toCenter = occluder.Center - eye;
			float alongSegment = toCenter.Dot(direction);
			if (alongSegment + occluder.BoundingRadius <= 0.0f) continue;
			if (alongSegment - occluder.BoundingRadius >= distance) continue;

			float perpendicularDistanceSquared = toCenter.LengthSquared() - alongSegment * alongSegment;
			if (perpendicularDistanceSquared > occluder.BoundingRadiusSquared) continue;

			if (SegmentIntersectsBox(eye, target, occluder.Center, occluder.Extents)) return true;
		}

		return false;
	}

	private static bool SegmentIntersectsBox(Vector3 start, Vector3 end, Vector3 center, Vector3 extents)
	{
		Vector3 direction = end - start;
		float minimum = 0.0f;
		float maximum = 1.0f;

		if (!IntersectAxis(start.X, direction.X, center.X, extents.X, ref minimum, ref maximum)) return false;
		if (!IntersectAxis(start.Y, direction.Y, center.Y, extents.Y, ref minimum, ref maximum)) return false;
		if (!IntersectAxis(start.Z, direction.Z, center.Z, extents.Z, ref minimum, ref maximum)) return false;

		return maximum >= minimum;
	}

	private static bool IntersectAxis(float origin, float direction, float center, float halfExtent, ref float minimum, ref float maximum)
	{
		float boxMinimum = center - halfExtent;
		float boxMaximum = center + halfExtent;

		if (MathF.Abs(direction) < 0.0001f)
		{
			return origin >= boxMinimum && origin <= boxMaximum;
		}

		float first = (boxMinimum - origin) / direction;
		float second = (boxMaximum - origin) / direction;
		if (first > second)
		{
			(first, second) = (second, first);
		}

		minimum = MathF.Max(minimum, first);
		maximum = MathF.Min(maximum, second);
		return maximum >= minimum;
	}

	private void RebuildBuildingOccluders(IReadOnlyList<Unit3D> units, Vector3 eye)
	{
		_buildingOccluders.Clear();

		for (int index = 0; index < units.Count; index++)
		{
			Unit3D unit = units[index];
			if (unit == null || !GodotObject.IsInstanceValid(unit)) continue;
			if (!unit.Visible || unit.IsPreview || !unit.IsBuilding) continue;

			GetBuildingWorldBounds(unit, out Vector3 center, out Vector3 extents);

			float boundingRadiusSquared = extents.LengthSquared();
			float boundingRadius = MathF.Sqrt(boundingRadiusSquared);
			float maximumCenterDistance = MaximumOcclusionDistance + boundingRadius;
			if ((center - eye).LengthSquared() > maximumCenterDistance * maximumCenterDistance) continue;

			_buildingOccluders.Add(new BuildingOccluder
			{
				Center = center,
				Extents = extents,
				BoundingRadius = boundingRadius,
				BoundingRadiusSquared = boundingRadiusSquared
			});
		}
	}

	private static void GetBuildingWorldBounds(Unit3D unit, out Vector3 center, out Vector3 extents)
	{
		Node3D modelNode = unit.ModelNode;
		if (modelNode == null || !GodotObject.IsInstanceValid(modelNode))
		{
			float radius = MathF.Max(MinimumBuildingHalfExtent, unit.GetBaseObstacleRadius());
			center = unit.GlobalPosition + new Vector3(0.0f, radius, 0.0f);
			extents = new Vector3(radius, MathF.Max(MinimumBuildingHalfHeight, radius * 1.1f), radius);
			return;
		}

		Aabb localBounds = modelNode.CalculateLocalBounds();
		Transform3D worldTransform = modelNode.GlobalTransform;
		Vector3 localHalfSize = localBounds.Size * 0.5f;
		Vector3 column0 = worldTransform.Basis.Column0;
		Vector3 column1 = worldTransform.Basis.Column1;
		Vector3 column2 = worldTransform.Basis.Column2;

		center = worldTransform * localBounds.GetCenter();
		extents = new Vector3(
			MathF.Max(MinimumBuildingHalfExtent, MathF.Abs(column0.X) * localHalfSize.X + MathF.Abs(column1.X) * localHalfSize.Y + MathF.Abs(column2.X) * localHalfSize.Z),
			MathF.Max(MinimumBuildingHalfHeight, MathF.Abs(column0.Y) * localHalfSize.X + MathF.Abs(column1.Y) * localHalfSize.Y + MathF.Abs(column2.Y) * localHalfSize.Z),
			MathF.Max(MinimumBuildingHalfExtent, MathF.Abs(column0.Z) * localHalfSize.X + MathF.Abs(column1.Z) * localHalfSize.Y + MathF.Abs(column2.Z) * localHalfSize.Z));
	}

	private void UpdateSilhouette(SilhouetteState state, bool isOccluded, float delta)
	{
		if (isOccluded)
		{
			state.HoldRemaining = SilhouetteOcclusionHoldSeconds;
			state.Alpha = MathF.Min(1.0f, state.Alpha + delta / SilhouetteFadeInSeconds);
		}
		else
		{
			state.HoldRemaining -= delta;
			if (state.HoldRemaining <= 0.0f)
			{
				state.Alpha = MathF.Max(0.0f, state.Alpha - delta / SilhouetteFadeOutSeconds);
			}
		}

		if (state.Alpha <= 0.001f)
		{
			ClearOverlay(state);
			return;
		}

		ApplyOverlay(state);
	}

	private void ApplyOverlay(SilhouetteState state)
	{
		if (state.Unit == null || !GodotObject.IsInstanceValid(state.Unit))
		{
			ClearOverlay(state);
			return;
		}

		EnsureMeshes(state);

		Color silhouetteColor = state.Unit.PlayerColor;
		silhouetteColor.A = 1.0f;

		bool refreshBindings = !state.OverlayApplied || !state.AppliedColor.IsEqualApprox(silhouetteColor);
		bool refreshAlpha = refreshBindings || MathF.Abs(state.Alpha - state.AppliedAlpha) >= SilhouetteAlphaUpdateThreshold;
		if (!refreshBindings && !refreshAlpha) return;

		float alpha = state.Alpha;

		for (int index = 0; index < state.Meshes.Count; index++)
		{
			GeometryInstance3D mesh = state.Meshes[index];
			if (mesh == null || !GodotObject.IsInstanceValid(mesh)) continue;

			if (refreshBindings)
			{
				mesh.MaterialOverlay = _silhouetteMaterial;
				mesh.SetInstanceShaderParameter(SilhouetteColorParameter, silhouetteColor);
			}

			mesh.SetInstanceShaderParameter(SilhouetteAlphaParameter, alpha);
		}

		state.OverlayApplied = true;
		state.AppliedColor = silhouetteColor;
		state.AppliedAlpha = state.Alpha;
	}

	private static void EnsureMeshes(SilhouetteState state)
	{
		Node3D modelNode = state.Unit.ModelNode;
		ulong modelInstanceId = modelNode != null && GodotObject.IsInstanceValid(modelNode) ? modelNode.GetInstanceId() : 0UL;
		if (modelInstanceId == state.ModelInstanceId && state.Meshes.Count > 0) return;

		state.ModelInstanceId = modelInstanceId;
		state.Meshes.Clear();
		if (modelInstanceId == 0UL) return;

		CollectMeshes(modelNode, state.Meshes);
	}

	private static void CollectMeshes(Node node, List<GeometryInstance3D> meshes)
	{
		if (node is MeshInstance3D meshInstance && meshInstance.Mesh != null && meshInstance.Visible && !ModelExtensions.IsAttachmentHelperNode(meshInstance))
		{
			meshes.Add(meshInstance);
		}

		int childCount = node.GetChildCount();
		for (int index = 0; index < childCount; index++)
		{
			CollectMeshes(node.GetChild(index), meshes);
		}
	}

	private static void ClearOverlay(SilhouetteState state)
	{
		if (!state.OverlayApplied) return;

		state.OverlayApplied = false;
		state.AppliedAlpha = 0.0f;
		for (int index = 0; index < state.Meshes.Count; index++)
		{
			GeometryInstance3D mesh = state.Meshes[index];
			if (mesh == null || !GodotObject.IsInstanceValid(mesh)) continue;
			mesh.MaterialOverlay = null;
		}
	}

	private void ReleaseStaleStates()
	{
		if (_silhouetteStates.Count == 0) return;

		_staleSilhouetteIds.Clear();
		foreach (KeyValuePair<ulong, SilhouetteState> pair in _silhouetteStates)
		{
			if (pair.Value.LastSeenTick == _tickIndex) continue;
			ClearOverlay(pair.Value);
			_staleSilhouetteIds.Add(pair.Key);
		}

		for (int index = 0; index < _staleSilhouetteIds.Count; index++)
		{
			_silhouetteStates.Remove(_staleSilhouetteIds[index]);
		}
	}

	private void ClearAllOverlays()
	{
		if (_silhouetteStates.Count == 0) return;

		foreach (KeyValuePair<ulong, SilhouetteState> pair in _silhouetteStates)
		{
			ClearOverlay(pair.Value);
		}

		_silhouetteStates.Clear();
		_buildingOccludersDirty = true;
	}
}
