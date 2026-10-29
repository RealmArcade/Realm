using System;
using System.Collections.Generic;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Realm.Ecs;
using Godot;
using MemoryPack;
using Realm.Client.Core;
using Realm.Client.Network;
using Realm.Client.Testing;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Resources;
using Realm.Ecs.Components.Tags;
using Realm.Ecs.Components.Terrain;
using Realm.Ecs.Services;
using Realm.Client.UI;
using Realm.Ecs.Replication;
using Realm.Shared.Network;
using HttpClient = System.Net.Http.HttpClient;

namespace Realm.Client.Services;

public class NetworkService
{
	private readonly UnitVisibilityIndex _visibilityIndex = new();
	private readonly ReplicatedStateMirror _mirror = new();
	private int _appliedComponentDeltaCount;
	private int _appliedColdPayloadCount;
	private int _appliedAbilityStateCount;
	private int _appliedStaticTextCount;
	private int _appliedCatalogCount;
	private bool _fingerprintMatchLogged;

	private readonly Dictionary<int, UnitSnapshot> _pendingUnitSpawns = new();
	private readonly List<Entity> _pendingUnitKills = new();
	private readonly List<Entity> _pendingUnitDespawns = new();

	private readonly Dictionary<(int Key, int ComponentTypeId), PendingUnitComponentDelta> _pendingUnitComponentDeltas = new();
	private readonly List<(int Key, int ComponentTypeId)> _pendingUnitComponentDeltaKeys = new();

	private const int UnplacedUnitDeltaGraceSequences = 4;

	private struct PendingUnitComponentDelta
	{
		public ComponentDelta Delta;
		public int FirstSeenSequence;
	}
	private readonly List<int> _untrackedServerIds = new();
	private readonly HashSet<int> _describedLootServerIds = new();
	private readonly List<int> _staleLootServerIds = new();
	private readonly Dictionary<int, ulong> _baselineUnitPresence = new();
	private readonly HashSet<(int Key, int TypeId)> _baselineSentUnitComponents = new();
	private byte[] _baselineReconcileScratch = new byte[256];


	public bool IsPaused { get; set; } = false;
	public int LastAppliedSnapshotSequenceForDiagnostics { get; private set; } = -1;
	public int LastAppliedBaselineSequenceForDiagnostics { get; private set; } = -1;

	public int PausedByPlayerIndex { get; set; } = -1;

	public void RemoveClientToServerMapping(int localEntityId)
	{
		ForgetReferencesHeldBy(localEntityId);

		Entity worldEntity = GameContext.Simulation.WorldEntity;
		if (worldEntity == Entity.Null || !GameContext.World.Has<NetworkMappingState>(worldEntity)) return;

		var mapping = GameContext.World.Get<NetworkMappingState>(worldEntity);
		if (mapping.ClientToServerEntityMap.TryGetValue(localEntityId, out int serverEntityId))
		{
			mapping.ServerToClientEntityMap.Remove(serverEntityId);
			ReplicatedEntityReferences.Unregister(serverEntityId);
		}

		mapping.ClientToServerEntityMap.Remove(localEntityId);
	}


	public void Clear()
	{
		IsPaused = false;
		PausedByPlayerIndex = -1;
		_pendingUnitSpawns.Clear();
		_pendingUnitKills.Clear();
		_pendingUnitDespawns.Clear();
		_pendingUnitComponentDeltas.Clear();
		GameContext.SnapshotTimeline.Clear();
		_referrersByServerEntityId.Clear();
		_referencedServerEntityIds.Clear();
		_referencedComponentTypesByLocalEntity.Clear();
		_untrackedServerIds.Clear();
		_playerTeams.Clear();
		_alliances.Clear();
		ReplicatedEntityReferences.Clear();
	}

	public bool TryGetServerEntityId(int localEntityId, out int serverEntityId)
	{
		serverEntityId = -1;
		Entity worldEntity = FindWorldEntity();
		if (worldEntity == Entity.Null || !GameContext.World.Has<NetworkMappingState>(worldEntity)) return false;
		var mapping = GameContext.World.Get<NetworkMappingState>(worldEntity);
		return mapping.ClientToServerEntityMap.TryGetValue(localEntityId, out serverEntityId);
	}

	public int GetServerEntityId(int localEntityId)
	{
		Entity worldEntity = FindWorldEntity();
		if (worldEntity != Entity.Null && GameContext.World.Has<NetworkMappingState>(worldEntity))
		{
			var mapping = GameContext.World.Get<NetworkMappingState>(worldEntity);
			if (mapping.ClientToServerEntityMap.TryGetValue(localEntityId, out int serverId))
			{
				return serverId;
			}
		}

		return -1;
	}

	public Entity FindServerEntity(int entityId, List<Unit3D> allUnits)
	{
		foreach (var unit in allUnits)
		{
			if (unit.Entity.Id == entityId)
			{
				return unit.Entity;
			}
		}

		return Entity.Null;
	}

	

	public int GetOwnerPlayerIndex(Entity unitEntity)
	{
		if (unitEntity == Entity.Null || GameContext.World == null || !GameContext.World.IsAlive(unitEntity)) return -1;
		if (!GameContext.World.Has<UnitOwnerPlayer>(unitEntity)) return -1;
		return GameContext.World.Get<UnitOwnerPlayer>(unitEntity).PlayerIndex;
	}

	public int GetOwnerPeerId(Entity unitEntity)
	{
		Entity worldEntity = FindWorldEntity();
		if (worldEntity == Entity.Null || !GameContext.World.Has<NetworkMappingState>(worldEntity)) return -1;
		var mapping = GameContext.World.Get<NetworkMappingState>(worldEntity);

		if (GameContext.World.Has<UnitOwnerPlayer>(unitEntity))
		{
			int playerIndex = GameContext.World.Get<UnitOwnerPlayer>(unitEntity).PlayerIndex;
			var lobby = GameContext.Lobby;
			if (lobby != null)
			{
				var players = lobby.PlayerList;
				for (int i = 0; i < players.Count; i++)
				{
					if (players[i].Slot == playerIndex)
					{
						return players[i].PeerId;
					}
				}
			}
		}

		if (!GameContext.World.Has<Owner>(unitEntity)) return -1;
		var owner = GameContext.World.Get<Owner>(unitEntity).PlayerEntity;
		foreach (var kvp in mapping.PeerIdToPlayerEntityMap)
		{
			if (kvp.Value == owner.Value)
			{
				return kvp.Key;
			}
		}

		return -1;
	}

	

	public bool IsUnitVisibleToPlayer(Entity playerEntity, Entity unitEntity)
	{
		if (IsShroudRevealed())
		{
			return true;
		}

		EcsWorld? world = GameContext.World;
		if (world == null || !world.IsAlive(unitEntity)) return false;

		if (world.Has<Owner>(unitEntity) && world.Get<Owner>(unitEntity).PlayerEntity.Value == playerEntity)
		{
			return true;
		}

		if (!world.Has<Position>(unitEntity)) return false;
		System.Numerics.Vector3 unitPosition = world.Get<Position>(unitEntity).Value;
		return _visibilityIndex.IsVisibleToPlayer(playerEntity.Id, unitPosition);
	}

	public bool IsShroudRevealed()
	{
		if (GameContext.World == null) return false;
		Entity worldEntity = FindWorldEntity();
		if (worldEntity == Entity.Null || !GameContext.World.IsAlive(worldEntity)) return false;
		if (!GameContext.World.Has<ShroudState>(worldEntity)) return false;
		return GameContext.World.Get<ShroudState>(worldEntity).VisibilityMode == ShroudVisibilityMode.Revealed;
	}

