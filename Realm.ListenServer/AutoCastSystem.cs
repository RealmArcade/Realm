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
	private readonly List<AutoCastRequest> _pendingCasts = new();

	public AutoCastSystem(SimulationContext context, ScriptAbilityRegistry abilities, ServerGameAPI api)
	{
		_context = context;
		_abilities = abilities;
		_api = api;
		_unitCallback = CollectUnit;
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
		_context.World.Query(in QueryCache.AllAutoCastQuery, _unitCallback);
		for (int index = 0; index < _pendingCasts.Count; index++) Cast(_pendingCasts[index]);
		_pendingCasts.Clear();
	}

	private void CollectUnit(Entity entity, ref AutoCastState autoCastState, ref Position position, ref DefinitionId definitionId, ref Owner owner)
	{
		if (!autoCastState.Enabled || autoCastState.ActiveAbilityBitmask == 0u) return;

		EcsWorld world = _context.World;
		if (world.Has<Dead>(entity) || world.Has<CommandsFrozen>(entity)) return;
		if (!world.TryGet<AbilityState>(entity, out AbilityState abilityState) || abilityState.Count == 0) return;

		Vector3 casterPosition = position.Value;
		bool hasTarget = TryReadEngagementPosition(entity, out Vector3 targetPosition);
		float targetDistance = hasTarget ? Vector3.Distance(casterPosition, targetPosition) : 0f;
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

			float reach = MathF.Max(definition.TargetRange, definition.AreaOfEffectRadius);
			if (definition.AutoCastRequiresEnemy)
			{
				if (!hasTarget) continue;
				if (reach > 0f && targetDistance > reach) continue;
			}

			float manaCost = definition.ManaCost;
			if (manaCost > 0f && !CanPayMana(hasMana, in mana, definition, manaCost)) continue;

			Vector3 castPosition = definition.AutoCastRequiresEnemy ? targetPosition : casterPosition;
			_pendingCasts.Add(new AutoCastRequest(entity, abilityId, castPosition, manaCost, definition.Cooldown));
			return;
		}
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

	private bool TryReadEngagementPosition(Entity entity, out Vector3 position)
	{
		EcsWorld world = _context.World;
		position = Vector3.Zero;

		if (!world.TryGet<AttackTarget>(entity, out AttackTarget attackTarget)) return false;

		Entity target = attackTarget.Target;
		if (target == Entity.Null || !world.IsAlive(target) || world.Has<Dead>(target)) return false;
		if (!world.TryGet<Position>(target, out Position targetPosition)) return false;

		position = targetPosition.Value;
		return true;
	}

	private static bool CanPayMana(bool hasMana, in Mana mana, AbilityDefinition definition, float manaCost)
	{
		if (!hasMana) return false;
		if (mana.Current < manaCost + definition.AutoCastManaReserve) return false;
		if (mana.Max > 0f && mana.Current - manaCost < definition.AutoCastMinManaPercent * mana.Max) return false;

		return true;
	}

	private const int MaximumAbilitySlots = 32;

	private readonly record struct AutoCastRequest(Entity Caster, string AbilityId, Vector3 TargetPosition, float ManaCost, float Cooldown);
}
