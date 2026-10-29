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

		GameContext.Host?.SubmitConsumableOrder(building, consumable.CardId, targetPosition);
		return new PurchaseResult(true, true, consumable.TemplateID, string.Empty);
	}
}
