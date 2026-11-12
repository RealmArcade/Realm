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
	private class ReconnectExpectation
	{
		public int DropSequence { get; init; }
		public int ResumeSequence { get; init; }
		public int ResumeBaselineSequence { get; init; }
		public int OutageBudget { get; init; }

		public bool Contains(int sequence) => sequence >= DropSequence && sequence <= ResumeSequence;

		public void Report(MultiplayerEcsSyncReport report, int outageSequences, int pairedSequencesAfterReconnect)
		{
			int missedSequences = Math.Max(0, ResumeSequence - DropSequence);
			report.AddSummaryLine($"Reconnect scenario: the client dropped at applied sequence {DropSequence} and resumed at {ResumeSequence}, a {missedSequences}-sequence outage (budget {OutageBudget}); {outageSequences} of those were confirmed host sequences it did not apply.");
			report.AddSummaryLine($"Reconnect scenario: {pairedSequencesAfterReconnect} host sequences were compared after the resume.");
			if (ResumeBaselineSequence > 0)
			{
				report.AddSummaryLine($"Reconnect scenario: sequence {ResumeBaselineSequence} was the baseline sent to this peer on reconnect; its baseline flag is excluded from the comparison because the host sends that baseline outside the per-tick fan-out.");
			}

			if (missedSequences > OutageBudget)
			{
				report.AddFailure($"The reconnect took {missedSequences} sequences, above the {OutageBudget}-sequence budget, so the client was blind for longer than the scenario allows.");
			}

			if (pairedSequencesAfterReconnect < 30)
			{
				report.AddFailure($"Only {pairedSequencesAfterReconnect} sequences were paired after the reconnect, which is not enough to show the client converged.");
			}
		}
	}

	private static ReconnectExpectation? ReadReconnectExpectation(MultiplayerEcsRunSettings settings, string clientDirectory, MultiplayerEcsSyncReport report)
	{
		if (settings.DropClientAtSeconds <= 0f) return null;

		if (!string.Equals(Path.GetFullPath(clientDirectory), Path.GetFullPath(settings.ResolveClientDirectory(1)), StringComparison.OrdinalIgnoreCase)) return null;

		string path = Path.Combine(clientDirectory, "client_drop.txt");
		if (!File.Exists(path))
		{
			report.AddFailure("The reconnect scenario asked the client to drop its connection, but the client wrote no drop record.");
			return null;
		}

		var fields = new Dictionary<string, string>(StringComparer.Ordinal);
		foreach (string line in File.ReadAllLines(path))
		{
			int separator = line.IndexOf('=');
			if (separator <= 0) continue;
			fields[line.Substring(0, separator)] = line.Substring(separator + 1);
		}

		if (!fields.TryGetValue("dropped", out string? dropped) || dropped != "1")
		{
			fields.TryGetValue("reason", out string? reason);
			report.AddFailure($"The client never dropped its connection for the reconnect scenario: {reason ?? "no reason recorded"}.");
			return null;
		}

		if (!fields.TryGetValue("resumed", out string? resumed) || resumed != "1")
		{
			report.AddFailure("The client dropped its connection and never applied another snapshot, so the reconnect did not complete.");
			return null;
		}

		if (!int.TryParse(fields.GetValueOrDefault("drop_sequence"), out int dropSequence) || !int.TryParse(fields.GetValueOrDefault("resume_sequence"), out int resumeSequence))
		{
			report.AddFailure("The client's reconnect record does not name the sequences it dropped and resumed at.");
			return null;
		}

		int.TryParse(fields.GetValueOrDefault("resume_baseline_sequence"), out int resumeBaselineSequence);
		return new ReconnectExpectation
		{
			DropSequence = dropSequence,
			ResumeSequence = resumeSequence,
			ResumeBaselineSequence = resumeBaselineSequence,
			OutageBudget = settings.ReconnectDeadlineSequences
		};
	}

	private static bool IsSameNavigation(MultiplayerNavigationState left, MultiplayerNavigationState right)
	{
		return left.HasTerrainState == right.HasTerrainState
			&& left.HasNavMesh == right.HasNavMesh
			&& left.Width == right.Width
			&& left.Depth == right.Depth
			&& left.PathingHash == right.PathingHash
			&& left.WalkableCellCount == right.WalkableCellCount
			&& left.BlockedCellCount == right.BlockedCellCount
			&& left.PolyCount == right.PolyCount
			&& left.GroundPolyCount == right.GroundPolyCount
			&& left.PolyHash == right.PolyHash;
	}

	private static void CompareNavigationState(int sequence, MultiplayerNavigationState hostNavigation, MultiplayerNavigationState clientNavigation, List<string> mismatches, List<string> navMeshObservations, ref int pathingMismatchCount, ref int navMeshMissingCount, ref int navMeshShapeDifferenceCount)
	{
		if (!hostNavigation.HasTerrainState)
		{
			AddMismatch(mismatches, 0, $"seq {sequence}: the host has no terrain state, so navigation could not be compared.");
			return;
		}

		if (!clientNavigation.HasTerrainState)
		{
			AddMismatch(mismatches, 0, $"seq {sequence}: the client has no terrain state, so it cannot path or carve obstacles.");
			pathingMismatchCount++;
			return;
		}

		bool pathingMatches = hostNavigation.Width == clientNavigation.Width
			&& hostNavigation.Depth == clientNavigation.Depth
			&& hostNavigation.PathingHash == clientNavigation.PathingHash
			&& hostNavigation.WalkableCellCount == clientNavigation.WalkableCellCount
			&& hostNavigation.BlockedCellCount == clientNavigation.BlockedCellCount;
		if (!pathingMatches)
		{
			pathingMismatchCount++;
			AddMismatch(mismatches, 0, $"seq {sequence}: terrain pathing host=(size {hostNavigation.Width}x{hostNavigation.Depth}, walkable {hostNavigation.WalkableCellCount}, blocked {hostNavigation.BlockedCellCount}, hash {hostNavigation.PathingHash:x8}) client=(size {clientNavigation.Width}x{clientNavigation.Depth}, walkable {clientNavigation.WalkableCellCount}, blocked {clientNavigation.BlockedCellCount}, hash {clientNavigation.PathingHash:x8}).");
		}

		if (!hostNavigation.HasNavMesh || !clientNavigation.HasNavMesh)
		{
			navMeshMissingCount++;
			AddMismatch(mismatches, 0, $"seq {sequence}: navmesh host present={hostNavigation.HasNavMesh} client present={clientNavigation.HasNavMesh}.");
			return;
		}

		bool obstacleInputMatches = hostNavigation.ObstacleVolumeCount == clientNavigation.ObstacleVolumeCount
			&& hostNavigation.ObstacleHash == clientNavigation.ObstacleHash;
		bool navMeshShapeMatches = hostNavigation.PolyCount == clientNavigation.PolyCount
			&& hostNavigation.GroundPolyCount == clientNavigation.GroundPolyCount
			&& hostNavigation.PolyHash == clientNavigation.PolyHash;
		if (!navMeshShapeMatches)
		{
			navMeshShapeDifferenceCount++;
			if (navMeshObservations.Count < 20)
			{
				navMeshObservations.Add($"seq {sequence}: navmesh host=(polys {hostNavigation.PolyCount}, ground {hostNavigation.GroundPolyCount}, hash {hostNavigation.PolyHash:x8}, obstacle volumes {hostNavigation.ObstacleVolumeCount}, input hash {hostNavigation.ObstacleHash:x8}) client=(polys {clientNavigation.PolyCount}, ground {clientNavigation.GroundPolyCount}, hash {clientNavigation.PolyHash:x8}, obstacle volumes {clientNavigation.ObstacleVolumeCount}, input hash {clientNavigation.ObstacleHash:x8}), obstacle inputs {(obstacleInputMatches ? "agree" : "differ")}.");
			}
		}
	}

	private static string DescribeSequenceList(List<int> sequences)
	{
		if (sequences.Count == 0) return "none";

		int listedCount = Math.Min(sequences.Count, 12);
		StringBuilder summary = new();
		for (int index = 0; index < listedCount; index++)
		{
			if (index > 0) summary.Append(", ");
			summary.Append(sequences[index]);
		}

		if (sequences.Count > listedCount) summary.Append($", ... ({sequences.Count - listedCount} more)");
		return summary.ToString();
	}

	private static float ResolvePresentationTolerance(NetworkProfile profile)
	{
		return profile switch
		{
			NetworkProfile.Clean => PresentationPositionTolerance,
			NetworkProfile.Latency => PresentationPositionTolerance,
			NetworkProfile.LatencyJitter => 0.35f,
			NetworkProfile.LatencyLoss => 0.6f,
			_ => PresentationPositionTolerance
		};
	}

	private static void ComparePresentationRecords(string outputDirectory, SortedDictionary<int, MultiplayerSnapshotRecord> hostRecords, MultiplayerEcsSyncReport report, MultiplayerEcsRunSettings settings)
	{
		NetworkProfile profile = settings.Profile;
		string presentationPath = Path.Combine(outputDirectory, "ECS_client_presentation.bin");
		if (!File.Exists(presentationPath))
		{
			report.AddFailure("Client presentation records are missing, so rendered placement parity was not verified.");
			return;
		}

		SortedDictionary<int, MultiplayerSnapshotRecord> presentedRecords = ReadRecords(presentationPath, out string presentedRecordTruncation);
		string appliedPath = Path.Combine(outputDirectory, "ECS_client_sync.bin");
		if (!File.Exists(appliedPath))
		{
			report.AddFailure("Client applied-state records are missing, so rendered placement parity was not verified.");
			return;
		}

		SortedDictionary<int, MultiplayerSnapshotRecord> appliedRecords = ReadRecords(appliedPath, out string appliedRecordTruncation);
		if (presentedRecordTruncation.Length > 0) report.AddSummaryLine($"Presentation records: {presentedRecordTruncation}.");
		if (appliedRecordTruncation.Length > 0) report.AddSummaryLine($"Applied records: {appliedRecordTruncation}.");
		float placementTolerance = ResolvePresentationTolerance(profile);
		float fastestUnitSpeed = MeasureFastestUnitSpeed(hostRecords);
		float interpolationBufferSeconds = ResolveInterpolationBufferSeconds(settings);
		float interpolationLagMeters = fastestUnitSpeed * interpolationBufferSeconds;
		float detailedAuthorityTolerance = StatedPositionThresholdMeters + interpolationLagMeters + placementTolerance;
		var mismatches = new List<string>();
		int pairedSequences = 0;
		int comparedTransforms = 0;
		int mismatchCount = 0;
		int culledTransforms = 0;
		int notYetAppliedUnits = 0;
		int unitsNoPeerWasToldAbout = 0;
		int describedUnitsInPairedRecords = 0;
		int detailedTransforms = 0;
		int renderedBeyondTolerance = 0;
		float worstRenderedOffset = 0f;
		int rotationDeviationCount = 0;
		float largestRotationDeviation = 0f;
		var placementStreaks = new Dictionary<int, PlacementStreak[]>();
		var completedPlacementStreaks = new List<(int EntityId, int Quantity, PlacementStreak Streak)>();

		Dictionary<ulong, int> lastSequencePerFrame = new();
		foreach (KeyValuePair<int, MultiplayerSnapshotRecord> presentedEntry in presentedRecords)
		{
			lastSequencePerFrame.TryGetValue(presentedEntry.Value.PhysicsFrame, out int lastSequence);
			if (presentedEntry.Key > lastSequence) lastSequencePerFrame[presentedEntry.Value.PhysicsFrame] = presentedEntry.Key;
		}

		var previousPresented = new Dictionary<int, TrackedPresentedPosition>();
		int backwardRenderCount = 0;
		float worstBackwardMeters = 0f;

		int collapsedRepeatedSamples = 0;
		foreach (KeyValuePair<int, MultiplayerSnapshotRecord> hostEntry in hostRecords)
		{
			if (!presentedRecords.TryGetValue(hostEntry.Key, out MultiplayerSnapshotRecord? presentedRecord)) continue;
			if (lastSequencePerFrame[presentedRecord.PhysicsFrame] != hostEntry.Key)
			{
				collapsedRepeatedSamples++;
				continue;
			}

			pairedSequences++;
			describedUnitsInPairedRecords += hostEntry.Value.Units.Count;

			foreach (MultiplayerSnapshotUnit hostUnit in hostEntry.Value.Units.Values)
			{
				if ((hostUnit.Flags & 8) == 0)
				{
					unitsNoPeerWasToldAbout++;
					continue;
				}

				if (!presentedRecord.Units.TryGetValue(hostUnit.EntityId, out MultiplayerSnapshotUnit? presentedUnit))
				{
					bool appliedByClient = appliedRecords.TryGetValue(hostEntry.Key, out MultiplayerSnapshotRecord? missingUnitAppliedRecord)
						&& missingUnitAppliedRecord.Units.ContainsKey(hostUnit.EntityId);
					if (!appliedByClient)
					{
						notYetAppliedUnits++;
						continue;
					}

					mismatchCount++;
					if (mismatches.Count < 20) mismatches.Add($"seq {hostEntry.Key}: rendered entity {hostUnit.EntityId} is missing on the client, which had already applied that entity's state.");
					continue;
				}

				comparedTransforms++;
				if ((presentedUnit.Flags & 32) != 0) culledTransforms++;
				detailedTransforms++;


				PlacementOffset observed = new(
					MaxAxisOffset(hostUnit.PositionX, hostUnit.PositionY, hostUnit.PositionZ, presentedUnit.PositionX, presentedUnit.PositionY, presentedUnit.PositionZ),
					MaxAxisOffset(presentedUnit.AppliedPositionX, presentedUnit.AppliedPositionY, presentedUnit.AppliedPositionZ, presentedUnit.PositionX, presentedUnit.PositionY, presentedUnit.PositionZ),
					MaxAxisOffset(hostUnit.PositionX, hostUnit.PositionY, hostUnit.PositionZ, presentedUnit.AppliedPositionX, presentedUnit.AppliedPositionY, presentedUnit.AppliedPositionZ));

				if (observed.HostToRendered > placementTolerance) renderedBeyondTolerance++;
				if (observed.HostToRendered > worstRenderedOffset) worstRenderedOffset = observed.HostToRendered;

				if (previousPresented.TryGetValue(hostUnit.EntityId, out TrackedPresentedPosition previous) && hostEntry.Key - previous.Sequence == 1)
				{
					float authorityDeltaX = hostUnit.PositionX - previous.AuthorityX;
					float authorityDeltaZ = hostUnit.PositionZ - previous.AuthorityZ;
					float authorityDistance = MathF.Sqrt(authorityDeltaX * authorityDeltaX + authorityDeltaZ * authorityDeltaZ);
					if (authorityDistance > BackwardMotionAuthorityMeters)
					{
						float presentedDeltaX = presentedUnit.PositionX - previous.PresentedX;
						float presentedDeltaZ = presentedUnit.PositionZ - previous.PresentedZ;
						float alongAuthority = (presentedDeltaX * authorityDeltaX + presentedDeltaZ * authorityDeltaZ) / authorityDistance;
						if (alongAuthority < -BackwardMotionToleranceMeters)
						{
							backwardRenderCount++;
							if (-alongAuthority > worstBackwardMeters) worstBackwardMeters = -alongAuthority;
						}
					}
				}

				previousPresented[hostUnit.EntityId] = new TrackedPresentedPosition(hostEntry.Key, hostUnit.PositionX, hostUnit.PositionZ, presentedUnit.PositionX, presentedUnit.PositionZ);

				TrackPlacementStreak(placementStreaks, completedPlacementStreaks, hostUnit.EntityId, PlacementQuantityRendererFidelity, true, hostEntry.Key, in observed, PresentationPositionTolerance);
				TrackPlacementStreak(placementStreaks, completedPlacementStreaks, hostUnit.EntityId, PlacementQuantityAuthority, true, hostEntry.Key, in observed, detailedAuthorityTolerance);

				if (observed.HostToRendered > placementTolerance)
				{
					float rotationDeviation = RotationDifference(hostUnit.RotationY, presentedUnit.RotationY);
					if (rotationDeviation > PresentationRotationTolerance) rotationDeviationCount++;
					if (rotationDeviation > largestRotationDeviation) largestRotationDeviation = rotationDeviation;
				}
			}
		}

		foreach (KeyValuePair<int, PlacementStreak[]> streakEntry in placementStreaks)
		{
			for (int quantity = 0; quantity < PlacementQuantityCount; quantity++)
			{
				PlacementStreak streak = streakEntry.Value[quantity];
				if (streak.Count <= PlacementLagAllowanceSequences) continue;

				completedPlacementStreaks.Add((streakEntry.Key, quantity, streak));
			}
		}

		foreach ((int entityId, int quantity, PlacementStreak streak) in completedPlacementStreaks)
		{
			mismatchCount++;
			if (mismatches.Count < 20) mismatches.Add(DescribePlacementStreak(entityId, quantity, streak));
		}

		report.AddSummaryLine($"Presentation parity window: from {MultiplayerSyncDiagnostics.PresentationSettleSeconds:F1}s to {MultiplayerSyncDiagnostics.PresentationWindowSeconds:F1}s after a baseline, paired ticks: {pairedSequences}, compared rendered transforms: {comparedTransforms} ({detailedTransforms} of a stated position), placement mismatches: {mismatchCount}");
		report.AddSummaryLine($"Records sharing a physics tick with a later record of the same tick: {collapsedRepeatedSamples} (the cursor and the renderer advance once per tick, so only the last record of a tick is the state that tick produced)");
		report.AddSummaryLine($"A rendered transform is judged by what the protocol promised it: within {detailedAuthorityTolerance:F2} m of the authority ({StatedPositionThresholdMeters:F2} m resend threshold + {interpolationLagMeters:F2} m of buffer at the run's fastest {fastestUnitSpeed:F2} m/s + {placementTolerance:F2} m of {profile} allowance) and within {PresentationPositionTolerance:F2} m of the client's own applied state. The buffer bound is the deepest delay the client may render from, {interpolationBufferSeconds:F2} s; the run reports the delay it actually held.");
		report.AddSummaryLine($"Host units the client had not applied yet, so there was nothing to render: {notYetAppliedUnits} (the state comparison owns whether the client received them).");
		report.AddSummaryLine($"Rendered transforms outside the camera frustum: {culledTransforms} of {comparedTransforms} (culling freezes rotation but still advances position, so they stay comparable).");
		report.AddSummaryLine($"Rendered transforms more than {placementTolerance:F2} m from the authoritative position: {renderedBeyondTolerance} of {comparedTransforms}, worst {worstRenderedOffset:F2} m; this is context rather than an assertion, because the renderer may legally be that far behind.");
		report.AddSummaryLine($"Buffer starvations: {ReadClientCounter(outputDirectory, "buffer_starvations=")} (the cursor reached the end of what had arrived, which is what deepens the delay), final delay {ReadClientCounter(outputDirectory, "interpolation_delay_sequences=")} sequences, samples drawn past the newest description: {ReadClientCounter(outputDirectory, "extrapolated_samples=")}");

		int lastHostSequence = 0;
		foreach (int hostSequence in hostRecords.Keys) lastHostSequence = hostSequence;

		float matchSeconds = lastHostSequence / (float)MatchTiming.SnapshotRateHz;
		if (settings.DurationSeconds > 0f && matchSeconds > 0f)
		{
			float simulatedFasterThanRealTime = matchSeconds / settings.DurationSeconds;
			report.AddSummaryLine($"Simulation pace: {matchSeconds:F1} s of match time in a {settings.DurationSeconds:F0} s run, {simulatedFasterThanRealTime:F1} times real time (an uncapped run simulates as fast as the machine allows, so every latency figure covers that much more of the match than it would in a game)");
		}
		report.AddSummaryLine($"Rendered rotation deviations beyond {PresentationRotationTolerance:F2} rad: {rotationDeviationCount} of {comparedTransforms}, largest {largestRotationDeviation:F2} rad (off-frustum units keep their frozen transform until they enter the view)");
		report.AddSummaryLine($"Rendered transforms that moved against the authority's own direction of travel: {backwardRenderCount} of {comparedTransforms}, worst {worstBackwardMeters:F2} m behind where it was drawn a sequence earlier (the cursor is the only thing that moves a unit, so this is what running out of world and being placed back on the newest description looks like)");
		report.AddSummaryLine($"Drawn positions further from the navigation mesh than {MultiplayerSyncDiagnostics.OffNavMeshToleranceMeters:F2} m: {ReadClientCounter(outputDirectory, "off_navmesh_units=")}, worst {ReadClientMetric(outputDirectory, "worst_off_navmesh_meters="):F2} m (a unit is drawn on the line joining two descriptions, so a corner of a building is how that line leaves the mesh)");
		bool parityMeasureWasEmpty = comparedTransforms == 0 && (unitsNoPeerWasToldAbout > 0 || describedUnitsInPairedRecords == 0);
		if (parityMeasureWasEmpty)
		{
			report.AddSummaryLine($"Rendered placement parity was not measured: the {pairedSequences} paired host records described {describedUnitsInPairedRecords} units in total, of which {unitsNoPeerWasToldAbout} were comparable for a room this size, so there was no rendered transform to hold against an authority position.");
		}
		else if (pairedSequences == 0) report.AddFailure("No rendered presentation records paired with host snapshots, so placement parity was not verified.");
		else if (comparedTransforms < 20) report.AddFailure($"Only {comparedTransforms} rendered unit transforms were compared in the presentation window.");

		int offNavMeshUnits = ReadClientCounter(outputDirectory, "off_navmesh_units=");
		if (offNavMeshUnits > 0)
		{
			report.AddFailure($"{offNavMeshUnits} drawn unit positions were more than {MultiplayerSyncDiagnostics.OffNavMeshToleranceMeters:F2} m from the navigation mesh this screen baked, worst {ReadClientMetric(outputDirectory, "worst_off_navmesh_meters="):F2} m, so the presentation put a unit inside geometry.");
		}

		int clientStarvations = ReadClientCounter(outputDirectory, "buffer_starvations=");
		if (backwardRenderCount > 0 && clientStarvations == 0)
		{
			report.AddFailure($"{backwardRenderCount} rendered transforms moved against the authority's direction of travel, worst {worstBackwardMeters:F2} m, and the buffer never ran out of world, so the cursor was not the cause.");
		}

		if (mismatchCount > 0)
		{
			report.AddFailure($"{mismatchCount} rendered placement mismatches were detected inside the presentation window: a rendered transform, or the applied state behind it, stayed beyond its tolerance for more than {PlacementLagAllowanceSequences} consecutive records. Each entry names the quantity that left its bound and the three distances at its worst sequence.");
			report.AddSummaryLine("First presentation mismatches:");
			foreach (string mismatch in mismatches) report.AddSummaryLine($"  {mismatch}");
		}
	}

	private static int ResolveObstacleInputAllowanceSequences(MultiplayerEcsRunSettings settings)
	{
		float oneWaySeconds = MathF.Max(settings.OneWayDelayMilliseconds, settings.PingMilliseconds / 2f) / 1000f;
		int linkSequences = (int)MathF.Ceiling((oneWaySeconds + (settings.JitterMilliseconds / 1000f)) * MatchTiming.SnapshotRateHz);
		return Math.Min(90, MatchTiming.SnapshotRateHz + linkSequences + 4);
	}

	private static float ResolveInterpolationBufferSeconds(MultiplayerEcsRunSettings settings)
	{
		return SnapshotTimeline.MaximumDelaySequences * MatchTiming.SnapshotIntervalSeconds;
	}

	private static float ReadClientMetric(string clientDirectory, string prefix)
	{
		string path = Path.Combine(clientDirectory, "ECS_client_replication.txt");
		if (!File.Exists(path)) return 0f;

		foreach (string line in File.ReadLines(path))
		{
			if (!line.StartsWith(prefix, StringComparison.Ordinal)) continue;
			return float.TryParse(line.AsSpan(prefix.Length), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float value) ? value : 0f;
		}

		return 0f;
	}

	private static int ReadClientCounter(string clientDirectory, string prefix)
	{
		string path = Path.Combine(clientDirectory, "ECS_client_replication.txt");
		if (!File.Exists(path)) return 0;

		foreach (string line in File.ReadLines(path))
		{
			if (!line.StartsWith(prefix, StringComparison.Ordinal)) continue;
			return int.TryParse(line.AsSpan(prefix.Length), out int count) ? count : 0;
		}

		return 0;
	}

	private static float MeasureFastestUnitSpeed(SortedDictionary<int, MultiplayerSnapshotRecord> hostRecords)
	{
		float fastest = 0f;
		MultiplayerSnapshotRecord? previous = null;
		foreach (MultiplayerSnapshotRecord record in hostRecords.Values)
		{
			if (previous != null && record.Sequence == previous.Sequence + 1)
			{
				foreach (MultiplayerSnapshotUnit unit in record.Units.Values)
				{
					if (!previous.Units.TryGetValue(unit.EntityId, out MultiplayerSnapshotUnit? before)) continue;
					float step = MaxAxisOffset(unit.PositionX, unit.PositionY, unit.PositionZ, before.PositionX, before.PositionY, before.PositionZ);
					if (step > MaximumTravelStepMeters) continue;
					float speed = step * SnapshotRecordsPerSecond;
					if (speed > fastest) fastest = speed;
				}
			}

			previous = record;
		}

		return fastest;
	}

	private static float MaxAxisOffset(float leftX, float leftY, float leftZ, float rightX, float rightY, float rightZ)
	{
		float offsetX = MathF.Abs(leftX - rightX);
		float offsetY = MathF.Abs(leftY - rightY);
		float offsetZ = MathF.Abs(leftZ - rightZ);
		return MathF.Max(offsetX, MathF.Max(offsetY, offsetZ));
	}

	private static void TrackPlacementStreak(Dictionary<int, PlacementStreak[]> streaks, List<(int EntityId, int Quantity, PlacementStreak Streak)> completed, int entityId, int quantity, bool tracked, int sequence, in PlacementOffset observed, float tolerance)
	{
		if (!streaks.TryGetValue(entityId, out PlacementStreak[]? perQuantity))
		{
			if (!tracked) return;
			perQuantity = new PlacementStreak[PlacementQuantityCount];
			streaks[entityId] = perQuantity;
		}

		ref PlacementStreak streak = ref perQuantity[quantity];
		float distance = observed.DistanceFor(quantity);
		if (!tracked || distance <= tolerance)
		{
			ClosePlacementStreak(completed, entityId, quantity, streak);
			streak = default;
			return;
		}

		if (streak.Count == 0 || sequence != streak.LastSequence + 1)
		{
			ClosePlacementStreak(completed, entityId, quantity, streak);
			streak = default;
			streak.FirstSequence = sequence;
		}

		streak.Count++;
		streak.LastSequence = sequence;
		if (distance > streak.WorstOffset)
		{
			streak.WorstOffset = distance;
			streak.WorstSequence = sequence;
			streak.WorstHostToRendered = observed.HostToRendered;
			streak.WorstAppliedToRendered = observed.AppliedToRendered;
			streak.WorstHostToApplied = observed.HostToApplied;
		}
	}

	private static void ClosePlacementStreak(List<(int EntityId, int Quantity, PlacementStreak Streak)> completed, int entityId, int quantity, in PlacementStreak streak)
	{
		if (streak.Count <= PlacementLagAllowanceSequences) return;
		completed.Add((entityId, quantity, streak));
	}

	private static string DescribePlacementStreak(int entityId, int quantity, PlacementStreak streak)
	{
		string explanation = quantity switch
		{
			PlacementQuantityRendererFidelity => $"the rendered transform stayed {streak.WorstOffset:F2} m from the state the client had already applied, so the renderer stopped following the client's own simulation",
			_ => $"the host described this unit and the rendered transform stayed {streak.WorstOffset:F2} m from the authoritative position, beyond the {StatedPositionThresholdMeters:F2} m resend threshold plus the interpolation catch-up the protocol allows"
		};

		return $"entity {entityId} for {streak.Count} consecutive records (sequences {streak.FirstSequence}-{streak.LastSequence}): {explanation}. At sequence {streak.WorstSequence} the render was {streak.WorstHostToRendered:F2} m from the authority, {streak.WorstAppliedToRendered:F2} m from the applied state, and the applied state was {streak.WorstHostToApplied:F2} m from the authority.";
	}

}
