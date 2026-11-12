// The selected-unit panel refresh. One entry point decides which sub-refresher runs, then each of them
// writes a single group of the panel: basic stats, state text, inventory, production and abilities.

using System;
using System.Collections.Generic;
using Realm.Ecs;
using Godot;
using Realm.Client.Core;
using Realm.Client.Network;
using Realm.Client.ReplaySystem;
using Realm.Client.Services;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Resources;
using Realm.Ecs.Components.Tags;
using Realm.Ecs.Services;
using Realm.Shared.Diagnostics;


namespace Realm.Client.UI.InGame;

public partial class InGameHUDViewModel
{
	private const string StateNoteKey = "state.note";

	public void UpdateSelectedUnits(List<Unit3D>? selectedUnits)
	{
		if (selectedUnits == null)
		{
			SelectedUnits.Clear();
			return;
		}

		SyncSelectedUnitsListSize(selectedUnits.Count);

		for (int i = 0; i < selectedUnits.Count; i++)
		{
			UpdateSingleSelectedUnit(selectedUnits[i], SelectedUnits[i]);
		}
	}

	private void SyncSelectedUnitsListSize(int targetCount)
	{
		while (SelectedUnits.Count > targetCount)
		{
			SelectedUnits.RemoveAt(SelectedUnits.Count - 1);
		}

		while (SelectedUnits.Count < targetCount)
		{
			SelectedUnits.Add(new SelectedUnitInfo());
		}
	}

	private void UpdateSingleSelectedUnit(Unit3D u, SelectedUnitInfo info)
	{
		bool entityChanged = info.Entity != u.Entity;

		InitializeUnitInfo(u, info, entityChanged);

		if (GameContext.Host == null || GameContext.World is not { } world || !world.IsAlive(u.Entity))
			return;

		UpdateUnitBasicStats(world, u, info);
		UpdateUnitStateText(world, u, info);
		UpdateUnitInventory(world, u, info);
		UpdateUnitProduction(world, u, info);

		if (!info.HasProduction && info.ProductionQueue.Count > 0)
		{
			info.ProductionQueue.Clear();
		}

		if (entityChanged || info.Abilities.Count == 0 || DescribedAbilityListChanged(world, u, info))
		{
			UpdateUnitAbilities(world, u, info);
		}
	}

	private void InitializeUnitInfo(Unit3D u, SelectedUnitInfo info, bool entityChanged)
	{
		info.Entity = u.Entity;
		info.UnitId = u.UnitId;
		info.IsEnemy = GameContext.PerspectiveFilter.IsHostileToObserved(u.Player);
		info.Player = u.Player;
		info.IsBuilding = u.IsBuilding;
		info.HasInventory = false;
		info.Name = u.UnitId;
		info.IsUnderConstruction = false;
		info.HasProduction = false;
		info.ProductionTitle = null;
		info.ProductionProgress = 0f;
		info.ProductionMaxProgress = 0f;

		if (entityChanged)
		{
			info.ProductionQueue.Clear();
			info.Abilities.Clear();
			info.AbilityListHandle = 0;
			info.Description = null;
		}

		info.Potions = 0;
		info.StateText = null;
		info.Health = 0f;
		info.MaxHealth = 0f;
		info.Damage = 0f;
		info.Range = 0f;
		info.Armor = 0f;
		info.Speed = 0f;
		info.Dps = 0f;
	}

	private void UpdateUnitBasicStats(EcsWorld world, Unit3D u, SelectedUnitInfo info)
	{
		if (world.Has<Name>(u.Entity))
			info.Name = world.Get<Name>(u.Entity).Value;

		// The description belongs to the portrait's tooltip now, not the stat sheet, and it is available as soon as
		// the unit is known rather than only after the authority has described its ability list.
		if (info.Description == null && GameHost.TryGetUnitOrBuildingMetadata(u.UnitId, out var registryMetadata))
			info.Description = registryMetadata.Description;

		if (u.IsBuilding && world.Has<UnderConstruction>(u.Entity))
			info.IsUnderConstruction = true;

		if (world.Has<Health>(u.Entity))
		{
			var hp = world.Get<Health>(u.Entity);
			info.MaxHealth = hp.Max;
			info.Health = hp.Current;
		}

		if (world.Has<Attack>(u.Entity))
		{
			var atk = world.Get<Attack>(u.Entity);
			info.Damage = atk.Damage;
			info.Range = atk.Range;
			if (atk.Cooldown > 0)
				info.Dps = atk.Damage / atk.Cooldown;
		}

		if (world.Has<Resistances>(u.Entity))
			info.Armor = world.Get<Resistances>(u.Entity).Physical;

		if (world.Has<MovementStats>(u.Entity))
			info.Speed = world.Get<MovementStats>(u.Entity).Speed;
	}

