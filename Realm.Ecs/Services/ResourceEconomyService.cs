using System;
using System.Collections.Generic;
using System.Numerics;
using Realm.Ecs;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Resources;
using Realm.Ecs.Components.Tags;
using Realm.Ecs.Services;
using static Realm.Ecs.Common.ResourceConstants;

namespace Realm.Ecs.Services;

public class ResourceEconomyService
{
	private const float DefaultSwingSeconds = 1f;

	private readonly SimulationContext _context;
	private float _fDelta;

	private readonly List<(Entity Worker, Gatherer NewState, Vector3? NewDestination)> _tickGatherersToUpdate = new();
	private readonly QueryDescription _gatherQuery = QueryCache.AllPositionAndGathererNoneDeadQuery;
	private readonly QueryDescription _passiveIncomeQuery = QueryCache.AllPlayerResourcesNoneDeadQuery;

	private EntityCallback<Position, Gatherer> _gatherQueryDelegate = null!;
	private EntityCallback<PlayerResources> _passiveIncomeQueryDelegate = null!;
	private EntityCallback<Position, DefinitionId, Owner> _dropoffQueryDelegate = null!;

	private Vector3 _dropoffSearchOrigin;
	private PlayerEntity _dropoffPlayer;
	private bool _dropoffRequireDropoffBuilding;
	private Entity _dropoffFound;
	private Vector3 _dropoffFoundPosition;
	private float _dropoffFoundDistance;

	private DefinitionManager _definitionManagerRef = null!;
	private ResourceId _goldResourceId;
	private ResourceId _woodResourceId;
	private ResourceId _stoneResourceId;

	public Action<int, string, float>? OnResourceDepositedForPlayer;

	/// <summary>
	///     States the number of pieces one inventory slot holds for an item. The map's catalog answers it, so the engine
	///     needs no knowledge of which items exist, and a worker with a full slot opens another before it stops gathering.
	/// </summary>
	public Func<string, int>? ItemMaxStacksProvider;
	public Action<Entity> OnClearUnitOrdersRequested = null!;
	public Action<Entity> OnStopGatheringMovementRequested = null!;
	public Action<Entity> OnPropDepleted = null!;
	public Action<Entity>? OnResourceHarvested;

	public ResourceEconomyService(SimulationContext context)
	{
		_context = context;
		_gatherQueryDelegate = GatherQueryAction;
		_passiveIncomeQueryDelegate = UpdatePassiveIncomeQueryAction;
		_dropoffQueryDelegate = FindDropoffCandidate;
		_populationUnitDelegate = PopulationUnitQueryAction;
		_populationQueueDelegate = PopulationQueueQueryAction;
	}

	public void SetRuntimeReferences(
		DefinitionManager definitionManager,
		ResourceId goldResourceId,
		ResourceId woodResourceId,
		ResourceId stoneResourceId)
	{
		_definitionManagerRef = definitionManager;
		_goldResourceId = goldResourceId;
		_woodResourceId = woodResourceId;
		_stoneResourceId = stoneResourceId;
	}

	public void StepEconomy(float delta)
	{
		_fDelta = delta;

		if (_context.MapRules.PassiveResourceIncomeEnabled) _context.World.Query(in _passiveIncomeQuery, _passiveIncomeQueryDelegate);
		ProcessGatheringTicks();
	}

	private Entity FindWorldEntity()
	{
		Entity worldEntity = Entity.Null;
		var query = QueryCache.AllWorldStateQuery;
		_context.World.Query(in query, (Entity entity) => worldEntity = entity);
		return worldEntity;
	}

	private bool GetHarvestingUpgrade(Entity playerEntity)
	{
		if (playerEntity == Entity.Null || !_context.World.IsAlive(playerEntity)) return false;
		return _context.World.GetFieldOrDefault<PlayerUpgrades, bool>(playerEntity, u => u.HarvestingUpgrade);
	}

	private readonly Dictionary<Entity, Dictionary<ResourceId, float>> _accumulators = new();

