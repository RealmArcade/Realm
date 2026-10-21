using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Realm.Ecs.Components.Terrain;

public static class ShroudGrid
{
	public const int Resolution = 128;
	public const int CellCount = Resolution * Resolution;
	public const int MaxVisionRadiusCells = 96;
	public const int PrewarmedVisionRadiusCells = 24;
	public const int ResolutionShift = 7;
	public const int ResolutionMask = Resolution - 1;
	public const ushort AllPlayerMask = 0xFFFF;

	private static readonly int[][] _visionOffsets = new int[MaxVisionRadiusCells + 1][];
	private static float _worldWidth = 0f;
	private static float _worldDepth = 0f;

	public static void ConfigureWorld(float worldWidth, float worldDepth)
	{
		if (worldWidth <= 0.0001f) worldWidth = 1f;
		if (worldDepth <= 0.0001f) worldDepth = 1f;
		if (MathF.Abs(_worldWidth - worldWidth) < 0.0001f && MathF.Abs(_worldDepth - worldDepth) < 0.0001f) return;

		_worldWidth = worldWidth;
		_worldDepth = worldDepth;
		Array.Clear(_visionOffsets);

		for (int radiusCells = 0; radiusCells <= PrewarmedVisionRadiusCells; radiusCells++)
		{
			_visionOffsets[radiusCells] = BuildVisionOffsets(radiusCells);
		}
	}

	public static ReadOnlySpan<int> GetVisionOffsets(int radiusCells)
	{
		if (radiusCells < 0) radiusCells = 0;
		if (radiusCells > MaxVisionRadiusCells) radiusCells = MaxVisionRadiusCells;

		int[] offsets = _visionOffsets[radiusCells];
		if (offsets == null)
		{
			offsets = BuildVisionOffsets(radiusCells);
			_visionOffsets[radiusCells] = offsets;
		}

		return offsets;
	}

	private static int[] BuildVisionOffsets(int radiusCells)
	{
		if (radiusCells == 0)
		{
			return new[] { 0, 0 };
		}

		float aspectDepth = _worldWidth > 0f ? _worldWidth / _worldDepth : 1f;
		int radiusCellsDepth = Math.Max(1, (int)MathF.Round(radiusCells * aspectDepth));
		float radiusSquared = radiusCells * radiusCells;
		var offsets = new List<int>(radiusCells * radiusCells * 3 + 1);

		for (int offsetX = -radiusCells; offsetX <= radiusCells; offsetX++)
		{
			float remaining = radiusSquared - (offsetX * offsetX);
			if (remaining < 0f) continue;

			int halfDepth = (int)MathF.Round(MathF.Sqrt(remaining) * radiusCellsDepth / radiusCells);
			for (int offsetZ = -halfDepth; offsetZ <= halfDepth; offsetZ++)
			{
				offsets.Add(offsetX);
				offsets.Add(offsetZ);
			}
		}

		return offsets.ToArray();
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool TryGetCellIndex(float worldX, float worldZ, float worldMinX, float worldMinZ, float worldWidth, float worldDepth, out int cellIndex)
	{
		int cellX = (int)MathF.Floor((worldX - worldMinX) * (Resolution / worldWidth));
		int cellZ = (int)MathF.Floor((worldZ - worldMinZ) * (Resolution / worldDepth));

		if ((uint)cellX > ResolutionMask || (uint)cellZ > ResolutionMask)
		{
			cellIndex = -1;
			return false;
		}

		cellIndex = cellZ * Resolution + cellX;
		return true;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static int GetCellX(int cellIndex) => cellIndex & ResolutionMask;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static int GetCellZ(int cellIndex) => cellIndex >> ResolutionShift;

	public static unsafe void RasterizeVision(ushort* visibleBitmask, ushort* exploredBitmask, ushort playerBit, int centerCellX, int centerCellZ, int radiusCells)
	{
		ReadOnlySpan<int> offsets = GetVisionOffsets(radiusCells);

		for (int index = 0; index < offsets.Length; index += 2)
		{
			int cellX = centerCellX + offsets[index];
			int cellZ = centerCellZ + offsets[index + 1];

			if ((uint)cellX > ResolutionMask || (uint)cellZ > ResolutionMask) continue;

			int cellIndex = cellZ * Resolution + cellX;
			visibleBitmask[cellIndex] |= playerBit;
			exploredBitmask[cellIndex] |= playerBit;
		}
	}

	public static unsafe ushort* AllocateCellMask()
	{
		return (ushort*)NativeMemory.AllocZeroed((nuint)(CellCount * sizeof(ushort)));
	}

	public static unsafe void FreeCellMask(ushort* bitmask)
	{
		if (bitmask == null) return;
		NativeMemory.Free(bitmask);
	}
}
