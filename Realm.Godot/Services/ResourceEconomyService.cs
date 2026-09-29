using Arch.Core;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Resources;
using Realm.Ecs.Components.Tags;
using Realm.Ecs.Services;
using System;
using System.Collections.Generic;
using static Realm.Ecs.Common.ResourceConstants;

internal class ResourceEconomyService
{
	private readonly WorldAccessor _ecsWorldAccessor;
	private World EcsWorld => _ecsWorldAccessor.Current;

	private float _fDelta;

	private readonly List<(Entity Worker, Gatherer NewState, System.Numerics.Vector3? NewDestination)> _tickGatherersToUpdate = new();
	private readonly QueryDescription _gatherQuery = Realm.Ecs.Common.QueryCache.AllPositionAndGathererNoneDeadQuery;
	private readonly QueryDescription _passiveIncomeQuery = Realm.Ecs.Common.QueryCache.AllPlayerResourcesNoneDeadQuery;

	private ForEachWithEntity<Position, Gatherer> _gatherQueryDelegate = null!;
	private ForEachWithEntity<PlayerResources> _passiveIncomeQueryDelegate = null!;

	private DefinitionManager _definitionManagerRef;
	private ResourceId _goldResourceId;
	private ResourceId _woodResourceId;
	private ResourceId _stoneResourceId;

	public Action<string, float> OnResourceDepositedForPlayer;
	public Action<Entity> OnClearUnitOrdersRequested;
	public Action<Entity> OnStopGatheringMovementRequested;
	public Action<Entity> OnPropDepleted;
	public Action<Entity>? OnResourceHarvested;

	public ResourceEconomyService(WorldAccessor ecsWorldAccessor)
	{
		_ecsWorldAccessor = ecsWorldAccessor;
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

		EcsWorld.Query(in _passiveIncomeQuery, _passiveIncomeQueryDelegate);
		ProcessGatheringTicks();
	}

	private Entity FindWorldEntity()
	{
		Entity worldEntity = Entity.Null;
		var query = Realm.Ecs.Common.QueryCache.AllWorldStateQuery;
		EcsWorld.Query(in query, (Entity entity) => worldEntity = entity);
		return worldEntity;
	}

	private bool GetHarvestingUpgrade()
	{
		var worldEntity = FindWorldEntity();
		if (worldEntity == Entity.Null) return false;
		var playerEntity = EcsWorld.Get<NetworkMappingState>(worldEntity).PlayerEntity;
		return EcsWorld.GetFieldOrDefault<PlayerUpgrades, bool>(playerEntity, u => u.HarvestingUpgrade);
	}

	private readonly Dictionary<Entity, Dictionary<ResourceId, float>> _accumulators = new();

	private void UpdatePassiveIncomeQueryAction(Entity ent, ref PlayerResources res)
	{
		float goldPerSec = DefaultGoldPerSec;
		float woodPerSec = DefaultWoodPerSec;
		float stonePerSec = DefaultStonePerSec;

		var worldEntity = FindWorldEntity();
		if (worldEntity != Entity.Null && EcsWorld.Has<NetworkMappingState>(worldEntity))
		{
			var playerEntityForUpgrade = EcsWorld.Get<NetworkMappingState>(worldEntity).PlayerEntity;
			if (ent == playerEntityForUpgrade && GetHarvestingUpgrade())
			{
				goldPerSec *= HarvestingUpgradeMultiplier;
				woodPerSec *= HarvestingUpgradeMultiplier;
				stonePerSec *= HarvestingUpgradeMultiplier;
			}
		}

		if (!_accumulators.TryGetValue(ent, out var acc))
		{
			acc = new Dictionary<ResourceId, float>();
			_accumulators[ent] = acc;
		}

		if (res.Value.ContainsKey(_goldResourceId))
		{
			float currentAcc = acc.GetValueOrDefault(_goldResourceId) + _fDelta * goldPerSec;
			if (currentAcc >= 1f)
			{
				int add = (int)currentAcc;
				res.Value[_goldResourceId] = (int)Math.Min(ResourceCap, res.Value[_goldResourceId] + add);
				currentAcc -= add;
			}
			acc[_goldResourceId] = currentAcc;
		}

		if (res.Value.ContainsKey(_woodResourceId))
		{
			float currentAcc = acc.GetValueOrDefault(_woodResourceId) + _fDelta * woodPerSec;
			if (currentAcc >= 1f)
			{
				int add = (int)currentAcc;
				res.Value[_woodResourceId] = (int)Math.Min(ResourceCap, res.Value[_woodResourceId] + add);
				currentAcc -= add;
			}
			acc[_woodResourceId] = currentAcc;
		}

		if (res.Value.ContainsKey(_stoneResourceId))
		{
			float currentAcc = acc.GetValueOrDefault(_stoneResourceId) + _fDelta * stonePerSec;
			if (currentAcc >= 1f)
			{
				int add = (int)currentAcc;
				res.Value[_stoneResourceId] = (int)Math.Min(ResourceCap, res.Value[_stoneResourceId] + add);
				currentAcc -= add;
			}
			acc[_stoneResourceId] = currentAcc;
		}
	}

