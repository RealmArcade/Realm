using System.Buffers;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using MemoryPack;
using Realm.Ecs;
using Realm.Ecs.Replication;
using Realm.Ecs.Services;
using Realm.Shared;
using Realm.Shared.Diagnostics;
using Realm.Shared.Maps;
using Realm.Shared.Network;
using Realm.Shared.Simulation;

namespace Realm.ListenServer;

/// <summary>answering a peer that could not apply a description.</summary>
public partial class ListenServerHost
	{
	/// <summary>
	///     Answers a screen that received a description it could not apply. The description is not sent again: it arrived,
	///     and whatever this screen could not make of it would be made again of the same bytes. What it carried is state the
	///     screen does not hold, so the match forgets what it was told and describes all of it once more.
	/// </summary>
	private void HandleDescriptionRepeatRequest(int peerId)
	{
		ServerSimulation? simulation = _simulation;
		if (simulation == null) return;

		simulation.RequestDescriptionRepeat(peerId);
		_log.Write($"peer {peerId} could not apply a description; it is told about every unit and text again next tick");
	}

	/// <summary>
	///     Answers a peer that names a run of descriptions it never received. The run is only answered while this match still
	///     holds every one of them: a run wider than the ring, or one the ring has already passed, says the peer stopped
	///     reading rather than that a datagram went missing, and nothing short of a baseline puts that screen back.
	/// </summary>
	private void HandleSnapshotRepairRequest(int peerId, ReadOnlySpan<byte> payload)
	{
		ServerSimulation? simulation = _simulation;
		if (simulation == null) return;

		SnapshotRepairRequest request;
		try
		{
			request = MemoryPackSerializer.Deserialize<SnapshotRepairRequest>(payload);
		}
		catch (Exception exception) when (exception is IOException or InvalidDataException or ArgumentException)
		{
			_log.Write($"peer {peerId} sent a repair request that could not be read: {exception.Message}");
			return;
		}

		if (request.Count <= 0 || request.Count > SnapshotFrameRing.Capacity || request.FirstSequence <= 0) return;

		long tick = simulation.TickCount;
		if (_lastRepairSentTickPerPeer.TryGetValue(peerId, out long lastRepairTick) && tick - lastRepairTick < RepairCooldownTicks) return;

		Span<int> missingSequences = stackalloc int[request.Count];
		for (int index = 0; index < missingSequences.Length; index++) missingSequences[index] = request.FirstSequence + index;

		if (TryResendMissingDescriptions(peerId, missingSequences))
		{
			_lastRepairSentTickPerPeer[peerId] = tick;
			_log.Write($"peer {peerId} asked for {request.Count} description(s) from sequence {request.FirstSequence}; they are sent again");
			return;
		}

		if (!TryClaimBaselineRequest(peerId, tick)) return;

		simulation.RequestBaseline(peerId);
		_log.Write($"peer {peerId} asked for a run this match no longer holds; it is described from scratch");
	}

	/// <summary>
	///     Sends each of these descriptions again, exactly as the peer was first sent them. Every one is checked before any
	///     is sent, because half a repaired run is a peer told the wrong thing about where the world is.
	/// </summary>
	private bool TryResendMissingDescriptions(int peerId, ReadOnlySpan<int> missingSequences)
	{
		if (missingSequences.Length == 0) return false;
		if (!_frameRings.TryGetValue(peerId, out SnapshotFrameRing? ring)) return false;

		for (int index = 0; index < missingSequences.Length; index++)
		{
			if (!ring.TryGet(missingSequences[index], out _)) return false;
		}

		for (int index = 0; index < missingSequences.Length; index++)
		{
			ring.TryGet(missingSequences[index], out byte[] frame);

			// A repair is asked for once and answered over the stream that cannot lose it: the peer has already shown
			// that it is missing state, and losing the repair as well would cost another round trip to learn the same.
			_transport.Send(peerId, NetworkFrameKind.Snapshot, frame, FrameDelivery.Reliable);
		}

		return true;
	}

	/// <summary>
	///     The ring this peer's descriptions are kept in, created the first time the peer is described. A ring belongs to
	///     the peer rather than to the seat it was published in, so no seat taken or given up elsewhere can address it.
	/// </summary>
	private SnapshotFrameRing FrameRingFor(int peerId)
	{
		if (_frameRings.TryGetValue(peerId, out SnapshotFrameRing? ring)) return ring;

		ring = new SnapshotFrameRing(peerId);
		_frameRings[peerId] = ring;
		return ring;
	}
}
