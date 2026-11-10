using System;
using System.Diagnostics.CodeAnalysis;
using System.Collections.Generic;
using System.Numerics;
using Realm.Ecs;
using Realm.Ecs.Utils;
using Realm.Client.Core;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Resources;
using Realm.Ecs.Components.Tags;
using Realm.Ecs.Services;
using Realm.Shared.Abilities;

namespace Realm.Client.Services;

public class InputService
{
	/// <summary>
	/// The world this service reads input state from. Every member below is an input path that
	/// only runs while a world exists, so reaching one without a world is a programming error;
	/// stating that once keeps fifty reads from each re-proving it.
	/// </summary>
	private readonly ProductionQueueCommands _productionQueue = new();

	private static EcsWorld World => GameContext.World
		?? throw new InvalidOperationException("InputService used before a world existed.");

	public int GetPopCostForIndex(Entity buildingEntity, int index) => _productionQueue.GetPopCostForIndex(buildingEntity, index);


	public void UpdateProductionQueueAfterCancel(Entity buildingEntity, int index, string? nextUnitId, bool isMultiplayerClient) =>
		_productionQueue.UpdateProductionQueueAfterCancel(buildingEntity, index, nextUnitId, isMultiplayerClient);

	private int _buildingCycleIndex;

	private Entity GetWorldEntity()
	{
		Entity worldEntity = Entity.Null;
		var query = QueryCache.AllInputStateQuery;
		World.Query(in query, entity => worldEntity = entity);
		return worldEntity;
	}

	private ref InputState GetInputState(Entity worldEntity)
	{
		return ref World.Get<InputState>(worldEntity);
	}


	public void DeductResourcesForBuilding(UnitMetadata meta, Action<float> deductGold, Action<float> deductWood, Action<float> deductStone)
	{
		deductGold?.Invoke(meta.CostGold);
		deductWood?.Invoke(meta.CostWood);
		deductStone?.Invoke(meta.CostStone);
	}

	public bool AssignFirstWorkerTask(Entity firstWorkerEntity, string type, Vector3 buildingPos, bool shiftHeld, Action<Entity, string, Vector3> assignBuildTask)
	{
		if (shiftHeld && ShouldEnqueueWorkerBuild(firstWorkerEntity))
		{
			EnqueueBuildTask(firstWorkerEntity, type, buildingPos);
			return true;
		}
		else
		{
			assignBuildTask?.Invoke(firstWorkerEntity, type, buildingPos);
			return false;
		}
	}

	public void ProcessWorkerTaskAssignment(Entity unitEntity, Entity firstWorkerEntity, bool isBuilding, string unitId, string type, Vector3 buildingPos, bool shiftHeld)
	{
		if (!IsValidAdditionalWorker(unitEntity, firstWorkerEntity, isBuilding, unitId)) return;

		if (shiftHeld && ShouldEnqueueWorkerBuild(unitEntity))
		{
			EnqueueBuildTask(unitEntity, type, buildingPos);
			return;
		}

		if (!World.Has<BuildTask>(unitEntity))
		{
			World.SetOrAdd(unitEntity, new MoveTo(buildingPos));
		}
	}

	public bool IsValidAdditionalWorker(Entity unitEntity, Entity firstWorkerEntity, bool isBuilding, string unitId)
	{
		if (unitEntity == Entity.Null || unitEntity == firstWorkerEntity) return false;
		if (isBuilding) return false;
		if (unitId != "worker" && unitId != "unit/builder" && unitId != "builder")
		{
			if (!GameHost.TryGetUnitOrBuildingMetadata(unitId, out var meta) || meta.BuildOptions == null || meta.BuildOptions.Length == 0)
			{
				return false;
			}
		}
		return World.IsAlive(unitEntity);
	}

	public bool ShouldEnqueueWorkerBuild(Entity entity)
	{
			return UnitActivityHelper.IsUnitActive(World, entity) || World.Has<BuildQueue>(entity);
	}

	public void EnqueueBuildTask(Entity worker, string type, Vector3 pos)
	{
		if (!World.Has<BuildQueue>(worker)) World.Add(worker, new BuildQueue());
		ref var q = ref World.Get<BuildQueue>(worker);
		q.TryEnqueue(type, pos);
	}

