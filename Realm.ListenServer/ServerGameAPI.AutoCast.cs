using System;
using System.Collections.Generic;
using System.Numerics;
using Realm.Ecs;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Abilities;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Resources;
using Realm.Ecs.Components.Stats;
using Realm.Ecs.Components.Tags;
using Realm.Ecs.Services;
using Realm.MapAPI;
using Realm.Shared.Abilities;
using Realm.Shared.Metadata;

namespace Realm.ListenServer;

public partial class ServerGameAPI
{
	private const float DefaultAutoCastSearchRadius = 15f;

	private readonly List<int> _autoCastUnitIds = new(64);

	/// <summary>
	///     Casts the abilities a unit holds in auto-cast once every condition the ability states is met: the slot is on,
	///     the map has not disabled it, its mana is paid and its cooldown has elapsed. Auto-cast is the engine's own
	///     cast, so it pays the unit's mana and starts the unit's cooldown exactly as a player-ordered cast does, and an
	///     ability whose effect list is empty still reaches the map through <see cref="OnSpellCast" />.
	/// </summary>
	/// <param name="deltaSeconds">The seconds this tick covers.</param>
	public void StepAutoCast(float deltaSeconds)
	{
		if (deltaSeconds <= 0f) return;

		EcsWorld world = _context.World;
		_autoCastUnitIds.Clear();

		foreach (Entity entity in world.Query<AutoCastState, AbilityState>())
		{
			_autoCastUnitIds.Add(entity.Id);
		}

		for (int candidateIndex = 0; candidateIndex < _autoCastUnitIds.Count; candidateIndex++)
		{
			var entity = new Entity(_autoCastUnitIds[candidateIndex]);
			if (!world.IsAlive(entity)) continue;
			if (world.Has<Dead>(entity) || world.Has<Dormant>(entity)) continue;
			if (!world.Has<AutoCastState>(entity) || !world.Has<AbilityState>(entity)) continue;

			CastEnabledAutoCastSlots(entity);
		}
	}

	private void CastEnabledAutoCastSlots(Entity entity)
	{
		EcsWorld world = _context.World;
		ref AutoCastState autoCast = ref world.Get<AutoCastState>(entity);
		uint activeSlots = autoCast.ActiveAbilityBitmask;
		if (activeSlots == 0) return;

		ref AbilityState abilities = ref world.Get<AbilityState>(entity);
		int slotCount = Math.Min(abilities.Count, 32);
		Vector3 casterPosition = world.TryGet<Position>(entity, out Position position) ? position.Value : Vector3.Zero;
		var unit = new UnitId(entity.Id);

		for (int slot = 0; slot < slotCount; slot++)
		{
			if ((activeSlots & (1u << slot)) == 0) continue;

			string? abilityId = abilities.Get(slot);
			if (string.IsNullOrEmpty(abilityId)) continue;
			if (_abilities.IsUnitAbilityDisabled(entity.Id, abilityId)) continue;

			AbilityDefinition definition = _abilities.GetDefinition(abilityId);
			if (!definition.AutoCastEnabled) continue;
			if (UnitOrderService.GetAbilityCooldownRemaining(world, entity, abilityId) > 0f) continue;

			float manaCost = _abilities.UnitAbilityManaCost(entity.Id, abilityId) ?? definition.ManaCost;
			if (!CanPayAutoCastMana(entity, definition, manaCost)) continue;

			Vector3 targetPosition = casterPosition;
			if (!TryResolveAutoCastTarget(entity, casterPosition, definition, out targetPosition)) continue;

			if (manaCost > 0f) SpendMana(entity, manaCost);

			float cooldown = UnitOrderService.EffectiveAbilityCooldown(world, entity, definition.Cooldown);
			if (cooldown > 0f) UnitOrderService.SetAbilityCooldown(world, entity, abilityId, cooldown);

			CastAbility(unit, abilityId, targetPosition);
		}
	}

