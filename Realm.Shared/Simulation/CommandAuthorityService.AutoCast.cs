using System;
using System.Collections.Generic;
using System.IO;
using Realm.Ecs;
using Realm.Ecs.Components.Abilities;
using Realm.Ecs.Components.Core;
using Realm.Shared.Network;

namespace Realm.Shared.Simulation;

/// <summary>routing and applying a player's auto-cast toggle.</summary>
public partial class CommandAuthorityService
{
	private bool DispatchAutoCastCommand(int peerId, NetworkCommand cmd, List<int> rejectedServerEntityIds, ref string rejectionReason)
	{
		bool active;
		if (cmd.CommandType == "autocast_on")
		{
			active = true;
		}
		else if (cmd.CommandType == "autocast_off")
		{
			active = false;
		}
		else
		{
			return false;
		}

		HandleAutoCastCommand(peerId, cmd, active, rejectedServerEntityIds, ref rejectionReason);
		return true;
	}

	/// <summary>
	///     Turns one ability's auto-cast slot on or off for every unit the order names. A slot is the position the ability
	///     holds in the unit's own ability list, which is the same list the map's metadata states and the screen draws, so
	///     the order names the ability and the authority resolves the slot rather than trusting a number from a client.
	/// </summary>
	private void HandleAutoCastCommand(int peerId, NetworkCommand cmd, bool active, List<int> rejectedServerEntityIds, ref string rejectionReason)
	{
		string abilityId = cmd.ArgString ?? string.Empty;
		if (string.IsNullOrWhiteSpace(abilityId))
		{
			rejectionReason = $"'{cmd.CommandType}' names no ability";
			return;
		}

		if (cmd.UnitEntityIds == null || cmd.UnitEntityIds.Count == 0) return;

		for (int index = 0; index < cmd.UnitEntityIds.Count; index++)
		{
			int serverEntityId = cmd.UnitEntityIds[index];
			Entity entity = _targets.Resolve(serverEntityId);
			if (entity == Entity.Null) continue;

			if (!IsClientAuthorized(peerId, entity))
			{
				rejectionReason = $"unit {serverEntityId} is not owned by peer {peerId}";
				RecordCommandRejection(rejectedServerEntityIds, serverEntityId, rejectionReason);
				continue;
			}

			if (!_context.World.TryGet<AbilityState>(entity, out AbilityState abilities)) continue;

			int slot = FindAbilitySlot(abilities, abilityId);
			if (slot < 0) continue;

			if (!_context.World.Has<AutoCastState>(entity))
			{
				if (!active) continue;

				_context.World.Add(entity, new AutoCastState(true, 0));
			}

			ref AutoCastState autoCast = ref _context.World.Get<AutoCastState>(entity);
			uint bit = 1u << slot;
			if (active)
			{
				autoCast.ActiveAbilityBitmask |= bit;
			}
			else
			{
				autoCast.ActiveAbilityBitmask &= ~bit;
			}

			autoCast.Enabled = autoCast.ActiveAbilityBitmask != 0;
		}
	}

	private static int FindAbilitySlot(AbilityState abilities, string abilityId)
	{
		int slotCount = Math.Min(abilities.Count, 32);
		for (int slot = 0; slot < slotCount; slot++)
		{
			string? candidate = abilities.Get(slot);
			if (candidate == null) continue;
			if (string.Equals(candidate, abilityId, StringComparison.OrdinalIgnoreCase)) return slot;
			if (string.Equals(Path.GetFileNameWithoutExtension(candidate), Path.GetFileNameWithoutExtension(abilityId), StringComparison.OrdinalIgnoreCase)) return slot;
		}

		return -1;
	}
}
