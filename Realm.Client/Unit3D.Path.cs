using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using Godot;
using Realm.Client.Animation;
using Realm.Client.Core;
using Realm.Client.Network;
using Realm.Client.Services;
using Realm.Client.Services.ModelOptimization;
using Realm.Client.VFX;
using Realm.Ecs;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Resources;
using Realm.Ecs.Components.Tags;
using Realm.Ecs.Components.Terrain;
using Realm.Ecs.Services;
using Realm.Shared.Animation;

namespace Realm.Client;
public partial class Unit3D
{
	public void UpdateSelectedVisuals(float fDelta)
	{
		if (IsSelected && GameContext.PerspectiveFilter.CanSeeOwnedUi(Player))
		{
			if (IsBuilding)
			{
				UpdateRallyVisuals();
			}
			else
			{
				UpdatePathVisuals();
			}
		}
		else
		{
			HidePathVisuals();
			if (_rallyVisualsContainer != null)
			{
				_rallyVisualsContainer.Visible = false;
			}
		}
	}

	private void EnsurePathVisualsContainer()
	{
		if (_pathVisualsContainer == null)
		{
			_pathVisualsContainer = new Node3D();
			_pathVisualsContainer.Name = "PathVisualsContainer_Node3D";
			_pathVisualsContainer.TopLevel = true;
			AddChild(_pathVisualsContainer);
		}
	}

	public bool IsAttackPath { get; set; } = false;

	private void GetRemainingPathPoints(List<Vector3> points)
	{
		points.Clear();
		IsAttackPath = false;
		if (GameContext.Host == null || GameContext.World is not { } world || !world.IsAlive(Entity)) return;

		if (world.Has<AttackTarget>(Entity) || world.Has<AttackMove>(Entity))
		{
			IsAttackPath = true;
		}

		points.Add(GetEcsOrLocalPosition());

		bool hasPathPoints = false;
		if (world.Has<PathFollow>(Entity))
		{
			var pf = world.Get<PathFollow>(Entity);
			for (int i = pf.CurrentWaypointIndex; i < pf.WaypointCount; i++)
			{
				points.Add(new Vector3(pf.Waypoints[i].X, pf.Waypoints[i].Y, pf.Waypoints[i].Z));
				hasPathPoints = true;
			}
		}

		if (!hasPathPoints && world.Has<MoveTo>(Entity))
		{
			var mt = world.Get<MoveTo>(Entity);
			points.Add(new Vector3(mt.Target.X, mt.Target.Y, mt.Target.Z));
		}

		if (!hasPathPoints && world.Has<PreviewMoveTo>(Entity))
		{
			var preview = world.Get<PreviewMoveTo>(Entity);
			points.Add(new Vector3(preview.Target.X, preview.Target.Y, preview.Target.Z));
		}

		if (world.Has<WaypointQueue>(Entity))
		{
			var q = world.Get<WaypointQueue>(Entity);
			for (int i = 0; i < q.Count; i++)
			{
				points.Add(new Vector3(q.Waypoints[i].X, q.Waypoints[i].Y, q.Waypoints[i].Z));
			}
		}
	}

	private const float PathSampleSpacing = 1.5f;
	private const float PathMarkerSpacing = 4.5f;
	private const float PathGroundOffset = 0.12f;
	private const float PathBaseWidth = 0.14f;
	private const float PathTaperLengthRatio = 0.55f;
	private const float PathFadeLengthRatio = 0.6f;

	private Vector3 SamplePathGround(Vector3 point)
	{
		if (GameContext.Host?.GroundTerrain != null)
		{
			GameContext.Host.GroundTerrain.GetHeightAndNormal(point.X, point.Z, out float height, out _);
			return new Vector3(point.X, height + PathGroundOffset, point.Z);
		}

		return new Vector3(point.X, point.Y + PathGroundOffset, point.Z);
	}

