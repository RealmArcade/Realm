using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Godot;
using Realm.Client.Core;
using Realm.Ecs;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Terrain;
using Realm.Shared.Textures;

namespace Realm.Client.Services;

/// <summary>
/// The parts of the editor that write the terrain surface in bulk: erasing a selection, flood filling a
/// texture or a pathing layer, and filling a water pool. It owns the two live splat maps it writes and the
/// visited buffer the water pool fill reuses, so those no longer sit among the editor's other state.
/// </summary>
internal sealed class EditorTerrainSurface
{
	public TerrainSplatWeights[,]? SplatMap;
	public TerrainSplatWeights[,]? CliffSplatMap;

	private bool[,]? _waterPoolVisited;

	public void AlignSplatMapSlots(int minX, int minZ, int maxX, int maxZ)
	{
		if (SplatMap != null) EditorSplatAlignment.AlignSplatMapMatrix(SplatMap, minX, minZ, maxX, maxZ);
		if (CliffSplatMap == null && GameContext.Host?.GroundTerrain != null)
		{
			CliffSplatMap = GameContext.Host.GroundTerrain.CliffSplatMap;
		}

		if (CliffSplatMap != null) EditorSplatAlignment.AlignSplatMapMatrix(CliffSplatMap, minX, minZ, maxX, maxZ);
	}

	public void SetGridNodeSplat(int gx, int gz, in TerrainSplatWeights nw, in TerrainSplatWeights ne, in TerrainSplatWeights se, in TerrainSplatWeights sw)
	{
		if (SplatMap == null) return;
		int w = SplatMap.GetLength(0);
		int d = SplatMap.GetLength(1);
		EditorTerrainGeometry.TrySetSplat(SplatMap, gx, gz, w, d, in nw);
		EditorTerrainGeometry.TrySetSplat(SplatMap, gx + 1, gz, w, d, in ne);
		EditorTerrainGeometry.TrySetSplat(SplatMap, gx + 1, gz + 1, w, d, in se);
		EditorTerrainGeometry.TrySetSplat(SplatMap, gx, gz + 1, w, d, in sw);
	}

	public void SetGridNodeCliffSplat(int gx, int gz, in TerrainSplatWeights nw, in TerrainSplatWeights ne, in TerrainSplatWeights se, in TerrainSplatWeights sw)
	{
		if (CliffSplatMap == null) return;
		int w = CliffSplatMap.GetLength(0);
		int d = CliffSplatMap.GetLength(1);
		EditorTerrainGeometry.TrySetSplat(CliffSplatMap, gx, gz, w, d, in nw);
		EditorTerrainGeometry.TrySetSplat(CliffSplatMap, gx + 1, gz, w, d, in ne);
		EditorTerrainGeometry.TrySetSplat(CliffSplatMap, gx + 1, gz + 1, w, d, in se);
		EditorTerrainGeometry.TrySetSplat(CliffSplatMap, gx, gz + 1, w, d, in sw);
	}

	public static void CalculateWorldBounds(int minX, int minZ, int maxX, int maxZ, int width, int depth, float quadSize, out float minWorldX, out float maxWorldX, out float minWorldZ, out float maxWorldZ, out float centerWorldX, out float centerWorldZ, out float rxWorld, out float rzWorld)
	{
		minWorldX = (minX - width / 2.0f) * quadSize - quadSize * 0.5f;
		maxWorldX = (maxX - width / 2.0f) * quadSize + quadSize * 0.5f;
		minWorldZ = (minZ - depth / 2.0f) * quadSize - quadSize * 0.5f;
		maxWorldZ = (maxZ - depth / 2.0f) * quadSize + quadSize * 0.5f;
		centerWorldX = (minWorldX + maxWorldX) * 0.5f;
		centerWorldZ = (minWorldZ + maxWorldZ) * 0.5f;
		rxWorld = Math.Max(quadSize * 0.5f, (maxWorldX - minWorldX) * 0.5f);
		rzWorld = Math.Max(quadSize * 0.5f, (maxWorldZ - minWorldZ) * 0.5f);
	}

