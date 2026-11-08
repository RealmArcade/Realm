using System;
using System.Collections.Generic;
using Godot;
using Realm.Ecs;
using Realm.Client.Core;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Resources;
using Realm.Ecs.Components.Tags;
using Realm.Ecs.Replication;
using Realm.Shared.Network;

namespace Realm.Client.Services;

/// <summary>
///     Hosts the local stand-in for a unit the host describes: builds it when it is first described, applies a later
///     description to it, replaces it when its identifier was recycled, and stamps its first presentation sample.
/// </summary>
public class ReplicatedUnitLifecycle
{
	internal const float MaximumPlausibleTargetStepMeters = 2f;

	internal const float PlausibleStepAllowanceFactor = 2f;

	private readonly Func<Entity> _findWorldEntity;
	private readonly Func<int> _lastAppliedSequence;
	private readonly PendingUnitSpawnQueue _pendingSpawns;
	private readonly Action<Entity> _notePendingKill;
	private readonly Action<NetworkMappingState, Entity, int> _retireReplica;

	internal ReplicatedUnitLifecycle(Func<Entity> findWorldEntity, Func<int> lastAppliedSequence,
		PendingUnitSpawnQueue pendingSpawns, Action<Entity> notePendingKill,
		Action<NetworkMappingState, Entity, int> retireReplica)
	{
		_findWorldEntity = findWorldEntity;
		_lastAppliedSequence = lastAppliedSequence;
		_pendingSpawns = pendingSpawns;
		_notePendingKill = notePendingKill;
		_retireReplica = retireReplica;
	}

	internal Entity Spawn(UnitSnapshot snap, Func<string, bool, string> getFallbackModelPath, out string modelPath, out int playerIndex)
	{
		modelPath = string.Empty;
		playerIndex = 0;

		if (GameContext.World is not { } world) return Entity.Null;

		if (string.IsNullOrEmpty(snap.UnitId))
		{
			GD.PushError($"[Network] Cannot spawn replicated entity {snap.EntityId}: the description named no definition, and this screen holds no copy of it.");
			return Entity.Null;
		}

		if (!GameHost.UnitRegistry.TryGetValue(snap.UnitId, out var meta) && !GameHost.BuildingRegistry.TryGetValue(snap.UnitId, out meta))
		{
			GD.PushWarning($"[Network] Cannot spawn replicated entity {snap.EntityId}: unit id '{snap.UnitId}' is not registered on this client.");
			return Entity.Null;
		}

		Entity worldEntity = _findWorldEntity();
		if (worldEntity == Entity.Null || !world.Has<NetworkMappingState>(worldEntity)) return Entity.Null;

		ref var mapping = ref world.Get<NetworkMappingState>(worldEntity);
		if (mapping.ServerToClientEntityMap.TryGetValue(snap.EntityId, out Entity heldForThisIdentifier)
			&& world.IsAlive(heldForThisIdentifier)
			&& world.Has<GroundLoot>(heldForThisIdentifier))
		{
			ReplicatedLootLifecycle.Destroy(snap.EntityId, heldForThisIdentifier, mapping, world);
		}

		if (mapping.ServerToClientEntityMap.TryGetValue(snap.EntityId, out Entity alreadySpawned))
		{
			GD.PushWarning($"[Network] Replicated entity {snap.EntityId} ('{snap.UnitId}') is already local entity {alreadySpawned.Id} at sequence {_lastAppliedSequence()}; the duplicate spawn is ignored.");
			modelPath = string.Empty;
			return Entity.Null;
		}

		Entity spawned = ApplySnapshotToEntity(snap.UnitId, snap.EntityId, snap.OwnerPlayerEntityId, snap.Position.ToNumerics(), snap.RotationY, snap.CurrentHp, snap.MaxHp, snap.IsBuilding, meta, ref mapping, getFallbackModelPath, out modelPath, out playerIndex, snap.OwnerPlayerIndex);
		if (spawned != Entity.Null)
		{
			GameContext.SnapshotTimeline.Reset(spawned.Id);
			RecordSpawnSample(spawned.Id, snap.Position.ToNumerics(), snap.RotationY);
		}

		return spawned;
	}

