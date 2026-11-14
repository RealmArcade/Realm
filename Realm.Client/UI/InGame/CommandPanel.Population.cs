using System;
using System.Collections.Generic;
using System.Text;
using Realm.Ecs;
using Godot;
using Realm.Client.Core;
using Realm.Client.Network;
using Realm.Client.Services;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Resources;
using Realm.Ecs.Components.Tags;
using Realm.Ecs.Services;
using Realm.Client.Utils;
using Realm.Shared.Abilities;

namespace Realm.Client.UI.InGame;

public partial class CommandPanel
{
	private List<CommandCardItem> GetCommandCardItems(SelectedUnitInfo focusedUnit, bool isBuildSubMenuOpen)
	{
		var items = new List<CommandCardItem>();
		if (focusedUnit == null) return items;

		if (focusedUnit.IsBuilding && focusedUnit.IsUnderConstruction)
		{
			// A site keeps every choice its map drew on it: a map that offers the buildings a site may become, or asks
			// anything else of the player before it is raised, has nowhere else to put those.
			foreach (var ability in focusedUnit.Abilities)
			{
				AddItem(items, CreateAbilityItem(ability, focusedUnit.Entity));
			}

			// Taking a site down is the engine's own command, so it is offered on the sites the engine is raising. A map
			// holding one short of completion owns that site's grid and its own way out of it, and an engine cancel there
			// would take the site out from under the map's rules.
			if (IsRaisedByTheEngine(focusedUnit.Entity)) items.Add(CreateCancelConstructionItem(focusedUnit.Entity));
			return items;
		}

		bool hasMetadata = GameHost.TryGetUnitOrBuildingMetadata(focusedUnit.UnitId, out var meta);

		if (!focusedUnit.IsBuilding)
		{
			PopulateUnitItems(items, focusedUnit, meta, hasMetadata, isBuildSubMenuOpen);
		}
		else
		{
			PopulateBuildingItems(items, focusedUnit, meta, hasMetadata);
		}

		items.RemoveAll(i => i == null);
		return items;
	}

	private static void AddItem(List<CommandCardItem> items, CommandCardItem? item)
	{
		if (item != null) items.Add(item);
	}

	/// <summary>
	///     Reports whether a site is being raised by the engine rather than held short of completion by its map. A map
	///     that states a lesser ceiling is holding the site itself, and the site is then the map's to finish or abandon.
	/// </summary>
	/// <param name="siteEntity">The site the grid is being drawn for.</param>
	/// <returns>True when nothing but the site's own work stands between it and a finished building.</returns>
	private static bool IsRaisedByTheEngine(Entity siteEntity)
	{
		EcsWorld? world = GameContext.World;
		if (world == null || !world.IsAlive(siteEntity)) return false;
		if (!world.TryGet<ConstructionState>(siteEntity, out ConstructionState construction)) return false;

		return construction.CompletionCeilingFraction >= 1f;
	}

	private static CommandCardItem CreateCancelConstructionItem(Entity siteEntity)
	{
		return new CommandCardItem
		{
			Id = "cancel_construction",
			IconPath = "res://Assets/UI/cancel_button_2.webp",
			IconTint = new Color(0.95f, 0.25f, 0.22f),
			Tooltip = TranslationServer.Translate("[S] Cancel Construction — Takes this unfinished site down and refunds its full cost"),
			HotkeyFromGrid = true,
			Callback = () => GameContext.Host?.CancelConstruction(siteEntity)
		};
	}

	private void PopulateUnitItems(List<CommandCardItem> items, SelectedUnitInfo focusedUnit, UnitMetadata? meta, bool hasMetadata, bool isBuildSubMenuOpen)
	{
		if (isBuildSubMenuOpen)
		{
			PopulateBuildSubMenuItems(items, focusedUnit, meta, hasMetadata);
			return;
		}

		bool isStationary = focusedUnit.Speed <= 0.001f;
		if (isStationary)
		{
			PopulateStationaryUnitItems(items, focusedUnit);

			if (meta?.BuildOptions?.Length > 0)
			{
				items.Add(new CommandCardItem
				{
					Id = "build",
					IconPath = "res://Assets/UI/golden_hammers.webp",
					Tooltip = "[B] Build Structure",
					HotkeyFromGrid = true,
					AbilityId = BuildCommandAbilityId,
					CasterEntity = focusedUnit.Entity,
					IgnoresCooldownGate = true,
					Callback = () => GameContext.InGameHud?.EnterBuildSubMenu()
				});
			}
		}
		else
		{
			PopulateMobileUnitItems(items, focusedUnit, hasMetadata, meta);
		}

		if (hasMetadata)
		{
			PopulateShopItems(items, meta, focusedUnit);
			PopulateGlobalUpgrades(items, meta, focusedUnit);
		}
	}

