using System;
using System.Collections.Generic;
using Godot;
using Realm.Client.Services;
using Realm.Client.UI;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Terrain;

namespace Realm.Client.Core
{
	public partial class GameHost
	{
		private void PerformCopyArea()
		{
			if (GroundTerrain == null || GameContext.EditorService.SelectionStart == null || GameContext.EditorService.SelectionEnd == null) return;

			var (minX, minZ, maxX, maxZ) = GameContext.EditorService.GetCurrentSelectionBounds();

			var node3Ds = new List<Node3D>();
			foreach (var child in GetChildren())
			{
				if (child is Node3D n3d) node3Ds.Add(n3d);
			}

			var entities = GameContext.EditorService.BuildCopiedEntityList(minX, minZ, maxX, maxZ, node3Ds, EditorBrushIsSquare);
			GameContext.EditorService.CopyArea(minX, minZ, maxX, maxZ, entities, EditorBrushIsSquare);

			int selWidth = maxX - minX + 1;
			int selDepth = maxZ - minZ + 1;
			MapEditorHUD.Instance?.ShowFeedbackExternal($"Copied Area: {selWidth}x{selDepth} tiles, {entities.Count} entities");
		}

		private bool IsCopiedQuadMasked(int sx, int sz)
		{
			float r = GameContext.EditorService.EditorPasteRotation % 360.0f;
			if (r < 0) r += 360.0f;
			int rotSteps = (int)Math.Round(r / 90.0f) % 4;

			int pasteWidth = GameContext.EditorService.CopiedAreaWidth;
			int pasteDepth = GameContext.EditorService.CopiedAreaDepth;

			int origSx = sx;
			int origSz = sz;

			if (rotSteps == 1)
			{
				origSx = sz;
				origSz = pasteDepth - 1 - sx;
			}
			else if (rotSteps == 2)
			{
				origSx = pasteWidth - 1 - sx;
				origSz = pasteDepth - 1 - sz;
			}
			else if (rotSteps == 3)
			{
				origSx = pasteWidth - 1 - sz;
				origSz = sx;
			}

			int srcX = origSx;
			int srcZ = origSz;

			if (GameContext.EditorService.EditorPasteReflection == PasteReflection.Horizontal) srcX = pasteWidth - 1 - origSx;
			else if (GameContext.EditorService.EditorPasteReflection == PasteReflection.Vertical) srcZ = pasteDepth - 1 - origSz;

			return GameContext.EditorService.IsCopiedCellMasked(srcX, srcZ);
		}

		public void PerformCopyAreaExternal()
		{
			if (GroundTerrain == null || GameContext.EditorService.SelectionStart == null || GameContext.EditorService.SelectionEnd == null)
			{
				MapEditorHUD.Instance?.ShowFeedbackExternal("Nothing to Copy (select an area first)");
				return;
			}

			PerformCopyArea();
			MapEditorHUD.Instance?.ShowFeedbackExternal("Area Copied");
		}

		private List<IEditorAction> PerformPasteArea(int startX, int startZ, float rotationDegrees, PasteReflection reflection = PasteReflection.None, bool recordToHistory = true)
		{
			if (GroundTerrain?.Cells == null || GroundTerrain.SplatMap == null || !GameContext.EditorService.HasCopiedArea) return new List<IEditorAction>();

			var cellsBefore = (TerrainCell[,])GroundTerrain.Cells.Clone();
			var splatBefore = (TerrainSplatWeights[,])GroundTerrain.SplatMap.Clone();
			var cliffBefore = GroundTerrain.CliffSplatMap != null ? (TerrainSplatWeights[,])GroundTerrain.CliffSplatMap.Clone() : null;
			var pathingBefore = (int[,])GroundTerrain.PathingCodes.Clone();

			var pasteResult = GameContext.EditorService.BuildPasteAreaResult(
				startX, startZ, GameContext.EditorService.PasteOptionHeights, GameContext.EditorService.PasteOptionTextures, GameContext.EditorService.PasteOptionEntities, PasteOptionPathing,
				EditorMirrorMode, rotationDegrees, reflection);

			ApplyPasteTerrainModifications(startX, startZ, pasteResult);
			var spawnActions = SpawnPastedEntities(pasteResult.SpawnRequests);

			return FinalizePasteActions(pasteResult.TerrainModified, cellsBefore, splatBefore, cliffBefore, pathingBefore, spawnActions, recordToHistory);
		}

