using System;
using System.Collections.Generic;
using Godot;
using Realm.Client.Core;
using Realm.Client.UI;
using Realm.Ecs.Components.Tags;

namespace Realm.Client.Services;

[GlobalClass]
public partial class UnitOcclusionOutlineService : Node3D
{
	private const string ShaderPath = "res://Assets/shaders/unit_occlusion_outline.gdshader";
	private const int InitialInstanceCapacity = 64;
	private const int TerrainOcclusionSamples = 10;
	private const float TerrainOcclusionMargin = 0.25f;
	private const float MaximumOcclusionDistance = 220.0f;
	private const float BuildingOccluderRefreshSeconds = 1.0f;
	private const float MinimumOutlineRadius = 0.45f;

	private readonly List<BuildingOccluder> _buildingOccluders = new();

	private MultiMeshInstance3D _outlineInstance;
	private MultiMesh _multiMesh;
	private int _instanceCapacity;
	private float _buildingRefreshTimer;
	private bool _isInitialized;

	private readonly struct BuildingOccluder
	{
		public BuildingOccluder(Vector3 center, Vector3 extents)
		{
			Center = center;
			Extents = extents;
		}

		public Vector3 Center { get; }

		public Vector3 Extents { get; }
	}

	public void Tick(float delta, Camera3D camera, IReadOnlyList<Unit3D> units)
	{
		bool isEnabled = GameSettings.OccludedUnitOutlines
		                 && GameSettings.QualityIdx != GraphicsQuality.Low
		                 && !GameContext.SessionState.IsMapEditorMode;
		if (!isEnabled || camera == null || !GodotObject.IsInstanceValid(camera) || units == null || units.Count == 0)
		{
			HideOutlines();
			return;
		}

		EnsureInitialized();

		_buildingRefreshTimer += delta;
		if (_buildingRefreshTimer >= BuildingOccluderRefreshSeconds)
		{
			_buildingRefreshTimer = 0.0f;
			RebuildBuildingOccluders(units);
		}

		WriteInstances(camera, units);
	}

	public void HideOutlines()
	{
		if (!_isInitialized || _outlineInstance == null || !GodotObject.IsInstanceValid(_outlineInstance)) return;

		_multiMesh.VisibleInstanceCount = 0;
		_outlineInstance.Visible = false;
	}

	private void EnsureInitialized()
	{
		if (_isInitialized) return;

		_multiMesh = new MultiMesh
		{
			TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
			UseColors = true,
			Mesh = new CapsuleMesh { Radius = 0.5f, Height = 1.0f, RadialSegments = 8, Rings = 2 }
		};

		EnsureCapacity(InitialInstanceCapacity);
		_multiMesh.VisibleInstanceCount = 0;

		_outlineInstance = new MultiMeshInstance3D
		{
			Name = "UnitOcclusionOutlineMultiMesh",
			Multimesh = _multiMesh,
			MaterialOverride = new ShaderMaterial { Shader = GD.Load<Shader>(ShaderPath) },
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			Visible = false
		};

		AddChild(_outlineInstance);
		_isInitialized = true;
	}

	private void WriteInstances(Camera3D camera, IReadOnlyList<Unit3D> units)
	{
		EnsureCapacity(units.Count);
		int instanceCount = 0;

		for (int index = 0; index < units.Count; index++)
		{
			Unit3D unit = units[index];
			if (!IsEligibleUnit(unit)) continue;

			Vector3 center = GetOutlineCenter(unit);
			if (!IsOccluded(camera, unit, center)) continue;

			if (instanceCount >= _instanceCapacity)
			{
				EnsureCapacity(_instanceCapacity * 2);
			}

			Transform3D instanceTransform = new Transform3D(Basis.Identity.Scaled(GetOutlineScale(unit)), center);
			_multiMesh.SetInstanceTransform(instanceCount, instanceTransform);

			Color outlineColor = unit.PlayerColor;
			outlineColor.A = 1.0f;
			_multiMesh.SetInstanceColor(instanceCount, outlineColor);

			instanceCount++;
		}

		_multiMesh.VisibleInstanceCount = instanceCount;
		_outlineInstance.Visible = instanceCount > 0;
	}

	private void EnsureCapacity(int capacity)
	{
		if (capacity <= _instanceCapacity) return;

		_instanceCapacity = Math.Max(InitialInstanceCapacity, capacity);
		_multiMesh.InstanceCount = _instanceCapacity;
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

	private bool IsOccluded(Camera3D camera, Unit3D unit, Vector3 center)
	{
		Vector3 eye = camera.GlobalPosition;
		Vector3 toUnit = center - eye;
		float distance = toUnit.Length();
		if (distance < 1.0f || distance > MaximumOcclusionDistance) return false;

		return IsOccludedByTerrain(eye, toUnit, distance) || IsOccludedByBuilding(eye, center);
	}

	private static bool IsOccludedByTerrain(Vector3 eye, Vector3 toUnit, float distance)
	{
		RuntimeTerrain terrain = GameHost.Instance?.GroundTerrain;
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

	private bool IsOccludedByBuilding(Vector3 eye, Vector3 target)
	{
		for (int index = 0; index < _buildingOccluders.Count; index++)
		{
			BuildingOccluder occluder = _buildingOccluders[index];
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

	private void RebuildBuildingOccluders(IReadOnlyList<Unit3D> units)
	{
		_buildingOccluders.Clear();

		for (int index = 0; index < units.Count; index++)
		{
			Unit3D unit = units[index];
			if (unit == null || !GodotObject.IsInstanceValid(unit)) continue;
			if (!unit.Visible || unit.IsPreview || !unit.IsBuilding) continue;

			float radius = MathF.Max(MinimumOutlineRadius, unit.GetBaseObstacleRadius());
			float halfHeight = MathF.Max(0.8f, radius * 1.1f);
			Vector3 center = unit.GlobalPosition + new Vector3(0.0f, halfHeight, 0.0f);
			_buildingOccluders.Add(new BuildingOccluder(center, new Vector3(radius, halfHeight, radius)));
		}
	}

	private static Vector3 GetOutlineCenter(Unit3D unit)
	{
		float height = GetOutlineHeight(unit);
		return unit.GlobalPosition + new Vector3(0.0f, height * 0.5f, 0.0f);
	}

	private static Vector3 GetOutlineScale(Unit3D unit)
	{
		float radius = MathF.Max(MinimumOutlineRadius, unit.GetBaseObstacleRadius());
		return new Vector3(radius * 2.0f, GetOutlineHeight(unit), radius * 2.0f);
	}

	private static float GetOutlineHeight(Unit3D unit)
	{
		float radius = MathF.Max(MinimumOutlineRadius, unit.GetBaseObstacleRadius());
		return MathF.Max(1.4f, radius * 2.4f);
	}
}
