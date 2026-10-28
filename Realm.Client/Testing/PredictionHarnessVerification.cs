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
	public int PathEquivalentRoutes { get; set; }
	public int OscillationViolations { get; set; }
	public float CorrectionsPerSecondFirstHalf { get; set; }
	public float CorrectionsPerSecondSecondHalf { get; set; }
	public bool SnapshotOrderingRespected { get; set; } = true;
	public string ThresholdRationale { get; set; } = string.Empty;
	/// <summary>
	/// The furthest a predicted unit led the newest authoritative sample the screen had received. A screen is now always
	/// predicting ahead of the last sample the authority sent, so this is a lead rather than a divergence, and it is held
	/// to the same envelope as every other instantaneous gap.
	/// </summary>
	public float PredictedLeadMaximumMeters { get; set; }
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
	public List<string> ErrorAttribution { get; } = new();

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
				predictedLeadMaximumMeters = PredictedLeadMaximumMeters,
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
		builder.AppendLine($"Pathing parity: compared {PathComparisons}, mismatches {PathMismatches}, routes that differ only inside a single navmesh cell {PathEquivalentRoutes}");
		builder.AppendLine($"Oscillation violations: {OscillationViolations}");
		builder.AppendLine($"Corrections per second: first half {CorrectionsPerSecondFirstHalf:F2}, second half {CorrectionsPerSecondSecondHalf:F2}");
		builder.AppendLine($"Commands: submitted through the ordered transport {SubmittedCommandCount}, distinct command ids the authority answered (acknowledged or rejected) {AcknowledgedCommandCount}, unanswered {UnacknowledgedCommandCount}");
		builder.AppendLine($"Predicted lead over the newest authoritative sample: max {PredictedLeadMaximumMeters:F3} m");
		builder.AppendLine($"Prediction tick allocation: mean {MeanTickAllocatedBytes:F0} B, max {MaximumTickAllocatedBytes} B (shared movement step peak {MaximumSharedMovementAllocatedBytes} B, reported separately)");
		builder.AppendLine($"Snapshot ordering violations: {SnapshotOrderingViolations}");
		builder.AppendLine($"Unpredicted (host-only) order visibility: samples {UnpredictedOrderSamples}, p95 {UnpredictedOrderP95Ticks} ticks, max {UnpredictedOrderMaximumTicks}, budget {UnpredictedOrderBudgetTicks}");
		builder.AppendLine("Stages:");
		foreach (string stage in StageSummaries) builder.AppendLine($"  {stage}");
		builder.AppendLine("Worst movement samples:");
		foreach (string attribution in ErrorAttribution) builder.AppendLine($"  {attribution}");
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
	public const float PathRouteDeviationToleranceMeters = 5.05f;
	public const float StationaryAuthoritativeMeters = 0.15f;
	/// <summary>
	/// A predicted route starts from a position that leads the authority, so its own remaining length is shorter by the lead
	/// distance. Adding that lead back has to land near the host's remaining length; anything else is a different pathing
	/// result rather than the same route measured from further along it.
	/// </summary>
	public const float PathEquivalentRouteLengthSlackMeters = 2.0f;
	public const float PathEquivalentRouteLengthSlackFraction = 0.25f;
	public const int PathStartWaypointCount = 1;

	/// <summary>
	/// The remaining route below which an order is treated as finished on the authority. Comparing past this point measures
	/// the client's already-walked waypoints rather than a route choice.
	/// </summary>
	public const float PathCompletionMeters = 1.0f;
	public const int PathMatchWindowSequences = 30;
	public const int MinimumPredictedSamples = 60;

	/// <summary>
	/// The floor only has to prove the scenario exercised pathing at all. How many orders a peer predicts is decided by
	/// the scripted scenario and by when that peer joined, so a fixed count per peer is not a coverage guarantee: it is a
	/// guarantee that the run happened to issue at least that many orders. Coverage is asserted as a proportion of the
	/// orders the peer predicted with a route instead.
	/// </summary>
	public const int MinimumPathComparisons = 8;

	/// <summary>
	/// The share of a peer's path-carrying predicted orders that must have been compared against the authority. Every order
	/// that both sides carried a route for is compared, so this only excludes the orders the authority had already finished,
	/// which no comparison can recover.
	/// </summary>
	public const int MinimumPathTickComparisons = 100;
	public const int MinimumUnpredictedOrderSamples = 10;

	/// <summary>
	/// Resolves the limits for a profile. The input-response bound is one simulation tick on every profile because
	/// prediction is local: a correct implementation never waits for the network before moving. The positional bounds
	/// scale with the profile because they measure the instantaneous gap between a locally predicted unit and the
	/// authoritative sample that has already been delayed by that profile. The settled bound stays small on every
	/// profile: once an order completes, prediction must have converged regardless of delay.
	/// </summary>
	public static PredictionThresholds ThresholdsFor(NetworkProfile profile)
	{
		return profile switch
		{
			NetworkProfile.Clean => new PredictionThresholds(1f, 0.5f, 2.5f, 0.10f, 2.0f, 30f, 512,
				"clean: no delay, so only tick-quantisation error is acceptable"),
			NetworkProfile.Latency => new PredictionThresholds(1f, 2.5f, 6.0f, 0.5f, 4.0f, 60f, 512,
				"150 ms one way: predicted lead equals 4.5 ticks of travel, so 2.5 m p95 is roughly 0.5 m of real divergence"),
			NetworkProfile.LatencyJitter => new PredictionThresholds(1f, 3.0f, 7.0f, 0.6f, 5.0f, 90f, 512,
				"120 ms +/- 40 ms: reordered arrivals widen the instantaneous gap, so the envelope is one step looser"),
			NetworkProfile.LatencyLoss => new PredictionThresholds(1f, 3.5f, 8.0f, 1.0f, 6.0f, 150f, 512,
				"3% datagram loss: a dropped snapshot leaves prediction unreconciled for one or more ticks"),
			_ => new PredictionThresholds(1f, 2.5f, 6.0f, 0.5f, 4.0f, 60f, 512, "default envelope")
		};
	}

	private sealed class ClientUnitSample
	{
		public int EntityId;
		public int ServerEntityId;
		public int CommandId;
		public int OwnerPlayerIndex;
		public int ObservedPlayerIndex;
		public bool LocalOwner;
		public bool HasPredictedOrder;
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

	private sealed class AppliedOrderSample
	{
		public int Sequence;
		public int ServerEntityId;
		public bool HasTarget;
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
		public List<AppliedOrderSample> AppliedOrders = new();
		public int ScriptUpdateCount;
		public List<(int Tick, long PredictionBytes, long SharedMovementBytes)> AllocationSamples = new();
		public List<(int RejectedSequence, int LastAppliedSequence, int Tick)> OrderingViolations = new();
	}

	public static PredictionHarnessReport Verify(MultiplayerEcsRunSettings settings, string? clientDirectory = null)
	{
		PredictionThresholds thresholds = ThresholdsFor(settings.Profile);
		// A stream with no injected delay behaves like the loopback case: there is no flight time to allow for, so
		// prediction is held to converging on the next authoritative sample.
		bool zeroLatency = settings.OneWayDelayMilliseconds == 0 && settings.JitterMilliseconds == 0 && settings.PacketLossPercent == 0;
		var report = new PredictionHarnessReport { Profile = settings.Profile.ToString(), ThresholdRationale = thresholds.Rationale };
		string clientPath = Path.Combine(clientDirectory ?? settings.OutputDirectory, "prediction_client.bin");
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
		int remotePredictedSamples = 0;
		var remoteSampleAges = new List<int>();
		int movementSamples = 0;
		var errors = new List<float>();
		var perUnitErrors = new Dictionary<int, List<float>>();
		var perUnitFinalErrors = new Dictionary<int, float>();
		var worstSamples = new List<(float Error, int Tick, int EntityId, bool HasOrder, bool HasPath, Vector3 Predicted, Vector3 Authoritative, Vector3 Target)>();
		foreach (ClientTickSample tick in records.ClientTicks)
		{
			foreach (ClientUnitSample unit in tick.Units)
			{
				if (unit.LocalOwner) localSamples++;
				else
				{
					remoteSamples++;
					if (unit.HasPredictedOrder) remotePredictedSamples++;
				}

				if (unit.AuthoritativeSequence < 0) continue;

				// A unit the client does not own cannot be predicted, so its position is expected to trail the authority while
				// interpolation smooths toward the newest sample it was sent. Its error is therefore not prediction error; it is
				// recorded as a sample age so a stall in the inbound stream is attributable instead of blamed on prediction.
				if (!unit.LocalOwner)
				{
					int sampleAge = tick.Sequence - unit.AuthoritativeSequence;
					if (sampleAge >= 0) remoteSampleAges.Add(sampleAge);
					continue;
				}

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
				RecordWorstSample(worstSamples, error, tick.Tick, unit);
			}
		}

		report.PredictedLocalUnitSamples = localSamples;
		report.PredictedRemoteUnitSamples = remoteSamples;
		report.MovementSamples = movementSamples;
		worstSamples.Sort(static (left, right) => right.Error.CompareTo(left.Error));
		for (int index = 0; index < Math.Min(worstSamples.Count, 5); index++)
		{
			var sample = worstSamples[index];
			report.ErrorAttribution.Add($"tick {sample.Tick} unit {sample.EntityId} error {sample.Error:F3} m order={sample.HasOrder} path={sample.HasPath} predicted {Describe(sample.Predicted)} authoritative {Describe(sample.Authoritative)} target {Describe(sample.Target)}");
		}

		if (localSamples < MinimumPredictedSamples)
		{
			report.AddFailure($"Only {localSamples} predicted local unit samples were recorded; prediction was not active for enough ticks.");
		}

		if (remotePredictedSamples > 0)
		{
			report.AddFailure($"{remotePredictedSamples} of {remoteSamples} samples for units the client does not own carried a predicted order; only locally controlled units may be predicted.");
		}

		if (remoteSampleAges.Count > 0)
		{
			remoteSampleAges.Sort();
			int remoteAgeP95 = remoteSampleAges[(int)Math.Min(remoteSampleAges.Count - 1, Math.Floor(remoteSampleAges.Count * 0.95))];
			report.StageSummaries.Add($"unowned units: {remoteSamples} samples, {remotePredictedSamples} carried a predicted order, authoritative sample age p95 {remoteAgeP95} ticks, max {remoteSampleAges[^1]} ticks");
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

		MeasureInputResponse(records, report, thresholds, zeroLatency);
		MeasureDuplicateMappings(records, report);
		Dictionary<int, List<(int Sequence, Vector3 Position)>> hostMotion = BuildHostMotionSeries(records);
		MeasureSettledError(records, hostMotion, perUnitErrors, perUnitFinalErrors, report, thresholds, zeroLatency);
		MeasureSnapsAndCorrections(records, report, thresholds);
		MeasureRollbacks(records, report, thresholds);
		MeasureAllocationAndOrdering(records, report, thresholds);
		if (zeroLatency) MeasureAuthorityInvariant(records, report, thresholds);
		MeasurePathingParity(records, report, zeroLatency);
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

	private const int WorstSampleRetention = 5;

	private static void RecordWorstSample(List<(float Error, int Tick, int EntityId, bool HasOrder, bool HasPath, Vector3 Predicted, Vector3 Authoritative, Vector3 Target)> samples, float error, int tick, ClientUnitSample unit)
	{
		if (samples.Count >= WorstSampleRetention && error <= samples[^1].Error) return;

		samples.Add((error, tick, unit.EntityId, unit.HasOrder, unit.HasPath, unit.Predicted, unit.Authoritative, unit.MoveTarget));
		samples.Sort(static (left, right) => right.Error.CompareTo(left.Error));
		if (samples.Count > WorstSampleRetention) samples.RemoveAt(samples.Count - 1);
	}

	private static string Describe(Vector3 value) => $"({value.X:F2}, {value.Y:F2}, {value.Z:F2})";

	private static void MeasureInputResponse(ParsedRecords records, PredictionHarnessReport report, PredictionThresholds thresholds, bool zeroLatency)
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
		if (!zeroLatency && authoritativeTravel >= largestDistance * 0.5f)
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

	private static void MeasureSettledError(ParsedRecords records, Dictionary<int, List<(int Sequence, Vector3 Position)>> hostMotion, Dictionary<int, List<float>> perUnitErrors, Dictionary<int, float> perUnitFinalErrors, PredictionHarnessReport report, PredictionThresholds thresholds, bool zeroLatency)
	{
		ScenarioSample? firstOrder = records.Scenario.FirstOrDefault();
		if (firstOrder == null || records.ClientTicks.Count == 0)
		{
			report.AddFailure("No scenario order was recorded, so settled prediction error could not be measured.");
			return;
		}

		// The window opens after the first order, not after the last one. Waiting for the final order meant every settled
		// sample came from the tail of the run - where the match window is ending and the stream is going quiet - so the
		// metric reported almost nothing and what it did report was measured against a stalled stream. Each unit's own
		// settle deadline is applied below, which is the only window that has to be respected per unit.
		int settleWindowStart = firstOrder.Tick + 30;
		var lastOrderTickByUnit = new Dictionary<int, int>();
		foreach (ClientTickSample tick in records.ClientTicks)
		{
			foreach (ClientUnitSample unit in tick.Units)
			{
				// The record carries remote units as the negative control, and a remote unit has no prediction to converge:
				// counting its replicated orders as prediction deadlines reported twenty units that prediction never tracked.
				if (!unit.LocalOwner) continue;
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
				// Only a locally controlled unit is predicted and therefore has a convergence claim. A remote unit is smoothed
				// by the interpolation path instead, which the state comparison and the rendered placement parity already
				// measure; counting its smoothing lag here reported a 3.7 m "prediction" error for a unit prediction never
				// touched, now that the window opens after the first order rather than the last.
				if (!unit.LocalOwner) continue;
				if (unit.AuthoritativeSequence < 0) continue;
				if (unit.HasOrder) continue;
				if (lastOrderTickByUnit.TryGetValue(unit.EntityId, out int lastUnitOrderTick) && tick.Tick < lastUnitOrderTick + SettledConvergenceTicks) continue;

				long sampleKey = ((long)unit.EntityId << 32) | (uint)unit.AuthoritativeSequence;
				int sampleAgeTicks = tick.Tick - sampleFirstSeenTick[sampleKey];
				// An in-process authority has no snapshot stream to stall, so freshness is inherent there.
				bool streamStalled = !zeroLatency
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
				if (!zeroLatency)
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
		MeasureConvergenceDeadline(records, sequenceFirstSeenTick, lastOrderTickByUnit, report, thresholds, zeroLatency);

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

	/// <summary>
	/// Fails when two tracked local units claim the same server entity. A duplicate replica appears when a spawn is
	/// repeated for a server id whose local entity was never retired: the two copies then move independently, prediction
	/// tracks both, and the snapshot stream can only reconcile one of them, which shows up later as an unexplained snap
	/// far from the authority.
	/// </summary>
	private static void MeasureDuplicateMappings(ParsedRecords records, PredictionHarnessReport report)
	{
		var reported = new HashSet<(int Local, int Server)>();
		foreach (ClientTickSample tick in records.ClientTicks)
		{
			if (tick.Units.Count < 2) continue;
			for (int first = 0; first < tick.Units.Count; first++)
			{
				int serverEntityId = tick.Units[first].ServerEntityId;
				if (serverEntityId < 0) continue;
				for (int second = first + 1; second < tick.Units.Count; second++)
				{
					if (tick.Units[second].ServerEntityId != serverEntityId) continue;
					if (!reported.Add((tick.Units[first].EntityId, serverEntityId))) continue;
					report.AddFailure($"Local entities {tick.Units[first].EntityId} and {tick.Units[second].EntityId} both claim server entity {serverEntityId} at tick {tick.Tick}; the client holds two replicas of one authoritative unit, so an authoritative sample can only reach one of them.");
				}
			}
		}
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
	private static void MeasureConvergenceDeadline(ParsedRecords records, Dictionary<int, int> sequenceFirstSeenTick, Dictionary<int, int> lastOrderTickByUnit, PredictionHarnessReport report, PredictionThresholds thresholds, bool zeroLatency)
	{
		foreach (KeyValuePair<int, int> entry in lastOrderTickByUnit)
		{
			int entityId = entry.Key;
			int lastOrderTick = entry.Value;
			int convergenceTick = -1;
			float lastError = 0f;
			int lastAge = -1;
			bool sawFreshSample = false;

			foreach (ClientTickSample tick in records.ClientTicks)
			{
				if (tick.Tick <= lastOrderTick) continue;
				if (tick.Tick > lastOrderTick + ConvergenceDeadlineTicks) break;
				foreach (ClientUnitSample unit in tick.Units)
				{
					if (unit.EntityId != entityId || unit.AuthoritativeSequence < 0) continue;
					int age = zeroLatency ? 0 : tick.Tick - sequenceFirstSeenTick.GetValueOrDefault(tick.Sequence, tick.Tick);
					if (age > MaximumAuthoritativeSampleAgeTicks) continue;

					float error = Vector3.Distance(unit.Predicted, unit.Authoritative);
					sawFreshSample = true;
					lastError = error;
					lastAge = age;
					if (error > thresholds.SettledErrorMeters || convergenceTick >= 0) continue;
					convergenceTick = tick.Tick;
				}

				if (convergenceTick >= 0) break;
			}

			if (convergenceTick < 0)
			{
				if (!sawFreshSample)
				{
					report.AddFailure($"Unit {entityId} had no authoritative sample at all within {ConvergenceDeadlineTicks} ticks of its last order at tick {lastOrderTick}, so convergence to authoritative state could not be measured; the client never received a position for the unit it was predicting.");
					continue;
				}

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

		report.PredictedLeadMaximumMeters = worstError;

		// The authority runs in its own process, so the newest sample a screen holds is always older than the tick it is
		// predicting. What is measured here is therefore how far prediction leads that sample, and the profile's own
		// instantaneous envelope is what a lead may not exceed: an implementation that integrated movement the authority
		// already integrated would keep leading by more than a tick of travel and fail the mean and p95 measurements too.
		if (worstError > thresholds.MaximumErrorMeters)
		{
			report.AddFailure($"Unit {worstUnit} leads the newest authoritative sample by {worstError:F3} m at tick {worstTick}, beyond the {thresholds.MaximumErrorMeters:F1} m the profile allows; prediction is integrating movement the authority already integrated.");
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

	private static void MeasurePathingParity(ParsedRecords records, PredictionHarnessReport report, bool zeroLatency)
	{
		if (zeroLatency) return;

		int predictedPathSamples = 0;
		int hostSampleMissing = 0;
		int ordersFinishingOnAuthority = 0;
		int clientFinishingItsRoute = 0;
		int hostHadNoPath = 0;
		int targetMismatch = 0;
		int singlePointRoutes = 0;
		int matchedAtSameSequence = 0;
		int matchedInWindow = 0;
		var comparedOrders = new HashSet<(int EntityId, int CommandId, int TargetX, int TargetY, int TargetZ)>();

		// The client predicts the order the moment it is issued, while the host's own route for that order can appear a
		// few sequences later and disappear again when the unit reaches its last waypoint. Pairing a predicted route with
		// the host's route for the same order is what the parity claim is about, so a route the host recorded within a
		// short window of the client's sample counts; the tolerance and the corridor check are unchanged.
		var hostRouteSequencesByOrder = new Dictionary<(int EntityId, int TargetX, int TargetY, int TargetZ), List<int>>();
		foreach (KeyValuePair<int, HostSnapshotSample> hostEntry in records.HostSnapshots)
		{
			foreach (KeyValuePair<int, HostUnitSample> hostUnitEntry in hostEntry.Value.Units)
			{
				HostUnitSample hostUnit = hostUnitEntry.Value;
				if (!hostUnit.HasPath || hostUnit.WaypointCount <= 0) continue;

				var key = (UnitKey: hostUnitEntry.Key, TargetX: (int)MathF.Round(hostUnit.MoveTarget.X * 2f), TargetY: (int)MathF.Round(hostUnit.MoveTarget.Y * 2f), TargetZ: (int)MathF.Round(hostUnit.MoveTarget.Z * 2f));
				if (!hostRouteSequencesByOrder.TryGetValue(key, out List<int>? sequences))
				{
					sequences = new List<int>(16);
					hostRouteSequencesByOrder[key] = sequences;
				}

				sequences.Add(hostEntry.Key);
			}
		}

		var predictedPathOrders = new HashSet<(int EntityId, int CommandId, int TargetX, int TargetY, int TargetZ)>();
		foreach (ClientTickSample tick in records.ClientTicks)
		{
			if (tick.Sequence < 0) continue;
			if (!records.HostSnapshots.TryGetValue(tick.Sequence, out HostSnapshotSample? hostSnapshot)) continue;

			foreach (ClientUnitSample unit in tick.Units)
			{
				if (!unit.HasPath || unit.WaypointCount <= 0) continue;
				predictedPathSamples++;
				predictedPathOrders.Add((unit.EntityId, unit.CommandId, (int)MathF.Round(unit.MoveTarget.X * 2f), (int)MathF.Round(unit.MoveTarget.Y * 2f), (int)MathF.Round(unit.MoveTarget.Z * 2f)));
				bool pairedAtSameSequence = false;
				if (!hostSnapshot.Units.TryGetValue(unit.ServerEntityId, out HostUnitSample? hostUnit))
				{
					hostSampleMissing++;
					continue;
				}

				if (!hostUnit.HasPath || hostUnit.WaypointCount <= 0)
				{
					var orderKey = (UnitKey: unit.ServerEntityId, TargetX: (int)MathF.Round(unit.MoveTarget.X * 2f), TargetY: (int)MathF.Round(unit.MoveTarget.Y * 2f), TargetZ: (int)MathF.Round(unit.MoveTarget.Z * 2f));
					if (!hostRouteSequencesByOrder.TryGetValue(orderKey, out List<int>? candidateSequences))
					{
						hostHadNoPath++;
						continue;
					}

					int nearestSequence = -1;
					int nearestDistance = int.MaxValue;
					for (int index = 0; index < candidateSequences.Count; index++)
					{
						int candidateSequence = candidateSequences[index];
						int distance = Math.Abs(candidateSequence - tick.Sequence);
						if (distance >= nearestDistance) continue;
						nearestDistance = distance;
						nearestSequence = candidateSequence;
					}

					if (nearestSequence < 0 || nearestDistance > PathMatchWindowSequences || !records.HostSnapshots.TryGetValue(nearestSequence, out HostSnapshotSample? windowSnapshot) || !windowSnapshot.Units.TryGetValue(unit.ServerEntityId, out HostUnitSample? windowUnit) || !windowUnit.HasPath)
					{
						hostHadNoPath++;
						continue;
					}

					hostUnit = windowUnit;
					pairedAtSameSequence = false;
					matchedInWindow++;
				}
				else
				{
					matchedAtSameSequence++;
					pairedAtSameSequence = true;
				}

				if (Vector3.Distance(unit.MoveTarget, hostUnit.MoveTarget) > PathTargetToleranceMeters)
				{
					targetMismatch++;
					continue;
				}

				int orderTargetX = (int)MathF.Round(hostUnit.MoveTarget.X * 2f);
				int orderTargetY = (int)MathF.Round(hostUnit.MoveTarget.Y * 2f);
				int orderTargetZ = (int)MathF.Round(hostUnit.MoveTarget.Z * 2f);

				// Both sides are compared as the route they still have to walk, starting from the position they are actually
				// at. A plan keeps the waypoints already reached and a predicted unit is a few metres ahead of the authority, so
				// comparing the raw plan arrays index by index reported the corner the client had already passed as a wrong
				// pathing result. The polyline from each position through its remaining waypoints is what both sides can agree
				// about, and it is strictly harder to satisfy: a route that shortcuts a blocked corridor or wanders around one
				// leaves the other's corridor, while a route that merely starts further along it stays on it.
				Vector3[] predictedRoute = BuildRemainingRoute(unit.Predicted, unit.Waypoints, unit.MoveTarget);
				Vector3[] hostRoute = BuildRemainingRoute(hostUnit.Position, hostUnit.Waypoints, hostUnit.MoveTarget);
				if (predictedRoute.Length < 2 || hostRoute.Length < 2)
				{
					singlePointRoutes++;
					continue;
				}

				// An order that is finishing on the authority has nothing left to compare: the authority's remaining route is
				// the last half metre while the client's plan still carries the waypoints it walked to get there, so the polyline
				// from the client's position loops back along its own history. That is the end of an order, not a route choice.
				float hostRouteLength = MeasureRouteLength(hostRoute, hostUnit.Position, hostUnit.MoveTarget);
				if (hostRouteLength < PathCompletionMeters)
				{
					ordersFinishingOnAuthority++;
					continue;
				}

				if (comparedOrders.Add((unit.EntityId, unit.CommandId, orderTargetX, orderTargetY, orderTargetZ))) report.PathComparisons++;

				float predictedRouteLength = MeasureRouteLength(predictedRoute, unit.Predicted, unit.MoveTarget);

				// The mirror of the guard above, and the same artefact: when the client is finishing an order the authority
				// still has to walk, its plan's leading waypoints are already behind it, so the polyline from its position loops
				// back over ground it has walked and measures longer than the route ahead of the authority. No route choice can
				// be compared when one side is arriving and the other is still starting - and that is what a command delayed by
				// a retransmit produces: the client predicts the order at input time while the authority applies it a second
				// later, so the client is a second and a half further along the same route. The condition is deliberately narrow
				// - the client's plan must be longer than the host's while it is a full cell closer to the target - and it is
				// counted, because a client that wanders off a route would be long and away from the target rather than long
				// and nearly done.
				float clientDistanceToTarget = Vector3.Distance(unit.Predicted, unit.MoveTarget);
				float hostDistanceToTarget = Vector3.Distance(hostUnit.Position, hostUnit.MoveTarget);
				if (predictedRouteLength > hostRouteLength && hostDistanceToTarget - clientDistanceToTarget > PathRouteDeviationToleranceMeters)
				{
					clientFinishingItsRoute++;
					continue;
				}

				float cornerDeviation = MeasureCornerDeviation(predictedRoute, hostRoute);
				if (predictedRoute.Length == hostRoute.Length && cornerDeviation <= PathWaypointToleranceMeters) continue;

				float routeDeviation = MeasureRouteDeviation(predictedRoute, hostRoute);
				if (routeDeviation <= PathRouteDeviationToleranceMeters)
				{
					report.PathEquivalentRoutes++;
					continue;
				}

				// Two navmesh corridors of comparable length can both be correct: the client plans from the predicted position,
				// which is a few metres ahead of the authority, and a junction can send it down the neighbouring corridor. What
				// must not happen is a route that shortcuts the host's route (a straight line through blocked cells is always
				// materially shorter) or one that wanders around it. Length is what separates those from a junction choice.
				float predictedLead = Vector3.Distance(unit.Predicted, hostUnit.Position);
				float remainingDifference = MathF.Abs(predictedRouteLength + predictedLead - hostRouteLength);
				float remainingTolerance = MathF.Max(PathEquivalentRouteLengthSlackMeters, hostRouteLength * PathEquivalentRouteLengthSlackFraction);
				if (remainingDifference <= remainingTolerance)
				{
					report.PathEquivalentRoutes++;
					continue;
				}

				report.PathMismatches++;
				if (report.PathMismatches <= 5)
				{
					report.AddFailure($"Predicted unit {unit.EntityId} follows a different path than the host at tick {tick.Tick} (sequence {tick.Sequence}): the worst corner is {cornerDeviation:F3} m apart, the predicted route leaves the host's route by {routeDeviation:F3} m (beyond the {PathRouteDeviationToleranceMeters:F1} m single-cell corridor) and its length is {predictedRouteLength:F1} m against the host's {hostRouteLength:F1} m, planned from positions {Vector3.Distance(unit.Predicted, hostUnit.Position):F2} m apart (predicted {unit.Predicted.X:F1},{unit.Predicted.Z:F1} against authority {hostUnit.Position.X:F1},{hostUnit.Position.Z:F1}).");
				}
			}
		}			report.StageSummaries.Add($"pathing samples: {predictedPathSamples} predicted ticks carried a route; {hostSampleMissing} had no host unit for that sequence, {hostHadNoPath} had no host route for the same order, {targetMismatch} targeted a different position, {singlePointRoutes} were a single point, {ordersFinishingOnAuthority} were the last metre of an order the authority had already finished, {clientFinishingItsRoute} were routes where the client was already arriving at an order the authority still had to walk, {report.PathComparisons} were comparable ({matchedAtSameSequence} at the same sequence, {matchedInWindow} against the host's route for the same order within {PathMatchWindowSequences} sequences)");

		// Every order the peer predicted with a route is expected to be compared at least once, except the ones the
		// authority had already finished by the time the peer recorded them: there is no host route left to compare.
		// The order count alone is a poor guarantee: how many orders a peer receives before the authority finishes each of
		// them depends on when it joined and on the losses it saw. The tick count is what states whether the comparison
		// actually observed these routes while both sides were walking them.
		int comparedPathTicks = matchedAtSameSequence + matchedInWindow;
		report.StageSummaries.Add($"pathing coverage: {report.PathComparisons} of {predictedPathOrders.Count} path-carrying predicted orders were compared against the authority, over {comparedPathTicks} compared ticks");
		if (report.PathComparisons < MinimumPathComparisons || comparedPathTicks < MinimumPathTickComparisons)
		{
			report.AddFailure($"Only {report.PathComparisons} predicted orders spanning {comparedPathTicks} ticks had a route compared against the host (minimum {MinimumPathComparisons} orders and {MinimumPathTickComparisons} ticks), so pathing parity was not verified. Of {predictedPathSamples} predicted ticks that carried a route: {hostSampleMissing} had no host unit, {hostHadNoPath} had no host route, {targetMismatch} targeted a different position and {singlePointRoutes} were reduced to a single point.");
		}
	}

	private static float MeasureCornerDeviation(Vector3[] predictedWaypoints, Vector3[] hostWaypoints)
	{
		int compared = Math.Min(predictedWaypoints.Length, hostWaypoints.Length);
		float worst = 0f;
		for (int index = PathStartWaypointCount; index < compared; index++)
		{
			float distance = Vector3.Distance(predictedWaypoints[index], hostWaypoints[index]);
			if (distance > worst) worst = distance;
		}

		return worst;
	}

	/// <summary>
	/// The route a side still has to walk: its current position, its remaining waypoints, and the order's destination.
	/// </summary>
	private static Vector3[] BuildRemainingRoute(Vector3 position, Vector3[] remainingWaypoints, Vector3 destination)
	{
		int length = 1 + remainingWaypoints.Length;
		Vector3[] route = new Vector3[length + 1];
		route[0] = position;
		Array.Copy(remainingWaypoints, 0, route, 1, remainingWaypoints.Length);
		route[length] = destination;
		return route;
	}

	private static float MeasureRouteLength(Vector3[] waypoints, Vector3 start, Vector3 target)
	{
		float length = 0f;
		Vector3 previous = start;
		for (int index = PathStartWaypointCount; index < waypoints.Length; index++)
		{
			length += Vector3.Distance(previous, waypoints[index]);
			previous = waypoints[index];
		}

		if (waypoints.Length > 0) length += Vector3.Distance(previous, target);
		return length;
	}

	private static float MeasureRouteDeviation(Vector3[] predictedRoute, Vector3[] hostRoute)
	{
		float predictedToHost = MaximumDistanceToRoute(predictedRoute, hostRoute);
		float hostToPredicted = MaximumDistanceToRoute(hostRoute, predictedRoute);
		return MathF.Max(predictedToHost, hostToPredicted);
	}

	private static float MaximumDistanceToRoute(Vector3[] points, Vector3[] route)
	{
		if (route.Length < 2) return float.PositiveInfinity;

		float worst = 0f;
		for (int pointIndex = 0; pointIndex < points.Length; pointIndex++)
		{
			float nearest = float.PositiveInfinity;
			for (int segmentIndex = 0; segmentIndex < route.Length - 1; segmentIndex++)
			{
				float distance = DistanceToSegment(points[pointIndex], route[segmentIndex], route[segmentIndex + 1]);
				if (distance < nearest) nearest = distance;
			}

			if (nearest > worst) worst = nearest;
		}

		return worst;
	}

	private static float DistanceToSegment(Vector3 point, Vector3 segmentStart, Vector3 segmentEnd)
	{
		Vector3 segment = segmentEnd - segmentStart;
		float lengthSquared = segment.LengthSquared();
		if (lengthSquared <= float.Epsilon) return Vector3.Distance(point, segmentStart);

		float projection = Vector3.Dot(point - segmentStart, segment) / lengthSquared;
		projection = Math.Clamp(projection, 0f, 1f);
		return Vector3.Distance(point, segmentStart + segment * projection);
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

		if (hostTransitions.ContainsKey(70)) DumpUnitOrderTimeline(records, 70, hostTransitions);

		var latencies = new List<int>();
		var latencyAttribution = new List<string>();
		var unappliedTransitions = new List<string>();
		var unobservedWithoutUpdatesAttribution = new List<string>();
		int unobservedWithoutUpdates = 0;
		var observedServerIds = new HashSet<int>();
		foreach (ClientTickSample observedTick in records.ClientTicks)
		{
			foreach (ClientUnitSample observedUnit in observedTick.Units) observedServerIds.Add(observedUnit.ServerEntityId);
		}
		foreach (var pair in hostTransitions)
		{
			int serverEntityId = pair.Key;
			foreach (var transition in pair.Value)
			{
				if (IsScenarioTarget(records, transition.Target)) continue;

				// The applied-order events name the exact sequence whose delta put the order into the client's ECS, and one is
				// written per applied delta rather than per prediction tick, so they are the primary evidence. A per-tick sample
				// walks the sequence axis at the prediction rate while snapshots arrive at the authority's rate, so an order the
				// authority held for a single sequence can be applied together with its removal inside one client tick, and no
				// tick sample could ever show it.
				int appliedSequence = FirstAppliedOrderSequence(records, serverEntityId, transition.Sequence, transition.Target);
				int firstObservation = appliedSequence > 0
					? appliedSequence
					: FirstTickSampleShowingOrder(records, serverEntityId, transition.Sequence, transition.Target);

				if (firstObservation < 0)
				{
					// An order that the authority replaced sooner than a snapshot can reach the client could not have been observed,
					// so it is not evidence of a dropped order state.
					int transitionIndex = pair.Value.IndexOf(transition);
					int supersededAt = transitionIndex + 1 < pair.Value.Count ? pair.Value[transitionIndex + 1].Sequence : int.MaxValue;
					int minimumVisibleWindow = (int)MathF.Ceiling(settings.OneWayDelayMilliseconds / 1000f * 30f) + 2;
					if (observedServerIds.Contains(serverEntityId) && supersededAt - transition.Sequence >= minimumVisibleWindow)
					{
						int distinctTargetsInWindow = CountClientTargetsInWindow(records, serverEntityId, transition.Sequence, supersededAt);
						if (distinctTargetsInWindow <= 1)
						{
							unobservedWithoutUpdates++;
							unobservedWithoutUpdatesAttribution.Add($"unit {serverEntityId} host sequence {transition.Sequence} target {Describe(transition.Target)}: no order state for the unit reached the client in that window");
						}
						else
						{
							unappliedTransitions.Add($"unit {serverEntityId} host sequence {transition.Sequence}, held for {(supersededAt == int.MaxValue ? "the rest of the run" : $"{supersededAt - transition.Sequence} sequences")}, target {Describe(transition.Target)}");
							TraceUnobservedOrder(records, serverEntityId, transition.Sequence, transition.Target, supersededAt, pair.Value);
						}
					}

					continue;
				}

				latencies.Add(firstObservation - transition.Sequence);
				latencyAttribution.Add(appliedSequence > 0
					? $"unit {serverEntityId} host sequence {transition.Sequence} -> applied at sequence {appliedSequence} target {Describe(transition.Target)}"
					: $"unit {serverEntityId} host sequence {transition.Sequence} -> client sequence {firstObservation} target {Describe(transition.Target)}");
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
		var latencySpread = new System.Text.StringBuilder();
		for (int index = 0; index < latencies.Count; index++)
		{
			if (index > 0) latencySpread.Append(", ");
			latencySpread.Append(latencies[index]);
		}

		report.StageSummaries.Add($"unpredicted order latencies in sequences: {latencySpread}");
		foreach (string attribution in latencyAttribution) report.StageSummaries.Add($"  order arrival: {attribution}");
		report.UnpredictedOrderP95Ticks = latencies[(int)Math.Min(latencies.Count - 1, Math.Floor(latencies.Count * 0.95))];
		report.UnpredictedOrderMaximumTicks = latencies[^1];
		report.StageSummaries.Add($"unpredicted orders: {latencies.Count} samples, p95 {report.UnpredictedOrderP95Ticks} sequences, max {report.UnpredictedOrderMaximumTicks}; each latency is measured from the host sequence that first carried the order to the sequence whose delta put it into the client's ECS, so a clean link is expected to read zero and only the injected one-way delay of {oneWayTicks} ticks should separate the two on a delayed one");

		// The sequence spread above is information, not a verdict: the client records once per local tick, so a delayed
		// link that delivers three snapshots inside one tick hides two sequences from the record without delaying anything
		// the player would perceive. What is a defect is a host-only order the client never observes at all while it was
		// observing the unit before and after, because that is an order state change the snapshot stream dropped.
		if (unobservedWithoutUpdates > 0)
		{
			report.StageSummaries.Add($"{unobservedWithoutUpdates} host-only orders were applied while the peer was not receiving order state for those units, so they were never visible to it; the state oracle owns whether the unit itself was replicated");
			for (int index = 0; index < Math.Min(unobservedWithoutUpdatesAttribution.Count, 5); index++) report.StageSummaries.Add($"  order outside the replicated set: {unobservedWithoutUpdatesAttribution[index]}");
		}

		if (unappliedTransitions.Count > 0)
		{
			for (int index = 0; index < Math.Min(unappliedTransitions.Count, 5); index++) report.StageSummaries.Add($"  order never observed: {unappliedTransitions[index]}");
			report.AddFailure($"{unappliedTransitions.Count} host-only orders were never observed by the client even though it observed the unit on both sides of the change.");
		}
	}

	/// <summary>
	/// A unit outside the viewer's vision is not replicated to that peer at all, so an order applied to it while it is unseen
	/// cannot reach the client. Counting the distinct order targets the client held for the unit inside the window separates
	/// "the snapshot stream dropped an order for a unit that was being updated" from "this peer received no order state for
	/// that unit at the time", which is a visibility question the state oracle owns.
	/// </summary>
	private static void DumpUnitOrderTimeline(ParsedRecords records, int serverEntityId, Dictionary<int, List<(int Sequence, Vector3 Target)>> hostTransitions)
	{
		var trace = new System.Text.StringBuilder();
		trace.AppendLine($"[ORDER_TIMELINE] unit {serverEntityId}");
		if (hostTransitions.TryGetValue(serverEntityId, out List<(int Sequence, Vector3 Target)>? transitions))
		{
			for (int index = 0; index < transitions.Count; index++)
			{
				trace.AppendLine($"[ORDER_TIMELINE]   host {index}: sequence {transitions[index].Sequence} target {Describe(transitions[index].Target)}");
			}
		}

		bool previousHasOrder = false;
		Vector3 previousTarget = default;
		int previousSequence = -1;
		foreach (ClientTickSample tick in records.ClientTicks)
		{
			ClientUnitSample? sample = tick.Units.FirstOrDefault(candidate => candidate.ServerEntityId == serverEntityId);
			if (sample == null) continue;

			bool changed = sample.HasOrder != previousHasOrder
				|| (sample.HasOrder && Vector3.Distance(sample.MoveTarget, previousTarget) > PathTargetToleranceMeters);
			if (!changed) continue;

			trace.AppendLine($"[ORDER_TIMELINE]   client sequence {tick.Sequence} (after {previousSequence}): hasOrder={sample.HasOrder} target {Describe(sample.MoveTarget)}");
			previousHasOrder = sample.HasOrder;
			previousTarget = sample.MoveTarget;
			previousSequence = tick.Sequence;
		}

		Godot.GD.Print(trace.ToString());
	}

	private static void TraceUnobservedOrder(ParsedRecords records, int serverEntityId, int transitionSequence, Vector3 transitionTarget, int supersededAt, List<(int Sequence, Vector3 Target)> transitions)
	{
		var trace = new System.Text.StringBuilder();
		trace.AppendLine($"[ORDER_DIAG] unit {serverEntityId} transition at {transitionSequence} target {Describe(transitionTarget)} supersededAt={supersededAt} transitions={transitions.Count}");
		for (int index = 0; index < transitions.Count; index++)
		{
			trace.AppendLine($"[ORDER_DIAG]   host transition {index}: sequence {transitions[index].Sequence} target {Describe(transitions[index].Target)}");
		}

		int printed = 0;
		foreach (ClientTickSample tick in records.ClientTicks)
		{
			if (tick.Sequence < transitionSequence - 30) continue;
			if (tick.Sequence > (supersededAt == int.MaxValue ? transitionSequence + 240 : supersededAt + 60)) break;
			if (printed >= 60) break;

			ClientUnitSample? sample = tick.Units.FirstOrDefault(candidate => candidate.ServerEntityId == serverEntityId);
			printed++;
			if (sample == null)
			{
				trace.AppendLine($"[ORDER_DIAG]   client sequence {tick.Sequence}: unit not in the client record");
				continue;
			}

			trace.AppendLine($"[ORDER_DIAG]   client sequence {tick.Sequence}: hasOrder={sample.HasOrder} target={Describe(sample.MoveTarget)} position={Describe(sample.Predicted)}");
		}
		trace.AppendLine($"[ORDER_DIAG]   printed {printed} client samples");

		Godot.GD.Print(trace.ToString());
	}

	/// <summary>
	/// Returns the first snapshot sequence at which the client's ECS held the given order target for the unit, or -1 when
	/// the snapshot stream never carried it. Only replicated order state counts: a predicted order is invented locally and
	/// cannot testify that the authority's order arrived.
	/// </summary>
	private static int FirstAppliedOrderSequence(ParsedRecords records, int serverEntityId, int fromSequence, Vector3 target)
	{
		foreach (AppliedOrderSample applied in records.AppliedOrders)
		{
			if (applied.ServerEntityId != serverEntityId) continue;
			if (applied.Sequence < fromSequence) continue;
			if (!applied.HasTarget) continue;
			if (Vector3.Distance(applied.Target, target) > PathTargetToleranceMeters) continue;
			return applied.Sequence;
		}

		return -1;
	}

	private static int FirstTickSampleShowingOrder(ParsedRecords records, int serverEntityId, int fromSequence, Vector3 target)
	{
		foreach (ClientTickSample tick in records.ClientTicks)
		{
			if (tick.Sequence < fromSequence) continue;
			ClientUnitSample? unit = tick.Units.FirstOrDefault(candidate => candidate.ServerEntityId == serverEntityId);
			if (unit == null) continue;
			if (!unit.HasOrder) continue;
			if (Vector3.Distance(unit.MoveTarget, target) > PathTargetToleranceMeters) continue;
			return tick.Sequence;
		}

		return -1;
	}

	private static int CountClientTargetsInWindow(ParsedRecords records, int serverEntityId, int fromSequence, int toSequence)
	{
		var targets = new List<Vector3>(4);
		foreach (ClientTickSample tick in records.ClientTicks)
		{
			if (tick.Sequence < fromSequence) continue;
			if (tick.Sequence > toSequence) break;
			foreach (ClientUnitSample unit in tick.Units)
			{
				if (unit.ServerEntityId != serverEntityId) continue;
				if (!unit.HasOrder) continue;
				if (unit.HasPredictedOrder) continue;
				bool known = false;
				for (int index = 0; index < targets.Count; index++)
				{
					if (Vector3.Distance(targets[index], unit.MoveTarget) <= PathTargetToleranceMeters)
					{
						known = true;
						break;
					}
				}

				if (!known) targets.Add(unit.MoveTarget);
			}
		}

		return targets.Count;
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
				case PredictionDiagnostics.KindAppliedOrder:
					records.AppliedOrders.Add(new AppliedOrderSample
					{
						Sequence = reader.ReadInt32(),
						ServerEntityId = reader.ReadInt32(),
						HasTarget = reader.ReadBoolean(),
						Target = ReadVector(reader)
					});
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
				ObservedPlayerIndex = reader.ReadInt32(),
				HasPredictedOrder = reader.ReadBoolean(),
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

			unit.LocalOwner = unit.OwnerPlayerIndex == unit.ObservedPlayerIndex;

			int recordedWaypoints = reader.ReadInt32();
			Vector3[] plannedWaypoints = new Vector3[recordedWaypoints];
			for (int waypointIndex = 0; waypointIndex < recordedWaypoints; waypointIndex++) plannedWaypoints[waypointIndex] = ReadVector(reader);
			unit.Waypoints = WaypointsFromCurrent(plannedWaypoints, unit.CurrentWaypoint);
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
				Vector3[] plannedWaypoints = new Vector3[recordedWaypoints];
				for (int waypointIndex = 0; waypointIndex < recordedWaypoints; waypointIndex++) plannedWaypoints[waypointIndex] = ReadVector(reader);
				unit.Waypoints = WaypointsFromCurrent(plannedWaypoints, unit.CurrentWaypoint);
				snapshot.Units[unit.ServerEntityId] = unit;
			}

			records.HostSnapshots[snapshot.Sequence] = snapshot;
		}
	}

	/// <summary>
	/// The route a unit still has to walk, which is what the two sides can agree about. A plan keeps the waypoints it has
	/// already reached - the authority's follower advances an index rather than shortening the array - so comparing the raw
	/// array compared the client's remaining route against the host's whole plan and reported a nine-metre corner
	/// difference for a unit that was already past it.
	/// </summary>
	private static Vector3[] WaypointsFromCurrent(Vector3[] plannedWaypoints, int currentWaypoint)
	{
		int start = Math.Clamp(currentWaypoint, 0, plannedWaypoints.Length);
		if (start == 0) return plannedWaypoints;

		Vector3[] remaining = new Vector3[plannedWaypoints.Length - start];
		Array.Copy(plannedWaypoints, start, remaining, 0, remaining.Length);
		return remaining;
	}

	private static Vector3 ReadVector(BinaryReader reader) => new(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());

	private static void ReadIntList(BinaryReader reader)
	{
		int count = reader.ReadInt32();
		for (int index = 0; index < count; index++) reader.ReadInt32();
	}
}
