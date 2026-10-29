using System;
using System.Numerics;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Resources;
using Realm.Ecs.Components.Stats;
using Realm.Ecs.Components.Tags;

namespace Realm.Ecs.Services;

/// <summary>
///     The orders, statuses and per-unit bookkeeping a unit can be given. The authoritative process, the client's own
///     command path and a map script all call these, so an order a script issues, an order a player clicks and an
///     order a client draws a route for are the same components with the same values.
/// </summary>
public static class UnitOrderService
{
	/// <summary>The custom data key a routed unit stores its route state under.</summary>
	public const string RouteStateKey = "__routeState";

	/// <summary>
	///     Drops every intent a unit carries, which is what stopping means: nothing left to walk to, shoot at, follow,
	///     gather or build.
	/// </summary>
	public static void ClearOrders(EcsWorld world, Entity entity)
	{
		if (!world.IsAlive(entity)) return;

		RemoveIfPresent<MoveTo>(world, entity);
		RemoveIfPresent<PathFollow>(world, entity);
		RemoveIfPresent<AttackTarget>(world, entity);
		RemoveIfPresent<AttackMove>(world, entity);
		RemoveIfPresent<HoldPosition>(world, entity);
		RemoveIfPresent<Follow>(world, entity);
		RemoveIfPresent<Patrol>(world, entity);
		RemoveIfPresent<HealingTarget>(world, entity);
		RemoveIfPresent<WaypointQueue>(world, entity);
		RemoveIfPresent<Gatherer>(world, entity);
		RemoveIfPresent<BuildTask>(world, entity);
		RemoveIfPresent<BuildQueue>(world, entity);
	}

	/// <summary>Sends a unit to a place, dropping whatever else it was doing.</summary>
	public static void MoveTo(EcsWorld world, Entity entity, Vector3 destination)
	{
		if (!CanTakeOrders(world, entity)) return;

		RemoveIfPresent<HoldPosition>(world, entity);
		RemoveIfPresent<AttackMove>(world, entity);
		RemoveIfPresent<AttackTarget>(world, entity);
		world.SetOrAdd(entity, new MoveTo(destination));
	}

	/// <summary>Sends a unit to a place while it engages whatever it meets on the way.</summary>
	public static void AttackMove(EcsWorld world, Entity entity, Vector3 destination)
	{
		if (!CanTakeOrders(world, entity)) return;

		RemoveIfPresent<HoldPosition>(world, entity);
		RemoveIfPresent<AttackTarget>(world, entity);
		world.SetOrAdd(entity, new AttackMove(destination));
		world.SetOrAdd(entity, new MoveTo(destination));
	}

	/// <summary>Points a unit's attack at a target.</summary>
	public static void Attack(EcsWorld world, Entity entity, Entity target)
	{
		if (!CanTakeOrders(world, entity)) return;
		if (target == Entity.Null || !world.IsAlive(target)) return;

		world.SetOrAdd(entity, new AttackTarget(target));
	}

	/// <summary>Sends a worker to a resource node.</summary>
	public static void Gather(EcsWorld world, Entity entity, Entity resourceNode, string resourceType)
	{
		if (!CanTakeOrders(world, entity)) return;
		if (resourceNode == Entity.Null || !world.IsAlive(resourceNode)) return;

		world.SetOrAdd(entity, new Gatherer(resourceType, resourceNode));
	}

	/// <summary>Stops a unit where it stands without holding it there.</summary>
	public static void Stop(EcsWorld world, Entity entity) => ClearOrders(world, entity);

	/// <summary>Stops a unit and tells it to stay put instead of chasing what it meets.</summary>
	public static void HoldPosition(EcsWorld world, Entity entity)
	{
		if (!CanTakeOrders(world, entity)) return;

		ClearOrders(world, entity);
		if (!world.Has<HoldPosition>(entity)) world.Add(entity, default(HoldPosition));
	}

	/// <summary>Puts a unit at a place without asking the movement step to walk it there.</summary>
	public static void Teleport(EcsWorld world, Entity entity, Vector3 position)
	{
		if (!world.IsAlive(entity)) return;

		world.SetOrAdd(entity, new Position(position));
		world.SetOrAdd(entity, new PreviousPosition(position));
		RemoveIfPresent<PathFollow>(world, entity);
	}