	private void UpdateUnitStateText(EcsWorld world, Unit3D u, SelectedUnitInfo info)
	{
		// A ruin is still a unit a player can select, so it says in its state line how it comes back. The stat sheet has no
		// readings block to carry that sentence any more, and the line is where a player already looks for what a unit is doing.
		if (world.Has<Dormant>(u.Entity))
		{
			info.StateText = TranslationServer.Translate("Destroyed (repair to restore)");
			return;
		}

		info.StateText = GetBaseStateText(world, u);

		// A map states what a unit is doing in its own words when the engine's reading would say nothing useful, which is
		// how a site that has not been committed to a building yet tells every player what the deck has reserved there.
		string? publishedNote = ReadPublishedStateNote(u.Entity);
		if (!string.IsNullOrEmpty(publishedNote))
		{
			info.StateText = string.IsNullOrEmpty(info.StateText) ? publishedNote : $"{info.StateText}\n{publishedNote}";
		}

		if (u.UnitId == "tower" && world.Has<TowerUpgradeLevel>(u.Entity))
		{
			int lvl = world.Get<TowerUpgradeLevel>(u.Entity).Value;
			info.StateText += $"   ★ {TranslationServer.Translate("LVL")} {lvl}";
		}
	}

	/// <summary>
	///     Reads the note a map published for a unit, which is text any player reads on the unit they select rather than
	///     something only its owner sees. Whitespace is treated as no note, so a map clears one by publishing nothing.
	/// </summary>
	/// <param name="entity">The selected unit.</param>
	/// <returns>The note the map published for that unit, or null when it published none.</returns>
	private static string? ReadPublishedStateNote(Entity entity)
	{
		TooltipValueStore? published = GameContext.RegistryService?.TooltipValues;
		if (published == null) return null;
		if (!published.TryGetText(TooltipValueStore.UnitOwnerKey(entity), StateNoteKey, out string note)) return null;
		if (string.IsNullOrWhiteSpace(note)) return null;

		return TranslationServer.Translate(note);
	}

	private string? GetBaseStateText(EcsWorld world, Unit3D u)
	{
		if (world.Has<Gatherer>(u.Entity))
			return GetGathererStateText(world.Get<Gatherer>(u.Entity));

		if (world.Has<BuildTask>(u.Entity))
			return GetBuildTaskStateText(world.Get<BuildTask>(u.Entity));

		return null;
	}

	private string GetGathererStateText(Gatherer gather)
	{
		string cachedDelivering = TranslationServer.Translate("DELIVERING");
		string cachedHarvesting = TranslationServer.Translate("HARVESTING");
		string stateLabel = gather.ReturningToBase ? "● " + cachedDelivering : "● " + cachedHarvesting;
		return $"{stateLabel} ({gather.CarriedAmount:F0} / {gather.MaxCapacity:F0} {TranslationServer.Translate(gather.ResourceType.ToUpper())})";
	}

	private string GetBuildTaskStateText(BuildTask bt)
	{
		string cachedConstructing = TranslationServer.Translate("CONSTRUCTING");
		int pct = (int)(bt.Progress / Mathf.Max(bt.TotalBuildTime, 0.001f) * 100f);
		return $"🔨 {cachedConstructing} ({pct}%)";
	}

	private void UpdateUnitInventory(EcsWorld world, Unit3D u, SelectedUnitInfo info)
	{
		info.InventoryItems.Clear();
		info.CarriedConsumables.Clear();
		info.HasInventory = world.Has<Inventory>(u.Entity);

		if (info.HasInventory)
		{
			var inv = world.Get<Inventory>(u.Entity);
			for (int itemIndex = 0; itemIndex < inv.Count; itemIndex++)
			{
				string? itemId = inv.GetItemId(itemIndex);
				if (itemId == null) continue;
				info.InventoryItems[itemId] = inv.GetCharges(itemIndex);
			}
		}

		if (!world.TryGet<ConsumableAttachment>(u.Entity, out ConsumableAttachment attachment)) return;
		if (!attachment.HasDefinition) return;

		string definitionId = attachment.GetDefinitionId();
		if (definitionId.Length == 0) return;

		info.CarriedConsumables.Add(new CarriedConsumableInfo(definitionId, attachment.GetSlotGroup()));
	}