		private void ApplyPasteTerrainModifications(int startX, int startZ, PasteAreaResult pasteResult)
		{
			if (!pasteResult.TerrainModified) return;

			int pasteW = Math.Max(GameContext.EditorService.CopiedAreaWidth, GameContext.EditorService.CopiedAreaDepth);
			var affectedRegions = GetAffectedRegions(startX, startZ, pasteW, pasteW);

			if (pasteResult.HeightsModified)
			{
				foreach (var aff in affectedRegions) AlignAllEntitiesToTerrain(aff);
			}

			GroundTerrain.UpdateMeshAndPhysics(pasteResult.HeightsModified, false, affectedRegions, pasteResult.HeightsModified);

			if (pasteResult.PathingModified) UpdatePathingOverlay();
		}

		private List<Rect2I> GetAffectedRegions(int startX, int startZ, int pasteW, int pasteD)
		{
			var affectedRegions = new List<Rect2I> { new Rect2I(startX - 2, startZ - 2, pasteW + 4, pasteD + 4) };
			if (EditorMirrorMode == MirrorMode.None) return affectedRegions;

			float quadSize = GroundTerrain.QuadSize;
			Vector3 centerPos = new Vector3((startX + pasteW / 2.0f - GroundTerrain.Width / 2.0f) * quadSize, 0, (startZ + pasteD / 2.0f - GroundTerrain.Depth / 2.0f) * quadSize);
			var transforms = GameContext.EditorService.GetMirroredTransforms(centerPos, 0.0f, EditorMirrorMode);

			foreach (var t in transforms)
			{
				var (rcx, rcz) = GameContext.EditorService.WorldPosToCellCoords(t.Position);
				affectedRegions.Add(new Rect2I(rcx - pasteW / 2 - 2, rcz - pasteD / 2 - 2, pasteW + 4, pasteD + 4));
			}

			return affectedRegions;
		}

		private List<IEditorAction> SpawnPastedEntities(List<EntitySpawnRequest> spawnRequests)
		{
			var spawnActions = new List<IEditorAction>();
			foreach (var req in spawnRequests)
			{
				var pastedNode = SpawnPastedNode(req);
				if (pastedNode != null)
				{
					spawnActions.Add(new ObjectSpawnAction(req.Type, req.Id, req.Position, req.Rotation, req.Scale, pastedNode, req.Player));
				}
			}

			return spawnActions;
		}

		private Node SpawnPastedNode(EntitySpawnRequest req)
		{
			if (req.Type == "unit") return SpawnUnitExternal(req.Id, req.Position, req.Rotation, req.Scale, req.Player);
			if (req.Type == "prop") return SpawnPropExternalWithParams(req.Id, req.Position, req.Rotation, req.Scale);
			if (req.Type == "decal") return SpawnDecalExternalWithParams(req.Id, req.Position, req.Rotation, req.Scale);
			return null;
		}

		private List<IEditorAction> FinalizePasteActions(bool terrainModified, TerrainCell[,] cellsBefore, TerrainSplatWeights[,] splatBefore, TerrainSplatWeights[,] cliffBefore, int[,] pathingBefore, List<IEditorAction> spawnActions, bool recordToHistory)
		{
			var actions = new List<IEditorAction>();
			if (terrainModified)
			{
				var cellsAfter = (TerrainCell[,])GroundTerrain.Cells.Clone();
				var splatAfter = (TerrainSplatWeights[,])GroundTerrain.SplatMap.Clone();
				var cliffAfter = GroundTerrain.CliffSplatMap != null ? (TerrainSplatWeights[,])GroundTerrain.CliffSplatMap.Clone() : null;
				var pathingAfter = (int[,])GroundTerrain.PathingCodes.Clone();
				actions.Add(new TerrainModifyAction(cellsBefore, cellsAfter, splatBefore, splatAfter, pathingBefore, pathingAfter, cliffBefore, cliffAfter));
			}

			if (spawnActions.Count > 0) actions.AddRange(spawnActions);

			if (actions.Count > 0 && recordToHistory)
			{
				EditorHistoryManager.RecordAction(new CompositeAction(actions));
				EditorHasUnsavedChanges = true;
				MapEditorHUD.Instance?.ShowFeedbackExternal("Pasted Area");
			}

			return actions;
		}
	}
}