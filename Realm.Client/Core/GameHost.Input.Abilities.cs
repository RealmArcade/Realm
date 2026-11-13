using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Realm.Ecs;
using Godot;
using Godot.Collections;
using Realm.Client.Network;
using Realm.Client.ReplaySystem;
using Realm.Client.Services;
using Realm.Client.UI;
using Realm.Client.UI.MapEditor;
using Realm.Client.VFX;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Resources;
using Realm.Ecs.Components.Tags;
using Realm.Ecs.Components.Terrain;
using Realm.Ecs.Services;
using Array = System.Array;
using Realm.Shared.Abilities;

namespace Realm.Client.Core;
public partial class GameHost
{
	public void CastSpellAt(string spellId, Vector3 position)
	{
		ExecuteSpellCast(spellId, position);
	}

	public void PlaceBuildingAt(string type, Vector3 position)
	{
		ExecuteBuildingPlacement(type, position);
	}

	public void EnterSpellTargeting(string spellId)
	{
		if (GameContext.PerspectiveFilter.IsSpectating) return;
		ActiveSpellTargeting = spellId;
		Input.SetDefaultCursorShape(Input.CursorShape.Cross);

		if (GameContext.InGameHud != null)
		{
			Hud.ShowFeedbackText($"Casting: Select Location for {spellId.ToUpper()}", new Color(1f, 0.7f, 0.1f));
		}
	}

	private void ExecuteSpellCast(string spellId, Vector3 position)
	{
		if (GameContext.PerspectiveFilter.IsSpectating) return;

		// A card that sells a consumable is not a spell to resolve: it is a purchase order aimed at the unit under the
		// cursor, which only the authority can price and attach. Offered here so a client buys it too, not just a host.
		if (ExecuteConsumablePurchase(GetFocusedSelectedUnitId(), spellId, position)) return;

		var def = GetAbilityDefinition(spellId);

		if (_multiplayerActive && !Multiplayer.IsServer())
		{
			ExecuteClientMultiplayerSpellCast(spellId, def, position);
			return;
		}

		ExecuteLocalSpellCast(spellId, def, position);
	}

	private Unit3D? GetFocusedSelectedUnit()
	{
		int focusedIdx = Math.Clamp(CycleSelectionIndex, 0, Math.Max(0, SelectedUnits.Count - 1));
		return SelectedUnits.Count > focusedIdx ? SelectedUnits[focusedIdx] : null;
	}

	private UnitId GetFocusedSelectedUnitId()
	{
		Unit3D? focusedUnit = GetFocusedSelectedUnit();
		return focusedUnit != null && World.IsAlive(focusedUnit.Entity)
			? (UnitId)focusedUnit.Entity.Id
			: UnitId.Invalid;
	}

	private void ExecuteClientMultiplayerSpellCast(string spellId, AbilityDefinition? def, Vector3 position)
	{
		if (def != null && def.Cooldown > 0f)
		{
			GameContext.InputService.SetSpellCooldown(GetPlayerEntityForPlayerIndex(GameContext.PerspectiveFilter.ControlledPlayerIndex), spellId, def.Cooldown);
		}

		if (def != null)
		{
			GameContext.FXService.SpawnAbilityEffect(this, def, position);
		}

		var targetIds = new List<int>();
		if (SelectedUnits.Count > 0 && GameContext.PerspectiveFilter.CanControl(SelectedUnits[0].Player))
		{
			targetIds.Add(GetServerEntityId(SelectedUnits[0].Entity));
		}

		SubmitRemoteCommand("spell", targetIds, position, 0, spellId);
	}

