using System.Numerics;
using Realm.Ecs;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Core;
using Realm.Shared.Maps;
using Realm.Shared.Metadata;

namespace Realm.ListenServer;

public class ServerProductionStep
{
	private const float FallbackProductionSeconds = 5f;

	private static readonly QueryDescription TrainingQuery = new QueryDescription().WithAll<ProductionQueue, Position>();
	private static readonly Vector3 DefaultSpawnOffset = new(0f, 0f, 8f);

	private readonly EcsWorld _world;
	private readonly LoadedMap _map;
	private readonly ServerLog _log;
	private readonly EntityCallback<ProductionQueue> _advanceTraining;
	private readonly List<PendingProduction> _completedTraining = new();
	private float _deltaSeconds;

	private struct PendingProduction
	{
		public Entity Building;
		public string UnitId;
	}

	public ServerProductionStep(EcsWorld world, LoadedMap map, ServerLog log)
	{
		_world = world;
		_map = map;
		_log = log;
		_advanceTraining = AdvanceTraining;
	}

	public void Step(float deltaSeconds)
	{
		_deltaSeconds = deltaSeconds;
		_completedTraining.Clear();
		_world.Query(in TrainingQuery, _advanceTraining);

		for (int index = 0; index < _completedTraining.Count; index++)
		{
			PendingProduction pending = _completedTraining[index];
			SpawnTrainedUnit(pending);
		}

		_completedTraining.Clear();
	}

	private void AdvanceTraining(Entity entity, ref ProductionQueue queue)
	{
		if (queue.Count == 0) return;

		queue.CurrentProgress += _deltaSeconds * FastBuildMultiplier;
		if (queue.CurrentProgress < queue.BuildTime) return;

		string? trainedUnitId = queue.RemoveAt(0);
		queue.CurrentProgress = 0f;

		string? nextUnitId = queue.Get(0);
		if (nextUnitId != null)
		{
			queue.BuildTime = ReadProductionSeconds(nextUnitId);
		}

		if (trainedUnitId == null) return;

		_completedTraining.Add(new PendingProduction
		{
			Building = entity,
			UnitId = trainedUnitId
		});
	}

	private float FastBuildMultiplier => _map.Simulation.FastBuildEnabled ? 10f : 1f;

	private float ReadProductionSeconds(string unitId)
	{
		UnitMetadata? metadata = _map.Catalog.FindUnitOrBuilding(unitId);
		if (metadata == null) return FallbackProductionSeconds;

		return metadata.ProductionTime > 0f ? metadata.ProductionTime : FallbackProductionSeconds;
	}

	private void SpawnTrainedUnit(in PendingProduction pending)
	{
		if (!_world.IsAlive(pending.Building)) return;
		if (!_world.TryGet<Position>(pending.Building, out Position buildingPosition)) return;

		Vector3 spawnOffset = _world.TryGet<BuildingSpawnOffset>(pending.Building, out BuildingSpawnOffset offset)
			? offset.Value
			: DefaultSpawnOffset;

		int buildingPlayerSlot = _world.TryGet<UnitOwnerPlayer>(pending.Building, out UnitOwnerPlayer owner) ? owner.Value : 0;
		PlayerEntity playerEntity = _map.GetPlayerEntity(buildingPlayerSlot);

		Entity spawned = UnitEntityFactory.CreateFromMetadata(
			_world,
			_map.Catalog,
			pending.UnitId,
			buildingPosition.Value + spawnOffset,
			0f,
			0f,
			playerEntity,
			false);

		if (spawned == Entity.Null)
		{
			_log.Write($"building {pending.Building.Id} finished training '{pending.UnitId}' but the map declares no such unit");
			return;
		}

		_world.SetOrAdd(spawned, new UnitOwnerPlayer(buildingPlayerSlot));
		_log.Write($"building {pending.Building.Id} trained '{pending.UnitId}' as entity {spawned.Id} for player {buildingPlayerSlot}");
	}
}
