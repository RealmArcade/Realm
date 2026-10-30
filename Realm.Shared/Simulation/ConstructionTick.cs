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

public class ConstructionTick
{
	public const float BaseWorkRatePerSecond = 1f;

	private const float WorkRateMultiplierFastBuild = 10f;

	private static readonly QueryDescription WorkerQuery = QueryCache.AllPositionAndBuildTaskNoneDeadQuery;

	private readonly SimulationContext _simulation;
	private readonly DefinitionCatalog _catalog;
	private readonly EntityCallback<Position, BuildTask> _advanceWorker;
	private readonly List<(Entity Worker, Entity Site)> _workingPairs = new();
	private readonly List<Entity> _completedSites = new();
	private readonly HashSet<Entity> _completedSet = new();
	private readonly List<Entity> _abandonedWorkers = new();
	private float _deltaSeconds;

	public ConstructionTick(SimulationContext simulation, DefinitionCatalog catalog)
	{
		_simulation = simulation;
		_catalog = catalog;
		_advanceWorker = AdvanceWorker;
	}

	public event Action<Entity>? Completed;

	public void Step(float deltaSeconds)
	{
		_deltaSeconds = deltaSeconds;
		_completedSites.Clear();
		_completedSet.Clear();
		_workingPairs.Clear();
		_abandonedWorkers.Clear();

		_simulation.World.Query(in WorkerQuery, _advanceWorker);

		for (int index = 0; index < _abandonedWorkers.Count; index++)
		{
			Entity worker = _abandonedWorkers[index];
			if (_simulation.World.IsAlive(worker) && _simulation.World.Has<BuildTask>(worker)) _simulation.World.Remove<BuildTask>(worker);
		}

		CollectSitesAlreadyComplete();

		for (int index = 0; index < _completedSites.Count; index++)
		{
			CompleteSite(_completedSites[index]);
		}
	}

	private void AdvanceWorker(Entity workerEntity, ref Position workerPosition, ref BuildTask buildTask)
	{
		Entity site = buildTask.BuildingEntity;
		if (site == Entity.Null || !_simulation.World.IsAlive(site) || !_simulation.World.Has<ConstructionState>(site))
		{
			_abandonedWorkers.Add(workerEntity);
			return;
		}

		if (!_simulation.World.TryGet<Position>(site, out Position sitePosition)) return;

		float reach = ReadConstructionRange(workerEntity) + ReadCollisionRadius(site);
		bool inRange = Vector3.DistanceSquared(workerPosition.Value, sitePosition.Value) <= reach * reach;

		if (!inRange)
		{
			if (!_simulation.World.Has<MoveTo>(workerEntity)) _simulation.World.Add(workerEntity, new MoveTo(sitePosition.Value));
			return;
		}

		if (_simulation.World.Has<MoveTo>(workerEntity)) _simulation.World.Remove<MoveTo>(workerEntity);
		if (_simulation.World.Has<Velocity>(workerEntity)) _simulation.World.Set(workerEntity, new Velocity(Vector3.Zero));

		float progressGain = BaseWorkRatePerSecond * (_simulation.FastBuildEnabled ? WorkRateMultiplierFastBuild : 1f) * _deltaSeconds;

		ref ConstructionState construction = ref _simulation.World.Get<ConstructionState>(site);
		construction.Progress = MathF.Min(construction.Progress + progressGain, construction.TotalBuildTime);

		buildTask.Progress = MathF.Min(buildTask.Progress + progressGain, MathF.Max(buildTask.TotalBuildTime, construction.TotalBuildTime));

		_workingPairs.Add((workerEntity, site));

		if (construction.IsComplete) QueueCompleted(site);
	}

	private void CollectSitesAlreadyComplete()
	{
		foreach (Entity site in _simulation.World.Query<ConstructionState>())
		{
			ref ConstructionState construction = ref _simulation.World.Get<ConstructionState>(site);
			if (construction.IsComplete) QueueCompleted(site);
		}
	}

	private void QueueCompleted(Entity site)
	{
		if (!_completedSet.Add(site)) return;

		_completedSites.Add(site);
	}

	private void CompleteSite(Entity site)
	{
		if (_simulation.World.Has<ConstructionState>(site)) _simulation.World.Remove<ConstructionState>(site);
		if (_simulation.World.Has<UnderConstruction>(site)) _simulation.World.Remove<UnderConstruction>(site);

		for (int index = 0; index < _workingPairs.Count; index++)
		{
			if (_workingPairs[index].Site != site) continue;

			Entity worker = _workingPairs[index].Worker;
			if (_simulation.World.IsAlive(worker) && _simulation.World.Has<BuildTask>(worker)) _simulation.World.Remove<BuildTask>(worker);
		}

		_simulation.MovementPathfindingService.InvalidateObstacleGrid();
		Completed?.Invoke(site);
	}

	private float ReadConstructionRange(Entity workerEntity)
	{
		if (!_simulation.World.TryGet<DefinitionId>(workerEntity, out DefinitionId definitionId)) return UnitMetadata.DefaultConstructionRange;

		UnitMetadata? metadata = _catalog.FindUnitOrBuilding(definitionId.Value);
		return metadata == null ? UnitMetadata.DefaultConstructionRange : metadata.ConstructionRange;
	}

	private float ReadCollisionRadius(Entity site)
	{
		return _simulation.World.TryGet<CollisionRadius>(site, out CollisionRadius radius) ? radius.Value : 0f;
	}
}