	private void PopulateBuildSubMenuItems(List<CommandCardItem> items, SelectedUnitInfo focusedUnit, UnitMetadata? meta, bool hasMetadata)
	{
		string[] options = meta?.BuildOptions ?? Array.Empty<string>();
		foreach (var opt in options)
		{
			var item = CreateBuildOptionItem(opt, focusedUnit.Entity);
			if (item != null)
			{
				items.Add(item);
			}
		}

		items.Add(new CommandCardItem
		{
			Id = "cancel_build",
			IconPath = "res://Assets/UI/cancel_button_2.webp",
			Tooltip = "[Esc] Cancel",
			Hotkey = Key.Escape,
			Callback = () => GameContext.InGameHud?.ExitBuildSubMenu()
		});
	}

	private void PopulateStationaryUnitItems(List<CommandCardItem> items, SelectedUnitInfo focusedUnit)
	{
		var perkAbilities = new List<string>();
		var otherAbilities = new List<string>();

		foreach (var ab in focusedUnit.Abilities)
		{
			if (ab.StartsWith("perk_")) perkAbilities.Add(ab);
			else otherAbilities.Add(ab);
		}

		if (perkAbilities.Count > 0)
		{
			foreach (var ab in perkAbilities) AddItem(items, CreateAbilityItem(ab, focusedUnit.Entity));
			AddStandardCombatCommands(items, focusedUnit.Entity);
			foreach (var ab in otherAbilities) AddItem(items, CreateAbilityItem(ab, focusedUnit.Entity));
		}
		else
		{
			foreach (var ab in focusedUnit.Abilities) AddItem(items, CreateAbilityItem(ab, focusedUnit.Entity));
			AddStandardCombatCommands(items, focusedUnit.Entity);
		}
	}

	private void PopulateMobileUnitItems(List<CommandCardItem> items, SelectedUnitInfo focusedUnit, bool hasMetadata, UnitMetadata? meta)
	{
		if (!IsCommandsFrozen(focusedUnit.Entity))
		{
			items.Add(new CommandCardItem
			{
				Id = "move",
				IconPath = "res://Assets/UI/move_speed.webp",
				Tooltip = "[M] Move / Right-Click Ground",
				HotkeyFromGrid = true,
				Callback = () => GameContext.Host?.EnterCommandTargeting("move")
			});

			items.Add(CreateStopCommand());
			items.Add(CreateHoldCommand());

			if (focusedUnit.Damage > 0f)
			{
				items.Add(new CommandCardItem
				{
					Id = "attack",
					IconPath = "res://Assets/UI/battle_axe.webp",
					Tooltip = "[A] Attack / Attack-Move — Click enemy to attack, click ground to attack-move",
					HotkeyFromGrid = true,
					Callback = () => GameContext.Host?.EnterCommandTargeting("attack")
				});
			}

			items.Add(new CommandCardItem
			{
				Id = "patrol",
				IconPath = "res://Assets/UI/patrol.webp",
				Tooltip = "[P] Patrol — Unit patrols between current position and target, engaging enemies",
				HotkeyFromGrid = true,
				Callback = () => GameContext.Host?.EnterCommandTargeting("patrol")
			});
		}

		if (meta?.BuildOptions?.Length > 0)
		{
			items.Add(new CommandCardItem
			{
				Id = "build",
				IconPath = "res://Assets/UI/golden_hammers.webp",
				Tooltip = "[B] Build Structure",
				HotkeyFromGrid = true,
				AbilityId = BuildCommandAbilityId,
				CasterEntity = focusedUnit.Entity,
				IgnoresCooldownGate = true,
				Callback = () => GameContext.InGameHud?.EnterBuildSubMenu()
			});
		}

		foreach (var ab in focusedUnit.Abilities)
		{
			AddItem(items, CreateAbilityItem(ab, focusedUnit.Entity));
		}
	}

	private void AddStandardCombatCommands(List<CommandCardItem> items, Entity entity)
	{
		if (IsCommandsFrozen(entity))
			return;

		items.Add(new CommandCardItem
		{
			Id = "attack",
			IconPath = "res://Assets/UI/battle_axe.webp",
			Tooltip = "[A] Attack / Attack-Move — Click enemy to attack, click ground to attack-move",
			HotkeyFromGrid = true,
			Callback = () => GameContext.Host?.EnterCommandTargeting("attack")
		});

		items.Add(CreateStopCommand());
	}