	private void UpdatePassiveIncomeQueryAction(Entity ent, ref PlayerResources res)
	{
		float goldPerSec = DefaultGoldPerSec;
		float woodPerSec = DefaultWoodPerSec;
		float stonePerSec = DefaultStonePerSec;

		if (GetHarvestingUpgrade(ent))
		{
			goldPerSec *= HarvestingUpgradeMultiplier;
			woodPerSec *= HarvestingUpgradeMultiplier;
			stonePerSec *= HarvestingUpgradeMultiplier;
		}

		if (!_accumulators.TryGetValue(ent, out var acc))
		{
			acc = new Dictionary<ResourceId, float>();
			_accumulators[ent] = acc;
		}

		AddPassiveResource(ref res, acc, _goldResourceId, goldPerSec);
		AddPassiveResource(ref res, acc, _woodResourceId, woodPerSec);
		AddPassiveResource(ref res, acc, _stoneResourceId, stonePerSec);
	}

	private void AddPassiveResource(ref PlayerResources res, Dictionary<ResourceId, float> acc, ResourceId resourceId, float rate)
	{
		if (!res.ContainsKey(resourceId)) return;

		float currentAcc = acc.GetValueOrDefault(resourceId) + _fDelta * rate;
		if (currentAcc >= 1f)
		{
			int add = (int)currentAcc;
			res[resourceId] = (int)Math.Min(ResourceCap, res[resourceId] + add);
			currentAcc -= add;
		}

		acc[resourceId] = currentAcc;
	}

	private void ProcessGatheringTicks()
	{
		_tickGatherersToUpdate.Clear();
		_context.World.Query(in _gatherQuery, _gatherQueryDelegate);

		foreach (var (worker, newState, dest) in _tickGatherersToUpdate)
		{
			if (_context.World.IsAlive(worker))
			{
				_context.World.Set(worker, newState);
				if (dest.HasValue)
				{
					var moveTo = new MoveTo(dest.Value);
					_context.World.SetOrAdd(worker, moveTo);
				}
			}
		}
	}

	private Entity FindNearbyResourceNode(Vector3 pos, string type, float radius)
	{
		Entity closest = Entity.Null;
		float closestDist = radius;
		var query = QueryCache.AllPositionAndResourceNodeAndPropIdentityQuery;
		_context.World.Query(in query, (Entity entity, ref Position nodePos, ref PropIdentity identity) =>
		{
			string? pType = _context.MapRules.GetResourceTypeForNode(identity.PropId);

			if (pType == type && _context.World.Get<ResourceNode>(entity).Amount > 0f)
			{
				float d = Vector3.Distance(pos, nodePos.Value);
				if (d < closestDist)
				{
					closestDist = d;
					closest = entity;
				}
			}
		});
		return closest;
	}

	private void GatherQueryAction(Entity entity, ref Position pos, ref Gatherer gather)
	{
		if (gather.ReturningToBase)
		{
			HandleReturningToBase(entity, ref pos, ref gather);
		}
		else
		{
			HandleGathering(entity, ref pos, ref gather);
		}
	}

	private void HandleReturningToBase(Entity entity, ref Position pos, ref Gatherer gather)
	{
		var currentPos = pos.Value;
		var wOwner = _context.World.Get<Owner>(entity).PlayerEntity;

		FindNearestDropoff(currentPos, wOwner, out var nearestDropoff, out var nearestDropoffPos, out float dropoffRadius);

		if (nearestDropoff == Entity.Null)
		{
			ResetGathererTarget(entity, ref gather);
			return;
		}

		if (Vector3.Distance(currentPos, nearestDropoffPos) <= dropoffRadius)
		{
			DepositResources(entity, ref gather);
			HandleNodeAfterDeposit(entity, ref gather);
		}
		else if (!_context.World.Has<MoveTo>(entity))
		{
			_tickGatherersToUpdate.Add((entity, gather, nearestDropoffPos));
		}
	}

	private void ResetGathererTarget(Entity entity, ref Gatherer gather)
	{
		var newState = gather;
		newState.ReturningToBase = false;
		newState.CarriedAmount = 0;
		newState.TargetEntity = Entity.Null;
		newState.ItemStringId = 0;
		newState.HarvestRemainder = 0f;
		_tickGatherersToUpdate.Add((entity, newState, null));
	}

