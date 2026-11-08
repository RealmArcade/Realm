using System;
using System.Collections.Generic;
using System.IO;
using Realm.Ecs;
using Godot;
using Realm.Client.ReplaySystem;
using Realm.Client.Services;
using Realm.Client.UI;
using Realm.Client.UI.MapEditor;
using Realm.Client.VFX;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Terrain;
using Realm.Ecs.Services;
using Realm.Shared.Services;

namespace Realm.Client.Core
{
	public partial class GameHost
	{
		public void ClearMeasureVisuals()
		{
			if (_measureMeshInstance != null)
			{
				_measureMeshInstance.Visible = false;
				_measureImmediateMesh?.ClearSurfaces();
			}

			GameContext.EditorService.Tools.TapeMeasureStart = null;
			EditorTapeMeasureEnd = null;
			EditorTapeMeasureActive = false;
		}

		public void UpdateSymmetryPivotVisuals()
		{
			if (!GameContext.SessionState.IsMapEditorMode || GroundTerrain == null)
			{
				if (_symmetryPivotMarkerMesh != null) _symmetryPivotMarkerMesh.Visible = false;
				return;
			}

			bool showPivot = EditorMirrorMode != MirrorMode.Rotational && (EditorMirrorMode != MirrorMode.None || GameContext.EditorService.Tools.PolarOverlayVisible);
			if (!showPivot)
			{
				if (_symmetryPivotMarkerMesh != null) _symmetryPivotMarkerMesh.Visible = false;
				return;
			}

			if (_symmetryPivotMarkerMesh == null)
			{
				_symmetryPivotMarkerMesh = new MeshInstance3D();
				_symmetryPivotMarkerMesh.Name = "SymmetryPivotMarker";
				var imm = new ImmediateMesh();
				_symmetryPivotMarkerMesh.Mesh = imm;
				var mat = new StandardMaterial3D
				{
					ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
					AlbedoColor = new Color(0.2f, 0.9f, 1.0f, 0.95f),
					Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
					CullMode = BaseMaterial3D.CullModeEnum.Disabled
				};
				_symmetryPivotMarkerMesh.MaterialOverride = mat;
				AddChild(_symmetryPivotMarkerMesh);
			}

			var immMesh = (ImmediateMesh)_symmetryPivotMarkerMesh.Mesh;
			immMesh.ClearSurfaces();
			immMesh.SurfaceBegin(Mesh.PrimitiveType.Lines);

			float px = GameContext.EditorService.Symmetry.Pivot.X;
			float pz = GameContext.EditorService.Symmetry.Pivot.Y;
			float py = GameContext.EditorService.GetTerrainHeightAt(new Vector3(px, 0, pz)) + 0.25f;
			float s = 1.5f;

			immMesh.SurfaceAddVertex(new Vector3(px - s, py, pz));
			immMesh.SurfaceAddVertex(new Vector3(px + s, py, pz));
			immMesh.SurfaceAddVertex(new Vector3(px, py, pz - s));
			immMesh.SurfaceAddVertex(new Vector3(px, py, pz + s));

			immMesh.SurfaceEnd();
			_symmetryPivotMarkerMesh.Visible = true;
		}

		public void ClearRampStartPosExternal()
		{
			GameContext.EditorService.SetRampStartPos(null);
			_rampPressActive = false;
			_rampPressDragging = false;
			ClearRampPreviewVisuals();
		}

		public List<MirroredTransform> GetMirroredTransforms(Vector3 pos, float rotation)
		{
			return GameContext.EditorService.Symmetry.GetTransforms(pos, rotation, EditorMirrorMode);
		}

		private bool ApplyRampInternal(Vector3 start, Vector3 end)
		{
			return GameContext.EditorService.ApplyRamp(start, end, EditorBrushRadius, EditorBlockMode, EditorBlockLevelHeight);
		}

