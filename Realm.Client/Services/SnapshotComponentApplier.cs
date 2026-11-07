using System;
using System.Collections.Generic;
using Realm.Ecs;
using Godot;
using Realm.Client.Core;
using Realm.Client.Network;
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
///     Applies the component state a description carries: resolves the delta's scope to a local entity, applies or
///     removes the payload through its codec, and holds a delta for a unit this screen has not built yet until the unit
///     arrives, the grace for that identifier runs out, or the unit is reported as still on its way.
/// </summary>
public class SnapshotComponentApplier
{
	internal delegate bool TryResolveEntity(int serverEntityId, out Entity localEntity);

	private const int UnplacedUnitDeltaGraceSequences = 4;

	private struct PendingUnitComponentDelta
	{
		public ComponentDelta Delta;
		public int FirstSeenSequence;
	}

	private readonly Dictionary<(int Key, int ComponentTypeId), PendingUnitComponentDelta> _pendingDeltas = new();
	private readonly List<(int Key, int ComponentTypeId)> _pendingKeys = new();
	private readonly ReplicatedStateMirror _mirror;
	private readonly ReplicatedReferenceIndex _references;
	private readonly TryResolveEntity _tryResolveEntity;
	private readonly Func<int, bool> _isWaitingForUnitSpawn;
	private readonly Func<Entity> _findWorldEntity;

	public int AppliedComponentDeltaCount { get; private set; }

	internal SnapshotComponentApplier(ReplicatedStateMirror mirror, ReplicatedReferenceIndex references,
		TryResolveEntity tryResolveEntity, Func<int, bool> isWaitingForUnitSpawn, Func<Entity> findWorldEntity)
	{
		_mirror = mirror;
		_references = references;
		_tryResolveEntity = tryResolveEntity;
		_isWaitingForUnitSpawn = isWaitingForUnitSpawn;
		_findWorldEntity = findWorldEntity;
	}

	internal void Clear()
	{
		_pendingDeltas.Clear();
		_pendingKeys.Clear();
	}

	internal void Apply(ComponentDelta delta, int snapshotSequence)
	{
		var codec = ReplicatedComponents.Find(delta.ComponentTypeId);
		if (codec == null)
		{
			GD.PushWarning($"[Network] Received delta for unregistered component type {delta.ComponentTypeId}");
			return;
		}

		if (delta.Bytes == null || (delta.Bytes.Length != 0 && delta.Bytes.Length != codec.SizeInBytes))
		{
			GD.PushWarning($"[Network] Discarded {codec.ComponentName} delta with {(delta.Bytes?.Length ?? 0)} bytes, expected {codec.SizeInBytes}");
			return;
		}

		Entity targetEntity = Entity.Null;
		if ((ReplicatedScope)delta.Scope == ReplicatedScope.Player)
		{
			Entity worldEntity = _findWorldEntity();
			if (worldEntity == Entity.Null || !GameContext.World.Has<NetworkMappingState>(worldEntity)) return;
			var mapping = GameContext.World.Get<NetworkMappingState>(worldEntity);
			if (mapping.PlayerEntities == null || delta.Key < 0 || delta.Key >= mapping.PlayerEntities.Length) return;
			targetEntity = mapping.PlayerEntities[delta.Key];
		}
		else if ((ReplicatedScope)delta.Scope == ReplicatedScope.Unit)
		{
			if (!_tryResolveEntity(delta.Key, out targetEntity))
			{
				if (codec.ComponentTypeId != ComponentType<GroundLoot>.Id || delta.Bytes.Length == 0)
				{
					if (!_pendingDeltas.ContainsKey((delta.Key, delta.ComponentTypeId)))
					{
						_pendingDeltas[(delta.Key, delta.ComponentTypeId)] = new PendingUnitComponentDelta
						{
							Delta = delta,
							FirstSeenSequence = snapshotSequence
						};
					}

					return;
				}
				Entity worldEntity = _findWorldEntity();
				if (worldEntity == Entity.Null || !GameContext.World.Has<NetworkMappingState>(worldEntity)) return;
				targetEntity = ReplicatedLootLifecycle.Create(delta.Key, GameContext.World.Get<NetworkMappingState>(worldEntity));
			}
		}
		else if ((ReplicatedScope)delta.Scope == ReplicatedScope.World)
		{
			targetEntity = GameContext.Simulation.WorldEntity;
		}

		if (targetEntity == Entity.Null || !GameContext.World.IsAlive(targetEntity)) return;
		if (delta.Bytes.Length == 0)
		{
			codec.Remove(GameContext.World, targetEntity);

			if (codec.ComponentTypeId == ComponentType<GroundLoot>.Id)
			{
				Entity worldEntity = _findWorldEntity();
				if (worldEntity != Entity.Null && GameContext.World.Has<NetworkMappingState>(worldEntity))
				{
					ReplicatedLootLifecycle.Destroy(delta.Key, targetEntity, GameContext.World.Get<NetworkMappingState>(worldEntity));
				}
			}
		}
		else
		{
			codec.Apply(GameContext.World, targetEntity, delta.Bytes);

			if (codec.ComponentTypeId == ComponentType<PlayerRelationshipMatrix>.Id)
			{
				GameContext.SimulationService?.MarkRelationshipsAuthoritative();
			}

			if (codec.ComponentTypeId == ComponentType<MoveTo>.Id && GameContext.World.Has<PreviewMoveTo>(targetEntity))
			{
				GameContext.World.Remove<PreviewMoveTo>(targetEntity);
			}

			if (codec.ComponentTypeId == ComponentType<GroundLoot>.Id) GameContext.Host?.SyncReplicatedLootNode(delta.Key, targetEntity);
		}

		// Whether this unit still holds a move order is stated by the delta that changed it rather than by asking the world
		// once the whole description is applied. A description's unit records are applied before its component state, so
		// asking there answered what the description before it had said: the lead an order draws was acquired a sequence
		// late and released one sequence past the moment the completion was drawn.
		if (codec.ComponentTypeId == ComponentType<MoveTo>.Id && (ReplicatedScope)delta.Scope == ReplicatedScope.Unit)
		{
			NoteLocalOrderState(targetEntity, delta.Bytes.Length != 0, snapshotSequence);
		}

		_references.Record(delta, targetEntity, codec);

		_mirror.RecordComponent(delta);
		AppliedComponentDeltaCount++;
		if (delta.ComponentTypeId == ComponentType<AbilityState>.Id && (ReplicatedScope)delta.Scope == ReplicatedScope.Unit)
		{
			GameContext.RegistryService?.ClearAbilityStateForUnit(new UnitId(targetEntity.Id));
			_mirror.RemoveAbilitiesForEntity(delta.Key);
		}
	}

