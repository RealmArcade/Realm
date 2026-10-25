using System;
using System.Collections.Generic;
using Realm.Client.Core;
using Realm.Ecs;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Movement;
using Realm.MapAPI;
using Realm.Shared.Metadata;

namespace Realm.Client.Services;

/// <summary>
/// Sells the map's authored consumables. A building offering the card, a player who can afford it and
/// a unit to bind it to are the whole rule set; what the attachment then does is the map's business,
/// which is why every purchase is announced through <see cref="GameHost.OnConsumableChanged" />.
/// </summary>
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

		if (!TryGetEntity(building, out Entity buildingEntity))
		{
			return new PurchaseResult(true, false, consumable.TemplateID, $"{consumable.TemplateID} was cast by a unit that no longer exists.");
		}

		if (!TryGetPlayerIndex(buildingEntity, out int playerIndex))
		{
			return new PurchaseResult(true, false, consumable.TemplateID, "Only a player-owned building can sell a consumable.");
		}

		Entity target = FindTarget(buildingEntity, playerIndex, consumable, targetPosition);
		if (target == Entity.Null && !consumable.ClearsSlot)
		{
			return new PurchaseResult(true, false, consumable.TemplateID, "Target one of your own units.");
		}

		if (target == Entity.Null)
		{
			return new PurchaseResult(true, false, consumable.TemplateID, "Nothing of yours is close enough to purge.");
		}

		if (!CanAfford(playerIndex, consumable, out string costMessage))
		{
			return new PurchaseResult(true, false, consumable.TemplateID, costMessage);
		}

		if (consumable.ClearsSlot)
		{
			if (!ClearAttachment(target, consumable, out string clearedId))
			{
				return new PurchaseResult(true, false, consumable.TemplateID, "That unit is not carrying one.");
			}

			Spend(playerIndex, consumable);
			GameContext.Host?.RaiseConsumableChanged((UnitId)target.Id, string.Empty);
			return new PurchaseResult(true, true, clearedId, "Consumable cleared.");
		}

		if (!HasRoom(target, consumable))
		{
			return new PurchaseResult(true, false, consumable.TemplateID, "That unit already carries one.");
		}

		Spend(playerIndex, consumable);
		Attach(target, consumable, playerIndex);
		GameContext.Host?.RaiseConsumableChanged((UnitId)target.Id, consumable.TemplateID);
		return new PurchaseResult(true, true, consumable.TemplateID, "Consumable bound.");
	}

	public string GetAttachment(Entity unit, string slotGroup)
	{
		EcsWorld? world = GameContext.World;
		if (world == null || !world.IsAlive(unit) || !world.Has<ConsumableAttachment>(unit)) return string.Empty;

		ConsumableAttachment attachment = world.Get<ConsumableAttachment>(unit);
		if (!attachment.HasDefinition) return string.Empty;
		return attachment.IsInGroup(slotGroup) ? attachment.GetDefinitionId() : string.Empty;
	}

	private static bool HasRoom(Entity unit, ConsumableMetadata consumable)
	{
		EcsWorld? world = GameContext.World;
		if (world == null || !world.Has<ConsumableAttachment>(unit)) return true;

		ConsumableAttachment attachment = world.Get<ConsumableAttachment>(unit);
		return !attachment.HasDefinition || !attachment.IsInGroup(consumable.SlotGroup);
	}

	private static void Attach(Entity unit, ConsumableMetadata consumable, int playerIndex)
	{
		EcsWorld? world = GameContext.World;
		if (world == null) return;

		var attachment = new ConsumableAttachment(consumable.TemplateID, consumable.SlotGroup, playerIndex);
		world.SetOrAdd(unit, attachment);
	}

	private static bool ClearAttachment(Entity unit, ConsumableMetadata consumable, out string clearedId)
	{
		clearedId = string.Empty;
		EcsWorld? world = GameContext.World;
		if (world == null || !world.Has<ConsumableAttachment>(unit)) return false;

		ConsumableAttachment attachment = world.Get<ConsumableAttachment>(unit);
		if (!attachment.HasDefinition || !attachment.IsInGroup(consumable.SlotGroup)) return false;

		clearedId = attachment.GetDefinitionId();
		world.Remove<ConsumableAttachment>(unit);
		return true;
	}

	private static Entity FindTarget(Entity buildingEntity, int playerIndex, ConsumableMetadata consumable, System.Numerics.Vector3 position)
	{
		EcsWorld? world = GameContext.World;
		if (world == null) return Entity.Null;

		bool ownOnly = !string.Equals(consumable.TargetScope, "AnyUnit", StringComparison.OrdinalIgnoreCase);
		float bestDistance = consumable.PurchaseRange;
		Entity best = Entity.Null;

		var query = QueryCache.AllPositionAndOwnerNoneDeadQuery;
		world.Query(in query, (Entity candidate, ref Position candidatePosition, ref Owner owner) =>
		{
			if (candidate == buildingEntity) return;

			System.Numerics.Vector3 delta = candidatePosition.Value - position;
			float distance = delta.Length();
			if (distance > bestDistance) return;

			if (ownOnly)
			{
				if (!TryGetPlayerIndex(candidate, out int ownerIndex) || ownerIndex != playerIndex) return;
			}

			bestDistance = distance;
			best = candidate;
		});

		return best;
	}

	private static bool TryGetPlayerIndex(Entity unit, out int playerIndex)
	{
		playerIndex = -1;
		EcsWorld? world = GameContext.World;
		if (world == null || !world.IsAlive(unit)) return false;

		if (world.Has<UnitOwnerPlayer>(unit))
		{
			playerIndex = world.Get<UnitOwnerPlayer>(unit).PlayerIndex;
			return true;
		}

		if (!world.Has<Owner>(unit)) return false;

		Entity playerEntity = world.Get<Owner>(unit).PlayerEntity.Value;
		if (playerEntity == Entity.Null || !world.IsAlive(playerEntity) || !world.Has<UnitOwnerPlayer>(playerEntity)) return false;

		playerIndex = world.Get<UnitOwnerPlayer>(playerEntity).PlayerIndex;
		return true;
	}

	private static bool CanAfford(int playerIndex, ConsumableMetadata consumable, out string message)
	{
		message = string.Empty;

		if (consumable.CostGold > 0f && GetGold(playerIndex) < consumable.CostGold)
		{
			message = $"Costs {consumable.CostGold:F0} gold.";
			return false;
		}

		if (consumable.CostWood > 0f && GameContext.ResourceEconomyService.GetPlayerWood(playerIndex) < consumable.CostWood)
		{
			message = $"Costs {consumable.CostWood:F0} wood.";
			return false;
		}

		if (consumable.CostStone > 0f && GameContext.ResourceEconomyService.GetPlayerStone(playerIndex) < consumable.CostStone)
		{
			message = $"Costs {consumable.CostStone:F0} stone.";
			return false;
		}

		return true;
	}

	private static void Spend(int playerIndex, ConsumableMetadata consumable)
	{
		if (consumable.CostGold > 0f)
		{
			((IGameAPI)GameContext.Host!).SetPlayerGold(playerIndex, GetGold(playerIndex) - consumable.CostGold);
		}

		if (consumable.CostWood > 0f)
		{
			GameContext.ResourceEconomyService.AdjustPlayerWood(playerIndex, -consumable.CostWood);
		}

		if (consumable.CostStone > 0f)
		{
			GameContext.ResourceEconomyService.AdjustPlayerStone(playerIndex, -consumable.CostStone);
		}
	}

	private static float GetGold(int playerIndex) => GameContext.SimulationService.GetPlayerGoldDomain(playerIndex);

	private static bool TryGetEntity(UnitId unit, out Entity entity)
	{
		entity = Entity.Null;
		if (!unit.IsValid) return false;
		return GameContext.SimulationService.TryGetEntity(unit, out entity);
	}
}
