using System;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Terrain;

namespace Realm.Ecs.Services;

public static class ShroudSimulationService
{
	public const int MaxBitmaskPlayers = 16;

	public static unsafe void Tick(EcsWorld world, Entity worldEntity)
	{
		if (world == null || !world.Has<ShroudState>(worldEntity)) return;

		ref var shroud = ref world.Get<ShroudState>(worldEntity);
		if (!shroud.IsAllocated) return;

		if (shroud.VisibilityMode == ShroudVisibilityMode.Revealed)
		{
			shroud.FillAllCells(ShroudGrid.AllPlayerMask);
			return;
		}

		new Span<ushort>(shroud.VisibleBitmask, ShroudGrid.CellCount).Clear();

		var query = QueryCache.AllShroudVisionSourceQuery;
		foreach (var entity in world.Query(in query))
		{
			ref var position = ref world.Get<Position>(entity);
			ref var owner = ref world.Get<UnitOwnerPlayer>(entity);

			int playerIndex = owner.PlayerIndex;
			if ((uint)playerIndex >= MaxBitmaskPlayers) continue;
			if (!shroud.TryGetCellIndex(position.Value.X, position.Value.Z, out int cellIndex)) continue;

			float scanRadius = world.Has<ScanRadius>(entity) ? world.Get<ScanRadius>(entity).Value : 0f;
			if (scanRadius <= 0f) continue;

			int radiusCells = (int)MathF.Ceiling(scanRadius * (ShroudGrid.Resolution / shroud.WorldWidth));
			if (radiusCells <= 0) radiusCells = 1;

			ushort playerBit = (ushort)(1 << playerIndex);
			ShroudGrid.RasterizeVision(
				shroud.VisibleBitmask,
				shroud.ExploredBitmask,
				playerBit,
				ShroudGrid.GetCellX(cellIndex),
				ShroudGrid.GetCellZ(cellIndex),
				radiusCells);
		}
	}
}