	public bool IsServerEntityVisibleToEveryRemoteClient(int serverEntityId)
	{
		GameHost? gameHost = GameContext.Host;
		if (gameHost == null) return true;

		LobbyManager? lobby = GameContext.Lobby;
		if (lobby == null) return true;

		Entity worldEntity = FindWorldEntity();
		if (worldEntity == Entity.Null || !GameContext.World.Has<NetworkMappingState>(worldEntity)) return true;

		var mapping = GameContext.World.Get<NetworkMappingState>(worldEntity);
		Entity unitEntity = new(serverEntityId);
		int localPeerId = gameHost.Multiplayer.GetUniqueId();
		int remoteClientCount = 0;

		foreach (PlayerInfo player in lobby.PlayerList)
		{
			if (player.PeerId <= 0 || player.PeerId == localPeerId) continue;
			if (player.IsDisconnected) continue;
			if (!mapping.PeerIdToPlayerEntityMap.TryGetValue(player.PeerId, out Entity playerEntity)) continue;

			remoteClientCount++;
			if (!IsUnitVisibleToPlayer(playerEntity, unitEntity)) return false;
		}

		return remoteClientCount > 0;
	}

	private bool TryChargeBuildingCost(Entity playerEntity, UnitMetadata meta)
	{
		if (!GameContext.World.TryGet<PlayerResources>(playerEntity, out var res)) return false;

		int costGold = (int)meta.CostGold;
		int costWood = (int)meta.CostWood;
		int costStone = (int)meta.CostStone;

		if (res.Gold < costGold || res.Wood < costWood || res.Stone < costStone) return false;

		res.Gold -= costGold;
		res.Wood -= costWood;
		res.Stone -= costStone;
		GameContext.World.Set(playerEntity, res);
		return true;
	}

	public void UpdateClientTickDomain()
	{
		VerifyReplicatedEcsDriftDomain();
	}

	public (int CommandId, byte[] Payload) BuildClientCommand(string commandType, List<int> unitIds, System.Numerics.Vector3 targetPos, int targetEntityId, string argString)
	{
		Entity worldEntity = FindWorldEntity();
		if (worldEntity == Entity.Null || !GameContext.World.Has<NetworkState>(worldEntity)) return (0, Array.Empty<byte>());

		int commandId = GameContext.World.Get<NetworkState>(worldEntity).NextCommandId;
		if (commandId <= 0) return (0, Array.Empty<byte>());

		ref var ns = ref GameContext.World.Get<NetworkState>(worldEntity);
		ns.NextCommandId++;

		var cmd = new NetworkCommand
		{
			CommandId = commandId,
			CommandType = commandType,
			UnitEntityIds = unitIds,
			TargetPosition = new NetworkVector3(targetPos.X, targetPos.Y, targetPos.Z),
			TargetEntityId = targetEntityId,
			ArgString = argString
		};
		return (cmd.CommandId, MemoryPackSerializer.Serialize(cmd));
	}

	public void ApplySnapshotToWorld(WorldSnapshot snapshot)
	{
		Entity worldEntity = FindWorldEntity();
		if (worldEntity == Entity.Null || !GameContext.World.Has<NetworkMappingState>(worldEntity)) return;
		var mapping = GameContext.World.Get<NetworkMappingState>(worldEntity);

		MultiplayerSyncDiagnostics.RecordClientSnapshotUnitPresence(snapshot.Sequence, snapshot.Units);

		ApplyReplicatedColdPayloads(snapshot);

		if (snapshot.IsBaseline)
		{
			_pendingUnitComponentDeltas.Clear();

			ProcessBaselineSnapshotMismatches(snapshot, mapping);
			ProcessBaselineComponentReconciliation(snapshot, mapping);
		}

		ApplyLootState(snapshot, mapping);

		LastAppliedSnapshotSequenceForDiagnostics = snapshot.Sequence;
		GameContext.SnapshotTimeline.AdvanceTo(snapshot.Sequence);

		RetireUndescribedUnits(snapshot, mapping);

		foreach (var snap in snapshot.Units)
		{
			ApplySnapshotToUnit(snap, snapshot.Sequence, mapping);
		}

		bool componentsFullyApplied = ApplyReplicatedComponentState(snapshot);
		ApplyPendingUnitComponentDeltas(snapshot.Sequence);

		if (snapshot.IsBaseline) LastAppliedBaselineSequenceForDiagnostics = snapshot.Sequence;
		if (snapshot.IsBaseline || GameContext.World.Get<NetworkState>(worldEntity).HasReceivedInitialBaseline)
		{
			MultiplayerSyncDiagnostics.RecordClientSnapshot(snapshot.IsBaseline);
		}
		GameContext.SnapshotApply.Acknowledge(snapshot.Sequence, componentsFullyApplied);
	}

	private void RetireUndescribedUnits(WorldSnapshot snapshot, NetworkMappingState mapping)
	{
		if (snapshot.RetiredEntityIds == null || snapshot.RetiredEntityIds.Count == 0) return;

		for (int index = 0; index < snapshot.RetiredEntityIds.Count; index++)
		{
			int serverEntityId = snapshot.RetiredEntityIds[index];

			_pendingUnitSpawns.Remove(serverEntityId);

			if (!mapping.ServerToClientEntityMap.TryGetValue(serverEntityId, out Entity localEntity)) continue;

			RetireClientEntityForUntrackedServerId(mapping, localEntity, serverEntityId);
		}
	}

	private void ProcessBaselineSnapshotMismatches(WorldSnapshot snapshot, NetworkMappingState mapping)
	{
		var serverIdsInSnapshot = new HashSet<int>(snapshot.Units.Count);
		for (int i = 0; i < snapshot.Units.Count; i++)
		{
			serverIdsInSnapshot.Add(snapshot.Units[i].EntityId);
		}

		_untrackedServerIds.Clear();
		foreach (var kvp in mapping.ServerToClientEntityMap)
		{
			if (serverIdsInSnapshot.Contains(kvp.Key)) continue;			_untrackedServerIds.Add(kvp.Key);
		}

		for (int index = 0; index < _untrackedServerIds.Count; index++)
		{
			int serverEntityId = _untrackedServerIds[index];
			if (!mapping.ServerToClientEntityMap.TryGetValue(serverEntityId, out var localEntity)) continue;
			if (GameContext.World.Has<GroundLoot>(localEntity)) continue;

			RetireClientEntityForUntrackedServerId(mapping, localEntity, serverEntityId);
		}
	}

