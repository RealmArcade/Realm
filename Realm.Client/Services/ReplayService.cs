using System;
using System.Collections.Generic;
using System.Numerics;
using Realm.Ecs;
using Realm.Client.Core;
using Realm.Client.Network;
using Realm.Client.ReplaySystem;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Resources;
using Realm.Ecs.Components.Tags;
using Realm.Ecs.Services;
using Realm.Shared.Network;

namespace Realm.Client.Services;

public class ReplayService
{
	private ReplayRecorder? _replayRecorder;
	private readonly Dictionary<int, ReplayUnitSnapshot> _lastRecordedUnits = new();
	private readonly List<ReplayProjectileSnapshot> _tickProjectiles = new();

	public bool IsResettingForReplay { get; set; } = false;
	public bool IsRecording => _replayRecorder != null;

	public void StartRecording(string path, string mapName, List<PlayerInfo>? players)
	{
		_lastRecordedUnits.Clear();
		_replayRecorder = new ReplayRecorder(path, mapName, players);
		_replayRecorder.Start();
	}

	public void StopRecording()
	{
		if (_replayRecorder != null)
		{
			_replayRecorder.Stop();
			_replayRecorder = null;
		}
	}

	public void RecordProjectile(string typeId, Vector3 start, Vector3 target)
	{
		if (_replayRecorder == null) return;
		_tickProjectiles.Add(new ReplayProjectileSnapshot
		{
			ProjectileTypeId = typeId,
			Start = new NetworkVector3(start.X, start.Y, start.Z),
			Target = new NetworkVector3(target.X, target.Y, target.Z),
			Speed = 0f
		});
	}

	public void SetupPlayersForPlayback(List<(int PeerId, string Name)> players)
	{
		if (GameContext.World is not { } world) return;

		Entity worldEntity = Entity.Null;
		var worldQuery = QueryCache.AllNetworkMappingStateQuery;
		world.Query(in worldQuery, (Entity entity) => worldEntity = entity);

		if (worldEntity == Entity.Null)
		{
			return;
		}

		ref var mapping = ref world.Get<NetworkMappingState>(worldEntity);
		mapping.ServerToClientEntityMap.Clear();
		mapping.ClientToServerEntityMap.Clear();
		mapping.PeerIdToPlayerEntityMap.Clear();

		foreach (var p in players)
		{
			var playerEntity = world.Create();
			world.Add(playerEntity, new Player());
			world.Add(playerEntity, new Name(p.Name));

			world.Add(playerEntity, new PlayerPopulation(0, 0));
			world.Add(playerEntity, new SpellCooldowns(null));
			world.Add(playerEntity, new PlayerUpgrades(false, false, false));

			var resourcesDict = new Dictionary<ResourceId, int>
			{
				{ new ResourceId("gold"), 0 },
				{ new ResourceId("wood"), 0 },
				{ new ResourceId("stone"), 0 }
			};
			world.Add(playerEntity, new PlayerResources(resourcesDict));

			mapping.PeerIdToPlayerEntityMap[p.PeerId] = playerEntity;
			if (p.PeerId >= 0 && p.PeerId < mapping.PlayerEntities.Length)
			{
				mapping.PlayerEntities[p.PeerId] = playerEntity;
			}
		}
	}

	public (Entity Entity, string ModelPath, int Player) SpawnUnitFromReplaySnapshot(ReplayUnitSnapshot snap)
	{
		if (!GameHost.UnitRegistry.TryGetValue(snap.UnitId, out var meta))
		{
			return (Entity.Null, "", 0);
		}

		if (GameContext.World is not { } world) return (Entity.Null, "", 0);

		Entity worldEntity = Entity.Null;
		var worldQuery = QueryCache.AllNetworkMappingStateQuery;
		world.Query(in worldQuery, (Entity entity) => worldEntity = entity);

		if (worldEntity == Entity.Null)
		{
			return (Entity.Null, "", 0);
		}

		ref var mapping = ref world.Get<NetworkMappingState>(worldEntity);

		var entity = ReplicatedUnitLifecycle.ApplySnapshotToEntity(snap.UnitId, snap.EntityId, snap.OwnerPlayerEntityId, snap.Position.ToNumerics(), snap.RotationY, snap.CurrentHp, snap.MaxHp, snap.IsBuilding, meta, ref mapping, GameContext.UnitSpawnService.GetFallbackModelPath, out string modelPath, out int playerIndex);
		return (entity, modelPath, playerIndex);
	}

	public void RecordGameplayTickDomain(List<(Entity Entity, System.Numerics.Vector3 Position, System.Numerics.Vector3 Velocity, float RotationY)> transforms)
	{
		if (GameContext.World is not { } world) return;

		foreach (var t in transforms)
		{
			if (world.Has<Position>(t.Entity))
			{
				world.Set(t.Entity, new Position(t.Position));
			}
			world.SetOrAdd(t.Entity, new Velocity(t.Velocity));
			world.SetOrAdd(t.Entity, new RotationY(t.RotationY));
		}
		
		RecordGameplayTick();
	}

