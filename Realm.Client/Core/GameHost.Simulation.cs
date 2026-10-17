using System;
using System.Collections.Generic;
using Arch.Core;
using Godot;
using Realm.Client.ReplaySystem;
using Realm.Client.Network;
using Realm.Client.Services;
using Realm.Client.UI;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Resources;
using Realm.Ecs.Components.Tags;
using Realm.Ecs.Components.Terrain;
using Realm.Ecs.Services;
using Realm.MapAPI;
using Vector3 = System.Numerics.Vector3;

namespace Realm.Client.Core;

public partial class GameHost
{
	// Upper bound for _warnedNonFinitePositions. Once it grows past this, stale warnings for
	// already-destroyed entities are swept so recycled entity ids get a fresh warning later.

	private void KillUnit(Unit3D unit)
	{
		KillUnit(unit, true, true);
	}

	private void KillUnit(Unit3D unit, bool executeDespawnShader, bool playDeathAnimation)
	{
		IUnit victimWrapper = null;
		if (GameContext.World.IsAlive(unit.Entity))
		{
			victimWrapper = GameContext.SimulationService.GetUnitWrapper(unit.Entity);
		}

		Entity killerEntity = GameContext.SimulationService.GetKillerEntityDomain(unit.Entity);
		
		IUnit killerWrapper = null;
		if (killerEntity != Entity.Null && GameContext.World.IsAlive(killerEntity))
		{
			killerWrapper = GameContext.SimulationService.GetUnitWrapper(killerEntity);
		}

		if (victimWrapper != null)
		{
			OnUnitDied?.Invoke(victimWrapper, killerWrapper);
		}

		GameContext.AudioService?.PlayUnitSound(unit.UnitId, UnitSoundEvent.Death, unit.GlobalPosition);
		RemoveUnitFromCollections(unit);
		HandleUnitDeathEconomy(unit);
		HandleMultiplayerUnitDeath(unit);

		string unitId = unit.UnitId;
		int unitPlayer = unit.Player;

		GameContext.SimulationService.DestroyUnitEntityDomain(unit.Entity);

		PlayDeathEffects(unit, executeDespawnShader, playDeathAnimation, unitId);

		GD.Print($"Unit {unit.Name} died.");
	}

	private void RemoveUnitFromCollections(Unit3D unit)
	{
		SelectedUnits.Remove(unit);
		AllUnits.Remove(unit);

		if (!unit.IsBuilding) return;

		var fallbackPos = new System.Numerics.Vector3(unit.Position.X, unit.Position.Y, unit.Position.Z);
		var (radius, unitPos) = GameContext.SimulationService.GetObstacleUncarveData(unit.Entity, fallbackPos);
		UncarveObstacle(new Vector3(unitPos.X, unitPos.Y, unitPos.Z), radius);
	}

	private void HandleUnitDeathEconomy(Unit3D unit)
	{
		int popCost = GameContext.SimulationService.GetDeathPopulationCost(unit.Entity, unit.UnitId);
		int playerIndex = unit.Player;

		if (popCost > 0)
		{
			int curPop = GameContext.ResourceEconomyService?.GetPlayerCurrentPopulation(playerIndex) ?? 0;
			GameContext.ResourceEconomyService?.SetPlayerCurrentPopulation(playerIndex, Math.Max(0, curPop - (int)popCost));
		}

		HandleUnitBountyEconomy(unit);
	}

	private void HandleUnitBountyEconomy(Unit3D unit)
	{
		float resCap = ECSContext.PlayerResourceService.ResourceCap;
		Entity beneficiaryPlayerEntity = Entity.Null;

		if (GameContext.World.IsAlive(unit.Entity) && GameContext.World.Has<LastAttacker>(unit.Entity))
		{
			Entity killer = GameContext.World.Get<LastAttacker>(unit.Entity).Value;
			if (GameContext.World.IsAlive(killer) && GameContext.World.Has<Owner>(killer))
			{
				beneficiaryPlayerEntity = GameContext.World.Get<Owner>(killer).PlayerEntity.Value;
			}
		}

		if (beneficiaryPlayerEntity == Entity.Null && (LobbyManager.Instance == null || LobbyManager.Instance.IsSinglePlayer))
		{
			int localSlot = LocalViewingPlayerIndex;
			beneficiaryPlayerEntity = GetPlayerEntityForPlayerIndex(localSlot);
		}

		if (beneficiaryPlayerEntity != Entity.Null)
		{
			bool shouldRefresh = GameContext.SimulationService.HandleEnemyDeathEconomyDomain(unit.Entity, unit.UnitId, beneficiaryPlayerEntity, (int)resCap);
			
			int localViewingSlot = LocalViewingPlayerIndex;
			Entity localPlayerEntity = GetPlayerEntityForPlayerIndex(localViewingSlot);
			if (shouldRefresh && beneficiaryPlayerEntity == localPlayerEntity)
			{
				InGameHUD.Instance?.RefreshUI(SelectedUnits);
			}
		}
	}