	private CommandCardItem CreateStopCommand()
	{
		return new CommandCardItem
		{
			Id = "stop",
			IconPath = "res://Assets/UI/cancel_button_2.webp",
			Tooltip = "[S] Stop Selected Units",
			HotkeyFromGrid = true,
			Callback = () =>
			{
				GameContext.InGameHud?.ShowFeedbackText(TranslationServer.Translate("Command: Stop Current Action"), new Color(0.9f, 0.2f, 0.2f));
				GameContext.Host?.StopSelectedUnits();
			}
		};
	}

	private CommandCardItem CreateHoldCommand()
	{
		return new CommandCardItem
		{
			Id = "hold",
			IconPath = "res://Assets/UI/magic_upgrade_arrow.webp",
			Tooltip = "[H] Hold Position — Unit stays put and attacks in place",
			HotkeyFromGrid = true,
			Callback = () =>
			{
				GameContext.InGameHud?.ShowFeedbackText(TranslationServer.Translate("Command: Hold Position"), new Color(0.9f, 0.8f, 0.1f));
				GameContext.Host?.HoldSelectedUnits();
			}
		};
	}

	private void PopulateBuildingItems(List<CommandCardItem> items, SelectedUnitInfo focusedUnit, UnitMetadata? meta, bool hasMetadata)
	{
		if (meta?.BuildOptions != null)
		{
			foreach (var opt in meta.BuildOptions) items.Add(CreateTrainOptionItem(opt));
		}

		if (meta != null && meta.BuildOptions != null && meta.BuildOptions.Length > 0)
		{
			items.Add(new CommandCardItem
			{
				Id = "set_rally",
				IconPath = "res://Assets/UI/alliance_flag.webp",
				Tooltip = "[Y] Set Rally Point — Set location where new units will walk",
				Hotkey = Key.Y,
				Callback = () => GameContext.Host?.EnterCommandTargeting("rally")
			});
		}

		if (hasMetadata)
		{
			PopulateShopItems(items, meta, focusedUnit);
			PopulateGlobalUpgrades(items, meta, focusedUnit);
		}

		foreach (var ab in focusedUnit.Abilities)
		{
			AddItem(items, CreateAbilityItem(ab, focusedUnit.Entity));
		}
	}

	private void PopulateShopItems(List<CommandCardItem> items, UnitMetadata? meta, SelectedUnitInfo focusedUnit)
	{
		if (meta?.ShopItems == null || meta.ShopItems.Length == 0) return;

		foreach (var itemId in meta.ShopItems)
		{
			if (string.IsNullOrEmpty(itemId)) continue;

			ItemMetadata? itemMeta = null;
			if (!GameHost.ItemRegistry.TryGetValue(itemId, out itemMeta))
			{
				if (itemId.StartsWith("item/", StringComparison.OrdinalIgnoreCase))
				{
					GameHost.ItemRegistry.TryGetValue(itemId.Substring(5), out itemMeta);
				}
				else
				{
					GameHost.ItemRegistry.TryGetValue("item/" + itemId, out itemMeta);
				}
			}

			string itemName = itemMeta != null && !string.IsNullOrEmpty(itemMeta.Name) ? MapText.TranslateName(itemMeta.Name) : itemId;
			string itemDesc = itemMeta != null && !string.IsNullOrEmpty(itemMeta.Description) ? MapText.Translate(itemMeta.Description) : $"Buy {itemName} for a nearby combat unit";
			float costGold = itemMeta?.CostGold ?? 0f;
			string iconPath = itemMeta != null && !string.IsNullOrEmpty(itemMeta.IconPath) ? itemMeta.IconPath : "res://Assets/UI/alliance_flag.webp";

			string header = $"Buy {UIHelper.BuildHeader(itemName, costGold, 0f, 0f)}";
			string statBlock = TooltipStatBlock.ForItem(itemMeta);
			string tooltip = string.IsNullOrEmpty(itemDesc) ? header : $"{header}\n{itemDesc}";
			if (statBlock.Length > 0) tooltip += "\n" + statBlock;

			items.Add(new CommandCardItem
			{
				Id = "buy_" + itemId,
				IconPath = iconPath,
				Tooltip = tooltip,
				TooltipContext = TooltipContext.ForItem(focusedUnit.Entity, itemId),
				Hotkey = Key.None,
				Callback = () =>
				{
					var sellerEntity = focusedUnit != null && focusedUnit.Entity != Entity.Null
						? focusedUnit.Entity
						: (GameContext.Host?.SelectedUnits.Count == 1 ? GameContext.Host.SelectedUnits[0].Entity : Entity.Null);

					if (sellerEntity != Entity.Null)
					{
						GameContext.Host?.BuyItem(itemId, sellerEntity);
					}
				}
			});
		}
	}