	public void RecordGameplayTick()
	{
		if (_replayRecorder == null) return;
		if (GameContext.World is not { } world) return;

		Entity worldEntity = Entity.Null;
		world.Query(in QueryCache.AllReplayStateAndNetworkMappingStateQuery, (Entity entity) => worldEntity = entity);
		if (worldEntity == Entity.Null) return;

		ref var replayState = ref world.Get<ReplayState>(worldEntity);
		int currentTick = replayState.ReplayTickCounter;
		bool isKeyframe = (currentTick % 600 == 0);

		List<ReplayUnitSnapshot> unitsToRecord = ReplayObjectPool.RentList();
		List<int> activeIds = ReplayObjectPool.RentIntList();
		List<ReplayProjectileSnapshot>? projectilesToRecord = GetTickProjectiles();

		if (isKeyframe) _lastRecordedUnits.Clear();

		var mapping = world.Get<NetworkMappingState>(worldEntity);

		world.Query(in QueryCache.AllDefinitionIdAndPositionAndOwnerQuery, (Entity entity, ref DefinitionId defId, ref Position posComp, ref Owner ownerComp) =>
		{
			int entityId = entity.Id;
			string unitId = defId.Value;
			int ownerPlayerEntityId = GetOwnerPlayerEntityId(mapping, ownerComp.PlayerEntity);

			Vector3 pos = posComp.Value;
			GetUnitState(world, entity, out float rotY, out float currentHp, out float maxHp, out bool isDead, out bool isBuilding, out Vector3 vel);

			string anim = DetermineAnimation(world, entity, pos, isDead, vel);
			activeIds.Add(entityId);

			if (isKeyframe || !_lastRecordedUnits.TryGetValue(entityId, out var last) || HasSnapshotChanged(last, unitId, ownerPlayerEntityId, pos, rotY, currentHp, maxHp, isDead, isBuilding, vel, anim))
			{
				var snap = CreateUnitSnapshot(entityId, unitId, ownerPlayerEntityId, pos, rotY, currentHp, maxHp, isDead, isBuilding, vel, anim);
				unitsToRecord.Add(snap);
				_lastRecordedUnits[entityId] = snap;
			}
		});

		if (!isKeyframe)
		{
			ProcessDestroyedUnits(activeIds, unitsToRecord);
		}

		float gold = replayState.GoldBackup, wood = replayState.WoodBackup, stone = replayState.StoneBackup;
		GetPlayerResources(world, mapping, ref gold, ref wood, ref stone);

		_replayRecorder.RecordTick(currentTick, unitsToRecord, projectilesToRecord, gold, wood, stone, isKeyframe);

		ReplayObjectPool.ReturnList(unitsToRecord);
		ReplayObjectPool.ReturnIntList(activeIds);
		if (projectilesToRecord != null) ReplayObjectPool.ReturnProjectileList(projectilesToRecord);

		replayState.ReplayTickCounter++;
	}

	private static void GetUnitState(EcsWorld world, Entity entity, out float rotY, out float currentHp, out float maxHp, out bool isDead, out bool isBuilding, out Vector3 vel)
	{
		rotY = world.Has<RotationY>(entity) ? world.Get<RotationY>(entity).Value : 0f;
		currentHp = world.Has<Health>(entity) ? world.Get<Health>(entity).Current : 0f;
		maxHp = world.Has<Health>(entity) ? world.Get<Health>(entity).Max : 0f;
		isDead = world.Has<Dead>(entity);
		isBuilding = world.Has<Building>(entity);
		vel = world.Has<Velocity>(entity) ? world.Get<Velocity>(entity).Value : Vector3.Zero;
	}

	private List<ReplayProjectileSnapshot>? GetTickProjectiles()
	{
		if (_tickProjectiles.Count == 0) return null;
		var list = ReplayObjectPool.RentProjectileList();
		list.AddRange(_tickProjectiles);
		_tickProjectiles.Clear();
		return list;
	}

	private int GetOwnerPlayerEntityId(NetworkMappingState mapping, PlayerEntity owner)
	{
		for (int i = 0; i < mapping.PlayerEntities.Length; i++)
		{
			if (mapping.PlayerEntities[i] == owner.Value)
			{
				return i;
			}
		}

		foreach (var kvp in mapping.PeerIdToPlayerEntityMap)
		{
			if (kvp.Value == owner.Value)
			{
				return kvp.Key;
			}
		}

		return -1;
	}

	private static string DetermineAnimation(EcsWorld world, Entity entity, Vector3 pos, bool isDead, Vector3 vel)
	{
		if (isDead) return "Death";
		if (world.Has<MoveTo>(entity) && vel.LengthSquared() > 0.01f) return "Walk";
		if (world.Has<AttackTarget>(entity)) return "Attack";
		if (world.Has<HealingTarget>(entity)) return "Spell_Cast";
		if (world.Has<Gatherer>(entity) && !world.Get<Gatherer>(entity).ReturningToBase) return "Labor";

		if (world.Has<BuildTask>(entity))
		{
			return DetermineBuildAnimation(world, entity, pos);
		}

		return "Idle";
	}

