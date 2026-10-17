using System;
using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Realm.Client.Core;
using Realm.Client.Network;
using Realm.Client.UI;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Resources;
using Realm.Ecs.Services;
using static Realm.Ecs.Common.ResourceConstants;

namespace Realm.Client.Services;

public class ResourceEconomyService
{
	private float _fDelta;

	private readonly List<(Entity Worker, Gatherer NewState, Vector3? NewDestination)> _tickGatherersToUpdate = new();
	private readonly QueryDescription _gatherQuery = QueryCache.AllPositionAndGathererNoneDeadQuery;
	private readonly QueryDescription _passiveIncomeQuery = QueryCache.AllPlayerResourcesNoneDeadQuery;

	private ForEachWithEntity<Position, Gatherer> _gatherQueryDelegate = null!;
	private ForEachWithEntity<PlayerResources> _passiveIncomeQueryDelegate = null!;

	private DefinitionManager _definitionManagerRef;
	private ResourceId _goldResourceId;
	private ResourceId _woodResourceId;
	private ResourceId _stoneResourceId;

	public Action<int, string, float>? OnResourceDepositedForPlayer;
	public Action<Entity> OnClearUnitOrdersRequested;
	public Action<Entity> OnStopGatheringMovementRequested;
	public Action<Entity> OnPropDepleted;
	public Action<Entity>? OnResourceHarvested;

	public ResourceEconomyService()
	{
		_gatherQueryDelegate = GatherQueryAction;
		_passiveIncomeQueryDelegate = UpdatePassiveIncomeQueryAction;
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

		GameContext.World.Query(in _passiveIncomeQuery, _passiveIncomeQueryDelegate);
		ProcessGatheringTicks();
	}

	private Entity FindWorldEntity()
	{
		Entity worldEntity = Entity.Null;
		var query = QueryCache.AllWorldStateQuery;
		GameContext.World.Query(in query, (Entity entity) => worldEntity = entity);
		return worldEntity;
	}

	private bool GetHarvestingUpgrade(Entity playerEntity)
	{
		if (playerEntity == Entity.Null || !GameContext.World.IsAlive(playerEntity)) return false;
		return GameContext.World.GetFieldOrDefault<PlayerUpgrades, bool>(playerEntity, u => u.HarvestingUpgrade);
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
		if (!res.Value.ContainsKey(resourceId)) return;

		float currentAcc = acc.GetValueOrDefault(resourceId) + _fDelta * rate;
		if (currentAcc >= 1f)
		{
			int add = (int)currentAcc;
			res.Value[resourceId] = (int)Math.Min(ResourceCap, res.Value[resourceId] + add);
			currentAcc -= add;
		}

		acc[resourceId] = currentAcc;
	}

	private void ProcessGatheringTicks()
	{
		_tickGatherersToUpdate.Clear();
		GameContext.World.Query(in _gatherQuery, _gatherQueryDelegate);

		foreach (var (worker, newState, dest) in _tickGatherersToUpdate)
		{
			if (GameContext.World.IsAlive(worker))
			{
				GameContext.World.Set(worker, newState);
				if (dest.HasValue)
				{
					var moveTo = new MoveTo(dest.Value);
					GameContext.World.SetOrAdd(worker, moveTo);
				}
			}
		}
	}

