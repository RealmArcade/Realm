using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.Json;
using Realm.Client.Services;

namespace Realm.Client.Testing;

public sealed class PredictionHarnessReport
{
	public string Profile { get; set; } = string.Empty;
	public string Verdict { get; set; } = "FAIL";
	public int Failures { get; set; }
	public List<string> FailureMessages { get; } = new();
	public float InputResponseTicks { get; set; } = -1f;
	public float MaximumInputResponseTicks { get; set; }
	public int PredictedLocalUnitSamples { get; set; }
	public int PredictedRemoteUnitSamples { get; set; }
	public int MovementSamples { get; set; }
	public float MeanErrorMeters { get; set; }
	public float P95ErrorMeters { get; set; }
	public float MaximumErrorMeters { get; set; }
	public float SettledErrorMeters { get; set; }
	public float MaximumSettledTransientMeters { get; set; }
	public int ConcentratedOrderUnits { get; set; }
	public int SlowestConvergenceTicks { get; set; } = -1;
	public int StaleSettledSamplesSkipped { get; set; }
	public int UnexplainedSnapCount { get; set; }
	public int ExplainedSnapCount { get; set; }
	public int LargeCorrectionCount { get; set; }
	public int RollbackCount { get; set; }
	public float WorstRollbackTicks { get; set; } = -1f;
	public int RejectionsReceived { get; set; }
	public int PathComparisons { get; set; }
	public int PathMismatches { get; set; }
	public int OscillationViolations { get; set; }
	public float CorrectionsPerSecondFirstHalf { get; set; }
	public float CorrectionsPerSecondSecondHalf { get; set; }
	public bool SnapshotOrderingRespected { get; set; } = true;
	public string ThresholdRationale { get; set; } = string.Empty;
	public float AuthorityInvariantMaximumErrorMeters { get; set; }
	public int SubmittedCommandCount { get; set; }
	public int AcknowledgedCommandCount { get; set; }
	public int UnacknowledgedCommandCount { get; set; }
	public long MaximumTickAllocatedBytes { get; set; }
	public double MeanTickAllocatedBytes { get; set; }
	public long MaximumSharedMovementAllocatedBytes { get; set; }
	public int SnapshotOrderingViolations { get; set; }
	public int UnpredictedOrderSamples { get; set; }
	public int UnpredictedOrderP95Ticks { get; set; }
	public int UnpredictedOrderMaximumTicks { get; set; }
	public int UnpredictedOrderBudgetTicks { get; set; }
	public List<string> UnitSummaries { get; } = new();
	public List<string> StageSummaries { get; } = new();

	public void AddFailure(string failure)
	{
		Failures++;
		FailureMessages.Add(failure);
	}

	public string RenderJson()
	{
		var payload = new
		{
			profile = Profile,
			verdict = Verdict,
			failures = Failures,
			failureMessages = FailureMessages,
			metrics = new
			{
				inputResponseTicks = InputResponseTicks,
				maximumInputResponseTicks = MaximumInputResponseTicks,
				predictedLocalUnitSamples = PredictedLocalUnitSamples,
				predictedRemoteUnitSamples = PredictedRemoteUnitSamples,
				movementSamples = MovementSamples,
				meanErrorMeters = MeanErrorMeters,
				p95ErrorMeters = P95ErrorMeters,
				maximumErrorMeters = MaximumErrorMeters,
				settledErrorMeters = SettledErrorMeters,
				maximumSettledTransientMeters = MaximumSettledTransientMeters,
				concentratedOrderUnits = ConcentratedOrderUnits,
				slowestConvergenceTicks = SlowestConvergenceTicks,
				staleSettledSamplesSkipped = StaleSettledSamplesSkipped,
				unexplainedSnapCount = UnexplainedSnapCount,
				explainedSnapCount = ExplainedSnapCount,
				largeCorrectionCount = LargeCorrectionCount,
				rollbackCount = RollbackCount,
				worstRollbackTicks = WorstRollbackTicks,
				rejectionsReceived = RejectionsReceived,
				pathComparisons = PathComparisons,
				pathMismatches = PathMismatches,
				oscillationViolations = OscillationViolations,
				correctionsPerSecondFirstHalf = CorrectionsPerSecondFirstHalf,
				correctionsPerSecondSecondHalf = CorrectionsPerSecondSecondHalf,
				snapshotOrderingRespected = SnapshotOrderingRespected,
				thresholdRationale = ThresholdRationale,
				authorityInvariantMaximumErrorMeters = AuthorityInvariantMaximumErrorMeters,
				submittedCommandCount = SubmittedCommandCount,
				acknowledgedCommandCount = AcknowledgedCommandCount,
				unacknowledgedCommandCount = UnacknowledgedCommandCount,
				maximumTickAllocatedBytes = MaximumTickAllocatedBytes,
				maximumSharedMovementAllocatedBytes = MaximumSharedMovementAllocatedBytes,
				meanTickAllocatedBytes = MeanTickAllocatedBytes,
				snapshotOrderingViolations = SnapshotOrderingViolations,
				unpredictedOrderSamples = UnpredictedOrderSamples,
				unpredictedOrderP95Ticks = UnpredictedOrderP95Ticks,
				unpredictedOrderMaximumTicks = UnpredictedOrderMaximumTicks,
				unpredictedOrderBudgetTicks = UnpredictedOrderBudgetTicks
			},
			units = UnitSummaries,
			stages = StageSummaries
		};

		return JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
	}

	public string RenderText()
	{
		StringBuilder builder = new();
		builder.AppendLine("=== Client prediction verification ===");
		builder.AppendLine($"Profile: {Profile}");
		builder.AppendLine($"Thresholds: {ThresholdRationale}");
		builder.AppendLine($"Verdict: {Verdict}");
		builder.AppendLine($"Input response (ticks): {InputResponseTicks:F2} (worst {MaximumInputResponseTicks:F2})");
		builder.AppendLine($"Predicted local unit samples: {PredictedLocalUnitSamples}, predicted remote unit samples: {PredictedRemoteUnitSamples}");
		builder.AppendLine($"Movement samples: {MovementSamples}, error mean {MeanErrorMeters:F3} m, p95 {P95ErrorMeters:F3} m, max {MaximumErrorMeters:F3} m");
		builder.AppendLine($"Settled error: {SettledErrorMeters:F3} m sustained (largest {MaximumSettledTransientMeters:F3} m transient) over {ConcentratedOrderUnits} order-free units; slowest convergence {SlowestConvergenceTicks} ticks, {StaleSettledSamplesSkipped} settled samples excluded because their snapshot stream had stalled");
		builder.AppendLine($"Snaps: explained {ExplainedSnapCount}, unexplained {UnexplainedSnapCount}; corrections above snap threshold {LargeCorrectionCount}");
		builder.AppendLine($"Rejections received: {RejectionsReceived}, rollbacks: {RollbackCount}, worst rollback {WorstRollbackTicks:F1} ticks");
		builder.AppendLine($"Pathing parity: compared {PathComparisons}, mismatches {PathMismatches}");
		builder.AppendLine($"Oscillation violations: {OscillationViolations}");
		builder.AppendLine($"Corrections per second: first half {CorrectionsPerSecondFirstHalf:F2}, second half {CorrectionsPerSecondSecondHalf:F2}");
		builder.AppendLine($"Commands: submitted through the ordered transport {SubmittedCommandCount}, distinct command ids the authority answered (acknowledged or rejected) {AcknowledgedCommandCount}, unanswered {UnacknowledgedCommandCount}");
		builder.AppendLine($"Authority invariant (predicted vs authoritative on an in-process authority): max {AuthorityInvariantMaximumErrorMeters:F4} m");
		builder.AppendLine($"Prediction tick allocation: mean {MeanTickAllocatedBytes:F0} B, max {MaximumTickAllocatedBytes} B (shared movement step peak {MaximumSharedMovementAllocatedBytes} B, reported separately)");
		builder.AppendLine($"Snapshot ordering violations: {SnapshotOrderingViolations}");
		builder.AppendLine($"Unpredicted (host-only) order visibility: samples {UnpredictedOrderSamples}, p95 {UnpredictedOrderP95Ticks} ticks, max {UnpredictedOrderMaximumTicks}, budget {UnpredictedOrderBudgetTicks}");
		builder.AppendLine("Stages:");
		foreach (string stage in StageSummaries) builder.AppendLine($"  {stage}");
		builder.AppendLine("Units:");
		foreach (string unit in UnitSummaries) builder.AppendLine($"  {unit}");
		builder.AppendLine($"Failures: {Failures}");
		foreach (string failure in FailureMessages) builder.AppendLine($"  - {failure}");
		return builder.ToString();
	}
}

