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
	private readonly SnapshotComponentApplier _componentApplier;
	private readonly ReplicatedUnitSpawner _unitSpawner;
	private int _appliedColdPayloadCount;
	private int _appliedAbilityStateCount;
	private int _appliedStaticTextCount;
	private int _appliedCatalogCount;
	private bool _fingerprintMatchLogged;

	private readonly PendingUnitSpawnQueue _pendingUnitSpawns = new();
	private List<Entity> _pendingUnitKills = new();
	private List<Entity> _pendingUnitDespawns = new();

	private readonly List<int> _untrackedServerIds = new();
	private readonly HashSet<int> _describedLootServerIds = new();
	private readonly List<int> _staleLootServerIds = new();
	private readonly Dictionary<int, ulong> _baselineUnitPresence = new();
	private readonly HashSet<(int Key, int TypeId)> _baselineSentUnitComponents = new();
	private byte[] _baselineReconcileScratch = new byte[256];


	public NetworkService()
	{
		_componentApplier = new SnapshotComponentApplier(_mirror, _references, TryGetLocalEntity, IsWaitingForUnitSpawn, FindWorldEntity);
		_unitSpawner = new ReplicatedUnitSpawner(FindWorldEntity, () => LastAppliedSnapshotSequenceForDiagnostics);
	}

	public bool IsPaused { get; set; } = false;
	public int LastAppliedSnapshotSequenceForDiagnostics { get; private set; } = -1;
	public int LastAppliedBaselineSequenceForDiagnostics { get; private set; } = -1;

	public int PausedByPlayerIndex { get; set; } = -1;

	public int PauseResumeCountdownSeconds { get; set; }

	public int RemainingPauses { get; set; } = 3;

	public int PauseCooldownSecondsRemaining { get; set; }

	public void RemoveClientToServerMapping(int localEntityId)
	{
		_references.ForgetAllHeldBy(localEntityId);

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
		PauseResumeCountdownSeconds = 0;
		RemainingPauses = 3;
		PauseCooldownSecondsRemaining = 0;
		_pendingUnitSpawns.Clear();
		_pendingUnitKills.Clear();
		_pendingUnitDespawns.Clear();
		_componentApplier.Clear();
		GameContext.SnapshotTimeline.Clear();
		_references.Clear();
		_untrackedServerIds.Clear();
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

	public void UpdateClientTickDomain()
	{
		VerifyReplicatedEcsDriftDomain();
	}

	public (int CommandId, byte[] Payload) BuildClientCommand(string commandType, List<int> unitIds, System.Numerics.Vector3 targetPos, int targetEntityId, string argString)
	{
		Entity worldEntity = FindWorldEntity();
		if (worldEntity == Entity.Null || !GameContext.World.Has<NetworkState>(worldEntity)) return (0, Array.Empty<byte>());

		// A command id is this peer's own counter, and the authority answers a repeat of one it already answered as a
		// duplicate. Minting a fallback id here re-sent an order that had already gone out, so nothing is authored
		// rather than something that might be a repeat.
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
			_componentApplier.Clear();

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

		ApplyReplicatedComponentState(snapshot);
		_componentApplier.FlushPending(snapshot.Sequence);

		if (snapshot.IsBaseline) LastAppliedBaselineSequenceForDiagnostics = snapshot.Sequence;
		if (snapshot.IsBaseline || GameContext.World.Get<NetworkState>(worldEntity).HasReceivedInitialBaseline)
		{
			MultiplayerSyncDiagnostics.RecordClientSnapshot(snapshot.IsBaseline);
		}
		GameContext.SnapshotApply.Acknowledge(snapshot.Sequence);
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
				_references.Forget(localEntity.Id, codec.ComponentTypeId);
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
			ReplicatedLootLifecycle.Destroy(serverEntityId, localEntity, mapping);
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
		localEntity = ReplicatedLootLifecycle.Create(entry.EntityId, mapping);
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

	/// <summary>
	///     States, for the presentation clock, whether this unit holds a move order the match confirmed, stamped with the
	///     sequence of the description that said so. Only this player's own units are drawn ahead of their descriptions, so
	///     only their order state is worth stamping.
	/// </summary>

	private void ApplySnapshotToUnit(UnitSnapshot snap, int sequence, NetworkMappingState mapping)
	{
		if (!mapping.ServerToClientEntityMap.TryGetValue(snap.EntityId, out var localEntity))
		{
			if (snap.IsDead) _pendingUnitSpawns.Remove(snap.EntityId);
			else _pendingUnitSpawns.Queue(snap);
			return;
		}

		if (!GameContext.World.IsAlive(localEntity) || ReplicatedUnitSpawner.IsServerEntityIdRecycled(snap, localEntity))
		{
			GameContext.SnapshotTimeline.Reset(localEntity.Id);
			RetireClientEntityForRecycledServerId(mapping, localEntity, snap.EntityId);
			if (!snap.IsDead) _pendingUnitSpawns.Queue(snap);
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

		bool targetJumped = (describedPosition - previousTargetPosition).Length() > PlausibleStepMeters(localEntity, sequence, lastDescribedSequence);

		if (targetJumped) GameContext.SnapshotTimeline.Reset(localEntity.Id);
		GameContext.SnapshotTimeline.Record(localEntity.Id, describedPosition, snap.RotationY, sequence);

		GameContext.World.SetOrAdd(localEntity, new LatestDescription
		{
			Position = describedPosition,
			RotationY = snap.RotationY
		});

		GameContext.World.SetOrAdd(localEntity, new DescribedAtSequence(sequence));
	}

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


	private void RetireClientEntityForRecycledServerId(NetworkMappingState mapping, Entity localEntity, int serverEntityId)
	{
		RetireClientEntityForUntrackedServerId(mapping, localEntity, serverEntityId);
	}

	public Entity SpawnUnitFromSnapshot(UnitSnapshot snap, Func<string, bool, string> getFallbackModelPath, out string modelPath, out int playerIndex) =>
		_unitSpawner.Spawn(snap, getFallbackModelPath, out modelPath, out playerIndex);

	public bool HasPendingUnitSpawns => _pendingUnitSpawns.Count > 0;

	/// <summary>
	///     Takes one unit this screen is still waiting to build, oldest insertion first.
	/// </summary>
	public bool TryTakePendingUnitSpawn(out UnitSnapshot snapshot) => _pendingUnitSpawns.TryTakeAny(out snapshot);

	public bool IsWaitingForUnitSpawn(int serverEntityId) => _pendingUnitSpawns.Contains(serverEntityId);


	public IReadOnlyList<Entity> FlushPendingUnitKills() => SwapPending(ref _pendingUnitKills, ref _pendingUnitKillsTaken);

	public IReadOnlyList<Entity> FlushPendingUnitDespawns() => SwapPending(ref _pendingUnitDespawns, ref _pendingUnitDespawnsTaken);

	private List<Entity> _pendingUnitKillsTaken = new();
	private List<Entity> _pendingUnitDespawnsTaken = new();

	/// <summary>
	///     Hands back everything waiting in <paramref name="pending" /> and empties it by exchanging the two buffers rather
	///     than copying: a description that buries an army is read on every render frame, and a copy of it per frame is an
	///     allocation the size of the army. The buffer handed back belongs to this service and is refilled by the swap on the
	///     next call, so the caller reads what it was handed before calling again.
	/// </summary>
	private static IReadOnlyList<Entity> SwapPending(ref List<Entity> pending, ref List<Entity> taken)
	{
		if (pending.Count == 0) return Array.Empty<Entity>();

		taken.Clear();
		(pending, taken) = (taken, pending);
		return taken;
	}




	private void ApplyReplicatedColdPayloads(WorldSnapshot snapshot)
	{
		if (snapshot.ColdPayloads == null) return;

		for (int i = 0; i < snapshot.ColdPayloads.Count; i++)
		{
			ApplyColdPayloadDelta(snapshot.ColdPayloads[i]);
		}
	}



	/// <summary>
	///     Applies the component state a description carries. A component delta for a unit this screen has not built yet is
	///     queued by <see cref="SnapshotComponentApplier.Apply" /> and retried by <see cref="SnapshotComponentApplier.FlushPending" />, which
	///     is why this reports nothing: a component still owed is content, not a description that never arrived.
	/// </summary>
	private void ApplyReplicatedComponentState(WorldSnapshot snapshot)
	{
		if (snapshot.ComponentDeltas != null)
		{
			for (int i = 0; i < snapshot.ComponentDeltas.Count; i++)
			{
				_componentApplier.Apply(snapshot.ComponentDeltas[i], snapshot.Sequence);
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









	private void ApplyAbilityStateDelta(AbilityStateDelta delta)
	{
		_mirror.RecordAbility(delta);
		_appliedAbilityStateCount++;
		ApplyAbilityStateToUnit(delta.ServerEntityId, delta.AbilityIndex, delta.Flags, delta.AbilityId);
	}

	private void ApplyAbilityStateToUnit(int serverEntityId, short abilityIndex, byte flags, string? namedAbilityId = null)
	{
		if (!TryGetLocalEntity(serverEntityId, out var localEntity)) return;

		// A delta names a slot in the unit's ability list, so a list that was replaced renumbers every slot. A change about
		// an ability the unit does not list is named instead, which is how a build option the map withheld reaches a
		// screen even though the unit can never cast it.
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
				GameLog.LogToConsole($"[Replication] client fingerprint match at sequence {sequence} (deltas={_componentApplier.AppliedComponentDeltaCount}, abilities={_appliedAbilityStateCount}, texts={_appliedStaticTextCount}, catalog={_appliedCatalogCount})");
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
				}				if (!actual.SequenceEqual(expected))
				{
					ReportReplicatedEcsDrift(slot, codec.ComponentName, "bytes differ from the last authoritative value");
				}
			}
		}

		AuditReplicatedUnitDrift(mapping);
	}

	private const int UnitDriftAuditWindowEntities = 128;
	private int _unitDriftAuditNextServerEntityId;

	/// <summary>
	///     Audits one window of the replicated unit components this screen has applied, so a whole board is swept every few
	///     seconds rather than every tick. It runs only while a recording harness is watching, because that is where the
	///     report is read: a unit component this screen writes itself is overwritten by the next description, which looks
	///     exactly like a description that never arrived, and this is the only check that can say which of the two it was.
	/// </summary>
	private void AuditReplicatedUnitDrift(NetworkMappingState mapping)
	{
		if (!MultiplayerSyncDiagnostics.IsRecording) return;

		IReadOnlyList<IReplicatedComponentCodec> codecs = ReplicatedUnitComponentSlots.Slots;
		int windowEnd = _unitDriftAuditNextServerEntityId + UnitDriftAuditWindowEntities;
		if (windowEnd > Realm.Ecs.EcsWorld.MAX_ENTITIES) windowEnd = Realm.Ecs.EcsWorld.MAX_ENTITIES;

		for (int serverEntityId = _unitDriftAuditNextServerEntityId; serverEntityId < windowEnd; serverEntityId++)
		{
			if (!mapping.ServerToClientEntityMap.TryGetValue(serverEntityId, out Entity localEntity)) continue;
			if (!GameContext.World.IsAlive(localEntity)) continue;

			for (int codecIndex = 0; codecIndex < codecs.Count; codecIndex++)
			{
				var codec = codecs[codecIndex];
				if (!_mirror.Components.TryGetValue(((byte)ReplicatedScope.Unit, serverEntityId, codec.ComponentTypeId), out var expected)) continue;

				if (_replicatedEcsDriftBuffer.Length < codec.SizeInBytes) _replicatedEcsDriftBuffer = new byte[codec.SizeInBytes];
				var actual = _replicatedEcsDriftBuffer.AsSpan(0, codec.SizeInBytes);
				if (!codec.TryWrite(GameContext.World, localEntity, actual))
				{
					ReportReplicatedUnitDrift(serverEntityId, codec.ComponentName, "the component the match described is gone");
					continue;
				}

				if (!actual.SequenceEqual(expected))
				{
					ReportReplicatedUnitDrift(serverEntityId, codec.ComponentName, "bytes differ from the last authoritative value");
				}
			}
		}

		_unitDriftAuditNextServerEntityId = windowEnd >= Realm.Ecs.EcsWorld.MAX_ENTITIES ? 0 : windowEnd;
	}

	private readonly HashSet<(int ServerEntityId, string ComponentName)> _reportedUnitEcsDrift = new();

	private void ReportReplicatedUnitDrift(int serverEntityId, string componentName, string reason)
	{
		if (!_reportedUnitEcsDrift.Add((serverEntityId, componentName))) return;
		GD.PushError($"[Replication] local write clobbered replicated unit state: {componentName} for unit {serverEntityId} ({reason})");
		MultiplayerSyncDiagnostics.RecordEcsDrift(serverEntityId, componentName);
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

	internal ReplicatedReferenceIndex References => _references;

	private readonly ReplicatedReferenceIndex _references = new();


	public bool IsReferenceUnresolved(int localEntityId, int componentTypeId) =>
		_references.IsReferenceUnresolved(localEntityId, componentTypeId);

	public void ForgetReferencesHeldBy(int localEntityId) => _references.ForgetAllHeldBy(localEntityId);

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

	/// <summary>
	///     States that the authority said something, whatever it was. A paused match publishes no world, so the pause it
	///     keeps restating is the only thing arriving: counting only descriptions made a pause longer than the liveness
	///     window read as a match that had gone away.
	/// </summary>
	public void NoteAuthorityAlive() => LastSnapshotReceivedTime = Time.GetTicksMsec();

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
			GameLog.LogToConsole($"[NetworkService] Offline or registry server unreachable, skipping creator registration prompt: {ex.Message}");
		}
	}
}