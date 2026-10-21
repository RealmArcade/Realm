using System;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Terrain;

namespace Realm.Ecs.Services;

public static class ShroudSimulationService
{
	public const float UpdateInterval = 0.1f;

	public static unsafe void Tick(EcsWorld world, Entity worldEntity)
	{
		if (world == null || !world.IsAlive(worldEntity) || !world.Has<ShroudState>(worldEntity)) return;

		ref var shroud = ref world.Get<ShroudState>(worldEntity);

		if (shroud.VisibilityMode == ShroudVisibilityMode.Revealed)
		{
			var revealedGrid = ShroudGridRegistry.Get(shroud.GridHandle);
			if (revealedGrid != null) revealedGrid.AppliedVisibilityMode = ShroudVisibilityMode.Revealed;
			return;
		}

		var grid = ShroudGridRegistry.Get(shroud.GridHandle);
		if (grid == null)
		{
			shroud.GridHandle = ShroudGridRegistry.Create();
			grid = ShroudGridRegistry.Get(shroud.GridHandle);
			if (grid == null) return;
		}

		float worldWidth = 1f;
		float worldDepth = 1f;
		if (world.Has<TerrainState>(worldEntity))
		{
			ref var terrain = ref world.Get<TerrainState>(worldEntity);
			worldWidth = terrain.Width * terrain.QuadSize;
			worldDepth = terrain.Depth * terrain.QuadSize;
		}

		Span<byte> playerGroups = stackalloc byte[ShroudGrid.MaxGroups];
		ComputeGroupAssignment(world, worldEntity, playerGroups);

		bool reallocated = grid.EnsureLayout(worldWidth, worldDepth);
		if (!grid.IsAllocated) return;

		bool groupAssignmentChanged = reallocated;
		if (!groupAssignmentChanged)
		{
			for (int playerIndex = 0; playerIndex < ShroudGrid.MaxGroups; playerIndex++)
			{
				if (grid.GroupOfPlayer[playerIndex] == playerGroups[playerIndex]) continue;
				groupAssignmentChanged = true;
				break;
			}
		}

		if (groupAssignmentChanged)
		{
			for (int playerIndex = 0; playerIndex < ShroudGrid.MaxGroups; playerIndex++)
			{
				grid.GroupOfPlayer[playerIndex] = playerGroups[playerIndex];
			}

			grid.ResetGroupData(shroud.VisibilityMode);
		}
		else if (grid.AppliedVisibilityMode != shroud.VisibilityMode)
		{
			grid.ResetGroupData(shroud.VisibilityMode);
		}

		int currentTick = grid.LastSeenTick + 1;
		grid.LastSeenTick = currentTick;
		bool versionChanged = false;

		var query = QueryCache.AllShroudVisionSourceQuery;
		foreach (var entity in world.Query(in query))
		{
			ref var position = ref world.Get<Position>(entity);
			ref var owner = ref world.Get<UnitOwnerPlayer>(entity);

			int playerIndex = owner.PlayerIndex;
			if ((uint)playerIndex >= (uint)ShroudGrid.MaxGroups) continue;

			float scanRadius = world.Has<ScanRadius>(entity) ? world.Get<ScanRadius>(entity).Value : 0f;
			if (scanRadius <= 0f) continue;

			if (!grid.TryGetCellIndex(position.Value.X, position.Value.Z, out int cellIndex)) continue;

			int groupIndex = grid.GetGroupIndex(playerIndex);
			if (groupIndex < 0) continue;

			int radiusCells = (int)MathF.Ceiling(scanRadius / grid.CellSize);
			if (radiusCells < 1) radiusCells = 1;
			if (radiusCells > ShroudGrid.MaxVisionRadiusCells) radiusCells = ShroudGrid.MaxVisionRadiusCells;

			int entityId = entity.Id;
			if ((uint)entityId >= (uint)ShroudGrid.MaxTrackedEntities) continue;

			ushort sourceMeta = ShroudGrid.PackSourceMeta(groupIndex, radiusCells);
			int previousCellIndex = grid.SourceCells[entityId];
			ushort previousSourceMeta = grid.SourceMeta[entityId];
			grid.SourceLastSeenTick[entityId] = currentTick;

			if (previousCellIndex == cellIndex && previousSourceMeta == sourceMeta) continue;

			if (previousCellIndex >= 0)
			{
				ShroudGrid.RemoveVisionDisc(
					grid.VisibleCells,
					grid.VisibleCoverage,
					grid.CellCount,
					ShroudGrid.GetSourceGroup(previousSourceMeta),
					ShroudGrid.GetCellX(previousCellIndex, grid.GridWidth),
					ShroudGrid.GetCellZ(previousCellIndex, grid.GridWidth),
					ShroudGrid.GetSourceRadius(previousSourceMeta),
					grid.GridWidth,
					grid.GridHeight);
			}

			grid.EnsureGroupCapacity(groupIndex + 1);

			ShroudGrid.AddVisionDisc(
				grid.ExploredCells,
				grid.VisibleCells,
				grid.VisibleCoverage,
				grid.CellCount,
				groupIndex,
				ShroudGrid.GetCellX(cellIndex, grid.GridWidth),
				ShroudGrid.GetCellZ(cellIndex, grid.GridWidth),
				radiusCells,
				grid.GridWidth,
				grid.GridHeight);

			grid.SourceCells[entityId] = cellIndex;
			grid.SourceMeta[entityId] = sourceMeta;
			versionChanged = true;
		}

		for (int entityId = 0; entityId < ShroudGrid.MaxTrackedEntities; entityId++)
		{
			int previousCellIndex = grid.SourceCells[entityId];
			if (previousCellIndex < 0) continue;
			if (grid.SourceLastSeenTick[entityId] == currentTick) continue;

			ushort previousSourceMeta = grid.SourceMeta[entityId];
			ShroudGrid.RemoveVisionDisc(
				grid.VisibleCells,
				grid.VisibleCoverage,
				grid.CellCount,
				ShroudGrid.GetSourceGroup(previousSourceMeta),
				ShroudGrid.GetCellX(previousCellIndex, grid.GridWidth),
				ShroudGrid.GetCellZ(previousCellIndex, grid.GridWidth),
				ShroudGrid.GetSourceRadius(previousSourceMeta),
				grid.GridWidth,
				grid.GridHeight);

			grid.SourceCells[entityId] = -1;
			grid.SourceMeta[entityId] = 0;
			versionChanged = true;
		}

		if (versionChanged) grid.Version++;
	}

