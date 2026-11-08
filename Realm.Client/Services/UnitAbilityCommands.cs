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
internal sealed class UnitAbilityCommands
{
	private readonly Func<UnitId, Entity> _entityOf;
	private readonly Func<UnitId, Unit_WasmRuntime?> _wrapperOf;

	internal UnitAbilityCommands(Func<UnitId, Entity> entityOf, Func<UnitId, Unit_WasmRuntime?> wrapperOf)
	{
		_entityOf = entityOf;
		_wrapperOf = wrapperOf;
	}

	internal void SetAbilityAutoCast(UnitId unit, string abilityId, bool active)
	{
		var entity = _entityOf(unit);
		if (entity != Entity.Null && SimulationService.World.IsAlive(entity))
		{
			var wrapper = _wrapperOf(unit);
			string unitTypeId = wrapper?.UnitId ?? string.Empty;
			var abilities = GameContext.RegistryService.GetUnitTypeAbilities(unitTypeId);
			int slotIdx = -1;
			for (int i = 0; i < abilities.Count; i++)
			{
				if (string.Equals(abilities[i], abilityId, StringComparison.OrdinalIgnoreCase) ||
					string.Equals(Path.GetFileNameWithoutExtension(abilities[i]), Path.GetFileNameWithoutExtension(abilityId), StringComparison.OrdinalIgnoreCase))
				{
					slotIdx = i;
					break;
				}
			}

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
	}

	internal void EnableUnitAutoCombat(UnitId unit, bool enabled)
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

	internal void CastAbility(UnitId unit, string abilityId, Vector3 targetPosition, AbilityDefinition def, Action<UnitId, string, Vector3>? onSpellCast, Action<AbilityDefinition, Godot.Vector3>? spawnFx)
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

	internal float GetAbilityCooldown(UnitId unit, string abilityId)
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

	internal void SetAbilityCooldown(UnitId unit, string abilityId, float cooldown)
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

	internal void SetAbilityCooldown(Unit_WasmRuntime unit, string abilityId, float cooldown)
	{
		SetAbilityCooldown((UnitId)unit.UniqueId, abilityId, cooldown);
	}

	internal void CastAbility(Unit_WasmRuntime unit, string abilityId, Vector3 targetPosition, AbilityDefinition def, Action<UnitId, string, Vector3>? onSpellCast, Action<AbilityDefinition, Godot.Vector3>? spawnFx)
	{
		CastAbility((UnitId)unit.UniqueId, abilityId, targetPosition, def, onSpellCast, spawnFx);
	}
}