	private void DepositResources(Entity entity, ref Gatherer gather)
	{
		var ownerEntity = _context.World.Get<Owner>(entity).PlayerEntity.Value;
		float carry = gather.CarriedAmount;

		// A worker hauls the node's own item, so delivering is spending that item: the inventory empties as the purse
		// fills, and what the node lost is exactly what the player may be paid for. A node that predates the item rule
		// still pays out of the carried amount it kept in hand.
		string itemId = StringRegistry.Get(gather.ItemStringId);
		if (!string.IsNullOrEmpty(itemId) && _context.World.Has<Inventory>(entity))
		{
			ref var inventory = ref _context.World.Get<Inventory>(entity);
			int carriedItemCount = inventory.GetItemCount(itemId);
			if (carriedItemCount > 0)
			{
				inventory.RemoveItem(itemId, carriedItemCount);
				carry = carriedItemCount;
			}
		}

		// A map may tax what a worker delivers, and the tax is taken here rather than out of the hit: the node still
		// loses exactly what the worker took, and only the pay into the purse is reduced. The fraction of a resource the
		// tax leaves behind rides on the worker into its next delivery instead of being rounded away, so a taxed economy
		// pays the same total over a run that its advertised rate states.
		float taxRate = Math.Clamp(gather.GatherTaxRate, 0f, 0.99f);
		float earned = carry * (1f - taxRate) + gather.PendingIncomeRemainder;
		int paid = (int)MathF.Floor(earned);
		gather.PendingIncomeRemainder = earned - paid;

		if (_context.World.Has<PlayerResources>(ownerEntity))
		{
			ref var playerRes = ref _context.World.Get<PlayerResources>(ownerEntity);
			var resId = gather.ResourceType.AsResourceId(_definitionManagerRef);
			if (playerRes.TryGetValue(resId, out var currentAmount))
			{
				float room = ResourceCap - currentAmount;
				if (paid > room)
				{
					paid = (int)MathF.Max(0f, MathF.Floor(room));
					gather.PendingIncomeRemainder = 0f;
				}

				playerRes[resId] = currentAmount + paid;
			}
		}

		int depositPlayer = _context.World.Has<UnitOwnerPlayer>(ownerEntity)
			? _context.World.Get<UnitOwnerPlayer>(ownerEntity).PlayerIndex
			: 0;
		OnResourceDepositedForPlayer?.Invoke(depositPlayer, gather.ResourceType, paid);
	}

	private void HandleNodeAfterDeposit(Entity entity, ref Gatherer gather)
	{
		Entity targetNode = gather.TargetEntity;
		bool nodeAlive = _context.World.IsAlive(targetNode)
		                && _context.World.Has<Position>(targetNode)
		                && (!_context.World.TryGet<ResourceNode>(targetNode, out ResourceNode node) || node.Amount > 0f);

		var newState = gather;
		newState.ReturningToBase = false;
		newState.CarriedAmount = 0f;

		if (nodeAlive)
		{
			var dest = _context.World.Get<Position>(targetNode).Value;
			_tickGatherersToUpdate.Add((entity, newState, dest));
		}
		else
		{
			newState.TargetEntity = Entity.Null;
			newState.ItemStringId = 0;
			_tickGatherersToUpdate.Add((entity, newState, null));
		}
	}

	private void HandleGathering(Entity entity, ref Position pos, ref Gatherer gather)
	{
		var currentPos = pos.Value;
		Entity targetNode = gather.TargetEntity;
		bool nodeAlive = _context.World.IsAlive(targetNode) && _context.World.Has<Position>(targetNode) && _context.World.Has<ResourceNode>(targetNode);

		if (!nodeAlive)
		{
			FindAlternateNodeOrClearOrders(entity, ref gather, currentPos);
			return;
		}

		var targetPos = _context.World.Get<Position>(targetNode).Value;
		float dist = Vector3.Distance(currentPos, targetPos);
		float gatherRange = GatherReach;
		if (_context.World.TryGet(targetNode, out CollisionRadius nodeCollision)) gatherRange = Math.Max(gatherRange, nodeCollision.Value + 2.5f);

		if (dist <= gatherRange)
		{
			MineResource(entity, ref gather, targetNode, currentPos);
		}
		else if (!_context.World.Has<MoveTo>(entity))
		{
			_tickGatherersToUpdate.Add((entity, gather, targetPos));
		}
	}