	internal void FlushPending(int sequence)
	{
		if (_pendingDeltas.Count == 0) return;

		_pendingKeys.Clear();
		foreach ((int Key, int ComponentTypeId) key in _pendingDeltas.Keys) _pendingKeys.Add(key);

		bool anUnplaceableChangeWasDropped = false;
		for (int index = 0; index < _pendingKeys.Count; index++)
		{
			(int Key, int ComponentTypeId) key = _pendingKeys[index];
			if (!_pendingDeltas.TryGetValue(key, out PendingUnitComponentDelta pending)) continue;

			if (_tryResolveEntity(key.Key, out _))
			{
				_pendingDeltas.Remove(key);
				Apply(pending.Delta, sequence);
				continue;
			}

			// A unit this screen is still waiting to build is not one it has given up on: its description is held and will be
			// built, so the grace below is for an identifier that resolved to nothing rather than for a spawn backlog. Those
			// two were indistinguishable, and a full backlog spent the grace on every unit behind it and answered a slow
			// first description with requests for the whole world.
			if (_isWaitingForUnitSpawn(key.Key)) continue;

			if (sequence - pending.FirstSeenSequence < UnplacedUnitDeltaGraceSequences) continue;

			_pendingDeltas.Remove(key);
			anUnplaceableChangeWasDropped = true;
		}

		if (anUnplaceableChangeWasDropped) GameContext.SnapshotApply.RequestBaseline();
	}

	private static void NoteLocalOrderState(Entity localEntity, bool holdsMoveOrder, int sequence)
	{
		int localPlayerIndex = GameContext.Host?.LocalPlayerIndex ?? -1;
		if (localPlayerIndex < 0) return;
		if (!GameContext.World.Has<UnitOwnerPlayer>(localEntity)) return;
		if (GameContext.World.Get<UnitOwnerPlayer>(localEntity).PlayerIndex != localPlayerIndex) return;

		GameContext.SnapshotTimeline.NoteConfirmedOrder(localEntity.Id, holdsMoveOrder, sequence);
	}
}