	/// <summary>
	///     Puts a timed status on a unit: the two status tables the match advances and the buff pipeline that folds the
	///     buff's registered modifiers into the unit's stats.
	/// </summary>
	public static void ApplyBuff(EcsWorld world, Entity entity, string buffId, float duration)
	{
		if (!world.IsAlive(entity) || string.IsNullOrEmpty(buffId)) return;

		if (world.Has<Buffs>(entity))
		{
			ref Buffs buffs = ref world.Get<Buffs>(entity);
			buffs.Set(buffId, duration, duration);
		}
		else
		{
			var buffs = new Buffs(null);
			buffs.Set(buffId, duration, duration);
			world.Add(entity, buffs);
		}

		if (world.Has<BuffState>(entity))
		{
			ref BuffState buffState = ref world.Get<BuffState>(entity);
			buffState.Set(buffId, duration, duration);
		}
		else
		{
			var buffState = new BuffState(null);
			buffState.Set(buffId, duration, duration);
			world.Add(entity, buffState);
		}

		BuffService.Apply(world, entity, buffId, duration);
	}

	/// <summary>Drops a status from a unit and with it the modifiers the status was contributing.</summary>
	public static void RemoveBuff(EcsWorld world, Entity entity, string buffId)
	{
		if (!world.IsAlive(entity) || string.IsNullOrEmpty(buffId)) return;

		if (world.Has<Buffs>(entity))
		{
			ref Buffs buffs = ref world.Get<Buffs>(entity);
			buffs.Remove(buffId);
		}

		if (world.Has<BuffState>(entity))
		{
			ref BuffState buffState = ref world.Get<BuffState>(entity);
			buffState.Remove(buffId);
		}

		BuffService.Remove(world, entity, buffId);
	}

	/// <summary>Whether a unit currently carries a status.</summary>
	public static bool HasBuff(EcsWorld world, Entity entity, string buffId)
	{
		if (!world.IsAlive(entity) || string.IsNullOrEmpty(buffId)) return false;

		if (world.Has<BuffState>(entity))
		{
			ref BuffState buffState = ref world.Get<BuffState>(entity);
			return buffState.TryGetValue(buffId, out _);
		}

		if (world.Has<Buffs>(entity))
		{
			ref Buffs buffs = ref world.Get<Buffs>(entity);
			return buffs.TryGetValue(buffId, out _);
		}

		return false;
	}

	/// <summary>
	///     Adds one stat modifier under a source, so everything one source granted can be taken back together. A
	///     modifier added this way lasts until its source removes it.
	/// </summary>
	public static void AddStatModifier(EcsWorld world, Entity entity, string statName, bool isPercentage, float value, string sourceId)
	{
		if (!world.IsAlive(entity) || string.IsNullOrWhiteSpace(statName)) return;

		if (!world.Has<ModifierState>(entity)) world.Add(entity, new ModifierState(null));

		ref ModifierState modifierState = ref world.Get<ModifierState>(entity);
		modifierState.Add(new StatModifier(
			statTypeId: new StatId(statName),
			type: isPercentage ? ModifierType.Percentage : ModifierType.Flat,
			value: value,
			duration: -1f,
			sourceUpgradeId: sourceId ?? string.Empty));

		AttributeStatCalculator.RecalculateEntityStats(world, entity);
	}

	/// <summary>Drops every stat modifier one source granted and recalculates the unit's stats.</summary>
	public static bool RemoveStatModifiers(EcsWorld world, Entity entity, string sourceId)
	{
		if (!world.IsAlive(entity) || string.IsNullOrWhiteSpace(sourceId)) return false;
		if (!world.Has<ModifierState>(entity)) return false;

		ref ModifierState modifierState = ref world.Get<ModifierState>(entity);
		bool removed = false;
		for (int index = modifierState.Count - 1; index >= 0; index--)
		{
			if (!string.Equals(modifierState.Get(index).SourceUpgradeId, sourceId, StringComparison.OrdinalIgnoreCase)) continue;

			modifierState.RemoveAt(index);
			removed = true;
		}

		if (removed) AttributeStatCalculator.RecalculateEntityStats(world, entity);
		return removed;
	}