	public static void ProcessTerrainErase(ref TerrainState terrain, int minX, int minZ, int maxX, int maxZ, int selWidth, int selDepth, bool isSquare, bool pasteHeights, bool pasteTextures, bool pastePathing, ref bool terrainModified, ref bool pathingModified, TerrainSplatWeights[,]? splatMap, TerrainSplatWeights[,]? cliffSplatMap)
	{
		float selCenterX = (minX + maxX) * 0.5f;
		float selCenterZ = (minZ + maxZ) * 0.5f;
		float rx = Math.Max(0.5f, (maxX - minX) * 0.5f);
		float rz = Math.Max(0.5f, (maxZ - minZ) * 0.5f);

		for (int sz = 0; sz < selDepth; sz++)
		{
			for (int sx = 0; sx < selWidth; sx++)
			{
				if (!IsPointInEllipse(minX + sx + 0.5f, minZ + sz + 0.5f, selCenterX, selCenterZ, rx, rz, isSquare, 1.05f)) continue;
				ProcessSingleEraseCell(ref terrain, minX + sx, minZ + sz, pasteHeights, pasteTextures, pastePathing, ref terrainModified, ref pathingModified, splatMap, cliffSplatMap);
			}
		}

		if (terrainModified && pasteHeights) EditorGridSnapping.SanitizeCornerHeights(ref terrain);
	}

	public static void ProcessSingleEraseCell(ref TerrainState terrain, int targetX, int targetZ, bool pasteHeights, bool pasteTextures, bool pastePathing, ref bool terrainModified, ref bool pathingModified, TerrainSplatWeights[,]? splatMap, TerrainSplatWeights[,]? cliffSplatMap)
	{
		if (targetX < 0 || targetX >= terrain.Width || targetZ < 0 || targetZ >= terrain.Depth) return;

		if (pasteHeights && terrain.Cells != null)
		{
			var targetCells = terrain.Cells;
			EditorTerrainGeometry.SetGridNodeHeight(ref terrain, targetX, targetZ, 0f);
			EditorTerrainGeometry.SetGridNodeHeight(ref terrain, targetX + 1, targetZ, 0f);
			EditorTerrainGeometry.SetGridNodeHeight(ref terrain, targetX + 1, targetZ + 1, 0f);
			EditorTerrainGeometry.SetGridNodeHeight(ref terrain, targetX, targetZ + 1, 0f);
			targetCells[targetX, targetZ] = default;
		}

		if (pasteTextures)
		{
			var defaultGround = TerrainSplatWeights.CreateSolid(0);
			var defaultCliff = TerrainSplatWeights.CreateSolid(1);
			SetGridNodeSplatOnto(splatMap, targetX, targetZ, in defaultGround, in defaultGround, in defaultGround, in defaultGround);
			SetGridNodeSplatOnto(cliffSplatMap, targetX, targetZ, in defaultCliff, in defaultCliff, in defaultCliff, in defaultCliff);
		}

		if (pastePathing && terrain.PathingCodes != null)
		{
			terrain.PathingCodes[targetX, targetZ] = EditableTerrain.PATHING_GROUND | EditableTerrain.PATHING_FLYING;
			pathingModified = true;
		}

		terrainModified = true;
	}

	private static void SetGridNodeSplatOnto(TerrainSplatWeights[,]? map, int gx, int gz, in TerrainSplatWeights nw, in TerrainSplatWeights ne, in TerrainSplatWeights se, in TerrainSplatWeights sw)
	{
		if (map == null) return;
		int w = map.GetLength(0);
		int d = map.GetLength(1);
		EditorTerrainGeometry.TrySetSplat(map, gx, gz, w, d, in nw);
		EditorTerrainGeometry.TrySetSplat(map, gx + 1, gz, w, d, in ne);
		EditorTerrainGeometry.TrySetSplat(map, gx + 1, gz + 1, w, d, in se);
		EditorTerrainGeometry.TrySetSplat(map, gx, gz + 1, w, d, in sw);
	}

	public static void ProcessEntityErase(int minX, int minZ, int maxX, int maxZ, int width, int depth, float quadSize, bool isSquare, ReadOnlySpan<Godot.Vector3> candidatePositions, List<int> erasedCandidateIndices)
	{
		CalculateWorldBounds(minX, minZ, maxX, maxZ, width, depth, quadSize, out float minWorldX, out float maxWorldX, out float minWorldZ, out float maxWorldZ, out float centerWorldX, out float centerWorldZ, out float rxWorld, out float rzWorld);

		for (int index = 0; index < candidatePositions.Length; index++)
		{
			Godot.Vector3 position = candidatePositions[index];
			if (position.X < minWorldX || position.X > maxWorldX || position.Z < minWorldZ || position.Z > maxWorldZ) continue;
			if (!IsPointInEllipse(position.X, position.Z, centerWorldX, centerWorldZ, rxWorld, rzWorld, isSquare, 1.0f)) continue;

			erasedCandidateIndices.Add(index);
		}

		ProcessEcsEntityErase(minWorldX, maxWorldX, minWorldZ, maxWorldZ, centerWorldX, centerWorldZ, rxWorld, rzWorld, isSquare);
	}

