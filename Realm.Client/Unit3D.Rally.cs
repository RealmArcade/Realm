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
	public static float GetMinY(Node node, Transform3D currentTransform = default)
	{
		if (currentTransform == default) currentTransform = Transform3D.Identity;
		float minY = float.MaxValue;
		bool foundMesh = false;
		GetMinYRecursive(node, currentTransform, ref minY, ref foundMesh);
		return foundMesh ? minY : 0f;
	}

	public static void GetMinYRecursive(Node node, Transform3D currentTransform, ref float minY, ref bool foundMesh)
	{
		if (node is MeshInstance3D meshInstance)
		{
			var mesh = meshInstance.Mesh;
			if (mesh != null)
			{
				var localAabb = mesh.GetAabb();

				Vector3[] corners = new Vector3[8]
				{
					new Vector3(localAabb.Position.X, localAabb.Position.Y, localAabb.Position.Z),
					new Vector3(localAabb.Position.X + localAabb.Size.X, localAabb.Position.Y, localAabb.Position.Z),
					new Vector3(localAabb.Position.X, localAabb.Position.Y + localAabb.Size.Y, localAabb.Position.Z),
					new Vector3(localAabb.Position.X, localAabb.Position.Y, localAabb.Position.Z + localAabb.Size.Z),
					new Vector3(localAabb.Position.X + localAabb.Size.X, localAabb.Position.Y + localAabb.Size.Y, localAabb.Position.Z),
					new Vector3(localAabb.Position.X + localAabb.Size.X, localAabb.Position.Y, localAabb.Position.Z + localAabb.Size.Z),
					new Vector3(localAabb.Position.X, localAabb.Position.Y + localAabb.Size.Y, localAabb.Position.Z + localAabb.Size.Z),
					new Vector3(localAabb.Position.X + localAabb.Size.X, localAabb.Position.Y + localAabb.Size.Y, localAabb.Position.Z + localAabb.Size.Z)
				};

				foreach (var corner in corners)
				{
					Vector3 modelSpaceCorner = currentTransform * corner;
					if (!foundMesh || modelSpaceCorner.Y < minY)
					{
						minY = modelSpaceCorner.Y;
						foundMesh = true;
					}
				}
			}
		}

		foreach (var child in node.GetChildren())
		{
			if (child is Node3D child3D)
			{
				GetMinYRecursive(child, currentTransform * child3D.Transform, ref minY, ref foundMesh);
			}
			else
			{
				GetMinYRecursive(child, currentTransform, ref minY, ref foundMesh);
			}
		}
	}

	private static Vector3 ToGodotVector(System.Numerics.Vector3 value) => new(value.X, value.Y, value.Z);

	private static System.Numerics.Vector3 ToEcsVector(Vector3 value) => new(value.X, value.Y, value.Z);

	private void FillRallyPath(List<System.Numerics.Vector3> points)
	{
		UnitPathQuery.FillRallyPath(Entity, ToEcsVector(Position), points);
	}

	private void EnsureRallyVisualsContainer()
	{
		if (_rallyVisualsContainer == null)
		{
			_rallyVisualsContainer = new Node3D();
			_rallyVisualsContainer.Name = "RallyVisualsContainer_Node3D";
			_rallyVisualsContainer.TopLevel = true;
			AddChild(_rallyVisualsContainer);
		}
	}

	private MeshInstance3D GetOrCreateRallyMarker(int index)
	{
		while (_rallyMarkersPool.Count <= index)
		{
			var marker = new MeshInstance3D();
			marker.Name = "Marker_MeshInstance3D";
			var cylinderMesh = new CylinderMesh();
			cylinderMesh.TopRadius = 0.25f;
			cylinderMesh.BottomRadius = 0.25f;
			cylinderMesh.Height = 0.05f;
			marker.Mesh = cylinderMesh;

			var mat = new StandardMaterial3D();
			mat.AlbedoColor = new Color(0.95f, 0.82f, 0.55f);
			mat.EmissionEnabled = true;
			mat.Emission = new Color(0.95f, 0.82f, 0.55f);
			mat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
			marker.MaterialOverride = mat;

			_rallyVisualsContainer.AddChild(marker);
			_rallyMarkersPool.Add(marker);
		}

		return _rallyMarkersPool[index];
	}

	private MeshInstance3D GetOrCreateRallyLine(int index)
	{
		while (_rallyLinesPool.Count <= index)
		{
			var line = new MeshInstance3D();
			line.Name = "Line_MeshInstance3D";
			var boxMesh = new BoxMesh();
			boxMesh.Size = new Vector3(0.1f, 0.05f, 1.0f);
			line.Mesh = boxMesh;

			var mat = new StandardMaterial3D();
			mat.AlbedoColor = new Color(0.95f, 0.82f, 0.55f, 0.5f);
			mat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
			mat.EmissionEnabled = true;
			mat.Emission = new Color(0.95f, 0.82f, 0.55f);
			mat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
			line.MaterialOverride = mat;

			_rallyVisualsContainer.AddChild(line);
			_rallyLinesPool.Add(line);
		}

		return _rallyLinesPool[index];
	}

	private void UpdateRallyMarkers(List<System.Numerics.Vector3> points, int markerCountNeeded)
	{
		for (int i = 0; i < markerCountNeeded; i++)
		{
			var marker = GetOrCreateRallyMarker(i);
			marker.Visible = true;
			Vector3 pos = ToGodotVector(points[i + 1]);
			if (GameContext.Host?.GroundTerrain is { } terrain)
			{
				terrain.GetHeightAndNormal(pos.X, pos.Z, out float h, out _);
				pos.Y = h + 0.1f;
			}

			marker.GlobalPosition = pos;
		}

		for (int i = Mathf.Max(0, markerCountNeeded); i < _rallyMarkersPool.Count; i++)
		{
			_rallyMarkersPool[i].Visible = false;
		}
	}

	private void UpdateLineTransform(MeshInstance3D line, Vector3 start, Vector3 end, float width, Color color, float alpha)
	{
		Vector3 diff = end - start;
		float length = diff.Length();

		if (length <= 0.02f)
		{
			line.Visible = false;
			return;
		}

		Vector3 direction = diff.Normalized();
		Vector3 upVector = Mathf.Abs(direction.Dot(Vector3.Up)) > 0.99f ? Vector3.Forward : Vector3.Up;
		var basis = Basis.LookingAt(direction, upVector).Scaled(new Vector3(width / 0.1f, 1f, length));
		line.GlobalTransform = new Transform3D(basis, start + diff * 0.5f);

		if (line.MaterialOverride is StandardMaterial3D lineMaterial)
		{
			lineMaterial.AlbedoColor = new Color(color.R, color.G, color.B, alpha * 0.75f);
			lineMaterial.Emission = new Color(color.R, color.G, color.B);
		}
	}

	private void UpdateRallyLines(List<System.Numerics.Vector3> points, int lineCountNeeded)
	{
		for (int i = 0; i < lineCountNeeded; i++)
		{
			var line = GetOrCreateRallyLine(i);
			line.Visible = true;

			Vector3 start = SamplePathGround(ToGodotVector(points[i]));
			Vector3 end = SamplePathGround(ToGodotVector(points[i + 1]));

			UpdateLineTransform(line, start, end, PathBaseWidth, new Color(0.95f, 0.85f, 0.25f), 0.85f);
		}

		for (int i = lineCountNeeded; i < _rallyLinesPool.Count; i++)
		{
			_rallyLinesPool[i].Visible = false;
		}
	}

	private static bool PointsListEqual(List<Vector3> a, List<Vector3> b)
	{
		if (a.Count != b.Count) return false;
		for (int i = 0; i < a.Count; i++)
		{
			if (a[i].DistanceSquaredTo(b[i]) > 0.0001f) return false;
		}

		return true;
	}

	private static bool PointsListEqual(List<System.Numerics.Vector3> a, List<System.Numerics.Vector3> b)
	{
		if (a.Count != b.Count) return false;
		for (int i = 0; i < a.Count; i++)
		{
			if (System.Numerics.Vector3.DistanceSquared(a[i], b[i]) > 0.0001f) return false;
		}

		return true;
	}

	public void UpdateRallyVisuals()
	{
		if (!IsBuilding || !GameContext.PerspectiveFilter.CanSeeOwnedUi(Player)) return;
		if (GameContext.Host == null || GameContext.World is not { } world || !world.IsAlive(Entity)) return;

		EnsureRallyVisualsContainer();
		if (!GameContext.Host.CanProduceUnits(this))
		{
			_rallyVisualsContainer.Visible = false;
			return;
		}

		_rallyVisualsContainer.Visible = IsSelected;
		if (!IsSelected) return;

		FillRallyPath(_rallyPointsBuffer);

		if (_rallyPointsBuffer.Count <= 1)
		{
			if (_rallyMarker != null) _rallyMarker.Visible = false;
			_lastRallyRenderPos = new Vector3(float.NaN, float.NaN, float.NaN);
			_lastRenderedRallyPoints.Clear();
			return;
		}

		Vector3 currentPos = ToGodotVector(_rallyPointsBuffer[0]);
		bool posUnchanged = !float.IsNaN(_lastRallyRenderPos.X) && currentPos.DistanceSquaredTo(_lastRallyRenderPos) < 0.0025f;
		bool pointsUnchanged = PointsListEqual(_rallyPointsBuffer, _lastRenderedRallyPoints);

		if (posUnchanged && pointsUnchanged) return;

		_lastRallyRenderPos = currentPos;
		_lastRenderedRallyPoints.Clear();
		_lastRenderedRallyPoints.AddRange(_rallyPointsBuffer);

		EnsureRallyMarkerExists();
		_rallyMarker.Visible = true;

		PositionRallyMarker(ToGodotVector(_rallyPointsBuffer[_rallyPointsBuffer.Count - 1]));

		UpdateRallyMarkers(_rallyPointsBuffer, _rallyPointsBuffer.Count - 2);
		UpdateRallyLines(_rallyPointsBuffer, _rallyPointsBuffer.Count - 1);
	}

	private void EnsureRallyMarkerExists()
	{
		if (_rallyMarker != null) return;

		var cylinderMesh = new CylinderMesh();
		cylinderMesh.TopRadius = 0.1f;
		cylinderMesh.BottomRadius = 0.1f;
		cylinderMesh.Height = 3.0f;
		_rallyMarker = new MeshInstance3D();
		_rallyMarker.Name = "RallyMarker_MeshInstance3D";
		_rallyMarker.Mesh = cylinderMesh;

		var markerMat = new StandardMaterial3D();
		markerMat.AlbedoColor = new Color(0.95f, 0.82f, 0.55f);
		markerMat.EmissionEnabled = true;
		markerMat.Emission = new Color(0.95f, 0.82f, 0.55f);
		_rallyMarker.MaterialOverride = markerMat;

		_rallyVisualsContainer.AddChild(_rallyMarker);
	}

	private void PositionRallyMarker(Vector3 finalPos)
	{
		if (GameContext.Host?.GroundTerrain is { } terrain)
		{
			terrain.GetHeightAndNormal(finalPos.X, finalPos.Z, out float hFinal, out _);
			finalPos.Y = hFinal + 1.5f;
		}

		_rallyMarker.GlobalPosition = finalPos;
	}


}
