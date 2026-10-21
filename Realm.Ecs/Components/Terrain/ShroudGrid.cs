using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Realm.Ecs.Components.Terrain;

public static class ShroudGrid
{
	public const float TargetCellSize = 2f;
	public const int MaxCellsPerAxis = 256;
	public const int MaxGroups = 25;
	public const int MaxVisionRadiusCells = 96;
	public const int MaxTrackedEntities = EcsWorld.MAX_ENTITIES;
	private const int MaxGroupFieldShift = 8;
	private const int GroupFieldMask = 0xFF;

	public static void ResolveGrid(float worldWidth, float worldDepth, out float cellSize, out int gridWidth, out int gridHeight)
	{
		if (worldWidth <= 0.0001f) worldWidth = 1f;
		if (worldDepth <= 0.0001f) worldDepth = 1f;

		cellSize = TargetCellSize;
		float requiredCellSize = MathF.Max(worldWidth, worldDepth) / MaxCellsPerAxis;
		if (requiredCellSize > cellSize) cellSize = requiredCellSize;

		gridWidth = (int)MathF.Ceiling(worldWidth / cellSize);
		gridHeight = (int)MathF.Ceiling(worldDepth / cellSize);
		if (gridWidth < 1) gridWidth = 1;
		if (gridWidth > MaxCellsPerAxis) gridWidth = MaxCellsPerAxis;
		if (gridHeight < 1) gridHeight = 1;
		if (gridHeight > MaxCellsPerAxis) gridHeight = MaxCellsPerAxis;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool TryGetCellIndex(float worldX, float worldZ, float worldMinX, float worldMinZ, float cellSize, int gridWidth, int gridHeight, out int cellIndex)
	{
		if (cellSize <= 0f || gridWidth <= 0 || gridHeight <= 0)
		{
			cellIndex = -1;
			return false;
		}

		int cellX = (int)MathF.Floor((worldX - worldMinX) / cellSize);
		int cellZ = (int)MathF.Floor((worldZ - worldMinZ) / cellSize);
		if ((uint)cellX >= (uint)gridWidth || (uint)cellZ >= (uint)gridHeight)
		{
			cellIndex = -1;
			return false;
		}

		cellIndex = cellZ * gridWidth + cellX;
		return true;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static int GetCellX(int cellIndex, int gridWidth)
	{
		return gridWidth > 0 ? cellIndex % gridWidth : 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static int GetCellZ(int cellIndex, int gridWidth)
	{
		return gridWidth > 0 ? cellIndex / gridWidth : 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static ushort PackSourceMeta(int groupIndex, int radiusCells)
	{
		return (ushort)((radiusCells & GroupFieldMask) | ((groupIndex & GroupFieldMask) << MaxGroupFieldShift));
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static int GetSourceRadius(ushort sourceMeta)
	{
		return sourceMeta & GroupFieldMask;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static int GetSourceGroup(ushort sourceMeta)
	{
		return (sourceMeta >> MaxGroupFieldShift) & GroupFieldMask;
	}

	public static unsafe bool AddVisionDisc(byte* exploredCells, byte* visibleCells, byte* visibleCounts, int cellCount, int groupIndex, int centerCellX, int centerCellZ, int radiusCells, int gridWidth, int gridHeight)
	{
		if (exploredCells == null || visibleCells == null || visibleCounts == null || cellCount <= 0) return false;
		if (radiusCells < 1) radiusCells = 1;
		if (radiusCells > MaxVisionRadiusCells) radiusCells = MaxVisionRadiusCells;

		int groupOffset = groupIndex * cellCount;
		int radiusSquared = radiusCells * radiusCells;
		int startX = Math.Max(0, centerCellX - radiusCells);
		int endX = Math.Min(gridWidth - 1, centerCellX + radiusCells);
		int startZ = Math.Max(0, centerCellZ - radiusCells);
		int endZ = Math.Min(gridHeight - 1, centerCellZ + radiusCells);
		bool changed = false;

		for (int cellZ = startZ; cellZ <= endZ; cellZ++)
		{
			int offsetZ = cellZ - centerCellZ;
			int rowOffset = offsetZ * offsetZ;
			int rowStart = groupOffset + cellZ * gridWidth;

			for (int cellX = startX; cellX <= endX; cellX++)
			{
				int offsetX = cellX - centerCellX;
				if ((offsetX * offsetX + rowOffset) > radiusSquared) continue;

				int index = rowStart + cellX;
				if (exploredCells[index] == 0)
				{
					exploredCells[index] = 1;
					changed = true;
				}

				byte coverage = visibleCounts[index];
				if (coverage == 0)
				{
					visibleCells[index] = 1;
					changed = true;
				}

				if (coverage < byte.MaxValue) visibleCounts[index] = (byte)(coverage + 1);
			}
		}

		return changed;
	}

	public static unsafe bool RemoveVisionDisc(byte* visibleCells, byte* visibleCounts, int cellCount, int groupIndex, int centerCellX, int centerCellZ, int radiusCells, int gridWidth, int gridHeight)
	{
		if (visibleCells == null || visibleCounts == null || cellCount <= 0) return false;
		if (radiusCells < 1) radiusCells = 1;
		if (radiusCells > MaxVisionRadiusCells) radiusCells = MaxVisionRadiusCells;

		int groupOffset = groupIndex * cellCount;
		int radiusSquared = radiusCells * radiusCells;
		int startX = Math.Max(0, centerCellX - radiusCells);
		int endX = Math.Min(gridWidth - 1, centerCellX + radiusCells);
		int startZ = Math.Max(0, centerCellZ - radiusCells);
		int endZ = Math.Min(gridHeight - 1, centerCellZ + radiusCells);
		bool changed = false;

		for (int cellZ = startZ; cellZ <= endZ; cellZ++)
		{
			int offsetZ = cellZ - centerCellZ;
			int rowOffset = offsetZ * offsetZ;
			int rowStart = groupOffset + cellZ * gridWidth;

			for (int cellX = startX; cellX <= endX; cellX++)
			{
				int offsetX = cellX - centerCellX;
				if ((offsetX * offsetX + rowOffset) > radiusSquared) continue;

				int index = rowStart + cellX;
				byte coverage = visibleCounts[index];
				if (coverage == 0) continue;

				coverage--;
				visibleCounts[index] = coverage;
				if (coverage == 0)
				{
					visibleCells[index] = 0;
					changed = true;
				}
			}
		}

		return changed;
	}

	public static unsafe void* AllocateZeroed(nuint byteCount)
	{
		if (byteCount == 0) return null;
		return NativeMemory.AllocZeroed(byteCount);
	}

	public static unsafe void Free(void* memory)
	{
		if (memory == null) return;
		NativeMemory.Free(memory);
	}
}
