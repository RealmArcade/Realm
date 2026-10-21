using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Realm.Ecs.Components.Terrain;

/// <summary>
/// Holds the per-observation-group shroud cell buffers for one world along with the native memory it owns, the vision-source cache used for incremental updates, and the grid geometry needed to map world positions onto cells.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public unsafe struct ShroudState
{
	public byte* ExploredCells;
	public byte* VisibleCells;
	public byte* VisibleCoverage;
	public ushort* SourceMeta;
	public int* SourceCells;
	public int* SourceLastSeenTick;
	public fixed byte GroupOfPlayer[ShroudGrid.MaxGroups];
	public int GroupCount;
	public int CellCount;
	public int GridWidth;
	public int GridHeight;
	public int Version;
	public int LastSeenTick;
	public float CellSize;
	public float WorldMinX;
	public float WorldMinZ;
	public float WorldWidth;
	public float WorldDepth;
	public ShroudVisibilityMode VisibilityMode;
	public ShroudVisibilityMode AppliedVisibilityMode;

	public bool IsAllocated => ExploredCells != null && VisibleCells != null && VisibleCoverage != null && SourceMeta != null && SourceCells != null && SourceLastSeenTick != null;

	public bool EnsureAllocated(float worldWidth, float worldDepth)
	{
		if (worldWidth <= 0.0001f) worldWidth = 1f;
		if (worldDepth <= 0.0001f) worldDepth = 1f;

		ShroudGrid.ResolveGrid(worldWidth, worldDepth, out float cellSize, out int gridWidth, out int gridHeight);

		bool layoutMatches = IsAllocated
		                     && GridWidth == gridWidth
		                     && GridHeight == gridHeight
		                     && MathF.Abs(CellSize - cellSize) < 0.0001f
		                     && MathF.Abs(WorldWidth - worldWidth) < 0.0001f
		                     && MathF.Abs(WorldDepth - worldDepth) < 0.0001f;
		if (layoutMatches) return false;

		int groupCapacity = GroupCount > 0 ? GroupCount : 1;
		Release();

		GroupCount = groupCapacity;
		GridWidth = gridWidth;
		GridHeight = gridHeight;
		CellCount = gridWidth * gridHeight;
		CellSize = cellSize;
		WorldWidth = worldWidth;
		WorldDepth = worldDepth;
		WorldMinX = worldWidth * -0.5f;
		WorldMinZ = worldDepth * -0.5f;

		int cellBufferLength = groupCapacity * CellCount;
		ExploredCells = (byte*)ShroudGrid.AllocateZeroed((nuint)cellBufferLength);
		VisibleCells = (byte*)ShroudGrid.AllocateZeroed((nuint)cellBufferLength);
		VisibleCoverage = (byte*)ShroudGrid.AllocateZeroed((nuint)cellBufferLength);
		SourceMeta = (ushort*)ShroudGrid.AllocateZeroed((nuint)(ShroudGrid.MaxTrackedEntities * sizeof(ushort)));
		SourceCells = (int*)ShroudGrid.AllocateZeroed((nuint)(ShroudGrid.MaxTrackedEntities * sizeof(int)));
		SourceLastSeenTick = (int*)ShroudGrid.AllocateZeroed((nuint)(ShroudGrid.MaxTrackedEntities * sizeof(int)));

		for (int entityId = 0; entityId < ShroudGrid.MaxTrackedEntities; entityId++)
		{
			SourceCells[entityId] = -1;
		}

		for (int playerIndex = 0; playerIndex < ShroudGrid.MaxGroups; playerIndex++)
		{
			GroupOfPlayer[playerIndex] = (byte)playerIndex;
		}

		Version = 0;
		LastSeenTick = 0;
		AppliedVisibilityMode = VisibilityMode;
		return true;
	}

	public void EnsureGroupCapacity(int requiredGroupCount)
	{
		if (!IsAllocated) return;
		if (requiredGroupCount <= GroupCount) return;
		if (requiredGroupCount > ShroudGrid.MaxGroups) requiredGroupCount = ShroudGrid.MaxGroups;

		int previousBufferLength = GroupCount * CellCount;
		int requiredBufferLength = requiredGroupCount * CellCount;

		byte* expandedExploredCells = (byte*)ShroudGrid.AllocateZeroed((nuint)requiredBufferLength);
		byte* expandedVisibleCells = (byte*)ShroudGrid.AllocateZeroed((nuint)requiredBufferLength);
		byte* expandedVisibleCoverage = (byte*)ShroudGrid.AllocateZeroed((nuint)requiredBufferLength);

		new Span<byte>(ExploredCells, previousBufferLength).CopyTo(new Span<byte>(expandedExploredCells, requiredBufferLength));
		new Span<byte>(VisibleCells, previousBufferLength).CopyTo(new Span<byte>(expandedVisibleCells, requiredBufferLength));
		new Span<byte>(VisibleCoverage, previousBufferLength).CopyTo(new Span<byte>(expandedVisibleCoverage, requiredBufferLength));

		ShroudGrid.Free(ExploredCells);
		ShroudGrid.Free(VisibleCells);
		ShroudGrid.Free(VisibleCoverage);

		if (AppliedVisibilityMode == ShroudVisibilityMode.VisionShroud)
		{
			new Span<byte>(expandedExploredCells + previousBufferLength, requiredBufferLength - previousBufferLength).Fill(1);
		}

		ExploredCells = expandedExploredCells;
		VisibleCells = expandedVisibleCells;
		VisibleCoverage = expandedVisibleCoverage;
		GroupCount = requiredGroupCount;
	}

	public void Release()
	{
		ShroudGrid.Free(ExploredCells);
		ShroudGrid.Free(VisibleCells);
		ShroudGrid.Free(VisibleCoverage);
		ShroudGrid.Free(SourceMeta);
		ShroudGrid.Free(SourceCells);
		ShroudGrid.Free(SourceLastSeenTick);

		ExploredCells = null;
		VisibleCells = null;
		VisibleCoverage = null;
		SourceMeta = null;
		SourceCells = null;
		SourceLastSeenTick = null;

		GroupCount = 0;
		CellCount = 0;
		GridWidth = 0;
		GridHeight = 0;
		CellSize = 0f;
		WorldMinX = 0f;
		WorldMinZ = 0f;
		WorldWidth = 0f;
		WorldDepth = 0f;
		Version = 0;
		LastSeenTick = 0;
	}

	public void ResetGroupData(ShroudVisibilityMode mode)
	{
		if (!IsAllocated) return;

		int cellBufferLength = GroupCount * CellCount;
		new Span<byte>(ExploredCells, cellBufferLength).Clear();
		new Span<byte>(VisibleCells, cellBufferLength).Clear();
		new Span<byte>(VisibleCoverage, cellBufferLength).Clear();
		new Span<ushort>(SourceMeta, ShroudGrid.MaxTrackedEntities).Clear();
		new Span<int>(SourceLastSeenTick, ShroudGrid.MaxTrackedEntities).Clear();
		new Span<int>(SourceCells, ShroudGrid.MaxTrackedEntities).Fill(-1);

		if (mode == ShroudVisibilityMode.VisionShroud)
		{
			new Span<byte>(ExploredCells, cellBufferLength).Fill(1);
		}

		AppliedVisibilityMode = mode;
		LastSeenTick = 0;
		Version++;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public int GetGroupIndex(int playerIndex)
	{
		if ((uint)playerIndex >= ShroudGrid.MaxGroups) return -1;
		return GroupOfPlayer[playerIndex];
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool TryGetCellIndex(float worldX, float worldZ, out int cellIndex)
	{
		if (!IsAllocated)
		{
			cellIndex = -1;
			return false;
		}

		return ShroudGrid.TryGetCellIndex(worldX, worldZ, WorldMinX, WorldMinZ, CellSize, GridWidth, GridHeight, out cellIndex);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool IsExplored(int cellIndex, int groupIndex)
	{
		if (!IsAllocated || (uint)cellIndex >= (uint)CellCount || (uint)groupIndex >= (uint)GroupCount) return false;
		return ExploredCells[groupIndex * CellCount + cellIndex] != 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool IsVisible(int cellIndex, int groupIndex)
	{
		if (!IsAllocated || (uint)cellIndex >= (uint)CellCount || (uint)groupIndex >= (uint)GroupCount) return false;
		return VisibleCells[groupIndex * CellCount + cellIndex] != 0;
	}
}