	public void ClearTargetingModes()
	{
		var worldEntity = GetWorldEntity();
		if (worldEntity != Entity.Null)
		{
			ref var state = ref GetInputState(worldEntity);
			state.ActiveSpellTargeting = null;
			state.ActiveCommandTargeting = null;
			state.ActiveBuildingPlacementType = null;
			state.ActivePingMode = false;
		}
	}

	public void AssignResumeTasksToWorkers(IEnumerable<Entity> selectedWorkerEntities, Entity targetBuildingEntity, string targetBuildingUnitId, Vector3 bPos, bool shiftHeld)
	{
		foreach (var workerEntity in selectedWorkerEntities)
		{
				if (shiftHeld && (UnitActivityHelper.IsUnitActive(World, workerEntity) || World.Has<BuildQueue>(workerEntity)))
			{
				if (!World.Has<BuildQueue>(workerEntity))
				{
					World.Add(workerEntity, new BuildQueue());
				}

				ref var q = ref World.Get<BuildQueue>(workerEntity);
				q.TryEnqueue(targetBuildingUnitId, bPos, targetBuildingEntity);
			}
			else
			{
				GameContext.SimulationService.OnClearUnitOrdersRequested?.Invoke(workerEntity);

				if (World.Has<ConstructionState>(targetBuildingEntity))
				{
					var cState = World.Get<ConstructionState>(targetBuildingEntity);
					var buildTask = new BuildTask(targetBuildingEntity, cState.TotalBuildTime)
					{
						Progress = cState.Progress
					};
					World.SetOrAdd(workerEntity, buildTask);
					World.SetOrAdd(workerEntity, new MoveTo(bPos));
				}
			}
		}
	}

	public int CycleSelectionFocus(Entity worldEntity, List<string> unitIds, bool reverse)
	{
		if (unitIds == null || unitIds.Count <= 1 || worldEntity == Entity.Null) return 0;

		ref var state = ref World.Get<InputState>(worldEntity);
		int currentIndex = Math.Clamp(state.CycleSelectionIndex, 0, unitIds.Count - 1);

		var subGroups = new List<string>();
		foreach (var id in unitIds)
		{
			if (!subGroups.Contains(id))
			{
				subGroups.Add(id);
			}
		}

		int newIndex;
		if (subGroups.Count > 1)
		{
			string currentUnitId = unitIds[currentIndex];
			int subGroupIdx = subGroups.IndexOf(currentUnitId);
			if (subGroupIdx < 0) subGroupIdx = 0;

			int nextSubGroupIdx = reverse
				? (subGroupIdx - 1 + subGroups.Count) % subGroups.Count
				: (subGroupIdx + 1) % subGroups.Count;

			string targetUnitId = subGroups[nextSubGroupIdx];
			newIndex = unitIds.IndexOf(targetUnitId);
		}
		else
		{
			newIndex = reverse
				? (currentIndex - 1 + unitIds.Count) % unitIds.Count
				: (currentIndex + 1) % unitIds.Count;
		}

		state.CycleSelectionIndex = newIndex;
		return newIndex;
	}

	public int CycleSelectionFocus(Entity worldEntity, int selectedCount, bool reverse)
	{
		if (selectedCount <= 1 || worldEntity == Entity.Null) return 0;

		ref var state = ref World.Get<InputState>(worldEntity);
		if (reverse)
		{
			state.CycleSelectionIndex = (state.CycleSelectionIndex - 1 + selectedCount) % selectedCount;
		}
		else
		{
			state.CycleSelectionIndex = (state.CycleSelectionIndex + 1) % selectedCount;
		}

		return state.CycleSelectionIndex;
	}

	public Entity CycleThroughBuildings(Entity playerEntity)
	{
		var buildings = new List<Entity>();
		var query = QueryCache.AllBuildingAndOwnerAndPositionNoneDeadQuery;
		World.Query(in query, (Entity entity, ref Owner owner) =>
		{
			if (owner.PlayerEntity.Value == playerEntity)
			{
				buildings.Add(entity);
			}
		});

		if (buildings.Count == 0) return Entity.Null;
		_buildingCycleIndex = (_buildingCycleIndex + 1) % buildings.Count;
		return buildings[_buildingCycleIndex];
	}

