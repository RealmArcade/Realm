using System;
using System.Collections.Generic;
using Realm.Ecs;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Abilities;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Resources;
using Realm.Ecs.Components.Stats;
using Realm.Ecs.Components.Tags;
using Realm.Ecs.Components.Terrain;
using Realm.Ecs.Services;
using Vector3 = System.Numerics.Vector3;

namespace Realm.Client.Services;

/// <summary>
///     The production queue of every building: whether an entity is already busy, dequeuing its next task
///     into the command list the host executes each tick, and the retry-then-drop behaviour when the next
///     task cannot be started. Completion of a building is the same subject, so the construction components
///     come off here too.
/// </summary>
public sealed class BuildQueueProcessor
{
	private readonly List<(Entity Entity, string? Type, Vector3 Position, Entity Target)> _pending;

	public BuildQueueProcessor(List<(Entity Entity, string? Type, Vector3 Position, Entity Target)> pending)
	{
		_pending = pending;
	}

	public void DomainProcessCompletedBuilding(Entity buildingEntity, Action<Entity>? viewCallback, Action<Entity>? notifyCallback)
	{
		if (!SimulationService.World.IsAlive(buildingEntity)) return;

		RemoveConstructionComponents(buildingEntity);
		viewCallback?.Invoke(buildingEntity);
		notifyCallback?.Invoke(buildingEntity);
	}

	public void RemoveConstructionComponents(Entity buildingEntity)
	{
		bool wasUnderConstruction = SimulationService.World.Has<UnderConstruction>(buildingEntity);
		if (wasUnderConstruction)
		{
			SimulationService.World.Remove<UnderConstruction>(buildingEntity);
		}

		if (SimulationService.World.Has<ConstructionState>(buildingEntity))
		{
			SimulationService.World.Remove<ConstructionState>(buildingEntity);
		}

		if (wasUnderConstruction && SimulationService.World.Has<Invulnerable>(buildingEntity))
		{
			SimulationService.World.Remove<Invulnerable>(buildingEntity);
		}
	}

	public void ProcessBuildQueueEntity(Entity entity)
	{
		if (HasActiveTask(entity)) return;

		ref var q = ref SimulationService.World.Get<BuildQueue>(entity);
		if (q.Count > 0)
		{
			if (q.TryDequeue(out string? nextType, out var nextPos, out Entity nextTarget))
			{
				_pending.Add((entity, nextType, nextPos, nextTarget));
			}
		}
		else
		{
			_pending.Add((entity, "clear_queue_component", Vector3.Zero, Entity.Null));
		}
	}

	public bool HasActiveTask(Entity entity)
	{
		return SimulationService.World.Has<MoveTo>(entity) ||
			   SimulationService.World.Has<BuildTask>(entity) ||
			   SimulationService.World.Has<RepairTask>(entity) ||
			   SimulationService.World.Has<AttackTarget>(entity) ||
			   SimulationService.World.Has<AttackMove>(entity) ||
			   SimulationService.World.Has<Follow>(entity) ||
			   SimulationService.World.Has<Patrol>(entity) ||
			   SimulationService.World.Has<Gatherer>(entity) ||
			   SimulationService.World.Has<HealingTarget>(entity);
	}

	public void ProcessPendingQueuedCommands(Func<Entity, string?, Vector3, Entity, bool> executeCommandCallback)
	{
		foreach (var cmd in _pending)
		{
			if (!SimulationService.World.IsAlive(cmd.Entity)) continue;

			ProcessPendingCommand(cmd, executeCommandCallback);
		}
	}

	public void ProcessPendingCommand((Entity Entity, string? Type, Vector3 Position, Entity Target) cmd, Func<Entity, string?, Vector3, Entity, bool> executeCommandCallback)
	{
		if (cmd.Type == "clear_queue_component")
		{
			ClearQueueComponent(cmd.Entity);
			return;
		}

		bool success = executeCommandCallback(cmd.Entity, cmd.Type, cmd.Position, cmd.Target);
		ProcessRemainingQueue(cmd.Entity, success, executeCommandCallback);
	}

	public void ClearQueueComponent(Entity entity)
	{
		if (SimulationService.World.Has<BuildQueue>(entity))
		{
			SimulationService.World.Remove<BuildQueue>(entity);
		}
	}

	public void ProcessRemainingQueue(Entity entity, bool lastSuccess, Func<Entity, string?, Vector3, Entity, bool> executeCommandCallback)
	{
		while (!lastSuccess && SimulationService.World.Has<BuildQueue>(entity))
		{
			ref var q = ref SimulationService.World.Get<BuildQueue>(entity);
			if (q.TryDequeue(out string? nextType, out var nextPos, out Entity nextTarget))
			{
				lastSuccess = executeCommandCallback(entity, nextType ?? "", nextPos, nextTarget);
			}
			else
			{
				SimulationService.World.Remove<BuildQueue>(entity);
				break;
			}
		}

		if (!lastSuccess && SimulationService.World.Has<BuildQueue>(entity) && SimulationService.World.Get<BuildQueue>(entity).Count == 0)
		{
			SimulationService.World.Remove<BuildQueue>(entity);
		}
	}
}