/// <summary>
/// The limits a prediction run is measured against. Every value is stated per network profile because the same
/// implementation legitimately produces larger transient error when the inbound stream is delayed, jittered or lossy.
/// </summary>
public readonly record struct PredictionThresholds(
	float InputResponseTicks,
	float P95ErrorMeters,
	float MaximumErrorMeters,
	float SettledErrorMeters,
	float CorrectionDisplacementMeters,
	float RollbackWindowTicks,
	float LoopbackInvariantMeters,
	long AllocationBytesPerTick,
	string Rationale);

public static class PredictionHarnessVerification
{
	public const int SettledConvergenceTicks = 30;
	/// <summary>
	/// How many client ticks may pass without the client applying any snapshot before a settled-error sample is excluded.
	/// A settled measurement asserts that the client knows where the authority put the unit, so a stalled stream means
	/// the gap being measured is the transport's, not prediction's. The age is measured on the applied snapshot sequence
	/// rather than on the unit's own sample, because deltas legitimately omit a resting unit for many ticks. Excluding
	/// those samples cannot hide a convergence defect: the convergence window below measures the same units against the
	/// same limit from the tick the order completed, using the newest sample the client actually received.
	/// </summary>
	public const int MaximumAuthoritativeSampleAgeTicks = 8;
	/// <summary>
	/// How many ticks prediction may take, after an order stops being active on the client, to bring the unit back within
	/// the settled error limit. One one-way delay plus a bounded correction window is about 60 ticks; this allows a full
	/// extra second for a lost snapshot to be repaired before calling the convergence a defect.
	/// </summary>
	public const int ConvergenceDeadlineTicks = 90;
	/// <summary>
	/// How many consecutive settled samples an error must persist before it counts as residual error rather than the
	/// authoritative input moving. A snapshot can move the authoritative position by up to the snap threshold in one
	/// sample (a group-scatter arrival, a formation offset, a script nudge), and the client is then legitimately that far
	/// away for the few ticks the bounded correction needs. A third of a second of sustained error is not that.
	/// </summary>
	public const int SettledSustainedTicks = 10;
	public const float PathWaypointToleranceMeters = 1.0f;
	public const float PathTargetToleranceMeters = 0.1f;
	public const float StationaryAuthoritativeMeters = 0.15f;
	public const int PathStartWaypointCount = 1;
	public const int MinimumPredictedSamples = 60;
	public const int MinimumPathComparisons = 20;
	public const int MinimumUnpredictedOrderSamples = 10;

	/// <summary>
	/// Resolves the limits for a profile. The input-response bound is one simulation tick on every profile because
	/// prediction is local: a correct implementation never waits for the network before moving. The positional bounds
	/// scale with the profile because they measure the instantaneous gap between a locally predicted unit and the
	/// authoritative sample that has already been delayed by that profile. The settled bound stays small on every
	/// profile: once an order completes, prediction must have converged regardless of delay. The loopback invariant is
	/// near zero because an in-process authority and its local viewpoint see the same tick.
	/// </summary>
	public static PredictionThresholds ThresholdsFor(NetworkProfile profile)
	{
		return profile switch
		{
			NetworkProfile.Clean => new PredictionThresholds(1f, 0.5f, 2.5f, 0.10f, 2.0f, 30f, 0.01f, 512,
				"clean: no delay, so only tick-quantisation error is acceptable"),
			NetworkProfile.Latency => new PredictionThresholds(1f, 2.5f, 6.0f, 0.5f, 4.0f, 60f, 0.01f, 512,
				"150 ms one way: predicted lead equals 4.5 ticks of travel, so 2.5 m p95 is roughly 0.5 m of real divergence"),
			NetworkProfile.LatencyJitter => new PredictionThresholds(1f, 3.0f, 7.0f, 0.6f, 5.0f, 90f, 0.01f, 512,
				"120 ms +/- 40 ms: reordered arrivals widen the instantaneous gap, so the envelope is one step looser"),
			NetworkProfile.LatencyLoss => new PredictionThresholds(1f, 3.5f, 8.0f, 1.0f, 6.0f, 150f, 0.01f, 512,
				"3% datagram loss: a dropped snapshot leaves prediction unreconciled for one or more ticks"),
			_ => new PredictionThresholds(1f, 2.5f, 6.0f, 0.5f, 4.0f, 60f, 0.01f, 512, "default envelope")
		};
	}

	private sealed class ClientUnitSample
	{
		public int EntityId;
		public int ServerEntityId;
		public int CommandId;
		public int OwnerPlayerIndex;
		public bool LocalOwner;
		public bool HasOrder;
		public bool HasPath;
		public int CurrentWaypoint;
		public int WaypointCount;
		public Vector3 Predicted;
		public Vector3 Previous;
		public Vector3 Authoritative;
		public Vector3 MoveTarget;
		public int AuthoritativeSequence;
		public Vector3[] Waypoints = Array.Empty<Vector3>();
	}

	private sealed class ClientTickSample
	{
		public int Tick;
		public int Sequence;
		public int LocalPlayerIndex;
		public List<ClientUnitSample> Units = new();
	}

	private sealed class HostUnitSample
	{
		public int ServerEntityId;
		public int OwnerPlayerIndex;
		public bool HasMoveTo;
		public bool HasPath;
		public int CurrentWaypoint;
		public int WaypointCount;
		public Vector3 Position;
		public Vector3 MoveTarget;
		public Vector3[] Waypoints = Array.Empty<Vector3>();
	}

	private sealed class HostSnapshotSample
	{
		public int Sequence;
		public Dictionary<int, HostUnitSample> Units = new();
	}

	private sealed class RollbackSample
	{
		public int CommandId;
		public int EntityId;
		public int Sequence;
		public int Tick;
		public string Reason = string.Empty;
	}

	private sealed class CorrectionSample
	{
		public int EntityId;
		public int Sequence;
		public int Tick;
		public Vector3 Predicted;
		public Vector3 Corrected;
		public Vector3 Authoritative;
	}

	private sealed class ScenarioSample
	{
		public string Stage = string.Empty;
		public int Tick;
		public int EntityId;
		public Vector3 Target;
	}

	private sealed class ParsedRecords
	{
		public List<ClientTickSample> ClientTicks = new();
		public Dictionary<int, HostSnapshotSample> HostSnapshots = new();
		public Dictionary<int, int> CommandTicks = new();
		public List<(int CommandId, int Tick)> AcceptedCommands = new();
		public List<(int CommandId, int Tick, string Reason)> Rejections = new();
		public List<RollbackSample> Rollbacks = new();
		public List<(int EntityId, int Sequence, int Tick, bool Explained, string Reason)> Snaps = new();
		public List<CorrectionSample> Corrections = new();
		public List<ScenarioSample> Scenario = new();
		public List<(int Tick, int EntityId, Vector3 Target, bool LocalOwned)> ScriptOrders = new();
		public int ScriptUpdateCount;
		public List<(int Tick, long PredictionBytes, long SharedMovementBytes)> AllocationSamples = new();
		public List<(int RejectedSequence, int LastAppliedSequence, int Tick)> OrderingViolations = new();
	}