	private void HandleMultiplayerUnitDeath(Unit3D unit)
	{
		if (!_multiplayerActive) return;

		GameContext.NetworkService.RemoveClientToServerMapping(unit.Entity.Id);
	}

	private void PlayDeathEffects(Unit3D unit, bool executeDespawnShader, bool playDeathAnimation, string unitId)
	{
		if (!GodotObject.IsInstanceValid(unit)) return;

		if (playDeathAnimation)
		{
			unit.PlayAnimation("Death");
		}

		unit.CollisionLayer = 0;
		unit.CollisionMask = 0;

		if (executeDespawnShader && TryExecuteDeathShader(unit, unitId)) return;
		if (playDeathAnimation && TryPlayDeathTween(unit)) return;

		unit.QueueFree();
	}

	private bool TryExecuteDeathShader(Unit3D unit, string unitId)
	{
		string deathShader = GetModelDeathShader(unitId);
		if (string.IsNullOrEmpty(deathShader))
		{
			deathShader = GetModelDeathShader(unit);
		}

		if (string.IsNullOrEmpty(deathShader)) return false;

		SpawnDeathShaderManager.AnimateTransition(unit, deathShader, false, null, () =>
		{
			if (GodotObject.IsInstanceValid(unit)) unit.QueueFree();
		});
		return true;
	}

	private bool TryPlayDeathTween(Unit3D unit)
	{
		var tween = CreateTween();
		tween.SetParallel(true);
		tween.TweenProperty(unit, "position:y", -3.0f, 1.0f);
		tween.TweenProperty(unit, "scale", Godot.Vector3.Zero, 1.0f);
		tween.Chain().TweenCallback(Callable.From(unit.QueueFree));
		return true;
	}

	private void DepleteProp(Prop3D prop)
	{
		if (GodotObject.IsInstanceValid(prop))
		{
			string propId = prop.PropId;
			var fallbackPos = new Vector3(prop.Position.X, prop.Position.Y, prop.Position.Z);
			AllProps.Remove(prop);
			EntityToProp3D.Remove(prop.Entity);
			var (radius, propPos) = GameContext.SimulationService.DomainDepleteProp(prop.Entity, fallbackPos);

			PropMultiMeshManager.Instance?.MarkDirty(propId);
			UncarveObstacle(propPos, radius);

			string deathShader = GetModelDeathShader(propId);
			if (!string.IsNullOrEmpty(deathShader))
			{
				SpawnDeathShaderManager.AnimateTransition(prop, deathShader, false, null, () =>
				{
					if (GodotObject.IsInstanceValid(prop)) prop.QueueFree();
				});
			}
			else
			{
				prop.QueueFree();
			}
		}
	}

	private float ConstructionWorkRatePerSecond => GameContext.SimulationService.FastBuildEnabled ? SimulationService.BaseConstructionWorkRatePerSecond * 10f : SimulationService.BaseConstructionWorkRatePerSecond;

	// Units face their attack/heal/build target once they are within this distance,
	// even while still moving toward it.

	private List<(Entity Worker, BuildTask UpdatedTask)> _pendingBuildTaskUpdates => GameContext.SimulationService.PendingBuildTaskUpdates;

	private Dictionary<int, List<MeshInstance3D>> _buildQueueGhosts { get; set; } = new();

	internal void TickConstructionSystem(float fDelta)
	{
		_pendingBuildTaskUpdates.Clear();
		GameContext.SimulationService.CompletedBuildings.Clear();

		var workerQuery = QueryCache.AllPositionAndBuildTaskNoneDeadQuery;
		GameContext.World.Query(in workerQuery, (Entity workerEntity, ref Position workerPos, ref BuildTask buildTask) => { ProcessWorkerConstruction(workerEntity, ref workerPos, ref buildTask, fDelta); });

		ProcessPendingBuildTasks();
		ProcessCompletedBuildings();

		if (_pendingBuildTaskUpdates.Count > 0)
		{
			InGameHUD.Instance?.RefreshUI(SelectedUnits);
		}

		GameContext.SimulationService.PendingQueuedCommands.Clear();
		var queueQuery = QueryCache.AllBuildQueueNoneDeadQuery;
		GameContext.World.Query(in queueQuery, (Entity entity) => { GameContext.SimulationService.ProcessBuildQueueEntity(entity); });

		GameContext.SimulationService.ProcessPendingQueuedCommands(ExecuteQueuedCommand);
		UpdateBuildQueueGhosts();
	}