		private bool CommitRamp(Vector3 start, Vector3 end)
		{
			if (GroundTerrain == null || GroundTerrain.Cells == null || GroundTerrain.SplatMap == null || GroundTerrain.PathingCodes == null) return false;

			var cellsBefore = (TerrainCell[,])GroundTerrain.Cells.Clone();
			var splatBefore = (TerrainSplatWeights[,])GroundTerrain.SplatMap.Clone();
			var cliffBefore = GroundTerrain.CliffSplatMap != null ? (TerrainSplatWeights[,])GroundTerrain.CliffSplatMap.Clone() : null;
			var pathingBefore = (int[,])GroundTerrain.PathingCodes.Clone();
			bool modified = ApplyRampInternal(start, end);

			if (EditorMirrorMode != MirrorMode.None)
			{
				var startMirrored = GetMirroredTransforms(start, 0.0f);
				var endMirrored = GetMirroredTransforms(end, 0.0f);
				for (int index = 0; index < startMirrored.Count; index++)
				{
					if (ApplyRampInternal(startMirrored[index].Position, endMirrored[index].Position)) modified = true;
				}
			}

			if (!modified) return false;

			float minX = Mathf.Min(start.X, end.X);
			float maxX = Mathf.Max(start.X, end.X);
			float minZ = Mathf.Min(start.Z, end.Z);
			float maxZ = Mathf.Max(start.Z, end.Z);

			if (EditorMirrorMode != MirrorMode.None)
			{
				var startMirrored = GetMirroredTransforms(start, 0.0f);
				var endMirrored = GetMirroredTransforms(end, 0.0f);
				for (int index = 0; index < startMirrored.Count; index++)
				{
					minX = Mathf.Min(minX, Mathf.Min(startMirrored[index].Position.X, endMirrored[index].Position.X));
					maxX = Mathf.Max(maxX, Mathf.Max(startMirrored[index].Position.X, endMirrored[index].Position.X));
					minZ = Mathf.Min(minZ, Mathf.Min(startMirrored[index].Position.Z, endMirrored[index].Position.Z));
					maxZ = Mathf.Max(maxZ, Mathf.Max(startMirrored[index].Position.Z, endMirrored[index].Position.Z));
				}
			}

			float brushRadius = EditorBrushRadius;
			float quadSize = GroundTerrain.QuadSize;
			int width = GroundTerrain.Width;
			int depth = GroundTerrain.Depth;

			int minGridX = Mathf.Clamp(Mathf.FloorToInt((minX - brushRadius) / quadSize + (width - 1) / 2.0f), 0, width - 1);
			int maxGridX = Mathf.Clamp(Mathf.CeilToInt((maxX + brushRadius) / quadSize + (width - 1) / 2.0f), 0, width - 1);
			int minGridZ = Mathf.Clamp(Mathf.FloorToInt((minZ - brushRadius) / quadSize + (depth - 1) / 2.0f), 0, depth - 1);
			int maxGridZ = Mathf.Clamp(Mathf.CeilToInt((maxZ + brushRadius) / quadSize + (depth - 1) / 2.0f), 0, depth - 1);

			Rect2I affected = new Rect2I(minGridX - 2, minGridZ - 2, maxGridX - minGridX + 4, maxGridZ - minGridZ + 4);

			GroundTerrain.UpdateMeshAndPhysics(true, false, affected);
			AlignAllEntitiesToTerrain(affected);
			var cellsAfter = (TerrainCell[,])GroundTerrain.Cells.Clone();
			var splatAfter = (TerrainSplatWeights[,])GroundTerrain.SplatMap.Clone();
			var cliffAfter = GroundTerrain.CliffSplatMap != null ? (TerrainSplatWeights[,])GroundTerrain.CliffSplatMap.Clone() : null;
			var pathingAfter = (int[,])GroundTerrain.PathingCodes.Clone();
			var action = new TerrainModifyAction(cellsBefore, cellsAfter, splatBefore, splatAfter, pathingBefore, pathingAfter, cliffBefore, cliffAfter);
			EditorHistoryManager.RecordAction(action);
			EditorHasUnsavedChanges = true;
			UpdatePathingOverlay();
			return true;
		}

		private void ApplyGeneralClumpSpawn(Vector3 centerPos)
		{
			float assetBaseCollisionRadius = Mathf.Max(0.1f, ResolveTemplateCollisionRadius(ActivePlaceId));

			var requests = GameContext.EditorService.BuildClumpSpawnRequests(
				centerPos,
				ActiveEditorTool,
				ActivePlaceId,
				GameContext.EditorService.Tools.PlaceUnitPlayerIndex,
				EditorPlacementScale,
				EditorClumpCount,
				EditorClumpScale,
				EditorBrushRadius,
				EditorBrushIsSquare,
				GameContext.EditorService.Tools.RandomRotation,
				GameContext.EditorService.Tools.RandomScale,
				GameContext.EditorService.Tools.PlacementRotation,
				EditorMirrorMode,
				assetBaseCollisionRadius);

			foreach (var req in requests)
			{
				Node? spawnedNode = null;
				if (req.Type == "unit")
					spawnedNode = SpawnUnitExternal(req.Id, req.Position, req.Rotation, req.Scale, req.Player);
				else if (req.Type == "prop")
					spawnedNode = SpawnPropExternalWithParams(req.Id, req.Position, req.Rotation, req.Scale);
				else if (req.Type == "decal")
					spawnedNode = SpawnDecalExternalWithParams(req.Id, req.Position, req.Rotation, req.Scale);

				if (spawnedNode != null)
				{
					GameContext.EditorService.Clump.Record(new ObjectSpawnAction(req.Type, req.Id, req.Position, req.Rotation, req.Scale, spawnedNode, req.Player));
				}
			}
		}