	public static PredictionHarnessReport Verify(MultiplayerEcsRunSettings settings)
	{
		PredictionThresholds thresholds = ThresholdsFor(settings.Profile);
		var report = new PredictionHarnessReport { Profile = settings.Profile.ToString(), ThresholdRationale = thresholds.Rationale };
		string clientPath = Path.Combine(settings.OutputDirectory, "prediction_client.bin");
		string hostPath = Path.Combine(settings.OutputDirectory, "prediction_host.bin");
		if (!File.Exists(clientPath))
		{
			report.AddFailure("prediction_client.bin is missing; the client never recorded prediction state.");
			report.Verdict = "FAIL";
			return report;
		}

		ParsedRecords records = new();
		try
		{
			ReadClientRecords(clientPath, records);
			if (File.Exists(hostPath)) ReadHostRecords(hostPath, records);
		}
		catch (Exception exception)
		{
			report.AddFailure($"Prediction records could not be parsed: {exception.Message}");
			report.Verdict = "FAIL";
			return report;
		}

		if (records.ClientTicks.Count == 0)
		{
			report.AddFailure("The client produced no prediction tick samples, so prediction was never active.");
		}

		records.ClientTicks.Sort((left, right) => left.Tick.CompareTo(right.Tick));

		int localSamples = 0;
		int remoteSamples = 0;
		int movementSamples = 0;
		var errors = new List<float>();
		var perUnitErrors = new Dictionary<int, List<float>>();
		var perUnitFinalErrors = new Dictionary<int, float>();
		foreach (ClientTickSample tick in records.ClientTicks)
		{
			foreach (ClientUnitSample unit in tick.Units)
			{
				if (unit.LocalOwner) localSamples++;
				else remoteSamples++;

				if (unit.AuthoritativeSequence < 0) continue;
				float error = Vector3.Distance(unit.Predicted, unit.Authoritative);
				if (!float.IsFinite(error)) continue;

				if (!perUnitErrors.TryGetValue(unit.EntityId, out List<float>? unitErrors))
				{
					unitErrors = new List<float>();
					perUnitErrors[unit.EntityId] = unitErrors;
				}

				unitErrors.Add(error);
				perUnitFinalErrors[unit.EntityId] = error;

				bool withinStage = IsWithinScenarioStage(records, tick.Tick);
				if (!withinStage) continue;
				errors.Add(error);
				if (unit.HasOrder) movementSamples++;
			}
		}

		report.PredictedLocalUnitSamples = localSamples;
		report.PredictedRemoteUnitSamples = remoteSamples;
		report.MovementSamples = movementSamples;

		if (localSamples < MinimumPredictedSamples)
		{
			report.AddFailure($"Only {localSamples} predicted local unit samples were recorded; prediction was not active for enough ticks.");
		}

		if (remoteSamples > 0)
		{
			report.AddFailure($"{remoteSamples} samples were recorded for units that are not locally controlled; only locally controlled units may be predicted.");
		}

		if (errors.Count > 0)
		{
			errors.Sort();
			report.MeanErrorMeters = errors.Average();
			report.P95ErrorMeters = errors[(int)Math.Min(errors.Count - 1, Math.Floor(errors.Count * 0.95))];
			report.MaximumErrorMeters = errors[^1];
		}

		if (report.P95ErrorMeters > thresholds.P95ErrorMeters)
		{
			report.AddFailure($"Predicted position error p95 is {report.P95ErrorMeters:F3} m, above the stated {thresholds.P95ErrorMeters:F3} m limit for {settings.Profile}.");
		}

		if (report.MaximumErrorMeters > thresholds.MaximumErrorMeters)
		{
			report.AddFailure($"Predicted position error peaked at {report.MaximumErrorMeters:F3} m, above the stated {thresholds.MaximumErrorMeters:F3} m limit for {settings.Profile}.");
		}

		MeasureInputResponse(records, report, thresholds, settings.Loopback);
		Dictionary<int, List<(int Sequence, Vector3 Position)>> hostMotion = BuildHostMotionSeries(records);
		MeasureSettledError(records, hostMotion, perUnitErrors, perUnitFinalErrors, report, thresholds, settings.Loopback);
		MeasureSnapsAndCorrections(records, report, thresholds);
		MeasureRollbacks(records, report, thresholds);
		MeasureAllocationAndOrdering(records, report, thresholds);
		if (settings.Loopback) MeasureAuthorityInvariant(records, report, thresholds);
		MeasurePathingParity(records, report, settings.Loopback);
		MeasureOscillation(records, report);
		MeasureCorrectionRate(records, report);
		MeasureUnpredictedOrderLatency(records, report, settings);

		foreach (KeyValuePair<int, List<float>> unit in perUnitErrors)
		{
			unit.Value.Sort();
			float unitMaximum = unit.Value[^1];
			float unitMean = unit.Value.Average();
			float unitFinal = perUnitFinalErrors.TryGetValue(unit.Key, out float finalError) ? finalError : 0f;
			report.UnitSummaries.Add($"unit {unit.Key}: samples {unit.Value.Count}, mean {unitMean:F3} m, max {unitMaximum:F3} m, final {unitFinal:F3} m");
		}

		report.Verdict = report.Failures == 0 ? "PASS" : "FAIL";
		return report;
	}

	private static bool IsWithinScenarioStage(ParsedRecords records, int tick)
	{
		if (records.Scenario.Count == 0) return true;
		int first = records.Scenario[0].Tick;
		int last = records.Scenario[^1].Tick;
		return tick >= first && tick <= last + 90;
	}

	private static void MeasureInputResponse(ParsedRecords records, PredictionHarnessReport report, PredictionThresholds thresholds, bool loopback)
	{
		ScenarioSample? firstOrder = records.Scenario.FirstOrDefault(stage => stage.Stage == "move");
		if (firstOrder == null)
		{
			report.AddFailure("The scenario never issued the initial move order, so input response could not be measured.");
			return;
		}

		ClientTickSample? baseline = records.ClientTicks.LastOrDefault(sample => sample.Tick <= firstOrder.Tick);
		if (baseline == null)
		{
			report.AddFailure($"No client prediction sample exists at or before the move order tick {firstOrder.Tick}.");
			return;
		}

		ClientUnitSample? baselineUnit = baseline.Units.FirstOrDefault(unit => unit.EntityId == firstOrder.EntityId);
		if (baselineUnit == null)
		{
			report.AddFailure($"Move order unit {firstOrder.EntityId} was not tracked at tick {baseline.Tick}.");
			return;
		}

		ClientTickSample? firstMoved = null;
		float largestDistance = 0f;
		for (int index = 0; index < records.ClientTicks.Count; index++)
		{
			ClientTickSample sample = records.ClientTicks[index];
			if (sample.Tick <= baseline.Tick) continue;
			if (sample.Tick > baseline.Tick + 10) break;

			ClientUnitSample? unit = sample.Units.FirstOrDefault(candidate => candidate.EntityId == firstOrder.EntityId);
			if (unit == null) continue;
			float distance = Vector3.Distance(unit.Predicted, baselineUnit.Predicted);
			if (distance > largestDistance) largestDistance = distance;
			if (distance <= 0.02f) continue;
			firstMoved = sample;
			break;
		}

		if (firstMoved == null)
		{
			report.AddFailure($"Unit {firstOrder.EntityId} did not move on the client after the move order; the largest predicted displacement was {largestDistance:F3} m.");
			return;
		}

		float responseTicks = firstMoved.Tick - baseline.Tick;
		report.InputResponseTicks = responseTicks;
		report.MaximumInputResponseTicks = responseTicks;
		if (responseTicks > thresholds.InputResponseTicks)
		{
			report.AddFailure($"Unit {firstOrder.EntityId} responded to the move order after {responseTicks:F1} ticks, above the {thresholds.InputResponseTicks:F1} tick bound.");
		}

		ClientUnitSample? movedUnit = firstMoved.Units.First(candidate => candidate.EntityId == firstOrder.EntityId);
		float authoritativeTravel = Vector3.Distance(movedUnit.Authoritative, baselineUnit.Authoritative);
		report.StageSummaries.Add($"move: response {responseTicks:F1} ticks, predicted displacement {largestDistance:F3} m, authoritative displacement {authoritativeTravel:F3} m");
		if (!loopback && authoritativeTravel >= largestDistance * 0.5f)
		{
			report.AddFailure($"The move response was dominated by authoritative movement ({authoritativeTravel:F3} m of {largestDistance:F3} m), so local prediction was not the source of the displacement.");
		}
	}

