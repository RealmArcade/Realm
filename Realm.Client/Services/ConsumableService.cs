using Realm.Client.Core;
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
		ConsumableMetadata? consumable = GameContext.RegistryService?.GetConsumableForCard(abilityId);
		if (consumable == null) return PurchaseResult.NotConsumable;

		string displayName = GameContext.Host?.GetAbilityDefinition(abilityId)?.DisplayName ?? abilityId;

		// Paying is the authority's job, but a buyer who is short of gold should hear that on the click that asked,
		// not watch a purchase quietly fail to arrive.
		float gold = GameContext.InGameHud?.Gold ?? -1f;
		if (gold >= 0f && gold < consumable.CostGold)
		{
			return new PurchaseResult(true, false, consumable.TemplateID,
				$"{displayName} costs {(int)consumable.CostGold} gold; you have {(int)gold}.");
		}

		string failure = string.Empty;
		bool ordered = GameContext.Host != null
			&& GameContext.Host.SubmitConsumableOrder(building, consumable.CardId, targetPosition, out failure);
		if (!ordered)
		{
			return new PurchaseResult(true, false, consumable.TemplateID,
				$"{displayName} could not be ordered: {(string.IsNullOrEmpty(failure) ? "there is no match running" : failure)}.");
		}

		return new PurchaseResult(true, true, consumable.TemplateID, string.Empty);
	}
}
