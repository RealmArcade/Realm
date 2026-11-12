using System;
using System.IO;
using Godot;
using Realm.Client.Core;
using Realm.Ecs;
using Realm.Ecs.Components.Abilities;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using Realm.Shared.Abilities;
using UnitId = Realm.Ecs.UnitId;
using Vector3 = System.Numerics.Vector3;

namespace Realm.Client.Services;

/// <summary>
///     The ability buttons a script drives: casting (direct effect or a spawn request), the two cooldown
///     stores the game reads, and the auto-cast and auto-combat toggles that are per-unit state.
/// </summary>
public sealed class UnitAbilityCommands
{
	private readonly Func<UnitId, Entity> _entityOf;
	private readonly Func<UnitId, Unit_WasmRuntime?> _wrapperOf;

	public UnitAbilityCommands(Func<UnitId, Entity> entityOf, Func<UnitId, Unit_WasmRuntime?> wrapperOf)
	{
		_entityOf = entityOf;
		_wrapperOf = wrapperOf;
	}

	public void SetAbilityAutoCast(UnitId unit, string abilityId, bool active)
	{
		var entity = _entityOf(unit);
		if (entity == Entity.Null || !SimulationService.World.IsAlive(entity)) return;

		int slotIdx = ResolveAutoCastSlot(unit, abilityId);
		uint bit = (slotIdx >= 0 && slotIdx < 32) ? (1u << slotIdx) : 1u;
		if (SimulationService.World.Has<AutoCastState>(entity))
		{
			ref var ac = ref SimulationService.World.Get<AutoCastState>(entity);
			if (active) ac.ActiveAbilityBitmask |= bit;
			else ac.ActiveAbilityBitmask &= ~bit;
			ac.Enabled = (ac.ActiveAbilityBitmask != 0);
		}
		else if (active)
		{
			SimulationService.World.Add(entity, new AutoCastState(true, bit));
		}
	}

	/// <summary>
	///     Whether one ability of a unit is currently on auto-cast, which is the state the card draws its marquee from.
	///     The slot is read from the unit's own ability list, because that list is what the authority resolves the order
	///     against.
	/// </summary>
	public bool IsAbilityAutoCast(UnitId unit, string abilityId)
	{
		var entity = _entityOf(unit);
		if (entity == Entity.Null || !SimulationService.World.IsAlive(entity)) return false;
		if (!SimulationService.World.TryGet<AutoCastState>(entity, out AutoCastState autoCast)) return false;

		int slotIdx = ResolveAutoCastSlot(unit, abilityId);
		if (slotIdx < 0 || slotIdx >= 32) return false;

		return (autoCast.ActiveAbilityBitmask & (1u << slotIdx)) != 0;
	}

	private int ResolveAutoCastSlot(UnitId unit, string abilityId)
	{
		if (SimulationService.World.Has<AbilityState>(_entityOf(unit)))
		{
			ref var abilityState = ref SimulationService.World.Get<AbilityState>(_entityOf(unit));
			for (int index = 0; index < abilityState.Count && index < 32; index++)
			{
				string? candidate = abilityState.Get(index);
				if (candidate == null) continue;
				if (string.Equals(candidate, abilityId, StringComparison.OrdinalIgnoreCase) ||
				    string.Equals(Path.GetFileNameWithoutExtension(candidate), Path.GetFileNameWithoutExtension(abilityId), StringComparison.OrdinalIgnoreCase))
				{
					return index;
				}
			}
		}

		var wrapper = _wrapperOf(unit);
		string unitTypeId = wrapper?.UnitId ?? string.Empty;
		var abilities = GameContext.RegistryService.GetUnitTypeAbilities(unitTypeId);
		for (int index = 0; index < abilities.Count; index++)
		{
			if (string.Equals(abilities[index], abilityId, StringComparison.OrdinalIgnoreCase) ||
			    string.Equals(Path.GetFileNameWithoutExtension(abilities[index]), Path.GetFileNameWithoutExtension(abilityId), StringComparison.OrdinalIgnoreCase))
			{
				return index;
			}
		}

		return -1;
	}

