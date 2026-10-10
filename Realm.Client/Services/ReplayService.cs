using Arch.Core;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Resources;
using Realm.Ecs.Components.Tags;
using Realm.Ecs.Services;
using Realm.Client.ReplaySystem;
using System.Collections.Generic;

namespace Realm.Client.Services;

public class ReplayService
{
	private readonly WorldAccessor _ecsWorldAccessor;
	private World EcsWorld => _ecsWorldAccessor.Current;
	private ReplayRecorder _replayRecorder;
	private readonly Dictionary<int, ReplayUnitSnapshot> _lastRecordedUnits = new();
	private readonly List<ReplayProjectileSnapshot> _tickProjectiles = new();

	public ReplayService(WorldAccessor ecsWorldAccessor)
	{
		_ecsWorldAccessor = ecsWorldAccessor;
	}

	public bool IsResettingForReplay { get; set; } = false;
	public bool IsRecording => _replayRecorder != null;

	public void StartRecording(string path, string mapName, List<Network.LobbyManager.PlayerInfo> players)
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

	public void RecordProjectile(string typeId, System.Numerics.Vector3 start, System.Numerics.Vector3 target)
	{
		if (_replayRecorder == null) return;
		_tickProjectiles.Add(new ReplayProjectileSnapshot
		{
			ProjectileTypeId = typeId,
			Start = new Realm.Client.NetworkVector3(start.X, start.Y, start.Z),
			Target = new Realm.Client.NetworkVector3(target.X, target.Y, target.Z),
			Speed = 0f // We might need to handle speed later or remove it from snapshot
		});
	}

	public void SetupPlayersForPlayback(List<(int PeerId, string Name)> players)
	{
		Entity worldEntity = Entity.Null;
		var worldQuery = QueryCache.AllNetworkMappingStateQuery;
		EcsWorld.Query(in worldQuery, (Entity entity) => worldEntity = entity);

		if (worldEntity == Entity.Null)
		{
			return;
		}

		ref var mapping = ref EcsWorld.Get<NetworkMappingState>(worldEntity);
		mapping.ServerToClientEntityMap.Clear();
		mapping.ClientToServerEntityMap.Clear();
		mapping.PeerIdToPlayerEntityMap.Clear();

		foreach (var p in players)
		{
			var playerEntity = EcsWorld.Create();
			EcsWorld.Add(playerEntity, new Player());
			EcsWorld.Add(playerEntity, new Name(p.Name));
			
			EcsWorld.Add(playerEntity, new PlayerPopulation(0, 0));
			EcsWorld.Add(playerEntity, new SpellCooldowns(new Dictionary<string, float>(System.StringComparer.OrdinalIgnoreCase)));
			EcsWorld.Add(playerEntity, new PlayerUpgrades(false, false, false));

			var resourcesDict = new Dictionary<ResourceId, int>
			{
				{ new ResourceId("gold"), 0 },
				{ new ResourceId("wood"), 0 },
				{ new ResourceId("stone"), 0 }
			};
			EcsWorld.Add(playerEntity, new PlayerResources(resourcesDict));

			mapping.PeerIdToPlayerEntityMap[p.PeerId] = playerEntity;
			if (p.PeerId == 1)
			{
				mapping.PlayerEntity = playerEntity;
			}
			else if (p.PeerId == -1)
			{
				mapping.EnemyPlayerEntity = playerEntity;
			}
		}
	}