		public void PerformFloodFill(Vector3 clickPos, int fillTextureIndex, bool isCliff = false)
		{
			if (GroundTerrain == null || GroundTerrain.Cells == null || GroundTerrain.SplatMap == null) return;

			if (GroundTerrain.CliffSplatMap == null)
			{
				GroundTerrain.CliffSplatMap = new TerrainSplatWeights[GroundTerrain.Width + 1, GroundTerrain.Depth + 1];
			}

			var splatBefore = (TerrainSplatWeights[,])GroundTerrain.SplatMap.Clone();
			var cliffBefore = (TerrainSplatWeights[,])GroundTerrain.CliffSplatMap.Clone();
			int cliffTextureIndex = GameContext.EditorService.Tools.CliffPaintTextureIndex;

			var result = GameContext.EditorService.PerformFloodFill(clickPos, fillTextureIndex, cliffTextureIndex, EditorMirrorMode, isCliff);
			if (result.SplatMap == null) return;

			if (result.IsCliff)
			{
				Array.Copy(result.SplatMap, GroundTerrain.CliffSplatMap, result.SplatMap.Length);
			}
			else
			{
				Array.Copy(result.SplatMap, GroundTerrain.SplatMap, result.SplatMap.Length);
			}

			GroundTerrain.UpdateMeshAndPhysics(false, false);
			var splatAfter = (TerrainSplatWeights[,])GroundTerrain.SplatMap.Clone();
			var cliffAfter = (TerrainSplatWeights[,])GroundTerrain.CliffSplatMap.Clone();
			var action = new TerrainModifyAction((TerrainCell[,]?)null, (TerrainCell[,]?)null, splatBefore, splatAfter, null, null, cliffBefore, cliffAfter);
			EditorHistoryManager.RecordAction(action);
			EditorHasUnsavedChanges = true;
			GameContext.MapEditorHud?.ShowFeedbackExternal(result.IsCliff ? "Flood filled cliff face area" : "Flood filled terrain area");
		}

		public void HideSelectionHighlight()
		{
			if (_selectionHighlightMesh != null)
			{
				_selectionHighlightMesh.Visible = false;
			}

			foreach (var symMesh in _symmetryHighlightMeshes)
			{
				if (GodotObject.IsInstanceValid(symMesh))
				{
					symMesh.Visible = false;
				}
			}

			GameContext.EditorService?.Interaction.SetIsSelectingArea(false);
			GameContext.EditorService?.Interaction.SetSelectionStart(null);
			GameContext.EditorService?.Interaction.SetSelectionEnd(null);
		}

