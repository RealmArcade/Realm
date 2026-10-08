using Godot;
using Arch.Core;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Tags;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Resources;
using Realm.Ecs.Components.Terrain;
using Realm.Ecs.Components.Meta;
using Realm.MapAPI;
using System;
using System.Collections.Generic;
using static Realm.Ecs.Common.ResourceConstants;
using Realm.Godot.Utils;

public partial class GameHost
{
	private readonly Dictionary<int, Unit_WasmRuntime> _unitWrapperCache = new();
	private readonly HashSet<Entity> _warnedNonFinitePositions = new();

	// Upper bound for _warnedNonFinitePositions. Once it grows past this, stale warnings for
	// already-destroyed entities are swept so recycled entity ids get a fresh warning later.
	private const int WarnedNonFinitePositionsLimit = 512;

	public Unit_WasmRuntime GetUnitWrapper(Entity entity)
	{
		if (!EcsWorld.IsAlive(entity))
		{
			throw new ArgumentException("Entity is not alive", nameof(entity));
		}
		if (_unitWrapperCache.TryGetValue(entity.Id, out var wrapper))
		{
			return wrapper;
		}
		wrapper = new Unit_WasmRuntime(entity, EcsWorld);
		_unitWrapperCache[entity.Id] = wrapper;
		return wrapper;
	}

	private void KillUnit(Unit3D unit)
	{
		KillUnit(unit, true, true);
	}

	private void KillUnit(Unit3D unit, bool executeDespawnShader, bool playDeathAnimation)
	{
		IUnit killer = null;
		if (EcsWorld.IsAlive(unit.Entity))
		{
			if (EcsWorld.Has<LastAttacker>(unit.Entity))
			{
				var killerEntity = EcsWorld.Get<LastAttacker>(unit.Entity).Value;
				if (EcsWorld.IsAlive(killerEntity))
				{
					killer = GetUnitWrapper(killerEntity);
				}
			}
			OnUnitDied?.Invoke(GetUnitWrapper(unit.Entity), killer);

			int id = unit.Entity.Id;
			_unitWrapperCache.Remove(id);
		}

		_audioService?.PlayUnitSound(unit.UnitId, UnitSoundEvent.Death, unit.GlobalPosition);

		SelectedUnits.Remove(unit);
		AllUnits.Remove(unit);
		if (unit.UnitId == "castle")
		{
			_castlesList.Remove(unit);
		}
		if (unit.IsBuilding)
		{
			float radius = EcsWorld.Has<CollisionRadius>(unit.Entity) ? EcsWorld.Get<CollisionRadius>(unit.Entity).Value : 2.0f;
			var unitPos = EcsWorld.Has<Position>(unit.Entity) ? EcsWorld.Get<Position>(unit.Entity).Value : new System.Numerics.Vector3(unit.Position.X, unit.Position.Y, unit.Position.Z);
			UncarveObstacle(unitPos, radius);
		}

		if (unit.IsEnemy && UnitRegistry.TryGetValue(unit.UnitId, out var bountyMeta) && bountyMeta.GoldBounty > 0f)
		{
			if (EcsWorld.IsAlive(_playerEntity) && EcsWorld.Has<PlayerResources>(_playerEntity))
			{
				EcsWorld.Mutate<PlayerResources>(_playerEntity, (ref PlayerResources r) =>
				{
					if (r.Value.TryGetValue(_goldResourceId, out var currentGold))
						r.Value[_goldResourceId] = (int)Math.Min(ResourceCap, currentGold + bountyMeta.GoldBounty);
				});
				InGameHUD.Instance?.RefreshUI(SelectedUnits);
			}
		}

		if (!unit.IsEnemy && UnitRegistry.TryGetValue(unit.UnitId, out var killMeta))
		{
			if (unit.UnitId == "castle")
			{
				MaxPopulation = Math.Max(0, MaxPopulation - 20);
			}
			if (!EcsWorld.Has<BypassPopulationTag>(unit.Entity))
			{
				CurrentPopulation = Math.Max(0, CurrentPopulation - killMeta.PopCost);
			}
		}

		if (_multiplayerActive)
		{
			if (_clientToServerEntityMap.TryGetValue(unit.Entity.Id, out int serverId))
			{
				_serverToClientEntityMap.Remove(serverId);
			}
			_clientToServerEntityMap.Remove(unit.Entity.Id);
		}

		string unitId = unit.UnitId;
		bool isEnemy = unit.IsEnemy;

		if (EcsWorld.IsAlive(unit.Entity))
		{
			EcsWorld.Destroy(unit.Entity);
		}

		if (playDeathAnimation && GodotObject.IsInstanceValid(unit))
		{
			unit.PlayAnimation("Death");
		}

		if (GodotObject.IsInstanceValid(unit))
		{
			unit.CollisionLayer = 0;
			unit.CollisionMask = 0;
		}

		string deathShader = executeDespawnShader ? GetModelDeathShader(unitId) : "";
		if (executeDespawnShader && string.IsNullOrEmpty(deathShader))
		{
			deathShader = GetModelDeathShader(unit);
		}

		if (!string.IsNullOrEmpty(deathShader))
		{
			SpawnDeathShaderManager.AnimateTransition(unit, deathShader, false, null, () =>
			{
				if (GodotObject.IsInstanceValid(unit)) unit.QueueFree();
			});
		}
		else if (playDeathAnimation)
		{
			var tween = CreateTween();
			tween.SetParallel(true);
			tween.TweenProperty(unit, "position:y", -3.0f, 1.0f);
			tween.TweenProperty(unit, "scale", Vector3.Zero, 1.0f);
			tween.Chain().TweenCallback(Callable.From(unit.QueueFree));
		}
		else
		{
			if (GodotObject.IsInstanceValid(unit)) unit.QueueFree();
		}

		if (unitId == "castle")
		{
			if (isEnemy)
			{
				GD.Print("[GameHost] Enemy Castle destroyed! Player wins!");
				IsGameOver = true;
				Callable.From(() => UIManager.Instance?.TransitionTo(GameScreen.GameOver, true)).CallDeferred();
			}
			else
			{
				GD.Print("[GameHost] Player Castle destroyed! Player loses!");
				IsGameOver = true;
				Callable.From(() => UIManager.Instance?.TransitionTo(GameScreen.GameOver, false)).CallDeferred();
			}
		}

		GD.Print($"Unit {unit.Name} died.");
	}

