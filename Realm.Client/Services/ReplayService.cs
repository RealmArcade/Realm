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

namespace Realm.Client.Services;

public class ReplayService
{
	private ReplayRecorder _replayRecorder;
	private readonly Dictionary<int, ReplayUnitSnapshot> _lastRecordedUnits = new();
	private readonly List<ReplayProjectileSnapshot> _tickProjectiles = new();

	public bool IsResettingForReplay { get; set; } = false;
	public bool IsRecording => _replayRecorder != null;

	public void StartRecording(string path, string mapName, List<PlayerInfo> players)
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
			Speed = 0f // We might need to handle speed later or remove it from snapshot
		});
	}

	public void SetupPlayersForPlayback(List<(int PeerId, string Name)> players)
	{
		Entity worldEntity = Entity.Null;
		var worldQuery = QueryCache.AllNetworkMappingStateQuery;
		GameContext.World.Query(in worldQuery, (Entity entity) => worldEntity = entity);

		if (worldEntity == Entity.Null)
		{
			return;
		}

		ref var mapping = ref GameContext.World.Get<NetworkMappingState>(worldEntity);
		mapping.ServerToClientEntityMap.Clear();
		mapping.ClientToServerEntityMap.Clear();
		mapping.PeerIdToPlayerEntityMap.Clear();

		foreach (var p in players)
		{
			var playerEntity = GameContext.World.Create();
			GameContext.World.Add(playerEntity, new Player());
			GameContext.World.Add(playerEntity, new Name(p.Name));

			GameContext.World.Add(playerEntity, new PlayerPopulation(0, 0));
			GameContext.World.Add(playerEntity, new SpellCooldowns(null));
			GameContext.World.Add(playerEntity, new PlayerUpgrades(false, false, false));

			var resourcesDict = new Dictionary<ResourceId, int>
			{
				{ new ResourceId("gold"), 0 },
				{ new ResourceId("wood"), 0 },
				{ new ResourceId("stone"), 0 }
			};
			GameContext.World.Add(playerEntity, new PlayerResources(resourcesDict));

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

		Entity worldEntity = Entity.Null;
		var worldQuery = QueryCache.AllNetworkMappingStateQuery;
		GameContext.World.Query(in worldQuery, (Entity entity) => worldEntity = entity);

		if (worldEntity == Entity.Null)
		{
			return (Entity.Null, "", 0);
		}

		ref var mapping = ref GameContext.World.Get<NetworkMappingState>(worldEntity);

		var entity = NetworkService.ApplySnapshotToEntity(snap.UnitId, snap.EntityId, snap.OwnerPlayerEntityId, snap.Position.ToNumerics(), snap.Velocity.ToNumerics(), snap.RotationY, snap.CurrentHp, snap.MaxHp, snap.IsBuilding, meta, ref mapping, GameContext.UnitSpawnService.GetFallbackModelPath, out string modelPath, out int playerIndex);
		return (entity, modelPath, playerIndex);
	}

	public void RecordGameplayTickDomain(List<(Entity Entity, System.Numerics.Vector3 Position, System.Numerics.Vector3 Velocity, float RotationY)> transforms)
	{
		foreach (var t in transforms)
		{
			if (GameContext.World.Has<Position>(t.Entity))
			{
				GameContext.World.Set(t.Entity, new Position(t.Position));
			}
			GameContext.World.SetOrAdd(t.Entity, new Velocity(t.Velocity));
			GameContext.World.SetOrAdd(t.Entity, new RotationY(t.RotationY));
		}
		
		RecordGameplayTick();
	}

	public void RecordGameplayTick()
	{
		if (_replayRecorder == null) return;

		Entity worldEntity = Entity.Null;
		GameContext.World.Query(in QueryCache.AllReplayStateAndNetworkMappingStateQuery, (Entity entity) => worldEntity = entity);
		if (worldEntity == Entity.Null) return;

		ref var replayState = ref GameContext.World.Get<ReplayState>(worldEntity);
		int currentTick = replayState.ReplayTickCounter;
		bool isKeyframe = (currentTick % 600 == 0);

		List<ReplayUnitSnapshot> unitsToRecord = ReplayObjectPool.RentList();
		List<int> activeIds = ReplayObjectPool.RentIntList();
		List<ReplayProjectileSnapshot> projectilesToRecord = GetTickProjectiles();

		if (isKeyframe) _lastRecordedUnits.Clear();

		var mapping = GameContext.World.Get<NetworkMappingState>(worldEntity);

		GameContext.World.Query(in QueryCache.AllDefinitionIdAndPositionAndOwnerQuery, (Entity entity, ref DefinitionId defId, ref Position posComp, ref Owner ownerComp) =>
		{
			int entityId = entity.Id;
			string unitId = defId.Value;
			int ownerPlayerEntityId = GetOwnerPlayerEntityId(mapping, ownerComp.PlayerEntity);

			Vector3 pos = posComp.Value;
			GetUnitState(entity, out float rotY, out float currentHp, out float maxHp, out bool isDead, out bool isBuilding, out Vector3 vel);

			string anim = DetermineAnimation(entity, pos, isDead, vel);
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
		GetPlayerResources(mapping, ref gold, ref wood, ref stone);

		_replayRecorder.RecordTick(currentTick, unitsToRecord, projectilesToRecord, gold, wood, stone, isKeyframe);

		ReplayObjectPool.ReturnList(unitsToRecord);
		ReplayObjectPool.ReturnIntList(activeIds);
		if (projectilesToRecord != null) ReplayObjectPool.ReturnProjectileList(projectilesToRecord);

		replayState.ReplayTickCounter++;
	}

	private void GetUnitState(Entity entity, out float rotY, out float currentHp, out float maxHp, out bool isDead, out bool isBuilding, out Vector3 vel)
	{
		rotY = GameContext.World.Has<RotationY>(entity) ? GameContext.World.Get<RotationY>(entity).Value : 0f;
		currentHp = GameContext.World.Has<Health>(entity) ? GameContext.World.Get<Health>(entity).Current : 0f;
		maxHp = GameContext.World.Has<Health>(entity) ? GameContext.World.Get<Health>(entity).Max : 0f;
		isDead = GameContext.World.Has<Dead>(entity);
		isBuilding = GameContext.World.Has<Building>(entity);
		vel = GameContext.World.Has<Velocity>(entity) ? GameContext.World.Get<Velocity>(entity).Value : Vector3.Zero;
	}

	private List<ReplayProjectileSnapshot> GetTickProjectiles()
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

	private string DetermineAnimation(Entity entity, Vector3 pos, bool isDead, Vector3 vel)
	{
		if (isDead) return "Death";
		if (GameContext.World.Has<MoveTo>(entity) && vel.LengthSquared() > 0.01f) return "Walk";
		if (GameContext.World.Has<AttackTarget>(entity)) return "Attack";
		if (GameContext.World.Has<HealingTarget>(entity)) return "Spell_Cast";
		if (GameContext.World.Has<Gatherer>(entity) && !GameContext.World.Get<Gatherer>(entity).ReturningToBase) return "Labor";

		if (GameContext.World.Has<BuildTask>(entity))
		{
			return DetermineBuildAnimation(entity, pos);
		}

		return "Idle";
	}

	private string DetermineBuildAnimation(Entity entity, Vector3 pos)
	{
		var task = GameContext.World.Get<BuildTask>(entity);
		var target = task.BuildingEntity;
		if (GameContext.World.IsAlive(target) && GameContext.World.Has<Position>(target))
		{
			var tPos = GameContext.World.Get<Position>(target).Value;
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

	private void GetPlayerResources(NetworkMappingState mapping, ref float gold, ref float wood, ref float stone)
	{
		Entity playerEnt = mapping.PlayerEntities[1];
		if (playerEnt == Entity.Null || !GameContext.World.IsAlive(playerEnt) || !GameContext.World.Has<PlayerResources>(playerEnt)) return;

		var resources = GameContext.World.Get<PlayerResources>(playerEnt);
		if (resources.TryGetValue(new ResourceId("gold"), out var gVal)) gold = gVal;
		if (resources.TryGetValue(new ResourceId("wood"), out var wVal)) wood = wVal;
		if (resources.TryGetValue(new ResourceId("stone"), out var sVal)) stone = sVal;
	}
}