		public void UpdatePasteSelectionHighlights(int startX, int startZ, int targetWidth, int targetDepth)
		{
			if (GroundTerrain == null || GroundTerrain.Cells == null || !GameContext.EditorService.Clipboard.HasCopiedArea) return;

			int width = GroundTerrain.Width;
			int depth = GroundTerrain.Depth;

			int minX = Mathf.Clamp(startX, 0, width - 1);
			int minZ = Mathf.Clamp(startZ, 0, depth - 1);
			int maxX = Mathf.Clamp(startX + targetWidth - 1, 0, width - 1);
			int maxZ = Mathf.Clamp(startZ + targetDepth - 1, 0, depth - 1);

			CreateSelectionHighlight();
			RebuildSelectionHighlightMesh(minX, minZ, maxX, maxZ);

			int symCount = 0;
			if (EditorMirrorMode != MirrorMode.None)
			{
				float quadSize = GroundTerrain.QuadSize;
				Vector3 centerPos = new Vector3((startX + targetWidth / 2.0f - width / 2.0f) * quadSize, 0, (startZ + targetDepth / 2.0f - depth / 2.0f) * quadSize);
				var transforms = GameContext.EditorService.Symmetry.GetTransforms(centerPos, 0.0f, EditorMirrorMode);
				symCount = transforms.Count;
				for (int i = 0; i < transforms.Count; i++)
				{
					var t = transforms[i];
					var (rcx, rcz) = EditorGridSnapping.WorldPosToCellCoords(t.Position);
					int rStartX = rcx - targetWidth / 2;
					int rStartZ = rcz - targetDepth / 2;
					int rMinX = Mathf.Clamp(rStartX, 0, width - 1);
					int rMinZ = Mathf.Clamp(rStartZ, 0, depth - 1);
					int rMaxX = Mathf.Clamp(rStartX + targetWidth - 1, 0, width - 1);
					int rMaxZ = Mathf.Clamp(rStartZ + targetDepth - 1, 0, depth - 1);

					var meshInst = GetOrCreateSymmetryHighlightMesh(i);
					BuildHighlightMeshForBounds(meshInst, rMinX, rMinZ, rMaxX, rMaxZ);
				}
			}

			for (int i = symCount; i < _symmetryHighlightMeshes.Count; i++)
			{
				if (GodotObject.IsInstanceValid(_symmetryHighlightMeshes[i]))
				{
					_symmetryHighlightMeshes[i].Visible = false;
				}
			}
		}

		private bool ShouldIncludeQuad(int sx, int sz, int minX, int minZ, float selCenterX, float selCenterZ, float rx, float rz)
		{
			if (ActiveEditorTool == EditorTool.SelectArea)
			{
				return EditorBrushIsSquare || IsWithinBrushRadius(sx, sz, minX, minZ, selCenterX, selCenterZ, rx, rz);
			}

			if (ActiveEditorTool == EditorTool.PasteArea)
			{
				if (GameContext.EditorService.Clipboard.HasCopiedArea && GameContext.EditorService.Clipboard.HasCopiedAreaMask)
				{
					return IsCopiedQuadMasked(sx, sz);
				}

				if (!EditorBrushIsSquare && !GameContext.EditorService.Clipboard.HasCopiedArea)
				{
					return IsWithinBrushRadius(sx, sz, minX, minZ, selCenterX, selCenterZ, rx, rz);
				}
			}

			return true;
		}

		private bool IsWithinBrushRadius(int sx, int sz, int minX, int minZ, float selCenterX, float selCenterZ, float rx, float rz)
		{
			float cellCenterX = minX + sx + 0.5f;
			float cellCenterZ = minZ + sz + 0.5f;
			float ndx = (cellCenterX - selCenterX) / rx;
			float ndz = (cellCenterZ - selCenterZ) / rz;
			return (ndx * ndx + ndz * ndz <= 1.05f);
		}

		private void CreateCoordinatePreviewMesh()
		{
			if (_coordinatePreviewMesh != null) return;
			_coordinatePreviewMesh = new MeshInstance3D();
			_coordinatePreviewMesh.Name = "CoordinatePreview";
			var mat = new StandardMaterial3D();
			mat.AlbedoColor = new Color(0.1f, 1.0f, 0.3f, 0.4f);
			mat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
			mat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
			mat.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
			_coordinatePreviewMesh.MaterialOverride = mat;
			AddChild(_coordinatePreviewMesh);
			_coordinatePreviewMesh.Visible = false;
		}

		public void UpdateCoordinatePreviewMesh(int minX, int minZ, int maxX, int maxZ)
		{
			CreateCoordinatePreviewMesh();
			RebuildCoordinateMeshInstance(_coordinatePreviewMesh, minX, minZ, maxX, maxZ, new Color(0.1f, 1.0f, 0.3f, 0.4f));
		}

		public void DeleteCoordinateExternal(string coordinateName)
		{
			var oldCoordinates = new List<EditorCoordinate>(GameContext.EditorService.EditorCoordinates);
			GameContext.EditorService.EditorCoordinates.RemoveAll(r => string.Equals(r.Name, coordinateName, StringComparison.OrdinalIgnoreCase));
			HideCoordinateSelectionOutline();
			HideSelectionHighlight();
			HideCoordinatePreviewMesh();
			RebuildAllCoordinatePersistentMeshes();

			var newCoordinates = new List<EditorCoordinate>(GameContext.EditorService.EditorCoordinates);
			var action = new CoordinateAction(oldCoordinates, newCoordinates);
			EditorHistoryManager.RecordAction(action);
			EditorHasUnsavedChanges = true;
		}