	public void MarkEntitiesAsDead(List<Entity> entities)
	{
		foreach (var entity in entities)
		{
			if (World.IsAlive(entity) && !World.Has<Dead>(entity))
			{
				World.Add<Dead>(entity);
			}
		}
	}

	public List<Entity> GetIdleUnitEntities(Entity playerEntity)
	{
		var result = new List<Entity>();
		var query = QueryCache.AllOwnerAndMovableNoneDeadAndBuildingQuery;
		World.Query(in query, (Entity entity, ref Owner owner) =>
		{
			if (owner.PlayerEntity.Value == playerEntity)
			{
				bool hasMoveTo = World.Has<MoveTo>(entity);
				bool hasAttackTarget = World.Has<AttackTarget>(entity);
				bool hasAttackMove = World.Has<AttackMove>(entity);
				bool isGathering = World.Has<Gatherer>(entity) && World.Get<Gatherer>(entity).TargetEntity != Entity.Null;

				if (!hasMoveTo && !hasAttackTarget && !hasAttackMove && !isGathering)
				{
					result.Add(entity);
				}
			}
		});
		return result;
	}

	public List<Entity> GetMilitaryUnitEntities(Entity playerEntity)
	{
		var result = new List<Entity>();
		var query = QueryCache.AllOwnerAndMovableNoneDeadAndBuildingQuery;
		World.Query(in query, (Entity entity, ref Owner owner) =>
		{
			if (owner.PlayerEntity.Value == playerEntity)
			{
				result.Add(entity);
			}
		});
		return result;
	}

	public List<Entity> GetBuildingEntities(Entity playerEntity)
	{
		var result = new List<Entity>();
		var query = QueryCache.AllOwnerAndBuildingNoneDeadQuery;
		World.Query(in query, (Entity entity, ref Owner owner) =>
		{
			if (owner.PlayerEntity.Value == playerEntity)
			{
				result.Add(entity);
			}
		});
		return result;
	}

	public bool TryPlaceBuilding(Vector3 position, float clearance) => PlacementQueries.TryPlaceBuilding(position, clearance);

	public bool IsAreaObstructed(Vector3 position, float clearance) => PlacementQueries.IsAreaObstructed(position, clearance);

	public bool IsPositionBlocked(Vector3 pos, float checkRadius, Entity ignoreEntity) => PlacementQueries.IsPositionBlocked(pos, checkRadius, ignoreEntity);

	public float GetPlacementRadius(string placeId, float scale = 1.0f) => PlacementQueries.GetPlacementRadius(placeId, scale);

	public Vector3? FindNearestFreePosition(Vector3 startPos, float checkRadius, float maxSearchDist = 20.0f, float terrainQuadSize = 2.0f, int terrainWidth = 256, int terrainDepth = 256) => PlacementQueries.FindNearestFreePosition(startPos, checkRadius, maxSearchDist, terrainQuadSize, terrainWidth, terrainDepth);

	public bool TryExecuteSpellCast(Entity playerEntity, string spellId, out float cooldownMax)
	{
		return TryExecuteSpellCast(playerEntity, Entity.Null, spellId, out cooldownMax);
	}

	private bool ConsumeManaIfSufficient(Entity entity, string spellId)
	{
		if (!World.Has<Mana>(entity)) return true;

		ref var mana = ref World.Get<Mana>(entity);
		float cost = 0f;
		if (GameContext.Host != null)
		{
			var def = GameContext.Host.GetAbilityDefinition(spellId);
			if (def != null) cost = def.ManaCost;
		}

		if (mana.Current < cost) return false;

		mana.Current = MathF.Max(0f, mana.Current - cost);
		return true;
	}

