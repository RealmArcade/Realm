using Realm.Shared.Maps;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using DotRecast.Detour;
using Godot;
using Godot.Collections;
using Realm.Client.Core;
using Realm.Client.Services;
using Realm.Client.UI;
using Realm.Ecs;
using Realm.Ecs.Components.Terrain;
using Realm.Ecs.Services;
using Realm.Shared.Services;
using Realm.Shared.Textures;
using Array = Godot.Collections.Array;

namespace Realm.Client;
public partial class RuntimeTerrain
{
	public const float TIER_HEIGHT = TerrainCell.TIER_HEIGHT;
	public const float WATER_DELTA = 0.9f;

	public const int PATHING_SHALLOW_WATER = (int)TerrainPathingFlags.ShallowWater;
	public const int PATHING_DEEP_WATER = (int)TerrainPathingFlags.DeepWater;
	public const int PATHING_FLYING = (int)TerrainPathingFlags.Flying;
	public const int PATHING_GROUND = (int)TerrainPathingFlags.Ground;
	public const int PATHING_BUILDABLE = (int)TerrainPathingFlags.Buildable;

	public static int GetDefaultPathingCode(WaterType waterMode) => TerrainPathing.DefaultPathingCodeByWaterType(waterMode);

	public static int GetDefaultPathingCode(TerrainCell cell)
	{
		if (cell.WaterMode != WaterType.None && GameContext.Terrain != null)
		{
			var prof = GameContext.Terrain.GetWaterProfile(cell.WaterProfileIndex);
			if (prof != null) return prof.DefaultPathingCode;
		}

		return GetDefaultPathingCode(cell.WaterMode);
	}

	public static void ReconcileScaledWater(TerrainCell[,] oldCells, int oldWidth, int oldDepth, TerrainCell[,] newCells, int[,] newPathing, int newWidth, int newDepth)
	{
		if (oldCells == null || newCells == null || oldWidth <= 0 || oldDepth <= 0 || newWidth <= 0 || newDepth <= 0) return;

		var waterBodies = FindOldWaterBodies(oldCells, oldWidth, oldDepth);
		ClearNewWaterData(newCells, newWidth, newDepth);

		if (waterBodies.Count == 0)
		{
			ClearNewWaterPathing(newCells, newPathing, newWidth, newDepth);
			return;
		}

		FloodFillNewWaterBodies(waterBodies, newCells, newPathing, newWidth, newDepth, oldWidth, oldDepth);
		ClearUnusedWaterPathing(newCells, newPathing, newWidth, newDepth);
	}

	private static List<(WaterType WaterMode, byte WaterProfileIndex, float WaterHeight, sbyte MacroTier, List<(int x, int z)> OldCoords)> FindOldWaterBodies(TerrainCell[,] oldCells, int oldWidth, int oldDepth)
	{
		var oldVisited = new bool[oldWidth, oldDepth];
		var waterBodies = new List<(WaterType WaterMode, byte WaterProfileIndex, float WaterHeight, sbyte MacroTier, List<(int x, int z)> OldCoords)>();
		int[] dx = { 0, 0, -1, 1 };
		int[] dz = { -1, 1, 0, 0 };

		for (int oz = 0; oz < oldDepth; oz++)
		{
			for (int ox = 0; ox < oldWidth; ox++)
			{
				if (oldVisited[ox, oz] || oldCells[ox, oz].WaterMode == WaterType.None) continue;
				waterBodies.Add(ExtractWaterBody(ox, oz, oldCells, oldVisited, oldWidth, oldDepth, dx, dz));
			}
		}

		return waterBodies;
	}

	private static (WaterType, byte, float, sbyte, List<(int x, int z)>) ExtractWaterBody(int startX, int startZ, TerrainCell[,] oldCells, bool[,] oldVisited, int oldWidth, int oldDepth, int[] dx, int[] dz)
	{
		var startCell = oldCells[startX, startZ];
		var oldCoords = new List<(int x, int z)>();
		var oldQueue = new Queue<(int x, int z)>();

		oldVisited[startX, startZ] = true;
		oldQueue.Enqueue((startX, startZ));
		oldCoords.Add((startX, startZ));

		while (oldQueue.Count > 0)
		{
			var (cx, cz) = oldQueue.Dequeue();
			ProcessNeighborsForWaterBody(cx, cz, startCell, oldCells, oldVisited, oldWidth, oldDepth, dx, dz, oldQueue, oldCoords);
		}

		return (startCell.WaterMode, startCell.WaterProfileIndex, startCell.WaterHeight, startCell.MacroTier, oldCoords);
	}

