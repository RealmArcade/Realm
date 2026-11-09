using System;
using System.Collections.Generic;
using System.Numerics;
using Realm.Client.Core;
using Realm.Ecs;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Resources;
using Realm.Ecs.Services;

namespace Realm.Client.Services;

/// <summary>
///     A building's production queue: the pop cost of a queued slot, the round-robin choice of which
///     building takes the next unit, enqueueing with the population check, cancelling one slot and
///     resetting the progress of the unit that moves to the front.
/// </summary>
public sealed class ProductionQueueCommands
{
	private static EcsWorld World => GameContext.World
		?? throw new InvalidOperationException("ProductionQueueCommands used before a world existed.");

	private static int _productionRoundRobinIndex;

	public int GetPopCostForIndex(Entity buildingEntity, int index)
	{
		var prod = World.Get<ProductionQueue>(buildingEntity);
		string? peekCancelledId = prod.Get(index);
		if (peekCancelledId != null && GameHost.UnitRegistry.TryGetValue(peekCancelledId, out var metaPeek))
		{
			return metaPeek.PopCost;
		}

		return 0;
	}

	public void UpdateProductionQueueAfterCancel(Entity buildingEntity, int index, string? nextUnitId, bool isMultiplayerClient)
	{
		if (index == 0 && nextUnitId != null && !isMultiplayerClient)
		{
			var nextMeta = GameHost.UnitRegistry[nextUnitId];
			if (World.Has<ProductionQueue>(buildingEntity))
			{
				var p = World.Get<ProductionQueue>(buildingEntity);
				p.BuildTime = nextMeta.ProductionTime;
				World.Set(buildingEntity, p);
			}
		}
	}

	public Entity GetNextProductionStructure(List<Entity> candidateBuildings)
	{
		if (candidateBuildings == null || candidateBuildings.Count == 0) return Entity.Null;

		int count = candidateBuildings.Count;
		int startIndex = Math.Abs(_productionRoundRobinIndex) % count;

		for (int i = 0; i < count; i++)
		{
			int currIdx = (startIndex + i) % count;
			var bldEntity = candidateBuildings[currIdx];
			if (!World.IsAlive(bldEntity)) continue;

			int queueCount = 0;
			if (World.Has<ProductionQueue>(bldEntity))
			{
				queueCount = World.Get<ProductionQueue>(bldEntity).Count;
			}

			if (queueCount < 5)
			{
				_productionRoundRobinIndex = (currIdx + 1) % count;
				return bldEntity;
			}
		}

		return Entity.Null;
	}

	public bool TryQueueUnit(Entity playerEntity, Entity buildingEntity, string unitId, int popCost, float productionTime)
	{
		if (!World.IsAlive(playerEntity) || !World.IsAlive(buildingEntity)) return false;

		if (World.Has<PlayerPopulation>(playerEntity))
		{
			ref var pop = ref World.Get<PlayerPopulation>(playerEntity);
			if (popCost > 0 && pop.Current + popCost > pop.Max)
			{
				return false;
			}
		}

		ref var prod = ref World.Has<ProductionQueue>(buildingEntity)
			? ref World.Get<ProductionQueue>(buildingEntity)
			: ref CreateProductionQueue(buildingEntity);

		if (prod.Count >= 5) return false;

		if (prod.Count == 0)
		{
			prod.BuildTime = productionTime;
		}

		if (!prod.Enqueue(unitId)) return false;
		World.Set(buildingEntity, prod);

		return true;
	}

	public ref ProductionQueue CreateProductionQueue(Entity buildingEntity)
	{
		World.Add(buildingEntity, new ProductionQueue());
		return ref World.Get<ProductionQueue>(buildingEntity);
	}

	public bool CancelQueuedUnitAt(Entity buildingEntity, int index, out string? cancelledUnitId, out string? nextUnitId)
	{
		cancelledUnitId = null;
		nextUnitId = null;

		if (!World.IsAlive(buildingEntity) || !World.Has<ProductionQueue>(buildingEntity))
			return false;

		var prod = World.Get<ProductionQueue>(buildingEntity);
		if (index < 0 || index >= prod.Count)
			return false;

		cancelledUnitId = prod.RemoveAt(index);

		if (index == 0)
		{
			prod.CurrentProgress = 0f;
			nextUnitId = prod.Get(0);
		}



		World.Set(buildingEntity, prod);
		return true;
	}
}