	private static void ProcessEcsEntityErase(float minWorldX, float maxWorldX, float minWorldZ, float maxWorldZ, float centerWorldX, float centerWorldZ, float rxWorld, float rzWorld, bool isSquare)
	{
		var world = GameContext.World;
		if (world == null) return;

		var staticPropsToDestroy = new List<(Entity Entity, string PropId)>();
		var propQuery = QueryCache.AllPropIdentityAndPositionQuery;
		world.Query(in propQuery, (Entity entity, ref PropIdentity propIdComp, ref Position posComp) =>
		{
			if (GameHost.EntityToProp3D.ContainsKey(entity)) return;
			if (posComp.Value.X >= minWorldX && posComp.Value.X <= maxWorldX && posComp.Value.Z >= minWorldZ && posComp.Value.Z <= maxWorldZ)
			{
				if (!IsPointInEllipse(posComp.Value.X, posComp.Value.Z, centerWorldX, centerWorldZ, rxWorld, rzWorld, isSquare, 1.0f)) return;
				staticPropsToDestroy.Add((entity, propIdComp.PropId));
			}
		});

		foreach (var sp in staticPropsToDestroy)
		{
			if (world.IsAlive(sp.Entity)) world.Destroy(sp.Entity);
			GameContext.PropMultiMesh?.MarkDirty(sp.PropId);
		}
	}

	public static bool IsPointInEllipse(float x, float z, float centerX, float centerZ, float rx, float rz, bool isSquare, float threshold)
	{
		if (isSquare) return true;
		float ndx = (x - centerX) / rx;
		float ndz = (z - centerZ) / rz;
		return ndx * ndx + ndz * ndz <= threshold;
	}

	private static void ProcessFloodFillNeighbor(int currX, int currZ, int nextX, int nextZ, int splatW, int splatD, bool[,] visited, TerrainSplatWeights[,] splatBefore, int startDominantIndex, bool isCliff, ref TerrainState terrain, Queue<(int x, int z)> queue)
	{
		if (nextX < 0 || nextX >= splatW || nextZ < 0 || nextZ >= splatD) return;
		if (visited[nextX, nextZ]) return;
		if (splatBefore[nextX, nextZ].GetDominantIndex() != startDominantIndex) return;

		if (!isCliff)
		{
			float hCurrent = EditorTerrainGeometry.GetGridNodeHeight(in terrain, currX, currZ);
			float hNext = EditorTerrainGeometry.GetGridNodeHeight(in terrain, nextX, nextZ);
			if (Mathf.Abs(hNext - hCurrent) >= 1.0f) return;
		}

		visited[nextX, nextZ] = true;
		queue.Enqueue((nextX, nextZ));
	}

	private static List<Vector2I> GetFloodFillCells(Godot.Vector3 clickPos, TerrainSplatWeights[,] splatBefore, int width, int depth, float quadSize, bool[,] visited, bool isCliff)
	{
		ref var terrain = ref EditorService.GetTerrainState();
		var cells = new List<Vector2I>();

		int splatW = splatBefore.GetLength(0);
		int splatD = splatBefore.GetLength(1);

		float startFx = clickPos.X / quadSize + width / 2.0f;
		float startFz = clickPos.Z / quadSize + depth / 2.0f;
		int clickX = Mathf.Clamp((int)Math.Round(startFx), 0, splatW - 1);
		int clickZ = Mathf.Clamp((int)Math.Round(startFz), 0, splatD - 1);
		int startDominantIndex = splatBefore[clickX, clickZ].GetDominantIndex();

		var queue = new Queue<(int x, int z)>();
		if (!visited[clickX, clickZ])
		{
			queue.Enqueue((clickX, clickZ));
			visited[clickX, clickZ] = true;
		}

		while (queue.Count > 0)
		{
			var (currX, currZ) = queue.Dequeue();
			cells.Add(new Vector2I(currX, currZ));

			int[] dx = { 0, 0, -1, 1 };
			int[] dz = { -1, 1, 0, 0 };
			for (int i = 0; i < 4; i++)
			{
				ProcessFloodFillNeighbor(currX, currZ, currX + dx[i], currZ + dz[i], splatW, splatD, visited, splatBefore, startDominantIndex, isCliff, ref terrain, queue);
			}
		}

		return cells;
	}