	private static void ProcessNeighborsForWaterBody(int cx, int cz, TerrainCell startCell, TerrainCell[,] oldCells, bool[,] oldVisited, int oldWidth, int oldDepth, int[] dx, int[] dz, Queue<(int x, int z)> oldQueue, List<(int x, int z)> oldCoords)
	{
		for (int i = 0; i < 4; i++)
		{
			int nx = cx + dx[i];
			int nz = cz + dz[i];

			if (IsInvalidOrVisitedWaterNeighbor(nx, nz, oldWidth, oldDepth, oldVisited)) continue;

			var nCell = oldCells[nx, nz];
			if (!IsSameWaterBody(nCell, startCell)) continue;

			oldVisited[nx, nz] = true;
			oldQueue.Enqueue((nx, nz));
			oldCoords.Add((nx, nz));
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static bool IsInvalidOrVisitedWaterNeighbor(int nx, int nz, int oldWidth, int oldDepth, bool[,] oldVisited)
	{
		return nx < 0 || nx >= oldWidth || nz < 0 || nz >= oldDepth || oldVisited[nx, nz];
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static bool IsSameWaterBody(TerrainCell nCell, TerrainCell startCell)
	{
		return nCell.WaterMode == startCell.WaterMode &&
		       nCell.WaterProfileIndex == startCell.WaterProfileIndex &&
		       MathF.Abs(nCell.WaterHeight - startCell.WaterHeight) < 0.01f &&
		       nCell.MacroTier == startCell.MacroTier;
	}

	private static void ClearNewWaterData(TerrainCell[,] newCells, int newWidth, int newDepth)
	{
		for (int z = 0; z < newDepth; z++)
		{
			for (int x = 0; x < newWidth; x++)
			{
				newCells[x, z].WaterMode = WaterType.None;
				newCells[x, z].WaterProfileIndex = 0;
				newCells[x, z].WaterHeight = 0f;
			}
		}
	}

	private static void ClearNewWaterPathing(TerrainCell[,] newCells, int[,] newPathing, int newWidth, int newDepth)
	{
		if (newPathing == null) return;
		for (int z = 0; z < newDepth; z++)
		{
			for (int x = 0; x < newWidth; x++)
			{
				if ((newPathing[x, z] & (PATHING_SHALLOW_WATER | PATHING_DEEP_WATER)) != 0)
				{
					newPathing[x, z] = GetDefaultPathingCode(newCells[x, z]);
				}
			}
		}
	}

	private static void ClearUnusedWaterPathing(TerrainCell[,] newCells, int[,] newPathing, int newWidth, int newDepth)
	{
		if (newPathing == null) return;
		for (int z = 0; z < newDepth; z++)
		{
			for (int x = 0; x < newWidth; x++)
			{
				if (newCells[x, z].WaterMode == WaterType.None && (newPathing[x, z] & (PATHING_SHALLOW_WATER | PATHING_DEEP_WATER)) != 0)
				{
					newPathing[x, z] = GetDefaultPathingCode(newCells[x, z]);
				}
			}
		}
	}

	private static void FloodFillNewWaterBodies(List<(WaterType WaterMode, byte WaterProfileIndex, float WaterHeight, sbyte MacroTier, List<(int x, int z)> OldCoords)> waterBodies, TerrainCell[,] newCells, int[,] newPathing, int newWidth, int newDepth, int oldWidth, int oldDepth)
	{
		var overallVisited = new bool[newWidth, newDepth];
		float stepCliffThreshold = TerrainCell.TIER_HEIGHT * 0.70f;
		int[] dx = { 0, 0, -1, 1 };
		int[] dz = { -1, 1, 0, 0 };

		foreach (var body in waterBodies)
		{
			float waterSurfaceY = body.MacroTier * TerrainCell.TIER_HEIGHT + (body.WaterHeight > 0.001f ? body.WaterHeight : WATER_DELTA);
			var seedCells = FindSeedCells(body, waterSurfaceY, newCells, newWidth, newDepth, oldWidth, oldDepth);
			FillSingleWaterBody(body, waterSurfaceY, seedCells, newCells, newPathing, overallVisited, newWidth, newDepth, dx, dz, stepCliffThreshold);
		}
	}

	private static HashSet<(int x, int z)> FindSeedCells((WaterType WaterMode, byte WaterProfileIndex, float WaterHeight, sbyte MacroTier, List<(int x, int z)> OldCoords) body, float waterSurfaceY, TerrainCell[,] newCells, int newWidth, int newDepth, int oldWidth, int oldDepth)
	{
		var seedCells = new HashSet<(int x, int z)>();
		(int x, int z) closestSeed = (-1, -1);
		float closestSubmergedCellHeight = float.MaxValue;

		foreach (var (ox, oz) in body.OldCoords)
		{
			int nx = Math.Clamp((int)Math.Round(ox * (float)newWidth / oldWidth), 0, newWidth - 1);
			int nz = Math.Clamp((int)Math.Round(oz * (float)newDepth / oldDepth), 0, newDepth - 1);

			if (waterSurfaceY <= GetCellFloorHeight(newCells[nx, nz])) continue;

			float hDiff = MathF.Abs(newCells[nx, nz].CenterHeight - waterSurfaceY);
			if (hDiff < closestSubmergedCellHeight)
			{
				closestSubmergedCellHeight = hDiff;
				closestSeed = (nx, nz);
			}

			if (newCells[nx, nz].MacroTier == body.MacroTier)
			{
				seedCells.Add((nx, nz));
			}
		}

		if (seedCells.Count == 0 && closestSeed.x >= 0)
		{
			seedCells.Add(closestSeed);
		}

		return seedCells;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static float GetCellFloorHeight(TerrainCell cell) => cell.MacroTier * TerrainCell.TIER_HEIGHT;

	private static void FillSingleWaterBody((WaterType WaterMode, byte WaterProfileIndex, float WaterHeight, sbyte MacroTier, List<(int x, int z)> OldCoords) body, float baseWaterLevel, HashSet<(int x, int z)> seedCells, TerrainCell[,] newCells, int[,] newPathing, bool[,] overallVisited, int newWidth, int newDepth, int[] dx, int[] dz, float stepCliffThreshold)
	{
		var newQueue = new Queue<(int x, int z)>();
		EnqueueSeedCells(seedCells, overallVisited, newQueue);

		while (newQueue.Count > 0)
		{
			var (currX, currZ) = newQueue.Dequeue();
			if (!ApplyWaterToCell(currX, currZ, body, baseWaterLevel, newCells, newPathing)) continue;

			var currCell = newCells[currX, currZ];
			SpreadWaterToNeighbors(currX, currZ, currCell, newCells, overallVisited, newWidth, newDepth, dx, dz, stepCliffThreshold, baseWaterLevel, newQueue);
		}
	}

	private static void EnqueueSeedCells(HashSet<(int x, int z)> seedCells, bool[,] overallVisited, Queue<(int x, int z)> newQueue)
	{
		foreach (var seed in seedCells)
		{
			if (overallVisited[seed.x, seed.z]) continue;

			overallVisited[seed.x, seed.z] = true;
			newQueue.Enqueue(seed);
		}
	}

	private static bool ApplyWaterToCell(int x, int z, (WaterType WaterMode, byte WaterProfileIndex, float WaterHeight, sbyte MacroTier, List<(int x, int z)> OldCoords) body, float baseWaterLevel, TerrainCell[,] newCells, int[,] newPathing)
	{
		float waterHeightAboveTier = baseWaterLevel - GetCellFloorHeight(newCells[x, z]);
		if (waterHeightAboveTier <= 0.001f) return false;

		newCells[x, z].WaterMode = body.WaterMode;
		newCells[x, z].WaterProfileIndex = body.WaterProfileIndex;
		newCells[x, z].WaterHeight = waterHeightAboveTier;

		if (newPathing != null)
		{
			newPathing[x, z] = GetDefaultPathingCode(newCells[x, z]);
		}

		return true;
	}

	private static void SpreadWaterToNeighbors(int currX, int currZ, TerrainCell currCell, TerrainCell[,] newCells, bool[,] overallVisited, int newWidth, int newDepth, int[] dx, int[] dz, float stepCliffThreshold, float baseWaterLevel, Queue<(int x, int z)> newQueue)
	{
		for (int i = 0; i < 4; i++)
		{
			int nextX = currX + dx[i];
			int nextZ = currZ + dz[i];

			if (nextX < 0 || nextX >= newWidth || nextZ < 0 || nextZ >= newDepth || overallVisited[nextX, nextZ]) continue;

			if (ShouldSpreadWater(currCell, newCells[nextX, nextZ], baseWaterLevel, stepCliffThreshold))
			{
				overallVisited[nextX, nextZ] = true;
				newQueue.Enqueue((nextX, nextZ));
			}
		}
	}

	private static bool ShouldSpreadWater(TerrainCell currCell, TerrainCell nextCell, float baseWaterLevel, float stepCliffThreshold)
	{
		float currHeight = currCell.CenterHeight;
		float nextHeight = nextCell.CenterHeight;
		float deltaH = nextHeight - currHeight;

		float nextMaxH = MathF.Max(MathF.Max(nextCell.Y_NW, nextCell.Y_NE), MathF.Max(nextCell.Y_SW, nextCell.Y_SE));
		float nextMinH = MathF.Min(MathF.Min(nextCell.Y_NW, nextCell.Y_NE), MathF.Min(nextCell.Y_SW, nextCell.Y_SE));
		float nextCellInternalSpan = nextMaxH - nextMinH;

		bool isCliffStepUp = (nextCell.MacroTier - currCell.MacroTier >= 1) || deltaH >= stepCliffThreshold || nextCellInternalSpan >= stepCliffThreshold;
		bool isCliffStepDown = (currCell.MacroTier - nextCell.MacroTier >= 1) || (-deltaH) >= stepCliffThreshold;

		if (isCliffStepDown) return false;

		if (isCliffStepUp)
		{
			float obstacleHeight = MathF.Max(nextMaxH, (float)nextCell.MacroTier * TerrainCell.TIER_HEIGHT);
			return baseWaterLevel >= obstacleHeight;
		}

		return true;
	}

}