	private void UpdateUnitProduction(EcsWorld world, Unit3D u, SelectedUnitInfo info)
	{
		if (!info.IsEnemy && world.Has<ConstructionState>(u.Entity))
		{
			var cs = world.Get<ConstructionState>(u.Entity);
			info.HasProduction = true;
			info.ProductionTitle = TranslationServer.Translate("UNDER CONSTRUCTION");
			info.ProductionProgress = cs.Progress;
			info.ProductionMaxProgress = cs.TotalBuildTime;
		}
		else if (!info.IsEnemy && world.Has<ProductionQueue>(u.Entity))
		{
			var prod = world.Get<ProductionQueue>(u.Entity);
			info.HasProduction = true;

			string? trainedUnitId = prod.Get(0);
			if (trainedUnitId != null)
			{
				info.ProductionTitle = string.Format(TranslationServer.Translate("TRAINING: {0}"), trainedUnitId.ToUpper());
				info.ProductionProgress = prod.CurrentProgress;
				info.ProductionMaxProgress = prod.BuildTime;

				UpdateUnitProductionQueue(info, prod);
			}
			else
			{
				info.ProductionTitle = TranslationServer.Translate("PRODUCTION IDLE");
				if (info.ProductionQueue.Count > 0)
				{
					info.ProductionQueue.Clear();
				}
			}
		}
	}

	private void UpdateUnitProductionQueue(SelectedUnitInfo info, ProductionQueue prod)
	{
		bool queueChanged = info.ProductionQueue.Count != prod.Count;
		if (!queueChanged)
		{
			for (int q = 0; q < prod.Count; q++)
			{
				if (info.ProductionQueue[q] != prod.Get(q))
				{
					queueChanged = true;
					break;
				}
			}
		}

		if (queueChanged)
		{
			info.ProductionQueue.Clear();
			prod.CopyTo(info.ProductionQueue);
		}
	}

	/// <summary>
	///     Whether the authority has described a different ability list for this unit since the panel last read one. The
	///     list a unit offers is the one its description carries, and a description arrives after the unit is first drawn,
	///     so the panel has to read it again when it lands rather than keep whatever it saw on the frame it was selected.
	/// </summary>
	private static bool DescribedAbilityListChanged(EcsWorld world, Unit3D u, SelectedUnitInfo info)
	{
		int handle = world.Has<AbilityState>(u.Entity) ? world.Get<AbilityState>(u.Entity).Handle : 0;
		return handle != info.AbilityListHandle;
	}

	private void UpdateUnitAbilities(EcsWorld world, Unit3D u, SelectedUnitInfo info)
	{
		info.Abilities.Clear();
		info.AbilityListHandle = world.Has<AbilityState>(u.Entity) ? world.Get<AbilityState>(u.Entity).Handle : 0;

		if (!world.Has<AbilityState>(u.Entity)) return;

		// What a replicated unit offers is what the authority described, and nothing else. The template a map ships is not
		// that list: a map narrows a unit's abilities per unit at runtime, and it also widens a unit type with the
		// abilities of every type it can be replaced by, so painting the template while the description is still in
		// flight offered every ability the type was widened with.
		world.Get<AbilityState>(u.Entity).CopyTo(info.Abilities);
		ReportDescribedAbilityList(u, info);
	}

	/// <summary>
	///     Names what the panel was given for the unit it just read, so a grid that offers more than the map let through
	///     can be told apart from a grid reading a list the authority never described.
	/// </summary>
	private static void ReportDescribedAbilityList(Unit3D u, SelectedUnitInfo info)
	{
		var unitId = new UnitId(u.Entity.Id);
		int hiddenAbilities = 0;
		for (int index = 0; index < info.Abilities.Count; index++)
		{
			if (GameContext.RegistryService.IsAbilityHiddenForUnit(unitId, info.Abilities[index])) hiddenAbilities++;
		}

		GameLog.LogToConsole($"[CommandPanel] unit {u.Entity.Id} ('{u.UnitId}') lists {info.Abilities.Count} ability(ies) at handle {info.AbilityListHandle}, {hiddenAbilities} of them hidden");
	}
}
