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

		float worldWidth = 1f;
		float worldDepth = 1f;
		if (world.Has<TerrainState>(worldEntity))
		{
			ref var terrain = ref world.Get<TerrainState>(worldEntity);
			worldWidth = terrain.Width * terrain.QuadSize;
			worldDepth = terrain.Depth * terrain.QuadSize;
		}

		ref var shroud = ref world.Get<ShroudState>(worldEntity);
		if (shroud.VisibilityMode == ShroudVisibilityMode.Revealed)
		{
			shroud.AppliedVisibilityMode = ShroudVisibilityMode.Revealed;
			return;
		}

		Span<byte> playerGroups = stackalloc byte[ShroudGrid.MaxGroups];
		ComputeGroupAssignment(world, worldEntity, playerGroups);

		bool reallocated = shroud.EnsureAllocated(worldWidth, worldDepth);
		if (!shroud.IsAllocated) return;

		bool groupAssignmentChanged = reallocated;
		if (!groupAssignmentChanged)
		{
			for (int playerIndex = 0; playerIndex < ShroudGrid.MaxGroups; playerIndex++)
			{
				if (shroud.GroupOfPlayer[playerIndex] == playerGroups[playerIndex]) continue;
				groupAssignmentChanged = true;
				break;
			}
		}

		if (groupAssignmentChanged)
		{
			for (int playerIndex = 0; playerIndex < ShroudGrid.MaxGroups; playerIndex++)
			{
				shroud.GroupOfPlayer[playerIndex] = playerGroups[playerIndex];
			}

			shroud.ResetGroupData(shroud.VisibilityMode);
		}
		else if (shroud.AppliedVisibilityMode != shroud.VisibilityMode)
		{
			shroud.ResetGroupData(shroud.VisibilityMode);
		}

		int currentTick = shroud.LastSeenTick + 1;
		shroud.LastSeenTick = currentTick;
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

			if (!shroud.TryGetCellIndex(position.Value.X, position.Value.Z, out int cellIndex)) continue;

			int groupIndex = shroud.GetGroupIndex(playerIndex);
			if (groupIndex < 0) continue;

			int radiusCells = (int)MathF.Ceiling(scanRadius / shroud.CellSize);
			if (radiusCells < 1) radiusCells = 1;
			if (radiusCells > ShroudGrid.MaxVisionRadiusCells) radiusCells = ShroudGrid.MaxVisionRadiusCells;

			int entityId = entity.Id;
			if ((uint)entityId >= (uint)ShroudGrid.MaxTrackedEntities) continue;

			ushort sourceMeta = ShroudGrid.PackSourceMeta(groupIndex, radiusCells);
			int previousCellIndex = shroud.SourceCells[entityId];
			ushort previousSourceMeta = shroud.SourceMeta[entityId];
			shroud.SourceLastSeenTick[entityId] = currentTick;

			if (previousCellIndex == cellIndex && previousSourceMeta == sourceMeta) continue;

			if (previousCellIndex >= 0)
			{
				ShroudGrid.RemoveVisionDisc(
					shroud.VisibleCells,
					shroud.VisibleCoverage,
					shroud.CellCount,
					ShroudGrid.GetSourceGroup(previousSourceMeta),
					ShroudGrid.GetCellX(previousCellIndex, shroud.GridWidth),
					ShroudGrid.GetCellZ(previousCellIndex, shroud.GridWidth),
					ShroudGrid.GetSourceRadius(previousSourceMeta),
					shroud.GridWidth,
					shroud.GridHeight);
			}

			shroud.EnsureGroupCapacity(groupIndex + 1);

			ShroudGrid.AddVisionDisc(
				shroud.ExploredCells,
				shroud.VisibleCells,
				shroud.VisibleCoverage,
				shroud.CellCount,
				groupIndex,
				ShroudGrid.GetCellX(cellIndex, shroud.GridWidth),
				ShroudGrid.GetCellZ(cellIndex, shroud.GridWidth),
				radiusCells,
				shroud.GridWidth,
				shroud.GridHeight);

			shroud.SourceCells[entityId] = cellIndex;
			shroud.SourceMeta[entityId] = sourceMeta;
			versionChanged = true;
		}

		for (int entityId = 0; entityId < ShroudGrid.MaxTrackedEntities; entityId++)
		{
			int previousCellIndex = shroud.SourceCells[entityId];
			if (previousCellIndex < 0) continue;
			if (shroud.SourceLastSeenTick[entityId] == currentTick) continue;

			ushort previousSourceMeta = shroud.SourceMeta[entityId];
			ShroudGrid.RemoveVisionDisc(
				shroud.VisibleCells,
				shroud.VisibleCoverage,
				shroud.CellCount,
				ShroudGrid.GetSourceGroup(previousSourceMeta),
				ShroudGrid.GetCellX(previousCellIndex, shroud.GridWidth),
				ShroudGrid.GetCellZ(previousCellIndex, shroud.GridWidth),
				ShroudGrid.GetSourceRadius(previousSourceMeta),
				shroud.GridWidth,
				shroud.GridHeight);

			shroud.SourceCells[entityId] = -1;
			shroud.SourceMeta[entityId] = 0;
			versionChanged = true;
		}

		if (versionChanged) shroud.Version++;
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