	private void DepleteProp(Prop3D prop)
	{
		if (GodotObject.IsInstanceValid(prop))
		{
			string propId = prop.PropId;
			float radius = EcsWorld.Has<CollisionRadius>(prop.Entity) ? EcsWorld.Get<CollisionRadius>(prop.Entity).Value : 1.0f;
			var propPos = EcsWorld.Has<Position>(prop.Entity) ? EcsWorld.Get<Position>(prop.Entity).Value : new System.Numerics.Vector3(prop.Position.X, prop.Position.Y, prop.Position.Z);
			AllProps.Remove(prop);
			EntityToProp3D.Remove(prop.Entity);
			if (EcsWorld.IsAlive(prop.Entity))
			{
				EcsWorld.Destroy(prop.Entity);
			}
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

	public bool FastBuildEnabled { get; set; } = false;

	private const float BaseConstructionWorkRatePerSecond = 1f / 20f;
	private float ConstructionWorkRatePerSecond => FastBuildEnabled ? BaseConstructionWorkRatePerSecond * 10f : BaseConstructionWorkRatePerSecond;

	// Units face their attack/heal/build target once they are within this distance,
	// even while still moving toward it.
	private const float LookTargetProximityDistance = 5.0f;

	private readonly List<(Entity Worker, BuildTask UpdatedTask)> _pendingBuildTaskUpdates = new();
	private readonly List<Entity> _completedBuildings = new();
	private readonly List<(Entity Entity, string? Type, System.Numerics.Vector3 Position, Entity Target)> _pendingQueuedCommands = new();

	private readonly Dictionary<int, List<MeshInstance3D>> _buildQueueGhosts = new();

	internal void TickConstructionSystem(float fDelta)
	{
		_pendingBuildTaskUpdates.Clear();
		_completedBuildings.Clear();

		var workerQuery = QueryCache.AllPositionAndBuildTaskNoneDeadQuery;
		EcsWorld.Query(in workerQuery, (Entity workerEntity, ref Position workerPos, ref BuildTask buildTask) =>
		{
			if (!EcsWorld.IsAlive(buildTask.BuildingEntity)) return;

			var buildingPos = EcsWorld.Has<Position>(buildTask.BuildingEntity)
				? EcsWorld.Get<Position>(buildTask.BuildingEntity).Value
				: workerPos.Value;

			float distSq = System.Numerics.Vector3.DistanceSquared(workerPos.Value, buildingPos);
			bool inRange = distSq <= 16f;

			if (!inRange)
			{
				if (!EcsWorld.Has<MoveTo>(workerEntity))
				{
					EcsWorld.Add(workerEntity, new MoveTo(buildingPos));
				}
				return;
			}

			if (EcsWorld.Has<MoveTo>(workerEntity))
			{
				EcsWorld.Remove<MoveTo>(workerEntity);
			}

			if (EcsWorld.Has<DefinitionId>(buildTask.BuildingEntity))
			{
				string bType = EcsWorld.Get<DefinitionId>(buildTask.BuildingEntity).Value;
				if ((UnitRegistry.TryGetValue(bType, out var m) || BuildingRegistry.TryGetValue(bType, out m)) && !TryGetUnit3D(buildTask.BuildingEntity, out _))
				{
					string targetModel = m.ModelPath;
					if (!string.IsNullOrEmpty(targetModel))
					{
						string modelPath = GetFallbackModelPath(targetModel, true);
						SpawnUnit3D(buildTask.BuildingEntity, bType, modelPath, new Godot.Vector3(buildingPos.X, buildingPos.Y, buildingPos.Z), true, false);

						if (TryGetUnit3D(buildTask.BuildingEntity, out var bNode) && GodotObject.IsInstanceValid(bNode))
						{
							bNode.Modulate = new Godot.Color(1f, 1f, 1f, 0.4f);
						}
					}
				}
			}

			float progressGain = ConstructionWorkRatePerSecond * fDelta;
			var updatedTask = new BuildTask(buildTask.BuildingEntity, buildTask.TotalBuildTime)
			{
				Progress = buildTask.Progress + progressGain
			};
			_pendingBuildTaskUpdates.Add((workerEntity, updatedTask));

			if (EcsWorld.Has<ConstructionState>(buildTask.BuildingEntity))
			{
				ref var constructionState = ref EcsWorld.Get<ConstructionState>(buildTask.BuildingEntity);
				constructionState.Progress = Mathf.Min(constructionState.Progress + progressGain, constructionState.TotalBuildTime);

				if (constructionState.Progress >= constructionState.TotalBuildTime && !_completedBuildings.Contains(buildTask.BuildingEntity))
				{
					_completedBuildings.Add(buildTask.BuildingEntity);
				}
			}
		});

		foreach (var (workerEntity, updatedTask) in _pendingBuildTaskUpdates)
		{
			if (EcsWorld.IsAlive(workerEntity))
			{
				EcsWorld.Set(workerEntity, updatedTask);

				if (updatedTask.Progress >= updatedTask.TotalBuildTime && EcsWorld.Has<BuildTask>(workerEntity))
				{
					EcsWorld.Remove<BuildTask>(workerEntity);

					if (EcsWorld.Has<BuildQueue>(workerEntity))
					{
						ref var buildQueue = ref EcsWorld.Get<BuildQueue>(workerEntity);
						bool startedNext = false;
						while (buildQueue.TryDequeue(out string? nextType, out var nextPos, out Arch.Core.Entity nextTarget))
						{
							if (ExecuteQueuedCommand(workerEntity, nextType, nextPos, nextTarget))
							{
								startedNext = true;
								break;
							}
						}
						if (!startedNext && buildQueue.Count == 0)
						{
							EcsWorld.Remove<BuildQueue>(workerEntity);
						}
					}
				}
			}
		}

		foreach (var buildingEntity in _completedBuildings)
		{
			if (!EcsWorld.IsAlive(buildingEntity)) continue;

			if (EcsWorld.Has<UnderConstruction>(buildingEntity))
			{
				EcsWorld.Remove<UnderConstruction>(buildingEntity);
			}

			if (EcsWorld.Has<ConstructionState>(buildingEntity))
			{
				EcsWorld.Remove<ConstructionState>(buildingEntity);
			}

			if (TryGetUnit3D(buildingEntity, out var buildingNode) && GodotObject.IsInstanceValid(buildingNode))
			{
				buildingNode.Modulate = new Godot.Color(1f, 1f, 1f, 1f);
				SpawnDeathShaderManager.ClearShaderOverride(buildingNode);
			}

			var buildingUnit = (EcsWorld.IsAlive(buildingEntity)) ? GetUnitWrapper(buildingEntity) : null;
			if (buildingUnit != null) { OnConstructionFinished?.Invoke(buildingUnit); }
			InGameHUD.Instance?.ShowFeedbackText("Construction complete!", new Godot.Color(0.3f, 0.9f, 0.4f));
			InGameHUD.Instance?.RefreshUI(SelectedUnits);
		}

		if (_pendingBuildTaskUpdates.Count > 0)
		{
			InGameHUD.Instance?.RefreshUI(SelectedUnits);
		}

		_pendingQueuedCommands.Clear();
		var queueQuery = QueryCache.AllBuildQueueNoneDeadQuery;
		EcsWorld.Query(in queueQuery, (Entity entity) =>
		{
			bool hasMoveTo = EcsWorld.Has<MoveTo>(entity);
			bool hasBuildTask = EcsWorld.Has<BuildTask>(entity);
			bool hasAttackTarget = EcsWorld.Has<AttackTarget>(entity);
			bool hasAttackMove = EcsWorld.Has<Realm.Ecs.Components.Movement.AttackMove>(entity);
			bool hasFollow = EcsWorld.Has<Realm.Ecs.Components.Movement.Follow>(entity);
			bool hasPatrol = EcsWorld.Has<Realm.Ecs.Components.Movement.Patrol>(entity);
			bool hasGatherer = EcsWorld.Has<Gatherer>(entity);
			bool hasHealingTarget = EcsWorld.Has<HealingTarget>(entity);

			if (!hasMoveTo && !hasBuildTask && !hasAttackTarget && !hasAttackMove && !hasFollow && !hasPatrol && !hasGatherer && !hasHealingTarget)
			{
				ref var q = ref EcsWorld.Get<BuildQueue>(entity);
				if (q.Count > 0)
				{
					if (q.TryDequeue(out string nextType, out var nextPos, out Arch.Core.Entity nextTarget))
					{
						_pendingQueuedCommands.Add((entity, nextType, nextPos, nextTarget));
					}
				}
				else
				{
					_pendingQueuedCommands.Add((entity, "clear_queue_component", System.Numerics.Vector3.Zero, Entity.Null));
				}
			}
		});

		foreach (var cmd in _pendingQueuedCommands)
		{
			if (EcsWorld.IsAlive(cmd.Entity))
			{
				if (cmd.Type == "clear_queue_component")
				{
					if (EcsWorld.Has<BuildQueue>(cmd.Entity))
					{
						EcsWorld.Remove<BuildQueue>(cmd.Entity);
					}
				}
				else
				{
					bool success = ExecuteQueuedCommand(cmd.Entity, cmd.Type, cmd.Position, cmd.Target);
					while (!success && EcsWorld.Has<BuildQueue>(cmd.Entity))
					{
						ref var q = ref EcsWorld.Get<BuildQueue>(cmd.Entity);
						if (q.TryDequeue(out string? nextType, out var nextPos, out Arch.Core.Entity nextTarget))
						{
							success = ExecuteQueuedCommand(cmd.Entity, nextType, nextPos, nextTarget);
						}
						else
						{
							EcsWorld.Remove<BuildQueue>(cmd.Entity);
							break;
						}
					}
					if (!success && EcsWorld.Has<BuildQueue>(cmd.Entity) && EcsWorld.Get<BuildQueue>(cmd.Entity).Count == 0)
					{
						EcsWorld.Remove<BuildQueue>(cmd.Entity);
					}
				}
			}
		}

		UpdateBuildQueueGhosts();
	}

	private void UpdateBuildQueueGhosts()
	{
		var workerQuery = QueryCache.AllPositionAndBuildTaskNoneDeadQuery;

		var activeWorkerIds = new System.Collections.Generic.HashSet<int>();
		EcsWorld.Query(in workerQuery, (Entity workerEntity, ref BuildTask _) =>
		{
			if (!EcsWorld.IsAlive(workerEntity)) return;
			activeWorkerIds.Add(workerEntity.Id);

			int queueCount = EcsWorld.Has<BuildQueue>(workerEntity)
				? EcsWorld.Get<BuildQueue>(workerEntity).Count
				: 0;

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

			ref var queue = ref EcsWorld.Get<BuildQueue>(workerEntity);
			for (int slotIndex = 0; slotIndex < queueCount; slotIndex++)
			{
				queue.PeekAt(slotIndex, out string? buildType, out var queuedPos);
				if (buildType == null) continue;

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
		});

		var toRemove = new List<int>();
		foreach (var kv in _buildQueueGhosts)
		{
			if (!activeWorkerIds.Contains(kv.Key))
			{
				foreach (var ghost in kv.Value)
					if (GodotObject.IsInstanceValid(ghost)) ghost.QueueFree();
				toRemove.Add(kv.Key);
			}
		}
		foreach (var id in toRemove)
			_buildQueueGhosts.Remove(id);
	}

	private MeshInstance3D CreateGhostMesh(string buildType)
	{
		var mesh = new MeshInstance3D();
		var box = new BoxMesh();
		box.Size = buildType == "castle" ? new Godot.Vector3(10f, 6f, 10f) : new Godot.Vector3(3.2f, 4f, 3.2f);
		mesh.Mesh = box;

		var mat = new StandardMaterial3D();
		mat.AlbedoColor = new Godot.Color(1.0f, 0.65f, 0.1f, 0.35f);
		mat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
		mat.EmissionEnabled = true;
		mat.Emission = new Godot.Color(1.0f, 0.5f, 0.05f) * 0.25f;
		mat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
		mesh.MaterialOverride = mat;

		AddChild(mesh);
		return mesh;
	}

	internal void ClearAllBuildQueueGhosts()
	{
		foreach (var kv in _buildQueueGhosts)
			foreach (var ghost in kv.Value)
				if (GodotObject.IsInstanceValid(ghost)) ghost.QueueFree();
		_buildQueueGhosts.Clear();
	}

	internal void AssignBuildTaskToWorker(Entity workerEntity, string buildType, System.Numerics.Vector3 targetPos)
	{
		if (!UnitRegistry.TryGetValue(buildType, out var meta) && !BuildingRegistry.TryGetValue(buildType, out meta)) return;
		string targetModel = meta.ModelPath;
		if (string.IsNullOrEmpty(targetModel)) return;
		float buildTime = meta.ProductionTime > 0f ? meta.ProductionTime : 30f;

		var playerOwner = _playerEntity.AsPlayerEntity(EcsWorld);
		string modelPath = GetFallbackModelPath(targetModel, true);

		var bldEntity = CreateEcsUnit(buildType, meta.Name, meta.MaxHp, meta.Damage, meta.Range, meta.Armor, 0f,
			new Godot.Vector3(targetPos.X, targetPos.Y, targetPos.Z), playerOwner);

		EcsWorld.Add(bldEntity, new ConstructionState(buildTime));
		EcsWorld.Add(bldEntity, new UnderConstruction());

		float bldRadius = GetOrCalculateObstacleRadius(buildType, null, true);
		if (!EcsWorld.Has<Realm.Ecs.Components.Core.CollisionRadius>(bldEntity))
		{
			EcsWorld.Add(bldEntity, new Realm.Ecs.Components.Core.CollisionRadius(bldRadius));
		}
		CarveObstacle(targetPos, bldRadius);

		var buildTask = new BuildTask(bldEntity, buildTime);
		EcsWorld.SetOrAdd(workerEntity, buildTask);

		var buildingPos = new System.Numerics.Vector3(targetPos.X, targetPos.Y, targetPos.Z);
		if (!EcsWorld.Has<MoveTo>(workerEntity))
			EcsWorld.Add(workerEntity, new MoveTo(buildingPos));
		else
			EcsWorld.Set(workerEntity, new MoveTo(buildingPos));
	}

	private void UpdateVisualNodesFromEcs(float fDelta)
	{
		var query = Realm.Ecs.Common.QueryCache.AllPositionAndDefinitionIdQuery;
		EcsWorld.Query(in query, (Entity entity, ref Position pos) =>
		{
			if (TryGetUnit3D(entity, out var unit3D) && GodotObject.IsInstanceValid(unit3D))
			{
				var posValue = pos.Value;
				if (!float.IsFinite(posValue.X) || !float.IsFinite(posValue.Y) || !float.IsFinite(posValue.Z))
				{
if (_warnedNonFinitePositions.Add(entity))
				{
					if (_warnedNonFinitePositions.Count > WarnedNonFinitePositionsLimit)
					{
						_warnedNonFinitePositions.RemoveWhere(warned => !EcsWorld.IsAlive(warned));
					}
					GD.PushWarning($"[Simulation] Unit '{unit3D.Name}' (entity {entity.Id}) has a non-finite ECS position ({posValue.X}, {posValue.Y}, {posValue.Z}); skipping visual sync.");
				}
					return;
				}
				Vector3 nextPos = new Vector3(posValue.X, posValue.Y, posValue.Z);
				unit3D.GlobalPosition = nextPos;

				Vector3 velVec = Vector3.Zero;
				if (EcsWorld.Has<Velocity>(entity))
				{
					var vel = EcsWorld.Get<Velocity>(entity);
					velVec = new Vector3(vel.Value.X, vel.Value.Y, vel.Value.Z);
				}

				if (!EcsWorld.Has<MoveTo>(entity) && !EcsWorld.Has<Follow>(entity) && !EcsWorld.Has<InterpolationTarget>(entity))
				{
					velVec = Vector3.Zero;
					if (EcsWorld.Has<Velocity>(entity))
					{
						EcsWorld.Set(entity, new Velocity(System.Numerics.Vector3.Zero));
					}
				}

				unit3D.Velocity = velVec;

				Vector3 lookTargetPos = Vector3.Zero;
				bool hasLookTarget = false;

				if (EcsWorld.Has<AttackTarget>(entity))
				{
					var targetEnt = EcsWorld.Get<AttackTarget>(entity).Target;
					if (EcsWorld.IsAlive(targetEnt) && EcsWorld.Has<Position>(targetEnt))
					{
						var tPosComp = EcsWorld.Get<Position>(targetEnt);
						lookTargetPos = new Vector3(tPosComp.Value.X, tPosComp.Value.Y, tPosComp.Value.Z);
						hasLookTarget = true;
					}
				}
				else if (EcsWorld.Has<HealingTarget>(entity))
				{
					var targetEnt = EcsWorld.Get<HealingTarget>(entity).Target;
					if (EcsWorld.IsAlive(targetEnt) && EcsWorld.Has<Position>(targetEnt))
					{
						var tPosComp = EcsWorld.Get<Position>(targetEnt);
						lookTargetPos = new Vector3(tPosComp.Value.X, tPosComp.Value.Y, tPosComp.Value.Z);
						hasLookTarget = true;
					}
				}
				else if (EcsWorld.Has<Follow>(entity))
				{
					var targetEnt = EcsWorld.Get<Follow>(entity).Target;
					if (EcsWorld.IsAlive(targetEnt) && EcsWorld.Has<Position>(targetEnt))
					{
						var tPosComp = EcsWorld.Get<Position>(targetEnt);
						lookTargetPos = new Vector3(tPosComp.Value.X, tPosComp.Value.Y, tPosComp.Value.Z);
						hasLookTarget = true;
					}
				}
				else if (EcsWorld.Has<BuildTask>(entity))
				{
					var buildTask = EcsWorld.Get<BuildTask>(entity);
					if (EcsWorld.IsAlive(buildTask.BuildingEntity) && EcsWorld.Has<Position>(buildTask.BuildingEntity))
					{
						var bPos = EcsWorld.Get<Position>(buildTask.BuildingEntity);
						lookTargetPos = new Vector3(bPos.Value.X, bPos.Value.Y, bPos.Value.Z);
						hasLookTarget = true;
					}
				}

				Vector3 dir = unit3D.Velocity;
				bool hasDir = dir.LengthSquared() > 0.01f;

				bool forceLookTarget = false;
				if (hasLookTarget)
				{
					if (!hasDir) forceLookTarget = true;
					else
					{
						float distToLook = lookTargetPos.DistanceTo(nextPos);
						if (distToLook < LookTargetProximityDistance || EcsWorld.Has<Follow>(entity)) forceLookTarget = true;
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
					dir = dir.Normalized();
					float angle = Mathf.Atan2(-dir.X, -dir.Z) + Mathf.Pi;
					var rot = unit3D.Rotation;

					bool isFlying = EcsWorld.Has<PathingFlags>(entity)
						&& ((TerrainPathingFlags)EcsWorld.Get<PathingFlags>(entity).Value & TerrainPathingFlags.Flying) != 0;
					float turnRate = 10f;
					if (EcsWorld.Has<MovementStats>(entity))
					{
						var moveStats = EcsWorld.Get<MovementStats>(entity);
						if (moveStats.TurnRate > 0f) turnRate = moveStats.TurnRate;
					}
					rot.Y = Mathf.LerpAngle(rot.Y, angle, turnRate * fDelta);
					unit3D.Rotation = rot;
					if (EcsWorld.Has<RotationY>(entity))
					{
						EcsWorld.Set(entity, new RotationY(rot.Y));
					}

					Vector3 normal = Vector3.Up;
					if (!isFlying && GroundTerrain != null)
					{
						GroundTerrain.GetHeightAndNormal(nextPos.X, nextPos.Z, out _, out normal);
					}

					Vector3 forwardDir = new Vector3(-Mathf.Sin(unit3D.Rotation.Y), 0f, -Mathf.Cos(unit3D.Rotation.Y));
					Vector3 up = normal.Normalized();
					Vector3 right = forwardDir.Cross(up);
					if (right.LengthSquared() > 0.00001f)
					{
						right = right.Normalized();
						Vector3 forwardPerp = right.Cross(up).Normalized();
						Basis targetBasis = new Basis(right, up, forwardPerp);
						var qTarget = targetBasis.GetRotationQuaternion();
						var qCurrent = unit3D.Basis.GetRotationQuaternion();
						var qLerp = qCurrent.Slerp(qTarget, 10f * fDelta);
						unit3D.Basis = new Basis(qLerp);
					}
				}
				else if (EcsWorld.Has<InterpolationTarget>(entity))
				{
					var interp = EcsWorld.Get<InterpolationTarget>(entity);
					var rot = unit3D.Rotation;
					rot.Y = Mathf.LerpAngle(rot.Y, interp.RotationY, 10f * fDelta);
					unit3D.Rotation = rot;
					if (EcsWorld.Has<RotationY>(entity))
					{
						EcsWorld.Set(entity, new RotationY(rot.Y));
					}
				}

				bool isLaborAnimating = (EcsWorld.Has<Gatherer>(entity) && !EcsWorld.Get<Gatherer>(entity).ReturningToBase)
					|| (EcsWorld.Has<BuildTask>(entity) && !EcsWorld.Has<MoveTo>(entity));

				if (isLaborAnimating)
				{
					var state = EcsWorld.Get<WorldState>(_worldEntity);
					float gameElapsed = state.GameElapsedTime;
					float pulse = 1.0f + Mathf.Sin(gameElapsed * 10f) * 0.1f;
					unit3D.Scale = new Vector3(pulse * 0.9f, (2.0f - pulse) * 0.9f, pulse * 0.9f);
				}
				else
				{
					float scaleVal = EcsWorld.Has<CollisionScale>(entity) ? EcsWorld.Get<CollisionScale>(entity).Value : 1.0f;
					unit3D.Scale = Vector3.One * Mathf.Max(0.01f, scaleVal);
				}

				unit3D.PlayAnimation(DetermineUnitAnimation(entity));
			}
		});

		var buildingQuery = QueryCache.AllBuildingAndConstructionStateAndOwnerNoneDeadQuery;
		EcsWorld.Query(in buildingQuery, (Entity entity, ref ConstructionState construction) =>
		{
			if (TryGetUnit3D(entity, out var buildingNode) && GodotObject.IsInstanceValid(buildingNode))
			{
				float alpha = Mathf.Lerp(0.3f, 1.0f, construction.Progress / Mathf.Max(construction.TotalBuildTime, 0.001f));
				buildingNode.Modulate = new Color(1f, 1f, 1f, alpha);
			}
		});
	}

	private string DetermineUnitAnimation(Entity entity)
	{
		if (Realm.Godot.ReplaySystem.ReplayPlaybackManager.Instance.IsPlayingReplay)
		{
			if (EcsWorld.Has<Realm.Ecs.Components.Meta.ReplayAnimationState>(entity))
			{
				return EcsWorld.Get<Realm.Ecs.Components.Meta.ReplayAnimationState>(entity).Animation;
			}
			return "Idle";
		}

		if (EcsWorld.Has<Dead>(entity))
			return "Death";

		bool isMoving = EcsWorld.Has<MoveTo>(entity) && EcsWorld.Has<Velocity>(entity)
			&& EcsWorld.Get<Velocity>(entity).Value.LengthSquared() > 0.01f;

		if (isMoving)
			return "Walk";

		if (EcsWorld.Has<AttackTarget>(entity))
			return "Attack";

		if (EcsWorld.Has<HealingTarget>(entity))
			return "Spell_Cast";

		if (EcsWorld.Has<Gatherer>(entity) && !EcsWorld.Get<Gatherer>(entity).ReturningToBase)
			return "Labor";

		if (EcsWorld.Has<BuildTask>(entity))
		{
			var task = EcsWorld.Get<BuildTask>(entity);
			var target = task.BuildingEntity;
			if (EcsWorld.IsAlive(target) && EcsWorld.Has<Position>(target))
			{
				var tPos = EcsWorld.Get<Position>(target).Value;
				var wPos = EcsWorld.Has<Position>(entity) ? EcsWorld.Get<Position>(entity).Value : System.Numerics.Vector3.Zero;
				if (System.Numerics.Vector3.Distance(wPos, tPos) < 4.0f)
				{
					return "Labor";
				}
				return "Walk";
			}
			return "Labor";
		}

		return "Idle";
	}

	internal bool ExecuteQueuedCommand(Entity entity, string? commandType, System.Numerics.Vector3 targetPos, Entity targetEntity)
	{
		if (commandType == "move")
		{
			var moveTo = new MoveTo(targetPos);
			EcsWorld.SetOrAdd(entity, moveTo);
			return true;
		}
		else if (commandType == "attack")
		{
			if (targetEntity != Entity.Null && EcsWorld.IsAlive(targetEntity))
			{
				var attackTarget = new AttackTarget(targetEntity);
				EcsWorld.SetOrAdd(entity, attackTarget);
				return true;
			}
			return false;
		}
		else if (commandType == "attackmove")
		{
			var attackMove = new AttackMove(targetPos);
			EcsWorld.SetOrAdd(entity, attackMove);

			var moveTo = new MoveTo(targetPos);
			EcsWorld.SetOrAdd(entity, moveTo);
			return true;
		}
		else if (commandType == "follow")
		{
			if (targetEntity != Entity.Null && EcsWorld.IsAlive(targetEntity))
			{
				if (EcsWorld.Has<DefinitionId>(entity) && EcsWorld.Get<DefinitionId>(entity).Value == "priest")
				{
					var healTarget = new HealingTarget(targetEntity);
					EcsWorld.SetOrAdd(entity, healTarget);
				}
				else
				{
					var follow = new Follow(targetEntity);
					EcsWorld.SetOrAdd(entity, follow);
				}
				return true;
			}
			return false;
		}
		else if (commandType == "patrol")
		{
			var unitPos = EcsWorld.Has<Position>(entity) ? EcsWorld.Get<Position>(entity).Value : System.Numerics.Vector3.Zero;
			var patrol = new Patrol(unitPos, targetPos);
			EcsWorld.SetOrAdd(entity, patrol);

			var moveTo = new MoveTo(targetPos);
			EcsWorld.SetOrAdd(entity, moveTo);
			return true;
		}
		else if (commandType == "gather")
		{
			if (targetEntity != Entity.Null && EcsWorld.IsAlive(targetEntity))
			{
				string propId = EcsWorld.Has<PropIdentity>(targetEntity)
					? EcsWorld.Get<PropIdentity>(targetEntity).PropId
					: (EcsWorld.Has<DefinitionId>(targetEntity) ? EcsWorld.Get<DefinitionId>(targetEntity).Value : "");
				string? resType = propId switch
				{
					"goldmine" => "gold",
					"tree" => "wood",
					"rock" => "stone",
					_ => null
				};

				if (resType != null)
				{
					var gatherer = new Gatherer(resType, targetEntity);
					EcsWorld.SetOrAdd(entity, gatherer);

					var moveTo = new MoveTo(targetPos);
					EcsWorld.SetOrAdd(entity, moveTo);
					return true;
				}
			}
			return false;
		}
		else
		{
			if (targetEntity != Entity.Null && EcsWorld.IsAlive(targetEntity) && EcsWorld.Has<ConstructionState>(targetEntity))
			{
				var cState = EcsWorld.Get<ConstructionState>(targetEntity);
				var newTask = new BuildTask(targetEntity, cState.TotalBuildTime)
				{
					Progress = cState.Progress
				};
				EcsWorld.SetOrAdd(entity, newTask);

				var moveTo = new MoveTo(new System.Numerics.Vector3(targetPos.X, targetPos.Y, targetPos.Z));
				EcsWorld.SetOrAdd(entity, moveTo);
				return true;
			}
			else if (!string.IsNullOrEmpty(commandType) && UnitRegistry.ContainsKey(commandType))
			{
				AssignBuildTaskToWorker(entity, commandType, targetPos);
				return true;
			}
			return false;
		}
	}
}