	private static Dictionary<int, List<(int Sequence, Vector3 Position)>> BuildHostMotionSeries(ParsedRecords records)
	{
		var series = new Dictionary<int, List<(int Sequence, Vector3 Position)>>();
		var sequences = new List<int>(records.HostSnapshots.Keys);
		sequences.Sort();
		foreach (int sequence in sequences)
		{
			foreach (var pair in records.HostSnapshots[sequence].Units)
			{
				if (!series.TryGetValue(pair.Key, out List<(int Sequence, Vector3 Position)>? positions))
				{
					positions = new List<(int Sequence, Vector3 Position)>();
					series[pair.Key] = positions;
				}

				positions.Add((sequence, pair.Value.Position));
			}
		}

		return series;
	}

	private static bool IsAuthorityAtRest(Dictionary<int, List<(int Sequence, Vector3 Position)>> hostMotion, int serverEntityId, int clientSequence)
	{
		if (!hostMotion.TryGetValue(serverEntityId, out List<(int Sequence, Vector3 Position)>? positions)) return false;

		int previousIndex = -1;
		int latestIndex = -1;
		for (int index = 0; index < positions.Count; index++)
		{
			if (positions[index].Sequence > clientSequence) break;
			previousIndex = latestIndex;
			latestIndex = index;
		}

		if (latestIndex < 0 || previousIndex < 0) return false;
		return Vector3.Distance(positions[latestIndex].Position, positions[previousIndex].Position) <= StationaryAuthoritativeMeters;
	}

	private static void MeasureSettledError(ParsedRecords records, Dictionary<int, List<(int Sequence, Vector3 Position)>> hostMotion, Dictionary<int, List<float>> perUnitErrors, Dictionary<int, float> perUnitFinalErrors, PredictionHarnessReport report, PredictionThresholds thresholds, bool loopback)
	{
		ScenarioSample? lastOrder = records.Scenario.LastOrDefault();
		if (lastOrder == null || records.ClientTicks.Count == 0)
		{
			report.AddFailure("No scenario order was recorded, so settled prediction error could not be measured.");
			return;
		}

		int settleWindowStart = lastOrder.Tick + 30;
		var lastOrderTickByUnit = new Dictionary<int, int>();
		foreach (ClientTickSample tick in records.ClientTicks)
		{
			foreach (ClientUnitSample unit in tick.Units)
			{
				if (!unit.HasOrder) continue;
				lastOrderTickByUnit[unit.EntityId] = tick.Tick;
			}
		}

		var sampleFirstSeenTick = new Dictionary<long, int>();
		var sequenceFirstSeenTick = new Dictionary<int, int>();
		foreach (ClientTickSample tick in records.ClientTicks)
		{
			if (!sequenceFirstSeenTick.ContainsKey(tick.Sequence)) sequenceFirstSeenTick[tick.Sequence] = tick.Tick;
			foreach (ClientUnitSample unit in tick.Units)
			{
				if (unit.AuthoritativeSequence < 0) continue;
				long sampleKey = ((long)unit.EntityId << 32) | (uint)unit.AuthoritativeSequence;
				if (!sampleFirstSeenTick.ContainsKey(sampleKey)) sampleFirstSeenTick[sampleKey] = tick.Tick;
			}
		}

		var settledErrors = new List<float>();
		var settledErrorsByUnit = new Dictionary<int, List<(int Tick, float Error)>>();
		var sampledUnits = new HashSet<int>();
		foreach (ClientTickSample tick in records.ClientTicks)
		{
			if (tick.Tick < settleWindowStart) continue;
			foreach (ClientUnitSample unit in tick.Units)
			{
				if (unit.AuthoritativeSequence < 0) continue;
				if (unit.HasOrder) continue;
				if (lastOrderTickByUnit.TryGetValue(unit.EntityId, out int lastUnitOrderTick) && tick.Tick < lastUnitOrderTick + SettledConvergenceTicks) continue;

				long sampleKey = ((long)unit.EntityId << 32) | (uint)unit.AuthoritativeSequence;
				int sampleAgeTicks = tick.Tick - sampleFirstSeenTick[sampleKey];
				// An in-process authority has no snapshot stream to stall, so freshness is inherent there.
				bool streamStalled = !loopback
					&& tick.Tick - sequenceFirstSeenTick.GetValueOrDefault(tick.Sequence, tick.Tick) > MaximumAuthoritativeSampleAgeTicks;
				if (streamStalled)
				{
					report.StaleSettledSamplesSkipped++;
					continue;
				}

				// Settled means both sides have finished: the client has dropped its predicted order, the paired host
				// snapshot shows no authoritative order, and the authority is not moving. Sampling earlier measures the
				// authoritative unit still travelling to a destination prediction already reached, which is the prediction
				// lead rather than residual error.
				if (!loopback)
				{
					if (!records.HostSnapshots.TryGetValue(tick.Sequence, out HostSnapshotSample? hostSnapshot)) continue;
					if (!hostSnapshot.Units.TryGetValue(unit.ServerEntityId, out HostUnitSample? hostUnit) || hostUnit.HasMoveTo) continue;
					if (!IsAuthorityAtRest(hostMotion, unit.ServerEntityId, tick.Sequence)) continue;
				}

				sampledUnits.Add(unit.EntityId);
				float settledError = Vector3.Distance(unit.Predicted, unit.Authoritative);
				if (settledError > report.MaximumSettledTransientMeters) report.MaximumSettledTransientMeters = settledError;
				if (!settledErrorsByUnit.TryGetValue(unit.EntityId, out List<(int Tick, float Error)>? unitErrors))
				{
					unitErrors = new List<(int Tick, float Error)>();
					settledErrorsByUnit[unit.EntityId] = unitErrors;
				}

				unitErrors.Add((tick.Tick, settledError));
			}
		}

		settledErrors.AddRange(SettleSustainedErrors(settledErrorsByUnit, thresholds, report, out string sustainedAttribution));
		report.ConcentratedOrderUnits = sampledUnits.Count;
		MeasureConvergenceDeadline(records, sequenceFirstSeenTick, lastOrderTickByUnit, report, thresholds, loopback);

		if (settledErrors.Count == 0)
		{
			report.AddFailure("No settled (order-free) prediction samples were recorded after the scenario completed.");
			return;
		}

		if (report.SettledErrorMeters > thresholds.SettledErrorMeters)
		{
			report.AddFailure($"Settled predicted position error is {report.SettledErrorMeters:F3} m, above the stated {thresholds.SettledErrorMeters:F3} m limit; prediction does not converge to authoritative state.{sustainedAttribution}");
		}

		foreach (KeyValuePair<int, float> unit in perUnitFinalErrors)
		{
			if (unit.Value <= thresholds.SettledErrorMeters) continue;
			report.AddFailure($"Unit {unit.Key} ends with a {unit.Value:F3} m permanent offset from authoritative state.");
		}
	}