	private void FindAlternateNodeOrClearOrders(Entity entity, ref Gatherer gather, Vector3 currentPos)
	{
		Entity alternate = FindNearbyResourceNode(currentPos, gather.ResourceType, 25.0f);
		if (alternate != Entity.Null)
		{
			var newState = gather;
			newState.TargetEntity = alternate;
			var dest = _context.World.Get<Position>(alternate).Value;
			_tickGatherersToUpdate.Add((entity, newState, dest));
		}
		else
		{
			OnClearUnitOrdersRequested?.Invoke(entity);
		}
	}

	/// <summary>
	///     Works the node the worker is standing at, one hit at a time. The hit is the unit's own attack, resolved against
	///     the node's resistances, so the resource a node loses is the damage the worker would have dealt in combat and the
	///     pieces it yields are that damage in items. A worker that cannot hold another piece carries what it has home,
	///     which is what makes the walk, the hit rate and the damage all show up in income.
	/// </summary>
	private void MineResource(Entity entity, ref Gatherer gather, Entity targetNode, Vector3 currentPos)
	{
		if (_context.World.Has<MoveTo>(entity))
		{
			OnStopGatheringMovementRequested?.Invoke(entity);
		}

		var newState = gather;
		bool harvestsByDamage = _context.World.TryGet<Harvestable>(targetNode, out Harvestable harvestable);

		if (harvestsByDamage)
		{
			newState.ItemStringId = harvestable.ItemStringId;
			newState.SwingSeconds = _context.CombatDamageService.GetGatherInterval(entity);
		}
		else
		{
			ref var legacyNode = ref _context.World.Get<ResourceNode>(targetNode);
			if (newState.SwingAmount <= 0f) newState.SwingAmount = legacyNode.HarvestRate > 0f ? legacyNode.HarvestRate : 4f;
			if (newState.SwingSeconds <= 0f) newState.SwingSeconds = DefaultSwingSeconds;
		}

		newState.SwingProgress += _fDelta;
		bool harvestedAnything = false;

		while (newState.SwingProgress >= newState.SwingSeconds)
		{
			float nodeAmount = _context.World.Get<ResourceNode>(targetNode).Amount;
			if (nodeAmount <= 0f) break;

			if (harvestsByDamage)
			{
				string itemId = harvestable.ItemId;
				int maxStacks = ItemMaxStacksProvider != null ? ItemMaxStacksProvider(itemId) : 1;
				int room = _context.World.Has<Inventory>(entity)
					? _context.World.Get<Inventory>(entity).AvailableRoom(itemId, maxStacks)
					: 0;
				if (room <= 0) break;

				float dealt = _context.CombatDamageService.HarvestFromNode(entity, targetNode, ref newState.HarvestRemainder, room);
				newState.SwingProgress -= newState.SwingSeconds;
				if (dealt <= 0f) continue;

				int pieces = Math.Min((int)MathF.Floor(dealt * harvestable.YieldPerDamage), room);
				if (pieces <= 0) continue;

				ref var inventory = ref _context.World.Get<Inventory>(entity);
				inventory.AddItem(itemId, pieces, maxStacks);
				harvestedAnything = true;
			}
			else
			{
				float swingAmount = newState.SwingAmount;
				var ownerPlayerEntity = _context.World.Has<Owner>(entity) ? _context.World.Get<Owner>(entity).PlayerEntity.Value : Entity.Null;
				if (ownerPlayerEntity != Entity.Null && GetHarvestingUpgrade(ownerPlayerEntity)) swingAmount *= 1.5f;

				if (newState.MaxCapacity - newState.CarriedAmount < MathF.Min(swingAmount, nodeAmount)) break;

				newState.SwingProgress -= newState.SwingSeconds;
				float taken = MathF.Min(swingAmount, nodeAmount);
				ref var resNode = ref _context.World.Get<ResourceNode>(targetNode);
				resNode.Amount -= taken;
				_context.World.Set(targetNode, resNode);
				newState.CarriedAmount += taken;
				harvestedAnything = true;
			}
		}

		float remaining = _context.World.TryGet<ResourceNode>(targetNode, out ResourceNode node) ? node.Amount : 0f;
		if (harvestedAnything) OnResourceHarvested?.Invoke(targetNode);
		if (remaining <= 0f) OnPropDepleted?.Invoke(targetNode);

		bool handsFull = harvestsByDamage
			? !CanHoldMore(entity, harvestable.ItemId, ItemMaxStacksProvider != null ? ItemMaxStacksProvider(harvestable.ItemId) : 1)
			: newState.CarriedAmount > 0f && newState.MaxCapacity - newState.CarriedAmount < newState.SwingAmount;

		if (handsFull || remaining <= 0f)
		{
			newState.ReturningToBase = true;
			FindNearestDropoffAndReturn(entity, ref newState, currentPos);
		}
		else
		{
			// The hit in progress is part of the state: without writing it back each tick the worker would restart
			// the same hit forever and a node would never be touched.
			_tickGatherersToUpdate.Add((entity, newState, null));
		}
	}

