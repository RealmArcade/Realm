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
using UnitId = Realm.Ecs.UnitId;
using Vector3 = System.Numerics.Vector3;

namespace Realm.Client.Core;

public partial class GameHost
{
	private void TickConstructionSystem(float fDelta, bool isAuthoritative)
	{
		_pendingBuildTaskUpdates.Clear();
		GameContext.SimulationService.CompletedBuildings.Clear();
		GameContext.SimulationService.PendingQueuedCommands.Clear();

		if (isAuthoritative)
		{
			var workerQuery = QueryCache.AllPositionAndBuildTaskNoneDeadQuery;
			World.Query(in workerQuery, (Entity workerEntity, ref Position workerPos, ref BuildTask buildTask) => { ProcessWorkerConstruction(workerEntity, ref workerPos, ref buildTask, fDelta); });

			ProcessPendingBuildTasks();
			ProcessCompletedBuildings();

			if (_pendingBuildTaskUpdates.Count > 0)
			{
				GameContext.InGameHud?.RefreshUI(SelectedUnits);
			}

			var queueQuery = QueryCache.AllBuildQueueNoneDeadQuery;
			World.Query(in queueQuery, (Entity entity) => { GameContext.SimulationService.ProcessBuildQueueEntity(entity); });

			GameContext.SimulationService.ProcessPendingQueuedCommands(ExecuteQueuedCommand);
		}

		UpdateBuildQueueGhosts();
	}

	private void ProcessWorkerConstruction(Entity workerEntity, ref Position workerPos, ref BuildTask buildTask, float fDelta)
	{
		if (!World.IsAlive(buildTask.BuildingEntity)) return;

		var buildingPos = World.Has<Position>(buildTask.BuildingEntity)
			? World.Get<Position>(buildTask.BuildingEntity).Value
			: workerPos.Value;

		float constructionRange = UnitMetadata.DefaultConstructionRange;
		if (World.Has<DefinitionId>(workerEntity))
		{
			string workerType = World.Get<DefinitionId>(workerEntity).Value;
			if (!UnitRegistry.TryGetValue(workerType, out var workerMeta))
				BuildingRegistry.TryGetValue(workerType, out workerMeta);
			if (workerMeta != null)
				constructionRange = workerMeta.ConstructionRange;
		}

		float buildingCollisionRadius = World.Has<CollisionRadius>(buildTask.BuildingEntity)
			? World.Get<CollisionRadius>(buildTask.BuildingEntity).Value
			: 0f;
		float effectiveRange = constructionRange + buildingCollisionRadius;
		float distSq = Vector3.DistanceSquared(workerPos.Value, buildingPos);
		bool inRange = distSq <= effectiveRange * effectiveRange;

		if (!inRange)
		{
			if (!World.Has<MoveTo>(workerEntity))
			{
				World.Add(workerEntity, new MoveTo(buildingPos));
			}

			return;
		}

		if (World.Has<MoveTo>(workerEntity))
		{
			World.Remove<MoveTo>(workerEntity);
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
		if (!World.Has<DefinitionId>(buildingEntity)) return;

		string bType = World.Get<DefinitionId>(buildingEntity).Value;
		bool isBuilding = false;
		if (!UnitRegistry.TryGetValue(bType, out var m) && !(isBuilding = BuildingRegistry.TryGetValue(bType, out m))) return;
		if (TryGetUnit3D(buildingEntity, out _)) return;

		if (m == null) return;
		string? targetModel = m.ModelPath;
		if (string.IsNullOrEmpty(targetModel)) return;

		string modelPath = GetFallbackModelPath(targetModel, isBuilding);
		int ownerPlayer = World.Has<UnitOwnerPlayer>(buildingEntity)
			? World.Get<UnitOwnerPlayer>(buildingEntity).PlayerIndex
			: 0;
		SpawnUnit3D(buildingEntity, bType, modelPath, new Godot.Vector3(buildingPos.X, buildingPos.Y, buildingPos.Z), isBuilding, ownerPlayer, executeSpawnShader: true);

		if (TryGetUnit3D(buildingEntity, out var bNode) && GodotObject.IsInstanceValid(bNode))
		{
			bNode.Modulate = new Color(1f, 1f, 1f, 0.4f);
		}
	}

	private void UpdateBuildingConstructionState(Entity buildingEntity, float progressGain)
	{
		if (!World.Has<ConstructionState>(buildingEntity)) return;

		ref var constructionState = ref World.Get<ConstructionState>(buildingEntity);
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
			if (!World.IsAlive(workerEntity)) continue;

			World.Set(workerEntity, updatedTask);

			if (updatedTask.Progress >= updatedTask.TotalBuildTime && World.Has<BuildTask>(workerEntity))
			{
				World.Remove<BuildTask>(workerEntity);
				TryStartNextQueuedCommand(workerEntity);
			}
		}
	}

