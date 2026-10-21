using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Realm.Ecs.Components.Terrain;

/// <summary>
/// Stores per-player exploration and active vision bitmasks for the 128x128 shroud grid.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public unsafe struct ShroudState
{
	public const int Resolution = ShroudGrid.Resolution;
	public const int CellCount = ShroudGrid.CellCount;

	public ushort* ExploredBitmask;
	public ushort* VisibleBitmask;
	public float WorldMinX;
	public float WorldMinZ;
	public float WorldWidth;
	public float WorldDepth;
	public ShroudVisibilityMode VisibilityMode;

	public bool IsAllocated => ExploredBitmask != null && VisibleBitmask != null;

	public void EnsureAllocated(float worldWidth, float worldDepth)
	{
		if (worldWidth <= 0.0001f) worldWidth = 1f;
		if (worldDepth <= 0.0001f) worldDepth = 1f;
		if (IsAllocated && WorldWidth == worldWidth && WorldDepth == worldDepth) return;

		Release();

		ExploredBitmask = ShroudGrid.AllocateCellMask();
		VisibleBitmask = ShroudGrid.AllocateCellMask();
		WorldWidth = worldWidth;
		WorldDepth = worldDepth;
		WorldMinX = worldWidth * -0.5f;
		WorldMinZ = worldDepth * -0.5f;
	}

	public void Release()
	{
		ShroudGrid.FreeCellMask(ExploredBitmask);
		ShroudGrid.FreeCellMask(VisibleBitmask);
		ExploredBitmask = null;
		VisibleBitmask = null;
		WorldMinX = 0f;
		WorldMinZ = 0f;
		WorldWidth = 0f;
		WorldDepth = 0f;
	}

	public void FillAllCells(ushort playerMask)
	{
		if (!IsAllocated) return;
		new Span<ushort>(ExploredBitmask, CellCount).Fill(playerMask);
		new Span<ushort>(VisibleBitmask, CellCount).Fill(playerMask);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool TryGetCellIndex(float worldX, float worldZ, out int cellIndex)
	{
		if (!IsAllocated || WorldWidth <= 0f || WorldDepth <= 0f)
		{
			cellIndex = -1;
			return false;
		}

		return ShroudGrid.TryGetCellIndex(worldX, worldZ, WorldMinX, WorldMinZ, WorldWidth, WorldDepth, out cellIndex);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool IsExplored(int cellIndex, ushort observedMask)
	{
		return IsAllocated && cellIndex >= 0 && cellIndex < CellCount && (ExploredBitmask[cellIndex] & observedMask) != 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool IsVisible(int cellIndex, ushort observedMask)
	{
		return IsAllocated && cellIndex >= 0 && cellIndex < CellCount && (VisibleBitmask[cellIndex] & observedMask) != 0;
	}
}
