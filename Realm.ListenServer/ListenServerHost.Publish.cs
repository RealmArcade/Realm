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

/// <summary>publishing a tick to every seated peer.</summary>
public partial class ListenServerHost
	{
	private void PublishTick(int sequence)
	{
		ServerSimulation? simulation = _simulation;
		if (simulation == null) return;


		_syncRecorder?.BeginSequence(sequence, simulation.TickCount);

		ServerPresentationEvent[] presentation = simulation.CollectPresentation();
		if (_seatedPeers.Count == 0)
		{
			_syncRecorder?.EndSequence();
			return;
		}

		int peerCount = CopySeatedPeerIds();
		_trafficRecorder?.BeginSequence(sequence);
		for (int index = 0; index < peerCount; index++)
		{
			int seatedPeerId = _seatedPeerIds[index];
			bool resyncRequested = simulation.ConsumeBaselineRequest(seatedPeerId);
			bool repeatRequested = !resyncRequested && simulation.ConsumeDescriptionRepeatRequest(seatedPeerId);
			bool refreshRequested = !resyncRequested
				&& IsRefreshDue(seatedPeerId, simulation.TickCount)
				&& TryClaimBaselineRequest(seatedPeerId, simulation.TickCount);
			_pendingBaselineRequests[index] = resyncRequested || refreshRequested;
			_pendingDescribeInFull[index] = _pendingBaselineRequests[index] || repeatRequested;
			_pendingBaselineReasons[index] = resyncRequested
				? SnapshotBaselineReason.Resync
				: refreshRequested ? SnapshotBaselineReason.Safety : SnapshotBaselineReason.None;
			_pendingSkippedPeers[index] = false;
			DropPeerIfItStoppedReading(seatedPeerId, simulation.TickCount);

			// A description this peer could not apply is state it never held, so its record of what it was told is dropped
			// here, on the tick thread, before any lane reads it: the next description then carries every unit and text
			// rather than only the ones that changed since a description the screen never applied.
			if (repeatRequested) simulation.ResetPeerDescription(seatedPeerId);
		}

		simulation.PreparePeerDescriptions(sequence);

		// Peer work is split over fixed lanes, so a lane owns its builder for the whole match instead of queuing for one.
		// The ability deltas are collected for every peer here, on the tick thread, because the registry behind them keeps
		// one scratch table per process and cannot be entered by two lanes at once.
		int laneCount = Math.Min(peerCount, simulation.SnapshotWorkerCount);
		simulation.PrepareAbilityDeltas(_seatedPeerIds.AsSpan(0, peerCount), _pendingDescribeInFull.AsSpan(0, peerCount), _pendingSkippedPeers.AsSpan(0, peerCount));

		_publishSequence = sequence;

		// A frame left over from the previous tick is not a description of this one. A lane that fails leaves the peers it had
		// not reached holding theirs, and sending one of those under this tick's sequence would tell a peer about a world the
		// match has moved past while filing it, and its repair history, under the wrong sequence.
		for (int peerIndex = 0; peerIndex < peerCount; peerIndex++) _publishedFrames[peerIndex] = null!;

		bool everyLaneDescribedItsPeers = true;

		if (laneCount == 1)
		{
			for (int peerIndex = 0; peerIndex < peerCount; peerIndex++) DescribeAndFrameOnePeer(peerIndex, 0);
		}
		else
		{
			_publishPeerCount = peerCount;
			_publishLaneCount = laneCount;
			_describeOneLane ??= DescribeOneLane;

			// The lanes are the match's own threads, not the pool's: a tick that waits for the pool to schedule its lanes
			// spends an unbounded part of its budget in whatever else this process happens to be doing with the pool.
			if (_publishLaneWorkers == null)
			{
				_publishLaneWorkers = new PublishLaneWorkers(simulation.SnapshotWorkerCount, "Realm publish lane");
				_publishLaneWorkers.Report = _log.Write;
			}

			// The lanes only read the world, and the lease is what says so: a lane that starts writing it instead is
			// reported by the debug assertion a structural change raises rather than corrupting a buffer mid-description.
			EcsWorld world = simulation.Map.World;
			world.EnterWorkerLease();
			try
			{
				everyLaneDescribedItsPeers = _publishLaneWorkers.DescribeInParallel(laneCount, _describeOneLane);
			}
			finally
			{
				world.ExitWorkerLease();
			}
		}

		if (!everyLaneDescribedItsPeers) OweBaselinesForPeersLaneFailureLeftUndescribed(peerCount);

		for (int index = 0; index < peerCount; index++) SendPublishedSnapshot(index);

		_syncRecorder?.EndSequence();

		if (_trafficRecorder != null && sequence % TrafficReportIntervalSequences == 0) QueueTrafficReport();

		if (presentation.Length > 0) SendPresentation(peerCount, presentation);
	}

	/// <summary>
	///     Answers a publish whose lane failed. The peers that lane had not described hold no frame for this tick, so they are
	///     described from scratch on the next one: a hole in their run is repaired by name, and the description that would
	///     fill it was never written.
	/// </summary>
	private void OweBaselinesForPeersLaneFailureLeftUndescribed(int peerCount)
	{
		ServerSimulation? simulation = _simulation;
		if (simulation == null) return;

		for (int index = 0; index < peerCount; index++)
		{
			if (_publishedFrames[index] != null) continue;

			int undescribedPeerId = _seatedPeerIds[index];
			_log.Write($"peer {undescribedPeerId} was not described this tick because a publish lane failed; it is described from scratch next tick");
			simulation.RequestBaseline(undescribedPeerId);
		}
	}

	private void DescribeOneLane(int laneIndex)
	{
		for (int peerIndex = laneIndex; peerIndex < _publishPeerCount; peerIndex += _publishLaneCount)
		{
			DescribeAndFrameOnePeer(peerIndex, laneIndex);
		}
	}

	private void DescribeAndFrameOnePeer(int index, int laneIndex)
	{
		ServerSimulation simulation = _simulation!;
		int peerId = _seatedPeerIds[index];

		// A peer this far behind is told nothing this tick: describing it would be work its screen cannot draw. What it
		// is owed is a baseline, which the tick that decided to skip it already asked for. The slot keeps its description
		// fields - its containers are what the next description is written into - and a null frame is what says it was
		// told nothing.
		if (_pendingSkippedPeers[index])
		{
			_publishedFrames[index] = null!;
			return;
		}

		// The description is written into the slot this peer already has, so describing a room replaces nothing and
		// empties what it held instead of allocating a set of lists per peer per tick.
		simulation.BuildSnapshotForPeerInto(ref _publishedSnapshots[index], peerId, _publishSequence, _pendingBaselineRequests[index], laneIndex, index);
		WorldSnapshot snapshot = _publishedSnapshots[index];

		// Serializing and framing is the host's cost of describing one peer, measured where it is paid, and the serializer
		// writes into a buffer this peer's slot already owns.
		long framingStarted = _trafficRecorder == null ? 0 : System.Diagnostics.Stopwatch.GetTimestamp();
		ArrayBufferWriter<byte> serialized = _serializationWriters[index];
		serialized.ResetWrittenCount();
		MemoryPackSerializer.Serialize(serialized, snapshot);
		SnapshotCompressor compressor = _publishCompressors[laneIndex] ??= new SnapshotCompressor();
		_publishedFrames[index] = snapshot.IsBaseline
			? compressor.EncodeBaseline(serialized.WrittenSpan)
			: compressor.EncodeDelta(serialized.WrittenSpan);
		if (_trafficRecorder == null) return;

		_publishedSerializedBytes[index] = serialized.WrittenCount;
		_publishedFramingMilliseconds[index] = (System.Diagnostics.Stopwatch.GetTimestamp() - framingStarted) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
	}

	private void SendPublishedSnapshot(int index)
	{
		ServerSimulation? simulation = _simulation;
		if (simulation == null) return;

		int peerId = _seatedPeerIds[index];
		if (_publishedFrames[index] == null) return;

		WorldSnapshot snapshot = _publishedSnapshots[index];

		// A description this match would have to split into more pieces than a screen is built to put back together is one
		// it must not send at all. The screen would drop every piece of it, report the hole the missing description left,
		// and be answered with the same unassemblable run on every repair, which is a peer that can never catch up while
		// looking exactly like a link that is losing datagrams. It is described from scratch on the next tick instead,
		// which travels on the reliable stream and is fragmented by the transport rather than by this match.
		if (!snapshot.IsBaseline
		    && _publishedFrames[index].Length > SnapshotFragment.MaximumDatagramBytes
		    && SnapshotFragment.CountFor(_publishedFrames[index].Length) > SnapshotFragment.MaximumFragmentsPerDescription)
		{
			_log.Write($"peer {peerId} would be described in more than the {SnapshotFragment.MaximumFragmentsPerDescription} pieces a screen can reassemble ({_publishedFrames[index].Length} bytes, {SnapshotFragment.MaximumReassembledBytes} is the widest); it is described from scratch instead");
			_publishedFrames[index] = null!;
			simulation.RequestBaseline(peerId);
			return;
		}

		bool isFirstFrameForPeer = !_firstSnapshotSequencePerPeer.ContainsKey(peerId);

		_syncRecorder?.AddPeerSnapshot(peerId, in snapshot);

		if (_trafficRecorder != null)
		{
			SnapshotBaselineReason reason = !snapshot.IsBaseline
				? SnapshotBaselineReason.None
				: isFirstFrameForPeer ? SnapshotBaselineReason.Initial : _pendingBaselineReasons[index];
			_trafficRecorder.RecordFrame(
				peerId,
				_publishSequence,
				_publishedSerializedBytes[index],
				_publishedFrames[index]?.Length ?? 0,
				snapshot.IsBaseline,
				reason,
				_publishedFramingMilliseconds[index],
				simulation.TickCount,
				System.Diagnostics.Stopwatch.GetTimestamp());
		}

		// A delta describes a world the next delta supersedes, so it travels on the stream that would rather be current
		// than complete; a lost one costs the change it carried until the acknowledgement reports the gap. A baseline is
		// the only thing a peer can rebuild from, so it travels reliably whatever its size.
		// A delta too large for one datagram is split rather than moved to the reliable stream: on the reliable stream it
		// would hold every description queued behind it, which on a busy match turns one large frame into the stall that
		// costs the peer everything it was about to be told.
		bool carriedUnreliably = !snapshot.IsBaseline;
		if (snapshot.IsBaseline)
		{
			_transport.Send(peerId, NetworkFrameKind.Snapshot, _publishedFrames[index]);
		}
		else if (_publishedFrames[index].Length <= SnapshotFragment.MaximumDatagramBytes)
		{
			_transport.Send(peerId, NetworkFrameKind.Snapshot, _publishedFrames[index], FrameDelivery.UnreliableOrdered);
		}
		else
		{
			SnapshotFragmentBuffer fragments = _fragmentBuffers[index];
			fragments.Split(_publishedFrames[index], _publishSequence);
			ReadOnlySpan<byte[]> datagrams = fragments.Datagrams;
			for (int datagramIndex = 0; datagramIndex < datagrams.Length; datagramIndex++)
			{
				_transport.Send(peerId, NetworkFrameKind.Snapshot, datagrams[datagramIndex], FrameDelivery.UnreliableOrdered);
			}

			_fragmentedSnapshotCount++;
		}

		if (carriedUnreliably) _unreliableSnapshotCount++;
		else _reliableSnapshotCount++;

		// The description this peer was just sent is kept until the ring's width has passed it, so a hole the
		// acknowledgement reports is answered with the description that went missing rather than with the whole world.
		// The frame is the peer's own buffer and the next tick writes a new one, so the ring holds it unshared.
		FrameRingFor(peerId).Add(_publishSequence, _publishedFrames[index]);

		// The first sequence this peer was described with bounds what its acknowledgement mask can claim: a bit for a
		// sequence the match never sent was never meant to be set. A baseline starts that bound over.
		if (snapshot.IsBaseline || !_firstSnapshotSequencePerPeer.ContainsKey(peerId)) _firstSnapshotSequencePerPeer[peerId] = _publishSequence;

		// What the map declared about the match - cards, colours, labels and the camera it placed - is stated once, to the
		// peer that has just been seated, and only to it. Sending it after every baseline replayed a camera pan the map
		// made at match start every ten seconds and snapped the screen back while the player looked elsewhere.
		if (!snapshot.IsBaseline || !isFirstFrameForPeer || !_seatedPeers.TryGetValue(peerId, out int playerSlot)) return;

		ServerPresentationEvent[] description = simulation.DescribePresentationStateFor(playerSlot);
		if (description.Length > 0) SendPresentationToPeer(peerId, description);
	}
}
