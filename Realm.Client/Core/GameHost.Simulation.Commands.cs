using System;
using System.Collections.Generic;
using Realm.Ecs;
using Godot;
using Realm.Client.ReplaySystem;
using Realm.Client.Network;
using Realm.Client.Services;
using Realm.Client.UI;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Resources;
using Realm.Ecs.Components.Tags;
using Realm.Ecs.Components.Terrain;
using Realm.Ecs.Services;
using UnitId = Realm.Ecs.UnitId;
using Vector3 = System.Numerics.Vector3;

namespace Realm.Client.Core;

public partial class GameHost
{
	private bool ExecuteQueuedCommand(Entity entity, string? commandType, Vector3 targetPos, Entity targetEntity)
	{
		return commandType switch
		{
			"move" => ExecuteMoveCommand(entity, targetPos),
			"attack" => ExecuteAttackCommand(entity, targetEntity),
			"attackmove" => ExecuteAttackMoveCommand(entity, targetPos),
			"follow" => ExecuteFollowCommand(entity, targetEntity),
			"patrol" => ExecutePatrolCommand(entity, targetPos),
			"gather" => ExecuteGatherCommand(entity, targetPos, targetEntity),
			_ => ExecuteFallbackCommand(entity, commandType, targetPos, targetEntity)
		};
	}

	private bool ExecuteMoveCommand(Entity entity, Vector3 targetPos)
	{
		return GameContext.SimulationService.ExecuteMoveCommandDomain(entity, targetPos);
	}

	private bool ExecuteAttackCommand(Entity entity, Entity targetEntity)
	{
		return GameContext.SimulationService.ExecuteAttackCommandDomain(entity, targetEntity);
	}

	private bool ExecuteAttackMoveCommand(Entity entity, Vector3 targetPos)
	{
		return GameContext.SimulationService.ExecuteAttackMoveCommandDomain(entity, targetPos);
	}

	private bool ExecuteFollowCommand(Entity entity, Entity targetEntity)
	{
		return GameContext.SimulationService.ExecuteFollowCommandDomain(entity, targetEntity);
	}

	private bool ExecutePatrolCommand(Entity entity, Vector3 targetPos)
	{
		return GameContext.SimulationService.ExecutePatrolCommandDomain(entity, targetPos);
	}

	private bool ExecuteGatherCommand(Entity entity, Vector3 targetPos, Entity targetEntity)
	{
		return GameContext.SimulationService.ExecuteGatherCommandDomain(entity, targetPos, targetEntity);
	}

	private bool ExecuteFallbackCommand(Entity entity, string? commandType, Vector3 targetPos, Entity targetEntity)
	{
		return GameContext.SimulationService.ExecuteFallbackCommandDomain(entity, commandType, targetPos, targetEntity, AssignBuildTaskToWorker);
	}
}
