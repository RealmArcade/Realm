using System;
using System.Collections.Generic;
using Realm.Ecs;
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
using UnitId = Realm.MapAPI.UnitId;
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
		UnitId victimId = GameContext.World.IsAlive(unit.Entity) ? (UnitId)unit.Entity.Id : UnitId.Invalid;
		Entity killerEntity = GameContext.SimulationService.GetKillerEntityDomain(unit.Entity);
		UnitId killerId = (killerEntity != Entity.Null && GameContext.World.IsAlive(killerEntity)) ? (UnitId)killerEntity.Id : UnitId.Invalid;

		if (victimId.IsValid)
		{
			OnUnitDied?.Invoke(victimId, killerId);
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
		_activeImpulseUnits.Remove(unit);
		AllUnits.Remove(unit);
		EntityToUnit3D.Remove(unit.Entity);
		EntityIdToUnit3D.Remove(unit.Entity.Id);
		if (PlayerToUnits.TryGetValue(unit.Player, out var pList))
		{
			pList.Remove(unit);
		}

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
			int localSlot = GameContext.PerspectiveFilter.ControlledPlayerIndex;
			beneficiaryPlayerEntity = GetPlayerEntityForPlayerIndex(localSlot);
		}

		if (beneficiaryPlayerEntity != Entity.Null)
		{
			bool shouldRefresh = GameContext.SimulationService.HandleEnemyDeathEconomyDomain(unit.Entity, unit.UnitId, beneficiaryPlayerEntity, (int)resCap);
			
			int localViewingSlot = GameContext.PerspectiveFilter.ControlledPlayerIndex;
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
		tween.TweenProperty(unit, "scale", new Godot.Vector3(0.001f, 0.001f, 0.001f), 1.0f);
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
		if (GameContext.World.IsAlive(buildingEntity))
		{
			OnConstructionFinished?.Invoke((UnitId)buildingEntity.Id);
		}

		InGameHUD.Instance?.ShowFeedbackText("Construction complete!", new Color(0.3f, 0.9f, 0.4f));
		InGameHUD.Instance?.RefreshUI(SelectedUnits);
	}

	private readonly HashSet<int> _activeBuildGhostWorkerIds = new();
	private readonly List<int> _inactiveBuildGhostWorkersToRemove = new();

	private void UpdateBuildQueueGhosts()
	{
		var workerQuery = QueryCache.AllPositionAndBuildTaskNoneDeadQuery;
		_activeBuildGhostWorkerIds.Clear();

		foreach (var workerEntity in GameContext.World.Query(in workerQuery))
		{
			ProcessWorkerGhostQueue(workerEntity, _activeBuildGhostWorkerIds);
		}

		CleanupInactiveGhosts(_activeBuildGhostWorkerIds);
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
		if (_buildQueueGhosts.Count == 0) return;
		_inactiveBuildGhostWorkersToRemove.Clear();
		foreach (var kv in _buildQueueGhosts)
		{
			if (!activeWorkerIds.Contains(kv.Key))
			{
				foreach (var ghost in kv.Value)
					if (GodotObject.IsInstanceValid(ghost))
						ghost.QueueFree();
				_inactiveBuildGhostWorkersToRemove.Add(kv.Key);
			}
		}

		for (int i = 0; i < _inactiveBuildGhostWorkersToRemove.Count; i++)
			_buildQueueGhosts.Remove(_inactiveBuildGhostWorkersToRemove[i]);
	}

	private MeshInstance3D CreateGhostMesh(string buildType)
	{
		var mesh = new MeshInstance3D();
		mesh.Name = "Mesh_MeshInstance3D";
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

	private struct UnitRenderWorkItem
	{
		public Entity Entity;
		public Unit3D Unit;
		public Vector3 PosValue;
		public bool HasCachedTransform;
		public Godot.Vector3 CachedPosition;
		public Godot.Vector3 CachedScale;
		public float CachedBaseScale;
		public float CachedRotationY;
		public Basis CachedBasis;
		public Godot.Vector3 CachedUnitVelocity;
		public Aabb CachedLocalAabb;
		public bool HasEvaluatedFrustum;
		public bool IsCulled;
		public bool IsFlashingScale;

		public bool ShouldSetCulledState;
		public bool NewIsCulled;
		public bool NewHasEvaluatedFrustum;

		public bool ShouldUpdateRenderPos;
		public Vector3 NewRenderPos;

		public bool ShouldZeroEcsVelocity;
		public bool ShouldUpdateUnitVelocity;
		public Godot.Vector3 NewUnitVelocity;

		public bool ShouldUpdateEcsRotationY;
		public float NewEcsRotationY;

		public byte LaborScaleAction;
		public float LaborPulseValue;

		public bool ShouldSetTransform;
		public Transform3D FinalTransform;
		public Godot.Vector3 NextPos;
		public float TargetRotY;
		public Basis TargetBasis;
		public Godot.Vector3 FinalScaleVec;
		public float ClampedScale;
	}

	private UnitRenderWorkItem[] _renderWorkItems = new UnitRenderWorkItem[256];
	private int _renderWorkItemCount = 0;
	private float _animationSelectionAccumulator = 0f;
	private float _renderFDelta;
	private float _renderFraction;
	private bool _renderHasFrustum;
	private Plane[] _renderFrustumPlanes = Array.Empty<Plane>();
	private bool _renderCameraFrustumChanged;
	private float _renderCurrentGameTime;
	private bool _renderMovementPrediction;
	private RuntimeTerrain? _renderGroundTerrain;
	private Action<int>? _processRenderWorkItemDelegate;

	private readonly Plane[] _cachedCameraFrustumPlanes = new Plane[6];
	private readonly Plane[] _previousCameraFrustumPlanes = new Plane[6];
	private bool _hadFrustumLastFrame = false;

	private void UpdateRenderFrameTransforms(float fDelta)
	{
		var camera = MainCamera ?? GetViewport()?.GetCamera3D();
		bool hasFrustum = false;
		if (camera != null && GodotObject.IsInstanceValid(camera))
		{
			var frustum = camera.GetFrustum();
			if (frustum != null && frustum.Count >= 6)
			{
				for (int i = 0; i < 6; i++)
				{
					_cachedCameraFrustumPlanes[i] = frustum[i];
				}
				hasFrustum = true;
			}
		}

		bool cameraFrustumChanged = false;
		if (hasFrustum != _hadFrustumLastFrame)
		{
			cameraFrustumChanged = true;
			_hadFrustumLastFrame = hasFrustum;
		}

		if (hasFrustum)
		{
			for (int i = 0; i < 6; i++)
			{
				if (!cameraFrustumChanged && (_cachedCameraFrustumPlanes[i].Normal != _previousCameraFrustumPlanes[i].Normal || Mathf.Abs(_cachedCameraFrustumPlanes[i].D - _previousCameraFrustumPlanes[i].D) > 0.001f))
				{
					cameraFrustumChanged = true;
				}
				_previousCameraFrustumPlanes[i] = _cachedCameraFrustumPlanes[i];
			}
		}

		_animationSelectionAccumulator += fDelta;
		bool shouldDriveUnitAnimations = !GameContext.SessionState.IsMapEditorMode && _animationSelectionAccumulator >= GameplayConstants.UnitAnimationStepIntervalSeconds;
		if (shouldDriveUnitAnimations)
		{
			_animationSelectionAccumulator = 0f;
		}

		_renderFDelta = fDelta;
		_renderFraction = (float)Engine.GetPhysicsInterpolationFraction();
		_renderHasFrustum = hasFrustum;
		_renderFrustumPlanes = hasFrustum ? _cachedCameraFrustumPlanes : Array.Empty<Plane>();
		_renderCameraFrustumChanged = cameraFrustumChanged;
		_renderCurrentGameTime = GameElapsedTime;
		_renderMovementPrediction = GameSettings.MovementPrediction;
		_renderGroundTerrain = (GroundTerrain != null && GodotObject.IsInstanceValid(GroundTerrain)) ? GroundTerrain : null;

		_renderWorkItemCount = 0;
		var query = QueryCache.AllPositionAndDefinitionIdQuery;
		GameContext.World.Query(in query, (Entity entity, ref Position pos) =>
		{
			if (!TryGetUnit3D(entity, out var unit3D) || !GodotObject.IsInstanceValid(unit3D))
				return;

			var posValue = pos.Value;
			if (CheckNonFinitePositionWarning(entity, posValue, unit3D))
				return;

			if (_renderWorkItemCount >= _renderWorkItems.Length)
			{
				Array.Resize(ref _renderWorkItems, _renderWorkItems.Length * 2);
			}

			bool hasCached = unit3D.HasCachedTransform;
			_renderWorkItems[_renderWorkItemCount++] = new UnitRenderWorkItem
			{
				Entity = entity,
				Unit = unit3D,
				PosValue = posValue,
				HasCachedTransform = hasCached,
				CachedPosition = hasCached ? unit3D.CachedPosition : unit3D.GlobalPosition,
				CachedScale = hasCached ? unit3D.CachedScale : unit3D.Scale,
				CachedBaseScale = unit3D.CachedBaseScale,
				CachedRotationY = hasCached ? unit3D.CachedRotationY : unit3D.Rotation.Y,
				CachedBasis = hasCached ? unit3D.CachedBasis : unit3D.Basis,
				CachedUnitVelocity = unit3D.Velocity,
				CachedLocalAabb = unit3D.CachedLocalAabb,
				HasEvaluatedFrustum = unit3D.HasEvaluatedFrustum,
				IsCulled = unit3D.IsCulled,
				IsFlashingScale = unit3D.IsFlashingScale
			};
		});

		if (_renderWorkItemCount == 0)
			return;

		_processRenderWorkItemDelegate ??= ProcessRenderWorkItemParallel;
		ParallelRunner.Execute(_renderWorkItemCount, _processRenderWorkItemDelegate);

		var world = GameContext.World;
		float currentGameTime = _renderCurrentGameTime;
		for (int i = 0; i < _renderWorkItemCount; i++)
		{
			ref var item = ref _renderWorkItems[i];
			var unit3D = item.Unit;

			if (item.ShouldUpdateRenderPos && world.Has<RenderPosition>(item.Entity))
			{
				world.Set(item.Entity, new RenderPosition(item.NewRenderPos));
			}

			if (item.ShouldZeroEcsVelocity && world.Has<Velocity>(item.Entity))
			{
				world.Set(item.Entity, new Velocity(Vector3.Zero));
			}

			if (item.ShouldUpdateEcsRotationY && world.Has<RotationY>(item.Entity))
			{
				world.Set(item.Entity, new RotationY(item.NewEcsRotationY));
			}

			if (item.NewHasEvaluatedFrustum)
			{
				unit3D.HasEvaluatedFrustum = true;
			}

			if (item.ShouldSetCulledState)
			{
				unit3D.SetCulledState(item.NewIsCulled, currentGameTime);
			}

			if (item.ShouldUpdateUnitVelocity)
			{
				unit3D.Velocity = item.NewUnitVelocity;
			}

			if (item.LaborScaleAction == 1)
			{
				unit3D.SetLaborPulseScale(item.LaborPulseValue);
			}
			else if (item.LaborScaleAction == 2)
			{
				unit3D.ResetLaborPulseScale();
			}

			if (item.ShouldSetTransform)
			{
				unit3D.SetTransform3D(in item.FinalTransform, in item.NextPos, item.TargetRotY, in item.TargetBasis, in item.FinalScaleVec, item.ClampedScale);
			}

			if (shouldDriveUnitAnimations && !unit3D.IsCulled && unit3D.Visible)
			{
				unit3D.PlayAnimation(DetermineUnitAnimation(item.Entity));
			}

			unit3D.StepManualAnimation(fDelta);
			item.Unit = null!;
		}
	}

	private void ProcessRenderWorkItemParallel(int index)
	{
		ref var item = ref _renderWorkItems[index];
		Entity entity = item.Entity;
		var world = GameContext.World;
		float fDelta = _renderFDelta;
		float fraction = _renderFraction;

		Vector3 posValue = item.PosValue;
		Vector3 prevPos = world.Has<PreviousPosition>(entity)
			? world.Get<PreviousPosition>(entity).Value
			: posValue;

		Vector3 interpPosVec = Vector3.Lerp(prevPos, posValue, fraction);

		Godot.Vector3 nextPos;
		if (_renderMovementPrediction && world.Has<RenderPosition>(entity))
		{
			var currentRenderPos = world.Get<RenderPosition>(entity).Value;
			var smoothedRenderPos = Vector3.Lerp(currentRenderPos, interpPosVec, Math.Min(1.0f, 15.0f * fDelta));
			item.ShouldUpdateRenderPos = true;
			item.NewRenderPos = smoothedRenderPos;
			nextPos = new Godot.Vector3(smoothedRenderPos.X, smoothedRenderPos.Y, smoothedRenderPos.Z);
		}
		else
		{
			nextPos = new Godot.Vector3(interpPosVec.X, interpPosVec.Y, interpPosVec.Z);
			if (world.Has<RenderPosition>(entity))
			{
				item.ShouldUpdateRenderPos = true;
				item.NewRenderPos = interpPosVec;
			}
		}

		bool posChanged = !item.HasCachedTransform || (nextPos - item.CachedPosition).LengthSquared() > 0.0001f;

		float scaleVal = world.Has<CollisionScale>(entity) ? world.Get<CollisionScale>(entity).Value : 1.0f;
		float clampedScale = Mathf.Max(0.01f, scaleVal);
		bool scaleChanged = Mathf.Abs(item.CachedBaseScale - clampedScale) > 0.001f;

		bool isCulled = item.IsCulled;
		if (!_renderHasFrustum)
		{
			item.ShouldSetCulledState = isCulled;
			item.NewIsCulled = false;
			isCulled = false;
		}
		else if (!item.HasEvaluatedFrustum || _renderCameraFrustumChanged || posChanged || scaleChanged)
		{
			item.NewHasEvaluatedFrustum = true;
			Godot.Vector3 cullPos = item.HasCachedTransform ? item.CachedPosition : nextPos;
			Godot.Vector3 cullScale = item.HasCachedTransform ? item.CachedScale : new Godot.Vector3(clampedScale, clampedScale, clampedScale);
			Aabb localAabb = item.CachedLocalAabb;
			Aabb worldAabb = new Aabb(cullPos + localAabb.Position * cullScale, localAabb.Size * cullScale);
			bool isVisible = RuntimeTerrain.IntersectsFrustum(_renderFrustumPlanes, worldAabb);
			bool shouldCull = !isVisible;
			if (shouldCull != isCulled)
			{
				item.ShouldSetCulledState = true;
				item.NewIsCulled = shouldCull;
			}
			isCulled = shouldCull;
		}

		bool hasLookTarget = TryGetLookTarget(entity, out Godot.Vector3 lookTargetPos);
		bool hasMovement = world.Has<MoveTo>(entity) || world.Has<Follow>(entity) || world.Has<InterpolationTarget>(entity);
		bool hasVelocityComp = world.Has<Velocity>(entity);
		Vector3 ecsVel = hasVelocityComp ? world.Get<Velocity>(entity).Value : Vector3.Zero;
		bool hasVelocity = hasVelocityComp && ecsVel.LengthSquared() > 0.0001f;
		bool isLaborAnimating = (world.Has<Gatherer>(entity) && !world.Get<Gatherer>(entity).ReturningToBase)
		                        || (world.Has<BuildTask>(entity) && !world.Has<MoveTo>(entity));

		if (!posChanged && !hasMovement && !hasVelocity && !hasLookTarget && !isLaborAnimating)
		{
			return;
		}

		if (isCulled)
		{
			if (posChanged || scaleChanged)
			{
				Godot.Vector3 scaleVec = new Godot.Vector3(clampedScale, clampedScale, clampedScale);
				Basis scaledBasis = item.CachedBasis.Scaled(scaleVec);
				item.ShouldSetTransform = true;
				item.FinalTransform = new Transform3D(scaledBasis, nextPos);
				item.NextPos = nextPos;
				item.TargetRotY = item.CachedRotationY;
				item.TargetBasis = item.CachedBasis;
				item.FinalScaleVec = scaleVec;
				item.ClampedScale = clampedScale;
			}

			return;
		}

		Godot.Vector3 velVec = new Godot.Vector3(ecsVel.X, ecsVel.Y, ecsVel.Z);
		if (!hasMovement)
		{
			if (hasVelocity)
			{
				item.ShouldZeroEcsVelocity = true;
			}
			velVec = Godot.Vector3.Zero;
		}

		Godot.Vector3 effectiveUnitVelocity = item.CachedUnitVelocity;
		if ((effectiveUnitVelocity - velVec).LengthSquared() > 0.0001f)
		{
			item.ShouldUpdateUnitVelocity = true;
			item.NewUnitVelocity = velVec;
			effectiveUnitVelocity = velVec;
		}

		Basis targetBasis = ComputeVisualRotationBasisParallel(ref item, entity, effectiveUnitVelocity, nextPos, lookTargetPos, hasLookTarget, fDelta, fraction, out float targetRotY);

		if (!item.IsFlashingScale)
		{
			if (isLaborAnimating)
			{
				item.LaborScaleAction = 1;
				item.LaborPulseValue = 1.0f + Mathf.Sin(_renderCurrentGameTime * 10f) * 0.1f;
			}
			else
			{
				item.LaborScaleAction = 2;
			}
		}

		bool basisChanged = Mathf.Abs(Mathf.AngleDifference(item.CachedRotationY, targetRotY)) > 0.0005f || !targetBasis.IsEqualApprox(item.CachedBasis);
		if (posChanged || scaleChanged || basisChanged)
		{
			Godot.Vector3 finalScaleVec = new Godot.Vector3(clampedScale, clampedScale, clampedScale);
			Basis finalScaledBasis = targetBasis.Scaled(finalScaleVec);
			item.ShouldSetTransform = true;
			item.FinalTransform = new Transform3D(finalScaledBasis, nextPos);
			item.NextPos = nextPos;
			item.TargetRotY = targetRotY;
			item.TargetBasis = targetBasis;
			item.FinalScaleVec = finalScaleVec;
			item.ClampedScale = clampedScale;
		}
	}

	private static readonly QueryDescription _constructionVisualQuery = new QueryDescription().WithAll<ConstructionState>().WithNone<Dead>();

	private void UpdateVisualNodesFromEcs(float fDelta)
	{
		foreach (var entity in GameContext.World.Query(in _constructionVisualQuery))
		{
			if (TryGetUnit3D(entity, out var buildingNode) && GodotObject.IsInstanceValid(buildingNode))
			{
				ref var construction = ref GameContext.World.Get<ConstructionState>(entity);
				float alpha = Mathf.Lerp(0.3f, 1.0f, construction.Progress / Mathf.Max(construction.TotalBuildTime, 0.001f));
				buildingNode.Modulate = new Color(1f, 1f, 1f, alpha);
			}
		}
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

	private Basis ComputeVisualRotationBasisParallel(ref UnitRenderWorkItem item, Entity entity, Godot.Vector3 unitVelocity, Godot.Vector3 nextPos, Godot.Vector3 lookTargetPos, bool hasLookTarget, float fDelta, float fraction, out float targetRotY)
	{
		var world = GameContext.World;
		Godot.Vector3 dir = unitVelocity;
		bool hasDir = dir.LengthSquared() > 0.01f;

		bool forceLookTarget = false;
		if (hasLookTarget)
		{
			if (!hasDir) forceLookTarget = true;
			else
			{
				float distToLook = lookTargetPos.DistanceTo(nextPos);
				if (distToLook < SimulationService.LookTargetProximityDistance || world.Has<Follow>(entity)) forceLookTarget = true;
			}
		}

		if (forceLookTarget)
		{
			dir = (lookTargetPos - nextPos);
			dir.Y = 0f;
			dir = dir.Normalized();
			hasDir = dir.LengthSquared() > 0.01f;
		}

		float currRot = world.Has<RotationY>(entity) ? world.Get<RotationY>(entity).Value : item.CachedRotationY;
		float prevRot = world.Has<PreviousRotationY>(entity) ? world.Get<PreviousRotationY>(entity).Value : currRot;
		float interpRotY = Mathf.LerpAngle(prevRot, currRot, fraction);

		if (hasDir)
		{
			return ComputeDirectionalRotationBasisParallel(ref item, entity, nextPos, dir, fDelta, out targetRotY);
		}
		else if (world.Has<InterpolationTarget>(entity))
		{
			var interp = world.Get<InterpolationTarget>(entity);
			targetRotY = Mathf.LerpAngle(item.CachedRotationY, interp.RotationY, 10f * fDelta);

			if (world.Has<RotationY>(entity))
			{
				float ecsRot = world.Get<RotationY>(entity).Value;
				if (Mathf.Abs(ecsRot - targetRotY) > 0.001f)
				{
					item.ShouldUpdateEcsRotationY = true;
					item.NewEcsRotationY = targetRotY;
				}
			}

			return Basis.FromEuler(new Godot.Vector3(0f, targetRotY, 0f));
		}
		else
		{
			targetRotY = interpRotY;
			return Basis.FromEuler(new Godot.Vector3(0f, targetRotY, 0f));
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

	private Basis ComputeDirectionalRotationBasisParallel(ref UnitRenderWorkItem item, Entity entity, Godot.Vector3 nextPos, Godot.Vector3 dir, float fDelta, out float targetRotY)
	{
		var world = GameContext.World;
		dir = dir.Normalized();
		float angle = Mathf.Atan2(-dir.X, -dir.Z) + Mathf.Pi;

		bool isFlying = world.Has<Realm.Ecs.Components.Movement.PathingFlags>(entity)
		                && ((TerrainPathingFlags)world.Get<Realm.Ecs.Components.Movement.PathingFlags>(entity).Value & TerrainPathingFlags.Flying) != 0;
		float turnRate = 10f;
		if (world.Has<MovementStats>(entity))
		{
			var moveStats = world.Get<MovementStats>(entity);
			if (moveStats.TurnRate > 0f) turnRate = moveStats.TurnRate;
		}

		targetRotY = Mathf.LerpAngle(item.CachedRotationY, angle, turnRate * fDelta);

		if (world.Has<RotationY>(entity))
		{
			float ecsRot = world.Get<RotationY>(entity).Value;
			if (Mathf.Abs(ecsRot - targetRotY) > 0.001f)
			{
				item.ShouldUpdateEcsRotationY = true;
				item.NewEcsRotationY = targetRotY;
			}
		}

		Basis currentBasis = Basis.FromEuler(new Godot.Vector3(0f, targetRotY, 0f));

		Godot.Vector3 normal = Godot.Vector3.Up;
		var terrain = _renderGroundTerrain;
		if (!isFlying && terrain != null)
		{
			terrain.GetHeightAndNormal(nextPos.X, nextPos.Z, out _, out normal);
		}

		if (Mathf.Abs(normal.Y - 1f) > 0.02f)
		{
			Godot.Vector3 forwardDir = new Godot.Vector3(-Mathf.Sin(targetRotY), 0f, -Mathf.Cos(targetRotY));
			Godot.Vector3 up = normal.Normalized();
			Godot.Vector3 right = forwardDir.Cross(up);
			if (right.LengthSquared() > 0.00001f)
			{
				right = right.Normalized();
				Godot.Vector3 forwardPerp = right.Cross(up).Normalized();
				Basis targetTerrainBasis = new Basis(right, up, forwardPerp);
				float det = targetTerrainBasis.Determinant();
				if (!float.IsNaN(det) && Mathf.Abs(det) > 0.001f)
				{
					var qTarget = targetTerrainBasis.GetRotationQuaternion();
					var qCurrent = item.CachedBasis.GetRotationQuaternion();
					var qLerp = qCurrent.Slerp(qTarget, 10f * fDelta);
					return new Basis(qLerp);
				}
			}
		}

		return currentBasis;
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