	private bool CanHoldMore(Entity entity, string itemId, int maxStacks)
	{
		if (!_context.World.Has<Inventory>(entity)) return false;

		return _context.World.Get<Inventory>(entity).AvailableRoom(itemId, maxStacks) > 0;
	}

	private void FindNearestDropoff(Vector3 currentPos, PlayerEntity wOwner, out Entity nearestDropoff, out Vector3 nearestDropoffPos, out float dropoffRadius)
	{
		// A dropoff building first, and any finished building of the owner's only when the map names none. Both passes
		// run through one cached callback that reads where it is looking from the fields above instead of capturing it:
		// this is walked for every worker carrying something home, every tick, and the closure it used to build was
		// thrown away on each of them.
		_dropoffSearchOrigin = currentPos;
		_dropoffPlayer = wOwner;
		_dropoffFound = Entity.Null;
		_dropoffFoundPosition = Vector3.Zero;
		_dropoffFoundDistance = float.MaxValue;
		float foundRadius = 6.0f;

		var buildingQuery = QueryCache.AllPositionAndDefinitionIdAndOwnerNoneDeadQuery;
		_dropoffRequireDropoffBuilding = true;
		_context.World.Query(in buildingQuery, _dropoffQueryDelegate);

		if (_dropoffFound == Entity.Null)
		{
			_dropoffRequireDropoffBuilding = false;
			_context.World.Query(in buildingQuery, _dropoffQueryDelegate);
		}

		if (_dropoffFound != Entity.Null && _context.World.Has<CollisionRadius>(_dropoffFound))
		{
			foundRadius = Math.Max(4f, _context.World.Get<CollisionRadius>(_dropoffFound).Value + 2.0f);
		}

		nearestDropoff = _dropoffFound;
		nearestDropoffPos = _dropoffFoundPosition;
		dropoffRadius = foundRadius;
	}

	private void FindDropoffCandidate(Entity buildingEntity, ref Position buildingPos, ref DefinitionId definitionId, ref Owner owner)
	{
		if (owner.PlayerEntity != _dropoffPlayer) return;

		bool candidate = _dropoffRequireDropoffBuilding
			? _context.MapRules.IsResourceDropoffBuilding(definitionId.Value)
			: _context.World.Has<Building>(buildingEntity) && !_context.World.Has<ConstructionState>(buildingEntity);
		if (candidate && _context.World.Has<Dormant>(buildingEntity)) candidate = false;
		if (!candidate) return;

		float distance = Vector3.Distance(_dropoffSearchOrigin, buildingPos.Value);
		if (distance >= _dropoffFoundDistance) return;

		_dropoffFoundDistance = distance;
		_dropoffFound = buildingEntity;
		_dropoffFoundPosition = buildingPos.Value;
	}

	private void FindNearestDropoffAndReturn(Entity entity, ref Gatherer gather, Vector3 currentPos)
	{
		var wOwner = _context.World.Get<Owner>(entity).PlayerEntity;
		FindNearestDropoff(currentPos, wOwner, out var nearestDropoff, out var nearestDropoffPos, out _);

		if (nearestDropoff != Entity.Null)
		{
			_tickGatherersToUpdate.Add((entity, gather, nearestDropoffPos));
		}
		else
		{
			_tickGatherersToUpdate.Add((entity, gather, null));
		}
	}