	private void BuildSampledPath()
	{
		_pathSampledPoints.Clear();

		if (_pathPointsBuffer.Count <= 1)
			return;

		_pathSampledPoints.Add(SamplePathGround(_pathPointsBuffer[0]));

		for (int segment = 0; segment + 1 < _pathPointsBuffer.Count; segment++)
		{
			Vector3 start = _pathPointsBuffer[segment];
			Vector3 end = _pathPointsBuffer[segment + 1];
			float horizontalLength = new Vector3(end.X - start.X, 0f, end.Z - start.Z).Length();
			int steps = Mathf.Max(1, Mathf.CeilToInt(horizontalLength / PathSampleSpacing));

			for (int step = 1; step <= steps; step++)
			{
				Vector3 point = start.Lerp(end, step / (float)steps);
				_pathSampledPoints.Add(SamplePathGround(point));
			}
		}
	}

	private void UpdatePathVisuals()
	{
		EnsurePathVisualsContainer();
		_pathVisualsContainer.Visible = true;

		GetRemainingPathPoints(_pathPointsBuffer);

		if (_pathPointsBuffer.Count <= 1)
		{
			HidePathVisuals();
			_lastPathRenderIsAttackPath = IsAttackPath;
			_lastRenderedPathPoints.Clear();
			_pathSampledPoints.Clear();
			return;
		}

		bool attackModeUnchanged = IsAttackPath == _lastPathRenderIsAttackPath;
		bool pointsUnchanged = PointsListEqual(_pathPointsBuffer, _lastRenderedPathPoints);

		if (attackModeUnchanged && pointsUnchanged && _pathSampledPoints.Count > 0) return;

		_lastPathRenderPos = _pathPointsBuffer[1];
		_lastPathRenderIsAttackPath = IsAttackPath;
		_lastRenderedPathPoints.Clear();
		_lastRenderedPathPoints.AddRange(_pathPointsBuffer);

		BuildSampledPath();

		Color pathColor = IsAttackPath ? new Color(0.9f, 0.22f, 0.18f) : new Color(0.22f, 0.62f, 1f);
		int sampleCount = _pathSampledPoints.Count;

		float totalLength = 0f;
		for (int i = 1; i < sampleCount; i++)
		{
			totalLength += _pathSampledPoints[i].DistanceTo(_pathSampledPoints[i - 1]);
		}

		int lineIndex = 0;
		float travelled = 0f;
		for (int i = 1; i < sampleCount; i++)
		{
			Vector3 start = _pathSampledPoints[i - 1];
			Vector3 end = _pathSampledPoints[i];
			float segmentLength = start.DistanceTo(end);
			travelled += segmentLength;

			float progress = totalLength > 0.001f ? travelled / totalLength : 0f;
			float width = PathBaseWidth * (1f - PathTaperLengthRatio * progress);
			float alpha = 1f - PathFadeLengthRatio * progress;

			var line = GetOrCreateLine(lineIndex);
			line.Visible = true;
			UpdateLineTransform(line, start, end, width, pathColor, alpha);
			lineIndex++;
		}

		for (int i = lineIndex; i < _pathLinesPool.Count; i++)
		{
			_pathLinesPool[i].Visible = false;
		}

		int markerIndex = 0;
		travelled = 0f;
		float sinceLastMarker = 0f;
		for (int i = 1; i < sampleCount; i++)
		{
			float segmentLength = _pathSampledPoints[i].DistanceTo(_pathSampledPoints[i - 1]);
			travelled += segmentLength;
			sinceLastMarker += segmentLength;

			if (sinceLastMarker < PathMarkerSpacing)
				continue;

			sinceLastMarker = 0f;
			float progress = totalLength > 0.001f ? travelled / totalLength : 0f;
			var marker = GetOrCreateMarker(markerIndex);
			marker.Visible = true;
			marker.GlobalPosition = _pathSampledPoints[i];
			SetMarkerColor(marker, pathColor, 1f - PathFadeLengthRatio * progress);
			markerIndex++;
		}

		for (int i = markerIndex; i < _pathMarkersPool.Count; i++)
		{
			_pathMarkersPool[i].Visible = false;
		}
	}

	private static void SetMarkerColor(MeshInstance3D marker, Color color, float alpha)
	{
		if (marker.MaterialOverride is not StandardMaterial3D markerMaterial) return;

		Color shaded = new Color(color.R, color.G, color.B, alpha);
		markerMaterial.AlbedoColor = shaded;
		markerMaterial.Emission = new Color(color.R, color.G, color.B);
	}

	private void HidePathVisuals()
	{
		if (_pathVisualsContainer != null)
		{
			_pathVisualsContainer.Visible = false;
		}
	}