	public static List<Vector2I> GetFloodFillArea(
		Godot.Vector3 clickPos,
		TerrainSplatWeights[,] splatBefore,
		int width,
		int depth,
		float quadSize,
		bool[,] visited,
		MirrorMode mirrorMode,
		bool isCliff,
		Func<int, int, bool> shouldFillCell,
		Vector2 pivot,
		int folds)
	{
		var cells = new List<Vector2I>();
		int splatW = splatBefore.GetLength(0);
		int splatD = splatBefore.GetLength(1);

		float startFx = clickPos.X / quadSize + width / 2.0f;
		float startFz = clickPos.Z / quadSize + depth / 2.0f;
		int startX = Mathf.Clamp((int)Math.Round(startFx), 0, splatW - 1);
		int startZ = Mathf.Clamp((int)Math.Round(startFz), 0, splatD - 1);

		if (shouldFillCell(startX, startZ))
		{
			cells.AddRange(GetFloodFillCells(clickPos, splatBefore, width, depth, quadSize, visited, isCliff));
		}

		if (mirrorMode != MirrorMode.None)
		{
			var mirrors = EditorGridSnapping.GetMirroredPositions(clickPos, mirrorMode, pivot, folds);
			foreach (var m in mirrors)
			{
				float mFx = m.X / quadSize + width / 2.0f;
				float mFz = m.Z / quadSize + depth / 2.0f;
				int mX = Mathf.Clamp((int)Math.Round(mFx), 0, splatW - 1);
				int mZ = Mathf.Clamp((int)Math.Round(mFz), 0, splatD - 1);
				if (shouldFillCell(mX, mZ))
				{
					cells.AddRange(GetFloodFillCells(m, splatBefore, width, depth, quadSize, visited, isCliff));
				}
			}
		}

		return cells;
	}

	private static List<Vector2I> GetFloodFillPathingCells(
		Godot.Vector3 clickPos, TerrainSplatWeights[,]? splatBefore, int width, int depth, float quadSize, bool[,] visited)
	{
		ref var terrain = ref EditorService.GetTerrainState();
		var cells = new List<Vector2I>();

		int clickX = Mathf.Clamp((int)Math.Floor(clickPos.X / quadSize + width / 2.0f), 0, width - 1);
		int clickZ = Mathf.Clamp((int)Math.Floor(clickPos.Z / quadSize + depth / 2.0f), 0, depth - 1);

		int startDominantIndex = GetStartDominantIndex(clickX, clickZ, splatBefore);
		WaterType startWaterType = terrain.Cells != null ? terrain.Cells[clickX, clickZ].WaterMode : WaterType.None;
		float startHeight = terrain.Cells != null ? terrain.Cells[clickX, clickZ].CenterHeight : clickPos.Y;

		var queue = new Queue<(int x, int z)>();
		if (!visited[clickX, clickZ])
		{
			queue.Enqueue((clickX, clickZ));
			visited[clickX, clickZ] = true;
		}

		ProcessFloodFillQueue(queue, cells, width, depth, visited, splatBefore, startDominantIndex, terrain.Cells, startWaterType, startHeight);

		return cells;
	}

	private static int GetStartDominantIndex(int clickX, int clickZ, TerrainSplatWeights[,]? splatBefore)
	{
		if (splatBefore == null) return 0;
		int splatW = splatBefore.GetLength(0);
		int splatD = splatBefore.GetLength(1);
		return (clickX < splatW && clickZ < splatD) ? splatBefore[clickX, clickZ].GetDominantIndex() : 0;
	}