	private void ProcessBaselineComponentReconciliation(WorldSnapshot snapshot, NetworkMappingState mapping)
	{
		if (snapshot.ComponentDeltas == null || snapshot.Units == null || snapshot.UnitComponentPresence == null) return;

		_baselineUnitPresence.Clear();
		for (int index = 0; index < snapshot.UnitComponentPresence.Count; index++)
		{
			var presence = snapshot.UnitComponentPresence[index];
			_baselineUnitPresence[presence.ServerEntityId] = presence.Mask;
		}

		_baselineSentUnitComponents.Clear();
		for (int index = 0; index < snapshot.ComponentDeltas.Count; index++)
		{
			var delta = snapshot.ComponentDeltas[index];
			if ((ReplicatedScope)delta.Scope != ReplicatedScope.Unit) continue;
			if (delta.Bytes == null || delta.Bytes.Length == 0) continue;
			_baselineSentUnitComponents.Add((delta.Key, delta.ComponentTypeId));
		}

		IReadOnlyList<IReplicatedComponentCodec> codecs = ReplicatedUnitComponentSlots.Slots;
		foreach (var kvp in mapping.ServerToClientEntityMap)
		{
			int serverEntityId = kvp.Key;
			if (!_baselineUnitPresence.TryGetValue(serverEntityId, out ulong presenceMask))
			{
				if (GameContext.World.IsAlive(kvp.Value) && !GameContext.World.Has<GroundLoot>(kvp.Value))
				{
					MultiplayerSyncDiagnostics.RecordUnreconciledUnit(serverEntityId);
				}

				continue;
			}

			Entity localEntity = kvp.Value;
			if (!GameContext.World.IsAlive(localEntity)) continue;

			for (int slot = 0; slot < codecs.Count; slot++)
			{
				var codec = codecs[slot];
				if (_baselineReconcileScratch.Length < codec.SizeInBytes) _baselineReconcileScratch = new byte[codec.SizeInBytes];

				bool peerHasComponent = codec.TryWrite(GameContext.World, localEntity, _baselineReconcileScratch.AsSpan(0, codec.SizeInBytes));
				if ((presenceMask & (1UL << slot)) != 0)
				{
					if (!peerHasComponent && !_baselineSentUnitComponents.Contains((serverEntityId, codec.ComponentTypeId)))
					{
						MultiplayerSyncDiagnostics.RecordMissingUnitComponent(serverEntityId, codec.ComponentName);
					}

					continue;
				}

				if (!peerHasComponent) continue;

				codec.Remove(GameContext.World, localEntity);
				MultiplayerSyncDiagnostics.RecordRemovedUnitComponent(serverEntityId, codec.ComponentName);
				ForgetReplicatedReference(localEntity.Id, codec.ComponentTypeId);
			}
		}
	}

	private void ApplyLootState(WorldSnapshot snapshot, NetworkMappingState mapping)
	{
		if (snapshot.Loot == null) return;

		_describedLootServerIds.Clear();
		for (int index = 0; index < snapshot.Loot.Count; index++)
		{
			LootSnapshot entry = snapshot.Loot[index];
			_describedLootServerIds.Add(entry.EntityId);
			ApplyLootEntry(in entry, mapping);
		}

		_staleLootServerIds.Clear();
		foreach (var kvp in mapping.ServerToClientEntityMap)
		{
			if (_describedLootServerIds.Contains(kvp.Key)) continue;
			if (!GameContext.World.Has<GroundLoot>(kvp.Value)) continue;
			_staleLootServerIds.Add(kvp.Key);
		}

		for (int index = 0; index < _staleLootServerIds.Count; index++)
		{
			int serverEntityId = _staleLootServerIds[index];
			if (!mapping.ServerToClientEntityMap.TryGetValue(serverEntityId, out Entity localEntity)) continue;
			DestroyReplicatedLootEntity(serverEntityId, localEntity, mapping);
		}
	}

	private static void ApplyLootEntry(in LootSnapshot entry, NetworkMappingState mapping)
	{
		var position = new System.Numerics.Vector3(entry.Position.X, entry.Position.Y, entry.Position.Z);			if (mapping.ServerToClientEntityMap.TryGetValue(entry.EntityId, out Entity localEntity)
				&& GameContext.World.IsAlive(localEntity)
				&& GameContext.World.Has<GroundLoot>(localEntity))
		{
			GroundLoot loot = GameContext.World.Get<GroundLoot>(localEntity);
			loot.UniqueId = entry.EntityId;
			loot.ItemTypeId = entry.ItemTypeId;
			loot.Charges = entry.Charges;
			loot.Lifetime = entry.Lifetime;
			loot.RemainingLifetime = entry.RemainingLifetime;
			loot.PickupRadius = entry.PickupRadius;
			loot.AutoPickup = entry.AutoPickup;
			loot.OwnerPlayerIndex = entry.OwnerPlayerIndex;

			GameContext.World.Set(localEntity, loot);
			GameContext.World.Set(localEntity, new Position(position));
			GameContext.Host?.SyncReplicatedLootNode(entry.EntityId, localEntity);
			return;
		}

		GameContext.NetworkService?.RetireUnitReplicaForReusedServerId(mapping, entry.EntityId);
		localEntity = CreateReplicatedLootEntity(entry.EntityId, mapping);
		var described = new GroundLoot(entry.EntityId, entry.ItemTypeId, entry.Charges, entry.Lifetime, entry.PickupRadius, entry.AutoPickup, entry.OwnerPlayerIndex)
		{
			RemainingLifetime = entry.RemainingLifetime
		};

		GameContext.World.Set(localEntity, new Position(position));
		GameContext.World.Set(localEntity, described);
		GameContext.Host?.SyncReplicatedLootNode(entry.EntityId, localEntity);
	}

	public void RetireUnitReplicaForReusedServerId(NetworkMappingState mapping, int serverEntityId)
	{
		if (!mapping.ServerToClientEntityMap.TryGetValue(serverEntityId, out Entity localEntity)) return;
		if (!GameContext.World.IsAlive(localEntity)) return;
		if (GameContext.World.Has<GroundLoot>(localEntity)) return;

		RetireClientEntityForUntrackedServerId(mapping, localEntity, serverEntityId);
	}

	private void RetireClientEntityForUntrackedServerId(NetworkMappingState mapping, Entity localEntity, int serverEntityId)
	{
		if (GameContext.World.IsAlive(localEntity) && !GameContext.World.Has<Dead>(localEntity))
		{
			_pendingUnitDespawns.Add(localEntity);
		}

		ForgetReferencesHeldBy(localEntity.Id);
		GameContext.SnapshotTimeline.Reset(localEntity.Id);
		mapping.ServerToClientEntityMap.Remove(serverEntityId);
		mapping.ClientToServerEntityMap.Remove(localEntity.Id);
		ReplicatedEntityReferences.Unregister(serverEntityId);
	}

	private void ApplySnapshotToUnit(UnitSnapshot snap, int sequence, NetworkMappingState mapping)
	{
		if (!mapping.ServerToClientEntityMap.TryGetValue(snap.EntityId, out var localEntity))
		{
			if (snap.IsDead) _pendingUnitSpawns.Remove(snap.EntityId);
			else _pendingUnitSpawns[snap.EntityId] = snap;
			return;
		}

		if (!GameContext.World.IsAlive(localEntity) || IsServerEntityIdRecycled(snap, localEntity))
		{
			GameContext.SnapshotTimeline.Reset(localEntity.Id);
			RetireClientEntityForRecycledServerId(mapping, localEntity, snap.EntityId);
			if (!snap.IsDead) _pendingUnitSpawns[snap.EntityId] = snap;
			return;
		}

		if (snap.IsDead)
		{
			if (!GameContext.World.Has<Dead>(localEntity))
			{
				GameContext.World.Add<Dead>(localEntity);
				_pendingUnitKills.Add(localEntity);
			}

			GameContext.SnapshotTimeline.Reset(localEntity.Id);
			return;
		}

		if (GameContext.World.Has<Health>(localEntity))
		{
			var hp = GameContext.World.Get<Health>(localEntity);
			hp.Current = snap.CurrentHp;
			hp.Max = snap.MaxHp;
			GameContext.World.Set(localEntity, hp);
		}

		System.Numerics.Vector3 describedPosition = snap.Position.ToNumerics();
		System.Numerics.Vector3 previousTargetPosition = GameContext.World.Has<LatestDescription>(localEntity)
			? GameContext.World.Get<LatestDescription>(localEntity).Position
			: describedPosition;

		int lastDescribedSequence = GameContext.World.Has<DescribedAtSequence>(localEntity)
			? GameContext.World.Get<DescribedAtSequence>(localEntity).Sequence
			: -1;
		bool descriptionResumed = lastDescribedSequence >= 0 && sequence - lastDescribedSequence > UnobservedUnitGapSequences;

		bool targetJumped = (describedPosition - previousTargetPosition).Length() > PlausibleStepMeters(localEntity, sequence, lastDescribedSequence);

		if (targetJumped || descriptionResumed) GameContext.SnapshotTimeline.Reset(localEntity.Id);
		GameContext.SnapshotTimeline.Record(localEntity.Id, describedPosition, snap.RotationY, sequence);

		GameContext.World.SetOrAdd(localEntity, new LatestDescription
		{
			Position = describedPosition,
			RotationY = snap.RotationY
		});

		GameContext.World.SetOrAdd(localEntity, new DescribedAtSequence(sequence));
	}