	private MeshInstance3D GetOrCreateMarker(int index)
	{
		while (_pathMarkersPool.Count <= index)
		{
			var marker = new MeshInstance3D();
			marker.Name = "Marker_MeshInstance3D";
			var cylinderMesh = new CylinderMesh();
			cylinderMesh.TopRadius = 0.25f;
			cylinderMesh.BottomRadius = 0.25f;
			cylinderMesh.Height = 0.05f;
			marker.Mesh = cylinderMesh;

			var mat = new StandardMaterial3D();
			mat.AlbedoColor = new Color(0.2f, 0.6f, 1.0f);
			mat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
			mat.EmissionEnabled = true;
			mat.Emission = new Color(0.2f, 0.6f, 1.0f);
			mat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
			marker.MaterialOverride = mat;

			_pathVisualsContainer.AddChild(marker);
			_pathMarkersPool.Add(marker);
		}

		var m = _pathMarkersPool[index];
		if (m.MaterialOverride is StandardMaterial3D markerMat)
		{
			Color c = IsAttackPath ? new Color(0.9f, 0.1f, 0.1f) : new Color(0.2f, 0.6f, 1.0f);
			markerMat.AlbedoColor = c;
			markerMat.Emission = c;
		}

		return m;
	}

	private MeshInstance3D GetOrCreateLine(int index)
	{
		while (_pathLinesPool.Count <= index)
		{
			var line = new MeshInstance3D();
			line.Name = "Line_MeshInstance3D";
			var boxMesh = new BoxMesh();
			boxMesh.Size = new Vector3(0.1f, 0.05f, 1.0f);
			line.Mesh = boxMesh;

			var mat = new StandardMaterial3D();
			mat.AlbedoColor = new Color(0.2f, 0.6f, 1.0f, 0.5f);
			mat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
			mat.EmissionEnabled = true;
			mat.Emission = new Color(0.2f, 0.6f, 1.0f);
			mat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
			line.MaterialOverride = mat;

			_pathVisualsContainer.AddChild(line);
			_pathLinesPool.Add(line);
		}

		var l = _pathLinesPool[index];
		if (l.MaterialOverride is StandardMaterial3D lineMat)
		{
			Color c = IsAttackPath ? new Color(0.9f, 0.1f, 0.1f, 0.5f) : new Color(0.2f, 0.6f, 1.0f, 0.5f);
			Color emissionColor = IsAttackPath ? new Color(0.9f, 0.1f, 0.1f) : new Color(0.2f, 0.6f, 1.0f);
			lineMat.AlbedoColor = c;
			lineMat.Emission = emissionColor;
		}

		return l;
	}

	public Color Modulate
	{
		get => new Color(1f, 1f, 1f, _modulateAlpha);
		set
		{
			if (Mathf.Abs(_modulateAlpha - value.A) < 0.005f) return;
			_modulateAlpha = value.A;
			if (_modelNode != null)
				SetAlphaRecursive(_modelNode, value.A);
		}
	}

	private float _modulateAlpha = 1f;

	public void SetConstructionAlpha(float alpha)
	{
		if (Mathf.Abs(_modulateAlpha - alpha) < 0.005f) return;
		_modulateAlpha = alpha;
		if (_modelNode != null)
		{
			SetAlphaRecursive(_modelNode, alpha);
		}
	}

	private void SetAlphaRecursive(Node node, float alpha)
	{
		if (node is MeshInstance3D mesh)
		{
			Material mat = mesh.MaterialOverride;
			if (mat is StandardMaterial3D stdMat)
			{
				if (!stdMat.HasMeta("AlphaDuplicated"))
				{
					stdMat = (StandardMaterial3D)stdMat.Duplicate();
					stdMat.SetMeta("AlphaDuplicated", true);
					mesh.MaterialOverride = stdMat;
				}
				stdMat.Transparency = alpha < 1f ? BaseMaterial3D.TransparencyEnum.Alpha : BaseMaterial3D.TransparencyEnum.Disabled;
				var c = stdMat.AlbedoColor;
				c.A = alpha;
				stdMat.AlbedoColor = c;
			}
		}

		int childCount = node.GetChildCount();
		for (int i = 0; i < childCount; i++)
		{
			SetAlphaRecursive(node.GetChild(i), alpha);
		}
	}
}