	private static string DetermineBuildAnimation(EcsWorld world, Entity entity, Vector3 pos)
	{
		var task = world.Get<BuildTask>(entity);
		var target = task.BuildingEntity;
		if (world.IsAlive(target) && world.Has<Position>(target))
		{
			var tPos = world.Get<Position>(target).Value;
			if (Vector3.Distance(pos, tPos) < 4.0f) return "Labor";
			return "Walk";
		}

		return "Labor";
	}

	private ReplayUnitSnapshot CreateUnitSnapshot(int entityId, string unitId, int ownerPlayerEntityId, Vector3 pos, float rotY, float currentHp, float maxHp, bool isDead, bool isBuilding, Vector3 vel, string anim)
	{
		return new ReplayUnitSnapshot
		{
			EntityId = entityId,
			UnitId = unitId,
			OwnerPlayerEntityId = ownerPlayerEntityId,
			Position = new NetworkVector3(pos.X, pos.Y, pos.Z),
			RotationY = rotY,
			CurrentHp = currentHp,
			MaxHp = maxHp,
			IsDead = isDead,
			IsBuilding = isBuilding,
			Velocity = new NetworkVector3(vel.X, vel.Y, vel.Z),
			Animation = anim
		};
	}

	private bool HasSnapshotChanged(ReplayUnitSnapshot last, string unitId, int ownerPlayerEntityId, Vector3 pos, float rotY, float currentHp, float maxHp, bool isDead, bool isBuilding, Vector3 vel, string anim)
	{
		return HasStateChanged(last, unitId, ownerPlayerEntityId, currentHp, maxHp, isDead, isBuilding, anim) ||
		       HasPositionChanged(last, pos, rotY) ||
		       HasVelocityChanged(last, vel);
	}

	private bool HasStateChanged(ReplayUnitSnapshot last, string unitId, int ownerPlayerEntityId, float currentHp, float maxHp, bool isDead, bool isBuilding, string anim)
	{
		if (last.UnitId != unitId) return true;
		if (last.OwnerPlayerEntityId != ownerPlayerEntityId) return true;
		if (last.CurrentHp != currentHp || last.MaxHp != maxHp) return true;
		if (last.IsDead != isDead || last.IsBuilding != isBuilding) return true;
		if (last.Animation != anim) return true;
		return false;
	}

	private bool HasPositionChanged(ReplayUnitSnapshot last, Vector3 pos, float rotY)
	{
		if (last.Position.X != pos.X || last.Position.Y != pos.Y || last.Position.Z != pos.Z) return true;
		if (last.RotationY != rotY) return true;
		return false;
	}

	private bool HasVelocityChanged(ReplayUnitSnapshot last, Vector3 vel)
	{
		if (last.Velocity.X != vel.X || last.Velocity.Y != vel.Y || last.Velocity.Z != vel.Z) return true;
		return false;
	}

	private void ProcessDestroyedUnits(List<int> activeIds, List<ReplayUnitSnapshot> unitsToRecord)
	{
		List<int> destroyedIds = ReplayObjectPool.RentIntList();
		foreach (var pair in _lastRecordedUnits)
		{
			if (!activeIds.Contains(pair.Key))
			{
				destroyedIds.Add(pair.Key);
			}
		}

		foreach (int id in destroyedIds)
		{
			var deadSnap = _lastRecordedUnits[id];
			var deadEventSnap = new ReplayUnitSnapshot
			{
				EntityId = deadSnap.EntityId,
				UnitId = deadSnap.UnitId,
				OwnerPlayerEntityId = deadSnap.OwnerPlayerEntityId,
				Position = deadSnap.Position,
				RotationY = deadSnap.RotationY,
				CurrentHp = 0f,
				MaxHp = deadSnap.MaxHp,
				IsDead = true,
				IsBuilding = deadSnap.IsBuilding,
				Velocity = default,
				Animation = "Death"
			};
			unitsToRecord.Add(deadEventSnap);
			_lastRecordedUnits.Remove(id);
		}

		ReplayObjectPool.ReturnIntList(destroyedIds);
	}

	private static void GetPlayerResources(EcsWorld world, NetworkMappingState mapping, ref float gold, ref float wood, ref float stone)
	{
		Entity playerEnt = mapping.PlayerEntities[1];
		if (playerEnt == Entity.Null || !world.IsAlive(playerEnt) || !world.Has<PlayerResources>(playerEnt)) return;

		var resources = world.Get<PlayerResources>(playerEnt);
		if (resources.TryGetValue(new ResourceId("gold"), out var gVal)) gold = gVal;
		if (resources.TryGetValue(new ResourceId("wood"), out var wVal)) wood = wVal;
		if (resources.TryGetValue(new ResourceId("stone"), out var sVal)) stone = sVal;
	}
}