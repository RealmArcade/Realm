using System;
using System.Collections.Generic;
using System.Numerics;
using Realm.Ecs;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Abilities;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Tags;
using Realm.Ecs.Services;
using Realm.MapAPI;
using Realm.Shared.Abilities;

namespace Realm.ListenServer;

public class AutoCastSystem
{
	private readonly SimulationContext _context;
	private readonly ScriptAbilityRegistry _abilities;
	private readonly ServerGameAPI _api;
	private readonly EntityCallback<AutoCastState, Position, DefinitionId, Owner> _unitCallback;
	private readonly EntityCallback<Position, Owner> _enemyCallback;
	private readonly QueryDescription _enemyQuery = QueryCache.AllPositionAndOwnerNoneDeadQuery;
	private readonly List<AutoCastRequest> _pendingCasts = new();
	private readonly List<EnemySnapshot> _enemies = new();

	public AutoCastSystem(SimulationContext context, ScriptAbilityRegistry abilities, ServerGameAPI api)
	{
		_context = context;
		_abilities = abilities;
		_api = api;
		_unitCallback = CollectUnit;
		_enemyCallback = CollectEnemy;
	}

	public void Grant(Entity entity)
	{
		EcsWorld world = _context.World;
		if (entity == Entity.Null || !world.IsAlive(entity)) return;
		if (world.Has<AutoCastState>(entity)) return;
		if (!world.TryGet<AbilityState>(entity, out AbilityState abilityState)) return;

		uint activeAbilityBitmask = 0u;
		for (int index = 0; index < abilityState.Count && index < AutoCastSystem.MaximumAbilitySlots; index++)
		{
			string? abilityId = abilityState.Get(index);
			if (string.IsNullOrEmpty(abilityId)) continue;

			AbilityDefinition definition = _abilities.GetDefinition(abilityId);
			if (definition == null || !definition.AutoCastDeclared || !definition.AutoCastEnabled) continue;

			activeAbilityBitmask |= 1u << index;
		}

		if (activeAbilityBitmask == 0u) return;
		world.SetOrAdd(entity, new AutoCastState(true, activeAbilityBitmask));
	}

	public void Step()
	{
		_pendingCasts.Clear();
		_enemies.Clear();
		_context.World.Query(in _enemyQuery, _enemyCallback);
		_context.World.Query(in QueryCache.AllAutoCastQuery, _unitCallback);
		for (int index = 0; index < _pendingCasts.Count; index++) Cast(_pendingCasts[index]);
		_pendingCasts.Clear();
		_enemies.Clear();
	}

	private void CollectEnemy(Entity entity, ref Position position, ref Owner owner)
	{
		if (_context.World.Has<Invulnerable>(entity)) return;

		_enemies.Add(new EnemySnapshot(position.Value, ResolvePlayerIndex(entity)));
	}

	private void CollectUnit(Entity entity, ref AutoCastState autoCastState, ref Position position, ref DefinitionId definitionId, ref Owner owner)
	{
		if (!autoCastState.Enabled || autoCastState.ActiveAbilityBitmask == 0u) return;

		EcsWorld world = _context.World;
		if (world.Has<Dead>(entity) || world.Has<CommandsFrozen>(entity)) return;
		if (!world.TryGet<AbilityState>(entity, out AbilityState abilityState) || abilityState.Count == 0) return;

		Vector3 casterPosition = position.Value;
		int casterPlayerIndex = ResolvePlayerIndex(entity);
		bool hasMana = world.TryGet<Mana>(entity, out Mana mana);

		for (int index = 0; index < abilityState.Count && index < AutoCastSystem.MaximumAbilitySlots; index++)
		{
			if ((autoCastState.ActiveAbilityBitmask & (1u << index)) == 0u) continue;

			string? abilityId = abilityState.Get(index);
			if (string.IsNullOrEmpty(abilityId)) continue;

			AbilityDefinition definition = _abilities.GetDefinition(abilityId);
			if (definition == null || !definition.AutoCastEnabled) continue;
			if (_abilities.IsUnitAbilityDisabled(entity.Id, abilityId)) continue;
			if (UnitOrderService.GetAbilityCooldownRemaining(world, entity, abilityId) > 0f) continue;

			Vector3 castPosition = casterPosition;
			if (definition.AutoCastRequiresEnemy)
			{
				float reach = MathF.Max(definition.TargetRange, definition.AreaOfEffectRadius);
				if (!TryFindNearestEnemy(casterPosition, casterPlayerIndex, reach, out castPosition)) continue;
			}

			float manaCost = definition.ManaCost;
			if (manaCost > 0f && !CanPayMana(hasMana, in mana, definition, manaCost)) continue;

			_pendingCasts.Add(new AutoCastRequest(entity, abilityId, castPosition, manaCost, definition.Cooldown));
			return;
		}
	}

