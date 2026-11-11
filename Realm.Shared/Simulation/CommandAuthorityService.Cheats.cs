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

/// <summary>the cheat command and the effects it applies.</summary>
public partial class CommandAuthorityService
	{
	private void HandleCheatCommand(int peerId, NetworkCommand cmd, ref string rejectionReason)
	{
		string key = (cmd.ArgString ?? string.Empty).Trim().ToLowerInvariant();
		if (key.Length == 0)
		{
			rejectionReason = "a cheat order names no cheat";
			return;
		}

		if (!_context.CheatsEnabled)
		{
			rejectionReason = "this match does not allow cheats";
			return;
		}

		if (!TryGetPlayerEntityForPeer(peerId, out Entity playerEntity))
		{
			rejectionReason = $"peer {peerId} has no player entity";
			return;
		}

		switch (ParseCheat(key))
		{
			case CheatKind.SecureTheBag:
				ApplySecureTheBag(playerEntity);
				break;
			case CheatKind.GigaChad:
				ApplyGigachad(playerEntity);
				break;
			case CheatKind.NeedForSpeed:
				ApplyNeedForSpeed(peerId, cmd);
				break;
			case CheatKind.ThanosSnap:
				ApplyThanosSnap();
				break;
			case CheatKind.EzClap:
				_context.IsGameOver = true;
				break;
			case CheatKind.Aura:
				ApplyAura();
				break;
			case CheatKind.SpeedRun:
				_context.FastBuildEnabled = !_context.FastBuildEnabled;
				break;
			case CheatKind.Skibidi:
				ApplySkibidi(playerEntity);
				break;
			case CheatKind.Sacrifice:
				ApplySacrifice(peerId, cmd);
				break;
			default:
				rejectionReason = $"'{key}' is not a cheat this build knows";
				break;
		}
	}

	/// <summary>The cheats this build answers to; the wire carries their names, not these values.</summary>
	private enum CheatKind
	{
		None,
		SecureTheBag,
		GigaChad,
		NeedForSpeed,
		ThanosSnap,
		EzClap,
		Aura,
		SpeedRun,
		Skibidi,
		Sacrifice
	}

	private static CheatKind ParseCheat(string key) => key switch
	{
		"securethebag" => CheatKind.SecureTheBag,
		"gigachad" => CheatKind.GigaChad,
		"needforspeed" => CheatKind.NeedForSpeed,
		"thanossnap" => CheatKind.ThanosSnap,
		"ezclap" => CheatKind.EzClap,
		"aura" => CheatKind.Aura,
		"speedrun" => CheatKind.SpeedRun,
		"skibidi" => CheatKind.Skibidi,
		"sacrifice" => CheatKind.Sacrifice,
		_ => CheatKind.None
	};

	private void ApplySecureTheBag(Entity playerEntity)
	{
		if (!_context.World.TryGet<PlayerResources>(playerEntity, out PlayerResources resources)) return;

		const float ceiling = 9999f;
		var goldResourceId = new ResourceId("Gold");
		var woodResourceId = new ResourceId("Wood");
		var stoneResourceId = new ResourceId("Stone");

		if (resources.TryGetValue(goldResourceId, out int gold)) resources[goldResourceId] = (int)Math.Min(ceiling, gold + 10000f);
		if (resources.TryGetValue(woodResourceId, out int wood)) resources[woodResourceId] = (int)Math.Min(ceiling, wood + 10000f);
		if (resources.TryGetValue(stoneResourceId, out int stone)) resources[stoneResourceId] = (int)Math.Min(ceiling, stone + 10000f);

		_context.World.Set(playerEntity, resources);
	}

	private void ApplyGigachad(Entity playerEntity)
	{
		var query = QueryCache.AllHealthAndOwnerQuery;
		_context.World.Query(in query, (Entity entity, ref Owner owner) =>
		{
			if (owner.PlayerEntity.Value != playerEntity) return;

			if (_context.World.TryGet<Health>(entity, out Health _)) _context.World.Set(entity, new Health(9000f, 9000f));
			if (_context.World.TryGet<Attack>(entity, out Attack attack)) _context.World.Set(entity, new Attack(9001f, attack.Range, attack.Cooldown, attack.CurrentCooldown));
		});
	}

	private void ApplyNeedForSpeed(int peerId, NetworkCommand cmd)
	{
		if (cmd.UnitEntityIds == null) return;

		foreach (int serverId in cmd.UnitEntityIds)
		{
			Entity entity = _targets.Resolve(serverId);
			if (entity == Entity.Null || !IsClientAuthorized(peerId, entity)) continue;
			if (!_context.World.TryGet<MovementStats>(entity, out MovementStats movement)) continue;

			_context.World.Set(entity, new MovementStats(25f, movement.Acceleration, movement.TurnRate));
		}
	}

	private void ApplyThanosSnap()
	{
		var targets = new List<Entity>();
		var query = QueryCache.AllHealthAndOwnerQuery;

		_context.World.Query(in query, (Entity entity, ref Owner owner) =>
		{
			Entity ownerEntity = owner.PlayerEntity.Value;
			if (!_context.World.IsAlive(ownerEntity)) return;
			if (!_context.World.TryGet<Name>(ownerEntity, out Name name)) return;
			if (!string.Equals(name.Value, "Enemy_AI", StringComparison.Ordinal)) return;

			targets.Add(entity);
		});

		for (int index = 0; index < targets.Count; index++)
		{
			Entity entity = targets[index];
			if (!_context.World.IsAlive(entity)) continue;

			if (_context.World.TryGet<Health>(entity, out Health health)) _context.World.Set(entity, new Health(0f, health.Max));
			if (!_context.World.Has<Dead>(entity)) _context.World.Add<Dead>(entity);
		}
	}

	private void ApplyAura()
	{
		Entity worldEntity = _context.WorldEntity;
		if (worldEntity == Entity.Null || !_context.World.IsAlive(worldEntity)) return;

		_context.World.Mutate<ShroudState>(worldEntity, (ref ShroudState shroud) => shroud.VisibilityMode = ShroudVisibilityMode.Revealed);
	}

	/// <summary>
	///     Kills the units the order names, so a developer can reach a death the match would otherwise have to be played
	///     into. The blow is dealt through the damage pipeline rather than by marking the unit dead, because a death the
	///     resolver never saw is one the match, the map and every bounty rule would never be told about. Only the caller's
	///     own units answer, because a cheat still goes through the same authority check an order does.
	/// </summary>
	private void ApplySacrifice(int peerId, NetworkCommand cmd)
	{
		if (cmd.UnitEntityIds == null) return;
		if (_context.CombatDamageService == null) return;

		foreach (int serverId in cmd.UnitEntityIds)
		{
			Entity entity = _targets.Resolve(serverId);
			if (entity == Entity.Null || !IsClientAuthorized(peerId, entity)) continue;
			if (!_context.World.IsAlive(entity) || _context.World.Has<Dead>(entity)) continue;

			_context.CombatDamageService.DealResolvedDamage(entity, entity, 1000000f, "physical");
		}
	}

	private void ApplySkibidi(Entity playerEntity)
	{
		_context.UnlimitedPowerEnabled = !_context.UnlimitedPowerEnabled;
		if (!_context.UnlimitedPowerEnabled) return;

		_context.World.Mutate<SpellCooldowns>(playerEntity, (ref SpellCooldowns cooldowns) => cooldowns.Clear());
	}
}