	public float GetPlayerWood(int playerIndex)
	{
		return GetPlayerStockpile(playerIndex, wood: true);
	}

	// The stockpile component is authoritative; the script slot is only a mirror kept up to date by script writes.
	private float GetPlayerStockpile(int playerIndex, bool wood)
	{
		var playerEntity = _context.GetPlayerEntity(playerIndex);
		if (playerEntity != Entity.Null && _context.World != null && _context.World.IsAlive(playerEntity) && _context.World.Has<PlayerResources>(playerEntity))
		{
			ref var resources = ref _context.World.Get<PlayerResources>(playerEntity);
			return resources[wood ? _context.PlayerResourceService.WoodResourceId : _context.PlayerResourceService.StoneResourceId];
		}

		var worldEntity = _context.WorldEntity;
		if (_context.World != null && _context.World.IsAlive(worldEntity) && _context.World.Has<ScriptPlayersState>(worldEntity))
		{
			ref var playersState = ref _context.World.Get<ScriptPlayersState>(worldEntity);
			if (playersState.IsValidSlot(playerIndex)) return wood ? playersState.Get(playerIndex).Wood : playersState.Get(playerIndex).Stone;
		}

		return 0f;
	}

	private void UpdatePlayerResourceECS(int playerIndex, float amount, bool isWood)
	{
		var worldEntity = _context.WorldEntity;
		if (_context.World != null && _context.World.IsAlive(worldEntity) && _context.World.Has<ScriptPlayersState>(worldEntity))
		{
			ref var playersState = ref _context.World.Get<ScriptPlayersState>(worldEntity);
			if (playersState.IsValidSlot(playerIndex))
			{
				if (isWood)
					playersState.SetWood(playerIndex, amount);
				else
					playersState.SetStone(playerIndex, amount);

				var player = playersState.Get(playerIndex);
				_context.World.GetBuffer<ScriptPlayer>().Set(playerIndex, in player);
			}
		}

		if (_context.WorldEntity != Entity.Null)
		{
			var pe = _context.GetPlayerEntity(playerIndex);
			if (pe != Entity.Null && _context.World != null && _context.World.IsAlive(pe) && _context.World.Has<PlayerResources>(pe))
			{
				ref var res = ref _context.World.Get<PlayerResources>(pe);
				res[isWood ? _context.PlayerResourceService.WoodResourceId : _context.PlayerResourceService.StoneResourceId] = (int)amount;
			}
		}
	}

	public void SetPlayerWood(int playerIndex, float amount)
	{
		float clamped = Math.Max(0f, amount);
		UpdatePlayerResourceECS(playerIndex, clamped, true);
	}

	public void AdjustPlayerWood(int playerIndex, float delta)
	{
		SetPlayerWood(playerIndex, GetPlayerStockpile(playerIndex, wood: true) + delta);
	}

	public float GetPlayerStone(int playerIndex)
	{
		return GetPlayerStockpile(playerIndex, wood: false);
	}

	public void SetPlayerStone(int playerIndex, float amount)
	{
		float clamped = Math.Max(0f, amount);
		UpdatePlayerResourceECS(playerIndex, clamped, false);
	}

	public void AdjustPlayerStone(int playerIndex, float delta)
	{
		SetPlayerStone(playerIndex, GetPlayerStockpile(playerIndex, wood: false) + delta);
	}

	private readonly int[] _populationTotals = new int[EcsWorld.MAX_PLAYERS];

	private EntityCallback<UnitOwnerPlayer, DefinitionId> _populationUnitDelegate = null!;
	private EntityCallback<ProductionQueue, UnitOwnerPlayer> _populationQueueDelegate = null!;