	private static void ProcessFloodFillQueue(Queue<(int x, int z)> queue, List<Vector2I> cells, int width, int depth, bool[,] visited, TerrainSplatWeights[,]? splatBefore, int startDominantIndex, TerrainCell[,]? terrainCells, WaterType startWaterType, float startHeight)
	{
		int[] dx = { 0, 0, -1, 1 };
		int[] dz = { -1, 1, 0, 0 };
		int splatW = splatBefore != null ? splatBefore.GetLength(0) : 0;
		int splatD = splatBefore != null ? splatBefore.GetLength(1) : 0;

		while (queue.Count > 0)
		{
			var (currX, currZ) = queue.Dequeue();
			cells.Add(new Vector2I(currX, currZ));
			ProcessFloodFillNeighbors(currX, currZ, dx, dz, queue, width, depth, visited, splatBefore, splatW, splatD, startDominantIndex, terrainCells, startWaterType, startHeight);
		}
	}

	private static void ProcessFloodFillNeighbors(int currX, int currZ, int[] dx, int[] dz, Queue<(int x, int z)> queue, int width, int depth, bool[,] visited, TerrainSplatWeights[,]? splatBefore, int splatW, int splatD, int startDominantIndex, TerrainCell[,]? terrainCells, WaterType startWaterType, float startHeight)
	{
		for (int i = 0; i < 4; i++)
		{
			int nextX = currX + dx[i];
			int nextZ = currZ + dz[i];
			if (nextX < 0 || nextX >= width || nextZ < 0 || nextZ >= depth || visited[nextX, nextZ]) continue;
			if (!ShouldIncludeNeighbor(nextX, nextZ, splatBefore, splatW, splatD, startDominantIndex, terrainCells, startWaterType, startHeight)) continue;

			visited[nextX, nextZ] = true;
			queue.Enqueue((nextX, nextZ));
		}
	}

	private static bool ShouldIncludeNeighbor(int nextX, int nextZ, TerrainSplatWeights[,]? splatBefore, int splatW, int splatD, int startDominantIndex, TerrainCell[,]? terrainCells, WaterType startWaterType, float startHeight)
	{
		if (splatBefore != null && nextX < splatW && nextZ < splatD)
		{
			if (splatBefore[nextX, nextZ].GetDominantIndex() != startDominantIndex) return false;
		}

		if (terrainCells != null)
		{
			if (startWaterType != terrainCells[nextX, nextZ].WaterMode) return false;
			float hNext = terrainCells[nextX, nextZ].CenterHeight;
			if (Mathf.Abs(hNext - startHeight) >= 3.0f) return false;
		}

		return true;
	}

	public static List<Vector2I> GetFloodFillPathingArea(
		Godot.Vector3 clickPos,
		TerrainSplatWeights[,]? splatBefore,
		int width,
		int depth,
		float quadSize,
		bool[,] visited,
		MirrorMode mirrorMode,
		Func<int, int, bool> shouldFillCell,
		Vector2 pivot,
		int folds)
	{
		var cells = new List<Vector2I>();

		float startFx = clickPos.X / quadSize + width / 2.0f;
		float startFz = clickPos.Z / quadSize + depth / 2.0f;
		int startX = Mathf.Clamp((int)Math.Floor(startFx), 0, width - 1);
		int startZ = Mathf.Clamp((int)Math.Floor(startFz), 0, depth - 1);

		if (shouldFillCell(startX, startZ))
		{
			cells.AddRange(GetFloodFillPathingCells(clickPos, splatBefore, width, depth, quadSize, visited));
		}

		if (mirrorMode != MirrorMode.None)
		{
			var mirrors = EditorGridSnapping.GetMirroredPositions(clickPos, mirrorMode, pivot, folds);
			foreach (var m in mirrors)
			{
				float mFx = m.X / quadSize + width / 2.0f;
				float mFz = m.Z / quadSize + depth / 2.0f;
				int mX = Mathf.Clamp((int)Math.Floor(mFx), 0, width - 1);
				int mZ = Mathf.Clamp((int)Math.Floor(mFz), 0, depth - 1);
				if (shouldFillCell(mX, mZ))
				{
					cells.AddRange(GetFloodFillPathingCells(m, splatBefore, width, depth, quadSize, visited));
				}
			}
		}

		return cells;
	}

	public static bool CheckPathingCondition(int[,] pathingBefore, int x, int z, int pathingMask, bool pathingAdd)
	{
		int current = pathingBefore[x, z] & pathingMask;
		if (pathingAdd) return current != pathingMask;
		return current != 0;
	}

