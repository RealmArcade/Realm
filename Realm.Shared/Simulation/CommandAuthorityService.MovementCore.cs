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

/// <summary>the group movement context every order shares.</summary>
public partial class CommandAuthorityService
	{
	private GroupMovementContext CreateMovementContext(int peerId, NetworkCommand cmd)
	{
		CalculateGroupMovement(peerId, cmd.UnitEntityIds, cmd.TargetPosition, out System.Numerics.Vector3 moveDir, out System.Numerics.Vector3 right);
		return new GroupMovementContext
		{
			MoveDir = moveDir,
			Right = right,
			Cols = (int)MathF.Ceiling(MathF.Sqrt(cmd.UnitEntityIds.Count)),
			Spacing = 2.2f
		};
	}

	private delegate void GroupCommandAction(Entity entity, System.Numerics.Vector3 scattered);

	private void ProcessGroupMovementCommand(int peerId, NetworkCommand cmd, GroupCommandAction processAction, List<int> rejectedServerEntityIds, ref string rejectionReason, bool requireMovable = true)
	{
		var ctx = CreateMovementContext(peerId, cmd);
		int unitIndex = 0;
		string? rejection = null;

		foreach (int serverId in cmd.UnitEntityIds)
		{
			var entity = _targets.Resolve(serverId);
			if (entity == Entity.Null)
			{
				rejection ??= $"entity {serverId} is not in the host unit list";
				RecordCommandRejection(rejectedServerEntityIds, serverId, rejection);
				continue;
			}

			if (!IsClientAuthorized(peerId, entity))
			{
				rejection ??= $"entity {serverId} is not owned by peer {peerId}";
				RecordCommandRejection(rejectedServerEntityIds, serverId, rejection);
				continue;
			}

			if (requireMovable && !_context.World.Has<Movable>(entity))
			{
				rejection ??= $"entity {serverId} is not movable";
				RecordCommandRejection(rejectedServerEntityIds, serverId, rejection);
				continue;
			}

			if (RejectsPlayerOrder(entity, serverId, rejectedServerEntityIds, ref rejectionReason)) continue;

			var scattered = CalculateScatteredPosition(cmd.TargetPosition, ctx.MoveDir, ctx.Right, unitIndex, ctx.Cols, ctx.Spacing);
			processAction(entity, scattered);
			unitIndex++;
		}

		if (rejection != null && string.IsNullOrEmpty(rejectionReason)) rejectionReason = rejection;

		if (unitIndex == 0 && cmd.UnitEntityIds.Count > 0)
		{
			DroppedOrderSink?.Invoke($"[Network] Dropped '{cmd.CommandType}' from peer {peerId}: none of its {cmd.UnitEntityIds.Count} units could take the order ({rejection}).");
		}
	}

	/// <summary>
	///     Answers whether this entity is one the map script drives, which is a unit its owner may select and inspect but
	///     may not order.
	/// </summary>
	private bool RejectsPlayerOrder(Entity entity, int serverId, List<int> rejectedServerEntityIds, ref string rejectionReason)
	{
		if (!_context.World.Has<PlayerOrdersLocked>(entity)) return false;

		const string reason = "the unit is driven by its map and takes no player order";
		RecordCommandRejection(rejectedServerEntityIds, serverId, reason);
		if (string.IsNullOrEmpty(rejectionReason)) rejectionReason = reason;
		return true;
	}

	private static void RecordCommandRejection(List<int> rejectedServerEntityIds, int serverId, string reason)
	{
		if (!rejectedServerEntityIds.Contains(serverId)) rejectedServerEntityIds.Add(serverId);
		_ = reason;
	}
}