	private void ProcessWorkerConstruction(Entity workerEntity, ref Position workerPos, ref BuildTask buildTask, float fDelta)
	{
		if (!GameContext.World.IsAlive(buildTask.BuildingEntity)) return;

		var buildingPos = GameContext.World.Has<Position>(buildTask.BuildingEntity)
			? GameContext.World.Get<Position>(buildTask.BuildingEntity).Value
			: workerPos.Value;

		float constructionRange = UnitMetadata.DefaultConstructionRange;
		if (GameContext.World.Has<DefinitionId>(workerEntity))
		{
			string workerType = GameContext.World.Get<DefinitionId>(workerEntity).Value;
			if (!UnitRegistry.TryGetValue(workerType, out var workerMeta))
				BuildingRegistry.TryGetValue(workerType, out workerMeta);
			if (workerMeta != null)
				constructionRange = workerMeta.ConstructionRange;
		}

		float buildingCollisionRadius = GameContext.World.Has<CollisionRadius>(buildTask.BuildingEntity)
			? GameContext.World.Get<CollisionRadius>(buildTask.BuildingEntity).Value
			: 0f;
		float effectiveRange = constructionRange + buildingCollisionRadius;
		float distSq = Vector3.DistanceSquared(workerPos.Value, buildingPos);
		bool inRange = distSq <= effectiveRange * effectiveRange;

		if (!inRange)
		{
			if (!GameContext.World.Has<MoveTo>(workerEntity))
			{
				GameContext.World.Add(workerEntity, new MoveTo(buildingPos));
			}

			return;
		}

		if (GameContext.World.Has<MoveTo>(workerEntity))
		{
			GameContext.World.Remove<MoveTo>(workerEntity);
		}

		EnsureBuildingModel(buildTask.BuildingEntity, buildingPos);

		float progressGain = ConstructionWorkRatePerSecond * fDelta;
		var updatedTask = new BuildTask(buildTask.BuildingEntity, buildTask.TotalBuildTime)
		{
			Progress = buildTask.Progress + progressGain
		};
		_pendingBuildTaskUpdates.Add((workerEntity, updatedTask));

		UpdateBuildingConstructionState(buildTask.BuildingEntity, progressGain);
	}

	private void EnsureBuildingModel(Entity buildingEntity, Vector3 buildingPos)
	{
		if (!GameContext.World.Has<DefinitionId>(buildingEntity)) return;

		string bType = GameContext.World.Get<DefinitionId>(buildingEntity).Value;
		bool isBuilding = false;
		if (!UnitRegistry.TryGetValue(bType, out var m) && !(isBuilding = BuildingRegistry.TryGetValue(bType, out m))) return;
		if (TryGetUnit3D(buildingEntity, out _)) return;

		string targetModel = m.ModelPath;
		if (string.IsNullOrEmpty(targetModel)) return;

		string modelPath = GetFallbackModelPath(targetModel, isBuilding);
		int ownerPlayer = GameContext.World.Has<UnitOwnerPlayer>(buildingEntity)
			? GameContext.World.Get<UnitOwnerPlayer>(buildingEntity).PlayerIndex
			: 0;
		SpawnUnit3D(buildingEntity, bType, modelPath, new Godot.Vector3(buildingPos.X, buildingPos.Y, buildingPos.Z), isBuilding, ownerPlayer, false, false);

		if (TryGetUnit3D(buildingEntity, out var bNode) && GodotObject.IsInstanceValid(bNode))
		{
			bNode.Modulate = new Color(1f, 1f, 1f, 0.4f);
		}
	}

	private void UpdateBuildingConstructionState(Entity buildingEntity, float progressGain)
	{
		if (!GameContext.World.Has<ConstructionState>(buildingEntity)) return;

		ref var constructionState = ref GameContext.World.Get<ConstructionState>(buildingEntity);
		constructionState.Progress = Mathf.Min(constructionState.Progress + progressGain, constructionState.TotalBuildTime);

		if (constructionState.Progress >= constructionState.TotalBuildTime && !GameContext.SimulationService.CompletedBuildings.Contains(buildingEntity))
		{
			GameContext.SimulationService.CompletedBuildings.Add(buildingEntity);
		}
	}

	private void ProcessPendingBuildTasks()
	{
		foreach (var (workerEntity, updatedTask) in _pendingBuildTaskUpdates)
		{
			if (!GameContext.World.IsAlive(workerEntity)) continue;

			GameContext.World.Set(workerEntity, updatedTask);

			if (updatedTask.Progress >= updatedTask.TotalBuildTime && GameContext.World.Has<BuildTask>(workerEntity))
			{
				GameContext.World.Remove<BuildTask>(workerEntity);
				TryStartNextQueuedCommand(workerEntity);
			}
		}
	}