	private void ExecuteLocalSpellCast(string spellId, AbilityDefinition? def, Vector3 position)
	{
		Unit3D? focusedUnit = GetFocusedSelectedUnit();

		if (_multiplayerActive && focusedUnit != null && !GameContext.PerspectiveFilter.CanControl(focusedUnit.Player)) return;

		bool hasCaster = focusedUnit != null && World.IsAlive(focusedUnit.Entity);
		UnitId caster = UnitId.Invalid;
		Entity casterEntity = Entity.Null;
		if (hasCaster && focusedUnit != null)
		{
			caster = (UnitId)focusedUnit.Entity.Id;
			casterEntity = focusedUnit.Entity;
		}

		// Affordability and cooldown are settled before the script hears about the cast, matching the authoritative server
		// path. Raising OnSpellCast first let a refused cast still run its map-side effects and left a cooldown behind.
		if (IsSpellOnCooldown(spellId, def)) return;

		if (!GameContext.InputService.TryExecuteSpellCast(GetPlayerEntityForPlayerIndex(GameContext.PerspectiveFilter.ControlledPlayerIndex), casterEntity, spellId, out float maxCd))
		{
			ShowInsufficientManaFeedback(spellId, def);
			return;
		}

		if (hasCaster && focusedUnit != null)
		{
			GameContext.AudioService?.PlayUnitSound(focusedUnit.UnitId, UnitSoundEvent.SpellCast, position, 0f, AudioPriorityTier.Critical);
		}

		OnSpellCast?.Invoke(caster, spellId, new System.Numerics.Vector3(position.X, position.Y, position.Z));

		HandleSpellCastSuccess(spellId, def, position);
	}

	private void ShowInsufficientManaFeedback(string spellId, AbilityDefinition? def)
	{
		float cost = def?.ManaCost ?? 0f;
		if (cost <= 0f) return;

		string displayName = def?.DisplayName ?? spellId;
		GameContext.InGameHud?.ShowFeedbackText($"{displayName} requires {(int)cost} mana", new Color(0.9f, 0.4f, 0.1f));
	}

	private bool IsSpellOnCooldown(string spellId, AbilityDefinition? def)
	{
		float cd = GameContext.InputService.GetSpellCooldown(GetPlayerEntityForPlayerIndex(GameContext.PerspectiveFilter.ControlledPlayerIndex), spellId);
		if (cd <= 0f) return false;

		string displayName = def?.DisplayName ?? spellId;
		GameContext.InGameHud?.ShowFeedbackText($"{displayName} on cooldown: {cd:F1}s remaining", new Color(0.9f, 0.4f, 0.1f));
		return true;
	}

	private void HandleSpellCastSuccess(string spellId, AbilityDefinition? def, Vector3 position)
	{
		string displayName = def?.DisplayName ?? spellId;
		if (GameContext.InGameHud != null)
		{
			Hud.ShowFeedbackText($"Cast: {displayName}", new Color(0.9f, 0.3f, 0.1f));
			GameContext.UserInterface?.PlayClickSound();
		}

		if (def != null)
		{
			GameContext.FXService.SpawnAbilityEffect(this, def, position);
			var simPos = new System.Numerics.Vector3(position.X, position.Y, position.Z);
			Entity target = SelectedUnits.Count > 0 ? SelectedUnits[0].Entity : Entity.Null;
			GameContext.InputService.ApplySpellEffect(def, simPos, target);
		}

		GameContext.InGameHud?.RefreshUI(SelectedUnits);
	}


	public void CastInstantAbility(string abilityId)
	{
		if (GameContext.PerspectiveFilter.IsSpectating) return;
		if (SelectedUnits.Count == 0 || !World.IsAlive(SelectedUnits[0].Entity)) return;
		if (!GameContext.PerspectiveFilter.CanControl(SelectedUnits[0].Player)) return;

		if (GameContext.InputService.TryGetCasterPosition(SelectedUnits[0].Entity, out var pos))
		{
			ExecuteSpellCast(abilityId, new Vector3(pos.X, pos.Y, pos.Z));
		}
	}

	public void CastScriptVehicleAbility(string abilityId)
	{
		if (MainCamera?.FollowTarget is not Unit3D unit || !World.IsAlive(unit.Entity)) return;
		if (!GameContext.PerspectiveFilter.CanControl(unit.Player)) return;

		var targetIds = new List<int> { GetServerEntityId(unit.Entity) };
		SubmitRemoteCommand("spell", targetIds, unit.GlobalPosition, 0, abilityId);
	}

