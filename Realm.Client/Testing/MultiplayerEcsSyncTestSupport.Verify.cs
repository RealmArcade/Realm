using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Realm.Client.Services;
using Realm.Ecs.Replication;
using Realm.Shared.Diagnostics;
using Realm.Shared.Network;
using Realm.Shared.Services;

namespace Realm.Client.Testing;

public static partial class MultiplayerEcsSyncTestSupport
{
	public static MultiplayerEcsSyncReport Verify(MultiplayerEcsRunSettings settings, string? clientDirectory = null)
	{
		MultiplayerEcsSyncReport report = new();
		string outputDirectory = settings.OutputDirectory;
		string clientOutputDirectory = clientDirectory ?? outputDirectory;
		report.AddSummaryLine($"Client records: {clientOutputDirectory}");
		string hostPath = Path.Combine(outputDirectory, "ECS_host_sync.bin");
		string clientPath = Path.Combine(clientOutputDirectory, "ECS_client_sync.bin");

		if (!File.Exists(hostPath) || !File.Exists(clientPath))
		{
			report.AddFailure("Sync record files are missing. Host or client never started recording.");
			return report;
		}

		SortedDictionary<int, MultiplayerSnapshotRecord> hostRecords = ReadRecords(hostPath, out string hostRecordTruncation);
		SortedDictionary<int, MultiplayerSnapshotRecord> clientRecords = ReadRecords(clientPath, out string clientRecordTruncation);
		report.HostSnapshotCount = hostRecords.Count;
		report.ClientSnapshotCount = clientRecords.Count;
		if (hostRecordTruncation.Length > 0) report.AddSummaryLine($"Host records: {hostRecordTruncation}.");
		if (clientRecordTruncation.Length > 0) report.AddSummaryLine($"Client records: {clientRecordTruncation}.");

		if (hostRecords.Count == 0 || clientRecords.Count == 0)
		{
			report.AddFailure("No sync records were produced.");
			return report;
		}

		HashSet<int> clientSeenSequences = ReadClientSeenSequences(clientOutputDirectory);
		TryReadSequence(Path.Combine(clientOutputDirectory, "client_final_sequence.txt"), out int clientFinalAppliedSequence);
		HashSet<int> clientPeerIds = ReadClientPeerIds(clientOutputDirectory);
		int inFlightSequenceCount = 0;
		int supersededSequenceCount = 0;

		ReconnectExpectation? reconnectExpectation = ReadReconnectExpectation(settings, clientOutputDirectory, report);
		HashSet<int>? sequencesSentToPeer = reconnectExpectation == null ? ReadHostSentSequences(outputDirectory, clientOutputDirectory) : null;
		List<int> notSentSequences = new();

		int lastRecordedSendSequence = sequencesSentToPeer is { Count: > 0 } ? MaxOf(sequencesSentToPeer) : -1;
		int unrecordedSequenceCount = 0;
		int reconnectOutageSequences = 0;
		int pairedSequencesAfterReconnect = 0;

		int firstPairedSequence = -1;
		List<int> missingBaselineSequences = new();
		List<int> supersededBaselineSequences = new();
		List<string> mismatches = new();
		int pairedSnapshots = 0;
		int comparedUnits = 0;
		int offscreenMissingUnits = 0;
		int lastPairedSequence = -1;
		int unpairedSequenceCount = 0;
		List<int> lostSequences = new();
		List<int> startupCatchUpSequences = new();
		List<string> transientDigestMismatches = new();
		int transientDigestMismatchCount = 0;
		List<(int Sequence, int Count)> transientMismatchSequences = new();
		List<(int EntityId, string Field, int Sequence, string Message)> deltaDivergences = new();
		List<(int EntityId, string Field, int Sequence, string Message)> membershipDivergences = new();
		int comparedLootEntities = 0;
		ReferenceDigestCoverage referenceCoverage = new();
		int comparedNavigationBaselines = 0;
		int pathingMismatchCount = 0;
		int navMeshMissingCount = 0;
		int navMeshShapeDifferenceCount = 0;
		int navMeshAgreedRecords = 0;
		int navMeshDisagreementAfterAgreementCount = 0;
		int navMeshDisagreementBeforeAgreementCount = 0;
		int navMeshAwaitingObstacleInputCount = 0;
		int navMeshObstacleInputRun = 0;
		int longestObstacleInputRun = 0;
		int firstNavMeshAgreementSequence = -1;
		int lastAgreedHostPolyCount = 0;
		int lastAgreedHostGroundPolyCount = 0;
		int lastAgreedClientPolyCount = 0;
		int lastAgreedClientGroundPolyCount = 0;
		int lastAgreedObstacleVolumeCount = 0;
		List<string> navMeshObservations = new();
		List<int> hostNavigationChangeSequences = new();
		MultiplayerNavigationState? previousHostNavigation = null;
		int navMeshObstacleInputAllowance = ResolveObstacleInputAllowanceSequences(settings);

		foreach (KeyValuePair<int, MultiplayerSnapshotRecord> hostEntry in hostRecords)
		{
			if (!clientRecords.TryGetValue(hostEntry.Key, out MultiplayerSnapshotRecord? clientRecord))
			{
				if (clientFinalAppliedSequence >= 0 && hostEntry.Key > clientFinalAppliedSequence)
				{
					inFlightSequenceCount++;
					continue;
				}

				if (firstPairedSequence >= 0)
				{
					unpairedSequenceCount++;

					if (reconnectExpectation != null && reconnectExpectation.Contains(hostEntry.Key))
					{
						reconnectOutageSequences++;
						continue;
					}

					if (clientSeenSequences.Contains(hostEntry.Key)) supersededSequenceCount++;
					else if (hostEntry.Key <= firstPairedSequence + StartupWarmupSequenceCount) startupCatchUpSequences.Add(hostEntry.Key);
					else if (sequencesSentToPeer != null && hostEntry.Key > lastRecordedSendSequence) unrecordedSequenceCount++;
					else if (sequencesSentToPeer != null && !sequencesSentToPeer.Contains(hostEntry.Key)) notSentSequences.Add(hostEntry.Key);
					else lostSequences.Add(hostEntry.Key);
					if (ContainsAnyPeerId(hostEntry.Value.BaselinePeerIds, clientPeerIds))
					{
						if (clientSeenSequences.Contains(hostEntry.Key)) supersededBaselineSequences.Add(hostEntry.Key);
						else missingBaselineSequences.Add(hostEntry.Key);
					}
				}

				continue;
			}

			if (firstPairedSequence < 0) firstPairedSequence = hostEntry.Key;
			lastPairedSequence = hostEntry.Key;
			pairedSnapshots++;
			if (reconnectExpectation != null && hostEntry.Key > reconnectExpectation.ResumeSequence) pairedSequencesAfterReconnect++;

			bool isReconnectBaselineRecord = reconnectExpectation != null && hostEntry.Key == reconnectExpectation.ResumeBaselineSequence;
			bool hostSentBaselineToThisPeer = clientPeerIds.Count > 0
				? ContainsAnyPeerId(hostEntry.Value.BaselinePeerIds, clientPeerIds)
				: hostEntry.Value.IsBaseline;
			if (hostSentBaselineToThisPeer != clientRecord.IsBaseline && !isReconnectBaselineRecord)
			{
				AddMismatch(mismatches, mismatches.Count, $"seq {hostEntry.Key}: baseline flag host={hostSentBaselineToThisPeer} (peer {string.Join('/', clientPeerIds)}) client={clientRecord.IsBaseline}.");
			}

			List<string> digestTarget = hostSentBaselineToThisPeer ? mismatches : transientDigestMismatches;
			int digestMismatchCountBefore = transientDigestMismatches.Count;
			ulong clientPeerMask = BuildPeerMask(hostEntry.Value.DescribedPeerIds, clientPeerIds);
			// Loot is described whenever its signature changes rather than only when the world is stated whole, so it is
			// counted on every paired record: a run that compares it only at baselines says nothing about the loot that
			// appeared since one, which is most of it.
			foreach (MultiplayerLootState describedLoot in hostEntry.Value.Loot.Values)
			{
				if (describedLoot.NamedByPeers == SyncRecordFormat.NamedByEveryPeer || (describedLoot.NamedByPeers & clientPeerMask) != 0) comparedLootEntities++;
			}

			CompareSnapshotRecord(hostEntry.Value, clientRecord, hostSentBaselineToThisPeer, mismatches, digestTarget, ref comparedUnits, ref offscreenMissingUnits, deltaDivergences, membershipDivergences, clientPeerMask, referenceCoverage);
			CompareScopeDigests(hostEntry.Value, clientRecord, clientPeerMask, digestTarget);
			int transientAddedForSequence = transientDigestMismatches.Count - digestMismatchCountBefore;
			transientDigestMismatchCount += transientAddedForSequence;
			if (transientAddedForSequence > 0) transientMismatchSequences.Add((hostEntry.Key, transientAddedForSequence));

			if (hostSentBaselineToThisPeer)
			{
				if (previousHostNavigation != null && !IsSameNavigation(previousHostNavigation, hostEntry.Value.Navigation))
				{
					hostNavigationChangeSequences.Add(hostEntry.Key);
				}

				previousHostNavigation = hostEntry.Value.Navigation;
				comparedNavigationBaselines++;
				CompareNavigationState(hostEntry.Value.Sequence, hostEntry.Value.Navigation, clientRecord.Navigation, mismatches, navMeshObservations, ref pathingMismatchCount, ref navMeshMissingCount, ref navMeshShapeDifferenceCount);
			}

			if (hostEntry.Value.Navigation.HasNavMesh && clientRecord.Navigation.HasNavMesh)
			{
				bool shapeMatches = hostEntry.Value.Navigation.PolyCount == clientRecord.Navigation.PolyCount
					&& hostEntry.Value.Navigation.GroundPolyCount == clientRecord.Navigation.GroundPolyCount
					&& hostEntry.Value.Navigation.PolyHash == clientRecord.Navigation.PolyHash;
				if (shapeMatches)
				{
					navMeshObstacleInputRun = 0;
					navMeshAgreedRecords++;
					lastAgreedHostPolyCount = hostEntry.Value.Navigation.PolyCount;
					lastAgreedHostGroundPolyCount = hostEntry.Value.Navigation.GroundPolyCount;
					lastAgreedClientPolyCount = clientRecord.Navigation.PolyCount;
					lastAgreedClientGroundPolyCount = clientRecord.Navigation.GroundPolyCount;
					lastAgreedObstacleVolumeCount = hostEntry.Value.Navigation.ObstacleVolumeCount;
					if (firstNavMeshAgreementSequence < 0) firstNavMeshAgreementSequence = hostEntry.Key;
				}
				else if (firstNavMeshAgreementSequence >= 0)
				{
					bool obstacleInputDiffers = hostEntry.Value.Navigation.ObstacleVolumeCount != clientRecord.Navigation.ObstacleVolumeCount
						|| hostEntry.Value.Navigation.ObstacleHash != clientRecord.Navigation.ObstacleHash;
					if (obstacleInputDiffers && ++navMeshObstacleInputRun <= navMeshObstacleInputAllowance)
					{
						navMeshAwaitingObstacleInputCount++;
						if (navMeshObstacleInputRun > longestObstacleInputRun) longestObstacleInputRun = navMeshObstacleInputRun;
						continue;
					}

					navMeshDisagreementAfterAgreementCount++;
					if (navMeshObservations.Count < 20)
					{
						navMeshObservations.Add($"seq {hostEntry.Key}: the mesh agreed at seq {firstNavMeshAgreementSequence} and differs here: host=(polys {hostEntry.Value.Navigation.PolyCount}, ground {hostEntry.Value.Navigation.GroundPolyCount}, hash {hostEntry.Value.Navigation.PolyHash:x8}, obstacle volumes {hostEntry.Value.Navigation.ObstacleVolumeCount}, volumes {hostEntry.Value.Navigation.ObstacleHash:x8}) client=(polys {clientRecord.Navigation.PolyCount}, ground {clientRecord.Navigation.GroundPolyCount}, hash {clientRecord.Navigation.PolyHash:x8}, obstacle volumes {clientRecord.Navigation.ObstacleVolumeCount}, volumes {clientRecord.Navigation.ObstacleHash:x8}) after {navMeshObstacleInputRun} record(s) of differing obstacle input.");
					}
				}
				else
				{
					navMeshDisagreementBeforeAgreementCount++;
				}
			}
		}

		report.PairedSnapshotCount = pairedSnapshots;
		report.ComparedUnitCount = comparedUnits;
		report.ComparedLootCount = comparedLootEntities;
		report.AddSummaryLine($"First paired sequence: {firstPairedSequence}");
		report.AddSummaryLine($"Last paired sequence: {lastPairedSequence}");
		int lostSequenceCount = lostSequences.Count;
		report.AddSummaryLine($"Host sequences missing on client after first pairing: {unpairedSequenceCount} (superseded by a newer snapshot: {supersededSequenceCount}, startup catch-up: {startupCatchUpSequences.Count}, the host never sent them to this peer: {notSentSequences.Count}, after the host's coverage record ended: {unrecordedSequenceCount}, sent but never received: {lostSequenceCount})");
		report.AddSummaryLine($"Snapshots still in flight when the client stopped recording: {inFlightSequenceCount} (after client sequence {clientFinalAppliedSequence})");
		report.AddSummaryLine($"Missing baseline sequences: {(missingBaselineSequences.Count == 0 ? "none" : string.Join(", ", missingBaselineSequences))}");
		report.AddSummaryLine($"Baselines the peer received but had already superseded with newer deltas: {supersededBaselineSequences.Count} ({DescribeSequenceList(supersededBaselineSequences)})");
		report.AddSummaryLine($"Startup catch-up sequences lost while the client applied its first baseline: {startupCatchUpSequences.Count} of the first {StartupWarmupSequenceCount} sequences ({DescribeSequenceBursts(startupCatchUpSequences)})");
		report.AddSummaryLine($"Lost sequence bursts after startup: {DescribeSequenceBursts(lostSequences)}");
		report.AddSummaryLine($"Off-screen host units absent on the client as expected: {offscreenMissingUnits}");
		report.AddSummaryLine($"Loot entities compared on paired records: {comparedLootEntities}");
		report.AddSummaryLine($"Navigation parity: compared {comparedNavigationBaselines} baselines, pathing grid mismatches {pathingMismatchCount}, baselines missing a navmesh {navMeshMissingCount}");
		report.AddSummaryLine($"Navmesh shape differences: {navMeshShapeDifferenceCount} of {comparedNavigationBaselines} baselines (a peer bakes the obstacle volumes the authority reports, so a peer's mesh must be the authority's mesh polygon for polygon)");
		report.AddSummaryLine($"Navmesh on every paired record: agreed on {navMeshAgreedRecords} records from seq {firstNavMeshAgreementSequence} onward, differed on {navMeshDisagreementBeforeAgreementCount} before the two sides first agreed (the peer's first bake) and on {navMeshDisagreementAfterAgreementCount} after; the agreed mesh is host {lastAgreedHostPolyCount} polygons ({lastAgreedHostGroundPolyCount} ground) and peer {lastAgreedClientPolyCount} polygons ({lastAgreedClientGroundPolyCount} ground) from {lastAgreedObstacleVolumeCount} obstacle volumes");
		report.AddSummaryLine($"Navmesh waiting for the authority's obstacle volumes: {navMeshAwaitingObstacleInputCount} records, longest run {longestObstacleInputRun} of {navMeshObstacleInputAllowance} allowed (the authority compares volumes once a second and a peer bakes the set it was last told, so a difference over a changed set is the report travelling rather than a divergent bake)");
		for (int index = 0; index < Math.Min(navMeshObservations.Count, 3); index++)
		{
			report.AddSummaryLine($"  {navMeshObservations[index]}");
		}
		report.AddSummaryLine($"Host navigation changes between baselines: {hostNavigationChangeSequences.Count} ({DescribeSequenceList(hostNavigationChangeSequences)})");
		report.AddSummaryLine($"Differences seen only on delta snapshots and healed by the next baseline: {transientDigestMismatchCount}");
		for (int index = 0; index < transientMismatchSequences.Count; index++)
		{
			report.AddSummaryLine($"  seq {transientMismatchSequences[index].Sequence}: {transientMismatchSequences[index].Count} difference(s)");
		}

		for (int index = 0; index < Math.Min(transientDigestMismatches.Count, 16); index++)
		{
			report.AddSummaryLine($"  {transientDigestMismatches[index]}");
		}

		if (pairedSnapshots < 3) report.AddFailure($"Only {pairedSnapshots} snapshot sequences were paired between host and client.");
		if (comparedUnits < 20) report.AddFailure($"Only {comparedUnits} unit states were compared, which is not enough to validate sync.");
		if (missingBaselineSequences.Count > 0)
		{
			report.AddFailure($"{missingBaselineSequences.Count} baseline snapshots produced by the host were never applied by the client.");
		}

		ReportNotSentSequences(report, notSentSequences, firstPairedSequence);
		ReportDivergenceStreaks(report, deltaDivergences, ResolveDeltaDivergenceRecordAllowance(settings), "Unit fields that diverged on a delta record", "retransmit window");
		ReportDivergenceStreaks(report, membershipDivergences, MembershipRecordAllowanceSequences, "Units the client had not built yet", "spawn window");

		int tolerance = ResolveLostSequenceTolerance(settings, pairedSnapshots, lostSequenceCount);
		if (lostSequenceCount > tolerance)
		{
			report.AddFailure($"{lostSequenceCount} host snapshot sequences never reached the client after startup (allowed {tolerance}); {supersededSequenceCount} more were received but superseded by a newer snapshot. Per-tick snapshots are several kilobytes, so they span multiple unreliable fragments and any lost fragment drops the whole snapshot; only a spike far above that, or a lost baseline, means the transport regressed.");
		}
		else if (lostSequenceCount > 0)
		{
			report.AddSummaryLine($"Unreliable snapshot loss stayed within the fragmented-channel tolerance: {lostSequenceCount} of {pairedSnapshots + lostSequenceCount} sequences ({100.0 * lostSequenceCount / Math.Max(1, pairedSnapshots + lostSequenceCount):F1}%) were lost, all repaired by later snapshots and baselines.");
		}
		else
		{
			report.AddSummaryLine($"No unreliable snapshot was lost after startup; every host sequence in the steady-state window reached the client and {supersededSequenceCount} were superseded by a newer snapshot.");
		}

		if (reconnectExpectation != null)
		{
			reconnectExpectation.Report(report, reconnectOutageSequences, pairedSequencesAfterReconnect);
		}

		if (comparedNavigationBaselines == 0 && navMeshAgreedRecords == 0)
		{
			report.AddFailure("No snapshot carried a navigation digest, so navmesh parity was not verified.");
		}

		if (pathingMismatchCount > 0)
		{
			report.AddFailure($"{pathingMismatchCount} baselines had a terrain pathing grid that differs from the host's.");
		}

		if (navMeshMissingCount > 0)
		{
			report.AddFailure($"{navMeshMissingCount} baselines had no baked navmesh on one side, so a route could not be planned there.");
		}

		if (navMeshShapeDifferenceCount > 0)
		{
			report.AddFailure($"{navMeshShapeDifferenceCount} of {comparedNavigationBaselines} baselines had a mesh whose polygons differ from the host's, so the same path query can answer with a different corridor.");
		}

		if (navMeshAgreedRecords == 0)
		{
			report.AddFailure("The peer's navigation mesh never matched the host's on any paired record, so pathing parity rests on the pathing grid alone.");
		}

		if (navMeshDisagreementAfterAgreementCount > 0)
		{
			report.AddFailure($"{navMeshDisagreementAfterAgreementCount} paired records had a mesh that differed from the host's after the two sides had already agreed on one, so one side re-baked from different obstacle input.");
		}

		report.AddSummaryLine(
			$"Reference digests: compared {referenceCoverage.Compared} and passed over {referenceCoverage.Skipped} whose target the authority did not describe to this peer (a reference is a statement only the peers it was sent to can be held to).");

		if (mismatches.Count > 0)
		{
			report.AddFailure($"{mismatches.Count} synced state mismatches were detected.");
			report.AddSummaryLine("First mismatches:");
			foreach (string mismatch in mismatches) report.AddSummaryLine($"  {mismatch}");
		}

		AppendConvergenceReport(report, settings, lastPairedSequence, pairedSnapshots, lostSequenceCount, missingBaselineSequences.Count, clientOutputDirectory);
		ComparePresentationRecords(clientOutputDirectory, hostRecords, report, settings);
		return report;
	}

	private static int ResolveLostSequenceTolerance(MultiplayerEcsRunSettings settings, int pairedSnapshots, int lostSequenceCount)
	{
		int allowancePercent = settings.Profile switch
		{
			NetworkProfile.Clean => 8,
			NetworkProfile.Latency => 12,
			NetworkProfile.LatencyJitter => 15,
			NetworkProfile.LatencyLoss => 45,
			_ => 12
		};

		int producedSequences = pairedSnapshots + lostSequenceCount;
		return Math.Max(10, producedSequences * allowancePercent / 100);
	}

}