	private bool TryApplyCooldown(Entity entity, string spellId, float cooldownMax)
	{
		if (World.Has<Cooldowns>(entity))
		{
			ref var cooldowns = ref World.Get<Cooldowns>(entity);
			if (cooldowns.TryGetValue(spellId, out float currentCooldown) && currentCooldown > 0f) return false;
			cooldowns.Set(spellId, cooldownMax, cooldownMax);
		}

		if (World.Has<SpellCooldowns>(entity))
		{
			ref var spellCooldowns = ref World.Get<SpellCooldowns>(entity);
			if (spellCooldowns.TryGetValue(spellId, out float currentCooldown) && currentCooldown > 0f) return false;
			spellCooldowns.Set(spellId, cooldownMax, cooldownMax);
		}

		return true;
	}

	public bool TryExecuteSpellCast(Entity playerEntity, Entity casterEntity, string spellId, out float cooldownMax)
	{
		cooldownMax = 10.0f;
		if (GameContext.Host != null)
		{
			var def = GameContext.Host.GetAbilityDefinition(spellId);
			if (def != null && def.Cooldown > 0f)
			{
				cooldownMax = def.Cooldown;
			}
		}

		if (casterEntity != Entity.Null && World.IsAlive(casterEntity))
		{
			if (!ConsumeManaIfSufficient(casterEntity, spellId)) return false;

			if (!TryApplyCooldown(casterEntity, spellId, cooldownMax)) return false;
		}

		if (World.IsAlive(playerEntity))
		{
			if (!TryApplyCooldown(playerEntity, spellId, cooldownMax)) return false;
		}

		return true;
	}

	public bool BuyItem(string itemId, Entity playerEntity, Vector3 shopPosition, Entity selectedUnitEntity, out Entity targetUnitEntity)
	{
		targetUnitEntity = Entity.Null;

		Entity target = FindClosestFriendlyCombatUnit(shopPosition, playerEntity, 20.0f);

		if (target == Entity.Null && World.IsAlive(selectedUnitEntity))
		{
			bool isBuilding = World.Has<Building>(selectedUnitEntity);
			bool isOwnedByPlayer = World.Has<Owner>(selectedUnitEntity) && World.Get<Owner>(selectedUnitEntity).PlayerEntity.Value == playerEntity;
			if (!isBuilding && isOwnedByPlayer && World.Has<Inventory>(selectedUnitEntity))
			{
				target = selectedUnitEntity;
			}
		}

		if (target != Entity.Null)
		{
			targetUnitEntity = target;
			Realm.Client.Utils.ItemGrantingUtility.EquipItem(World, target, itemId);
			return true;
		}

		return false;
	}

	private Entity FindClosestFriendlyCombatUnit(Vector3 originPos, Entity playerEntity, float maxDistance)
	{
		Entity closestUnit = Entity.Null;
		float closestDist = maxDistance;

		var query = QueryCache.AllPositionAndOwnerAndInventoryNoneDeadAndBuildingQuery;
		World.Query(in query, (Entity entity, ref Position pos, ref Owner owner) =>
		{
			if (owner.PlayerEntity.Value == playerEntity)
			{
				float dist = Vector3.Distance(originPos, pos.Value);
				if (dist < closestDist)
				{
					closestDist = dist;
					closestUnit = entity;
				}
			}
		});

		return closestUnit;
	}

	public bool UseItem(Entity unitEntity, string itemId, out float healedAmount)
	{
		healedAmount = 0f;
		if (!World.IsAlive(unitEntity) || !World.Has<Inventory>(unitEntity) || !World.Has<Health>(unitEntity))
			return false;

		ref var inv = ref World.Get<Inventory>(unitEntity);
		int currentCount = inv.GetItemCount(itemId);
		if (currentCount <= 0) return false;

		ref var hp = ref World.Get<Health>(unitEntity);
		if (hp.Current >= hp.Max) return false;

		int newCount = currentCount - 1;
		inv.SetItemCount(itemId, newCount);
		float oldCurrent = hp.Current;
		hp.Current = Math.Min(hp.Max, hp.Current + 50f);
		healedAmount = hp.Current - oldCurrent;

		if (newCount <= 0)
		{
			Realm.Client.Utils.ItemGrantingUtility.UnequipItem(World, unitEntity, itemId);
		}

		return true;
	}

	public bool IsUnitAlive(Entity unitEntity)
	{
		return World.IsAlive(unitEntity);
	}

