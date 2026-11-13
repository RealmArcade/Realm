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
	/// <summary>
	///     Asks for an upgrade, and answers for it when the player cannot pay: the cost is named along with what is
	///     missing, rather than the card being pre-emptively dark and silent.
	/// </summary>
	private void PurchaseUpgrade(string upgradeId, string name, float gold, float wood, float stone, int playerIndex)
	{
		InGameHUD? hud = GameContext.InGameHud;
		if (hud != null)
		{
			if (hud.Gold < gold)
			{
				ReportUpgradeShortfall(name, hud.Gold, gold, TranslationServer.Translate("Gold"));
				return;
			}

			if (hud.Wood < wood)
			{
				ReportUpgradeShortfall(name, hud.Wood, wood, TranslationServer.Translate("Wood"));
				return;
			}

			if (hud.Stone < stone)
			{
				ReportUpgradeShortfall(name, hud.Stone, stone, TranslationServer.Translate("Stone"));
				return;
			}
		}

		GameContext.Host?.BuyUpgrade(upgradeId, playerIndex);
	}

	private static void ReportUpgradeShortfall(string name, float held, float cost, string resourceName)
	{
		string shortfall = MathF.Ceiling(cost - held).ToString("F0");
		string message = string.Format(TranslationServer.Translate("{0} needs {1} more {2}"), name, shortfall, resourceName);

		GameContext.InGameHud?.ShowFeedbackText(message, new Color(1.0f, 0.3f, 0.25f));
	}

	private CommandCardItem? CreateBuildOptionItem(string unitId, Entity builderEntity = default)
	{
		var host = GameContext.Host;
		var unitWrapper = GetUnitWrapperIfAlive(host, builderEntity);
		if (IsAbilityHidden(host, unitWrapper, unitId)) return null;

		HotkeySpec hotkey = HotkeySpec.None;

		var abilityDef = host?.GetAbilityDefinition(unitId);
		if (abilityDef != null && !string.IsNullOrEmpty(abilityDef.Hotkey))
		{
			hotkey = ParseHotkey(abilityDef.Hotkey);
		}

		string finalTooltip;
		if (abilityDef != null && !string.IsNullOrEmpty(abilityDef.TooltipTemplate))
		{
			finalTooltip = MapText.TranslateTemplate(abilityDef.TooltipTemplate);
		}
		else
		{
				finalTooltip = UIHelper.GetUnitCostTooltip(unitId, hotkey, "Build");
		}

		string iconPath = abilityDef != null && !string.IsNullOrEmpty(abilityDef.IconPath)
			? abilityDef.IconPath
			: GetUnitIcon(unitId);

		return new CommandCardItem
		{
			Id = "build_" + unitId,
			IconPath = iconPath,
			Tooltip = finalTooltip,
			TooltipContext = TooltipContext.ForUnit(builderEntity),
			Hotkey = hotkey,
			GridX = abilityDef?.GridX ?? -1,
			GridY = abilityDef?.GridY ?? -1,
			Callback = () => GameContext.Host?.EnterBuildingPlacement(unitId),
			IsDisabled = () => host != null && GameContext.RegistryService.IsAbilityDisabledForUnit(unitWrapper, unitId)
		};
	}

	private CommandCardItem CreateTrainOptionItem(string unitId)
	{
		HotkeySpec hotkey = HotkeySpec.None;

			string finalTooltip = UIHelper.GetUnitCostTooltip(unitId, hotkey, "Train");

		return new CommandCardItem
		{
			Id = "train_" + unitId,
			IconPath = GetUnitIcon(unitId),
			Tooltip = finalTooltip,
			TooltipContext = TooltipContext.ForUnit(Entity.Null),
			Hotkey = hotkey,
			Callback = () => GameContext.Host?.TrainUnitAtBuilding(unitId)
		};
	}

	private string GetDefaultAbilityIcon(string abilityId)
	{
		return "res://Assets/UI/alliance_flag.webp";
	}

	private string GetDefaultAbilityTooltip(string abilityId)
	{
		return string.Format(TranslationServer.Translate("Cast {0}"), abilityId.ToUpper());
	}

	private CommandCardItem? CreateAbilityItem(string abilityId, Entity casterEntity)
	{
		var host = GameContext.Host;
		var unitWrapper = GetUnitWrapperIfAlive(host, casterEntity);

		if (IsAbilityHidden(host, unitWrapper, abilityId)) return null;

		var abilityDef = host?.GetAbilityDefinition(abilityId);
		float manaCost = GetAbilityManaCost(host, unitWrapper, abilityDef, abilityId);

		return new CommandCardItem
		{
			Id = abilityId,
			AbilityId = abilityId,
			CasterEntity = casterEntity,
			ManaCost = manaCost,
			IconPath = ResolveAbilityIcon(abilityId, abilityDef),
			Tooltip = ResolveAbilityTooltip(abilityId, abilityDef, IsAbilityStateDisabled(casterEntity, abilityId), manaCost),
			TooltipContext = TooltipContext.ForAbility(casterEntity, abilityId),
			Hotkey = ParseHotkey(abilityDef?.Hotkey),
			GridX = abilityDef?.GridX ?? -1,
			GridY = abilityDef?.GridY ?? -1,
			Callback = GetAbilityCallback(abilityId, abilityDef),
			IsDisabled = () => IsAbilityDisabled(host, unitWrapper, casterEntity, abilityId, manaCost)
		};
	}
}