	/// <summary>
	/// Reduces the settled error series to the error that is actually sustained. Every run of consecutive samples above
	/// the limit is kept only when it lasts at least <see cref="SettledSustainedTicks"/> samples, so one authoritative
	/// sample that legitimately moved the unit cannot be reported as residual error, while a drift that persists or grows
	/// still fails. The raw maximum is reported separately so the transient is never hidden.
	/// </summary>
	private static List<float> SettleSustainedErrors(Dictionary<int, List<(int Tick, float Error)>> settledErrorsByUnit, PredictionThresholds thresholds, PredictionHarnessReport report, out string attribution)
	{
		var sustained = new List<float>();
		attribution = string.Empty;
		int worstUnit = -1;
		int worstTick = -1;
		int worstRunLength = 0;

		foreach (KeyValuePair<int, List<(int Tick, float Error)>> entry in settledErrorsByUnit)
		{
			sustained.AddRange(entry.Value.Select(sample => sample.Error));

			int runStart = -1;
			float runMaximum = 0f;
			for (int index = 0; index <= entry.Value.Count; index++)
			{
				bool aboveLimit = index < entry.Value.Count && entry.Value[index].Error > thresholds.SettledErrorMeters;
				if (aboveLimit)
				{
					if (runStart < 0) runStart = index;
					if (entry.Value[index].Error > runMaximum) runMaximum = entry.Value[index].Error;
					continue;
				}

				if (runStart < 0) continue;
				int runLength = index - runStart;
				if (runLength >= SettledSustainedTicks && runMaximum > report.SettledErrorMeters)
				{
					report.SettledErrorMeters = runMaximum;
					worstUnit = entry.Key;
					worstTick = entry.Value[runStart].Tick;
					worstRunLength = runLength;
				}

				runStart = -1;
				runMaximum = 0f;
			}
		}

		if (worstUnit >= 0)
		{
			attribution = $" Unit {worstUnit} held that error for {worstRunLength} consecutive stays starting at tick {worstTick}.";
		}

		return sustained;
	}

	private static void MeasureSnapsAndCorrections(ParsedRecords records, PredictionHarnessReport report, PredictionThresholds thresholds)
	{
		var sequenceFirstSeenTick = new Dictionary<int, int>();
		foreach (ClientTickSample tick in records.ClientTicks)
		{
			if (!sequenceFirstSeenTick.ContainsKey(tick.Sequence)) sequenceFirstSeenTick[tick.Sequence] = tick.Tick;
		}

		foreach (var snap in records.Snaps)
		{
			if (!snap.Explained)
			{
				report.UnexplainedSnapCount++;
				report.AddFailure($"Unit {snap.EntityId} snapped at tick {snap.Tick} (sequence {snap.Sequence}): {snap.Reason}{DescribeSnapContext(records, sequenceFirstSeenTick, snap.EntityId, snap.Tick, snap.Sequence)}");
			}
			else
			{
				report.ExplainedSnapCount++;
			}
		}

		foreach (CorrectionSample correction in records.Corrections)
		{
			float displacement = Vector3.Distance(correction.Predicted, correction.Corrected);
			if (displacement <= thresholds.CorrectionDisplacementMeters) continue;
			report.LargeCorrectionCount++;
			report.AddFailure($"Correction of unit {correction.EntityId} at tick {correction.Tick} moved the unit {displacement:F3} m in one step without a snap event.");
		}
	}

	/// <summary>
	/// Checks that every unit returns within the settled error limit inside a bounded window after its last order stops
	/// being active on the client. This is the strict counterpart to the settled metric: it samples the newest
	/// authoritative position the client actually received, so a snapshot that never arrived delays the deadline instead
	/// of being blamed on prediction, and a prediction that drifts or stalls still fails.
	/// </summary>
	private static void MeasureConvergenceDeadline(ParsedRecords records, Dictionary<int, int> sequenceFirstSeenTick, Dictionary<int, int> lastOrderTickByUnit, PredictionHarnessReport report, PredictionThresholds thresholds, bool loopback)
	{
		foreach (KeyValuePair<int, int> entry in lastOrderTickByUnit)
		{
			int entityId = entry.Key;
			int lastOrderTick = entry.Value;
			int convergenceTick = -1;
			float lastError = 0f;
			int lastAge = -1;

			foreach (ClientTickSample tick in records.ClientTicks)
			{
				if (tick.Tick <= lastOrderTick) continue;
				if (tick.Tick > lastOrderTick + ConvergenceDeadlineTicks) break;
				foreach (ClientUnitSample unit in tick.Units)
				{
					if (unit.EntityId != entityId || unit.AuthoritativeSequence < 0) continue;
					int age = loopback ? 0 : tick.Tick - sequenceFirstSeenTick.GetValueOrDefault(tick.Sequence, tick.Tick);
					if (age > MaximumAuthoritativeSampleAgeTicks) continue;

					float error = Vector3.Distance(unit.Predicted, unit.Authoritative);
					lastError = error;
					lastAge = age;
					if (error > thresholds.SettledErrorMeters || convergenceTick >= 0) continue;
					convergenceTick = tick.Tick;
				}

				if (convergenceTick >= 0) break;
			}

			if (convergenceTick < 0)
			{
				report.AddFailure($"Unit {entityId} did not return within {thresholds.SettledErrorMeters:F2} m of authoritative state within {ConvergenceDeadlineTicks} ticks of its last order at tick {lastOrderTick}; the last fresh sample left it {lastError:F3} m off ({lastAge} ticks old).");
				continue;
			}

			int elapsed = convergenceTick - lastOrderTick;
			if (elapsed > report.SlowestConvergenceTicks) report.SlowestConvergenceTicks = elapsed;
		}
	}

	private static string DescribeSnapContext(ParsedRecords records, Dictionary<int, int> sequenceFirstSeenTick, int entityId, int snapTick, int snapSequence)
	{
		ClientTickSample? tick = records.ClientTicks.LastOrDefault(sample => sample.Tick < snapTick);
		if (tick == null) return string.Empty;

		int streamAgeTicks = sequenceFirstSeenTick.TryGetValue(snapSequence, out int firstSeen) ? tick.Tick - firstSeen : -1;
		ClientUnitSample? unit = tick.Units.FirstOrDefault(sample => sample.EntityId == entityId);
		if (unit == null) return $" The client applied that snapshot {streamAgeTicks} ticks earlier but had already stopped recording the unit.";

		int lastCommandTick = records.CommandTicks.TryGetValue(unit.CommandId, out int commandTick) ? commandTick : -1;
		float errorMeters = Vector3.Distance(unit.Predicted, unit.Authoritative);

		StringBuilder series = new();
		for (int previous = snapTick - 9; previous < snapTick; previous++)
		{
			ClientTickSample? earlier = records.ClientTicks.LastOrDefault(sample => sample.Tick == previous);
			ClientUnitSample? earlierUnit = earlier?.Units.FirstOrDefault(sample => sample.EntityId == entityId);
			if (earlierUnit == null) continue;
			series.Append(series.Length == 0 ? string.Empty : ", ");
			series.Append($"{previous}:{Vector3.Distance(earlierUnit.Predicted, earlierUnit.Authoritative):F2}{(earlierUnit.HasOrder ? "o" : "-")}");
		}

		return $" The snap moved the unit {errorMeters:F2} m onto the authoritative position; the client had applied that snapshot {streamAgeTicks} ticks earlier, the unit had {(unit.HasOrder ? "a predicted order" : "no predicted order")} with path {unit.CurrentWaypoint}/{unit.WaypointCount} to {unit.MoveTarget.X:F2},{unit.MoveTarget.Y:F2},{unit.MoveTarget.Z:F2}, and its last submitted command {unit.CommandId} was {(lastCommandTick >= 0 ? snapTick - lastCommandTick : -1)} ticks before the snap. Error series (tick:meters, o = has order): {series}.";
	}