	private void RecordSpawnSample(int localEntityId, System.Numerics.Vector3 position, float rotationY)
	{
		int anchorSequence = _lastAppliedSequence();
		float cursorSequence = GameContext.SnapshotTimeline.CursorSequence;
		if (cursorSequence >= 0f)
		{
			int drawnSequence = (int)MathF.Floor(cursorSequence);
			if (drawnSequence < anchorSequence) anchorSequence = drawnSequence;
		}

		GameContext.SnapshotTimeline.Record(localEntityId, position, rotationY, anchorSequence);
	}

	internal static Entity ApplySnapshotToEntity(string unitId, int entityId, int ownerPlayerEntityId, System.Numerics.Vector3 position, float rotationY, float currentHp, float maxHp, bool isBuilding, UnitMetadata meta, ref NetworkMappingState mapping, Func<string, bool, string> getFallbackModelPath, out string modelPath, out int playerIndex, int ownerPlayerIndex = -1)
	{
		modelPath = string.Empty;
		playerIndex = 0;
		if (GameContext.World is not { } world) return Entity.Null;

		Entity ownerPlayerEntity = mapping.PlayerEntities != null && mapping.PlayerEntities.Length > 1 ? mapping.PlayerEntities[1] : Entity.Null;
		if (ownerPlayerIndex >= 0 && mapping.PlayerEntities != null && ownerPlayerIndex < mapping.PlayerEntities.Length)
		{
			ownerPlayerEntity = mapping.PlayerEntities[ownerPlayerIndex];
		}
		else if (ownerPlayerEntityId >= 0 && mapping.PlayerEntities != null && ownerPlayerEntityId < mapping.PlayerEntities.Length)
		{
			ownerPlayerEntity = mapping.PlayerEntities[ownerPlayerEntityId];
		}

		if (ownerPlayerIndex < 0 && mapping.PeerIdToPlayerEntityMap.TryGetValue(ownerPlayerEntityId, out var pe))
		{
			ownerPlayerEntity = pe;
		}

		if (ownerPlayerEntity == Entity.Null && mapping.PlayerEntities != null && mapping.PlayerEntities.Length > 1)
		{
			ownerPlayerEntity = mapping.PlayerEntities[1];
		}

		playerIndex = ownerPlayerIndex >= 0
			? ownerPlayerIndex
			: ((ownerPlayerEntity != Entity.Null && world.Has<UnitOwnerPlayer>(ownerPlayerEntity))
				? world.Get<UnitOwnerPlayer>(ownerPlayerEntity).PlayerIndex
				: (ownerPlayerEntityId > 0 ? ownerPlayerEntityId - 1 : 0));

		var entity = world.Create();
		world.Add(entity, new DefinitionId(unitId));
		world.Add(entity, new Name(meta.Name));
		PositionWriteTrace.Mark(entity, PositionWriter.Spawn);
		world.Add(entity, new Position(position));
		world.Add(entity, new Owner(ownerPlayerEntity.AsPlayerEntity(world)));
		world.Add(entity, new UnitOwnerPlayer(playerIndex));
		world.Add(entity, new Health(currentHp, maxHp));
		if (meta.Damage > 0)
		{
			world.Add(entity, new Attack(meta.Damage, meta.Range, meta.AttackCooldown));
		}

		world.Add(entity, new Resistances(meta.Armor, meta.MagicResistance));
		if (isBuilding)
		{
			world.Add(entity, new Building());
		}
		else
		{
			world.Add(entity, new MovementStats(meta.Speed, 20f, 10f));
			world.Add(entity, new Movable());
			world.Add(entity, new Inventory());
			world.Add(entity, new Realm.Ecs.Components.Movement.PathingFlags(GameHost.GetUnitPathingFlags(meta)));
		}

		var interpolationTarget = new LatestDescription
		{
			Position = position,
			RotationY = rotationY
		};
		world.Add(entity, interpolationTarget);

		mapping.ServerToClientEntityMap[entityId] = entity;
		mapping.ClientToServerEntityMap[entity.Id] = entityId;
		Realm.Ecs.Replication.ReplicatedEntityReferences.Register(entityId, entity.Id);
		GameContext.NetworkService?.References.Refix(entityId);

		string targetModel = !string.IsNullOrEmpty(meta.ModelPath) ? meta.ModelPath : unitId;
		modelPath = getFallbackModelPath(targetModel, isBuilding);
		return entity;
	}