	private Entity FindNearbyResourceNode(Vector3 pos, string type, float radius)
	{
		Entity closest = Entity.Null;
		float closestDist = radius;
		var query = QueryCache.AllPositionAndResourceNodeAndPropIdentityQuery;
		GameContext.World.Query(in query, (Entity entity, ref Position nodePos, ref PropIdentity identity) =>
		{
			string pType = identity.PropId switch
			{
				"goldmine" => "gold",
				"tree" => "wood",
				"rock" => "stone",
				_ => null
			};

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
		var wOwner = GameContext.World.Get<Owner>(entity).PlayerEntity;

		Entity nearestCastle = Entity.Null;
		Vector3 nearestCastlePos = Vector3.Zero;
		float nearestDist = float.MaxValue;

		var castleQuery = QueryCache.AllPositionAndDefinitionIdAndOwnerNoneDeadQuery;
		GameContext.World.Query(in castleQuery, (Entity castleEntity, ref Position castlePos, ref DefinitionId defId, ref Owner ownerComp) =>
		{
			if (defId.Value == "castle" && ownerComp.PlayerEntity == wOwner)
			{
				float dist = Vector3.Distance(currentPos, castlePos.Value);
				if (dist < nearestDist)
				{
					nearestDist = dist;
					nearestCastle = castleEntity;
					nearestCastlePos = castlePos.Value;
				}
			}
		});

		if (nearestCastle == Entity.Null)
		{
			ResetGathererTarget(entity, ref gather);
			return;
		}

		float castleRadius = 6.0f;
		if (Vector3.Distance(currentPos, nearestCastlePos) <= castleRadius)
		{
			DepositResources(entity, ref gather);
			HandleNodeAfterDeposit(entity, ref gather);
		}
		else if (!GameContext.World.Has<MoveTo>(entity))
		{
			_tickGatherersToUpdate.Add((entity, gather, nearestCastlePos));
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
		var ownerEntity = GameContext.World.Get<Owner>(entity).PlayerEntity.Value;

		if (GameContext.World.Has<PlayerResources>(ownerEntity))
		{
			ref var playerRes = ref GameContext.World.Get<PlayerResources>(ownerEntity);
			var resId = gather.ResourceType.AsResourceId(_definitionManagerRef);
			if (playerRes.Value.TryGetValue(resId, out var currentAmount))
			{
				playerRes.Value[resId] = (int)Math.Min(ResourceCap, currentAmount + carry);
			}
		}

		int depositPlayer = GameContext.World.Has<UnitOwnerPlayer>(ownerEntity)
			? GameContext.World.Get<UnitOwnerPlayer>(ownerEntity).PlayerIndex
			: 0;
		OnResourceDepositedForPlayer?.Invoke(depositPlayer, gather.ResourceType, carry);
	}

	private void HandleNodeAfterDeposit(Entity entity, ref Gatherer gather)
	{
		Entity targetNode = gather.TargetEntity;
		bool nodeAlive = GameContext.World.IsAlive(targetNode) && GameContext.World.Has<Position>(targetNode);

		var newState = gather;
		newState.ReturningToBase = false;
		newState.CarriedAmount = 0f;

		if (nodeAlive)
		{
			var dest = GameContext.World.Get<Position>(targetNode).Value;
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
		bool nodeAlive = GameContext.World.IsAlive(targetNode) && GameContext.World.Has<Position>(targetNode) && GameContext.World.Has<ResourceNode>(targetNode);

		if (!nodeAlive)
		{
			FindAlternateNodeOrClearOrders(entity, ref gather, currentPos);
			return;
		}

		var targetPos = GameContext.World.Get<Position>(targetNode).Value;
		float dist = Vector3.Distance(currentPos, targetPos);
		float gatherRange = 3.5f;

		if (dist <= gatherRange)
		{
			MineResource(entity, ref gather, targetNode, currentPos);
		}
		else if (!GameContext.World.Has<MoveTo>(entity))
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
			var dest = GameContext.World.Get<Position>(alternate).Value;
			_tickGatherersToUpdate.Add((entity, newState, dest));
		}
		else
		{
			OnClearUnitOrdersRequested?.Invoke(entity);
		}
	}

	private void MineResource(Entity entity, ref Gatherer gather, Entity targetNode, Vector3 currentPos)
	{
		if (GameContext.World.Has<MoveTo>(entity))
		{
			OnStopGatheringMovementRequested?.Invoke(entity);
		}

		var newState = gather;
		float mineRate = CalculateMineRate(entity);

		ref var resNode = ref GameContext.World.Get<ResourceNode>(targetNode);
		mineRate = Math.Min(mineRate, resNode.Amount);

		resNode.Amount -= mineRate;
		GameContext.World.Set(targetNode, resNode);
		OnResourceHarvested?.Invoke(targetNode);

		newState.CarriedAmount = Math.Min(gather.MaxCapacity, gather.CarriedAmount + mineRate);

		if (resNode.Amount <= 0f)
		{
			OnPropDepleted?.Invoke(targetNode);
		}

		if (newState.CarriedAmount >= gather.MaxCapacity)
		{
			newState.ReturningToBase = true;
			FindNearestCastleAndReturn(entity, ref newState, currentPos);
		}
		else
		{
			_tickGatherersToUpdate.Add((entity, newState, null));
		}
	}

	private float CalculateMineRate(Entity entity)
	{
		float mineRate = 4.0f * _fDelta;
		var ownerPlayerEntity = GameContext.World.Has<Owner>(entity) ? GameContext.World.Get<Owner>(entity).PlayerEntity.Value : Entity.Null;
		if (ownerPlayerEntity != Entity.Null && GetHarvestingUpgrade(ownerPlayerEntity))
		{
			mineRate *= 1.5f;
		}

		return mineRate;
	}

	private void FindNearestCastleAndReturn(Entity entity, ref Gatherer gather, Vector3 currentPos)
	{
		Entity nearestCastle = Entity.Null;
		Vector3 nearestCastlePos = Vector3.Zero;
		float nearestDist = float.MaxValue;
		var wOwner = GameContext.World.Get<Owner>(entity).PlayerEntity;

		var castleQuery = QueryCache.AllPositionAndDefinitionIdAndOwnerNoneDeadQuery;
		GameContext.World.Query(in castleQuery, (Entity castleEntity, ref Position castlePos, ref DefinitionId defId, ref Owner ownerComp) =>
		{
			if (defId.Value == "castle" && ownerComp.PlayerEntity == wOwner)
			{
				float d = Vector3.Distance(currentPos, castlePos.Value);
				if (d < nearestDist)
				{
					nearestDist = d;
					nearestCastle = castleEntity;
					nearestCastlePos = castlePos.Value;
				}
			}
		});

		if (nearestCastle != Entity.Null)
		{
			_tickGatherersToUpdate.Add((entity, gather, nearestCastlePos));
		}
		else
		{
			_tickGatherersToUpdate.Add((entity, gather, null));
		}
	}

	public float GetPlayerWood(int playerIndex)
	{
		if (GameContext.World?.TryGet<ScriptPlayersState>(ECSContext.WorldEntity, out var playersState) == true
		    && playerIndex >= 0 && playerIndex < playersState.Players.Length)
		{
			return playersState.Players[playerIndex].Wood;
		}

		return 0f;
	}

	public void SetPlayerWood(int playerIndex, float amount)
	{
		if (GameContext.World?.TryGet<ScriptPlayersState>(ECSContext.WorldEntity, out var playersState) == true
		    && playerIndex >= 0 && playerIndex < playersState.Players.Length)
		{
			playersState.Players[playerIndex].Wood = Math.Max(0f, amount);
		}
	}

	public void AdjustPlayerWood(int playerIndex, float delta)
	{
		if (GameContext.World?.TryGet<ScriptPlayersState>(ECSContext.WorldEntity, out var playersState) == true
		    && playerIndex >= 0 && playerIndex < playersState.Players.Length)
		{
			playersState.Players[playerIndex].Wood = Math.Max(0f, playersState.Players[playerIndex].Wood + delta);
		}
	}

	private readonly Dictionary<int, float> _playerStoneState = new();

	public float GetPlayerStone(int playerIndex)
	{
		return _playerStoneState.GetValueOrDefault(playerIndex, 0f);
	}

	public void SetPlayerStone(int playerIndex, float amount)
	{
		_playerStoneState[playerIndex] = Math.Max(0f, amount);
	}

	public void AdjustPlayerStone(int playerIndex, float delta)
	{
		_playerStoneState[playerIndex] = Math.Max(0f, _playerStoneState.GetValueOrDefault(playerIndex, 0f) + delta);
	}

	public int GetPlayerCurrentPopulation(int playerIndex)
	{
		if (GameHost.Instance != null)
		{
			var playerEntity = GameHost.Instance.GetPlayerEntityForPlayerIndex(playerIndex);
			if (playerEntity != Entity.Null && GameContext.World != null && GameContext.World.IsAlive(playerEntity) && GameContext.World.Has<PlayerPopulation>(playerEntity))
			{
				return GameContext.World.Get<PlayerPopulation>(playerEntity).Current;
			}
		}

		return 0;
	}

	public void SetPlayerCurrentPopulation(int playerIndex, int current)
	{
		if (GameHost.Instance != null)
		{
			var playerEntity = GameHost.Instance.GetPlayerEntityForPlayerIndex(playerIndex);
			if (playerEntity != Entity.Null && GameContext.World != null && GameContext.World.IsAlive(playerEntity))
			{
				if (GameContext.World.Has<PlayerPopulation>(playerEntity))
				{
					ref var pop = ref GameContext.World.Get<PlayerPopulation>(playerEntity);
					GameContext.World.Set(playerEntity, new PlayerPopulation(current, pop.Max));
				}
				else
				{
					GameContext.World.Add(playerEntity, new PlayerPopulation(current, 0));
				}
			}
		}
	}

	public void SetPlayerMaxPopulation(int playerIndex, int max)
	{
		if (GameHost.Instance != null)
		{
			var playerEntity = GameHost.Instance.GetPlayerEntityForPlayerIndex(playerIndex);
			if (playerEntity != Entity.Null && GameContext.World != null && GameContext.World.IsAlive(playerEntity))
			{
				if (GameContext.World.Has<PlayerPopulation>(playerEntity))
				{
					ref var pop = ref GameContext.World.Get<PlayerPopulation>(playerEntity);
					GameContext.World.Set(playerEntity, new PlayerPopulation(pop.Current, max));
				}
				else
				{
					GameContext.World.Add(playerEntity, new PlayerPopulation(0, max));
				}
			}
		}
	}

	public int GetPlayerMaxPopulation(int playerIndex)
	{
		if (GameHost.Instance != null)
		{
			var playerEntity = GameHost.Instance.GetPlayerEntityForPlayerIndex(playerIndex);
			if (playerEntity != Entity.Null && GameContext.World != null && GameContext.World.IsAlive(playerEntity) && GameContext.World.Has<PlayerPopulation>(playerEntity))
			{
				return GameContext.World.Get<PlayerPopulation>(playerEntity).Max;
			}
		}

		return 0;
	}
}