	private static void MeasureRollbacks(ParsedRecords records, PredictionHarnessReport report, PredictionThresholds thresholds)
	{
		report.RejectionsReceived = records.Rejections.Count;
		report.RollbackCount = records.Rollbacks.Count;

		foreach (RollbackSample rollback in records.Rollbacks)
		{
			ClientTickSample? rollbackTick = records.ClientTicks.LastOrDefault(sample => sample.Tick <= rollback.Tick);
			if (rollbackTick == null) continue;

			ClientTickSample? cleared = records.ClientTicks.FirstOrDefault(sample =>
				sample.Tick >= rollback.Tick
				&& sample.Units.Exists(unit => unit.EntityId == rollback.EntityId && !unit.HasOrder));
			if (cleared == null)
			{
				report.AddFailure($"Rolled-back order {rollback.CommandId} left unit {rollback.EntityId} with client-side order state after tick {rollback.Tick}.");
				continue;
			}

			ClientTickSample? settled = records.ClientTicks.FirstOrDefault(sample =>
				sample.Tick >= cleared.Tick
				&&			sample.Units.Exists(unit => unit.EntityId == rollback.EntityId && unit.AuthoritativeSequence >= 0 && !unit.HasOrder && Vector3.Distance(unit.Predicted, unit.Authoritative) <= thresholds.SettledErrorMeters));
			float rollbackTicks = settled != null ? settled.Tick - rollback.Tick : thresholds.RollbackWindowTicks + 1f;
			if (rollbackTicks > report.WorstRollbackTicks) report.WorstRollbackTicks = rollbackTicks;
			if (settled == null)
			{
				report.AddFailure($"Rolled-back order {rollback.CommandId} did not converge unit {rollback.EntityId} within {thresholds.RollbackWindowTicks:F0} ticks ({rollback.Reason}).");
			}
		}
	}

	private static void MeasureAllocationAndOrdering(ParsedRecords records, PredictionHarnessReport report, PredictionThresholds thresholds)
	{
		if (records.AllocationSamples.Count > 0)
		{
			long maximum = 0;
			long total = 0;
			long sharedMaximum = 0;
			foreach (var sample in records.AllocationSamples)
			{
				total += sample.PredictionBytes;
				if (sample.PredictionBytes > maximum) maximum = sample.PredictionBytes;
				if (sample.SharedMovementBytes > sharedMaximum) sharedMaximum = sample.SharedMovementBytes;
			}

			report.MaximumTickAllocatedBytes = maximum;
			report.MeanTickAllocatedBytes = (double)total / records.AllocationSamples.Count;
			report.MaximumSharedMovementAllocatedBytes = sharedMaximum;
			if (maximum > thresholds.AllocationBytesPerTick)
			{
				report.AddFailure($"A prediction tick allocated {maximum} bytes, above the {thresholds.AllocationBytesPerTick} byte budget; the prediction tick must not allocate.");
			}
		}
		else
		{
			report.AddFailure("No prediction tick allocation samples were recorded, so per-tick allocation was not measured.");
		}

		report.SubmittedCommandCount = records.CommandTicks.Count;
		report.AcknowledgedCommandCount = records.AcceptedCommands
			.Select(entry => entry.CommandId)
			.Concat(records.Rejections.Select(entry => entry.CommandId))
			.Distinct()
			.Count();
		report.SnapshotOrderingViolations = records.OrderingViolations.Count;
		report.SnapshotOrderingRespected = records.OrderingViolations.Count == 0;
		foreach (var violation in records.OrderingViolations)
		{
			report.AddFailure($"Snapshot {violation.RejectedSequence} was offered to prediction at tick {violation.Tick} after {violation.LastAppliedSequence} had already been applied, so snapshots reached prediction out of order.");
		}
	}

	private static void MeasureAuthorityInvariant(ParsedRecords records, PredictionHarnessReport report, PredictionThresholds thresholds)
	{
		float worstError = 0f;
		int worstTick = -1;
		int worstUnit = -1;
		foreach (ClientTickSample tick in records.ClientTicks)
		{
			foreach (ClientUnitSample unit in tick.Units)
			{
				if (unit.AuthoritativeSequence < 0) continue;
				float error = Vector3.Distance(unit.Predicted, unit.Authoritative);
				if (!float.IsFinite(error) || error <= worstError) continue;
				worstError = error;
				worstTick = tick.Tick;
				worstUnit = unit.EntityId;
			}
		}

		report.AuthorityInvariantMaximumErrorMeters = worstError;
		if (worstError > thresholds.LoopbackInvariantMeters)
		{
			report.AddFailure($"Unit {worstUnit} differs from the authority by {worstError:F4} m at tick {worstTick}; an in-process authority and its local viewpoint must not diverge, so prediction is integrating movement the authority already integrated.");
		}

		foreach (int commandId in records.CommandTicks.Keys)
		{
			bool acknowledged = records.AcceptedCommands.Exists(entry => entry.CommandId == commandId)
				|| records.Rejections.Exists(entry => entry.CommandId == commandId);
			if (acknowledged) continue;
			report.UnacknowledgedCommandCount++;
			report.AddFailure($"Command {commandId} was submitted through the in-process transport at tick {records.CommandTicks[commandId]} but the authority never acknowledged or rejected it.");
		}
	}

	private static void MeasurePathingParity(ParsedRecords records, PredictionHarnessReport report, bool loopback)
	{
		if (loopback) return;

		foreach (ClientTickSample tick in records.ClientTicks)
		{
			if (tick.Sequence < 0) continue;
			if (!records.HostSnapshots.TryGetValue(tick.Sequence, out HostSnapshotSample? hostSnapshot)) continue;

			foreach (ClientUnitSample unit in tick.Units)
			{
				if (!unit.HasPath || unit.WaypointCount <= 0) continue;
				if (!hostSnapshot.Units.TryGetValue(unit.ServerEntityId, out HostUnitSample? hostUnit)) continue;
				if (!hostUnit.HasPath || hostUnit.WaypointCount <= 0) continue;

				if (Vector3.Distance(unit.MoveTarget, hostUnit.MoveTarget) > PathTargetToleranceMeters) continue;

				int cornerCount = Math.Min(unit.Waypoints.Length, hostUnit.Waypoints.Length) - PathStartWaypointCount;
				if (cornerCount <= 0) continue;

				report.PathComparisons++;
				if (unit.WaypointCount != hostUnit.WaypointCount)
				{
					report.PathMismatches++;
					if (report.PathMismatches <= 5)
					{
						report.AddFailure($"Predicted unit {unit.EntityId} follows a different path than the host at tick {tick.Tick} (sequence {tick.Sequence}): client waypoints {unit.WaypointCount}, host waypoints {hostUnit.WaypointCount}.");
					}

					continue;
				}

				int differingIndex = -1;
				float differingDistance = 0f;
				int compared = Math.Min(unit.Waypoints.Length, hostUnit.Waypoints.Length);

				for (int index = PathStartWaypointCount; index < compared; index++)
				{
					float distance = Vector3.Distance(unit.Waypoints[index], hostUnit.Waypoints[index]);
					if (distance <= PathWaypointToleranceMeters) continue;
					differingIndex = index;
					differingDistance = distance;
					break;
				}

				if (differingIndex < 0) continue;
				report.PathMismatches++;
				if (report.PathMismatches <= 5)
				{
					report.AddFailure($"Predicted unit {unit.EntityId} follows a different path than the host at tick {tick.Tick} (sequence {tick.Sequence}): corner {differingIndex} is {differingDistance:F3} m apart.");
				}
			}
		}

		if (report.PathComparisons < MinimumPathComparisons)
		{
			report.AddFailure($"Only {report.PathComparisons} confirmed predicted orders had a path compared against the host (minimum {MinimumPathComparisons}), so pathing parity was not verified.");
		}
	}

