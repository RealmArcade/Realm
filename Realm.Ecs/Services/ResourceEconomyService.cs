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
	private readonly SimulationContext _context;
	private float _fDelta;

	private readonly List<(Entity Worker, Gatherer NewState, Vector3? NewDestination)> _tickGatherersToUpdate = new();
	private readonly QueryDescription _gatherQuery = QueryCache.AllPositionAndGathererNoneDeadQuery;
	private readonly QueryDescription _passiveIncomeQuery = QueryCache.AllPlayerResourcesNoneDeadQuery;

	private EntityCallback<Position, Gatherer> _gatherQueryDelegate = null!;
	private EntityCallback<PlayerResources> _passiveIncomeQueryDelegate = null!;

	private DefinitionManager _definitionManagerRef;
	private ResourceId _goldResourceId;
	private ResourceId _woodResourceId;
	private ResourceId _stoneResourceId;

	public Action<int, string, float>? OnResourceDepositedForPlayer;
	public Action<Entity> OnClearUnitOrdersRequested;
	public Action<Entity> OnStopGatheringMovementRequested;
	public Action<Entity> OnPropDepleted;
	public Action<Entity>? OnResourceHarvested;

	public ResourceEconomyService(SimulationContext context)
	{
		_context = context;
		_gatherQueryDelegate = GatherQueryAction;
		_passiveIncomeQueryDelegate = UpdatePassiveIncomeQueryAction;
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

		_context.World.Query(in _passiveIncomeQuery, _passiveIncomeQueryDelegate);
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
			string pType = _context.MapRules.GetResourceTypeForNode(identity.PropId);

			if (pType == type)
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
		_tickGatherersToUpdate.Add((entity, newState, null));
	}

	private void DepositResources(Entity entity, ref Gatherer gather)
	{
		float carry = gather.CarriedAmount;
		var ownerEntity = _context.World.Get<Owner>(entity).PlayerEntity.Value;

		if (_context.World.Has<PlayerResources>(ownerEntity))
		{
			ref var playerRes = ref _context.World.Get<PlayerResources>(ownerEntity);
			var resId = gather.ResourceType.AsResourceId(_definitionManagerRef);
			if (playerRes.TryGetValue(resId, out var currentAmount))
			{
				playerRes[resId] = (int)Math.Min(ResourceCap, currentAmount + carry);
			}
		}

		int depositPlayer = _context.World.Has<UnitOwnerPlayer>(ownerEntity)
			? _context.World.Get<UnitOwnerPlayer>(ownerEntity).PlayerIndex
			: 0;
		OnResourceDepositedForPlayer?.Invoke(depositPlayer, gather.ResourceType, carry);
	}

	private void HandleNodeAfterDeposit(Entity entity, ref Gatherer gather)
	{
		Entity targetNode = gather.TargetEntity;
		bool nodeAlive = _context.World.IsAlive(targetNode) && _context.World.Has<Position>(targetNode);

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
		float gatherRange = 3.5f;

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

	private void MineResource(Entity entity, ref Gatherer gather, Entity targetNode, Vector3 currentPos)
	{
		if (_context.World.Has<MoveTo>(entity))
		{
			OnStopGatheringMovementRequested?.Invoke(entity);
		}

		var newState = gather;
		float mineRate = CalculateMineRate(entity);

		ref var resNode = ref _context.World.Get<ResourceNode>(targetNode);
		mineRate = Math.Min(mineRate, resNode.Amount);

		resNode.Amount -= mineRate;
		_context.World.Set(targetNode, resNode);
		OnResourceHarvested?.Invoke(targetNode);

		newState.CarriedAmount = Math.Min(gather.MaxCapacity, gather.CarriedAmount + mineRate);

		if (resNode.Amount <= 0f)
		{
			OnPropDepleted?.Invoke(targetNode);
		}

		if (newState.CarriedAmount >= gather.MaxCapacity)
		{
			newState.ReturningToBase = true;
			FindNearestDropoffAndReturn(entity, ref newState, currentPos);
		}
		else
		{
			_tickGatherersToUpdate.Add((entity, newState, null));
		}
	}

	private float CalculateMineRate(Entity entity)
	{
		float mineRate = 4.0f * _fDelta;
		var ownerPlayerEntity = _context.World.Has<Owner>(entity) ? _context.World.Get<Owner>(entity).PlayerEntity.Value : Entity.Null;
		if (ownerPlayerEntity != Entity.Null && GetHarvestingUpgrade(ownerPlayerEntity))
		{
			mineRate *= 1.5f;
		}

		return mineRate;
	}

	private void FindNearestDropoff(Vector3 currentPos, PlayerEntity wOwner, out Entity nearestDropoff, out Vector3 nearestDropoffPos, out float dropoffRadius)
	{
		Entity foundDropoff = Entity.Null;
		Vector3 foundDropoffPos = Vector3.Zero;
		float foundRadius = 6.0f;
		float nearestDist = float.MaxValue;

		var buildingQuery = QueryCache.AllPositionAndDefinitionIdAndOwnerNoneDeadQuery;
		_context.World.Query(in buildingQuery, (Entity bldEntity, ref Position bldPos, ref DefinitionId defId, ref Owner ownerComp) =>
		{
			if (ownerComp.PlayerEntity == wOwner && _context.MapRules.IsResourceDropoffBuilding(defId.Value))
			{
				float dist = Vector3.Distance(currentPos, bldPos.Value);
				if (dist < nearestDist)
				{
					nearestDist = dist;
					foundDropoff = bldEntity;
					foundDropoffPos = bldPos.Value;
				}
			}
		});

		if (foundDropoff == Entity.Null)
		{
			_context.World.Query(in buildingQuery, (Entity bldEntity, ref Position bldPos, ref DefinitionId defId, ref Owner ownerComp) =>
			{
				if (ownerComp.PlayerEntity == wOwner && _context.World.Has<Building>(bldEntity) && !_context.World.Has<ConstructionState>(bldEntity))
				{
					float dist = Vector3.Distance(currentPos, bldPos.Value);
					if (dist < nearestDist)
					{
						nearestDist = dist;
						foundDropoff = bldEntity;
						foundDropoffPos = bldPos.Value;
					}
				}
			});
		}

		if (foundDropoff != Entity.Null && _context.World.Has<CollisionRadius>(foundDropoff))
		{
			foundRadius = Math.Max(4f, _context.World.Get<CollisionRadius>(foundDropoff).Value + 2.0f);
		}

		nearestDropoff = foundDropoff;
		nearestDropoffPos = foundDropoffPos;
		dropoffRadius = foundRadius;
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
		var worldEntity = _context.WorldEntity;
		if (_context.World != null && _context.World.IsAlive(worldEntity) && _context.World.Has<ScriptPlayersState>(worldEntity))
		{
			ref var playersState = ref _context.World.Get<ScriptPlayersState>(worldEntity);
			if (playersState.IsValidSlot(playerIndex)) return playersState.Get(playerIndex).Wood;
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
		var worldEntity = _context.WorldEntity;
		if (_context.World != null && _context.World.IsAlive(worldEntity) && _context.World.Has<ScriptPlayersState>(worldEntity))
		{
			ref var playersState = ref _context.World.Get<ScriptPlayersState>(worldEntity);
			if (playersState.IsValidSlot(playerIndex)) SetPlayerWood(playerIndex, playersState.Get(playerIndex).Wood + delta);
		}
	}

	private readonly Dictionary<int, float> _playerStoneState = new();

	public float GetPlayerStone(int playerIndex)
	{
		return _playerStoneState.GetValueOrDefault(playerIndex, 0f);
	}

	public void SetPlayerStone(int playerIndex, float amount)
	{
		float clamped = Math.Max(0f, amount);
		_playerStoneState[playerIndex] = clamped;
		UpdatePlayerResourceECS(playerIndex, clamped, false);
	}

	public void AdjustPlayerStone(int playerIndex, float delta)
	{
		SetPlayerStone(playerIndex, _playerStoneState.GetValueOrDefault(playerIndex, 0f) + delta);
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