	private static int ComputeGroupAssignment(EcsWorld world, Entity worldEntity, Span<byte> playerGroups)
	{
		Span<int> parents = stackalloc int[ShroudGrid.MaxGroups];
		for (int playerIndex = 0; playerIndex < ShroudGrid.MaxGroups; playerIndex++)
		{
			parents[playerIndex] = playerIndex;
		}

		if (world.Has<PlayerRelationshipMatrix>(worldEntity))
		{
			ref var relationships = ref world.Get<PlayerRelationshipMatrix>(worldEntity);
			for (int firstPlayer = 0; firstPlayer < ShroudGrid.MaxGroups; firstPlayer++)
			{
				for (int secondPlayer = firstPlayer + 1; secondPlayer < ShroudGrid.MaxGroups; secondPlayer++)
				{
					if (!relationships.AreAllied(firstPlayer, secondPlayer)) continue;
					UnionGroupParents(parents, firstPlayer, secondPlayer);
				}
			}
		}

		Span<int> groupIdsByRoot = stackalloc int[ShroudGrid.MaxGroups];
		groupIdsByRoot.Fill(-1);
		int groupCount = 0;

		for (int playerIndex = 0; playerIndex < ShroudGrid.MaxGroups; playerIndex++)
		{
			int root = FindGroupParent(parents, playerIndex);
			int groupIndex = groupIdsByRoot[root];
			if (groupIndex < 0)
			{
				groupIndex = groupCount;
				groupIdsByRoot[root] = groupIndex;
				groupCount++;
			}

			playerGroups[playerIndex] = (byte)groupIndex;
		}

		return groupCount;
	}

	private static int FindGroupParent(Span<int> parents, int index)
	{
		while (parents[index] != index)
		{
			parents[index] = parents[parents[index]];
			index = parents[index];
		}

		return index;
	}

	private static void UnionGroupParents(Span<int> parents, int first, int second)
	{
		int firstRoot = FindGroupParent(parents, first);
		int secondRoot = FindGroupParent(parents, second);
		if (firstRoot == secondRoot) return;

		if (firstRoot < secondRoot)
		{
			parents[secondRoot] = firstRoot;
		}
		else
		{
			parents[firstRoot] = secondRoot;
		}
	}
}
