using System;
using System.Collections.Generic;
using System.Numerics;
using Realm.Ecs;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Resources;
using Realm.Ecs.Components.Tags;
using Realm.Ecs.Services;
using Realm.Shared.Maps;
using Realm.Shared.Metadata;

namespace Realm.Shared.Simulation;

/// <summary>
///     Advances the mending a unit is doing on a building. Repair is a share of the building's maximum health per second,
///     so a hero spends the same time raising a building from zero as it does mending one that lost that much, and the
///     work only advances while the unit stands within reach of what it is mending.
/// </summary>
public class BuildingRepairTick
{
	private static readonly QueryDescription RepairQuery = new QueryDescription().WithAll<Position, RepairTask>().WithNone<Dead>();

	private readonly SimulationContext _simulation;
	private readonly DefinitionCatalog _catalog;
	private readonly EntityCallback<Position, RepairTask> _advanceRepairer;
	private readonly List<Entity> _finishedRepairers = new();
	private float _deltaSeconds;

	public BuildingRepairTick(SimulationContext simulation, DefinitionCatalog catalog)
	{
		_simulation = simulation;
		_catalog = catalog;
		_advanceRepairer = AdvanceRepairer;
	}

	/// <summary>Called with the building each time one is restored to full health by a repairing unit.</summary>
	public event Action<Entity>? Repaired;

	public void Step(float deltaSeconds)
	{
		_deltaSeconds = deltaSeconds;
		_finishedRepairers.Clear();

		_simulation.World.Query(in RepairQuery, _advanceRepairer);

		for (int index = 0; index < _finishedRepairers.Count; index++)
		{
			Entity repairer = _finishedRepairers[index];
			if (_simulation.World.IsAlive(repairer) && _simulation.World.Has<RepairTask>(repairer)) _simulation.World.Remove<RepairTask>(repairer);
		}
	}

	private void AdvanceRepairer(Entity repairerEntity, ref Position repairerPosition, ref RepairTask repairTask)
	{
		Entity building = repairTask.BuildingEntity;
		if (building == Entity.Null || !_simulation.World.IsAlive(building) || !_simulation.World.Has<Health>(building) || _simulation.World.Has<Dead>(building))
		{
			_finishedRepairers.Add(repairerEntity);
			return;
		}

		if (!_simulation.World.Has<RepairableBuilding>(building) || _simulation.World.Has<UnderConstruction>(building))
		{
			_finishedRepairers.Add(repairerEntity);
			return;
		}

		Health health = _simulation.World.Get<Health>(building);
		if (health.Current >= health.Max)
		{
			_finishedRepairers.Add(repairerEntity);
			return;
		}

		if (!_simulation.World.TryGet<Position>(building, out Position buildingPosition)) return;

		float reach = ReadRepairRange(repairerEntity) + ReadCollisionRadius(building);
		bool inRange = Vector3.DistanceSquared(repairerPosition.Value, buildingPosition.Value) <= reach * reach;

		if (!inRange)
		{
			if (!_simulation.World.Has<MoveTo>(repairerEntity)) _simulation.World.Add(repairerEntity, new MoveTo(buildingPosition.Value));
			return;
		}

		if (_simulation.World.Has<MoveTo>(repairerEntity)) _simulation.World.Remove<MoveTo>(repairerEntity);
		if (_simulation.World.Has<Velocity>(repairerEntity)) _simulation.World.Set(repairerEntity, new Velocity(Vector3.Zero));

		float rate = _simulation.World.TryGet<RepairWorker>(repairerEntity, out RepairWorker repairWorker)
			? repairWorker.HealthPercentPerSecond
			: 0f;
		if (rate <= 0f)
		{
			_finishedRepairers.Add(repairerEntity);
			return;
		}

		bool wasDormant = _simulation.World.Has<Dormant>(building);
		bool restored = health.Current + health.Max * rate * _deltaSeconds >= health.Max;

		health.Current = restored ? health.Max : health.Current + health.Max * rate * _deltaSeconds;
		_simulation.World.Set(building, health);

		if (!restored) return;

		if (wasDormant) _simulation.World.Remove<Dormant>(building);
		_finishedRepairers.Add(repairerEntity);
		Repaired?.Invoke(building);
	}

	private float ReadRepairRange(Entity repairerEntity)
	{
		if (!_simulation.World.TryGet<DefinitionId>(repairerEntity, out DefinitionId definitionId)) return UnitMetadata.DefaultConstructionRange;

		UnitMetadata? metadata = _catalog.FindUnitOrBuilding(definitionId.Value);
		return metadata == null ? UnitMetadata.DefaultConstructionRange : metadata.ConstructionRange;
	}

	private float ReadCollisionRadius(Entity building)
	{
		return _simulation.World.TryGet<CollisionRadius>(building, out CollisionRadius radius) ? radius.Value : 0f;
	}
}
