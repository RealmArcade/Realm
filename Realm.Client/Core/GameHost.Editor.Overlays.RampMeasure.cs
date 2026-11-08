using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using Godot;
using Realm.Client.Services;
using Realm.Client.UI;
using Realm.Ecs;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Services;
using Array = Godot.Collections.Array;

namespace Realm.Client.Core
{
	public partial class GameHost
	{
		private MeshInstance3D _rampPreviewMesh { get; set; } = null!;
		private ImmediateMesh _rampPreviewImmediateMesh { get; set; } = null!;

		public void UpdateRampPreviewVisuals(Vector3 start, Vector3 end)
		{
			if (_rampPreviewMesh == null)
			{
				_rampPreviewImmediateMesh = new ImmediateMesh();
				_rampPreviewMesh = new MeshInstance3D
				{
					Name = "RampDirectionArrow",
					CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
					GIMode = GeometryInstance3D.GIModeEnum.Disabled,
					Mesh = _rampPreviewImmediateMesh,
					MaterialOverride = new StandardMaterial3D
					{
						ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
						AlbedoColor = new Color(0.35f, 0.85f, 1.0f, 0.9f),
						Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
						CullMode = BaseMaterial3D.CullModeEnum.Disabled
					}
				};
				AddChild(_rampPreviewMesh);
			}

			float lift = 0.25f;
			Vector3 from = start + new Vector3(0f, lift, 0f);
			Vector3 to = end + new Vector3(0f, lift, 0f);
			Vector3 flat = new Vector3(to.X - from.X, 0f, to.Z - from.Z);
			float length = flat.Length();
			_rampPreviewImmediateMesh.ClearSurfaces();
			if (length < 0.05f)
			{
				_rampPreviewMesh.Visible = false;
				return;
			}

			Vector3 direction = flat / length;
			Vector3 side = new Vector3(-direction.Z, 0f, direction.X);
			float shaftHalfWidth = MathF.Max(0.12f, length * 0.02f);
			float headLength = MathF.Min(length * 0.4f, MathF.Max(0.9f, length * 0.18f));
			float headHalfWidth = shaftHalfWidth * 3.5f;
			Vector3 headBase = to - direction * headLength;

			_rampPreviewImmediateMesh.SurfaceBegin(Mesh.PrimitiveType.Triangles);
			AddRampPreviewQuad(_rampPreviewImmediateMesh, from - side * shaftHalfWidth, from + side * shaftHalfWidth, headBase + side * shaftHalfWidth, headBase - side * shaftHalfWidth);
			AddRampPreviewTriangle(_rampPreviewImmediateMesh, headBase - side * headHalfWidth, headBase + side * headHalfWidth, to);
			_rampPreviewImmediateMesh.SurfaceEnd();
			_rampPreviewMesh.Visible = true;
		}

		private static void AddRampPreviewQuad(ImmediateMesh mesh, Vector3 cornerA, Vector3 cornerB, Vector3 cornerC, Vector3 cornerD)
		{
			mesh.SurfaceAddVertex(cornerA);
			mesh.SurfaceAddVertex(cornerB);
			mesh.SurfaceAddVertex(cornerC);
			mesh.SurfaceAddVertex(cornerA);
			mesh.SurfaceAddVertex(cornerC);
			mesh.SurfaceAddVertex(cornerD);
		}

		private static void AddRampPreviewTriangle(ImmediateMesh mesh, Vector3 cornerA, Vector3 cornerB, Vector3 tip)
		{
			mesh.SurfaceAddVertex(cornerA);
			mesh.SurfaceAddVertex(cornerB);
			mesh.SurfaceAddVertex(tip);
		}

		public void ClearRampPreviewVisuals()
		{
			if (_rampPreviewMesh != null) _rampPreviewMesh.Visible = false;
		}

		public void UpdateMeasureVisuals(Vector3 start, Vector3 end)
		{
			if (_measureMeshInstance == null)
			{
				_measureMeshInstance = new MeshInstance3D();
				_measureMeshInstance.Name = "TapeMeasureIndicator";
				_measureImmediateMesh = new ImmediateMesh();
				_measureMeshInstance.Mesh = _measureImmediateMesh;
				var mat = new StandardMaterial3D
				{
					ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
					AlbedoColor = new Color(1.0f, 0.85f, 0.1f, 0.95f),
					Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
					CullMode = BaseMaterial3D.CullModeEnum.Disabled
				};
				_measureMeshInstance.MaterialOverride = mat;
				AddChild(_measureMeshInstance);
			}

			_measureImmediateMesh.ClearSurfaces();
			_measureImmediateMesh.SurfaceBegin(Mesh.PrimitiveType.Lines);
			_measureImmediateMesh.SurfaceAddVertex(start + new Vector3(0, 0.2f, 0));
			_measureImmediateMesh.SurfaceAddVertex(end + new Vector3(0, 0.2f, 0));

			float markerSize = 0.5f;
			_measureImmediateMesh.SurfaceAddVertex(start + new Vector3(-markerSize, 0.2f, 0));
			_measureImmediateMesh.SurfaceAddVertex(start + new Vector3(markerSize, 0.2f, 0));
			_measureImmediateMesh.SurfaceAddVertex(start + new Vector3(0, 0.2f, -markerSize));
			_measureImmediateMesh.SurfaceAddVertex(start + new Vector3(0, 0.2f, markerSize));

			_measureImmediateMesh.SurfaceAddVertex(end + new Vector3(-markerSize, 0.2f, 0));
			_measureImmediateMesh.SurfaceAddVertex(end + new Vector3(markerSize, 0.2f, 0));
			_measureImmediateMesh.SurfaceAddVertex(end + new Vector3(0, 0.2f, -markerSize));
			_measureImmediateMesh.SurfaceAddVertex(end + new Vector3(0, 0.2f, markerSize));

			_measureImmediateMesh.SurfaceEnd();
			_measureMeshInstance.Visible = true;
		}

	}
}