	public void BuyItem(string itemId, Entity buildingEntity)
	{
		if (GameContext.PerspectiveFilter.IsSpectating) return;
		ResolveItemInfo(itemId, out string itemName, out float costGold);

		if (GameContext.InGameHud == null) return;

		if (Hud.Gold < costGold)
		{
			Hud.ShowFeedbackText("Cannot buy item: Insufficient gold!", new Color(1.0f, 0.2f, 0.2f));
			GameContext.UserInterface?.PlayWarningSound();
			return;
		}

		if (buildingEntity == Entity.Null && SelectedUnits.Count > 0)
		{
			buildingEntity = SelectedUnits[0].Entity;
		}

		if (!TryGetUnit3D(buildingEntity, out var building3D)) return;

		var selectedEntity = SelectedUnits.Count > 0 ? SelectedUnits[0].Entity : Entity.Null;
		var pos = new System.Numerics.Vector3(building3D.GlobalPosition.X, building3D.GlobalPosition.Y, building3D.GlobalPosition.Z);

		if (GameContext.InputService.BuyItem(itemId, GetPlayerEntityForPlayerIndex(GameContext.PerspectiveFilter.ControlledPlayerIndex), pos, selectedEntity, out Entity targetUnitEntity))
		{
			CompleteItemPurchase(itemId, itemName, costGold, targetUnitEntity);
		}
		else
		{
			Hud.ShowFeedbackText("Cannot buy item: No friendly combat units nearby!", new Color(1.0f, 0.2f, 0.2f));
			GameContext.UserInterface?.PlayWarningSound();
		}
	}

	private void ResolveItemInfo(string itemId, out string itemName, out float costGold)
	{
		itemName = itemId;
		costGold = 50f;
		if (ItemRegistry.TryGetValue(itemId, out var itemMeta))
		{
			if (!string.IsNullOrEmpty(itemMeta.Name)) itemName = itemMeta.Name;
			if (itemMeta.CostGold > 0) costGold = itemMeta.CostGold;
		}
	}

	private void CompleteItemPurchase(string itemId, string itemName, float costGold, Entity targetUnitEntity)
	{
		var targetUnit = (GameContext.InputService.IsUnitAlive(targetUnitEntity)) ? GameContext.SimulationService.GetUnitWrapper(targetUnitEntity) : null;
		
		if (GameContext.InGameHud != null)
		{
			Hud.Gold -= costGold;
		}

		if (targetUnit != null)
		{
			OnItemSold?.Invoke((UnitId)targetUnit.Entity.Id, itemId);
		}

		GameContext.InGameHud?.ShowFeedbackText($"Bought {itemName} for {targetUnit?.TemplateID.ToUpper()}!", new Color(0.3f, 0.9f, 0.4f));
		GameContext.UserInterface?.PlayClickSound();
		GameContext.InGameHud?.RefreshUI(SelectedUnits);
	}

	public void UseItem(Unit3D unit, string itemId)
	{
		ResolveItemName(itemId, out string itemName, out var itemMeta);

		if (GameContext.InputService.UseItem(unit.Entity, itemId, out float healedAmount))
		{
			ProcessSuccessfulItemUse(unit, itemName, itemMeta, healedAmount);
		}
		else
		{
			ProcessFailedItemUse(unit);
		}
	}

	private void ResolveItemName(string itemId, out string itemName, out ItemMetadata? itemMeta)
	{
		itemName = itemId;
		if (GameContext.InputService.TryGetItemMeta(itemId, out itemMeta) && !string.IsNullOrEmpty(itemMeta.Name))
		{
			itemName = itemMeta.Name;
		}
	}

	private void ProcessSuccessfulItemUse(Unit3D unit, string itemName, ItemMetadata? itemMeta, float healedAmount)
	{
		GameContext.InGameHud?.ShowFeedbackText($"{unit.UnitId.ToUpper()} used {itemName} (+{healedAmount:F0} HP)!", new Color(0.3f, 0.9f, 0.4f));

		if (itemMeta != null && !string.IsNullOrEmpty(itemMeta.UseAbility) && GameContext.InputService.GetAbilityDefinition(itemMeta.UseAbility) is AbilityDefinition abDef)
		{
			GameContext.FXService.SpawnAbilityEffect(this, abDef, unit.GlobalPosition);
		}

		FlashHealUnit(unit);
		GameContext.FXService.SpawnHealNumber(this, unit.GlobalPosition, healedAmount);

		GameContext.UserInterface?.PlayClickSound();
		GameContext.InGameHud?.RefreshUI(SelectedUnits);
	}

	private void ProcessFailedItemUse(Unit3D unit)
	{
		if (GameContext.InputService.IsUnitAtFullHealth(unit.Entity))
		{
			GameContext.InGameHud?.ShowFeedbackText("Unit is already at full health!", new Color(0.8f, 0.8f, 0.8f));
			GameContext.UserInterface?.PlayWarningSound();
		}
	}
}
