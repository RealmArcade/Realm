// Ability introspection for the command card: is it hidden, what icon and tooltip does it get, is the
// caster alive, what does it cost, is it disabled, on cooldown or out of mana, and what callback fires.
// Every one of these asks the same host/wasm-runtime pair, which is why they read as one block.

using System;
using System.Collections.Generic;
using System.Text;
using Realm.Ecs;
using Godot;
using Realm.Client.Core;
using Realm.Client.Network;
using Realm.Client.Services;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Tags;
using Realm.Ecs.Services;
using Realm.Client.Utils;
using Realm.Shared.Abilities;


namespace Realm.Client.UI.InGame;

public partial class CommandPanel
{
	private bool IsAbilityHidden(GameHost? host, Unit_WasmRuntime? unitWrapper, string abilityId)
	{
		if (host == null) return false;
		return host.IsAbilityHiddenForUnit(unitWrapper, abilityId);
	}

	private string ResolveAbilityIcon(string abilityId, AbilityDefinition? abilityDef)
	{
		if (abilityDef != null && !string.IsNullOrEmpty(abilityDef.IconPath))
		{
			return abilityDef.IconPath;
		}

		return GetDefaultAbilityIcon(abilityId);
	}

	private string ResolveAbilityTooltip(string abilityId, AbilityDefinition? abilityDef, bool disabled, float manaCost)
	{
		// The description is map text, so it is looked up in the player's catalog before its placeholders are resolved:
		// a translation keeps the {{tokens}} it was given, and TooltipTemplate fills them in on the way to the tooltip.
		string body = abilityDef != null && !string.IsNullOrEmpty(abilityDef.TooltipTemplate)
			? MapText.TranslateTemplate(abilityDef.TooltipTemplate)
			: GetDefaultAbilityTooltip(abilityId);

		return UIHelper.PrependAbilityHeader(abilityId, abilityDef, body, manaCost, disabled);
	}

	/// <summary>
	///     Whether the map script has disabled this ability for the casting unit through SetAbilityState, as opposed to a
	///     cooldown or mana shortfall.
	/// </summary>
	private static bool IsAbilityStateDisabled(Entity casterEntity, string abilityId)
	{
		if (casterEntity == Entity.Null) return false;
		return GameContext.RegistryService.IsAbilityDisabledForUnit(new UnitId(casterEntity.Id), abilityId);
	}

	private Unit_WasmRuntime? GetUnitWrapperIfAlive(GameHost? host, Entity casterEntity)
	{
		if (host == null || casterEntity == Entity.Null) return null;
		return GameContext.World is { } world && world.IsAlive(casterEntity) ? GameContext.SimulationService.GetUnitWrapper(casterEntity) : null;
	}

	private float GetAbilityManaCost(GameHost? host, Unit_WasmRuntime? unitWrapper, AbilityDefinition? abilityDef, string abilityId)
	{
		return host != null ? host.GetAbilityManaCostForUnit(unitWrapper, abilityId) : (abilityDef?.ManaCost ?? 0f);
	}

	private HotkeySpec ParseHotkey(string? hotkeyString)
	{
		if (string.IsNullOrWhiteSpace(hotkeyString)) return HotkeySpec.None;

		if (HotkeySpec.TryParse(hotkeyString, out HotkeySpec hotkey, out string errorMessage))
		{
			return hotkey;
		}

		GameLog.LogError($"[CommandPanel] Rejected hotkey '{hotkeyString}': {errorMessage}");
		return HotkeySpec.None;
	}

	private Action GetAbilityCallback(string abilityId, bool isInstant, bool isPassive)
	{
		// A passive card is a description with an icon: there is no cast to start, so pressing it is deliberately inert
		// rather than entering targeting or being greyed out like an unaffordable active.
		if (isPassive) return static () => { };
		if (isInstant) return () => GameContext.Host?.CastInstantAbility(abilityId);
		return () => GameContext.Host?.EnterSpellTargeting(abilityId);
	}

	private bool IsAbilityDisabled(GameHost? host, Unit_WasmRuntime? unitWrapper, Entity casterEntity, string abilityId, float manaCost)
	{
		if (host == null) return false;

		// A unit the map drives keeps its cards, so a player can read what the army does, but the cards are disabled:
		// casting is the map's decision for that unit, and a click here would either be refused or fight the script.
		if (GameContext.World != null && casterEntity != Entity.Null && GameContext.World.Has<PlayerOrdersLocked>(casterEntity)) return true;

		if (GameContext.RegistryService.IsAbilityDisabledForUnit(unitWrapper, abilityId)) return true;

		int casterPlayer = GameContext.World != null && GameContext.World.IsAlive(casterEntity) && GameContext.World.Has<UnitOwnerPlayer>(casterEntity)
			? GameContext.World.Get<UnitOwnerPlayer>(casterEntity).PlayerIndex
			: GameContext.PerspectiveFilter.ControlledPlayerIndex;
		if (host.GetPlayerSpellCooldown(casterPlayer, abilityId) > 0f) return true;

		var world = GameContext.World;
		if (world == null || casterEntity == Entity.Null || !world.IsAlive(casterEntity)) return false;

		return IsAbilityOnCooldownOrOOM(world, casterEntity, abilityId, manaCost);
	}

	private bool IsAbilityOnCooldownOrOOM(EcsWorld world, Entity casterEntity, string abilityId, float manaCost)
	{
		if (world.Has<Mana>(casterEntity))
		{
			if (world.Get<Mana>(casterEntity).Current < manaCost) return true;
		}

		if (world.Has<Cooldowns>(casterEntity))
		{
			ref var cooldowns = ref world.Get<Cooldowns>(casterEntity);
			if (cooldowns.TryGetValue(abilityId, out float cooldown) && cooldown > 0f) return true;
		}

		if (world.Has<SpellCooldowns>(casterEntity))
		{
			ref var spellCooldowns = ref world.Get<SpellCooldowns>(casterEntity);
			if (spellCooldowns.TryGetValue(abilityId, out float spellCooldown) && spellCooldown > 0f) return true;
		}

		return false;
	}

	private string GetUnitIcon(string unitId)
	{
		return "res://Assets/UI/unit_placeholder.webp";
	}
}