	private static void ProcessWaterFloodFillNeighbor(TerrainCell currCell, TerrainCell nextCell, int nextX, int nextZ, bool isRemoveAction, float stepCliffThreshold, float baseWaterLevel, bool[,] visited, Queue<(int x, int z)> queue)
	{
		if (isRemoveAction)
		{
			if (nextCell.WaterMode != WaterType.None)
			{
				visited[nextX, nextZ] = true;
				queue.Enqueue((nextX, nextZ));
			}

			return;
		}

		float currHeight = currCell.CenterHeight;
		float nextHeight = nextCell.CenterHeight;
		float deltaH = nextHeight - currHeight;

		if (deltaH <= 0.05f)
		{
			visited[nextX, nextZ] = true;
			queue.Enqueue((nextX, nextZ));
			return;
		}

		float nextMaxH = Mathf.Max(Mathf.Max(nextCell.Y_NW, nextCell.Y_NE), Mathf.Max(nextCell.Y_SW, nextCell.Y_SE));
		float nextMinH = Mathf.Min(Mathf.Min(nextCell.Y_NW, nextCell.Y_NE), Mathf.Min(nextCell.Y_SW, nextCell.Y_SE));
		float nextCellInternalSpan = nextMaxH - nextMinH;

		bool isCliffStep = (nextCell.MacroTier - currCell.MacroTier >= 1) || deltaH >= stepCliffThreshold || nextCellInternalSpan >= stepCliffThreshold;
		if (!isCliffStep)
		{
			visited[nextX, nextZ] = true;
			queue.Enqueue((nextX, nextZ));
			return;
		}

		float obstacleHeight = Mathf.Max(nextMaxH, (float)nextCell.MacroTier * TerrainCell.TIER_HEIGHT);
		if (baseWaterLevel >= obstacleHeight)
		{
			visited[nextX, nextZ] = true;
			queue.Enqueue((nextX, nextZ));
		}
	}

	public bool[,] AcquireWaterPoolVisited(int width, int depth)
	{
		if (_waterPoolVisited == null || _waterPoolVisited.GetLength(0) != width || _waterPoolVisited.GetLength(1) != depth)
		{
			_waterPoolVisited = new bool[width, depth];
			return _waterPoolVisited;
		}

		Array.Clear(_waterPoolVisited);
		return _waterPoolVisited;
	}

	public static List<Vector2I> GetWaterFloodFillCells(
		Godot.Vector3 worldPos,
		TerrainCell[,] cellsBefore,
		int width,
		int depth,
		float quadSize,
		bool[,] visited,
		float waterSurfaceY,
		bool isRemoveAction = false)
	{
		int clickX = Mathf.Clamp((int)Math.Floor(worldPos.X / quadSize + width / 2.0f), 0, width - 1);
		int clickZ = Mathf.Clamp((int)Math.Floor(worldPos.Z / quadSize + depth / 2.0f), 0, depth - 1);
		return CollectWaterPoolCells(clickX, clickZ, cellsBefore, width, depth, visited, waterSurfaceY, isRemoveAction, new List<Vector2I>());
	}

	public static List<Vector2I> CollectWaterPoolCells(
		int seedX,
		int seedZ,
		TerrainCell[,] cellsBefore,
		int width,
		int depth,
		bool[,] visited,
		float waterSurfaceY,
		bool isRemoveAction,
		List<Vector2I> resultCells)
	{
		if (seedX < 0 || seedX >= width || seedZ < 0 || seedZ >= depth) return resultCells;
		if (visited[seedX, seedZ]) return resultCells;
		if (isRemoveAction && cellsBefore[seedX, seedZ].WaterMode == WaterType.None) return resultCells;

		var queue = new Queue<(int x, int z)>();
		queue.Enqueue((seedX, seedZ));
		visited[seedX, seedZ] = true;

		float stepCliffThreshold = TerrainCell.TIER_HEIGHT * 0.70f;
		ProcessWaterFloodFillQueue(queue, resultCells, cellsBefore, width, depth, visited, isRemoveAction, stepCliffThreshold, waterSurfaceY);

		return resultCells;
	}

	public static float CalculateBaseWaterLevel(Godot.Vector3 clickPos, TerrainCell startCell, float waterHeight)
	{
		float effectiveWaterHeight = waterHeight > 0.001f ? waterHeight : 0.9f;
		float startTerrainHeight = float.IsNaN(clickPos.Y) || float.IsInfinity(clickPos.Y) ? startCell.CenterHeight : clickPos.Y;
		return startTerrainHeight + effectiveWaterHeight;
	}