	public bool TryGetItemMeta(string itemId, [NotNullWhen(true)] out ItemMetadata? itemMeta)
	{
        var itemRegistry = GameContext.RegistryService?.ItemRegistry;
        if (itemRegistry != null)
        {
            return itemRegistry.TryGetValue(itemId, out itemMeta);
        }
        itemMeta = null;
        return false;
	}

	public AbilityDefinition? GetAbilityDefinition(string abilityId)
	{
		return GameContext.RegistryService.GetAbilityDefinition(abilityId);
	}

	public bool IsUnitAtFullHealth(Entity unitEntity)
	{
		if (World.IsAlive(unitEntity) && World.Has<Health>(unitEntity))
		{
			var hp = World.Get<Health>(unitEntity);
			return hp.Current >= hp.Max;
		}
		return false;
	}

	public Entity CreateInstantBuildingEcsUnit(string type, UnitMetadata meta, System.Numerics.Vector3 position, Entity playerEntity)
	{
		return GameContext.UnitSpawnService.CreateEcsUnitEntity(
			type, meta.Name, meta.MaxHp, meta.Damage, meta.Range, meta.Armor, 0f, 
			meta.SightRange, false, 1.5f, meta.PopCost, 
			new Godot.Vector3(position.X, position.Y, position.Z), playerEntity.AsPlayerEntity(World), playerEntity, 
			false, false, 
			meta.Abilities, meta.CostGold, meta.CostWood, meta.CostStone, 
			meta.ProductionTime, meta.MagicResistance, 0f, 0f, 
			0f, meta.DamageType, 0f, 0f, 
			meta.MovementType, 0f, 1f, 0f, 
			meta.HpRegen, 0f, 0f, false, 
			(int)meta.GoldBounty, "Ground");
	}



	public bool TryAssignFirstWorkerTask(Entity firstWorkerEntity, string type, System.Numerics.Vector3 buildingPos, bool shiftHeld)
	{
		if (World.IsAlive(firstWorkerEntity))
		{
			AssignFirstWorkerTask(firstWorkerEntity, type, buildingPos, shiftHeld, (entity, t, pos) => {
				if (!World.Has<BuildTask>(entity))
				{
					World.SetOrAdd(entity, new MoveTo(pos));
				}
			});
			return true;
		}
		return false;
	}

	public CameraLocationSlot GetCameraSlotFromState(ref CameraState state, int slotIndex)
	{
		return slotIndex switch
		{
			1 => state.LocationSlot1,
			2 => state.LocationSlot2,
			3 => state.LocationSlot3,
			4 => state.LocationSlot4,
			_ => default
		};
	}

	public bool TryGetCameraState(out CameraState state)
	{
		if (GameContext.World != null && World.IsAlive(GameContext.Simulation.WorldEntity) && World.Has<CameraState>(GameContext.Simulation.WorldEntity))
		{
			state = World.Get<CameraState>(GameContext.Simulation.WorldEntity);
			return true;
		}

		state = default;
		return false;
	}

	public void TrySaveCameraSlot(int slotIndex, Vector3 pos, float zoom)
	{
		if (GameContext.World == null || !World.IsAlive(GameContext.Simulation.WorldEntity) || !World.Has<CameraState>(GameContext.Simulation.WorldEntity)) return;

		ref var state = ref World.Get<CameraState>(GameContext.Simulation.WorldEntity);
		var slot = new CameraLocationSlot
		{
			Position = pos,
			ZoomLevel = zoom,
			IsSet = true
		};

		switch (slotIndex)
		{
			case 1: state.LocationSlot1 = slot; break;
			case 2: state.LocationSlot2 = slot; break;
			case 3: state.LocationSlot3 = slot; break;
			case 4: state.LocationSlot4 = slot; break;
		}
	}

	public void DeductResources(UnitMetadata meta, ref float currentGold, ref float currentWood, ref float currentStone)
	{
		currentGold -= meta.CostGold;
		currentWood -= meta.CostWood;
		currentStone -= meta.CostStone;
	}

	public bool HasSufficientResources(UnitMetadata meta, float currentGold, float currentWood, float currentStone)
	{
		return currentGold >= meta.CostGold &&
		       currentWood >= meta.CostWood &&
		       currentStone >= meta.CostStone;
	}

