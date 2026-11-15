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

/// <summary>authorising and validating a spell command.</summary>
public partial class CommandAuthorityService
	{
	public bool TryGetPlayerEntityForPeer(int peerId, out Entity playerEntity)
	{
		playerEntity = Entity.Null;
		Entity worldEntity = _context.WorldEntity;
		if (worldEntity == Entity.Null || !_context.World.Has<NetworkMappingState>(worldEntity)) return false;
		var mapping = _context.World.Get<NetworkMappingState>(worldEntity);
		if (!mapping.PeerIdToPlayerEntityMap.TryGetValue(peerId, out playerEntity)) return false;
		return playerEntity != Entity.Null && _context.World.IsAlive(playerEntity);
	}

	public bool ValidateSpellCommandForHarness(int peerId, NetworkCommand cmd, out string rejectionReason)
	{
		return ValidateSpellCommand(peerId, cmd, out _, out rejectionReason);
	}

	private bool TryAuthorizeSpellCommand(int peerId, NetworkCommand cmd, out string rejectionReason)
	{
		if (!ValidateSpellCommand(peerId, cmd, out Entity casterEntity, out rejectionReason)) return false;

		string spellId = cmd.ArgString ?? string.Empty;
		if (!TryGetAbilityRules(spellId, out AbilityRules definition)) return false;

		if (!TryGetPlayerEntityForPeer(peerId, out Entity playerEntity)) return false;

		if (_context.UnlimitedPowerEnabled) return true;

		if (definition.ManaCost > 0f && _context.World.Has<Mana>(casterEntity))
		{
			ref var mana = ref _context.World.Get<Mana>(casterEntity);
			mana.Current -= definition.ManaCost;
		}

		// The cooldown belongs to the caster rather than to the player: two buildings raising the same soldier line are
		// two casters, and a player-wide clock stopped the second one from producing for as long as the first. It is
		// stated after the caster's own cooldown reduction, the same arithmetic the engine's auto-cast uses.
		float cooldown = UnitOrderService.EffectiveAbilityCooldown(_context.World, casterEntity, definition.Cooldown);
		if (cooldown > 0f) UnitOrderService.SetAbilityCooldown(_context.World, casterEntity, spellId, cooldown);

		return true;
	}

	private bool ValidateSpellCommand(int peerId, NetworkCommand cmd, out Entity casterEntity, out string rejectionReason)
	{
		casterEntity = Entity.Null;
		rejectionReason = string.Empty;

		if (cmd.UnitEntityIds == null || cmd.UnitEntityIds.Count == 0)
		{
			rejectionReason = "spell command does not name a caster";
			return false;
		}

		string spellId = cmd.ArgString ?? string.Empty;
		if (!TryGetAbilityRules(spellId, out AbilityRules definition))
		{
			rejectionReason = $"spell '{spellId}' is not defined";
			return false;
		}

		casterEntity = _targets.Resolve(cmd.UnitEntityIds[0]);
		if (casterEntity == Entity.Null)
		{
			rejectionReason = $"caster {cmd.UnitEntityIds[0]} is not a live unit";
			return false;
		}

		if (!IsClientAuthorized(peerId, casterEntity))
		{
			rejectionReason = $"caster {cmd.UnitEntityIds[0]} is not owned by peer {peerId}";
			return false;
		}

		if (!TryGetPlayerEntityForPeer(peerId, out Entity playerEntity))
		{
			rejectionReason = $"peer {peerId} has no player entity";
			return false;
		}

		float remainingCooldown = UnitOrderService.GetAbilityCooldownRemaining(_context.World, casterEntity, spellId);
		if (!_context.UnlimitedPowerEnabled && remainingCooldown > 0f)
		{
			rejectionReason = $"spell '{spellId}' is on cooldown for {remainingCooldown:F1} s";
			return false;
		}

		if (!_context.UnlimitedPowerEnabled && definition.ManaCost > 0f && _context.World.Has<Mana>(casterEntity))
		{
			ref var mana = ref _context.World.Get<Mana>(casterEntity);
			if (mana.Current < definition.ManaCost)
			{
				rejectionReason = $"caster needs {definition.ManaCost:F0} mana and has {mana.Current:F0}";
				return false;
			}
		}

		if (definition.TargetRange > 0f && _context.World.Has<Position>(casterEntity))
		{
			System.Numerics.Vector3 casterPosition = _context.World.Get<Position>(casterEntity).Value;
			System.Numerics.Vector3 targetPosition = cmd.TargetPosition.ToNumerics();
			float distance = System.Numerics.Vector3.Distance(casterPosition, targetPosition);
			if (distance > definition.TargetRange)
			{
				rejectionReason = $"target is {distance:F1} m away, beyond the {definition.TargetRange:F1} m range of '{spellId}'";
				return false;
			}
		}

		return true;
	}
}
