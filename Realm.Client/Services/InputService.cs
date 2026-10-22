using System;
using System.Collections.Generic;
using System.Numerics;
using Realm.Ecs;
using Realm.Client.Core;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Resources;
using Realm.Ecs.Components.Tags;
using Realm.Ecs.Services;

namespace Realm.Client.Services;

public class InputService
{
	public int GetPopCostForIndex(Entity buildingEntity, int index)
	{
		var prod = GameContext.World.Get<ProductionQueue>(buildingEntity);
		if (index >= 0 && index < prod.UnitIds.Count)
		{
			string peekCancelledId = prod.UnitIds[index];
			if (GameHost.UnitRegistry.TryGetValue(peekCancelledId, out var metaPeek))
			{
				return metaPeek.PopCost;
			}
		}

		return 0;
	}


	public void UpdateProductionQueueAfterCancel(Entity buildingEntity, int index, string? nextUnitId, bool isMultiplayerClient)
	{
		if (index == 0 && nextUnitId != null && !isMultiplayerClient)
		{
			var nextMeta = GameHost.UnitRegistry[nextUnitId];
			if (GameContext.World.Has<ProductionQueue>(buildingEntity))
			{
				var p = GameContext.World.Get<ProductionQueue>(buildingEntity);
				p.BuildTime = nextMeta.ProductionTime;
				GameContext.World.Set(buildingEntity, p);
			}
		}
	}

	private int _buildingCycleIndex;
	private int _productionRoundRobinIndex;

	public InputService()
	{
	}

	private Entity GetWorldEntity()
	{
		Entity worldEntity = Entity.Null;
		var query = QueryCache.AllInputStateQuery;
		GameContext.World.Query(in query, entity => worldEntity = entity);
		return worldEntity;
	}

	private ref InputState GetInputState(Entity worldEntity)
	{
		return ref GameContext.World.Get<InputState>(worldEntity);
	}