	private void PopulateGlobalUpgrades(List<CommandCardItem> items, UnitMetadata? meta, SelectedUnitInfo focusedUnit)
	{
		if (meta?.Upgrades == null || meta.Upgrades.Length == 0) return;

		int localViewingIndex = GameContext.PerspectiveFilter.ControlledPlayerIndex;

		foreach (var upgradeId in meta.Upgrades)
		{
			if (string.IsNullOrEmpty(upgradeId)) continue;

			UpgradeMetadata? upgMeta = null;
			if (!GameHost.UpgradeRegistry.TryGetValue(upgradeId, out upgMeta))
			{
				if (upgradeId.StartsWith("upgrade/", StringComparison.OrdinalIgnoreCase))
				{
					GameHost.UpgradeRegistry.TryGetValue(upgradeId.Substring(8), out upgMeta);
				}
				else
				{
					GameHost.UpgradeRegistry.TryGetValue("upgrade/" + upgradeId, out upgMeta);
				}
			}

			string name = upgMeta != null && !string.IsNullOrEmpty(upgMeta.Name) ? MapText.TranslateName(upgMeta.Name) : upgradeId;
			string desc = MapText.Translate(upgMeta?.Description);
			float gold = upgMeta?.CostGold ?? 0f;
			float wood = upgMeta?.CostWood ?? 0f;
			float stone = upgMeta?.CostStone ?? 0f;
			int maxLevel = upgMeta != null && upgMeta.MaxLevel > 0 ? upgMeta.MaxLevel : 1;
			string iconPath = upgMeta != null && !string.IsNullOrEmpty(upgMeta.IconPath) ? upgMeta.IconPath : "res://Assets/UI/magic_upgrade_arrow.webp";

			HotkeySpec hotkey = HotkeySpec.None;
			if (upgMeta != null && !string.IsNullOrEmpty(upgMeta.Hotkey))
			{
				hotkey = ParseHotkey(upgMeta.Hotkey);
			}

			string hotkeyPrefix = !hotkey.IsNone ? $"[{hotkey}] " : "";
			string statBlock = TooltipStatBlock.ForUpgrade(upgMeta);
			string header = $"{hotkeyPrefix}Upgrade {UIHelper.BuildHeader(name, gold, wood, stone)}";
			string tooltip = string.IsNullOrEmpty(desc) ? header : $"{header}\n{desc}";
			if (statBlock.Length > 0) tooltip += "\n" + statBlock;

			string canonicalId = upgMeta != null && !string.IsNullOrEmpty(upgMeta.TemplateID) ? upgMeta.TemplateID : upgradeId;

			Func<bool> isMaxed = () =>
			{
				int lvl = GameContext.Host?.GetPlayerTechLevel(localViewingIndex, canonicalId) ?? 0;
				if (lvl == 0 && canonicalId != upgradeId)
				{
					lvl = GameContext.Host?.GetPlayerTechLevel(localViewingIndex, upgradeId) ?? 0;
				}
				return lvl >= maxLevel;
			};

			Func<string> getButtonText = () =>
			{
				if (isMaxed()) return TranslationServer.Translate("MAXED");
				int lvl = GameContext.Host?.GetPlayerTechLevel(localViewingIndex, canonicalId) ?? 0;
				if (lvl == 0 && canonicalId != upgradeId)
				{
					lvl = GameContext.Host?.GetPlayerTechLevel(localViewingIndex, upgradeId) ?? 0;
				}
				if (lvl > 0 && maxLevel > 1) return $"Lvl {lvl}";
				return "";
			};

			// What an upgrade costs is not a reason to darken its card: a greyed card says no without saying why, and the
			// map's resources move while the card is on screen. A player who cannot pay is told so when they ask to buy,
			// and one who has just been paid for it can always try.
			items.Add(new CommandCardItem
			{
				Id = "upgrade_" + upgradeId,
				IconPath = iconPath,
				Tooltip = tooltip,
				TooltipContext = TooltipContext.ForUpgrade(focusedUnit.Entity, upgradeId),
				Hotkey = hotkey,
				Callback = () => PurchaseUpgrade(upgradeId, name, gold, wood, stone, localViewingIndex),
				IsDisabled = isMaxed,
				GetButtonText = getButtonText
			});
		}
	}
}
