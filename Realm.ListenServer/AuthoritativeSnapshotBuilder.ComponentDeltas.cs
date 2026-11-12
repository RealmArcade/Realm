using System.Numerics;
using Realm.Ecs;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Tags;
using Realm.Ecs.Replication;
using Realm.Ecs.Services;
using Realm.Shared.Network;

namespace Realm.ListenServer;

/// <summary>component and unit component deltas, and their removals.</summary>
public partial class AuthoritativeSnapshotBuilder
	{
	private void AppendComponentDeltas(SnapshotPeerState state, WorldSnapshot snapshot, int sequence, bool isBaseline)
	{
		RestatePendingRemovals(state, snapshot, sequence);

		for (int codecIndex = 0; codecIndex < WorldCodecs.Length; codecIndex++)
		{
			AppendComponentDelta(state, snapshot, WorldCodecs[codecIndex], _worldEntity, (byte)ReplicatedScope.World, 0, sequence, isBaseline);
		}

		Entity[] playerEntities = ResolvePlayerEntities();
		for (int codecIndex = 0; codecIndex < PlayerCodecs.Length; codecIndex++)
		{
			IReplicatedComponentCodec codec = PlayerCodecs[codecIndex];

			for (int slot = 0; slot < playerEntities.Length; slot++)
			{
				Entity playerEntity = playerEntities[slot];
				if (playerEntity == Entity.Null || !_world.IsAlive(playerEntity)) continue;

				AppendComponentDelta(state, snapshot, codec, playerEntity, (byte)ReplicatedScope.Player, slot, sequence, isBaseline);
			}
		}

		for (int unitIndex = 0; unitIndex < _describedUnits.Count; unitIndex++)
		{
			AppendUnitComponentDeltas(state, snapshot, _describedUnits[unitIndex].Entity, _describedUnitStates[unitIndex], sequence, isBaseline);
		}
	}

	/// <summary>
	///     Says again, for a few sequences, that a component this peer was told it had is gone. A description that did not
	///     arrive costs whatever it stated, and a removal is the one statement no later description repeats: a peer that
	///     missed it keeps a component the authority dropped, and a baseline does not clear what it leaves out. Stating it
	///     again costs one empty delta per removed component per sequence, and stops once the window has passed.
	/// </summary>
	private void RestatePendingRemovals(SnapshotPeerState state, WorldSnapshot snapshot, int sequence)
	{
		for (int index = state.PendingRemovals.Count - 1; index >= 0; index--)
		{
			PendingComponentRemoval removal = state.PendingRemovals[index];
			int age = sequence - removal.DescribedAtSequence;
			if (age <= 0) continue;

			if (age > RemovalRestateSequences)
			{
				state.PendingRemovals.RemoveAt(index);
				continue;
			}

			snapshot.ComponentDeltas.Add(new ComponentDelta
			{
				Key = removal.Key,
				ComponentTypeId = removal.TypeId,
				Scope = removal.Scope,
				Bytes = Array.Empty<byte>()
			});
		}
	}

	/// <summary>
	///     Forgets that a component was removed once it is being described again, because the value this description states
	///     is newer than the removal it supersedes.
	/// </summary>
	private static void ForgetPendingRemoval(SnapshotPeerState state, byte scope, int key, int componentTypeId)
	{
		for (int index = state.PendingRemovals.Count - 1; index >= 0; index--)
		{
			PendingComponentRemoval removal = state.PendingRemovals[index];
			if (removal.Scope != scope || removal.Key != key || removal.TypeId != componentTypeId) continue;

			state.PendingRemovals.RemoveAt(index);
		}
	}

	private void AppendUnitComponentDeltas(SnapshotPeerState state, WorldSnapshot snapshot, Entity unitEntity, UnitDescriptionState held, int tick, bool isBaseline)
	{
		if (!_descriptions.TryGetUnitComponentState(tick, unitEntity.Id, out ulong presentCodecMask, out ulong presenceMask))
		{
			(presentCodecMask, presenceMask) = DescribeUnitComponentState(unitEntity);
			_descriptions.SetUnitComponentState(tick, unitEntity.Id, presentCodecMask, presenceMask);
		}

		for (ulong forgottenBits = held.ToldCodecMask & ~presentCodecMask; forgottenBits != 0; forgottenBits &= forgottenBits - 1)
		{
			RemoveSentUnitComponent(state, snapshot, held, BitOperations.TrailingZeroCount(forgottenBits), unitEntity.Id, tick);
		}

		ulong describedMask = 0;
		for (ulong presentBits = presentCodecMask; presentBits != 0; presentBits &= presentBits - 1)
		{
			int codecIndex = BitOperations.TrailingZeroCount(presentBits);
			if (!AppendUnitComponentDelta(state, snapshot, held, UnitCodecs[codecIndex], codecIndex, unitEntity, tick, isBaseline)) continue;

			describedMask |= 1UL << codecIndex;
		}

		held.ToldCodecMask = describedMask;
		snapshot.UnitComponentPresence?.Add(new UnitComponentPresence
		{
			ServerEntityId = unitEntity.Id,
			Mask = presenceMask
		});
	}

	private (ulong PresentCodecMask, ulong PresenceMask) DescribeUnitComponentState(Entity unitEntity)
	{
		ulong presentCodecMask = 0;
		ulong presenceMask = 0;

		for (int codecIndex = 0; codecIndex < UnitCodecs.Length; codecIndex++)
		{
			if (!_world.Has(unitEntity, UnitCodecMasks[codecIndex])) continue;

			presentCodecMask |= 1UL << codecIndex;

			int presenceSlot = UnitCodecPresenceSlots[codecIndex];
			if (presenceSlot >= 0) presenceMask |= 1UL << presenceSlot;
		}

		return (presentCodecMask, presenceMask);
	}

	private bool AppendUnitComponentDelta(SnapshotPeerState state, WorldSnapshot snapshot, UnitDescriptionState held, IReplicatedComponentCodec codec, int codecIndex, Entity entity, int tick, bool isBaseline)
	{
		if (!_descriptions.TryGetComponentPayload(tick, entity.Id, codecIndex, out ReadOnlySpan<byte> payload))
		{
			payload = WriteComponentPayload(codec, entity);
			if (payload.Length == 0)
			{
				RemoveSentUnitComponent(state, snapshot, held, codecIndex, entity.Id, tick);
				return false;
			}

			_descriptions.SetComponentPayload(tick, entity.Id, codecIndex, payload);
		}

		// The buffer this peer was last told about the component in is written into again rather than replaced. It holds what
		// this description sends and it is what the next description compares against, so a baseline - which states every
		// unit and every component - allocates nothing for a peer that has already been described once.
		byte[]? previous = held.SentPayloads[codecIndex];
		bool reusable = previous != null && previous.Length == codec.SizeInBytes;
		if (!isBaseline && reusable && previous!.AsSpan().SequenceEqual(payload)) return true;

		byte[] bytes = reusable ? previous! : new byte[codec.SizeInBytes];
		payload.CopyTo(bytes);
		held.SentPayloads[codecIndex] = bytes;
		ForgetPendingRemoval(state, (byte)ReplicatedScope.Unit, entity.Id, codec.ComponentTypeId);

		snapshot.ComponentDeltas.Add(new ComponentDelta
		{
			Key = entity.Id,
			ComponentTypeId = codec.ComponentTypeId,
			Scope = (byte)ReplicatedScope.Unit,
			Bytes = bytes
		});
		return true;
	}

	private static void RemoveSentUnitComponent(SnapshotPeerState state, WorldSnapshot snapshot, UnitDescriptionState held, int codecIndex, int entityId, int sequence)
	{
		if ((held.ToldCodecMask & (1UL << codecIndex)) == 0) return;

		held.SentPayloads[codecIndex] = null;
		snapshot.ComponentDeltas.Add(new ComponentDelta
		{
			Key = entityId,
			ComponentTypeId = UnitCodecs[codecIndex].ComponentTypeId,
			Scope = (byte)ReplicatedScope.Unit,
			Bytes = Array.Empty<byte>()
		});

		state.PendingRemovals.Add(new PendingComponentRemoval((byte)ReplicatedScope.Unit, entityId, UnitCodecs[codecIndex].ComponentTypeId, sequence));
	}

	private ReadOnlySpan<byte> WriteComponentPayload(IReplicatedComponentCodec codec, Entity entity)
	{
		if (_writeScratch.Length < codec.SizeInBytes) _writeScratch = new byte[codec.SizeInBytes];

		Span<byte> scratch = _writeScratch.AsSpan(0, codec.SizeInBytes);
		return codec.TryWrite(_world, entity, scratch) ? scratch : ReadOnlySpan<byte>.Empty;
	}

	private bool AppendComponentDelta(SnapshotPeerState state, WorldSnapshot snapshot, IReplicatedComponentCodec codec, Entity entity, byte scope, int key, int sequence, bool isBaseline)
	{
		if (_writeScratch.Length < codec.SizeInBytes) _writeScratch = new byte[codec.SizeInBytes];

		Span<byte> scratch = _writeScratch.AsSpan(0, codec.SizeInBytes);
		if (!codec.TryWrite(_world, entity, scratch))
		{
			RemoveComponentDeltaWhenSent(state, snapshot, codec, scope, key, sequence);
			return false;
		}

		var deltaKey = (scope, key, codec.ComponentTypeId);
		state.SentComponents.TryGetValue(deltaKey, out byte[]? previous);
		bool reusable = previous != null && previous.Length == codec.SizeInBytes;
		if (!isBaseline && reusable && previous!.AsSpan().SequenceEqual(scratch)) return true;

		byte[] bytes = reusable ? previous! : new byte[codec.SizeInBytes];
		scratch.CopyTo(bytes);
		state.SentComponents[deltaKey] = bytes;
		ForgetPendingRemoval(state, scope, key, codec.ComponentTypeId);

		snapshot.ComponentDeltas.Add(new ComponentDelta
		{
			Key = key,
			ComponentTypeId = codec.ComponentTypeId,
			Scope = scope,
			Bytes = bytes
		});
		return true;
	}

	private static void RemoveComponentDeltaWhenSent(SnapshotPeerState state, WorldSnapshot snapshot, IReplicatedComponentCodec codec, byte scope, int key, int sequence)
	{
		if (!state.SentComponents.Remove((scope, key, codec.ComponentTypeId))) return;

		state.PendingRemovals.Add(new PendingComponentRemoval(scope, key, codec.ComponentTypeId, sequence));

		snapshot.ComponentDeltas.Add(new ComponentDelta
		{
			Key = key,
			ComponentTypeId = codec.ComponentTypeId,
			Scope = scope,
			Bytes = Array.Empty<byte>()
		});
	}
}