	private bool TryFindNearestEnemy(Vector3 casterPosition, int casterPlayerIndex, float reach, out Vector3 enemyPosition)
	{
		enemyPosition = Vector3.Zero;
		float bestDistanceSquared = reach > 0f ? reach * reach : float.PositiveInfinity;
		bool found = false;

		for (int index = 0; index < _enemies.Count; index++)
		{
			EnemySnapshot enemy = _enemies[index];
			if (!ArePlayersEnemies(casterPlayerIndex, enemy.PlayerIndex)) continue;

			float distanceSquared = Vector3.DistanceSquared(casterPosition, enemy.Position);
			if (distanceSquared > bestDistanceSquared) continue;

			bestDistanceSquared = distanceSquared;
			enemyPosition = enemy.Position;
			found = true;
		}

		return found;
	}

	private int ResolvePlayerIndex(Entity entity)
	{
		EcsWorld world = _context.World;
		if (entity == Entity.Null || !world.IsAlive(entity)) return 0;
		if (world.Has<UnitOwnerPlayer>(entity)) return world.Get<UnitOwnerPlayer>(entity).PlayerIndex;
		if (!world.Has<Owner>(entity)) return 0;

		Entity playerEntity = world.Get<Owner>(entity).PlayerEntity.Value;
		if (!world.IsAlive(playerEntity) || !world.Has<UnitOwnerPlayer>(playerEntity)) return 0;

		return world.Get<UnitOwnerPlayer>(playerEntity).PlayerIndex;
	}

	private bool ArePlayersEnemies(int playerIndex, int otherPlayerIndex)
	{
		if (playerIndex == otherPlayerIndex) return false;

		EcsWorld world = _context.World;
		Entity worldEntity = _context.WorldEntity;
		if (worldEntity != Entity.Null && world.IsAlive(worldEntity) && world.Has<PlayerRelationshipMatrix>(worldEntity))
		{
			PlayerRelationship relationship = world.Get<PlayerRelationshipMatrix>(worldEntity).GetRelationship(playerIndex, otherPlayerIndex);
			if ((relationship & PlayerRelationship.Ally) != 0) return false;
			if ((relationship & PlayerRelationship.Enemy) != 0) return true;
		}

		return true;
	}

	private void Cast(AutoCastRequest request)
	{
		EcsWorld world = _context.World;
		if (!world.IsAlive(request.Caster) || world.Has<Dead>(request.Caster)) return;

		if (request.ManaCost > 0f)
		{
			if (!world.Has<Mana>(request.Caster)) return;

			ref Mana mana = ref world.Get<Mana>(request.Caster);
			if (mana.Current < request.ManaCost) return;

			mana.Current -= request.ManaCost;
		}

		if (request.Cooldown > 0f) UnitOrderService.SetAbilityCooldown(world, request.Caster, request.AbilityId, request.Cooldown);

		_api.CastAbility(new UnitId(request.Caster.Id), request.AbilityId, request.TargetPosition);
	}

	private bool CanPayMana(bool hasMana, in Mana mana, AbilityDefinition definition, float manaCost)
	{
		if (!hasMana) return false;
		if (mana.Current < manaCost + definition.AutoCastManaReserve) return false;
		if (mana.Max > 0f && mana.Current - manaCost < definition.AutoCastMinManaPercent * mana.Max) return false;

		return true;
	}

	private const int MaximumAbilitySlots = 32;

	private readonly record struct AutoCastRequest(Entity Caster, string AbilityId, Vector3 TargetPosition, float ManaCost, float Cooldown);

	private readonly record struct EnemySnapshot(Vector3 Position, int PlayerIndex);
}