	public const int UnobservedUnitGapSequences = 10;

	public const float MaximumPlausibleTargetStepMeters = 2f;

	public const float PlausibleStepAllowanceFactor = 2f;

	private static float PlausibleStepMeters(Entity localEntity, int sequence, int lastDescribedSequence)
	{
		if (!GameContext.World.Has<MovementStats>(localEntity)) return MaximumPlausibleTargetStepMeters;

		int undescribedSequences = lastDescribedSequence >= 0 ? Math.Max(1, sequence - lastDescribedSequence) : 1;
		float coveredAtSpeed = GameContext.World.Get<MovementStats>(localEntity).Speed
			* undescribedSequences
			* MatchTiming.SnapshotIntervalSeconds
			* PlausibleStepAllowanceFactor;

		return MathF.Max(MaximumPlausibleTargetStepMeters, coveredAtSpeed);
	}

	private static bool IsServerEntityIdRecycled(UnitSnapshot snap, Entity localEntity)
	{
		if (GameContext.World.Has<DefinitionId>(localEntity) && GameContext.World.Get<DefinitionId>(localEntity).Value != snap.UnitId) return true;
		if (GameContext.World.Has<UnitOwnerPlayer>(localEntity)
			&& snap.OwnerPlayerIndex >= 0
			&& GameContext.World.Get<UnitOwnerPlayer>(localEntity).PlayerIndex != snap.OwnerPlayerIndex) return true;
		if (GameContext.World.Has<Building>(localEntity) != snap.IsBuilding) return true;
		return GameContext.World.Has<Dead>(localEntity);
	}

	private void RetireClientEntityForRecycledServerId(NetworkMappingState mapping, Entity localEntity, int serverEntityId)
	{
		RetireClientEntityForUntrackedServerId(mapping, localEntity, serverEntityId);
	}

	public bool HasPendingUnitSpawns => _pendingUnitSpawns.Count > 0;

	public bool TryTakePendingUnitSpawn(out UnitSnapshot snapshot)
	{
		snapshot = default;
		if (_pendingUnitSpawns.Count == 0) return false;

		int serverEntityId = 0;
		foreach (int candidate in _pendingUnitSpawns.Keys)
		{
			serverEntityId = candidate;
			break;
		}

		return _pendingUnitSpawns.Remove(serverEntityId, out snapshot);
	}

	public IReadOnlyList<Entity> FlushPendingUnitKills() => TakePending(_pendingUnitKills);

	public IReadOnlyList<Entity> FlushPendingUnitDespawns() => TakePending(_pendingUnitDespawns);

	private static IReadOnlyList<Entity> TakePending(List<Entity> pending)
	{
		if (pending.Count == 0) return Array.Empty<Entity>();

		var result = new List<Entity>(pending);
		pending.Clear();
		return result;
	}

