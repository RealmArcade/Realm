using System;
using Godot;
using Realm.Client.Core;
using Realm.Client.UI;
using Realm.Ecs;
using Realm.Shared.Metadata;

namespace Realm.Client.Services;

public class ConsumableService
{
	public readonly record struct PurchaseResult(bool IsConsumable, bool Purchased, string ConsumableId, string Message)
	{
		public static readonly PurchaseResult NotConsumable = new(false, false, string.Empty, string.Empty);
	}

	public PurchaseResult TryPurchase(UnitId building, string abilityId, System.Numerics.Vector3 targetPosition)
	{
		ItemMetadata? consumable = GameContext.RegistryService?.GetConsumableForCard(abilityId);
		if (consumable == null) return PurchaseResult.NotConsumable;

		string displayName = GameContext.Host?.GetAbilityDefinition(abilityId)?.DisplayName ?? abilityId;

		// Paying is the authority's job, but a buyer who is short of a resource should hear that on the click that asked,
		// not watch a purchase quietly fail to arrive. The authority charges all three resources, so all three are checked.
		InGameHUD? hud = GameContext.InGameHud;
		if (hud != null)
		{
			if (hud.Gold < consumable.CostGold)
			{
				return ReportShortfall(consumable.TemplateID, displayName, hud.Gold, consumable.CostGold, TranslationServer.Translate("Gold"));
			}

			if (hud.Wood < consumable.CostWood)
			{
				return ReportShortfall(consumable.TemplateID, displayName, hud.Wood, consumable.CostWood, TranslationServer.Translate("Wood"));
			}

			if (hud.Stone < consumable.CostStone)
			{
				return ReportShortfall(consumable.TemplateID, displayName, hud.Stone, consumable.CostStone, TranslationServer.Translate("Stone"));
			}
		}

		string failure = string.Empty;
		bool ordered = GameContext.Host != null
			&& GameContext.Host.SubmitConsumableOrder(building, consumable.CardId, targetPosition, out failure);
		if (!ordered)
		{
			string reason = string.IsNullOrEmpty(failure)
				? TranslationServer.Translate("there is no match running")
				: failure;

			return new PurchaseResult(true, false, consumable.TemplateID,
				string.Format(TranslationServer.Translate("{0} could not be ordered: {1}."), displayName, reason));
		}

		return new PurchaseResult(true, true, consumable.TemplateID, string.Empty);
	}

	private static PurchaseResult ReportShortfall(string consumableId, string displayName, float held, float cost, string resourceName)
	{
		string missing = MathF.Ceiling(cost - held).ToString("F0");
		string message = string.Format(TranslationServer.Translate("{0} needs {1} more {2}"), displayName, missing, resourceName);

		return new PurchaseResult(true, false, consumableId, message);
	}
}
