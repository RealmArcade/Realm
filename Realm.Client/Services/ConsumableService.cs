using Realm.Client.Core;
using Realm.Ecs;
using Realm.MapAPI;
using Realm.Shared.Metadata;

namespace Realm.Client.Services;

/// <summary>
/// Sells the map's authored consumables. Which cards are for sale, and whether a card is a purchase at all, is answered
/// here; what the purchase costs, which unit is close enough to receive it and what it does to that unit are the
/// match's to decide, because they are decisions about world state. A purchase therefore leaves this screen as an order,
/// and the unit it changed reaches every player as replicated state.
/// </summary>
public class ConsumableService
{
	public readonly record struct PurchaseResult(bool IsConsumable, bool Purchased, string ConsumableId, string Message)
	{
		public static readonly PurchaseResult NotConsumable = new(false, false, string.Empty, string.Empty);
	}

	/// <summary>
	///     Asks the match to sell the card a player clicked to the unit it was aimed at, bought from the building the cast
	///     came from. A card that is not a consumable comes back as one, so the cast path can carry on and treat it as an
	///     ordinary spell.
	/// </summary>
	public PurchaseResult TryPurchase(UnitId building, string abilityId, System.Numerics.Vector3 targetPosition)
	{
		ConsumableMetadata? consumable = GameContext.RegistryService?.GetConsumableForCard(abilityId);
		if (consumable == null) return PurchaseResult.NotConsumable;

		GameContext.Host?.SubmitConsumableOrder(building, consumable.CardId, targetPosition);
		return new PurchaseResult(true, true, consumable.TemplateID, string.Empty);
	}
}