	private static void ProcessWaterFloodFillQueue(Queue<(int x, int z)> queue, List<Vector2I> resultCells, TerrainCell[,] cellsBefore, int width, int depth, bool[,] visited, bool isRemoveAction, float stepCliffThreshold, float baseWaterLevel)
	{
		int[] dx = { 0, 0, -1, 1 };
		int[] dz = { -1, 1, 0, 0 };

		while (queue.Count > 0)
		{
			var (currX, currZ) = queue.Dequeue();
			resultCells.Add(new Vector2I(currX, currZ));
			var currCell = cellsBefore[currX, currZ];

			for (int i = 0; i < 4; i++)
			{
				int nextX = currX + dx[i];
				int nextZ = currZ + dz[i];

				if (nextX >= 0 && nextX < width && nextZ >= 0 && nextZ < depth && !visited[nextX, nextZ])
				{
					var nextCell = cellsBefore[nextX, nextZ];
					ProcessWaterFloodFillNeighbor(currCell, nextCell, nextX, nextZ, isRemoveAction, stepCliffThreshold, baseWaterLevel, visited, queue);
				}
			}
		}
	}

	public static List<Vector2I> GetWaterFloodFillArea(
		Godot.Vector3 clickPos,
		TerrainCell[,] cellsBefore,
		int width,
		int depth,
		float quadSize,
		bool[,] visited,
		MirrorMode mirrorMode,
		float waterSurfaceY,
		bool isRemoveAction,
		Vector2 pivot,
		int folds)
	{
		var areaCells = new List<Vector2I>();

		areaCells.AddRange(GetWaterFloodFillCells(clickPos, cellsBefore, width, depth, quadSize, visited, waterSurfaceY, isRemoveAction));

		if (mirrorMode != MirrorMode.None)
		{
			var mirrors = EditorGridSnapping.GetMirroredPositions(clickPos, mirrorMode, pivot, folds);
			foreach (var m in mirrors)
			{
				areaCells.AddRange(GetWaterFloodFillCells(m, cellsBefore, width, depth, quadSize, visited, waterSurfaceY, isRemoveAction));
			}
		}

		return areaCells;
	}

	public static void ApplyWaterPool(
		TerrainCell[,] cells,
		int[,] pathingCodes,
		List<Vector2I> filledCells,
		WaterType? uniformWaterMode,
		byte waterProfileIndex,
		float waterSurfaceY,
		float quadSize,
		int width,
		int depth,
		bool isRemoveAction)
	{
		var waterProfile = isRemoveAction ? null : GameContext.Terrain?.GetWaterProfile(waterProfileIndex);
		var waterSpawnScratch = new List<ProceduralSpawnDescriptor>();

		foreach (var cellPos in filledCells)
		{
			int x = cellPos.X;
			int z = cellPos.Y;

			if (isRemoveAction)
			{
				cells[x, z].WaterMode = WaterType.None;
				cells[x, z].WaterProfileIndex = 0;
				cells[x, z].WaterHeight = 0f;
				pathingCodes[x, z] = EditableTerrain.GetDefaultPathingCode(cells[x, z]);
				GameContext.Host?.ClearProceduralSpawns(x, z, ProceduralSpawnLayer.Water);
				continue;
			}

			WaterType waterMode = uniformWaterMode ?? (cells[x, z].MacroTier <= -2 ? WaterType.Deep : WaterType.Shallow);
			float waterHeight = Math.Max(RuntimeTerrain.WATER_DELTA, waterSurfaceY - cells[x, z].MacroTier * TerrainCell.TIER_HEIGHT);
			cells[x, z].WaterMode = waterMode;
			cells[x, z].WaterProfileIndex = waterProfileIndex;
			cells[x, z].WaterHeight = waterHeight;

			pathingCodes[x, z] = waterProfile != null ? waterProfile.DefaultPathingCode : EditableTerrain.GetDefaultPathingCode(waterMode);

			float waterY = cells[x, z].MacroTier * TerrainCell.TIER_HEIGHT + waterHeight;
			waterSpawnScratch.Clear();
			EditorProceduralSpawns.CollectWater(x, z, waterProfile, waterY, quadSize, width, depth, waterSpawnScratch);
			GameContext.Host?.ApplyProceduralSpawns(x, z, CollectionsMarshal.AsSpan(waterSpawnScratch), ProceduralSpawnLayer.Water);
		}
	}