	public enum ValidateTrainingConstraintsResult
	{
		Success,
		PopulationCapReached
	}

	public ValidateTrainingConstraintsResult ValidateTrainingConstraints(UnitMetadata meta, int currentPopulation, int maxPopulation)
	{
		if (meta.PopCost > 0 && currentPopulation + meta.PopCost > maxPopulation)
		{
			return ValidateTrainingConstraintsResult.PopulationCapReached;
		}

		return ValidateTrainingConstraintsResult.Success;
	}

	public enum TryGetCandidateBuildingResult
	{
		Success,
		NoProducingBuildingSelected,
		TrainingQueueFull
	}

	public TryGetCandidateBuildingResult TryGetCandidateBuilding(List<Entity> candidateBuildingEntities, out Entity targetBuildingEntity)
	{
		targetBuildingEntity = Entity.Null;
		if (candidateBuildingEntities == null || candidateBuildingEntities.Count == 0)
		{
			return TryGetCandidateBuildingResult.NoProducingBuildingSelected;
		}

		targetBuildingEntity = GetNextProductionStructure(candidateBuildingEntities);
		if (targetBuildingEntity == Entity.Null)
		{
			return TryGetCandidateBuildingResult.TrainingQueueFull;
		}

		return TryGetCandidateBuildingResult.Success;
	}

	public Entity GetNextProductionStructure(List<Entity> candidateBuildings) => _productionQueue.GetNextProductionStructure(candidateBuildings);

	public bool TryQueueUnit(Entity playerEntity, Entity buildingEntity, string unitId, int popCost, float productionTime) =>
		_productionQueue.TryQueueUnit(playerEntity, buildingEntity, unitId, popCost, productionTime);

	private ref ProductionQueue CreateProductionQueue(Entity buildingEntity) => ref _productionQueue.CreateProductionQueue(buildingEntity);

	public bool CancelQueuedUnitAt(Entity buildingEntity, int index, out string? cancelledUnitId, out string? nextUnitId) =>
		_productionQueue.CancelQueuedUnitAt(buildingEntity, index, out cancelledUnitId, out nextUnitId);


	public void ApplySpawnRallyPointOrMove(Entity entity, Entity buildingEntity, Vector3 defaultPosition)
	{
		if (!TryApplyRallyPoint(entity, buildingEntity))
		{
			World.Add(entity, new MoveTo(defaultPosition));
		}
	}

	public bool TryApplyRallyPoint(Entity entity, Entity buildingEntity)
	{
		if (!World.IsAlive(buildingEntity) || !World.Has<RallyPoint>(buildingEntity))
		{
			return false;
		}

		var rp = World.Get<RallyPoint>(buildingEntity);
		if (rp.Count <= 0)
		{
			return false;
		}

		World.Add(entity, new MoveTo(rp.Waypoints[0]));
		if (rp.Count > 1)
		{
			var wq = new WaypointQueue(rp.Waypoints[1]);
			for (int i = 2; i < rp.Count; i++)
			{
				wq.Add(rp.Waypoints[i]);
			}

			World.Add(entity, wq);
		}

		return true;
	}

	public void SetRallyPoint(Entity buildingEntity, Vector3 position, bool queue)
	{
		if (!World.IsAlive(buildingEntity)) return;

		if (queue)
		{
			if (World.Has<RallyPoint>(buildingEntity))
			{
				var rp = World.Get<RallyPoint>(buildingEntity);
				rp.Add(position);
				World.Set(buildingEntity, rp);
			}
			else
			{
				var rp = new RallyPoint(position);
				World.Add(buildingEntity, rp);
			}
		}
		else
		{
			var rp = new RallyPoint(position);
			World.SetOrAdd(buildingEntity, rp);
		}
	}

	public void SetEntityPosition(Entity entity, Vector3 position)
	{
		if (World.IsAlive(entity))
		{
			PositionWriteTrace.Mark(entity, PositionWriter.Input);
			World.Set(entity, new Position(position));
		}
	}