	/// <summary>
	/// A correction must not overshoot: after it is applied, the distance to the authority has to keep shrinking until it
	/// is inside the settled band. Only samples where the authority itself is at rest can testify to that, because a
	/// moving authority legitimately increases the distance while the client waits for the next snapshot.
	/// </summary>
	/// <summary>
	/// A correction may only reduce the distance to the authority and must never cross it. Both properties are checked
	/// on every recorded correction, which is the only place overshoot can be introduced; a time series of distance
	/// cannot distinguish overshoot from the authoritative unit still travelling towards its destination.
	/// </summary>
	private static void MeasureOscillation(ParsedRecords records, PredictionHarnessReport report)
	{
		foreach (CorrectionSample correction in records.Corrections)
		{
			Vector3 errorBefore = correction.Predicted - correction.Authoritative;
			Vector3 errorAfter = correction.Corrected - correction.Authoritative;
			float distanceBefore = errorBefore.Length();
			float distanceAfter = errorAfter.Length();

			if (distanceAfter > distanceBefore + 0.0001f)
			{
				report.OscillationViolations++;
				report.AddFailure($"A correction of unit {correction.EntityId} at tick {correction.Tick} increased the distance to the authority from {distanceBefore:F3} m to {distanceAfter:F3} m.");
				continue;
			}

			if (distanceBefore <= 0.0001f) continue;
			if (Vector3.Dot(errorBefore, errorAfter) >= 0f) continue;

			report.OscillationViolations++;
			report.AddFailure($"A correction of unit {correction.EntityId} at tick {correction.Tick} overshot the authoritative position instead of approaching it ({distanceBefore:F3} m to {distanceAfter:F3} m).");
		}
	}

	private static void MeasureCorrectionRate(ParsedRecords records, PredictionHarnessReport report)
	{
		if (records.ClientTicks.Count < 20) return;
		int firstTick = records.ClientTicks[0].Tick;
		int lastTick = records.ClientTicks[^1].Tick;
		int midpoint = firstTick + (lastTick - firstTick) / 2;
		int firstHalf = records.Corrections.Count(correction => correction.Tick <= midpoint);
		int secondHalf = records.Corrections.Count - firstHalf;
		float firstSeconds = Math.Max(0.1f, (midpoint - firstTick) / 30f);
		float secondSeconds = Math.Max(0.1f, (lastTick - midpoint) / 30f);
		report.CorrectionsPerSecondFirstHalf = firstHalf / firstSeconds;
		report.CorrectionsPerSecondSecondHalf = secondHalf / secondSeconds;
		if (report.CorrectionsPerSecondSecondHalf > report.CorrectionsPerSecondFirstHalf * 2.5f + 0.5f)
		{
			report.AddFailure($"Correction rate trends upward over the run ({report.CorrectionsPerSecondFirstHalf:F2}/s to {report.CorrectionsPerSecondSecondHalf:F2}/s); prediction is not stable.");
		}
	}

	/// <summary>
	/// Measures how late an order the client cannot anticipate becomes visible, which is the class of behaviour that
	/// motivated client-side script execution. Every host order target is compared with the targets the scenario itself
	/// submitted; anything else is a host-only order (map script, AI, or an authority-side override) and its visibility
	/// delay is measured against the snapshot sequence it first appeared in. The claim this replaces is "the client runs
	/// the map script for prediction", so the envelope is stated in ticks over the injected one-way delay instead.
	/// </summary>
	private static void MeasureUnpredictedOrderLatency(ParsedRecords records, PredictionHarnessReport report, MultiplayerEcsRunSettings settings)
	{
		if (records.HostSnapshots.Count == 0 || records.ClientTicks.Count == 0) return;

		var sequenceNumbers = new List<int>(records.HostSnapshots.Keys);
		sequenceNumbers.Sort();

		var hostTransitions = new Dictionary<int, List<(int Sequence, Vector3 Target)>>();
		foreach (int sequence in sequenceNumbers)
		{
			HostSnapshotSample snapshot = records.HostSnapshots[sequence];
			foreach (var pair in snapshot.Units)
			{
				HostUnitSample unit = pair.Value;
				if (!unit.HasMoveTo) continue;
				if (!hostTransitions.TryGetValue(pair.Key, out List<(int Sequence, Vector3 Target)>? transitions))
				{
					transitions = new List<(int Sequence, Vector3 Target)>();
					hostTransitions[pair.Key] = transitions;
				}

				if (transitions.Count > 0 && Vector3.Distance(transitions[^1].Target, unit.MoveTarget) <= PathTargetToleranceMeters) continue;
				transitions.Add((sequence, unit.MoveTarget));
			}
		}

		var latencies = new List<int>();
		foreach (var pair in hostTransitions)
		{
			int serverEntityId = pair.Key;
			foreach (var transition in pair.Value)
			{
				if (IsScenarioTarget(records, transition.Target)) continue;

				int firstObservation = -1;
				foreach (ClientTickSample tick in records.ClientTicks)
				{
					if (tick.Sequence < transition.Sequence) continue;
					ClientUnitSample? unit = tick.Units.FirstOrDefault(candidate => candidate.ServerEntityId == serverEntityId);
					if (unit == null) continue;
					if (!unit.HasOrder) continue;
					if (Vector3.Distance(unit.MoveTarget, transition.Target) > PathTargetToleranceMeters) continue;
					firstObservation = tick.Sequence;
					break;
				}

				if (firstObservation < 0) continue;
				latencies.Add(firstObservation - transition.Sequence);
			}
		}

		report.UnpredictedOrderSamples = latencies.Count;
		int oneWayTicks = (int)MathF.Ceiling(settings.OneWayDelayMilliseconds / 1000f * 30f);
		report.UnpredictedOrderBudgetTicks = oneWayTicks + 2;
		if (latencies.Count == 0)
		{
			report.StageSummaries.Add("unpredicted orders: none observed, so the host-only order visibility envelope is unmeasured for this map");
			return;
		}

		if (latencies.Count < MinimumUnpredictedOrderSamples)
		{
			report.StageSummaries.Add($"unpredicted orders: only {latencies.Count} samples, below the {MinimumUnpredictedOrderSamples} needed to state an envelope, so no p95 is published");
			return;
		}

		latencies.Sort();
		report.UnpredictedOrderP95Ticks = latencies[(int)Math.Min(latencies.Count - 1, Math.Floor(latencies.Count * 0.95))];
		report.UnpredictedOrderMaximumTicks = latencies[^1];
		report.StageSummaries.Add($"unpredicted orders: {latencies.Count} samples, p95 {report.UnpredictedOrderP95Ticks} ticks, max {report.UnpredictedOrderMaximumTicks} ticks, budget {report.UnpredictedOrderBudgetTicks} ticks (one-way delay plus two ticks)");

		if (report.UnpredictedOrderP95Ticks > report.UnpredictedOrderBudgetTicks)
		{
			report.AddFailure($"Host-only orders took p95 {report.UnpredictedOrderP95Ticks} ticks to become visible on the client, above the {report.UnpredictedOrderBudgetTicks} tick envelope (one-way delay plus two ticks).");
		}
	}

