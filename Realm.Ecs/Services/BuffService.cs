using System;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Stats;

namespace Realm.Ecs.Services;

/// <summary>
/// Bridges a timed buff onto the one stat modifier pipeline. A buff with registered modifiers
/// contributes them while the buff is active, tagged with the buff as their source so refreshing
/// or removing the buff changes only that buff's contribution.
/// </summary>
public static class BuffService
{
	public const string ModifierSourcePrefix = "buff:";

	public static string ModifierSource(string buffId) => ModifierSourcePrefix + buffId;

	public static bool HasRegisteredModifiers(string? buffId)
	{
		return !string.IsNullOrEmpty(buffId)
			&& BuffRegistry.BuffModifiers.TryGetValue(buffId, out var modifiers)
			&& modifiers.Count > 0;
	}

	/// <summary>
	/// Adds (or refreshes) the buff's stat contribution and recalculates the entity.
	/// A duration of zero or less marks the contribution permanent for as long as the buff exists.
	/// </summary>
	public static void Apply(EcsWorld world, Entity entity, string buffId, float duration)
	{
		if (!world.IsAlive(entity) || !HasRegisteredModifiers(buffId)) return;

		if (!world.Has<ModifierState>(entity))
		{
			world.Add(entity, new ModifierState(null));
		}

		ref var state = ref world.Get<ModifierState>(entity);
		string source = ModifierSource(buffId);
		RemoveBySource(ref state, source);

		var modifiers = BuffRegistry.BuffModifiers[buffId];
		for (int i = 0; i < modifiers.Count; i++)
		{
			var modifier = modifiers[i];
			state.Add(new StatModifier(
				modifier.StatTypeId,
				modifier.Type,
				modifier.Value,
				duration,
				string.Empty,
				1,
				source));
		}

		AttributeStatCalculator.RecalculateEntityStats(world, entity);
	}

	/// <summary>
	/// Drops the buff's stat contribution immediately, for example when a dispel removes it.
	/// Returns true when a contribution was actually removed.
	/// </summary>
	public static bool Remove(EcsWorld world, Entity entity, string buffId)
	{
		if (!world.IsAlive(entity) || !world.Has<ModifierState>(entity)) return false;

		ref var state = ref world.Get<ModifierState>(entity);
		if (!RemoveBySource(ref state, ModifierSource(buffId))) return false;

		AttributeStatCalculator.RecalculateEntityStats(world, entity);
		return true;
	}

	private static bool RemoveBySource(ref ModifierState state, string source)
	{
		bool removed = false;
		for (int i = state.Count - 1; i >= 0; i--)
		{
			if (!string.Equals(state.Get(i).SourceUpgradeId, source, StringComparison.OrdinalIgnoreCase)) continue;

			state.RemoveAt(i);
			removed = true;
		}

		return removed;
	}
}