	public string? ActiveSpellTargeting
	{
		get
		{
			var worldEntity = GetWorldEntity();
			return worldEntity != Entity.Null && World.Has<InputState>(worldEntity)
				? World.Get<InputState>(worldEntity).ActiveSpellTargeting
				: null;
		}
		set
		{
			var worldEntity = GetWorldEntity();
			if (worldEntity == Entity.Null || !World.Has<InputState>(worldEntity)) return;
			ref var state = ref World.Get<InputState>(worldEntity);
			state.ActiveSpellTargeting = value;
		}
	}

	public string? ActiveCommandTargeting
	{
		get
		{
			var worldEntity = GetWorldEntity();
			return worldEntity != Entity.Null && World.Has<InputState>(worldEntity)
				? World.Get<InputState>(worldEntity).ActiveCommandTargeting
				: null;
		}
		set
		{
			var worldEntity = GetWorldEntity();
			if (worldEntity == Entity.Null || !World.Has<InputState>(worldEntity)) return;
			ref var state = ref World.Get<InputState>(worldEntity);
			state.ActiveCommandTargeting = value;
		}
	}

	public string? ActiveBuildingPlacementType
	{
		get
		{
			var worldEntity = GetWorldEntity();
			return worldEntity != Entity.Null && World.Has<InputState>(worldEntity)
				? World.Get<InputState>(worldEntity).ActiveBuildingPlacementType
				: null;
		}
		set
		{
			var worldEntity = GetWorldEntity();
			if (worldEntity == Entity.Null || !World.Has<InputState>(worldEntity)) return;
			ref var state = ref World.Get<InputState>(worldEntity);
			state.ActiveBuildingPlacementType = value;
		}
	}

	public bool ActivePingMode
	{
		get
		{
			var worldEntity = GetWorldEntity();
			return worldEntity != Entity.Null && World.Has<InputState>(worldEntity)
			                                  && World.Get<InputState>(worldEntity).ActivePingMode;
		}
		set
		{
			var worldEntity = GetWorldEntity();
			if (worldEntity == Entity.Null || !World.Has<InputState>(worldEntity)) return;
			ref var state = ref World.Get<InputState>(worldEntity);
			state.ActivePingMode = value;
		}
	}

	public int GetCycleSelectionIndex(int selectedCount)
	{
		if (selectedCount == 0) return 0;
		var worldEntity = GetWorldEntity();
		if (worldEntity == Entity.Null || !World.Has<InputState>(worldEntity)) return 0;
		int val = World.Get<InputState>(worldEntity).CycleSelectionIndex;
		return Math.Clamp(val, 0, selectedCount - 1);
	}

	public void SetCycleSelectionIndex(int value, int selectedCount)
	{
		var worldEntity = GetWorldEntity();
		if (worldEntity == Entity.Null || !World.Has<InputState>(worldEntity)) return;
		ref var state = ref World.Get<InputState>(worldEntity);
		state.CycleSelectionIndex = selectedCount > 0 ? Math.Clamp(value, 0, selectedCount - 1) : 0;
	}

	public void SetSpellCooldown(Entity playerEntity, string spellId, float cooldown)
	{
		if (playerEntity == Entity.Null || !World.IsAlive(playerEntity)) return;
		if (World.Has<SpellCooldowns>(playerEntity))
		{
			ref var spellCooldowns = ref World.Get<SpellCooldowns>(playerEntity);
			spellCooldowns.Set(spellId, cooldown, cooldown);
		}
	}

	public float GetSpellCooldown(Entity playerEntity, string spellId)
	{
		if (playerEntity == Entity.Null || !World.IsAlive(playerEntity)) return 0f;
		if (World.Has<SpellCooldowns>(playerEntity))
		{
			var spellCooldowns = World.Get<SpellCooldowns>(playerEntity);
			if (spellCooldowns.TryGetValue(spellId, out float remaining)) return remaining;
		}

		return 0f;
	}

	public void ApplySpellEffect(AbilityDefinition def, Vector3 simPos, Entity target)
	{
		AbilityEffectService.Execute(target, simPos, def, simPos);
	}

	public bool TryGetCasterPosition(Entity casterEntity, out Vector3 position)
	{
		position = default;
		if (!World.IsAlive(casterEntity) || !World.Has<Position>(casterEntity)) return false;
		position = World.Get<Position>(casterEntity).Value;
		return true;
	}
}