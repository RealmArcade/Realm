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
	private const int WarmFramingUnitCount = 32;

	private void PublishTick(int sequence)
	{
		ServerSimulation? simulation = _simulation;
		if (simulation == null) return;

		if (_startupSettlesAtSequence > 0 && sequence >= _startupSettlesAtSequence)
		{
			_startupSettlesAtSequence = 0;
			LagProbe.MarkStartupComplete();
		}

		long publishStarted = LagProbe.IsEnabled ? LagProbe.Timestamp() : 0L;

		// The work a publish does between the peer descriptions is measured in three pieces beside them, because a publish
		// that costs sixty milliseconds has to be told apart from twenty peer descriptions that each cost three: the
		// description stages only account for the peers, and everything before and after them runs once whatever the room
		// holds.
		long recordStarted = LagProbe.IsEnabled ? LagProbe.Timestamp() : 0L;
		_syncRecorder?.BeginSequence(sequence, simulation.TickCount);
		if (recordStarted != 0L) LagProbe.Add(LagStage.AuthorityRecordMilliseconds, LagProbe.ElapsedMilliseconds(recordStarted));

		long presentationStarted = LagProbe.IsEnabled ? LagProbe.Timestamp() : 0L;
		ReadOnlySpan<ServerPresentationEvent> presentation = simulation.CollectPresentation();
		if (presentationStarted != 0L) LagProbe.Add(LagStage.AuthorityPresentationMilliseconds, LagProbe.ElapsedMilliseconds(presentationStarted));
		if (_seatedPeers.Count == 0)
		{
			recordStarted = LagProbe.IsEnabled ? LagProbe.Timestamp() : 0L;
			_syncRecorder?.EndSequence();
			if (recordStarted != 0L) LagProbe.Add(LagStage.AuthorityRecordMilliseconds, LagProbe.ElapsedMilliseconds(recordStarted));
			return;
		}

		long prepareStarted = LagProbe.IsEnabled ? LagProbe.Timestamp() : 0L;
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

		// The prepare phase is serial, so it is measured in its parts: what it costs to bookkeep twelve peers, what it
		// costs to collect the board they all share, and what it costs to collect the ability deltas each of them is owed.
		// A total alone cannot say which of the three is the reason a full room publishes at half its cadence.
		if (prepareStarted != 0L) LagProbe.Add(LagStage.AuthorityPrepareBookkeepingMilliseconds, LagProbe.ElapsedMilliseconds(prepareStarted));

		long boardStarted = LagProbe.IsEnabled ? LagProbe.Timestamp() : 0L;
		simulation.PreparePeerDescriptions(sequence);
		if (boardStarted != 0L) LagProbe.Add(LagStage.AuthorityPrepareBoardMilliseconds, LagProbe.ElapsedMilliseconds(boardStarted));

		// Peer work is split over fixed lanes, so a lane owns its builder for the whole match instead of queuing for one.
		// The ability deltas are collected for every peer here, on the tick thread, because the registry behind them keeps
		// one scratch table per process and cannot be entered by two lanes at once.
		long abilitiesStarted = LagProbe.IsEnabled ? LagProbe.Timestamp() : 0L;
		int laneCount = Math.Min(peerCount, simulation.SnapshotWorkerCount);
		simulation.PrepareAbilityBookkeeping();
		if (abilitiesStarted != 0L) LagProbe.Add(LagStage.AuthorityPrepareAbilityBookkeepingMilliseconds, LagProbe.ElapsedMilliseconds(abilitiesStarted));

		long peerAbilitiesStarted = LagProbe.IsEnabled ? LagProbe.Timestamp() : 0L;
		simulation.PrepareAbilityDeltas(_seatedPeerIds.AsSpan(0, peerCount), _pendingDescribeInFull.AsSpan(0, peerCount), _pendingSkippedPeers.AsSpan(0, peerCount));
		if (peerAbilitiesStarted != 0L) LagProbe.Add(LagStage.AuthorityPrepareAbilitiesMilliseconds, LagProbe.ElapsedMilliseconds(peerAbilitiesStarted));

		_publishSequence = sequence;

		// A frame left over from the previous tick is not a description of this one. A lane that fails leaves the peers it had
		// not reached holding theirs, and sending one of those under this tick's sequence would tell a peer about a world the
		// match has moved past while filing it, and its repair history, under the wrong sequence.
		for (int peerIndex = 0; peerIndex < peerCount; peerIndex++) _publishedFrames[peerIndex] = null!;
		if (prepareStarted != 0L) LagProbe.Add(LagStage.AuthorityPrepareMilliseconds, LagProbe.ElapsedMilliseconds(prepareStarted));

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

		recordStarted = LagProbe.IsEnabled ? LagProbe.Timestamp() : 0L;
		_syncRecorder?.EndSequence();
		if (recordStarted != 0L) LagProbe.Add(LagStage.AuthorityRecordMilliseconds, LagProbe.ElapsedMilliseconds(recordStarted));

		if (_trafficRecorder != null && sequence % TrafficReportIntervalSequences == 0) QueueTrafficReport();

		if (presentation.Length > 0) SendPresentation(peerCount, presentation);

		if (publishStarted != 0L)
		{
			LagProbe.Add(LagStage.AuthorityPublishMilliseconds, LagProbe.ElapsedMilliseconds(publishStarted));
			ObserveSequencesPerSecond(sequence);
		}
	}

	/// <summary>
	///     Frames one throwaway description the way a baseline is framed, before anything is published. The first baseline
	///     a match sends is the first time MemoryPack writes a whole unit section and the first time a lane's compressor is
	///     asked for a baseline: that work is compiled and initialised once, and measuring it showed it costs about thirty
	///     milliseconds on its first call and two on every one after. Paying it before the clock starts is what keeps the
	///     tick a screen joins on inside a tick's budget.
	/// </summary>
	private void WarmSnapshotFraming()
	{
		WorldSnapshot warm = default;
		warm.ResetForDescription(0, true, 0);
		for (int index = 0; index < WarmFramingUnitCount; index++)
		{
			warm.Units.Add(new UnitSnapshot
			{
				EntityId = index + 1,
				UnitId = "warm",
				OwnerPlayerEntityId = index + 1,
				OwnerPlayerIndex = 0,
				Position = new NetworkVector3(index, 0f, 0f),
				RotationY = 0f,
				CurrentHp = 1f,
				MaxHp = 1f
			});
		}

		ArrayBufferWriter<byte> serialized = _serializationWriters[0];
		serialized.ResetWrittenCount();
		MemoryPackSerializer.Serialize(serialized, warm);

		for (int lane = 0; lane < _publishCompressors.Length; lane++)
		{
			SnapshotCompressor compressor = _publishCompressors[lane] ??= new SnapshotCompressor();
			compressor.EncodeDelta(serialized.WrittenSpan);
			compressor.EncodeBaseline(serialized.WrittenSpan);
		}

		serialized.ResetWrittenCount();
	}

	private double _sequencesPerSecondStartedSeconds;
	private int _sequencesPerSecondFirstSequence = -1;

	private void ObserveSequencesPerSecond(int sequence)
	{
		double seconds = _startClock.Elapsed.TotalSeconds;
		if (_sequencesPerSecondFirstSequence < 0)
		{
			_sequencesPerSecondFirstSequence = sequence;
			_sequencesPerSecondStartedSeconds = seconds;
			return;
		}

		double elapsed = seconds - _sequencesPerSecondStartedSeconds;
		if (elapsed < 1d) return;

		LagProbe.ObserveGauge(LagGauge.AuthoritySequencesPerSecond, (sequence - _sequencesPerSecondFirstSequence) / elapsed, seconds);
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
			LagProbe.Count(LagEvent.AuthorityBaselinesBecausePeerWasUndescribed);
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
		long describeStarted = LagProbe.IsEnabled ? LagProbe.Timestamp() : 0L;
		simulation.BuildSnapshotForPeerInto(ref _publishedSnapshots[index], peerId, _publishSequence, _pendingBaselineRequests[index], laneIndex, index);
		if (describeStarted != 0L) LagProbe.Add(LagStage.AuthorityDescribeMilliseconds, LagProbe.ElapsedMilliseconds(describeStarted));
		WorldSnapshot snapshot = _publishedSnapshots[index];

		// Serializing and framing is the host's cost of describing one peer, measured where it is paid, and the serializer
		// writes into a buffer this peer's slot already owns.
		long framingStarted = System.Diagnostics.Stopwatch.GetTimestamp();
		ArrayBufferWriter<byte> serialized = _serializationWriters[index];
		serialized.ResetWrittenCount();
		MemoryPackSerializer.Serialize(serialized, snapshot);
		long packingFinished = System.Diagnostics.Stopwatch.GetTimestamp();
		SnapshotCompressor compressor = _publishCompressors[laneIndex] ??= new SnapshotCompressor();
		_publishedFrames[index] = snapshot.IsBaseline
			? compressor.EncodeBaseline(serialized.WrittenSpan)
			: compressor.EncodeDelta(serialized.WrittenSpan);
		long encodingFinished = System.Diagnostics.Stopwatch.GetTimestamp();
		_publishedSerializedBytes[index] = serialized.WrittenCount;
		_publishedFramingMilliseconds[index] = (encodingFinished - framingStarted) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
		LagProbe.Add(LagStage.AuthoritySerializeMilliseconds, _publishedFramingMilliseconds[index]);

		// Packing the description and compressing it are told apart because a baseline is where both are paid at a size a
		// delta never reaches, and they are answered by different work: one is this match's own object graph, the other is
		// the compressor's effort.
		if (LagProbe.IsEnabled)
		{
			LagProbe.Add(LagStage.AuthorityPackMilliseconds, (packingFinished - framingStarted) * 1000.0 / System.Diagnostics.Stopwatch.Frequency);
			LagProbe.Add(LagStage.AuthorityCompressMilliseconds, (encodingFinished - packingFinished) * 1000.0 / System.Diagnostics.Stopwatch.Frequency);
		}
	}

	private void SendPublishedSnapshot(int index)
	{
		ServerSimulation? simulation = _simulation;
		if (simulation == null) return;

		int peerId = _seatedPeerIds[index];
		if (_publishedFrames[index] == null)
		{
			// A tick that describes nothing to a peer leaves a hole in the run it was told, which it pays for with a repair
			// or the world itself, so every one of them is counted where it happens rather than inferred from the screen.
			LagProbe.Count(LagEvent.AuthorityPeerToldNothing);
			return;
		}

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
			LagProbe.Count(LagEvent.AuthorityBaselinesBecauseDescriptionTooWide);
			simulation.RequestBaseline(peerId);
			return;
		}

		bool isFirstFrameForPeer = !_firstSnapshotSequencePerPeer.ContainsKey(peerId);

		_syncRecorder?.AddPeerSnapshot(peerId, in snapshot);

		SnapshotBaselineReason reason = !snapshot.IsBaseline
			? SnapshotBaselineReason.None
			: isFirstFrameForPeer ? SnapshotBaselineReason.Initial : _pendingBaselineReasons[index];

		if (LagProbe.IsEnabled)
		{
			if (!snapshot.IsBaseline)
			{
				LagProbe.Count(LagEvent.AuthorityDeltaPublished);
			}
			else
			{
				LagProbe.Count(LagEvent.AuthorityBaselinesPublished);
				if (reason == SnapshotBaselineReason.Safety) LagProbe.Count(LagEvent.AuthorityBaselinesBecausePeerBehind);
				else if (reason == SnapshotBaselineReason.Initial) LagProbe.Count(LagEvent.AuthorityBaselinesPublished);
				else LagProbe.Count(LagEvent.AuthorityBaselinesBecausePeerAsked);
			}
		}

		if (_trafficRecorder != null)
		{
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
		long sendStarted = LagProbe.IsEnabled ? LagProbe.Timestamp() : 0L;

		// A peer that has named a lost description is sent the description before this one again, ahead of this one so the
		// ordered stream keeps both: the copy a tick behind fills a description that went missing without a round trip,
		// and one that did arrive is discarded by the stream as older than what it already delivered. Only a description
		// that fits a datagram is repeated - one that already needs fragments is where a second copy costs real bandwidth,
		// and a lost fragment is not what a repeat of the whole frame answers.
		if (!snapshot.IsBaseline && _peersThatLoseDescriptions.Contains(peerId))
		{
			// A link that has stopped asking for lost descriptions has stopped losing them, and every description sent
			// twice after that is bandwidth spent on a fault that is gone.
			if (_lastRepairSentTickPerPeer.TryGetValue(peerId, out long lastRepairTick)
			    && simulation.TickCount - lastRepairTick > LossyPeerMemoryTicks)
			{
				_peersThatLoseDescriptions.Remove(peerId);
				_descriptionsLostByPeer.Remove(peerId);
				_lossWindowStartedTickPerPeer.Remove(peerId);
			}

			if (_peersThatLoseDescriptions.Contains(peerId)
			    && _publishSequence > 1
			    && FrameRingFor(peerId).TryGet(_publishSequence - 1, out byte[] previousFrame)
			    && previousFrame != null
			    && previousFrame.Length <= SnapshotFragment.MaximumDatagramBytes)
			{
				_transport.Send(peerId, NetworkFrameKind.Snapshot, previousFrame, FrameDelivery.UnreliableOrdered);
				LagProbe.Count(LagEvent.AuthorityDuplicatedDescriptions);
			}
		}

		if (snapshot.IsBaseline)
		{
			if (_startupSettlesAtSequence == 0) _startupSettlesAtSequence = _publishSequence + MatchTiming.SimulationTickRateHz * 2;
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
			LagProbe.Count(LagEvent.AuthorityFragmentedDescriptions);
		}

		if (sendStarted != 0L) LagProbe.Add(LagStage.AuthoritySendMilliseconds, LagProbe.ElapsedMilliseconds(sendStarted));
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