	private void TryStartNextQueuedCommand(Entity workerEntity)
	{
		if (!World.Has<BuildQueue>(workerEntity)) return;

		ref var buildQueue = ref World.Get<BuildQueue>(workerEntity);
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
			World.Remove<BuildQueue>(workerEntity);
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
		if (World.IsAlive(buildingEntity))
		{
			OnConstructionFinished?.Invoke((UnitId)buildingEntity.Id);
		}

		GameContext.InGameHud?.ShowFeedbackText("Construction complete!", new Color(0.3f, 0.9f, 0.4f));
		GameContext.InGameHud?.RefreshUI(SelectedUnits);
	}

	private readonly HashSet<int> _activeBuildGhostWorkerIds = new();
	private readonly List<int> _inactiveBuildGhostWorkersToRemove = new();

	private void UpdateBuildQueueGhosts()
	{
		var workerQuery = QueryCache.AllPositionAndBuildTaskNoneDeadQuery;
		_activeBuildGhostWorkerIds.Clear();

		foreach (var workerEntity in World.Query(in workerQuery))
		{
			ProcessWorkerGhostQueue(workerEntity, _activeBuildGhostWorkerIds);
		}

		CleanupInactiveGhosts(_activeBuildGhostWorkerIds);
	}

	private void ProcessWorkerGhostQueue(Entity workerEntity, HashSet<int> activeWorkerIds)
	{
		if (!World.IsAlive(workerEntity)) return;
		activeWorkerIds.Add(workerEntity.Id);

		int queueCount = World.Has<BuildQueue>(workerEntity) ? World.Get<BuildQueue>(workerEntity).Count : 0;

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

		ref var queue = ref World.Get<BuildQueue>(workerEntity);
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
		float bRadius = ResolveTemplateBodyRadius(buildType);
		float bWidth = bRadius * 2f;
		float bHeight = Math.Max(4f, bRadius);
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

	private void ClearAllBuildQueueGhosts()
	{
		foreach (var kv in _buildQueueGhosts)
		foreach (var ghost in kv.Value)
			if (GodotObject.IsInstanceValid(ghost))
				ghost.QueueFree();
		_buildQueueGhosts.Clear();
	}

	private void AssignBuildTaskToWorker(Entity workerEntity, string buildType, Vector3 targetPos)
	{
		if (!UnitRegistry.TryGetValue(buildType, out var meta) && !BuildingRegistry.TryGetValue(buildType, out meta)) return;
		if (meta == null) return;
		string? targetModel = meta.ModelPath;
		if (string.IsNullOrEmpty(targetModel)) return;
		float buildTime = Math.Max(meta.ProductionTime, 0);

		if (!World.Has<UnitOwnerPlayer>(workerEntity)) return;
		int workerPlayerIndex = World.Get<UnitOwnerPlayer>(workerEntity).PlayerIndex;

		var playerOwner = GetPlayerEntityForPlayerIndex(workerPlayerIndex).AsPlayerEntity(World);

		var bldEntity = CreateEcsUnit(buildType, meta.Name, meta.MaxHp, meta.Damage, meta.Range, meta.Armor, meta.Speed,
			new Godot.Vector3(targetPos.X, targetPos.Y, targetPos.Z), playerOwner);
		World.SetOrAdd(bldEntity, new UnitOwnerPlayer(workerPlayerIndex));

		World.Add(bldEntity, new ConstructionState(buildTime));
		World.Add(bldEntity, new UnderConstruction());
		World.Add(bldEntity, new Invulnerable());

		if (!World.Has<CollisionRadius>(bldEntity))
		{
			World.Add(bldEntity, new CollisionRadius(ResolveTemplateCollisionRadius(buildType)));
		}

		EnsureBuildingModel(bldEntity, targetPos);

		var buildTask = new BuildTask(bldEntity, buildTime);
		World.SetOrAdd(workerEntity, buildTask);

		var buildingPos = new Vector3(targetPos.X, targetPos.Y, targetPos.Z);
		if (!World.Has<MoveTo>(workerEntity))
			World.Add(workerEntity, new MoveTo(buildingPos));
		else
			World.Set(workerEntity, new MoveTo(buildingPos));
	}

}