	private void TryStartNextQueuedCommand(Entity workerEntity)
	{
		if (!GameContext.World.Has<BuildQueue>(workerEntity)) return;

		ref var buildQueue = ref GameContext.World.Get<BuildQueue>(workerEntity);
		bool startedNext = false;
		while (buildQueue.TryDequeue(out string? nextType, out var nextPos, out Entity nextTarget))
		{
			if (ExecuteQueuedCommand(workerEntity, nextType, nextPos, nextTarget))
			{
				startedNext = true;
				break;
			}
		}

		if (!startedNext && buildQueue.Count == 0)
		{
			GameContext.World.Remove<BuildQueue>(workerEntity);
		}
	}

	private void ProcessCompletedBuildings()
	{
		foreach (var buildingEntity in GameContext.SimulationService.CompletedBuildings)
		{
			ProcessCompletedBuilding(buildingEntity);
		}
	}

	private void ProcessCompletedBuilding(Entity buildingEntity)
	{
		GameContext.SimulationService.DomainProcessCompletedBuilding(buildingEntity, ResetBuildingVisuals, NotifyConstructionFinished);
	}

	private void RemoveConstructionComponents(Entity buildingEntity)
	{
		GameContext.SimulationService.RemoveConstructionComponents(buildingEntity);
	}

	private void ResetBuildingVisuals(Entity buildingEntity)
	{
		if (!TryGetUnit3D(buildingEntity, out var buildingNode) || !GodotObject.IsInstanceValid(buildingNode)) return;

		buildingNode.Modulate = new Color(1f, 1f, 1f, 1f);
		SpawnDeathShaderManager.ClearShaderOverride(buildingNode);
	}

	private void NotifyConstructionFinished(Entity buildingEntity)
	{
		var buildingUnit = GameContext.World.IsAlive(buildingEntity) ? GameContext.SimulationService.GetUnitWrapper(buildingEntity) : null;
		if (buildingUnit != null)
		{
			OnConstructionFinished?.Invoke(buildingUnit);
		}

		InGameHUD.Instance?.ShowFeedbackText("Construction complete!", new Color(0.3f, 0.9f, 0.4f));
		InGameHUD.Instance?.RefreshUI(SelectedUnits);
	}

	private void UpdateBuildQueueGhosts()
	{
		var workerQuery = QueryCache.AllPositionAndBuildTaskNoneDeadQuery;
		var activeWorkerIds = new HashSet<int>();

		GameContext.World.Query(in workerQuery, (Entity workerEntity, ref BuildTask _) => { ProcessWorkerGhostQueue(workerEntity, activeWorkerIds); });

		CleanupInactiveGhosts(activeWorkerIds);
	}

	private void ProcessWorkerGhostQueue(Entity workerEntity, HashSet<int> activeWorkerIds)
	{
		if (!GameContext.World.IsAlive(workerEntity)) return;
		activeWorkerIds.Add(workerEntity.Id);

		int queueCount = GameContext.World.Has<BuildQueue>(workerEntity) ? GameContext.World.Get<BuildQueue>(workerEntity).Count : 0;

		if (!_buildQueueGhosts.TryGetValue(workerEntity.Id, out var ghosts))
		{
			ghosts = new List<MeshInstance3D>();
			_buildQueueGhosts[workerEntity.Id] = ghosts;
		}

		while (ghosts.Count > queueCount)
		{
			int last = ghosts.Count - 1;
			if (GodotObject.IsInstanceValid(ghosts[last]))
				ghosts[last].QueueFree();
			ghosts.RemoveAt(last);
		}

		if (queueCount == 0) return;

		ref var queue = ref GameContext.World.Get<BuildQueue>(workerEntity);
		for (int slotIndex = 0; slotIndex < queueCount; slotIndex++)
		{
			UpdateGhostAtSlot(slotIndex, ref queue, ghosts);
		}
	}

	private void UpdateGhostAtSlot(int slotIndex, ref BuildQueue queue, List<MeshInstance3D> ghosts)
	{
		queue.PeekAt(slotIndex, out string? buildType, out var queuedPos);
		if (buildType == null) return;

		var worldPos = new Godot.Vector3(queuedPos.X, GetTerrainHeightAt(new Godot.Vector3(queuedPos.X, 0, queuedPos.Z)), queuedPos.Z);

		if (slotIndex >= ghosts.Count)
		{
			ghosts.Add(CreateGhostMesh(buildType));
		}
		else if (!GodotObject.IsInstanceValid(ghosts[slotIndex]))
		{
			ghosts[slotIndex] = CreateGhostMesh(buildType);
		}

		ghosts[slotIndex].GlobalPosition = worldPos;
	}