	/// <summary>How long a unit must wait before it can use an ability again.</summary>
	public static float GetAbilityCooldownRemaining(EcsWorld world, Entity entity, string abilityId)
	{
		if (!world.IsAlive(entity) || string.IsNullOrEmpty(abilityId)) return 0f;

		if (world.Has<Cooldowns>(entity))
		{
			ref Cooldowns cooldowns = ref world.Get<Cooldowns>(entity);
			if (cooldowns.TryGetValue(abilityId, out float remaining)) return remaining;
		}

		if (world.Has<SpellCooldowns>(entity))
		{
			ref SpellCooldowns spellCooldowns = ref world.Get<SpellCooldowns>(entity);
			if (spellCooldowns.TryGetValue(abilityId, out float remaining)) return remaining;
		}

		return 0f;
	}

	/// <summary>Puts an ability on cooldown, in both cooldown tables the match advances.</summary>
	public static void SetAbilityCooldown(EcsWorld world, Entity entity, string abilityId, float cooldown)
	{
		if (!world.IsAlive(entity) || string.IsNullOrEmpty(abilityId)) return;

		if (world.Has<Cooldowns>(entity))
		{
			ref Cooldowns cooldowns = ref world.Get<Cooldowns>(entity);
			cooldowns.Set(abilityId, cooldown, cooldown);
		}
		else
		{
			var cooldowns = new Cooldowns(null);
			cooldowns.Set(abilityId, cooldown, cooldown);
			world.Add(entity, cooldowns);
		}

		if (world.Has<SpellCooldowns>(entity))
		{
			ref SpellCooldowns spellCooldowns = ref world.Get<SpellCooldowns>(entity);
			spellCooldowns.Set(abilityId, cooldown, cooldown);
		}
		else
		{
			var spellCooldowns = new SpellCooldowns(null);
			spellCooldowns.Set(abilityId, cooldown, cooldown);
			world.Add(entity, spellCooldowns);
		}
	}

	/// <summary>Stores a script's own value on a unit.</summary>
	public static void SetCustomData(EcsWorld world, Entity entity, string key, object value)
	{
		if (!world.IsAlive(entity) || string.IsNullOrEmpty(key)) return;

		if (world.Has<CustomMetadata>(entity))
		{
			ref CustomMetadata metadata = ref world.Get<CustomMetadata>(entity);
			metadata.Value[key] = value;
			return;
		}

		world.Add(entity, new CustomMetadata(new System.Collections.Generic.Dictionary<string, object> { [key] = value }));
	}

	/// <summary>Reads back a value a script stored on a unit.</summary>
	public static bool TryGetCustomData(EcsWorld world, Entity entity, string key, out object? value)
	{
		value = null;
		if (!world.IsAlive(entity) || string.IsNullOrEmpty(key)) return false;
		if (!world.Has<CustomMetadata>(entity)) return false;

		ref CustomMetadata metadata = ref world.Get<CustomMetadata>(entity);
		return metadata.Value.TryGetValue(key, out value);
	}

	/// <summary>Whether a script ever stored a value under a key.</summary>
	public static bool HasCustomData(EcsWorld world, Entity entity, string key) => TryGetCustomData(world, entity, key, out _);

	/// <summary>Drops one script-stored value.</summary>
	public static bool RemoveCustomData(EcsWorld world, Entity entity, string key)
	{
		if (!world.IsAlive(entity) || string.IsNullOrEmpty(key)) return false;
		if (!world.Has<CustomMetadata>(entity)) return false;

		ref CustomMetadata metadata = ref world.Get<CustomMetadata>(entity);
		return metadata.Value.Remove(key);
	}

	/// <summary>The route a script put a unit on, or zero when it is on none.</summary>
	public static int GetRouteState(EcsWorld world, Entity entity)
	{
		if (!TryGetCustomData(world, entity, RouteStateKey, out object? value)) return 0;
		return value switch
		{
			int state => state,
			float state => (int)state,
			double state => (int)state,
			string text when int.TryParse(text, out int parsed) => parsed,
			_ => 0
		};
	}

	/// <summary>Puts a unit on a route.</summary>
	public static void SetRouteState(EcsWorld world, Entity entity, int state) => SetCustomData(world, entity, RouteStateKey, state);

	private static bool CanTakeOrders(EcsWorld world, Entity entity)
	{
		if (!world.IsAlive(entity)) return false;
		return !world.Has<Dead>(entity);
	}

	private static void RemoveIfPresent<TComponent>(EcsWorld world, Entity entity) where TComponent : unmanaged
	{
		if (world.Has<TComponent>(entity)) world.Remove<TComponent>(entity);
	}
}