	private bool CanPayAutoCastMana(Entity entity, AbilityDefinition definition, float manaCost)
	{
		if (manaCost <= 0f) return true;

		EcsWorld world = _context.World;
		if (!world.TryGet<Mana>(entity, out Mana mana)) return false;
		if (mana.Current < manaCost) return false;

		float remaining = mana.Current - manaCost;
		if (remaining < definition.AutoCastManaReserve) return false;

		return remaining >= mana.Max * definition.AutoCastMinManaPercent;
	}

	private void SpendMana(Entity entity, float manaCost)
	{
		EcsWorld world = _context.World;
		if (!world.Has<Mana>(entity)) return;

		ref Mana mana = ref world.Get<Mana>(entity);
		mana.Current = MathF.Max(0f, mana.Current - manaCost);
	}

	private bool TryResolveAutoCastTarget(Entity caster, Vector3 casterPosition, AbilityDefinition definition, out Vector3 targetPosition)
	{
		targetPosition = casterPosition;

		return definition.AutoCastTargetRule switch
		{
			AutoCastTargetRule.Self => true,
			AutoCastTargetRule.NearestEnemy => TryFindNearestEnemyTarget(caster, casterPosition, definition, out targetPosition),
			AutoCastTargetRule.MostWoundedAlly => TryFindLowestHealthAlly(caster, casterPosition, definition, out targetPosition, byHealthFraction: true),
			AutoCastTargetRule.LowestHealthAlly => TryFindLowestHealthAlly(caster, casterPosition, definition, out targetPosition, byHealthFraction: false),
			AutoCastTargetRule.AllyWithoutEffect => TryFindAllyWithoutEffect(caster, casterPosition, definition, out targetPosition),
			_ => false,
		};
	}

	private static float ResolveAutoCastSearchRadius(AbilityDefinition definition)
	{
		return definition.TargetRange > 0f ? definition.TargetRange : DefaultAutoCastSearchRadius;
	}

	private bool TryFindNearestEnemyTarget(Entity caster, Vector3 casterPosition, AbilityDefinition definition, out Vector3 targetPosition)
	{
		targetPosition = casterPosition;

		EcsWorld world = _context.World;
		float searchRadius = ResolveAutoCastSearchRadius(definition);
		float bestDistanceSquared = searchRadius * searchRadius;
		Entity best = Entity.Null;
		int casterPlayer = AutoCastPlayerOf(caster);

		foreach (Entity candidate in world.Query<Position, Health>())
		{
			if (candidate.Id == caster.Id) continue;
			if (world.Has<Dead>(candidate)) continue;
			if (world.Get<Health>(candidate).Current <= 0f) continue;

			int candidatePlayer = AutoCastPlayerOf(candidate);
			if (candidatePlayer <= 0) continue;
			if (ArePlayersAllied(casterPlayer, candidatePlayer)) continue;

			float distanceSquared = Vector3.DistanceSquared(world.Get<Position>(candidate).Value, casterPosition);
			if (distanceSquared > bestDistanceSquared) continue;

			best = candidate;
			bestDistanceSquared = distanceSquared;
		}

		if (best == Entity.Null) return false;

		targetPosition = world.Get<Position>(best).Value;
		return true;
	}

	private bool TryFindLowestHealthAlly(Entity caster, Vector3 casterPosition, AbilityDefinition definition, out Vector3 targetPosition, bool byHealthFraction)
	{
		targetPosition = casterPosition;

		EcsWorld world = _context.World;
		float searchRadius = ResolveAutoCastSearchRadius(definition);
		float searchRadiusSquared = searchRadius * searchRadius;
		float healthFractionCeiling = Math.Clamp(definition.AutoCastAllyHealthPercentBelow, 0f, 1f);
		int casterPlayer = AutoCastPlayerOf(caster);
		Entity best = Entity.Null;
		float bestReading = float.MaxValue;

		foreach (Entity candidate in world.Query<Position, Health>())
		{
			if (candidate.Id == caster.Id) continue;
			if (world.Has<Dead>(candidate) || world.Has<Building>(candidate)) continue;
			if (!world.TryGet<Health>(candidate, out Health health)) continue;
			if (health.Current <= 0f || health.Max <= 0f) continue;

			int candidatePlayer = AutoCastPlayerOf(candidate);
			if (candidatePlayer <= 0) continue;
			if (!ArePlayersAllied(casterPlayer, candidatePlayer)) continue;

			float healthFraction = health.Current / health.Max;
			if (healthFraction > healthFractionCeiling) continue;

			float distanceSquared = Vector3.DistanceSquared(world.Get<Position>(candidate).Value, casterPosition);
			if (distanceSquared > searchRadiusSquared) continue;

			float reading = byHealthFraction ? healthFraction : health.Current;
			if (reading >= bestReading) continue;

			best = candidate;
			bestReading = reading;
		}

		if (best == Entity.Null) return false;

		targetPosition = world.Get<Position>(best).Value;
		return true;
	}