	public (float[,]? Heights, TerrainSplatWeights[,]? SplatMap, bool IsCliff) PerformTextureFloodFill(
		Godot.Vector3 clickPos,
		int fillTextureIndex,
		int cliffTextureIndex,
		MirrorMode mirrorMode,
		bool isCliff,
		Vector2 symmetryPivot,
		int symmetryFolds)
	{
		ref var terrain = ref EditorService.GetTerrainState();
		if (terrain.Cells == null) return (null, null, isCliff);

		if (SplatMap == null && GameContext.Host?.GroundTerrain != null)
		{
			SplatMap = GameContext.Host.GroundTerrain.SplatMap;
		}

		if (CliffSplatMap == null && GameContext.Host?.GroundTerrain != null)
		{
			CliffSplatMap = GameContext.Host.GroundTerrain.CliffSplatMap;
		}

		if (SplatMap == null) return (null, null, isCliff);

		int width = terrain.Width;
		int depth = terrain.Depth;
		float quadSize = terrain.QuadSize;

		TerrainSplatWeights[,] targetSplatMap = (isCliff && CliffSplatMap != null) ? CliffSplatMap : SplatMap;
		int targetTextureIndex = isCliff ? cliffTextureIndex : fillTextureIndex;

		int splatW = targetSplatMap.GetLength(0);
		int splatD = targetSplatMap.GetLength(1);

		var splatBefore = (TerrainSplatWeights[,])targetSplatMap.Clone();
		var visited = new bool[splatW, splatD];

		var cells = GetFloodFillArea(
			clickPos,
			splatBefore,
			width,
			depth,
			quadSize,
			visited,
			mirrorMode,
			isCliff,
			(x, z) => !splatBefore[x, z].IsSolid(targetTextureIndex),
			symmetryPivot,
			symmetryFolds
		);

		if (cells.Count == 0) return (null, null, isCliff);

		foreach (var cell in cells)
		{
			targetSplatMap[cell.X, cell.Y] = TerrainSplatWeights.CreateSolid(targetTextureIndex);
		}

		int minGridX = splatW;
		int maxGridX = 0;
		int minGridZ = splatD;
		int maxGridZ = 0;
		foreach (var cell in cells)
		{
			if (cell.X < minGridX) minGridX = cell.X;
			if (cell.X > maxGridX) maxGridX = cell.X;
			if (cell.Y < minGridZ) minGridZ = cell.Y;
			if (cell.Y > maxGridZ) maxGridZ = cell.Y;
		}

		AlignSplatMapSlots(minGridX - 2, minGridZ - 2, maxGridX + 2, maxGridZ + 2);

		return (null, (TerrainSplatWeights[,])targetSplatMap.Clone(), isCliff);
	}

	public (int[,]? Before, int[,]? After) PerformPathingFloodFill(Godot.Vector3 clickPos, int pathingMask, bool pathingAdd, MirrorMode mirrorMode, Vector2 symmetryPivot, int symmetryFolds)
	{
		ref var terrain = ref EditorService.GetTerrainState();
		if (terrain.Cells == null || terrain.PathingCodes == null) return (null, null);

		if (SplatMap == null && GameContext.Host?.GroundTerrain != null)
		{
			SplatMap = GameContext.Host.GroundTerrain.SplatMap;
		}

		int width = terrain.Width;
		int depth = terrain.Depth;
		float quadSize = terrain.QuadSize;
		var splatBefore = SplatMap != null ? (TerrainSplatWeights[,])SplatMap.Clone() : null;
		var pathingBefore = (int[,])terrain.PathingCodes.Clone();
		var visited = new bool[width, depth];

		var pathingCodes = terrain.PathingCodes;

		var cells = GetFloodFillPathingArea(
			clickPos,
			splatBefore,
			width,
			depth,
			quadSize,
			visited,
			mirrorMode,
			(x, z) => CheckPathingCondition(pathingBefore, x, z, pathingMask, pathingAdd),
			symmetryPivot,
			symmetryFolds
		);

		if (cells.Count == 0) return (null, null);

		foreach (var cell in cells)
		{
			if (pathingAdd)
				pathingCodes[cell.X, cell.Y] |= pathingMask;
			else
				pathingCodes[cell.X, cell.Y] &= ~pathingMask;
		}

		return (pathingBefore, (int[,])pathingCodes.Clone());
	}
}