	public (Entity Entity, string ModelPath, bool IsEnemy) SpawnUnitFromReplaySnapshot(ReplayUnitSnapshot snap)
	{
		if (!Realm.Client.Core.GameHost.UnitRegistry.TryGetValue(snap.UnitId, out var meta))
		{
			return (Entity.Null, "", false);
		}

		Entity worldEntity = Entity.Null;
		var worldQuery = QueryCache.AllNetworkMappingStateQuery;
		EcsWorld.Query(in worldQuery, (Entity entity) => worldEntity = entity);

		if (worldEntity == Entity.Null)
		{
			return (Entity.Null, "", false);
		}

		ref var mapping = ref EcsWorld.Get<NetworkMappingState>(worldEntity);

		Entity ownerPlayerEntity = mapping.PlayerEntity;
		if (mapping.PeerIdToPlayerEntityMap.TryGetValue(snap.OwnerPlayerEntityId, out var pe))
		{
			ownerPlayerEntity = pe;
		}
		bool isEnemy = ownerPlayerEntity != mapping.PlayerEntity;

		var entity = EcsWorld.Create();
		EcsWorld.Add(entity, new DefinitionId(snap.UnitId));
		EcsWorld.Add(entity, new Name(meta.Name));
		EcsWorld.Add(entity, new Position(snap.Position.ToNumerics()));
		EcsWorld.Add(entity, new Owner(ownerPlayerEntity.AsPlayerEntity(EcsWorld)));
		EcsWorld.Add(entity, new Health(snap.CurrentHp, snap.MaxHp));
		if (meta.Damage > 0 || snap.UnitId == "priest")
		{
			EcsWorld.Add(entity, new Attack(meta.Damage, meta.Range, meta.AttackCooldown));
		}
		EcsWorld.Add(entity, new Armor(meta.Armor));
		if (meta.Speed > 0)
		{
			EcsWorld.Add(entity, new MovementStats(meta.Speed, 20f, 10f));
			EcsWorld.Add(entity, new Realm.Ecs.Components.Tags.Movable());
			EcsWorld.Add(entity, new Inventory());
		}
		else
		{
			EcsWorld.Add(entity, new Building());
		}
		var target = new InterpolationTarget
		{
			Position = snap.Position.ToNumerics(),
			Velocity = snap.Velocity.ToNumerics(),
			RotationY = snap.RotationY
		};
		EcsWorld.Add(entity, target);

		mapping.ServerToClientEntityMap[snap.EntityId] = entity;
		mapping.ClientToServerEntityMap[entity.Id] = snap.EntityId;

		return (entity, meta.ModelPath ?? "", isEnemy);
	}