	private void ProcessGatheringTicks()
	{
		_tickGatherersToUpdate.Clear();
		EcsWorld.Query(in _gatherQuery, _gatherQueryDelegate);

		foreach (var (worker, newState, dest) in _tickGatherersToUpdate)
		{
			if (EcsWorld.IsAlive(worker))
			{
				EcsWorld.Set(worker, newState);
				if (dest.HasValue)
				{
					var moveTo = new MoveTo(dest.Value);
					EcsWorld.SetOrAdd(worker, moveTo);
				}
			}
		}
	}

	private Entity FindNearbyResourceNode(System.Numerics.Vector3 pos, string type, float radius)
	{
		Entity closest = Entity.Null;
		float closestDist = radius;
		var query = Realm.Ecs.Common.QueryCache.AllPositionAndResourceNodeAndPropIdentityQuery;
		EcsWorld.Query(in query, (Entity entity, ref Position nodePos, ref PropIdentity identity) =>
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
				float d = System.Numerics.Vector3.Distance(pos, nodePos.Value);
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
		var currentPos = pos.Value;

		if (gather.ReturningToBase)
		{
			Entity nearestCastle = Entity.Null;
			System.Numerics.Vector3 nearestCastlePos = System.Numerics.Vector3.Zero;
			float nearestDist = float.MaxValue;
			var wOwner = EcsWorld.Get<Owner>(entity).PlayerEntity;

			var castleQuery = Realm.Ecs.Common.QueryCache.AllPositionAndDefinitionIdAndOwnerNoneDeadQuery;
			EcsWorld.Query(in castleQuery, (Entity castleEntity, ref Position castlePos, ref DefinitionId defId, ref Owner ownerComp) =>
			{
				if (defId.Value == "castle" && ownerComp.PlayerEntity == wOwner)
				{
					float dist = System.Numerics.Vector3.Distance(currentPos, castlePos.Value);
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
				var newState = gather;
				newState.ReturningToBase = false;
				newState.CarriedAmount = 0;
				_tickGatherersToUpdate.Add((entity, newState, null));
				return;
			}

			float castleRadius = 6.0f;
			if (System.Numerics.Vector3.Distance(currentPos, nearestCastlePos) <= castleRadius)
			{
				float carry = gather.CarriedAmount;
				var ownerEntity = EcsWorld.Get<Owner>(entity).PlayerEntity.Value;
				if (EcsWorld.Has<PlayerResources>(ownerEntity))
				{
					ref var playerRes = ref EcsWorld.Get<PlayerResources>(ownerEntity);
					var resId = gather.ResourceType.AsResourceId(_definitionManagerRef);
					if (playerRes.Value.TryGetValue(resId, out var currentAmount))
					{
						playerRes.Value[resId] = (int)Math.Min(ResourceCap, currentAmount + carry);
					}
				}

				var worldEntity = FindWorldEntity();
				if (worldEntity != Entity.Null)
				{
					var playerEntityForAlert = EcsWorld.Get<NetworkMappingState>(worldEntity).PlayerEntity;
					if (ownerEntity == playerEntityForAlert)
					{
						OnResourceDepositedForPlayer?.Invoke(gather.ResourceType, carry);
					}
				}

				Entity targetNode = gather.TargetEntity;
				bool nodeAlive = EcsWorld.IsAlive(targetNode) && EcsWorld.Has<Position>(targetNode);

				if (nodeAlive)
				{
					var newState = gather;
					newState.ReturningToBase = false;
					newState.CarriedAmount = 0f;
					var dest = EcsWorld.Get<Position>(targetNode).Value;
					_tickGatherersToUpdate.Add((entity, newState, dest));
				}
				else
				{
					var newState = gather;
					newState.ReturningToBase = false;
					newState.CarriedAmount = 0f;
					newState.TargetEntity = Entity.Null;
					_tickGatherersToUpdate.Add((entity, newState, null));
				}
			}
			else
			{
				if (!EcsWorld.Has<MoveTo>(entity))
				{
					_tickGatherersToUpdate.Add((entity, gather, nearestCastlePos));
				}
			}
		}
		else
		{
			Entity targetNode = gather.TargetEntity;
			bool nodeAlive = EcsWorld.IsAlive(targetNode) && EcsWorld.Has<Position>(targetNode) && EcsWorld.Has<ResourceNode>(targetNode);

			if (!nodeAlive)
			{
				Entity alternate = FindNearbyResourceNode(currentPos, gather.ResourceType, 25.0f);
				if (alternate != Entity.Null)
				{
					var newState = gather;
					newState.TargetEntity = alternate;
					var dest = EcsWorld.Get<Position>(alternate).Value;
					_tickGatherersToUpdate.Add((entity, newState, dest));
				}
				else
				{
					OnClearUnitOrdersRequested?.Invoke(entity);
				}
				return;
			}

			var targetPos = EcsWorld.Get<Position>(targetNode).Value;
			float dist = System.Numerics.Vector3.Distance(currentPos, targetPos);
			float gatherRange = 3.5f;
			if (dist <= gatherRange)
			{
				if (EcsWorld.Has<MoveTo>(entity))
				{
					OnStopGatheringMovementRequested?.Invoke(entity);
				}

				var newState = gather;
				float mineRate = 4.0f * _fDelta;

				var worldEntity = FindWorldEntity();
				if (worldEntity != Entity.Null)
				{
					var enemyPlayerEntity = EcsWorld.Get<NetworkMappingState>(worldEntity).EnemyPlayerEntity;
					bool isEnemy = EcsWorld.Get<Owner>(entity).PlayerEntity == enemyPlayerEntity.AsPlayerEntity(EcsWorld);
					if (!isEnemy && GetHarvestingUpgrade()) mineRate *= 1.5f;
				}

				ref var resNode = ref EcsWorld.Get<ResourceNode>(targetNode);
				float nodeRemaining = resNode.Amount;
				if (mineRate > nodeRemaining)
				{
					mineRate = nodeRemaining;
				}

				resNode.Amount -= mineRate;
				EcsWorld.Set(targetNode, resNode);
				OnResourceHarvested?.Invoke(targetNode);

				newState.CarriedAmount = Math.Min(gather.MaxCapacity, gather.CarriedAmount + mineRate);

				if (resNode.Amount <= 0f)
				{
					OnPropDepleted?.Invoke(targetNode);
				}

				if (newState.CarriedAmount >= gather.MaxCapacity)
				{
					newState.ReturningToBase = true;
					Entity nearestCastle = Entity.Null;
					System.Numerics.Vector3 nearestCastlePos = System.Numerics.Vector3.Zero;
					float nearestDist = float.MaxValue;
					var wOwner = EcsWorld.Get<Owner>(entity).PlayerEntity;

					var castleQuery = Realm.Ecs.Common.QueryCache.AllPositionAndDefinitionIdAndOwnerNoneDeadQuery;
					EcsWorld.Query(in castleQuery, (Entity castleEntity, ref Position castlePos, ref DefinitionId defId, ref Owner ownerComp) =>
					{
						if (defId.Value == "castle" && ownerComp.PlayerEntity == wOwner)
						{
							float d = System.Numerics.Vector3.Distance(currentPos, castlePos.Value);
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
						_tickGatherersToUpdate.Add((entity, newState, nearestCastlePos));
					}
					else
					{
						_tickGatherersToUpdate.Add((entity, newState, null));
					}
				}
				else
				{
					_tickGatherersToUpdate.Add((entity, newState, null));
				}
			}
			else
			{
				if (!EcsWorld.Has<MoveTo>(entity))
				{
					_tickGatherersToUpdate.Add((entity, gather, targetPos));
				}
			}
		}
	}
}
