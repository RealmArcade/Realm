using System;
using System.Collections.Generic;
using Realm.Client.Core;
using Realm.Ecs;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Replication;
using Realm.Shared.Network;

namespace Realm.Client.Services;

/// <summary>
///     Reconciling a baseline snapshot against what this screen already holds: units the snapshot no longer
///     describes are retired, and a unit component the baseline says the host does not have is removed
///     locally, so a stale component cannot outlive the description that omitted it.
/// </summary>
public sealed class BaselineReconciler
{
	private static EcsWorld Ecs => GameContext.World
		?? throw new InvalidOperationException("The baseline reconciler was used before a world existed.");

	private readonly ReplicatedReferenceIndex _references;
	private readonly Action<NetworkMappingState, Entity, int> _retire;

	private readonly List<int> _untrackedServerIds = new();
	private readonly Dictionary<int, ulong> _baselineUnitPresence = new();
	private readonly HashSet<(int Key, int TypeId)> _baselineSentUnitComponents = new();
	private byte[] _baselineReconcileScratch = new byte[256];

	public BaselineReconciler(ReplicatedReferenceIndex references, Action<NetworkMappingState, Entity, int> retire)
	{
		_references = references;
		_retire = retire;
	}

	/// <summary>Drops the per-baseline scratch state; the next baseline rebuilds it.</summary>
	public void Reset()
	{
		_untrackedServerIds.Clear();
		_baselineUnitPresence.Clear();
		_baselineSentUnitComponents.Clear();
	}

	public void SnapshotMismatches(WorldSnapshot snapshot, NetworkMappingState mapping)
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
			if (Ecs.Has<GroundLoot>(localEntity)) continue;

			_retire(mapping, localEntity, serverEntityId);
		}
	}

	public void ReconcileComponents(WorldSnapshot snapshot, NetworkMappingState mapping)
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
				if (Ecs.IsAlive(kvp.Value) && !Ecs.Has<GroundLoot>(kvp.Value))
				{
					MultiplayerSyncDiagnostics.RecordUnreconciledUnit(serverEntityId);
				}

				continue;
			}

			Entity localEntity = kvp.Value;
			if (!Ecs.IsAlive(localEntity)) continue;

			for (int slot = 0; slot < codecs.Count; slot++)
			{
				var codec = codecs[slot];
				if (_baselineReconcileScratch.Length < codec.SizeInBytes) _baselineReconcileScratch = new byte[codec.SizeInBytes];

				bool peerHasComponent = codec.TryWrite(Ecs, localEntity, _baselineReconcileScratch.AsSpan(0, codec.SizeInBytes));
				if ((presenceMask & (1UL << slot)) != 0)
				{
					if (!peerHasComponent && !_baselineSentUnitComponents.Contains((serverEntityId, codec.ComponentTypeId)))
					{
						MultiplayerSyncDiagnostics.RecordMissingUnitComponent(serverEntityId, codec.ComponentName);
					}

					continue;
				}

				if (!peerHasComponent) continue;

				codec.Remove(Ecs, localEntity);
				MultiplayerSyncDiagnostics.RecordRemovedUnitComponent(serverEntityId, codec.ComponentName);
				_references.Forget(localEntity.Id, codec.ComponentTypeId);
			}
		}
	}
}
