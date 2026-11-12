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
		public void ShowScaleMapSilhouette(int previewWidth, int previewDepth)
		{
			if (GroundTerrain == null) return;

			float targetWidthSize = previewWidth * GroundTerrain.QuadSize;
			float targetDepthSize = previewDepth * GroundTerrain.QuadSize;

			if (_scaleMapSilhouetteMesh != null && GodotObject.IsInstanceValid(_scaleMapSilhouetteMesh))
			{
				if (_scaleMapSilhouetteMesh.Mesh is PlaneMesh existingPlane)
				{
					existingPlane.Size = new Vector2(targetWidthSize, targetDepthSize);
					return;
				}
			}

			HideScaleMapSilhouette();

			_scaleMapSilhouetteMesh = new MeshInstance3D();
			_scaleMapSilhouetteMesh.Name = "ScaleMapSilhouette";

			var plane = new PlaneMesh();
			plane.Size = new Vector2(targetWidthSize, targetDepthSize);
			plane.SubdivideWidth = 0;
			plane.SubdivideDepth = 0;
			_scaleMapSilhouetteMesh.Mesh = plane;

			var mat = new StandardMaterial3D();
			mat.AlbedoColor = new Color(0.8f, 0.5f, 0.05f, 0.25f);
			mat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
			mat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
			mat.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
			mat.NoDepthTest = true;
			mat.RenderPriority = 10;
			mat.EmissionEnabled = true;
			mat.Emission = new Color(1.0f, 0.6f, 0.1f) * 0.4f;
			_scaleMapSilhouetteMesh.MaterialOverride = mat;

			_scaleMapSilhouetteMesh.Position = new Vector3(0f, 1.0f, 0f);

			AddChild(_scaleMapSilhouetteMesh);
		}

		public void HideScaleMapSilhouette()
		{
			if (_scaleMapSilhouetteMesh != null)
			{
				_scaleMapSilhouetteMesh.QueueFree();
				_scaleMapSilhouetteMesh = null;
			}
		}

		public void RebuildCameraBoundsOverlay()
		{
			if (_cameraBoundsOverlayMesh == null || GroundTerrain == null || GroundTerrain.Cells == null) return;
			if (!GameContext.EditorService.Tools.CameraBoundsVisible) return;

			int width = GroundTerrain.Width;
			int depth = GroundTerrain.Depth;
			float quadSize = GroundTerrain.QuadSize;

			float halfW = width / 2.0f;
			float halfD = depth / 2.0f;

			float minWorldX = -halfW * quadSize;
			float maxWorldX = halfW * quadSize;
			float minWorldZ = -halfD * quadSize;
			float maxWorldZ = halfD * quadSize;

			float left = Mathf.Clamp(EditorCameraBoundsLeft, minWorldX, maxWorldX);
			float right = Mathf.Clamp(EditorCameraBoundsRight, minWorldX, maxWorldX);
			float top = Mathf.Clamp(EditorCameraBoundsTop, minWorldZ, maxWorldZ);
			float bottom = Mathf.Clamp(EditorCameraBoundsBottom, minWorldZ, maxWorldZ);

			var linePoints = new List<Vector3>();

			float GetTerrainHeightAtCoord(float worldX, float worldZ)
			{
				if (GroundTerrain == null || GroundTerrain.Cells == null) return 0f;
				float gridX = worldX / quadSize + halfW;
				float gridZ = worldZ / quadSize + halfD;
				int x0 = Mathf.Clamp((int)Mathf.Floor(gridX), 0, width - 1);
				int x1 = Mathf.Clamp(x0 + 1, 0, width - 1);
				int z0 = Mathf.Clamp((int)Mathf.Floor(gridZ), 0, depth - 1);
				int z1 = Mathf.Clamp(z0 + 1, 0, depth - 1);

				float tx = gridX - x0;
				float tz = gridZ - z0;

				var cells = GroundTerrain.Cells;
				float h00 = EditableTerrain.GetGridNodeHeight(x0, z0, cells, width, depth);
				float h10 = EditableTerrain.GetGridNodeHeight(x1, z0, cells, width, depth);
				float h01 = EditableTerrain.GetGridNodeHeight(x0, z1, cells, width, depth);
				float h11 = EditableTerrain.GetGridNodeHeight(x1, z1, cells, width, depth);

				float h0 = Mathf.Lerp(h00, h10, tx);
				float h1 = Mathf.Lerp(h01, h11, tx);
				return Mathf.Lerp(h0, h1, tz);
			}

			void AddSegmentedLine(float x1, float z1, float x2, float z2)
			{
				float dist = Mathf.Sqrt((x2 - x1) * (x2 - x1) + (z2 - z1) * (z2 - z1));
				int segments = Mathf.Max(1, (int)Mathf.Ceil(dist / quadSize));
				for (int i = 0; i < segments; i++)
				{
					float t1 = (float)i / segments;
					float t2 = (float)(i + 1) / segments;

					float lx1 = Mathf.Lerp(x1, x2, t1);
					float lz1 = Mathf.Lerp(z1, z2, t1);
					float lx2 = Mathf.Lerp(x1, x2, t2);
					float lz2 = Mathf.Lerp(z1, z2, t2);

					float y1 = GetTerrainHeightAtCoord(lx1, lz1) + 0.2f;
					float y2 = GetTerrainHeightAtCoord(lx2, lz2) + 0.2f;

					linePoints.Add(new Vector3(lx1, y1, lz1));
					linePoints.Add(new Vector3(lx2, y2, lz2));
				}
			}

			AddSegmentedLine(left, top, right, top);
			AddSegmentedLine(right, top, right, bottom);
			AddSegmentedLine(right, bottom, left, bottom);
			AddSegmentedLine(left, bottom, left, top);

			int totalVertices = linePoints.Count * 3;
			var vertices = new Vector3[totalVertices];
			var colors = new Color[totalVertices];
			int idx = 0;

			Color boundsColor = new Color(0.9f, 0.1f, 0.8f, 0.95f);

			for (int i = 0; i < linePoints.Count; i += 2)
			{
				Vector3 p1 = linePoints[i];
				Vector3 p2 = linePoints[i + 1];

				vertices[idx] = p1;
				colors[idx] = boundsColor;
				idx++;
				vertices[idx] = p2;
				colors[idx] = boundsColor;
				idx++;

				Vector3 dir = (p2 - p1).Normalized();
				Vector3 ortho = new Vector3(-dir.Z, 0, dir.X) * 0.08f;

				vertices[idx] = p1 + ortho;
				colors[idx] = boundsColor;
				idx++;
				vertices[idx] = p2 + ortho;
				colors[idx] = boundsColor;
				idx++;

				vertices[idx] = p1 - ortho;
				colors[idx] = boundsColor;
				idx++;
				vertices[idx] = p2 - ortho;
				colors[idx] = boundsColor;
				idx++;
			}

			var arrays = new Array();
			arrays.Resize((int)Mesh.ArrayType.Max);
			arrays[(int)Mesh.ArrayType.Vertex] = vertices;
			arrays[(int)Mesh.ArrayType.Color] = colors;

			var arrayMesh = new ArrayMesh();
			arrayMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Lines, arrays);
			_cameraBoundsOverlayMesh.Mesh = arrayMesh;
		}

		private void InitializeCameraBoundsOverlay()
		{
			if (_cameraBoundsOverlayMesh != null) return;

			_cameraBoundsOverlayMesh = new MeshInstance3D();
			_cameraBoundsOverlayMesh.Name = "CameraBoundsOverlay";

			var mat = new StandardMaterial3D();
			mat.AlbedoColor = new Color(1.0f, 1.0f, 1.0f, 1.0f);
			mat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
			mat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
			mat.NoDepthTest = false;
			mat.VertexColorUseAsAlbedo = true;
			_cameraBoundsOverlayMesh.MaterialOverride = mat;

			AddChild(_cameraBoundsOverlayMesh);
			_cameraBoundsOverlayMesh.Visible = false;
		}

		private void RebuildPathingOverlay()
		{
			if (GroundTerrain == null || GroundTerrain.PathingCodes == null || GroundTerrain.Cells == null) return;
			GroundTerrain.UpdatePathingTexture();
		}

		public void UpdateGridOverlayVisibility()
		{
			if (GroundTerrain != null)
			{
				bool gridVisible = GameContext.SessionState.IsMapEditorMode && (GameContext.EditorService.Tools.GridMode == GridOverlayMode.Grid || GameContext.EditorService.Tools.GridMode == GridOverlayMode.Both);
				GroundTerrain.SetGridVisible(gridVisible);
				bool polarVisible = GameContext.SessionState.IsMapEditorMode && (EditorMirrorMode == MirrorMode.Rotational || GameContext.EditorService.Tools.GridMode == GridOverlayMode.Polar || GameContext.EditorService.Tools.GridMode == GridOverlayMode.Both);
				GroundTerrain.SetPolarOverlayVisible(polarVisible);
				GameContext.EditorService.Tools.PolarOverlayVisible = polarVisible;
			}

			UpdateSymmetryPivotVisuals();
		}

		public void UpdatePolarOverlayVisibility()
		{
			UpdateGridOverlayVisibility();
		}

		private MeshInstance3D GetOrCreateSymmetryHighlightMesh(int index)
		{
			while (_symmetryHighlightMeshes.Count <= index)
			{
				var meshInst = new MeshInstance3D();
				meshInst.Name = $"SymmetrySelectionHighlight_{_symmetryHighlightMeshes.Count}";
				var mat = new StandardMaterial3D();
				mat.AlbedoColor = new Color(0.0f, 0.6f, 1.0f, 0.35f);
				mat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
				mat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
				mat.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
				meshInst.MaterialOverride = mat;
				AddChild(meshInst);
				meshInst.Visible = false;
				_symmetryHighlightMeshes.Add(meshInst);
			}

			return _symmetryHighlightMeshes[index];
		}

		private void CreateSelectionHighlight()
		{
			if (_selectionHighlightMesh != null) return;
			_selectionHighlightMesh = new MeshInstance3D();
			_selectionHighlightMesh.Name = "SelectionHighlight";
			var mat = new StandardMaterial3D();
			mat.AlbedoColor = new Color(0.0f, 0.6f, 1.0f, 0.35f);
			mat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
			mat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
			mat.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
			_selectionHighlightMesh.MaterialOverride = mat;
			AddChild(_selectionHighlightMesh);
			_selectionHighlightMesh.Visible = false;
		}

		public void InvalidateSelectionHighlightMesh()
		{
			GameContext.EditorService.Tools.LastSelectionMinX = -1;
			GameContext.EditorService.Tools.LastSelectionMinZ = -1;
			GameContext.EditorService.Tools.LastSelectionMaxX = -1;
			GameContext.EditorService.Tools.LastSelectionMaxZ = -1;
		}

		public void RebuildSelectionHighlightMeshExternal(int minX, int minZ, int maxX, int maxZ)
		{
			GameContext.EditorService.Tools.LastSelectionMinX = -1;
			if (ActiveEditorTool == EditorTool.PasteArea && GameContext.EditorService.Clipboard.HasCopiedArea)
			{
				int w = maxX - minX + 1;
				int d = maxZ - minZ + 1;
				UpdatePasteSelectionHighlights(minX, minZ, w, d);
			}
			else
			{
				RebuildSelectionHighlightMesh(minX, minZ, maxX, maxZ);
			}
		}

		private void BuildHighlightMeshForBounds(MeshInstance3D meshInst, int minX, int minZ, int maxX, int maxZ)
		{
			if (meshInst == null || GroundTerrain == null || GroundTerrain.Cells == null) return;
			int selWidth = maxX - minX + 1;
			int selDepth = maxZ - minZ + 1;
			if (selWidth < 2 || selDepth < 2)
			{
				meshInst.Visible = false;
				return;
			}

			var vertices = BuildHighlightMeshVertices(selWidth, selDepth, minX, minZ);
			var indices = BuildHighlightMeshIndices(selWidth, selDepth, minX, minZ, maxX, maxZ);

			if (indices.Length == 0)
			{
				meshInst.Visible = false;
				return;
			}

			var arrays = new Array();
			arrays.Resize((int)Mesh.ArrayType.Max);
			arrays[(int)Mesh.ArrayType.Vertex] = vertices;
			arrays[(int)Mesh.ArrayType.Index] = indices;

			var arrayMesh = new ArrayMesh();
			arrayMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
			meshInst.Mesh = arrayMesh;
			meshInst.Visible = true;
		}

		private int[] BuildHighlightMeshIndices(int selWidth, int selDepth, int minX, int minZ, int maxX, int maxZ)
		{
			int cellWidth = selWidth - 1;
			int cellDepth = selDepth - 1;
			int indexCount = cellWidth * cellDepth * 6;
			var indices = new int[indexCount];
			int iIdx = 0;

			float selCenterX = (minX + maxX) * 0.5f;
			float selCenterZ = (minZ + maxZ) * 0.5f;
			float rx = Math.Max(0.5f, (maxX - minX) * 0.5f);
			float rz = Math.Max(0.5f, (maxZ - minZ) * 0.5f);

			for (int sz = 0; sz < cellDepth; sz++)
			{
				int row0 = sz * selWidth;
				int row1 = (sz + 1) * selWidth;
				for (int sx = 0; sx < cellWidth; sx++)
				{
					if (ShouldIncludeQuad(sx, sz, minX, minZ, selCenterX, selCenterZ, rx, rz))
					{
						AddQuadIndices(indices, ref iIdx, row0, row1, sx);
					}
				}
			}

			if (iIdx < indexCount) System.Array.Resize(ref indices, iIdx);
			return indices;
		}

		private void RebuildSelectionHighlightMesh(int minX, int minZ, int maxX, int maxZ)
		{
			if (!CanRebuildSelectionHighlightMesh(minX, minZ, maxX, maxZ)) return;
			if (_selectionHighlightMesh is not { } selectionMesh) return;

			GameContext.EditorService.Tools.LastSelectionMinX = minX;
			GameContext.EditorService.Tools.LastSelectionMinZ = minZ;
			GameContext.EditorService.Tools.LastSelectionMaxX = maxX;
			GameContext.EditorService.Tools.LastSelectionMaxZ = maxZ;
			GameContext.EditorService.Tools.LastSelectionBrushIsSquare = EditorBrushIsSquare;

			BuildHighlightMeshForBounds(selectionMesh, minX, minZ, maxX, maxZ);
		}

		private bool IsSelectionUnchanged(int minX, int minZ, int maxX, int maxZ)
		{
			return minX == GameContext.EditorService.Tools.LastSelectionMinX
			       && minZ == GameContext.EditorService.Tools.LastSelectionMinZ
			       && maxX == GameContext.EditorService.Tools.LastSelectionMaxX
			       && maxZ == GameContext.EditorService.Tools.LastSelectionMaxZ
			       && GameContext.EditorService.Tools.LastSelectionBrushIsSquare == EditorBrushIsSquare
			       && _selectionHighlightMesh?.Visible == true;
		}

		private bool CanRebuildSelectionHighlightMesh(int minX, int minZ, int maxX, int maxZ)
		{
			if (_selectionHighlightMesh == null) return false;
			if (GroundTerrain == null) return false;
			if (GroundTerrain.Cells == null) return false;

			int selWidth = maxX - minX + 1;
			int selDepth = maxZ - minZ + 1;
			if (selWidth < 2 || selDepth < 2)
			{
				_selectionHighlightMesh.Visible = false;
				GameContext.EditorService.Tools.LastSelectionMinX = -1;
				return false;
			}

			if (IsSelectionUnchanged(minX, minZ, maxX, maxZ))
			{
				return false;
			}

			return true;
		}

	}
}