	private void CleanupInactiveGhosts(HashSet<int> activeWorkerIds)
	{
		var toRemove = new List<int>();
		foreach (var kv in _buildQueueGhosts)
		{
			if (!activeWorkerIds.Contains(kv.Key))
			{
				foreach (var ghost in kv.Value)
					if (GodotObject.IsInstanceValid(ghost))
						ghost.QueueFree();
				toRemove.Add(kv.Key);
			}
		}

		foreach (var id in toRemove)
			_buildQueueGhosts.Remove(id);
	}

	private MeshInstance3D CreateGhostMesh(string buildType)
	{
		var mesh = new MeshInstance3D();
		float bWidth = 3.2f;
		float bHeight = 4f;
		if (TryGetUnitOrBuildingMetadata(buildType, out var bMeta))
		{
			if (bMeta.ObstacleRadius.HasValue && bMeta.ObstacleRadius.Value > 0)
			{
				bWidth = bMeta.ObstacleRadius.Value * 2f;
				bHeight = Math.Max(4f, bMeta.ObstacleRadius.Value);
			}
			else if (bMeta.CollisionCircle > 0)
			{
				bWidth = bMeta.CollisionCircle * 2f;
				bHeight = Math.Max(4f, bMeta.CollisionCircle);
			}
		}
		var box = new BoxMesh();
		box.Size = new Godot.Vector3(bWidth, bHeight, bWidth);
		mesh.Mesh = box;

		var mat = new StandardMaterial3D();
		mat.AlbedoColor = new Color(1.0f, 0.65f, 0.1f, 0.35f);
		mat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
		mat.EmissionEnabled = true;
		mat.Emission = new Color(1.0f, 0.5f, 0.05f) * 0.25f;
		mat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
		mesh.MaterialOverride = mat;

		AddChild(mesh);
		return mesh;
	}

	internal void ClearAllBuildQueueGhosts()
	{
		foreach (var kv in _buildQueueGhosts)
		foreach (var ghost in kv.Value)
			if (GodotObject.IsInstanceValid(ghost))
				ghost.QueueFree();
		_buildQueueGhosts.Clear();
	}

	internal void AssignBuildTaskToWorker(Entity workerEntity, string buildType, Vector3 targetPos)
	{
		bool isBuilding = false;
		if (!UnitRegistry.TryGetValue(buildType, out var meta) && !(isBuilding = BuildingRegistry.TryGetValue(buildType, out meta))) return;
		string targetModel = meta.ModelPath;
		if (string.IsNullOrEmpty(targetModel)) return;
		float buildTime = Math.Max(meta.ProductionTime, 0);

		if (!GameContext.World.Has<UnitOwnerPlayer>(workerEntity)) return;
		int workerPlayerIndex = GameContext.World.Get<UnitOwnerPlayer>(workerEntity).PlayerIndex;

		var playerOwner = GetPlayerEntityForPlayerIndex(workerPlayerIndex).AsPlayerEntity(GameContext.World);

		var bldEntity = CreateEcsUnit(buildType, meta.Name, meta.MaxHp, meta.Damage, meta.Range, meta.Armor, meta.Speed,
			new Godot.Vector3(targetPos.X, targetPos.Y, targetPos.Z), playerOwner);
		GameContext.World.SetOrAdd(bldEntity, new UnitOwnerPlayer(workerPlayerIndex));

		GameContext.World.Add(bldEntity, new ConstructionState(buildTime));
		GameContext.World.Add(bldEntity, new UnderConstruction());

		float bldRadius = GetOrCalculateObstacleRadius(buildType, null, isBuilding);
		if (!GameContext.World.Has<CollisionRadius>(bldEntity))
		{
			GameContext.World.Add(bldEntity, new CollisionRadius(bldRadius));
		}

		if (isBuilding)
		{
			CarveObstacle(targetPos, bldRadius);
		}

		EnsureBuildingModel(bldEntity, targetPos);

		var buildTask = new BuildTask(bldEntity, buildTime);
		GameContext.World.SetOrAdd(workerEntity, buildTask);

		var buildingPos = new Vector3(targetPos.X, targetPos.Y, targetPos.Z);
		if (!GameContext.World.Has<MoveTo>(workerEntity))
			GameContext.World.Add(workerEntity, new MoveTo(buildingPos));
		else
			GameContext.World.Set(workerEntity, new MoveTo(buildingPos));
	}