	public void ExecuteClientBuildingPlacement(UnitMetadata bldMeta, string type, Vector3 position)
	{
		// Domain logic can be extended here if needed
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

		if (!GameContext.World.Has<BuildTask>(unitEntity))
		{
			GameContext.World.SetOrAdd(unitEntity, new MoveTo(buildingPos));
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
		return GameContext.World.IsAlive(unitEntity);
	}

	public bool ShouldEnqueueWorkerBuild(Entity entity)
	{
			return Realm.Client.Utils.UnitActivityHelper.IsUnitActive(entity) || GameContext.World.Has<BuildQueue>(entity);
	}

	public void EnqueueBuildTask(Entity worker, string type, Vector3 pos)
	{
		if (!GameContext.World.Has<BuildQueue>(worker)) GameContext.World.Add(worker, new BuildQueue());
		ref var q = ref GameContext.World.Get<BuildQueue>(worker);
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

	public bool BuyWeaponsUpgrade(Entity playerEntity)
	{
		return GameContext.TechTreeService.BuyWeaponsUpgrade(playerEntity);
	}

	public bool BuyShieldsUpgrade(Entity playerEntity)
	{
		return GameContext.TechTreeService.BuyShieldsUpgrade(playerEntity);
	}

	public bool BuyHarvestingUpgrade(Entity playerEntity)
	{
		return GameContext.TechTreeService.BuyHarvestingUpgrade(playerEntity);
	}

	public void AssignResumeTasksToWorkers(IEnumerable<Entity> selectedWorkerEntities, Entity targetBuildingEntity, string targetBuildingUnitId, Vector3 bPos, bool shiftHeld)
	{
		foreach (var workerEntity in selectedWorkerEntities)
		{
				if (shiftHeld && (Realm.Client.Utils.UnitActivityHelper.IsUnitActive(workerEntity) || GameContext.World.Has<BuildQueue>(workerEntity)))
			{
				if (!GameContext.World.Has<BuildQueue>(workerEntity))
				{
					GameContext.World.Add(workerEntity, new BuildQueue());
				}

				ref var q = ref GameContext.World.Get<BuildQueue>(workerEntity);
				q.TryEnqueue(targetBuildingUnitId, bPos, targetBuildingEntity);
			}
			else
			{
				GameContext.SimulationService.OnClearUnitOrdersRequested?.Invoke(workerEntity);

				if (GameContext.World.Has<ConstructionState>(targetBuildingEntity))
				{
					var cState = GameContext.World.Get<ConstructionState>(targetBuildingEntity);
					var buildTask = new BuildTask(targetBuildingEntity, cState.TotalBuildTime)
					{
						Progress = cState.Progress
					};
					GameContext.World.SetOrAdd(workerEntity, buildTask);
					GameContext.World.SetOrAdd(workerEntity, new MoveTo(bPos));
				}
			}
		}
	}

	public void EnqueueCommand(Entity entity, string type, Vector3 position, Entity target = default)
	{
		if (!GameContext.World.Has<BuildQueue>(entity))
		{
			GameContext.World.Add(entity, new BuildQueue());
		}

		ref var q = ref GameContext.World.Get<BuildQueue>(entity);
		q.TryEnqueue(type, position, target);
	}

	private bool IsControllableUnit(Entity entity)
	{
		if (!GameContext.World.IsAlive(entity)) return false;
		if (GameContext.World.Has<Building>(entity)) return false;
		return true;
	}

	private void CalculateGroupMovement(List<Entity> selectedEntities, Vector3 targetPos, out Vector3 moveDir, out Vector3 right)
	{
		Vector3 groupCenter = Vector3.Zero;
		int movableCount = 0;
		foreach (var entity in selectedEntities)
		{
			if (!IsControllableUnit(entity)) continue;
			if (GameContext.World.Has<Movable>(entity) && GameContext.World.Has<Position>(entity))
			{
				groupCenter += GameContext.World.Get<Position>(entity).Value;
				movableCount++;
			}
		}

		if (movableCount > 0)
		{
			groupCenter /= movableCount;
		}

		moveDir = targetPos - groupCenter;
		moveDir.Y = 0f;
		if (moveDir.LengthSquared() > 0.01f)
		{
			moveDir = Vector3.Normalize(moveDir);
		}
		else
		{
			moveDir = new Vector3(0f, 0f, -1f);
		}

		right = new Vector3(-moveDir.Z, 0f, moveDir.X);
	}

	private void QueueOrExecuteMove(Entity entity, Vector3 scattered)
	{
		bool hasNonMoveTasks = GameContext.World.Has<BuildTask>(entity) ||
		                       GameContext.World.Has<AttackTarget>(entity) ||
		                       GameContext.World.Has<AttackMove>(entity) ||
		                       GameContext.World.Has<Follow>(entity) ||
		                       GameContext.World.Has<Patrol>(entity) ||
		                       GameContext.World.Has<Gatherer>(entity);

		if (hasNonMoveTasks)
		{
			EnqueueCommand(entity, "move", scattered);
			return;
		}

		bool alreadyMoving = GameContext.World.Has<MoveTo>(entity);
		if (alreadyMoving)
		{
			if (GameContext.World.Has<WaypointQueue>(entity))
			{
				var q = GameContext.World.Get<WaypointQueue>(entity);
				q.Add(scattered);
				GameContext.World.Set(entity, q);
			}
			else
			{
				var q = new WaypointQueue(scattered);
				GameContext.World.Add(entity, q);
			}
		}
		else
		{
			GameContext.SimulationService.OnClearUnitOrdersRequested?.Invoke(entity);
			var moveTo = new MoveTo(scattered);
			GameContext.World.Add(entity, moveTo);
		}
	}

	public void IssueMoveCommand(List<Entity> selectedEntities, Vector3 targetPos)
	{
		int unitIndex = 0;
		int cols = (int)Math.Ceiling(Math.Sqrt(selectedEntities.Count));
		float spacing = 2.2f;

		CalculateGroupMovement(selectedEntities, targetPos, out Vector3 moveDir, out Vector3 right);

		foreach (var entity in selectedEntities)
		{
			if (!IsControllableUnit(entity)) continue;

			GameContext.SimulationService.OnClearUnitOrdersRequested?.Invoke(entity);

			if (GameContext.World.Has<Movable>(entity))
			{
				int row = unitIndex / cols;
				int col = unitIndex % cols;
				float offsetX = (col - cols * 0.5f + 0.5f) * spacing;
				float offsetZ = -row * spacing;
				var scattered = targetPos + right * offsetX + moveDir * offsetZ;

				var moveTo = new MoveTo(scattered);
				GameContext.World.SetOrAdd(entity, moveTo);

				unitIndex++;
			}
		}
	}

	public void IssueMoveCommandQueued(List<Entity> selectedEntities, Vector3 targetPos)
	{
		int unitIndex = 0;
		int cols = (int)Math.Ceiling(Math.Sqrt(selectedEntities.Count));
		float spacing = 2.2f;

		CalculateGroupMovement(selectedEntities, targetPos, out Vector3 moveDir, out Vector3 right);

		foreach (var entity in selectedEntities)
		{
			if (!IsControllableUnit(entity)) continue;
			if (!GameContext.World.Has<Movable>(entity)) continue;

			int row = unitIndex / cols;
			int col = unitIndex % cols;
			float offsetX = (col - cols * 0.5f + 0.5f) * spacing;
			float offsetZ = -row * spacing;
			var scattered = targetPos + right * offsetX + moveDir * offsetZ;

			QueueOrExecuteMove(entity, scattered);

			unitIndex++;
		}
	}


	public void IssueAttackCommand(List<Entity> selectedEntities, Entity targetEntity, bool isQueued = false)
	{
		var targetPos = GameContext.World.Has<Position>(targetEntity) ? GameContext.World.Get<Position>(targetEntity).Value : Vector3.Zero;
		foreach (var entity in selectedEntities)
		{
			if (!GameContext.World.IsAlive(entity)) continue;
			bool isBuilding = GameContext.World.Has<Building>(entity);
			if (isBuilding) continue;

				if (isQueued && Realm.Client.Utils.UnitActivityHelper.IsUnitActive(entity))
			{
				EnqueueCommand(entity, "attack", targetPos, targetEntity);
			}
			else
			{
				if (!isQueued)
				{
					GameContext.SimulationService.OnClearUnitOrdersRequested?.Invoke(entity);
				}

				var attackTarget = new AttackTarget(targetEntity);
				GameContext.World.SetOrAdd(entity, attackTarget);
			}
		}
	}

	private void AssignFollowCommand(Entity entity, Entity targetEntity, Vector3 targetPos, bool isQueued)
	{
			if (isQueued && Realm.Client.Utils.UnitActivityHelper.IsUnitActive(entity))
		{
			EnqueueCommand(entity, "follow", targetPos, targetEntity);
			return;
		}

		if (!isQueued)
		{
			GameContext.SimulationService.OnClearUnitOrdersRequested?.Invoke(entity);
		}

		if (GameContext.World.Has<Movable>(entity))
		{
			var follow = new Follow(targetEntity);
			GameContext.World.SetOrAdd(entity, follow);
		}
	}


	public void IssueFollowCommand(List<Entity> selectedEntities, Entity targetEntity, bool isQueued = false)
	{
		var targetPos = GameContext.World.Has<Position>(targetEntity) ? GameContext.World.Get<Position>(targetEntity).Value : Vector3.Zero;
		foreach (var entity in selectedEntities)
		{
			if (entity == targetEntity || !IsControllableUnit(entity)) continue;

			AssignFollowCommand(entity, targetEntity, targetPos, isQueued);
		}
	}

	private void AssignPatrolCommand(Entity entity, Vector3 scattered, bool isQueued)
	{
			if (isQueued && Realm.Client.Utils.UnitActivityHelper.IsUnitActive(entity))
		{
			EnqueueCommand(entity, "patrol", scattered);
			return;
		}

		if (!isQueued)
		{
			GameContext.SimulationService.OnClearUnitOrdersRequested?.Invoke(entity);
		}

		if (GameContext.World.Has<Movable>(entity))
		{
			var unitPos = GameContext.World.Has<Position>(entity) ? GameContext.World.Get<Position>(entity).Value : Vector3.Zero;
			var patrolA = new Vector3(unitPos.X, unitPos.Y, unitPos.Z);

			var patrol = new Patrol(patrolA, scattered);
			GameContext.World.SetOrAdd(entity, patrol);

			var moveTo = new MoveTo(scattered);
			GameContext.World.SetOrAdd(entity, moveTo);
		}
	}

	public void IssuePatrolCommand(List<Entity> selectedEntities, Vector3 targetPos, bool isQueued = false)
	{
		int unitIndex = 0;
		int cols = (int)Math.Ceiling(Math.Sqrt(selectedEntities.Count));
		float spacing = 2.2f;

		foreach (var entity in selectedEntities)
		{
			if (!IsControllableUnit(entity)) continue;

			int row = unitIndex / cols;
			int col = unitIndex % cols;
			float offsetX = (col - cols * 0.5f + 0.5f) * spacing;
			float offsetZ = row * spacing;
			var scattered = new Vector3(targetPos.X + offsetX, targetPos.Y, targetPos.Z + offsetZ);

			AssignPatrolCommand(entity, scattered, isQueued);

			unitIndex++;
		}
	}

	public void IssueAttackMoveCommand(List<Entity> selectedEntities, Vector3 targetPos, bool isQueued = false)
	{
		foreach (var entity in selectedEntities)
		{
			if (!GameContext.World.IsAlive(entity)) continue;
			bool isBuilding = GameContext.World.Has<Building>(entity);
			if (isBuilding) continue;

				if (isQueued && Realm.Client.Utils.UnitActivityHelper.IsUnitActive(entity))
			{
				EnqueueCommand(entity, "attackmove", targetPos);
			}
			else
			{
				if (!isQueued)
				{
					GameContext.SimulationService.OnClearUnitOrdersRequested?.Invoke(entity);
				}

				var attackMove = new AttackMove(targetPos);
				GameContext.World.SetOrAdd(entity, attackMove);

				var moveTo = new MoveTo(targetPos);
				GameContext.World.SetOrAdd(entity, moveTo);
			}
		}
	}

	public void HoldSelectedUnits(List<Entity> selectedEntities)
	{
		foreach (var entity in selectedEntities)
		{
			if (!GameContext.World.IsAlive(entity)) continue;
			bool isBuilding = GameContext.World.Has<Building>(entity);
			if (isBuilding) continue;

			GameContext.SimulationService.OnClearUnitOrdersRequested?.Invoke(entity);

			if (!GameContext.World.Has<HoldPosition>(entity))
				GameContext.World.Add<HoldPosition>(entity);
		}
	}

	public void StopSelectedUnits(List<Entity> selectedEntities)
	{
		foreach (var entity in selectedEntities)
		{
			if (!GameContext.World.IsAlive(entity)) continue;

			GameContext.SimulationService.OnClearUnitOrdersRequested?.Invoke(entity);
		}
	}

	public int CycleSelectionFocus(Entity worldEntity, List<string> unitIds, bool reverse)
	{
		if (unitIds == null || unitIds.Count <= 1 || worldEntity == Entity.Null) return 0;

		ref var state = ref GameContext.World.Get<InputState>(worldEntity);
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

		ref var state = ref GameContext.World.Get<InputState>(worldEntity);
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
		GameContext.World.Query(in query, (Entity entity, ref Owner owner) =>
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
			if (GameContext.World.IsAlive(entity) && !GameContext.World.Has<Dead>(entity))
			{
				GameContext.World.Add<Dead>(entity);
			}
		}
	}

	public List<Entity> GetIdleUnitEntities(Entity playerEntity)
	{
		var result = new List<Entity>();
		var query = QueryCache.AllOwnerAndMovableNoneDeadAndBuildingQuery;
		GameContext.World.Query(in query, (Entity entity, ref Owner owner) =>
		{
			if (owner.PlayerEntity.Value == playerEntity)
			{
				bool hasMoveTo = GameContext.World.Has<MoveTo>(entity);
				bool hasAttackTarget = GameContext.World.Has<AttackTarget>(entity);
				bool hasAttackMove = GameContext.World.Has<AttackMove>(entity);
				bool isGathering = GameContext.World.Has<Gatherer>(entity) && GameContext.World.Get<Gatherer>(entity).TargetEntity != Entity.Null;

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
		GameContext.World.Query(in query, (Entity entity, ref Owner owner) =>
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
		GameContext.World.Query(in query, (Entity entity, ref Owner owner) =>
		{
			if (owner.PlayerEntity.Value == playerEntity)
			{
				result.Add(entity);
			}
		});
		return result;
	}

	public bool TryPlaceBuilding(Vector3 position, float clearance)
	{
		return !IsAreaObstructed(position, clearance);
	}

	public bool IsAreaObstructed(Vector3 position, float clearance)
	{
		if (GameContext.World != null && GameContext.World.IsAlive(ECSContext.WorldEntity) && GameContext.World.Has<Realm.Ecs.Components.Terrain.TerrainState>(ECSContext.WorldEntity))
		{
			ref var terrain = ref GameContext.World.Get<Realm.Ecs.Components.Terrain.TerrainState>(ECSContext.WorldEntity);
			if (terrain.PathingCodes != null && terrain.Width > 0 && terrain.Depth > 0 && terrain.QuadSize > 0f)
			{
				int cx = Math.Clamp((int)MathF.Floor(position.X / terrain.QuadSize + terrain.Width / 2.0f), 0, terrain.Width - 1);
				int cz = Math.Clamp((int)MathF.Floor(position.Z / terrain.QuadSize + terrain.Depth / 2.0f), 0, terrain.Depth - 1);
				if ((terrain.PathingCodes[cx, cz] & (int)Realm.Ecs.Components.Terrain.TerrainPathingFlags.Buildable) == 0)
				{
					return true;
				}
			}
		}

		bool obstructed = false;
		var query = QueryCache.AllPositionNoneDeadQuery;
		GameContext.World.Query(in query, (Entity entity, ref Position pos) =>
		{
			if (obstructed) return;

			float entityRadius = GameContext.World.Has<CollisionRadius>(entity)
				? GameContext.World.Get<CollisionRadius>(entity).Value
				: 1.0f;

			if (GameContext.World.Has<DefinitionId>(entity))
			{
				string defId = GameContext.World.Get<DefinitionId>(entity).Value;
				if (GameHost.TryGetUnitOrBuildingMetadata(defId, out var meta))
				{
					if (meta.PathingType == 16 || string.Equals(meta.VisualMode, "GroundPlane", StringComparison.OrdinalIgnoreCase))
					{
						return;
					}

					if (meta.CollisionCircle > 0f)
					{
						entityRadius = MathF.Max(entityRadius, meta.CollisionCircle * (meta.Scale > 0f ? meta.Scale : 1.0f) * 0.5f);
					}
				}
			}
			else if (!GameContext.World.Has<PropIdentity>(entity))
			{
				return;
			}

			float minDist = MathF.Max(clearance, entityRadius + 0.5f);
			float dx = position.X - pos.Value.X;
			float dz = position.Z - pos.Value.Z;
			if (dx * dx + dz * dz < minDist * minDist)
			{
				obstructed = true;
			}
		});
		return obstructed;
	}

	public bool IsPositionBlocked(Vector3 pos, float checkRadius, Entity ignoreEntity)
	{
		bool blocked = false;

		var query = QueryCache.AllPositionNoneDeadQuery;
		GameContext.World.Query(in query, (Entity entity, ref Position entityPos) =>
		{
			if (entity == ignoreEntity) return;

			float r1 = checkRadius;
			float r2 = 1.2f;

			if (GameContext.World.Has<DefinitionId>(entity))
			{
				string defId = GameContext.World.Get<DefinitionId>(entity).Value;
				float scale = GameContext.World.Has<ModelScale>(entity) ? GameContext.World.Get<ModelScale>(entity).Value : 1.0f;
				r2 = GetPlacementRadius(defId, scale);
			}
			else if (GameContext.World.Has<PropIdentity>(entity))
			{
				string propId = GameContext.World.Get<PropIdentity>(entity).PropId;
				float scale = GameContext.World.Has<ModelScale>(entity) ? GameContext.World.Get<ModelScale>(entity).Value : 1.0f;
				r2 = GetPlacementRadius(propId, scale);
			}
			else
			{
				float scale = GameContext.World.Has<ModelScale>(entity) ? GameContext.World.Get<ModelScale>(entity).Value : 1.0f;
				r2 = scale * 1.2f;
			}

			float dx = entityPos.Value.X - pos.X;
			float dz = entityPos.Value.Z - pos.Z;
			float distXZ = (float)Math.Sqrt(dx * dx + dz * dz);

			if (distXZ < (r1 + r2) * 0.85f)
			{
				blocked = true;
			}
		});

		return blocked;
	}

	public float GetPlacementRadius(string placeId, float scale = 1.0f)
	{
		return GameContext.RegistryService.GetPlacementRadius(placeId, scale);
	}

	public Vector3? FindNearestFreePosition(Vector3 startPos, float checkRadius, float maxSearchDist = 20.0f, float terrainQuadSize = 2.0f, int terrainWidth = 256, int terrainDepth = 256)
	{
		if (!IsPositionBlocked(startPos, checkRadius, Entity.Null))
		{
			return startPos;
		}

		float stepDist = 1.0f;
		int numSteps = (int)Math.Ceiling(maxSearchDist / stepDist);

		for (int i = 1; i <= numSteps; i++)
		{
			float dist = i * stepDist;
			int numAngles = 8 + i * 4;
			for (int a = 0; a < numAngles; a++)
			{
				float angle = a * ((float)Math.PI * 2.0f) / numAngles;
				var testPos = new Vector3(
					startPos.X + dist * (float)Math.Cos(angle),
					startPos.Y,
					startPos.Z + dist * (float)Math.Sin(angle)
				);

				float halfW = (terrainWidth - 1) / 2.0f * terrainQuadSize;
				float halfD = (terrainDepth - 1) / 2.0f * terrainQuadSize;
				if (Math.Abs(testPos.X) > halfW || Math.Abs(testPos.Z) > halfD) continue;

				if (!IsPositionBlocked(testPos, checkRadius, Entity.Null))
				{
					return testPos;
				}
			}
		}

		return null;
	}

	public bool TryExecuteSpellCast(Entity playerEntity, string spellId, out float cooldownMax)
	{
		return TryExecuteSpellCast(playerEntity, Entity.Null, spellId, out cooldownMax);
	}

	private bool ConsumeManaIfSufficient(Entity entity, string spellId)
	{
		if (!GameContext.World.Has<Mana>(entity)) return true;

		ref var mana = ref GameContext.World.Get<Mana>(entity);
		float cost = 0f;
		if (GameHost.Instance != null)
		{
			var def = GameHost.Instance.GetAbilityDefinition(spellId);
			if (def != null) cost = def.ManaCost;
		}

		if (mana.Current < cost) return false;

		mana.Current = MathF.Max(0f, mana.Current - cost);
		return true;
	}

	private bool TryApplyCooldown(Entity entity, string spellId, float cooldownMax)
	{
		if (GameContext.World.Has<Cooldowns>(entity))
		{
			var cds = GameContext.World.Get<Cooldowns>(entity).Value;
			if (cds.TryGetValue(spellId, out float currentCd) && currentCd > 0f) return false;
			cds[spellId] = cooldownMax;
		}

		if (GameContext.World.Has<SpellCooldowns>(entity))
		{
			var scd = GameContext.World.Get<SpellCooldowns>(entity).Value;
			if (scd != null)
			{
				if (scd.TryGetValue(spellId, out float currentCd) && currentCd > 0f) return false;
				scd[spellId] = cooldownMax;
			}
		}

		return true;
	}

	public bool TryExecuteSpellCast(Entity playerEntity, Entity casterEntity, string spellId, out float cooldownMax)
	{
		cooldownMax = 10.0f;
		if (GameHost.Instance != null)
		{
			var def = GameHost.Instance.GetAbilityDefinition(spellId);
			if (def != null && def.Cooldown > 0f)
			{
				cooldownMax = def.Cooldown;
			}
		}

		if (casterEntity != Entity.Null && GameContext.World.IsAlive(casterEntity))
		{
			if (!ConsumeManaIfSufficient(casterEntity, spellId)) return false;

			if (!TryApplyCooldown(casterEntity, spellId, cooldownMax)) return false;
		}

		if (GameContext.World.IsAlive(playerEntity))
		{
			if (!TryApplyCooldown(playerEntity, spellId, cooldownMax)) return false;
		}

		return true;
	}

	public bool BuyItem(string itemId, Entity playerEntity, Vector3 shopPosition, Entity selectedUnitEntity, out Entity targetUnitEntity)
	{
		targetUnitEntity = Entity.Null;

		Entity target = FindClosestFriendlyCombatUnit(shopPosition, playerEntity, 20.0f);

		if (target == Entity.Null && GameContext.World.IsAlive(selectedUnitEntity))
		{
			bool isBuilding = GameContext.World.Has<Building>(selectedUnitEntity);
			bool isOwnedByPlayer = GameContext.World.Has<Owner>(selectedUnitEntity) && GameContext.World.Get<Owner>(selectedUnitEntity).PlayerEntity.Value == playerEntity;
			if (!isBuilding && isOwnedByPlayer && GameContext.World.Has<Inventory>(selectedUnitEntity))
			{
				target = selectedUnitEntity;
			}
		}

		if (target != Entity.Null)
		{
			targetUnitEntity = target;
			Realm.Client.Utils.ItemGrantingUtility.EquipItem(GameContext.World, target, itemId);
			return true;
		}

		return false;
	}

	private Entity FindClosestFriendlyCombatUnit(Vector3 originPos, Entity playerEntity, float maxDistance)
	{
		Entity closestUnit = Entity.Null;
		float closestDist = maxDistance;

		var query = QueryCache.AllPositionAndOwnerAndInventoryNoneDeadAndBuildingQuery;
		GameContext.World.Query(in query, (Entity entity, ref Position pos, ref Owner owner) =>
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
		if (!GameContext.World.IsAlive(unitEntity) || !GameContext.World.Has<Inventory>(unitEntity) || !GameContext.World.Has<Health>(unitEntity))
			return false;

		ref var inv = ref GameContext.World.Get<Inventory>(unitEntity);
		int currentCount = inv.GetItemCount(itemId);
		if (currentCount <= 0) return false;

		ref var hp = ref GameContext.World.Get<Health>(unitEntity);
		if (hp.Current >= hp.Max) return false;

		int newCount = currentCount - 1;
		inv.SetItemCount(itemId, newCount);
		float oldCurrent = hp.Current;
		hp.Current = Math.Min(hp.Max, hp.Current + 50f);
		healedAmount = hp.Current - oldCurrent;

		if (newCount <= 0)
		{
			Realm.Client.Utils.ItemGrantingUtility.UnequipItem(GameContext.World, unitEntity, itemId);
		}

		return true;
	}

	public bool IsUnitAlive(Entity unitEntity)
	{
		return GameContext.World.IsAlive(unitEntity);
	}

	public bool TryGetItemMeta(string itemId, out ItemMetadata itemMeta)
	{
        var itemRegistry = GameContext.RegistryService?.ItemRegistry;
        if (itemRegistry != null)
        {
            return itemRegistry.TryGetValue(itemId, out itemMeta);
        }
        itemMeta = null;
        return false;
	}

	public AbilityDefinition GetAbilityDefinition(string abilityId)
	{
		return GameContext.RegistryService.GetAbilityDefinition(abilityId);
	}

	public bool IsUnitAtFullHealth(Entity unitEntity)
	{
		if (GameContext.World.IsAlive(unitEntity) && GameContext.World.Has<Health>(unitEntity))
		{
			var hp = GameContext.World.Get<Health>(unitEntity);
			return hp.Current >= hp.Max;
		}
		return false;
	}

	public Entity CreateInstantBuildingEcsUnit(string type, UnitMetadata meta, System.Numerics.Vector3 position, Entity playerEntity)
	{
		return GameContext.UnitSpawnService.CreateEcsUnitEntity(
			type, meta.Name, meta.MaxHp, meta.Damage, meta.Range, meta.Armor, 0f, 
			meta.SightRange, false, 1.5f, meta.PopCost, 
			new Godot.Vector3(position.X, position.Y, position.Z), playerEntity.AsPlayerEntity(GameContext.World), playerEntity, 
			false, false, 
			meta.Abilities, meta.CostGold, meta.CostWood, meta.CostStone, 
			meta.ProductionTime, 0f, meta.ArmorType, 0f, 0f, 
			0f, meta.AttackType, 0f, 0f, 
			meta.MovementType, 0f, 1f, 0f, 
			meta.HpRegen, 0f, 0f, false, 
			(int)meta.GoldBounty, "Ground");
	}



	public bool TryAssignFirstWorkerTask(Entity firstWorkerEntity, string type, System.Numerics.Vector3 buildingPos, bool shiftHeld)
	{
		if (GameContext.World.IsAlive(firstWorkerEntity))
		{
			AssignFirstWorkerTask(firstWorkerEntity, type, buildingPos, shiftHeld, (entity, t, pos) => {
				if (!GameContext.World.Has<BuildTask>(entity))
				{
					GameContext.World.SetOrAdd(entity, new MoveTo(pos));
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
		if (GameContext.World != null && GameContext.World.IsAlive(ECSContext.WorldEntity) && GameContext.World.Has<CameraState>(ECSContext.WorldEntity))
		{
			state = GameContext.World.Get<CameraState>(ECSContext.WorldEntity);
			return true;
		}

		state = default;
		return false;
	}

	public void TrySaveCameraSlot(int slotIndex, Vector3 pos, float zoom)
	{
		if (GameContext.World == null || !GameContext.World.IsAlive(ECSContext.WorldEntity) || !GameContext.World.Has<CameraState>(ECSContext.WorldEntity)) return;

		ref var state = ref GameContext.World.Get<CameraState>(ECSContext.WorldEntity);
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

	public Entity GetNextProductionStructure(List<Entity> candidateBuildings)
	{
		if (candidateBuildings == null || candidateBuildings.Count == 0) return Entity.Null;

		int count = candidateBuildings.Count;
		int startIndex = Math.Abs(_productionRoundRobinIndex) % count;

		for (int i = 0; i < count; i++)
		{
			int currIdx = (startIndex + i) % count;
			var bldEntity = candidateBuildings[currIdx];
			if (!GameContext.World.IsAlive(bldEntity)) continue;

			int queueCount = 0;
			if (GameContext.World.Has<ProductionQueue>(bldEntity))
			{
				queueCount = GameContext.World.Get<ProductionQueue>(bldEntity).UnitIds.Count;
			}

			if (queueCount < 5)
			{
				_productionRoundRobinIndex = (currIdx + 1) % count;
				return bldEntity;
			}
		}

		return Entity.Null;
	}

	public bool TryQueueUnit(Entity playerEntity, Entity buildingEntity, string unitId, int popCost, float productionTime)
	{
		if (!GameContext.World.IsAlive(playerEntity) || !GameContext.World.IsAlive(buildingEntity)) return false;

		if (GameContext.World.Has<PlayerPopulation>(playerEntity))
		{
			ref var pop = ref GameContext.World.Get<PlayerPopulation>(playerEntity);
			if (popCost > 0 && pop.Current + popCost > pop.Max)
			{
				return false;
			}

			pop.Current += popCost;
		}

		ref var prod = ref GameContext.World.Has<ProductionQueue>(buildingEntity)
			? ref GameContext.World.Get<ProductionQueue>(buildingEntity)
			: ref CreateProductionQueue(buildingEntity);

		if (prod.UnitIds.Count >= 5) return false;

		if (prod.UnitIds.Count == 0)
		{
			prod.BuildTime = productionTime;
		}

		prod.UnitIds.Add(unitId);
		GameContext.World.Set(buildingEntity, prod);

		return true;
	}

	private ref ProductionQueue CreateProductionQueue(Entity buildingEntity)
	{
		GameContext.World.Add(buildingEntity, new ProductionQueue());
		return ref GameContext.World.Get<ProductionQueue>(buildingEntity);
	}

	private void RefundPopulationCost(Entity buildingEntity, int popCost)
	{
		if (popCost <= 0) return;
		if (!GameContext.World.Has<Owner>(buildingEntity)) return;

		var ownerPlayerEntity = GameContext.World.Get<Owner>(buildingEntity).PlayerEntity.Value;
		if (GameContext.World.IsAlive(ownerPlayerEntity) && GameContext.World.Has<PlayerPopulation>(ownerPlayerEntity))
		{
			ref var pop = ref GameContext.World.Get<PlayerPopulation>(ownerPlayerEntity);
			pop.Current = Math.Max(0, pop.Current - popCost);
		}
	}

	public bool CancelQueuedUnitAt(Entity buildingEntity, int index, out string? cancelledUnitId, out string? nextUnitId, int popCost = 0)
	{
		cancelledUnitId = null;
		nextUnitId = null;

		if (!GameContext.World.IsAlive(buildingEntity) || !GameContext.World.Has<ProductionQueue>(buildingEntity))
			return false;

		var prod = GameContext.World.Get<ProductionQueue>(buildingEntity);
		if (index < 0 || index >= prod.UnitIds.Count)
			return false;

		cancelledUnitId = prod.UnitIds[index];
		prod.UnitIds.RemoveAt(index);

		if (index == 0)
		{
			prod.CurrentProgress = 0f;
			if (prod.UnitIds.Count > 0)
			{
				nextUnitId = prod.UnitIds[0];
			}
		}

		RefundPopulationCost(buildingEntity, popCost);

		GameContext.World.Set(buildingEntity, prod);
		return true;
	}


	public void ApplySpawnRallyPointOrMove(Entity entity, Entity buildingEntity, Vector3 defaultPosition)
	{
		if (!TryApplyRallyPoint(entity, buildingEntity))
		{
			GameContext.World.Add(entity, new MoveTo(defaultPosition));
		}
	}

	public bool TryApplyRallyPoint(Entity entity, Entity buildingEntity)
	{
		if (!GameContext.World.IsAlive(buildingEntity) || !GameContext.World.Has<RallyPoint>(buildingEntity))
		{
			return false;
		}

		var rp = GameContext.World.Get<RallyPoint>(buildingEntity);
		if (rp.Count <= 0)
		{
			return false;
		}

		GameContext.World.Add(entity, new MoveTo(rp.Waypoints[0]));
		if (rp.Count > 1)
		{
			var wq = new WaypointQueue(rp.Waypoints[1]);
			for (int i = 2; i < rp.Count; i++)
			{
				wq.Add(rp.Waypoints[i]);
			}

			GameContext.World.Add(entity, wq);
		}

		return true;
	}

	public void SetRallyPoint(Entity buildingEntity, Vector3 position, bool queue)
	{
		if (!GameContext.World.IsAlive(buildingEntity)) return;

		if (queue)
		{
			if (GameContext.World.Has<RallyPoint>(buildingEntity))
			{
				var rp = GameContext.World.Get<RallyPoint>(buildingEntity);
				rp.Add(position);
				GameContext.World.Set(buildingEntity, rp);
			}
			else
			{
				var rp = new RallyPoint(position);
				GameContext.World.Add(buildingEntity, rp);
			}
		}
		else
		{
			var rp = new RallyPoint(position);
			GameContext.World.SetOrAdd(buildingEntity, rp);
		}
	}

	public void SetEntityPosition(Entity entity, Vector3 position)
	{
		if (GameContext.World.IsAlive(entity))
		{
			GameContext.World.Set(entity, new Position(position));
		}
	}

	public string? ActiveSpellTargeting
	{
		get
		{
			var worldEntity = GetWorldEntity();
			return worldEntity != Entity.Null && GameContext.World.Has<InputState>(worldEntity)
				? GameContext.World.Get<InputState>(worldEntity).ActiveSpellTargeting
				: null;
		}
		set
		{
			var worldEntity = GetWorldEntity();
			if (worldEntity == Entity.Null || !GameContext.World.Has<InputState>(worldEntity)) return;
			ref var state = ref GameContext.World.Get<InputState>(worldEntity);
			state.ActiveSpellTargeting = value;
		}
	}

	public string? ActiveCommandTargeting
	{
		get
		{
			var worldEntity = GetWorldEntity();
			return worldEntity != Entity.Null && GameContext.World.Has<InputState>(worldEntity)
				? GameContext.World.Get<InputState>(worldEntity).ActiveCommandTargeting
				: null;
		}
		set
		{
			var worldEntity = GetWorldEntity();
			if (worldEntity == Entity.Null || !GameContext.World.Has<InputState>(worldEntity)) return;
			ref var state = ref GameContext.World.Get<InputState>(worldEntity);
			state.ActiveCommandTargeting = value;
		}
	}

	public string? ActiveBuildingPlacementType
	{
		get
		{
			var worldEntity = GetWorldEntity();
			return worldEntity != Entity.Null && GameContext.World.Has<InputState>(worldEntity)
				? GameContext.World.Get<InputState>(worldEntity).ActiveBuildingPlacementType
				: null;
		}
		set
		{
			var worldEntity = GetWorldEntity();
			if (worldEntity == Entity.Null || !GameContext.World.Has<InputState>(worldEntity)) return;
			ref var state = ref GameContext.World.Get<InputState>(worldEntity);
			state.ActiveBuildingPlacementType = value;
		}
	}

	public bool ActivePingMode
	{
		get
		{
			var worldEntity = GetWorldEntity();
			return worldEntity != Entity.Null && GameContext.World.Has<InputState>(worldEntity)
			                                  && GameContext.World.Get<InputState>(worldEntity).ActivePingMode;
		}
		set
		{
			var worldEntity = GetWorldEntity();
			if (worldEntity == Entity.Null || !GameContext.World.Has<InputState>(worldEntity)) return;
			ref var state = ref GameContext.World.Get<InputState>(worldEntity);
			state.ActivePingMode = value;
		}
	}

	public int GetCycleSelectionIndex(int selectedCount)
	{
		if (selectedCount == 0) return 0;
		var worldEntity = GetWorldEntity();
		if (worldEntity == Entity.Null || !GameContext.World.Has<InputState>(worldEntity)) return 0;
		int val = GameContext.World.Get<InputState>(worldEntity).CycleSelectionIndex;
		return Math.Clamp(val, 0, selectedCount - 1);
	}

	public void SetCycleSelectionIndex(int value, int selectedCount)
	{
		var worldEntity = GetWorldEntity();
		if (worldEntity == Entity.Null || !GameContext.World.Has<InputState>(worldEntity)) return;
		ref var state = ref GameContext.World.Get<InputState>(worldEntity);
		state.CycleSelectionIndex = selectedCount > 0 ? Math.Clamp(value, 0, selectedCount - 1) : 0;
	}

	public void SetSpellCooldown(Entity playerEntity, string spellId, float cooldown)
	{
		if (playerEntity == Entity.Null || !GameContext.World.IsAlive(playerEntity)) return;
		if (GameContext.World.Has<SpellCooldowns>(playerEntity))
		{
			var scd = GameContext.World.Get<SpellCooldowns>(playerEntity).Value;
			if (scd != null)
			{
				scd[spellId] = cooldown;
			}
		}
	}

	public float GetSpellCooldown(Entity playerEntity, string spellId)
	{
		if (playerEntity == Entity.Null || !GameContext.World.IsAlive(playerEntity)) return 0f;
		if (GameContext.World.Has<SpellCooldowns>(playerEntity))
		{
			var scd = GameContext.World.Get<SpellCooldowns>(playerEntity).Value;
			if (scd != null && scd.TryGetValue(spellId, out float cd))
			{
				return cd;
			}
		}

		return 0f;
	}

	public void ApplySpellEffect(AbilityDefinition def, Vector3 simPos, Entity target)
	{
		float aoe = def.AreaOfEffectRadius > 0f ? def.AreaOfEffectRadius : 4.0f;

		if (def.Damage > 0f)
		{
			GameContext.SimulationService.DealSpellDamageAOE(simPos, aoe, def.Damage, target);
		}
		else if (def.Healing > 0f)
		{
			GameContext.SimulationService.HealAOE(simPos, aoe, def.Healing, target);
		}
	}

	public bool TryGetCasterPosition(Entity casterEntity, out Vector3 position)
	{
		position = default;
		if (!GameContext.World.IsAlive(casterEntity) || !GameContext.World.Has<Position>(casterEntity)) return false;
		position = GameContext.World.Get<Position>(casterEntity).Value;
		return true;
	}
}