	internal static bool IsServerEntityIdRecycled(UnitSnapshot snap, Entity localEntity)
	{
		if (GameContext.World is not { } world) return true;

		// A description that names no definition says the unit is one this screen already knows, which cannot be a different
		// unit using a recycled identifier: that is what the definition in the description is for.
		if (snap.UnitId.Length > 0
		    && world.Has<DefinitionId>(localEntity)
		    && world.Get<DefinitionId>(localEntity).Value != snap.UnitId) return true;

		if (world.Has<UnitOwnerPlayer>(localEntity)
			&& snap.OwnerPlayerIndex >= 0
			&& world.Get<UnitOwnerPlayer>(localEntity).PlayerIndex != snap.OwnerPlayerIndex) return true;
		if (world.Has<Building>(localEntity) != snap.IsBuilding) return true;
		return world.Has<Dead>(localEntity);
	}
	internal void ApplyDescription(UnitSnapshot snap, int sequence, NetworkMappingState mapping)
	{
		if (GameContext.World is not { } world) return;

		if (!mapping.ServerToClientEntityMap.TryGetValue(snap.EntityId, out var localEntity))
		{
			if (snap.IsDead) _pendingSpawns.Remove(snap.EntityId);
			else _pendingSpawns.Queue(snap);
			return;
		}

		if (!world.IsAlive(localEntity) || IsServerEntityIdRecycled(snap, localEntity))
		{
			GameContext.SnapshotTimeline.Reset(localEntity.Id);
			_retireReplica(mapping, localEntity, snap.EntityId);
			if (!snap.IsDead) _pendingSpawns.Queue(snap);
			return;
		}

		if (snap.IsDead)
		{
			if (!world.Has<Dead>(localEntity))
			{
				world.Add<Dead>(localEntity);
				_notePendingKill(localEntity);
			}

			GameContext.SnapshotTimeline.Reset(localEntity.Id);
			return;
		}

		if (world.Has<Health>(localEntity))
		{
			var hp = world.Get<Health>(localEntity);
			hp.Current = snap.CurrentHp;
			hp.Max = snap.MaxHp;
			world.Set(localEntity, hp);
		}

		System.Numerics.Vector3 describedPosition = snap.Position.ToNumerics();
		System.Numerics.Vector3 previousTargetPosition = world.Has<LatestDescription>(localEntity)
			? world.Get<LatestDescription>(localEntity).Position
			: describedPosition;

		int lastDescribedSequence = world.Has<DescribedAtSequence>(localEntity)
			? world.Get<DescribedAtSequence>(localEntity).Sequence
			: -1;

		bool targetJumped = (describedPosition - previousTargetPosition).Length() > PlausibleStepMeters(localEntity, sequence, lastDescribedSequence);

		if (targetJumped) GameContext.SnapshotTimeline.Reset(localEntity.Id);
		GameContext.SnapshotTimeline.Record(localEntity.Id, describedPosition, snap.RotationY, sequence);

		world.SetOrAdd(localEntity, new LatestDescription
		{
			Position = describedPosition,
			RotationY = snap.RotationY
		});

		world.SetOrAdd(localEntity, new DescribedAtSequence(sequence));
	}

	private static float PlausibleStepMeters(Entity localEntity, int sequence, int lastDescribedSequence)
	{
		if (GameContext.World is not { } world) return MaximumPlausibleTargetStepMeters;
		if (!world.Has<MovementStats>(localEntity)) return MaximumPlausibleTargetStepMeters;

		int undescribedSequences = lastDescribedSequence >= 0 ? Math.Max(1, sequence - lastDescribedSequence) : 1;
		float coveredAtSpeed = world.Get<MovementStats>(localEntity).Speed
			* undescribedSequences
			* MatchTiming.SnapshotIntervalSeconds
			* PlausibleStepAllowanceFactor;

		return MathF.Max(MaximumPlausibleTargetStepMeters, coveredAtSpeed);
	}
}