	private void UpdateVisualNodesFromEcs(float fDelta)
	{
		var query = QueryCache.AllPositionAndDefinitionIdQuery;
		GameContext.World.Query(in query, (Entity entity, ref Position pos) => { ProcessVisualNode(entity, ref pos, fDelta); });

		var buildingQuery = new QueryDescription().WithAll<ConstructionState>().WithNone<Dead>();
		GameContext.World.Query(in buildingQuery, (Entity entity, ref ConstructionState construction) =>
		{
			if (TryGetUnit3D(entity, out var buildingNode) && GodotObject.IsInstanceValid(buildingNode))
			{
				float alpha = Mathf.Lerp(0.3f, 1.0f, construction.Progress / Mathf.Max(construction.TotalBuildTime, 0.001f));
				buildingNode.Modulate = new Color(1f, 1f, 1f, alpha);
			}
		});
	}

	private void ProcessVisualNode(Entity entity, ref Position pos, float fDelta)
	{
		if (!TryGetUnit3D(entity, out var unit3D) || !GodotObject.IsInstanceValid(unit3D)) return;

		var posValue = pos.Value;
		if (CheckNonFinitePositionWarning(entity, posValue, unit3D)) return;

		Godot.Vector3 nextPos = new Godot.Vector3(posValue.X, posValue.Y, posValue.Z);
		unit3D.GlobalPosition = nextPos;

		UpdateVisualVelocity(entity, unit3D);
		UpdateVisualRotation(entity, unit3D, nextPos, fDelta);
		UpdateVisualScale(entity, unit3D);

		unit3D.PlayAnimation(DetermineUnitAnimation(entity));
	}

	private bool CheckNonFinitePositionWarning(Entity entity, Vector3 posValue, Unit3D unit3D)
	{
		if (float.IsFinite(posValue.X) && float.IsFinite(posValue.Y) && float.IsFinite(posValue.Z))
			return false;

		if (GameContext.SimulationService.WarnedNonFinitePositions.Add(entity))
		{
			if (GameContext.SimulationService.WarnedNonFinitePositions.Count > SimulationService.WarnedNonFinitePositionsLimit)
			{
				GameContext.SimulationService.WarnedNonFinitePositions.RemoveWhere(warned => !GameContext.World.IsAlive(warned));
			}

			GD.PushWarning($"[Simulation] Unit '{unit3D.Name}' (entity {entity.Id}) has a non-finite ECS position ({posValue.X}, {posValue.Y}, {posValue.Z}); skipping visual sync.");
		}

		return true;
	}

	private void UpdateVisualVelocity(Entity entity, Unit3D unit3D)
	{
		Godot.Vector3 velVec = Godot.Vector3.Zero;
		if (GameContext.World.Has<Velocity>(entity))
		{
			var vel = GameContext.World.Get<Velocity>(entity);
			velVec = new Godot.Vector3(vel.Value.X, vel.Value.Y, vel.Value.Z);
		}

		if (!GameContext.World.Has<MoveTo>(entity) && !GameContext.World.Has<Follow>(entity) && !GameContext.World.Has<InterpolationTarget>(entity))
		{
			velVec = Godot.Vector3.Zero;
			if (GameContext.World.Has<Velocity>(entity))
			{
				GameContext.World.Set(entity, new Velocity(Vector3.Zero));
			}
		}

		unit3D.Velocity = velVec;
	}

	private void UpdateVisualRotation(Entity entity, Unit3D unit3D, Godot.Vector3 nextPos, float fDelta)
	{
		bool hasLookTarget = TryGetLookTarget(entity, out Godot.Vector3 lookTargetPos);
		Godot.Vector3 dir = unit3D.Velocity;
		bool hasDir = dir.LengthSquared() > 0.01f;

		bool forceLookTarget = false;
		if (hasLookTarget)
		{
			if (!hasDir) forceLookTarget = true;
			else
			{
				float distToLook = lookTargetPos.DistanceTo(nextPos);
				if (distToLook < SimulationService.LookTargetProximityDistance || GameContext.World.Has<Follow>(entity)) forceLookTarget = true;
			}
		}

		if (forceLookTarget)
		{
			dir = (lookTargetPos - nextPos);
			dir.Y = 0f; // Keep rotation level
			dir = dir.Normalized();
			hasDir = dir.LengthSquared() > 0.01f;
		}

		if (hasDir)
		{
			ApplyDirectionalRotation(entity, unit3D, nextPos, dir, fDelta);
		}
		else if (GameContext.World.Has<InterpolationTarget>(entity))
		{
			var interp = GameContext.World.Get<InterpolationTarget>(entity);
			var rot = unit3D.Rotation;
			rot.Y = Mathf.LerpAngle(rot.Y, interp.RotationY, 10f * fDelta);
			unit3D.Rotation = rot;
			if (GameContext.World.Has<RotationY>(entity))
			{
				GameContext.World.Set(entity, new RotationY(rot.Y));
			}
		}
	}