	public void RecordGameplayTick()
	{
		if (_replayRecorder == null) return;

		Entity worldEntity = Entity.Null;
		EcsWorld.Query(in QueryCache.AllReplayStateAndNetworkMappingStateQuery, (Entity entity) => worldEntity = entity);
		if (worldEntity == Entity.Null) return;

		ref var replayState = ref EcsWorld.Get<ReplayState>(worldEntity);
		int currentTick = replayState.ReplayTickCounter;
		bool isKeyframe = (currentTick % 600 == 0);

		List<ReplayUnitSnapshot> unitsToRecord = ReplayObjectPool.RentList();
		List<int> activeIds = ReplayObjectPool.RentIntList();
		List<ReplayProjectileSnapshot> projectilesToRecord = GetTickProjectiles();

		if (isKeyframe) _lastRecordedUnits.Clear();

		var mapping = EcsWorld.Get<NetworkMappingState>(worldEntity);

		EcsWorld.Query(in QueryCache.AllDefinitionIdAndPositionAndOwnerQuery, (Entity entity, ref DefinitionId defId, ref Position posComp, ref Owner ownerComp) =>
		{
			int entityId = entity.Id;
			string unitId = defId.Value;
			int ownerPlayerEntityId = GetOwnerPlayerEntityId(mapping, ownerComp.PlayerEntity);

			System.Numerics.Vector3 pos = posComp.Value;
			GetUnitState(entity, out float rotY, out float currentHp, out float maxHp, out bool isDead, out bool isBuilding, out System.Numerics.Vector3 vel);

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

	private void GetUnitState(Entity entity, out float rotY, out float currentHp, out float maxHp, out bool isDead, out bool isBuilding, out System.Numerics.Vector3 vel)
	{
		rotY = EcsWorld.Has<RotationY>(entity) ? EcsWorld.Get<RotationY>(entity).Value : 0f;
		currentHp = EcsWorld.Has<Health>(entity) ? EcsWorld.Get<Health>(entity).Current : 0f;
		maxHp = EcsWorld.Has<Health>(entity) ? EcsWorld.Get<Health>(entity).Max : 0f;
		isDead = EcsWorld.Has<Dead>(entity);
		isBuilding = EcsWorld.Has<Building>(entity);
		vel = EcsWorld.Has<Velocity>(entity) ? EcsWorld.Get<Velocity>(entity).Value : System.Numerics.Vector3.Zero;
	}

	private List<ReplayProjectileSnapshot> GetTickProjectiles()
	{
		if (_tickProjectiles.Count == 0) return null;
		var list = ReplayObjectPool.RentProjectileList();
		list.AddRange(_tickProjectiles);
		_tickProjectiles.Clear();
		return list;
	}

	private int GetOwnerPlayerEntityId(NetworkMappingState mapping, Realm.Ecs.Common.PlayerEntity owner)
	{
		foreach (var kvp in mapping.PeerIdToPlayerEntityMap)
		{
			if (kvp.Value == owner.Value)
			{
				return kvp.Key;
			}
		}
		return -1;
	}

	private string DetermineAnimation(Entity entity, System.Numerics.Vector3 pos, bool isDead, System.Numerics.Vector3 vel)
	{
		if (isDead) return "Death";
		if (EcsWorld.Has<MoveTo>(entity) && vel.LengthSquared() > 0.01f) return "Walk";
		if (EcsWorld.Has<AttackTarget>(entity)) return "Attack";
		if (EcsWorld.Has<HealingTarget>(entity)) return "Spell_Cast";
		if (EcsWorld.Has<Gatherer>(entity) && !EcsWorld.Get<Gatherer>(entity).ReturningToBase) return "Labor";
		
		if (EcsWorld.Has<BuildTask>(entity))
		{
			return DetermineBuildAnimation(entity, pos);
		}
		
		return "Idle";
	}

	private string DetermineBuildAnimation(Entity entity, System.Numerics.Vector3 pos)
	{
		var task = EcsWorld.Get<BuildTask>(entity);
		var target = task.BuildingEntity;
		if (EcsWorld.IsAlive(target) && EcsWorld.Has<Position>(target))
		{
			var tPos = EcsWorld.Get<Position>(target).Value;
			if (System.Numerics.Vector3.Distance(pos, tPos) < 4.0f) return "Labor";
			return "Walk";
		}
		return "Labor";
	}

	private ReplayUnitSnapshot CreateUnitSnapshot(int entityId, string unitId, int ownerPlayerEntityId, System.Numerics.Vector3 pos, float rotY, float currentHp, float maxHp, bool isDead, bool isBuilding, System.Numerics.Vector3 vel, string anim)
	{
		return new ReplayUnitSnapshot
		{
			EntityId = entityId,
			UnitId = unitId,
			OwnerPlayerEntityId = ownerPlayerEntityId,
			Position = new Realm.Client.NetworkVector3(pos.X, pos.Y, pos.Z),
			RotationY = rotY,
			CurrentHp = currentHp,
			MaxHp = maxHp,
			IsDead = isDead,
			IsBuilding = isBuilding,
			Velocity = new Realm.Client.NetworkVector3(vel.X, vel.Y, vel.Z),
			Animation = anim
		};
	}

	private bool HasSnapshotChanged(ReplayUnitSnapshot last, string unitId, int ownerPlayerEntityId, System.Numerics.Vector3 pos, float rotY, float currentHp, float maxHp, bool isDead, bool isBuilding, System.Numerics.Vector3 vel, string anim)
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

	private bool HasPositionChanged(ReplayUnitSnapshot last, System.Numerics.Vector3 pos, float rotY)
	{
		if (last.Position.X != pos.X || last.Position.Y != pos.Y || last.Position.Z != pos.Z) return true;
		if (last.RotationY != rotY) return true;
		return false;
	}

	private bool HasVelocityChanged(ReplayUnitSnapshot last, System.Numerics.Vector3 vel)
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
		Entity playerEnt = mapping.PlayerEntity;
		if (!EcsWorld.IsAlive(playerEnt) || !EcsWorld.Has<PlayerResources>(playerEnt)) return;

		var dict = EcsWorld.Get<PlayerResources>(playerEnt).Value;
		if (dict.TryGetValue(new ResourceId("gold"), out var gVal)) gold = gVal;
		if (dict.TryGetValue(new ResourceId("wood"), out var wVal)) wood = wVal;
		if (dict.TryGetValue(new ResourceId("stone"), out var sVal)) stone = sVal;
	}

}