	private bool TryFindAllyWithoutEffect(Entity caster, Vector3 casterPosition, AbilityDefinition definition, out Vector3 targetPosition)
	{
		targetPosition = casterPosition;

		string? buffId = FirstAutoCastBuffId(definition);
		if (string.IsNullOrEmpty(buffId)) return false;

		EcsWorld world = _context.World;
		float searchRadius = ResolveAutoCastSearchRadius(definition);
		float bestDistanceSquared = searchRadius * searchRadius;
		int casterPlayer = AutoCastPlayerOf(caster);
		Entity best = Entity.Null;

		foreach (Entity candidate in world.Query<Position, Health>())
		{
			if (world.Has<Dead>(candidate) || world.Has<Building>(candidate)) continue;
			if (!world.TryGet<Health>(candidate, out Health health)) continue;
			if (health.Current <= 0f) continue;

			int candidatePlayer = AutoCastPlayerOf(candidate);
			if (candidatePlayer <= 0) continue;
			if (!ArePlayersAllied(casterPlayer, candidatePlayer)) continue;
			if (UnitOrderService.HasBuff(world, candidate, buffId)) continue;

			float distanceSquared = Vector3.DistanceSquared(world.Get<Position>(candidate).Value, casterPosition);
			if (distanceSquared > bestDistanceSquared) continue;

			best = candidate;
			bestDistanceSquared = distanceSquared;
		}

		if (best == Entity.Null) return false;

		targetPosition = world.Get<Position>(best).Value;
		return true;
	}

	private static string? FirstAutoCastBuffId(AbilityDefinition definition)
	{
		List<AbilityEffectDefinition> effects = definition.Effects;
		for (int index = 0; index < effects.Count; index++)
		{
			if (!string.IsNullOrEmpty(effects[index].BuffId)) return effects[index].BuffId;
		}

		return null;
	}

	/// <summary>
	///     Turns a gather effect into the order the caster already understands: the nearest resource node to the point the
	///     ability was aimed at, as long as it stands inside the radius the effect states. This is the same
	///     <see cref="UnitOrderService.Gather" /> path a right-click on the mine takes, so a hero and a worker gather
	///     through one rule.
	/// </summary>
	private void RequestGatherAround(Entity caster, Vector3 position, float radius)
	{
		EcsWorld world = _context.World;
		Entity nearest = Entity.Null;
		float nearestDistanceSquared = radius * radius;

		foreach (Entity node in world.Query<ResourceNode>())
		{
			if (world.Has<Dead>(node)) continue;
			if (!world.TryGet<Position>(node, out Position nodePosition)) continue;

			float distanceSquared = Vector3.DistanceSquared(nodePosition.Value, position);
			if (distanceSquared > nearestDistanceSquared) continue;

			nearest = node;
			nearestDistanceSquared = distanceSquared;
		}

		if (nearest == Entity.Null) return;

		UnitOrderService.Gather(world, caster, nearest, ReadResourceNodeType(nearest));
	}

	private int AutoCastPlayerOf(Entity entity)
	{
		EcsWorld world = _context.World;
		if (world.TryGet<UnitOwnerPlayer>(entity, out UnitOwnerPlayer unitOwner)) return unitOwner.PlayerIndex;
		if (world.TryGet<Owner>(entity, out Owner owner) && _context.PlayerSlotForEntity(owner.PlayerEntity.Value) is int slot && slot >= 0) return slot;

		return 0;
	}
}