	public void EnableUnitAutoCombat(UnitId unit, bool enabled)
	{
		var entity = _entityOf(unit);
		if (entity != Entity.Null && SimulationService.World.IsAlive(entity))
		{
			if (enabled)
			{
				SimulationService.World.SetOrAdd(entity, new AutoCombatState(true));
			}
			else
			{
				if (SimulationService.World.Has<AutoCombatState>(entity))
				{
					SimulationService.World.Remove<AutoCombatState>(entity);
				}
			}
		}
	}

	public void CastAbility(UnitId unit, string abilityId, Vector3 targetPosition, AbilityDefinition def, Action<UnitId, string, Vector3>? onSpellCast, Action<AbilityDefinition, Godot.Vector3>? spawnFx)
	{
		var godotPos = new Godot.Vector3(targetPosition.X, targetPosition.Y, targetPosition.Z);
		var casterEnt = _entityOf(unit);

		if (def != null)
		{
			spawnFx?.Invoke(def, godotPos);
			if (AbilityEffectService.HasEffects(def))
			{
				Vector3 casterPosition = SimulationService.World.IsAlive(casterEnt) && SimulationService.World.Has<Position>(casterEnt)
					? SimulationService.World.Get<Position>(casterEnt).Value
					: targetPosition;

				AbilityEffectService.Execute(casterEnt, casterPosition, def, targetPosition);
			}
			else
			{
				onSpellCast?.Invoke(unit, abilityId, targetPosition);
			}
		}
		else
		{
			onSpellCast?.Invoke(unit, abilityId, targetPosition);
		}
	}

	public float GetAbilityCooldown(UnitId unit, string abilityId)
	{
		var entity = _entityOf(unit);
		if (entity != Entity.Null && SimulationService.World.IsAlive(entity))
		{
			if (SimulationService.World.Has<Cooldowns>(entity))
			{
				var cooldowns = SimulationService.World.Get<Cooldowns>(entity);
				if (cooldowns.TryGetValue(abilityId, out float remaining)) return remaining;
			}

			if (SimulationService.World.Has<SpellCooldowns>(entity))
			{
				var spellCooldowns = SimulationService.World.Get<SpellCooldowns>(entity);
				if (spellCooldowns.TryGetValue(abilityId, out float remaining)) return remaining;
			}
		}

		return 0f;
	}

	public void SetAbilityCooldown(UnitId unit, string abilityId, float cooldown)
	{
		var entity = _entityOf(unit);
		if (entity != Entity.Null && SimulationService.World.IsAlive(entity))
		{
			if (!SimulationService.World.Has<Cooldowns>(entity))
			{
				SimulationService.World.Add(entity, new Cooldowns(null));
			}
			ref var cooldowns = ref SimulationService.World.Get<Cooldowns>(entity);
			cooldowns.Set(abilityId, cooldown, cooldown);

			if (!SimulationService.World.Has<SpellCooldowns>(entity))
			{
				SimulationService.World.Add(entity, new SpellCooldowns(null));
			}
			ref var spellCooldowns = ref SimulationService.World.Get<SpellCooldowns>(entity);
			spellCooldowns.Set(abilityId, cooldown, cooldown);
		}
	}

	public void SetAbilityCooldown(Unit_WasmRuntime unit, string abilityId, float cooldown)
	{
		SetAbilityCooldown((UnitId)unit.UniqueId, abilityId, cooldown);
	}

	public void CastAbility(Unit_WasmRuntime unit, string abilityId, Vector3 targetPosition, AbilityDefinition def, Action<UnitId, string, Vector3>? onSpellCast, Action<AbilityDefinition, Godot.Vector3>? spawnFx)
	{
		CastAbility((UnitId)unit.UniqueId, abilityId, targetPosition, def, onSpellCast, spawnFx);
	}
}