		public void RebuildAllCoordinatePersistentMeshes()
		{
			foreach (var mesh in _coordinatePersistentMeshes)
			{
				if (GodotObject.IsInstanceValid(mesh))
				{
					RemoveChild(mesh);
					mesh.QueueFree();
				}
			}

			_coordinatePersistentMeshes.Clear();

			if (ActiveEditorTool != EditorTool.DrawCoordinate)
			{
				HideCoordinatePreviewMesh();
			}

			if (!GameContext.SessionState.IsMapEditorMode || ActiveEditorTool != EditorTool.DrawCoordinate)
			{
				return;
			}

			if (GroundTerrain == null || GroundTerrain.Cells == null) return;

			foreach (var coord in GameContext.EditorService.EditorCoordinates)
			{
				var (minX, minZ, maxX, maxZ) = GetClampedCoordinateBounds(coord);

				var meshInst = new MeshInstance3D();
				meshInst.Name = $"Coordinate_{coord.Name}";
				var mat = new StandardMaterial3D();
				mat.AlbedoColor = new Color(0.1f, 0.9f, 0.3f, 0.25f);
				mat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
				mat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
				mat.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
				meshInst.MaterialOverride = mat;
				AddChild(meshInst);
				RebuildCoordinateMeshInstance(meshInst, minX, minZ, maxX, maxZ, new Color(0.1f, 0.9f, 0.3f, 0.25f));
				_coordinatePersistentMeshes.Add(meshInst);
			}
		}

		public void SelectCoordinateExternal(string coordinateName)
		{
			if (string.IsNullOrEmpty(coordinateName) || GroundTerrain == null || GroundTerrain.Cells == null)
			{
				HideCoordinateSelectionOutline();
				return;
			}

			EditorCoordinate? found = FindCoordinate(coordinateName);
			if (!found.HasValue)
			{
				HideCoordinateSelectionOutline();
				return;
			}

			UpdateCoordinateOutline(found.Value);
			FocusCameraOnCoordinate(found.Value);
		}

		private EditorCoordinate? FindCoordinate(string coordinateName)
		{
			foreach (var r in GameContext.EditorService.EditorCoordinates)
			{
				if (string.Equals(r.Name, coordinateName, StringComparison.OrdinalIgnoreCase)) return r;
			}

			return null;
		}

		public void PerformEraseAreaExternal()
		{
			PerformEraseArea();
			if (GroundTerrain != null && GameContext.EditorService.Interaction.SelectionStart != null && GameContext.EditorService.Interaction.SelectionEnd != null)
			{
				var (minX, minZ, maxX, maxZ) = GameContext.EditorService.Interaction.GetSelectionBounds();
				if (_selectionHighlightMesh != null && _selectionHighlightMesh.Visible)
				{
					RebuildSelectionHighlightMesh(minX, minZ, maxX, maxZ);
				}
			}
		}

		public void PerformCutAreaExternal()
		{
			if (GroundTerrain == null || GameContext.EditorService.Interaction.SelectionStart == null || GameContext.EditorService.Interaction.SelectionEnd == null)
			{
				GameContext.MapEditorHud?.ShowFeedbackExternal("Nothing to Cut (select an area first)");
				return;
			}

			PerformCopyArea();
			PerformEraseArea();

			var (minX, minZ, maxX, maxZ) = GameContext.EditorService.Interaction.GetSelectionBounds();
			if (_selectionHighlightMesh != null && _selectionHighlightMesh.Visible)
			{
				RebuildSelectionHighlightMesh(minX, minZ, maxX, maxZ);
			}

			GameContext.MapEditorHud?.ShowFeedbackExternal("Area Cut");
		}

		public void RefreshSelectionHighlight()
		{
			if (GroundTerrain != null && GameContext.EditorService.Interaction.SelectionStart != null && GameContext.EditorService.Interaction.SelectionEnd != null)
			{
				var (minX, minZ, maxX, maxZ) = GameContext.EditorService.Interaction.GetSelectionBounds();
				if (_selectionHighlightMesh != null && _selectionHighlightMesh.Visible)
				{
					RebuildSelectionHighlightMesh(minX, minZ, maxX, maxZ);
				}
			}
		}

		public void UpdateEditorShadows()
		{
			var sun = GetNodeOrNull<DirectionalLight3D>("DirectionalLight3D");
			if (sun != null && GodotObject.IsInstanceValid(sun))
			{
				GameSettings.ApplyDirectionalLightQuality(sun, GameSettings.QualityIdx);
			}
		}
	}
}