	public Entity SpawnUnitFromSnapshot(UnitSnapshot snap, Func<string, bool, string> getFallbackModelPath, out string modelPath, out int playerIndex)
	{
		modelPath = string.Empty;
		playerIndex = 0;

		if (!GameHost.UnitRegistry.TryGetValue(snap.UnitId, out var meta) && !GameHost.BuildingRegistry.TryGetValue(snap.UnitId, out meta))
		{
			GD.PushWarning($"[Network] Cannot spawn replicated entity {snap.EntityId}: unit id '{snap.UnitId}' is not registered on this client.");
			return Entity.Null;
		}

		Entity worldEntity = FindWorldEntity();
		if (worldEntity == Entity.Null || !GameContext.World.Has<NetworkMappingState>(worldEntity)) return Entity.Null;

		ref var mapping = ref GameContext.World.Get<NetworkMappingState>(worldEntity);
		if (mapping.ServerToClientEntityMap.TryGetValue(snap.EntityId, out Entity heldForThisIdentifier)
			&& GameContext.World.IsAlive(heldForThisIdentifier)
			&& GameContext.World.Has<GroundLoot>(heldForThisIdentifier))
		{
			DestroyReplicatedLootEntity(snap.EntityId, heldForThisIdentifier, mapping);
		}

		if (mapping.ServerToClientEntityMap.TryGetValue(snap.EntityId, out Entity alreadySpawned))
		{
			GD.PushWarning($"[Network] Replicated entity {snap.EntityId} ('{snap.UnitId}') is already local entity {alreadySpawned.Id} at sequence {LastAppliedSnapshotSequenceForDiagnostics}; the duplicate spawn is ignored.");
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
		int anchorSequence = LastAppliedSnapshotSequenceForDiagnostics;
		float cursorSequence = GameContext.SnapshotTimeline.CursorSequence;
		if (cursorSequence >= 0f)
		{
			int drawnSequence = (int)MathF.Floor(cursorSequence);
			if (drawnSequence < anchorSequence) anchorSequence = drawnSequence;
		}

		GameContext.SnapshotTimeline.Record(localEntityId, position, rotationY, anchorSequence);
	}

	public static Entity ApplySnapshotToEntity(string unitId, int entityId, int ownerPlayerEntityId, System.Numerics.Vector3 position, float rotationY, float currentHp, float maxHp, bool isBuilding, UnitMetadata meta, ref NetworkMappingState mapping, Func<string, bool, string> getFallbackModelPath, out string modelPath, out int playerIndex, int ownerPlayerIndex = -1)
	{
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
			: ((ownerPlayerEntity != Entity.Null && GameContext.World.Has<UnitOwnerPlayer>(ownerPlayerEntity))
				? GameContext.World.Get<UnitOwnerPlayer>(ownerPlayerEntity).PlayerIndex
				: (ownerPlayerEntityId > 0 ? ownerPlayerEntityId - 1 : 0));

		var entity = GameContext.World.Create();
		GameContext.World.Add(entity, new DefinitionId(unitId));
		GameContext.World.Add(entity, new Name(meta.Name));
		PositionWriteTrace.Mark(entity, PositionWriter.Spawn);
		GameContext.World.Add(entity, new Position(position));
		GameContext.World.Add(entity, new Owner(ownerPlayerEntity.AsPlayerEntity(GameContext.World)));
		GameContext.World.Add(entity, new UnitOwnerPlayer(playerIndex));
		GameContext.World.Add(entity, new Health(currentHp, maxHp));
		if (meta.Damage > 0)
		{
			GameContext.World.Add(entity, new Attack(meta.Damage, meta.Range, meta.AttackCooldown));
		}

		GameContext.World.Add(entity, new Resistances(meta.Armor, meta.MagicResistance));
		if (isBuilding)
		{
			GameContext.World.Add(entity, new Building());
		}
		else
		{
			GameContext.World.Add(entity, new MovementStats(meta.Speed, 20f, 10f));
			GameContext.World.Add(entity, new Movable());
			GameContext.World.Add(entity, new Inventory());
			GameContext.World.Add(entity, new Realm.Ecs.Components.Movement.PathingFlags(GameHost.GetUnitPathingFlags(meta)));
		}

		var interpolationTarget = new LatestDescription
		{
			Position = position,
			RotationY = rotationY
		};
		GameContext.World.Add(entity, interpolationTarget);

		mapping.ServerToClientEntityMap[entityId] = entity;
		mapping.ClientToServerEntityMap[entity.Id] = entityId;
		Realm.Ecs.Replication.ReplicatedEntityReferences.Register(entityId, entity.Id);
		GameContext.NetworkService?.RefixReplicatedReferences(entityId);

		string targetModel = !string.IsNullOrEmpty(meta.ModelPath) ? meta.ModelPath : unitId;
		modelPath = getFallbackModelPath(targetModel, isBuilding);
		return entity;
	}

	private void ApplyReplicatedColdPayloads(WorldSnapshot snapshot)
	{
		if (snapshot.ColdPayloads == null) return;

		for (int i = 0; i < snapshot.ColdPayloads.Count; i++)
		{
			ApplyColdPayloadDelta(snapshot.ColdPayloads[i]);
		}
	}



	private bool ApplyReplicatedComponentState(WorldSnapshot snapshot)
	{
		bool allApplied = true;
		if (snapshot.ComponentDeltas != null)
		{
			for (int i = 0; i < snapshot.ComponentDeltas.Count; i++)
			{
				if (!ApplyComponentDelta(snapshot.ComponentDeltas[i], snapshot.Sequence)) allApplied = false;
			}
		}

		if (snapshot.AbilityStates != null)
		{
			for (int i = 0; i < snapshot.AbilityStates.Count; i++)
			{
				ApplyAbilityStateDelta(snapshot.AbilityStates[i]);
			}
			GameContext.RegistryService?.ClearAbilityStateDirty();
		}

		if (snapshot.StaticTexts != null)
		{
			for (int i = 0; i < snapshot.StaticTexts.Count; i++)
			{
				ApplyStaticTextDelta(snapshot.StaticTexts[i]);
			}
		}

		if (snapshot.Catalog != null)
		{
			var registry = GameContext.RegistryService;
			if (registry != null)
			{
				for (int i = 0; i < snapshot.Catalog.Count; i++)
				{
					var entry = snapshot.Catalog[i];
					registry.ApplyCatalogEntry(entry.Channel, entry.Key, entry.Value, entry.Number);
					_appliedCatalogCount++;
				}
			}
		}

		if (snapshot.IsBaseline && snapshot.StateFingerprint != 0u)
		{
			VerifyReplicatedStateFingerprint(snapshot.Sequence, snapshot.StateFingerprint);
		}

		return allApplied;
	}

	private void ApplyPendingUnitComponentDeltas(int sequence)
	{
		if (_pendingUnitComponentDeltas.Count == 0) return;

		_pendingUnitComponentDeltaKeys.Clear();
		foreach ((int Key, int ComponentTypeId) key in _pendingUnitComponentDeltas.Keys) _pendingUnitComponentDeltaKeys.Add(key);

		bool anUnplaceableChangeWasDropped = false;
		for (int index = 0; index < _pendingUnitComponentDeltaKeys.Count; index++)
		{
			(int Key, int ComponentTypeId) key = _pendingUnitComponentDeltaKeys[index];
			if (!_pendingUnitComponentDeltas.TryGetValue(key, out PendingUnitComponentDelta pending)) continue;

			if (TryGetLocalEntity(key.Key, out _))
			{
				_pendingUnitComponentDeltas.Remove(key);
				ApplyComponentDelta(pending.Delta, sequence);
				continue;
			}

			if (sequence - pending.FirstSeenSequence < UnplacedUnitDeltaGraceSequences) continue;

			_pendingUnitComponentDeltas.Remove(key);
			anUnplaceableChangeWasDropped = true;
		}

		if (anUnplaceableChangeWasDropped) GameContext.SnapshotApply.RequestBaseline();
	}

	private void ApplyColdPayloadDelta(ColdPayloadDelta delta)
	{
		_mirror.RecordColdPayload(delta);
		if (delta.Destroyed != 0) return;

		switch ((ColdPayloadKind)delta.Kind)
		{
			case ColdPayloadKind.ContentIdPayload:
			{
				int registeredContentId = ContentIdStore.GetOrAddBytes(delta.ContentKind, delta.Bytes);
				if (registeredContentId != delta.ContentId)
				{
					GD.PushWarning($"[Network] Cold payload content id {delta.ContentId} registered as {registeredContentId}; the payload bytes disagree with the sender's handle.");
				}

				break;
			}
			case ColdPayloadKind.LeaderboardBoards:
			{
				ColdPayloadCodecs.ApplyLeaderboards(GameContext.World, FindWorldEntity(), delta.Bytes);
				GameContext.Host?.RefreshReplicatedOverlayUi();
				break;
			}
			case ColdPayloadKind.SummaryTable:
			{
				ColdPayloadCodecs.ApplySummaryTable(GameContext.World, FindWorldEntity(), delta.Bytes);
				GameContext.Host?.RefreshReplicatedOverlayUi();
				break;
			}
		}

		_appliedColdPayloadCount++;
	}

	private bool ApplyComponentDelta(ComponentDelta delta, int snapshotSequence)
	{
		var codec = ReplicatedComponents.Find(delta.ComponentTypeId);
		if (codec == null)
		{
			GD.PushWarning($"[Network] Received delta for unregistered component type {delta.ComponentTypeId}");
			return false;
		}

		if (delta.Bytes == null || (delta.Bytes.Length != 0 && delta.Bytes.Length != codec.SizeInBytes))
		{
			GD.PushWarning($"[Network] Discarded {codec.ComponentName} delta with {(delta.Bytes?.Length ?? 0)} bytes, expected {codec.SizeInBytes}");
			return false;
		}

		Entity targetEntity = Entity.Null;
		if ((ReplicatedScope)delta.Scope == ReplicatedScope.Player)
		{
			Entity worldEntity = FindWorldEntity();
			if (worldEntity == Entity.Null || !GameContext.World.Has<NetworkMappingState>(worldEntity)) return false;
			var mapping = GameContext.World.Get<NetworkMappingState>(worldEntity);
			if (mapping.PlayerEntities == null || delta.Key < 0 || delta.Key >= mapping.PlayerEntities.Length) return false;
			targetEntity = mapping.PlayerEntities[delta.Key];
		}
		else if ((ReplicatedScope)delta.Scope == ReplicatedScope.Unit)
		{
			if (!TryGetLocalEntity(delta.Key, out targetEntity))
			{
				if (codec.ComponentTypeId != ComponentType<GroundLoot>.Id || delta.Bytes.Length == 0)
				{
					if (!_pendingUnitComponentDeltas.ContainsKey((delta.Key, delta.ComponentTypeId)))
					{
						_pendingUnitComponentDeltas[(delta.Key, delta.ComponentTypeId)] = new PendingUnitComponentDelta
						{
							Delta = delta,
							FirstSeenSequence = snapshotSequence
						};
					}

					return false;
				}
				Entity worldEntity = FindWorldEntity();
				if (worldEntity == Entity.Null || !GameContext.World.Has<NetworkMappingState>(worldEntity)) return false;
				targetEntity = CreateReplicatedLootEntity(delta.Key, GameContext.World.Get<NetworkMappingState>(worldEntity));
			}
		}
		else if ((ReplicatedScope)delta.Scope == ReplicatedScope.World)
		{
			targetEntity = GameContext.Simulation.WorldEntity;
		}

		if (targetEntity == Entity.Null || !GameContext.World.IsAlive(targetEntity)) return false;
		if (delta.Bytes.Length == 0)
		{
			codec.Remove(GameContext.World, targetEntity);

			if (codec.ComponentTypeId == ComponentType<GroundLoot>.Id)
			{
				Entity worldEntity = FindWorldEntity();
				if (worldEntity != Entity.Null && GameContext.World.Has<NetworkMappingState>(worldEntity))
				{
					DestroyReplicatedLootEntity(delta.Key, targetEntity, GameContext.World.Get<NetworkMappingState>(worldEntity));
				}
			}
		}
		else
		{
			codec.Apply(GameContext.World, targetEntity, delta.Bytes);

			if (codec.ComponentTypeId == ComponentType<MoveTo>.Id && GameContext.World.Has<PreviewMoveTo>(targetEntity))
			{
				GameContext.World.Remove<PreviewMoveTo>(targetEntity);
			}

			if (codec.ComponentTypeId == ComponentType<GroundLoot>.Id) GameContext.Host?.SyncReplicatedLootNode(delta.Key, targetEntity);
		}

		RecordReplicatedReference(delta, targetEntity, codec);

		_mirror.RecordComponent(delta);
		_appliedComponentDeltaCount++;
		if (delta.ComponentTypeId == ComponentType<AbilityState>.Id && (ReplicatedScope)delta.Scope == ReplicatedScope.Unit)
		{
			GameContext.RegistryService?.ClearAbilityStateForUnit(new UnitId(targetEntity.Id));
			_mirror.RemoveAbilitiesForEntity(delta.Key);
		}
		return true;
	}

	private void RecordReplicatedReference(ComponentDelta delta, Entity referrer, IReplicatedComponentCodec codec)
	{
		if (codec is not INormalizingComponentCodec normalizing) return;

		if (delta.Bytes == null || delta.Bytes.Length == 0)
		{
			ForgetReplicatedReference(referrer.Id, codec.ComponentTypeId);
			return;
		}

		int referencedServerEntityId = normalizing.ReadReferencedServerEntityId(delta.Bytes);
		var referenceKey = (referrer.Id, codec.ComponentTypeId);
		if (_referencedServerEntityIds.TryGetValue(referenceKey, out int previousServerEntityId))
		{
			if (previousServerEntityId == referencedServerEntityId) return;
			ForgetReplicatedReference(referrer.Id, codec.ComponentTypeId);
		}

		if (referencedServerEntityId < 0) return;

		if (!_referrersByServerEntityId.TryGetValue(referencedServerEntityId, out var referrers))
		{
			referrers = new List<(int LocalEntityId, IReplicatedComponentCodec Codec)>(4);
			_referrersByServerEntityId[referencedServerEntityId] = referrers;
		}

		referrers.Add((referrer.Id, codec));
		_referencedServerEntityIds[referenceKey] = referencedServerEntityId;

		if (!_referencedComponentTypesByLocalEntity.TryGetValue(referrer.Id, out List<int>? componentTypeIds))
		{
			componentTypeIds = new List<int>(2);
			_referencedComponentTypesByLocalEntity[referrer.Id] = componentTypeIds;
		}

		if (!componentTypeIds.Contains(codec.ComponentTypeId)) componentTypeIds.Add(codec.ComponentTypeId);
	}

	public bool IsReferenceUnresolved(int localEntityId, int componentTypeId)
	{
		if (!_referencedServerEntityIds.TryGetValue((localEntityId, componentTypeId), out int serverEntityId)) return false;

		return ReplicatedEntityReferences.Resolve(new Entity(serverEntityId)) == Entity.Null;
	}

	public void ForgetReferencesHeldBy(int localEntityId)
	{
		if (!_referencedComponentTypesByLocalEntity.Remove(localEntityId, out List<int>? componentTypeIds)) return;

		for (int index = 0; index < componentTypeIds.Count; index++) ForgetReplicatedReference(localEntityId, componentTypeIds[index]);
	}

	private void ForgetReplicatedReference(int localEntityId, int componentTypeId)
	{
		var referenceKey = (localEntityId, componentTypeId);

		if (_referencedComponentTypesByLocalEntity.TryGetValue(localEntityId, out List<int>? heldComponentTypeIds))
		{
			heldComponentTypeIds.Remove(componentTypeId);
			if (heldComponentTypeIds.Count == 0) _referencedComponentTypesByLocalEntity.Remove(localEntityId);
		}

		if (!_referencedServerEntityIds.TryGetValue(referenceKey, out int serverEntityId)) return;

		_referencedServerEntityIds.Remove(referenceKey);
		if (!_referrersByServerEntityId.TryGetValue(serverEntityId, out var referrers)) return;

		for (int index = referrers.Count - 1; index >= 0; index--)
		{
			if (referrers[index].LocalEntityId != localEntityId || referrers[index].Codec.ComponentTypeId != componentTypeId) continue;
			referrers.RemoveAt(index);
		}

		if (referrers.Count == 0) _referrersByServerEntityId.Remove(serverEntityId);
	}

	private void RefixReplicatedReferences(int serverEntityId)
	{
		if (!_referrersByServerEntityId.TryGetValue(serverEntityId, out var referrers)) return;

		Span<byte> payload = stackalloc byte[sizeof(int)];
		BitConverter.TryWriteBytes(payload, serverEntityId);

		for (int index = 0; index < referrers.Count; index++)
		{
			var referrer = referrers[index];
			Entity localEntity = new(referrer.LocalEntityId);
			if (!GameContext.World.IsAlive(localEntity)) continue;

			referrer.Codec.Apply(GameContext.World, localEntity, payload);
		}
	}

	private static Entity CreateReplicatedLootEntity(int serverEntityId, NetworkMappingState mapping)
	{
		Entity localEntity = GameContext.World.Create();
		mapping.ServerToClientEntityMap[serverEntityId] = localEntity;
		mapping.ClientToServerEntityMap[localEntity.Id] = serverEntityId;
		ReplicatedEntityReferences.Register(serverEntityId, localEntity.Id);
		GameContext.NetworkService?.RefixReplicatedReferences(serverEntityId);
		return localEntity;
	}

	private static void DestroyReplicatedLootEntity(int serverEntityId, Entity localEntity, NetworkMappingState mapping)
	{
		GameContext.Host?.RemoveLootItemLocal(serverEntityId);
		GameContext.NetworkService?.ForgetReferencesHeldBy(localEntity.Id);
		ReplicatedEntityReferences.Unregister(serverEntityId);
		mapping.ServerToClientEntityMap.Remove(serverEntityId);
		mapping.ClientToServerEntityMap.Remove(localEntity.Id);
		if (GameContext.World.IsAlive(localEntity)) GameContext.World.Destroy(localEntity);
	}

	private void ApplyAbilityStateDelta(AbilityStateDelta delta)
	{
		_mirror.RecordAbility(delta);
		_appliedAbilityStateCount++;
		ApplyAbilityStateToUnit(delta.ServerEntityId, delta.AbilityIndex, delta.Flags, delta.AbilityId);
	}

	private void ApplyAbilityStateToUnit(int serverEntityId, short abilityIndex, byte flags, string? namedAbilityId = null)
	{
		if (!TryGetLocalEntity(serverEntityId, out var localEntity)) return;

		string? abilityId = string.IsNullOrEmpty(namedAbilityId) ? ResolveAbilityIdAt(localEntity, abilityIndex) : namedAbilityId;
		if (abilityId == null) return;

		GameContext.RegistryService?.SetAbilityState(new UnitId(localEntity.Id), abilityId, (flags & 1) != 0, (flags & 2) != 0);
	}

	private static string? ResolveAbilityIdAt(Entity localEntity, short abilityIndex)
	{
		if (!GameContext.World.Has<AbilityState>(localEntity)) return null;

		return GameContext.World.Get<AbilityState>(localEntity).Get(abilityIndex);
	}

	public void ReapplyAbilityStatesToUnits()
	{
		if (_mirror.Abilities.Count == 0 && _mirror.AbilitiesById.Count == 0) return;

		foreach (var kvp in _mirror.Abilities)
		{
			ApplyAbilityStateToUnit(kvp.Key.ServerEntityId, kvp.Key.AbilityIndex, kvp.Value);
		}

		foreach (var kvp in _mirror.AbilitiesById)
		{
			ApplyAbilityStateToUnit(kvp.Key.ServerEntityId, -1, kvp.Value, kvp.Key.AbilityId);
		}

		GameContext.RegistryService?.ClearAbilityStateDirty();
	}

	private void ApplyStaticTextDelta(StaticTextDelta delta)
	{
		_mirror.RecordText(delta);
		_appliedStaticTextCount++;
		GameContext.Host?.ApplyStaticTextDelta(delta);
	}

	private void VerifyReplicatedStateFingerprint(int sequence, uint hostFingerprint)
	{
		uint localFingerprint = _mirror.ComputeFingerprint(ReadPlayerComponentRecordsFromEcs());
		localFingerprint = ReplicatedStateFingerprint.CombineWeatherState(localFingerprint, GameContext.World, FindWorldEntity());
		if (localFingerprint == hostFingerprint)
		{
			if (!_fingerprintMatchLogged)
			{
				_fingerprintMatchLogged = true;
				GD.Print($"[Replication] client fingerprint match at sequence {sequence} (deltas={_appliedComponentDeltaCount}, abilities={_appliedAbilityStateCount}, texts={_appliedStaticTextCount}, catalog={_appliedCatalogCount})");
			}
			return;
		}

		GD.PushError($"[Network] Replicated state fingerprint mismatch at sequence {sequence}: host={hostFingerprint:x8} client={localFingerprint:x8}");
		MultiplayerSyncDiagnostics.RecordFingerprintMismatch(sequence, hostFingerprint, localFingerprint);
	}

	private int _replicatedEcsDriftCheckCounter;

	public void VerifyReplicatedEcsDriftDomain()
	{
		_replicatedEcsDriftCheckCounter++;
		if (_replicatedEcsDriftCheckCounter % 30 != 0) return;

		Entity worldEntity = FindWorldEntity();
		if (worldEntity == Entity.Null || !GameContext.World.Has<NetworkMappingState>(worldEntity)) return;
		var mapping = GameContext.World.Get<NetworkMappingState>(worldEntity);

		var codecs = ReplicatedComponents.All;
		for (int codecIndex = 0; codecIndex < codecs.Count; codecIndex++)
		{
			var codec = codecs[codecIndex];
			if (codec.Scope != ReplicatedScope.Player) continue;

			for (int slot = 0; slot < Realm.Ecs.EcsWorld.MAX_PLAYERS; slot++)
			{
				if (mapping.PlayerEntities == null || slot >= mapping.PlayerEntities.Length) continue;
				if (!_mirror.Components.TryGetValue(((byte)ReplicatedScope.Player, slot, codec.ComponentTypeId), out var expected)) continue;

				Entity playerEntity = mapping.PlayerEntities[slot];
				if (playerEntity == Entity.Null || !GameContext.World.IsAlive(playerEntity)) continue;

				if (_replicatedEcsDriftBuffer.Length < codec.SizeInBytes) _replicatedEcsDriftBuffer = new byte[codec.SizeInBytes];
				var actual = _replicatedEcsDriftBuffer.AsSpan(0, codec.SizeInBytes);
				if (!codec.TryWrite(GameContext.World, playerEntity, actual))
				{
					ReportReplicatedEcsDrift(slot, codec.ComponentName, "component missing");
					continue;
				}

				if (!actual.SequenceEqual(expected))
			{
					ReportReplicatedEcsDrift(slot, codec.ComponentName, "bytes differ from the last authoritative value");
				}
			}
		}
	}

	private void ReportReplicatedEcsDrift(int slot, string componentName, string reason)
	{
		if (_reportedEcsDrift.Contains((slot, componentName))) return;
		_reportedEcsDrift.Add((slot, componentName));
		GD.PushError($"[Replication] local write clobbered replicated state: {componentName} for player slot {slot} ({reason})");
		MultiplayerSyncDiagnostics.RecordEcsDrift(slot, componentName);
	}

	private readonly HashSet<(int Slot, string ComponentName)> _reportedEcsDrift = new();
	private byte[] _replicatedEcsDriftBuffer = new byte[16];

	private List<(int Key, int ComponentTypeId, byte[] Bytes)> ReadPlayerComponentRecordsFromEcs()
	{
		var records = new List<(int Key, int ComponentTypeId, byte[] Bytes)>();
		Entity worldEntity = FindWorldEntity();
		if (worldEntity == Entity.Null || !GameContext.World.Has<NetworkMappingState>(worldEntity)) return records;

		var mapping = GameContext.World.Get<NetworkMappingState>(worldEntity);
		var codecs = ReplicatedComponents.All;
		for (int codecIndex = 0; codecIndex < codecs.Count; codecIndex++)
		{
			var codec = codecs[codecIndex];
			if (codec.Scope != ReplicatedScope.Player) continue;

			for (int slot = 0; slot < Realm.Ecs.EcsWorld.MAX_PLAYERS; slot++)
			{
				if (mapping.PlayerEntities == null || slot >= mapping.PlayerEntities.Length) continue;
				Entity playerEntity = mapping.PlayerEntities[slot];
				if (playerEntity == Entity.Null || !GameContext.World.IsAlive(playerEntity)) continue;

				var bytes = new byte[codec.SizeInBytes];
				if (!codec.TryWrite(GameContext.World, playerEntity, bytes)) continue;
				records.Add((slot, codec.ComponentTypeId, bytes));
			}
		}

		return records;
	}

	public bool TryGetLocalEntity(int serverEntityId, out Entity localEntity)
	{
		localEntity = Entity.Null;
		Entity worldEntity = FindWorldEntity();
		if (worldEntity == Entity.Null || !GameContext.World.Has<NetworkMappingState>(worldEntity)) return false;
		var mapping = GameContext.World.Get<NetworkMappingState>(worldEntity);
		return mapping.ServerToClientEntityMap.TryGetValue(serverEntityId, out localEntity);
	}

	public void SetBackupResources(float gold, float wood, float stone)
	{
		var worldQuery = QueryCache.AllReplayStateQuery;
		GameContext.World.Query(in worldQuery, (Entity entity) =>
		{
			ref var state = ref GameContext.World.Get<ReplayState>(entity);
			state.GoldBackup = gold;
			state.WoodBackup = wood;
			state.StoneBackup = stone;
		});
	}


	
	public ServerCommandResult ExecuteServerCommand(int peerId, NetworkCommand cmd)
	{
		return GameContext.CommandAuthority.ExecuteServerCommand(peerId, cmd);
	}

	public bool TryGetPlayerEntityForPeer(int peerId, out Entity playerEntity)
	{
		return GameContext.CommandAuthority.TryGetPlayerEntityForPeer(peerId, out playerEntity);
	}

	public bool ValidateSpellCommandForHarness(int peerId, NetworkCommand cmd, out string rejectionReason)
	{
		return GameContext.CommandAuthority.ValidateSpellCommandForHarness(peerId, cmd, out rejectionReason);
	}

	private readonly Dictionary<int, List<(int LocalEntityId, IReplicatedComponentCodec Codec)>> _referrersByServerEntityId = new();
	private readonly Dictionary<(int LocalEntityId, int ComponentTypeId), int> _referencedServerEntityIds = new();

	private readonly Dictionary<int, List<int>> _referencedComponentTypesByLocalEntity = new();

	public Entity FindWorldEntity()
	{
		Entity worldEntity = GameContext.Simulation.WorldEntity;
		if (GameContext.World == null || worldEntity == Entity.Null) return Entity.Null;
		if (!GameContext.World.IsAlive(worldEntity)) return Entity.Null;
		return GameContext.World.Has<NetworkState>(worldEntity) ? worldEntity : Entity.Null;
	}

	public bool WasClientInMultiplayer { get; private set; } = false;
	public bool IsConnectionLost { get; private set; } = false;

	public int LocalPeerId
	{
		get
		{
			var worldEntity = FindWorldEntity();
			return worldEntity != Entity.Null && GameContext.World.Has<NetworkState>(worldEntity)
				? GameContext.World.Get<NetworkState>(worldEntity).LocalPeerId
				: 1;
		}
		set
		{
			var worldEntity = FindWorldEntity();
			if (worldEntity != Entity.Null && GameContext.World.Has<NetworkState>(worldEntity))
			{
				ref var state = ref GameContext.World.Get<NetworkState>(worldEntity);
				state.LocalPeerId = value;
			}
		}
	}

	public float CommandSendTimer
	{
		get
		{
			var worldEntity = FindWorldEntity();
			return worldEntity != Entity.Null && GameContext.World.Has<NetworkState>(worldEntity)
				? GameContext.World.Get<NetworkState>(worldEntity).CommandSendTimer
				: 0f;
		}
		set
		{
			var worldEntity = FindWorldEntity();
			if (worldEntity != Entity.Null && GameContext.World.Has<NetworkState>(worldEntity))
			{
				ref var state = ref GameContext.World.Get<NetworkState>(worldEntity);
				state.CommandSendTimer = value;
			}
		}
	}

	public ulong LastSnapshotReceivedTime
	{
		get
		{
			var worldEntity = FindWorldEntity();
			return worldEntity != Entity.Null && GameContext.World.Has<NetworkState>(worldEntity)
				? GameContext.World.Get<NetworkState>(worldEntity).LastSnapshotReceivedTime
				: 0;
		}
		set
		{
			var worldEntity = FindWorldEntity();
			if (worldEntity != Entity.Null && GameContext.World.Has<NetworkState>(worldEntity))
			{
				ref var state = ref GameContext.World.Get<NetworkState>(worldEntity);
				state.LastSnapshotReceivedTime = value;
			}
		}
	}

	public void MarkClientEnteredMultiplayer()
	{
		WasClientInMultiplayer = true;
		LastSnapshotReceivedTime = Time.GetTicksMsec();
	}

	public void BroadcastMessageDomainLogic(string message) => GameLog.LogToConsole($"[HOST BROADCAST] {message}");

	public void ReportAuthorityLost() => IsConnectionLost = true;

	public void UpdateConnectionStatus(bool multiplayerActive, bool isServer)
	{
		bool isLost;
		if (multiplayerActive && !isServer)
		{
			ulong now = Time.GetTicksMsec();
			ulong lastReceived = LastSnapshotReceivedTime;
			if (lastReceived > 0)
			{
				double timeSinceLastSnapshot = (now - lastReceived) / 1000.0;
				isLost = timeSinceLastSnapshot > 30.0;
			}
			else
			{
				isLost = false;
			}
		}
		else
		{
			isLost = false;
		}

		IsConnectionLost = isLost;
	}

	private static readonly Dictionary<int, int> _playerTeams = new();
	private static readonly Dictionary<(int, int), bool> _alliances = new();

	public static void SetPlayerTeam(int playerIndex, int teamIndex)
	{
		_playerTeams[playerIndex] = teamIndex;
	}

	public static int GetPlayerTeam(int playerIndex)
	{
		if (_playerTeams.TryGetValue(playerIndex, out int team)) return team;
		return playerIndex;
	}

	public static void SetPlayersAllied(int playerIndex1, int playerIndex2, bool allied)
	{
		_alliances[(playerIndex1, playerIndex2)] = allied;
		_alliances[(playerIndex2, playerIndex1)] = allied;
	}

	public static bool ArePlayerIndicesEnemies(int playerIndex1, int playerIndex2)
	{
		if (playerIndex1 == playerIndex2) return false;

		if (GameContext.SimulationService != null)
		{
			return GameContext.SimulationService.ArePlayersEnemies(playerIndex1, playerIndex2);
		}

		if (!HasActiveLobby()) return playerIndex1 != playerIndex2;

		int t1 = GetTeamIndexForPlayerIndex(playerIndex1, playerIndex1);
		int t2 = GetTeamIndexForPlayerIndex(playerIndex2, playerIndex2);
		return t1 != t2;
	}

	private static bool HasActiveLobby()
	{
		if (GameContext.Lobby == null) return false;
		if (GameContext.Lobby.PlayerList == null) return false;
		return GameContext.Lobby.PlayerList.Count > 0;
	}

	private static int GetTeamIndexForPeer(int peerId, int defaultTeam)
	{
		var list = GameContext.Lobby.PlayerList;
		for (int i = 0; i < list.Count; i++)
		{
			if (list[i].PeerId == peerId)
			{
				return list[i].TeamIndex;
			}
		}
		return defaultTeam;
	}

	private static int GetTeamIndexForPlayerIndex(int playerIndex, int defaultTeam)
	{
		var list = GameContext.Lobby.PlayerList;
		for (int i = 0; i < list.Count; i++)
		{
			if (list[i].Slot == playerIndex)
			{
				return list[i].TeamIndex;
			}
		}
		return defaultTeam;
	}

	public void StartCountdownTimer(float duration, string label, bool multiplayerActive, bool isServerActive, Action<float, string> startView, Action<float, string> rpcAction)
	{
		startView?.Invoke(duration, label);
		if (multiplayerActive && isServerActive)
		{
			rpcAction?.Invoke(duration, label);
		}
	}

	public void BroadcastActionIfServer(bool isServer, Action rpcAction)
	{
		if (isServer && rpcAction != null)
		{
			rpcAction.Invoke();
		}
	}

	public void SetWeather(string weatherType, bool isMultiplayer, bool isServer, Action<string> viewAction, Action<string> rpcAction)
	{
		viewAction?.Invoke(weatherType);
		if (isMultiplayer && isServer) rpcAction?.Invoke(weatherType);
	}

	public void RemoveMultiplayerMapping(int entityId)
	{
		ForgetReferencesHeldBy(entityId);

		if (!GameContext.World.TryGet<NetworkMappingState>(GameContext.Simulation.WorldEntity, out var mapState)) return;

		if (mapState.ClientToServerEntityMap.TryGetValue(entityId, out int serverEntityId))
		{
			mapState.ServerToClientEntityMap.Remove(serverEntityId);
			ReplicatedEntityReferences.Unregister(serverEntityId);
		}

		mapState.ClientToServerEntityMap.Remove(entityId);
	}

	public async Task CheckCreatorRegistrationAsync(string pubKeyStr, string seedServerUrl, Action<string> onNotFound)
	{
		try
		{
			using (var httpClient = new HttpClient())
			{
				httpClient.Timeout = TimeSpan.FromSeconds(3);
				var res = await httpClient.GetAsync(seedServerUrl + "/api/creators/check/" + Uri.EscapeDataString(pubKeyStr));
				if (res.StatusCode == HttpStatusCode.NotFound)
				{
					onNotFound?.Invoke(pubKeyStr);
				}
			}
		}
		catch (Exception ex)
		{
			GD.Print($"[NetworkService] Offline or registry server unreachable, skipping creator registration prompt: {ex.Message}");
		}
	}
}