using System;
using System.Collections.Generic;
using System.Numerics;
using Realm.Ecs;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Resources;
using Realm.Ecs.Components.Tags;
using Realm.Ecs.Components.Terrain;
using Realm.Ecs.Services;
using Realm.Ecs.Utils;
using Realm.Shared.Metadata;
using Realm.Shared.Network;

namespace Realm.Shared.Simulation;

/// <summary>the cheat and consumable dispatchers and their target search.</summary>
public partial class CommandAuthorityService
	{
	private bool DispatchCheatCommand(int peerId, NetworkCommand cmd, ref string rejectionReason)
	{
		if (cmd.CommandType != "cheat") return false;

		HandleCheatCommand(peerId, cmd, ref rejectionReason);
		return true;
	}

	private bool DispatchConsumableCommand(int peerId, NetworkCommand cmd, ref string rejectionReason)
	{
		if (cmd.CommandType != "use_consumable") return false;

		HandleConsumableCommand(peerId, cmd, ref rejectionReason);
		return true;
	}

	private void HandleConsumableCommand(int peerId, NetworkCommand cmd, ref string rejectionReason)
	{
		string cardId = cmd.ArgString ?? string.Empty;
		if (string.IsNullOrEmpty(cardId))
		{
			rejectionReason = "a purchase order names no consumable";
			return;
		}

		if (ConsumableProvider == null)
		{
			rejectionReason = $"nothing is sold as '{cardId}' in this match";
			return;
		}

		ItemMetadata? consumable = ConsumableProvider(cardId);
		if (consumable == null)
		{
			rejectionReason = $"the map sells no consumable '{cardId}'";
			return;
		}

		if (cmd.UnitEntityIds == null || cmd.UnitEntityIds.Count == 0)
		{
			rejectionReason = "a purchase order names no building to buy from";
			return;
		}

		Entity building = _targets.Resolve(cmd.UnitEntityIds[0]);
		if (building == Entity.Null || !IsClientAuthorized(peerId, building))
		{
			rejectionReason = $"building {cmd.UnitEntityIds[0]} is not owned by peer {peerId}";
			return;
		}

		if (!TryGetPlayerEntityForPeer(peerId, out Entity playerEntity))
		{
			rejectionReason = $"peer {peerId} has no player entity";
			return;
		}

		Entity target = FindConsumableTarget(building, playerEntity, consumable, cmd.TargetPosition.ToNumerics(), out bool aimedAtRefusingUnit);
		if (target == Entity.Null)
		{
			rejectionReason = aimedAtRefusingUnit
				? "that unit cannot be given a consumable"
				: consumable.ClearsSlot ? "nothing of the buyer's is close enough to purge" : "no unit of the buyer's is close enough to receive it";
			return;
		}

		bool carriesOne = CarriesAttachment(target, consumable);
		if (consumable.ClearsSlot && !carriesOne)
		{
			rejectionReason = "that unit is not carrying one";
			return;
		}

		if (!consumable.ClearsSlot && carriesOne)
		{
			rejectionReason = "that unit already carries one";
			return;
		}

		if (!TryPayCosts(playerEntity, consumable.CostGold, consumable.CostWood, consumable.CostStone))
		{
			rejectionReason = $"'{consumable.TemplateID}' costs more than the player has";
			return;
		}

		if (consumable.ClearsSlot)
		{
			_context.World.Remove<ConsumableAttachment>(target);
			ConsumableChangedSink?.Invoke(target.Id, string.Empty);
			return;
		}

		_context.World.SetOrAdd(target, new ConsumableAttachment(consumable.TemplateID, consumable.SlotGroup, _context.PlayerSlotForEntity(playerEntity)));
		ConsumableChangedSink?.Invoke(target.Id, consumable.TemplateID);
	}

	private Entity FindConsumableTarget(Entity building, Entity playerEntity, ItemMetadata consumable, Vector3 position, out bool aimedAtRefusingUnit)
	{
		bool ownOnly = !string.Equals(consumable.TargetScope, "AnyUnit", StringComparison.OrdinalIgnoreCase);
		float bestDistance = consumable.PurchaseRange;
		Entity best = Entity.Null;

		var query = QueryCache.AllPositionAndOwnerNoneDeadQuery;
		_context.World.Query(in query, (Entity candidate, ref Position candidatePosition, ref Owner owner) =>
		{
			if (candidate == building) return;
			if (ownOnly && owner.PlayerEntity.Value != playerEntity) return;

			float distance = (candidatePosition.Value - position).Length();
			if (distance > bestDistance) return;

			bestDistance = distance;
			best = candidate;
		});

		aimedAtRefusingUnit = best != Entity.Null && !ReceivesConsumable(best);
		return aimedAtRefusingUnit ? Entity.Null : best;
	}

	/// <summary>
	///     Answers whether one unit may be given a consumable. The rule belongs to the unit's type and to the match, so a
	///     map that never states it leaves every unit accepting one.
	/// </summary>
	/// <param name="unit">The unit a purchase would land on.</param>
	/// <returns>Whether that unit accepts a consumable.</returns>
	private bool ReceivesConsumable(Entity unit)
	{
		string definitionId = _context.World.Has<DefinitionId>(unit) ? _context.World.Get<DefinitionId>(unit).Value : string.Empty;
		return _context.MapRules.UnitTypeReceivesConsumables(definitionId);
	}

	private bool CarriesAttachment(Entity unit, ItemMetadata consumable)
	{
		if (!_context.World.TryGet<ConsumableAttachment>(unit, out ConsumableAttachment attachment)) return false;
		if (!attachment.HasDefinition) return false;

		return attachment.IsInGroup(consumable.SlotGroup);
	}
}
