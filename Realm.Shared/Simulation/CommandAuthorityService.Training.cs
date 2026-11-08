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

/// <summary>training, its cost and the cancel and refund path.</summary>
public partial class CommandAuthorityService
	{
	private void HandleTrainCommand(int peerId, NetworkCommand cmd, List<int> rejectedServerEntityIds, ref string rejectionReason)
	{
		string unitId = cmd.ArgString;
		if (!TryGetProductionCosts(unitId, out ProductionCosts costs))
		{
			for (int index = 0; index < cmd.UnitEntityIds.Count; index++)
			{
				RecordCommandRejection(rejectedServerEntityIds, cmd.UnitEntityIds[index], $"unit type '{unitId}' is not registered");
			}

			if (string.IsNullOrEmpty(rejectionReason)) rejectionReason = $"unit type '{unitId}' is not registered";
			return;
		}

		foreach (int serverId in cmd.UnitEntityIds)
		{
			var entity = _targets.Resolve(serverId);
			if (entity == Entity.Null || !IsClientAuthorized(peerId, entity))
			{
				RecordCommandRejection(rejectedServerEntityIds, serverId, $"entity {serverId} is not owned by peer {peerId}");
				if (string.IsNullOrEmpty(rejectionReason)) rejectionReason = $"entity {serverId} is not owned by peer {peerId}";
				continue;
			}

			var ownerComp = _context.World.Get<Owner>(entity);
			var ownerEntity = ownerComp.PlayerEntity.Value;
			if (!_context.World.TryGet<PlayerResources>(ownerEntity, out var res))
			{
				RecordCommandRejection(rejectedServerEntityIds, serverId, "the owning player has no resource pool");
				if (string.IsNullOrEmpty(rejectionReason)) rejectionReason = "the owning player has no resource pool";
				continue;
			}

			if (!TryQueueProduction(entity, ownerEntity, unitId, costs, ref res))
			{
				RecordCommandRejection(rejectedServerEntityIds, serverId, "not enough resources");
				if (string.IsNullOrEmpty(rejectionReason)) rejectionReason = "not enough resources";
			}
		}
	}

	private bool TryGetProductionCosts(string? unitId, out ProductionCosts costs)
	{
		costs = default;
		if (string.IsNullOrEmpty(unitId)) return false;
		if (ProductionCostsProvider == null) return false;

		ProductionCosts? resolved = ProductionCostsProvider(unitId);
		if (resolved == null) return false;

		costs = resolved.Value;
		return true;
	}

	private bool TryQueueProduction(Entity entity, Entity ownerEntity, string unitId, ProductionCosts costs, ref PlayerResources res)
	{
		var goldResourceId = new ResourceId("Gold");
		var woodResourceId = new ResourceId("Wood");
		var stoneResourceId = new ResourceId("Stone");

		int costGold = (int)costs.Gold;
		int costWood = (int)costs.Wood;
		int costStone = (int)costs.Stone;

		if (res[goldResourceId] < costGold || res[woodResourceId] < costWood || res[stoneResourceId] < costStone)
			return false;

		res[goldResourceId] -= costGold;
		res[woodResourceId] -= costWood;
		res[stoneResourceId] -= costStone;
		_context.World.Set(ownerEntity, res);

		if (!_context.World.Has<ProductionQueue>(entity)) _context.World.Add(entity, new ProductionQueue());

		ref var prod = ref _context.World.Get<ProductionQueue>(entity);
		prod.Enqueue(unitId);
		if (prod.Count == 1)
		{
			prod.BuildTime = costs.ProductionTime;
			prod.CurrentProgress = 0f;
		}

		return true;
	}

	private void HandleCancelTrainCommand(int peerId, NetworkCommand cmd)
	{
		foreach (int serverId in cmd.UnitEntityIds)
		{
			var entity = _targets.Resolve(serverId);
			if (entity == Entity.Null || !IsClientAuthorized(peerId, entity)) continue;
			if (!_context.World.Has<ProductionQueue>(entity)) continue;

			CancelProductionQueueItem(entity, cmd.TargetEntityId);
		}
	}

	private void CancelProductionQueueItem(Entity entity, int idx)
	{
		ref var prod = ref _context.World.Get<ProductionQueue>(entity);
		if (idx < 0 || idx >= prod.Count) return;

		string? unitId = prod.RemoveAt(idx);

		if (idx == 0)
		{
			prod.CurrentProgress = 0f;
			string? nextUnitId = prod.Get(0);
			if (TryGetProductionCosts(nextUnitId, out ProductionCosts nextCosts))
			{
				prod.BuildTime = nextCosts.ProductionTime;
			}
		}

		if (TryGetProductionCosts(unitId, out ProductionCosts refunded)) RefundCanceledProduction(entity, refunded);
	}

	private void RefundCanceledProduction(Entity entity, ProductionCosts refunded)
	{
		var ownerComp = _context.World.Get<Owner>(entity);
		var ownerEntity = ownerComp.PlayerEntity.Value;

		if (_context.World.TryGet<PlayerResources>(ownerEntity, out var res))
		{
			res[new ResourceId("Gold")] += (int)refunded.Gold;
			res[new ResourceId("Wood")] += (int)refunded.Wood;
			res[new ResourceId("Stone")] += (int)refunded.Stone;
			_context.World.Set(ownerEntity, res);
		}

	}
}