	private static bool IsScenarioTarget(ParsedRecords records, Vector3 target)
	{
		foreach (ScenarioSample stage in records.Scenario)
		{
			if (Vector3.Distance(stage.Target, target) <= PathTargetToleranceMeters) return true;
		}

		return false;
	}

	private static void ReadClientRecords(string path, ParsedRecords records)
	{
		using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16);
		using BinaryReader reader = new(stream);
		uint magic = reader.ReadUInt32();
		if (magic != PredictionDiagnostics.FileMagic) throw new InvalidDataException("prediction_client.bin has an unexpected magic number.");
		int version = reader.ReadInt32();
		if (version != PredictionDiagnostics.FileVersion) throw new InvalidDataException($"prediction_client.bin uses unsupported version {version}.");
		reader.ReadBoolean();

		while (stream.Position < stream.Length)
		{
			byte kind = reader.ReadByte();
			switch (kind)
			{
				case PredictionDiagnostics.KindClientTick:
					records.ClientTicks.Add(ReadClientTick(reader));
					break;
				case PredictionDiagnostics.KindCommandRegistered:
					records.CommandTicks[reader.ReadInt32()] = reader.ReadInt32();
					ReadIntList(reader);
					break;
				case PredictionDiagnostics.KindCommandAccepted:
					records.AcceptedCommands.Add((reader.ReadInt32(), reader.ReadInt32()));
					break;
				case PredictionDiagnostics.KindRejection:
					reader.ReadBoolean();
					records.Rejections.Add((reader.ReadInt32(), reader.ReadInt32(), reader.ReadString()));
					break;
				case PredictionDiagnostics.KindRollback:
					records.Rollbacks.Add(new RollbackSample
					{
						CommandId = reader.ReadInt32(),
						EntityId = reader.ReadInt32(),
						Sequence = reader.ReadInt32(),
						Tick = reader.ReadInt32(),
						Reason = reader.ReadString()
					});
					break;
				case PredictionDiagnostics.KindSnap:
				{
					int entityId = reader.ReadInt32();
					int sequence = reader.ReadInt32();
					int tick = reader.ReadInt32();
					bool explained = reader.ReadBoolean();
					ReadVector(reader);
					ReadVector(reader);
					string reason = reader.ReadString();
					records.Snaps.Add((entityId, sequence, tick, explained, reason));
					break;
				}
				case PredictionDiagnostics.KindCorrection:
					records.Corrections.Add(new CorrectionSample
					{
						EntityId = reader.ReadInt32(),
						Sequence = reader.ReadInt32(),
						Tick = reader.ReadInt32(),
						Predicted = ReadVector(reader),
						Corrected = ReadVector(reader),
						Authoritative = ReadVector(reader)
					});
					break;
				case PredictionDiagnostics.KindPredictionStopped:
					reader.ReadInt32();
					reader.ReadString();
					break;
				case PredictionDiagnostics.KindScriptOrder:
					records.ScriptOrders.Add((reader.ReadInt32(), reader.ReadInt32(), ReadVector(reader), reader.ReadBoolean()));
					break;
				case PredictionDiagnostics.KindScriptUpdate:
					records.ScriptUpdateCount++;
					reader.ReadInt32();
					break;
				case PredictionDiagnostics.KindScenario:
					records.Scenario.Add(new ScenarioSample
					{
						Stage = reader.ReadString(),
						Tick = reader.ReadInt32(),
						EntityId = reader.ReadInt32(),
						Target = ReadVector(reader)
					});
					break;
				case PredictionDiagnostics.KindAllocation:
					records.AllocationSamples.Add((reader.ReadInt32(), reader.ReadInt64(), reader.ReadInt64()));
					break;
				case PredictionDiagnostics.KindSnapshotOrderingViolation:
					records.OrderingViolations.Add((reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32()));
					break;
				default:
					throw new InvalidDataException($"prediction_client.bin contains unknown record kind {kind}.");
			}
		}
	}

	private static ClientTickSample ReadClientTick(BinaryReader reader)
	{
		ClientTickSample tick = new()
		{
			Tick = reader.ReadInt32(),
			Sequence = reader.ReadInt32(),
			LocalPlayerIndex = reader.ReadInt32()
		};
		int unitCount = reader.ReadInt32();
		for (int index = 0; index < unitCount; index++)
		{
			ClientUnitSample unit = new()
			{
				EntityId = reader.ReadInt32(),
				ServerEntityId = reader.ReadInt32(),
				CommandId = reader.ReadInt32(),
				OwnerPlayerIndex = reader.ReadInt32(),
				LocalOwner = reader.ReadInt32() == tick.LocalPlayerIndex,
				HasOrder = reader.ReadBoolean(),
				HasPath = reader.ReadBoolean(),
				CurrentWaypoint = reader.ReadInt32(),
				WaypointCount = reader.ReadInt32(),
				Predicted = ReadVector(reader),
				Previous = ReadVector(reader),
				Authoritative = ReadVector(reader),
				MoveTarget = ReadVector(reader),
				AuthoritativeSequence = reader.ReadInt32()
			};

			int recordedWaypoints = reader.ReadInt32();
			unit.Waypoints = new Vector3[recordedWaypoints];
			for (int waypointIndex = 0; waypointIndex < recordedWaypoints; waypointIndex++) unit.Waypoints[waypointIndex] = ReadVector(reader);
			tick.Units.Add(unit);
		}

		return tick;
	}

	private static void ReadHostRecords(string path, ParsedRecords records)
	{
		using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16);
		using BinaryReader reader = new(stream);
		uint magic = reader.ReadUInt32();
		if (magic != PredictionDiagnostics.FileMagic) throw new InvalidDataException("prediction_host.bin has an unexpected magic number.");
		int version = reader.ReadInt32();
		if (version != PredictionDiagnostics.FileVersion) throw new InvalidDataException($"prediction_host.bin uses unsupported version {version}.");
		reader.ReadBoolean();

		while (stream.Position < stream.Length)
		{
			byte kind = reader.ReadByte();
			if (kind == PredictionDiagnostics.KindRejection)
			{
				reader.ReadBoolean();
				reader.ReadInt32();
				reader.ReadInt32();
				reader.ReadString();
				continue;
			}

			if (kind != PredictionDiagnostics.KindHostSnapshot) throw new InvalidDataException($"prediction_host.bin contains unknown record kind {kind}.");

			HostSnapshotSample snapshot = new() { Sequence = reader.ReadInt32() };
			int unitCount = reader.ReadInt32();
			for (int index = 0; index < unitCount; index++)
			{
				HostUnitSample unit = new()
				{
					ServerEntityId = reader.ReadInt32(),
					OwnerPlayerIndex = reader.ReadInt32(),
					HasMoveTo = reader.ReadBoolean(),
					HasPath = reader.ReadBoolean(),
					CurrentWaypoint = reader.ReadInt32(),
					WaypointCount = reader.ReadInt32(),
					Position = ReadVector(reader),
					MoveTarget = ReadVector(reader)
				};
				int recordedWaypoints = reader.ReadInt32();
				unit.Waypoints = new Vector3[recordedWaypoints];
				for (int waypointIndex = 0; waypointIndex < recordedWaypoints; waypointIndex++) unit.Waypoints[waypointIndex] = ReadVector(reader);
				snapshot.Units[unit.ServerEntityId] = unit;
			}

			records.HostSnapshots[snapshot.Sequence] = snapshot;
		}
	}

	private static Vector3 ReadVector(BinaryReader reader) => new(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());

	private static void ReadIntList(BinaryReader reader)
	{
		int count = reader.ReadInt32();
		for (int index = 0; index < count; index++) reader.ReadInt32();
	}
}