	public void RecomputeCurrentPopulation()
	{
		var world = _context.World;
		if (world == null || _populationUnitDelegate == null) return;

		Array.Clear(_populationTotals);
		world.Query(in QueryCache.AllUnitOwnerPlayerAndDefinitionIdNoneDeadQuery, _populationUnitDelegate);
		world.Query(in QueryCache.AllProductionQueueAndUnitOwnerPlayerQuery, _populationQueueDelegate);

		for (int playerIndex = 0; playerIndex < EcsWorld.MAX_PLAYERS; playerIndex++)
		{
			var playerEntity = _context.GetPlayerEntity(playerIndex);
			if (playerEntity == Entity.Null || !world.IsAlive(playerEntity)) continue;
			if (!world.Has<PlayerPopulation>(playerEntity)) continue;

			ref var population = ref world.Get<PlayerPopulation>(playerEntity);
			if (population.Current == _populationTotals[playerIndex]) continue;
			population.Current = _populationTotals[playerIndex];
		}
	}

	private void PopulationUnitQueryAction(Entity entity, ref UnitOwnerPlayer owner, ref DefinitionId definition)
	{
		if (_context.World.Has<BypassPopulationTag>(entity)) return;
		AccumulatePopulationCost(owner.PlayerIndex, definition.Value);
	}

	private void PopulationQueueQueryAction(Entity entity, ref ProductionQueue queue, ref UnitOwnerPlayer owner)
	{
		for (int index = 0; index < queue.Count; index++)
		{
			string? queuedUnitId = queue.Get(index);
			if (queuedUnitId != null) AccumulatePopulationCost(owner.PlayerIndex, queuedUnitId);
		}
	}

	private void AccumulatePopulationCost(int playerIndex, string definitionId)
	{
		if (playerIndex < 0 || playerIndex >= EcsWorld.MAX_PLAYERS) return;

		int populationCost = _context.MapRules.GetPopulationCost(definitionId);
		if (populationCost <= 0) return;
		_populationTotals[playerIndex] += populationCost;
	}

	public int GetPlayerCurrentPopulation(int playerIndex)
	{
		if (_context.WorldEntity != Entity.Null)
		{
			var playerEntity = _context.GetPlayerEntity(playerIndex);
			if (playerEntity != Entity.Null && _context.World != null && _context.World.IsAlive(playerEntity) && _context.World.Has<PlayerPopulation>(playerEntity))
			{
				return _context.World.Get<PlayerPopulation>(playerEntity).Current;
			}
		}

		return 0;
	}

	public void SetPlayerCurrentPopulation(int playerIndex, int current)
	{
		if (_context.WorldEntity != Entity.Null)
		{
			var playerEntity = _context.GetPlayerEntity(playerIndex);
			if (playerEntity != Entity.Null && _context.World != null && _context.World.IsAlive(playerEntity))
			{
				if (_context.World.Has<PlayerPopulation>(playerEntity))
				{
					ref var pop = ref _context.World.Get<PlayerPopulation>(playerEntity);
					_context.World.Set(playerEntity, new PlayerPopulation(current, pop.Max));
				}
				else
				{
					_context.World.Add(playerEntity, new PlayerPopulation(current, 0));
				}
			}
		}
	}

	public void SetPlayerMaxPopulation(int playerIndex, int max)
	{
		if (_context.WorldEntity != Entity.Null)
		{
			var playerEntity = _context.GetPlayerEntity(playerIndex);
			if (playerEntity != Entity.Null && _context.World != null && _context.World.IsAlive(playerEntity))
			{
				if (_context.World.Has<PlayerPopulation>(playerEntity))
				{
					ref var pop = ref _context.World.Get<PlayerPopulation>(playerEntity);
					_context.World.Set(playerEntity, new PlayerPopulation(pop.Current, max));
				}
				else
				{
					_context.World.Add(playerEntity, new PlayerPopulation(0, max));
				}
			}
		}
	}

	public int GetPlayerMaxPopulation(int playerIndex)
	{
		if (_context.WorldEntity != Entity.Null)
		{
			var playerEntity = _context.GetPlayerEntity(playerIndex);
			if (playerEntity != Entity.Null && _context.World != null && _context.World.IsAlive(playerEntity) && _context.World.Has<PlayerPopulation>(playerEntity))
			{
				return _context.World.Get<PlayerPopulation>(playerEntity).Max;
			}
		}

		return 0;
	}
}