	private bool TryGetLookTarget(Entity entity, out Godot.Vector3 lookTargetPos)
	{
		lookTargetPos = Godot.Vector3.Zero;

		if (GameContext.World.Has<AttackTarget>(entity))
			return TryGetTargetPosition(GameContext.World.Get<AttackTarget>(entity).Target, out lookTargetPos);

		if (GameContext.World.Has<HealingTarget>(entity))
			return TryGetTargetPosition(GameContext.World.Get<HealingTarget>(entity).Target, out lookTargetPos);

		if (GameContext.World.Has<Follow>(entity))
			return TryGetTargetPosition(GameContext.World.Get<Follow>(entity).Target, out lookTargetPos);

		if (GameContext.World.Has<BuildTask>(entity))
			return TryGetTargetPosition(GameContext.World.Get<BuildTask>(entity).BuildingEntity, out lookTargetPos);

		return false;
	}

	private bool TryGetTargetPosition(Entity targetEnt, out Godot.Vector3 position)
	{
		position = Godot.Vector3.Zero;
		if (GameContext.World.IsAlive(targetEnt) && GameContext.World.Has<Position>(targetEnt))
		{
			var tPosComp = GameContext.World.Get<Position>(targetEnt);
			position = new Godot.Vector3(tPosComp.Value.X, tPosComp.Value.Y, tPosComp.Value.Z);
			return true;
		}

		return false;
	}

	private void ApplyDirectionalRotation(Entity entity, Unit3D unit3D, Godot.Vector3 nextPos, Godot.Vector3 dir, float fDelta)
	{
		dir = dir.Normalized();
		float angle = Mathf.Atan2(-dir.X, -dir.Z) + Mathf.Pi;
		var rot = unit3D.Rotation;

		bool isFlying = GameContext.World.Has<Realm.Ecs.Components.Movement.PathingFlags>(entity)
		                && ((TerrainPathingFlags)GameContext.World.Get<Realm.Ecs.Components.Movement.PathingFlags>(entity).Value & TerrainPathingFlags.Flying) != 0;
		float turnRate = 10f;
		if (GameContext.World.Has<MovementStats>(entity))
		{
			var moveStats = GameContext.World.Get<MovementStats>(entity);
			if (moveStats.TurnRate > 0f) turnRate = moveStats.TurnRate;
		}

		rot.Y = Mathf.LerpAngle(rot.Y, angle, turnRate * fDelta);
		unit3D.Rotation = rot;
		if (GameContext.World.Has<RotationY>(entity))
		{
			GameContext.World.Set(entity, new RotationY(rot.Y));
		}

		Godot.Vector3 normal = Godot.Vector3.Up;
		if (!isFlying && GroundTerrain != null)
		{
			GroundTerrain.GetHeightAndNormal(nextPos.X, nextPos.Z, out _, out normal);
		}

		Godot.Vector3 forwardDir = new Godot.Vector3(-Mathf.Sin(unit3D.Rotation.Y), 0f, -Mathf.Cos(unit3D.Rotation.Y));
		Godot.Vector3 up = normal.Normalized();
		Godot.Vector3 right = forwardDir.Cross(up);
		if (right.LengthSquared() > 0.00001f)
		{
			right = right.Normalized();
			Godot.Vector3 forwardPerp = right.Cross(up).Normalized();
			Basis targetBasis = new Basis(right, up, forwardPerp);
			var qTarget = targetBasis.GetRotationQuaternion();
			var qCurrent = unit3D.Basis.GetRotationQuaternion();
			var qLerp = qCurrent.Slerp(qTarget, 10f * fDelta);
			unit3D.Basis = new Basis(qLerp);
		}
	}

	private void UpdateVisualScale(Entity entity, Unit3D unit3D)
	{
		bool isLaborAnimating = (GameContext.World.Has<Gatherer>(entity) && !GameContext.World.Get<Gatherer>(entity).ReturningToBase)
		                        || (GameContext.World.Has<BuildTask>(entity) && !GameContext.World.Has<MoveTo>(entity));

		if (isLaborAnimating)
		{
			var state = GameContext.World.Get<WorldState>(ECSContext.WorldEntity);
			float gameElapsed = state.GameElapsedTime;
			float pulse = 1.0f + Mathf.Sin(gameElapsed * 10f) * 0.1f;
			unit3D.Scale = new Godot.Vector3(pulse * 0.9f, (2.0f - pulse) * 0.9f, pulse * 0.9f);
		}
		else
		{
			float scaleVal = GameContext.World.Has<CollisionScale>(entity) ? GameContext.World.Get<CollisionScale>(entity).Value : 1.0f;
			unit3D.Scale = Godot.Vector3.One * Mathf.Max(0.01f, scaleVal);
		}
	}

