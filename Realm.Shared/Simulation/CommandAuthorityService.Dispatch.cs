using System;
using System.Collections.Generic;
using System.Numerics;
using Realm.Ecs;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Resources;
using Realm.Ecs.Components.Tags;
using Realm.Ecs.Components.Terrain;
using Realm.Ecs.Services;
using Realm.Ecs.Utils;
using Realm.Shared.Metadata;
using Realm.Shared.Network;

namespace Realm.Shared.Simulation;

/// <summary>routing a server command to the family that owns it.</summary>
public partial class CommandAuthorityService
	{
	private void DispatchServerCommand(int peerId, NetworkCommand cmd, List<int> stopVelocityEntityIds, List<int> holdVelocityEntityIds, List<int> rejectedServerEntityIds, ref string rejectionReason)
	{
		if (DispatchMovementCommand(peerId, cmd, stopVelocityEntityIds, holdVelocityEntityIds, rejectedServerEntityIds, ref rejectionReason)) return;
		if (DispatchActionCommand(peerId, cmd, rejectedServerEntityIds, ref rejectionReason)) return;
		if (DispatchTrainingCommand(peerId, cmd, rejectedServerEntityIds, ref rejectionReason)) return;
		if (DispatchResearchCommand(peerId, cmd, ref rejectionReason)) return;
		if (DispatchConsumableCommand(peerId, cmd, ref rejectionReason)) return;
		if (DispatchAutoCastCommand(peerId, cmd, rejectedServerEntityIds, ref rejectionReason)) return;
		DispatchCheatCommand(peerId, cmd, ref rejectionReason);
	}

	private bool DispatchMovementCommand(int peerId, NetworkCommand cmd, List<int> stopVelocityEntityIds, List<int> holdVelocityEntityIds, List<int> rejectedServerEntityIds, ref string rejectionReason)
	{
		switch (cmd.CommandType)
		{
			case "move":
				HandleMoveCommand(peerId, cmd, rejectedServerEntityIds, ref rejectionReason);
				return true;
			case "move_queued":
				HandleMoveQueuedCommand(peerId, cmd, rejectedServerEntityIds, ref rejectionReason);
				return true;
			case "attack_move":
				HandleAttackMoveCommand(peerId, cmd, false, rejectedServerEntityIds, ref rejectionReason);
				return true;
			case "attack_move_queued":
				HandleAttackMoveCommand(peerId, cmd, true, rejectedServerEntityIds, ref rejectionReason);
				return true;
			case "stop":
				HandleStopCommand(peerId, cmd, stopVelocityEntityIds, rejectedServerEntityIds, ref rejectionReason);
				return true;
			case "hold":
				HandleHoldCommand(peerId, cmd, holdVelocityEntityIds, rejectedServerEntityIds, ref rejectionReason);
				return true;
			case "patrol":
			case "patrol_queued":
				HandlePatrolCommand(peerId, cmd, cmd.CommandType == "patrol_queued", rejectedServerEntityIds, ref rejectionReason);
				return true;
		}

		return false;
	}

	private bool DispatchActionCommand(int peerId, NetworkCommand cmd, List<int> rejectedServerEntityIds, ref string rejectionReason)
	{
		bool isQueued = cmd.CommandType.EndsWith("_queued");
		switch (cmd.CommandType)
		{
			case "attack":
			case "attack_queued":
				HandleAttackCommand(peerId, cmd, isQueued, rejectedServerEntityIds, ref rejectionReason);
				return true;
			case "follow":
			case "follow_queued":
				HandleFollowCommand(peerId, cmd, isQueued, rejectedServerEntityIds, ref rejectionReason);
				return true;
			case "gather":
			case "gather_queued":
				HandleGatherCommand(peerId, cmd, isQueued, rejectedServerEntityIds, ref rejectionReason);
				return true;
			case "repair":
			case "repair_queued":
				HandleRepairCommand(peerId, cmd, isQueued, rejectedServerEntityIds, ref rejectionReason);
				return true;
			case "pickup":
			case "pickup_queued":
				HandlePickupCommand(peerId, cmd, isQueued, rejectedServerEntityIds, ref rejectionReason);
				return true;
		}

		return false;
	}

	private bool DispatchTrainingCommand(int peerId, NetworkCommand cmd, List<int> rejectedServerEntityIds, ref string rejectionReason)
	{
		switch (cmd.CommandType)
		{
			case "train":
				HandleTrainCommand(peerId, cmd, rejectedServerEntityIds, ref rejectionReason);
				return true;
			case "cancel_train":
				HandleCancelTrainCommand(peerId, cmd);
				return true;
		}

		return false;
	}

	private bool DispatchResearchCommand(int peerId, NetworkCommand cmd, ref string rejectionReason)
	{
		if (cmd.CommandType != "research") return false;

		HandleResearchCommand(peerId, cmd, ref rejectionReason);
		return true;
	}

	private struct GroupMovementContext
	{
		public System.Numerics.Vector3 MoveDir;
		public System.Numerics.Vector3 Right;
		public int Cols;
		public float Spacing;
	}
}
