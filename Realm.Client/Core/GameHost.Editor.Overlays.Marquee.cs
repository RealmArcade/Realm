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
		public void HideCoordinatePreviewMesh()
		{
			if (_coordinatePreviewMesh != null) _coordinatePreviewMesh.Visible = false;
		}

		private void UpdateCoordinateOutline(EditorCoordinate coord)
		{
			MeshInstance3D outlineMesh = _coordinateSelectionOutlineMesh ?? CreateCoordinateSelectionOutlineMesh();

			var (minX, minZ, maxX, maxZ) = GetClampedCoordinateBounds(coord);

			outlineMesh.Visible = true;
			RebuildCoordinateMeshInstance(outlineMesh, minX, minZ, maxX, maxZ, new Color(1.0f, 0.6f, 0.0f, 0.45f), 0.25f);
		}

		private MeshInstance3D CreateCoordinateSelectionOutlineMesh()
		{
			_coordinateSelectionOutlineMesh = new MeshInstance3D();
			_coordinateSelectionOutlineMesh.Name = "CoordinateSelectionOutline";

			var mat = new StandardMaterial3D();
			mat.AlbedoColor = new Color(1.0f, 0.6f, 0.0f, 0.45f);
			mat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
			mat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
			mat.CullMode = BaseMaterial3D.CullModeEnum.Disabled;

			_coordinateSelectionOutlineMesh.MaterialOverride = mat;
			AddChild(_coordinateSelectionOutlineMesh);
			return _coordinateSelectionOutlineMesh;
		}

		private void EnsureObjectMarqueeMeshes()
		{
			if (_objectMarqueeFillMesh == null)
			{
				_objectMarqueeFillMesh = new MeshInstance3D();
				_objectMarqueeFillMesh.Name = "ObjectMarqueeFill";
				_objectMarqueeFillMesh.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
				_objectMarqueeFillMesh.GIMode = GeometryInstance3D.GIModeEnum.Disabled;

				var fillMaterial = new StandardMaterial3D();
				fillMaterial.AlbedoColor = new Color(0.10f, 0.85f, 0.30f, 0.22f);
				fillMaterial.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
				fillMaterial.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
				fillMaterial.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
				_objectMarqueeFillMesh.MaterialOverride = fillMaterial;

				AddChild(_objectMarqueeFillMesh);
				_objectMarqueeFillMesh.Visible = false;
			}

			if (_objectMarqueeOutlineMesh == null)
			{
				_objectMarqueeOutlineMesh = new MeshInstance3D();
				_objectMarqueeOutlineMesh.Name = "ObjectMarqueeOutline";
				_objectMarqueeOutlineMesh.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
				_objectMarqueeOutlineMesh.GIMode = GeometryInstance3D.GIModeEnum.Disabled;

				var outlineMaterial = new StandardMaterial3D();
				outlineMaterial.AlbedoColor = new Color(0.25f, 1.00f, 0.45f, 0.95f);
				outlineMaterial.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
				outlineMaterial.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
				outlineMaterial.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
				_objectMarqueeOutlineMesh.MaterialOverride = outlineMaterial;

				AddChild(_objectMarqueeOutlineMesh);
				_objectMarqueeOutlineMesh.Visible = false;
			}
		}

		public void UpdateEditorObjectMarqueeVisual()
		{
			var editorService = GameContext.EditorService;
			if (!editorService.Interaction.IsObjectMarqueeSelecting || editorService.Interaction.ObjectMarqueeStart == null || editorService.Interaction.ObjectMarqueeEnd == null)
			{
				HideEditorObjectMarquee();
				return;
			}

			Vector2I start = editorService.Interaction.ObjectMarqueeStart.Value;
			Vector2I end = editorService.Interaction.ObjectMarqueeEnd.Value;
			int minX = Mathf.Min(start.X, end.X);
			int minZ = Mathf.Min(start.Y, end.Y);
			int maxX = Mathf.Max(start.X, end.X);
			int maxZ = Mathf.Max(start.Y, end.Y);

			EnsureObjectMarqueeMeshes();
			RebuildCoordinateMeshInstance(_objectMarqueeFillMesh, minX, minZ, maxX, maxZ, new Color(0.10f, 0.85f, 0.30f, 0.22f), 0.08f);
			RebuildObjectMarqueeOutline(minX, minZ, maxX, maxZ);
		}

		public void HideEditorObjectMarquee()
		{
			if (_objectMarqueeFillMesh != null && GodotObject.IsInstanceValid(_objectMarqueeFillMesh))
			{
				_objectMarqueeFillMesh.Visible = false;
			}

			if (_objectMarqueeOutlineMesh != null && GodotObject.IsInstanceValid(_objectMarqueeOutlineMesh))
			{
				_objectMarqueeOutlineMesh.Visible = false;
			}
		}

		private void RebuildObjectMarqueeOutline(int minX, int minZ, int maxX, int maxZ)
		{
			if (_objectMarqueeOutlineMesh == null || GroundTerrain?.Cells == null)
			{
				HideEditorObjectMarquee();
				return;
			}

			int selectionWidth = maxX - minX + 1;
			int selectionDepth = maxZ - minZ + 1;
			if (selectionWidth < 2 || selectionDepth < 2)
			{
				_objectMarqueeOutlineMesh.Visible = false;
				return;
			}

			Vector3[] gridVertices = GenerateGridMeshVertices(minX, minZ, selectionWidth, selectionDepth, 0.12f);
			int horizontalSegments = selectionWidth - 1;
			int verticalSegments = selectionDepth - 1;
			float borderWidth = Mathf.Max(0.05f, GroundTerrain.QuadSize * 0.10f);

			var outlineVertices = new List<Vector3>();
			var outlineIndices = new List<int>();
			int bottomRow = verticalSegments * selectionWidth;

			for (int x = 0; x < horizontalSegments; x++)
			{
				AppendMarqueeBorderQuad(outlineVertices, outlineIndices, gridVertices[x], gridVertices[x + 1], new Vector3(0.0f, 0.0f, borderWidth));
				AppendMarqueeBorderQuad(outlineVertices, outlineIndices, gridVertices[bottomRow + x], gridVertices[bottomRow + x + 1], new Vector3(0.0f, 0.0f, -borderWidth));
			}

			int rightColumn = selectionWidth - 1;
			for (int z = 0; z < verticalSegments; z++)
			{
				int row = z * selectionWidth;
				int nextRow = (z + 1) * selectionWidth;
				AppendMarqueeBorderQuad(outlineVertices, outlineIndices, gridVertices[row], gridVertices[nextRow], new Vector3(borderWidth, 0.0f, 0.0f));
				AppendMarqueeBorderQuad(outlineVertices, outlineIndices, gridVertices[row + rightColumn], gridVertices[nextRow + rightColumn], new Vector3(-borderWidth, 0.0f, 0.0f));
			}

			var arrays = new Array();
			arrays.Resize((int)Mesh.ArrayType.Max);
			arrays[(int)Mesh.ArrayType.Vertex] = outlineVertices.ToArray();
			arrays[(int)Mesh.ArrayType.Index] = outlineIndices.ToArray();

			var arrayMesh = new ArrayMesh();
			arrayMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
			_objectMarqueeOutlineMesh.Mesh = arrayMesh;
			_objectMarqueeOutlineMesh.Visible = true;
		}

		private static void AppendMarqueeBorderQuad(List<Vector3> vertices, List<int> indices, Vector3 edgeStart, Vector3 edgeEnd, Vector3 inwardOffset)
		{
			int baseIndex = vertices.Count;
			vertices.Add(edgeStart);
			vertices.Add(edgeEnd);
			vertices.Add(edgeStart + inwardOffset);
			vertices.Add(edgeEnd + inwardOffset);

			indices.Add(baseIndex + 0);
			indices.Add(baseIndex + 1);
			indices.Add(baseIndex + 2);
			indices.Add(baseIndex + 1);
			indices.Add(baseIndex + 3);
			indices.Add(baseIndex + 2);
		}

		public void HideCoordinateSelectionOutline()
		{
			if (_coordinateSelectionOutlineMesh != null && GodotObject.IsInstanceValid(_coordinateSelectionOutlineMesh))
			{
				_coordinateSelectionOutlineMesh.Visible = false;
				_coordinateSelectionOutlineMesh.Mesh = null;
			}
		}

	}
}