	private string DetermineUnitAnimation(Entity entity)
	{
		if (TryGetReplayAnimation(entity, out string replayAnim)) return replayAnim;
		if (GameContext.World.Has<Dead>(entity)) return "Death";
		if (IsEntityMoving(entity)) return "Walk";
		if (GameContext.World.Has<AttackTarget>(entity)) return "Attack";
		if (GameContext.World.Has<HealingTarget>(entity)) return "Spell_Cast";
		if (IsEntityGathering(entity)) return "Labor";
		if (GameContext.World.Has<BuildTask>(entity)) return DetermineBuildTaskAnimation(entity, GameContext.World.Get<BuildTask>(entity));

		return "Idle";
	}

	private bool TryGetReplayAnimation(Entity entity, out string animation)
	{
		animation = string.Empty;
		if (!ReplayPlaybackManager.Instance.IsPlayingReplay) return false;

		animation = GameContext.World.Has<ReplayAnimationState>(entity)
			? GameContext.World.Get<ReplayAnimationState>(entity).Animation
			: "Idle";
		return true;
	}

	private bool IsEntityMoving(Entity entity)
	{
		return GameContext.World.Has<MoveTo>(entity) && GameContext.World.Has<Velocity>(entity) &&
		       GameContext.World.Get<Velocity>(entity).Value.LengthSquared() > 0.01f;
	}

	private bool IsEntityGathering(Entity entity)
	{
		return GameContext.World.Has<Gatherer>(entity) && !GameContext.World.Get<Gatherer>(entity).ReturningToBase;
	}

	private string DetermineBuildTaskAnimation(Entity entity, BuildTask task)
	{
		var target = task.BuildingEntity;
		if (!GameContext.World.IsAlive(target) || !GameContext.World.Has<Position>(target))
			return "Labor";

		var tPos = GameContext.World.Get<Position>(target).Value;
		var wPos = GameContext.World.Has<Position>(entity) ? GameContext.World.Get<Position>(entity).Value : Vector3.Zero;
		if (Vector3.Distance(wPos, tPos) < 4.0f)
		{
			return "Labor";
		}

		return "Walk";
	}

	internal bool ExecuteQueuedCommand(Entity entity, string? commandType, Vector3 targetPos, Entity targetEntity)
	{
		return commandType switch
		{
			"move" => ExecuteMoveCommand(entity, targetPos),
			"attack" => ExecuteAttackCommand(entity, targetEntity),
			"attackmove" => ExecuteAttackMoveCommand(entity, targetPos),
			"follow" => ExecuteFollowCommand(entity, targetEntity),
			"patrol" => ExecutePatrolCommand(entity, targetPos),
			"gather" => ExecuteGatherCommand(entity, targetPos, targetEntity),
			_ => ExecuteFallbackCommand(entity, commandType, targetPos, targetEntity)
		};
	}

	private bool ExecuteMoveCommand(Entity entity, Vector3 targetPos)
	{
		return GameContext.SimulationService.ExecuteMoveCommandDomain(entity, targetPos);
	}

	private bool ExecuteAttackCommand(Entity entity, Entity targetEntity)
	{
		return GameContext.SimulationService.ExecuteAttackCommandDomain(entity, targetEntity);
	}

	private bool ExecuteAttackMoveCommand(Entity entity, Vector3 targetPos)
	{
		return GameContext.SimulationService.ExecuteAttackMoveCommandDomain(entity, targetPos);
	}

	private bool ExecuteFollowCommand(Entity entity, Entity targetEntity)
	{
		return GameContext.SimulationService.ExecuteFollowCommandDomain(entity, targetEntity);
	}

	private bool ExecutePatrolCommand(Entity entity, Vector3 targetPos)
	{
		return GameContext.SimulationService.ExecutePatrolCommandDomain(entity, targetPos);
	}

	private bool ExecuteGatherCommand(Entity entity, Vector3 targetPos, Entity targetEntity)
	{
		return GameContext.SimulationService.ExecuteGatherCommandDomain(entity, targetPos, targetEntity);
	}

	private bool ExecuteFallbackCommand(Entity entity, string? commandType, Vector3 targetPos, Entity targetEntity)
	{
		return GameContext.SimulationService.ExecuteFallbackCommandDomain(entity, commandType, targetPos, targetEntity, AssignBuildTaskToWorker);
	}
}