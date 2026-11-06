using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Realm.Client.Testing;

public sealed record LagScenario(string Name, int AddedRoundTripMilliseconds, int PacketLossPercent)
{
	/// <summary>
	///     The most presentation delay this scenario may settle at. A buffer is only behind the match by the trip it is
	///     paying for, plus the sequence that keeps it fed, so this is the link and not the code: a run above it means the
	///     client has deepened its buffer to hide something, which a human feels as input that arrives late.
	/// </summary>
	public double MaximumPresentationDelayMilliseconds { get; init; } = double.MaxValue;

	/// <summary>
	///     The oldest the drawn world may be on average. It is the measure a player feels as a match that responds late,
	///     and unlike a peak it is not satisfied by the buffer holding still for most of the run.
	/// </summary>
	public double MaximumDrawnWorldAgeMilliseconds { get; init; } = double.MaxValue;

	/// <summary>
	///     How many times this scenario may rebuild the world. A baseline is a whole world rather than the units that
	///     changed, which puts about ten kilobytes on the reliable stream, and it is the fallback a screen pays for when a
	///     run it was waiting on never arrives. It is not a visible snap: a baseline is applied through the same per-unit
	///     description path a delta is, so a unit it describes where the timeline already has it is drawn where it was
	///     already going. The unit jumps this harness watches for are counted separately.
	/// </summary>
	public double MaximumBaselines { get; init; } = double.MaxValue;

	/// <summary>
	///     The longest the drawn world may stand still in one go. This is what a stall looks like on screen: every unit
	///     holding its ground while the clock behind them is frozen. A pause the width of a frame is invisible, a fifth of
	///     a second reads as a hitch, and a second is a freeze a player remembers.
	///     <para>
	///         Read it beside the longest client frame this run drew. A harness that runs twelve players, an authority and
	///         a screen on one machine stalls that machine, and a screen that stopped for a third of a second cannot draw the
	///         descriptions that arrived during it; those stalls reach this figure without the link having done anything.
	///         The ceilings here are set where a person notices rather than where the best run landed, because below this
	///         the measurement is of the machine.
	///     </para>
	/// </summary>
	public double MaximumLongestFreezeMilliseconds { get; init; } = double.MaxValue;

	/// <summary>
	///     How many times this scenario may ask the match for descriptions it is missing. It is the ceiling that keeps
	///     recovery traffic proportional to the loss: a screen that asks per lost description asks about once per loss, and
	///     one that asks again for what it has already asked about asks many times per loss, which is a stream that spends
	///     its bandwidth on its own recovery and loses more for it.
	/// </summary>
	public double MaximumRepairRequests { get; init; } = double.MaxValue;

	public string Describe()
	{
		string delayText = AddedRoundTripMilliseconds > 0 ? $"{AddedRoundTripMilliseconds}ms round trip" : "no added latency";
		string lossText = PacketLossPercent > 0 ? $"{PacketLossPercent}% seeded datagram loss" : "no added loss";
		return $"{delayText}, {lossText}";
	}

	/// <summary>
	///     Names every way the metrics this scenario produced exceed its ceilings. The ceilings are stated per scenario
	///     because the link is, and they are set above the figures measured on a quiet machine rather than at them: a
	///     budget tight enough to fail on the run-to-run spread of the worst case would be a test that fails on its own
	///     noise.
	/// </summary>
	public List<string> FindBudgetViolations(Dictionary<string, double> metrics)
	{
		List<string> violations = new();
		CheckCeiling(metrics, violations, "client.gauges.ClientPresentationDelayMilliseconds.p50", "presentation delay p50", MaximumPresentationDelayMilliseconds, "ms");
		CheckCeiling(metrics, violations, "client.gauges.ClientDrawnWorldAgeMilliseconds.mean", "drawn world age mean", MaximumDrawnWorldAgeMilliseconds, "ms");
		CheckCeiling(metrics, violations, "client.events.ClientBaselinesApplied.count", "baselines applied", MaximumBaselines, "");
		CheckCeiling(metrics, violations, "client.events.ClientStarvationHoldMilliseconds.worst", "longest freeze", MaximumLongestFreezeMilliseconds, "ms");
		CheckCeiling(metrics, violations, "client.events.ClientRepairsRequested.count", "repair requests", MaximumRepairRequests, "");
		return violations;
	}

	private void CheckCeiling(Dictionary<string, double> metrics, List<string> violations, string key, string label, double ceiling, string unit)
	{
		if (!metrics.TryGetValue(key, out double measured)) return;
		if (measured <= ceiling) return;
		violations.Add($"{Name}: {label} was {measured:F1}{unit}, above its ceiling of {ceiling:F1}{unit}.");
	}
}

public static class LagMetricCatalog
{
	public readonly record struct Entry(string Key, string Label, string Unit);

	public static readonly Entry[] Entries =
	{
		new("authority.stages.AuthorityTickIntervalMilliseconds.p99", "authority tick interval p99", "ms"),
		new("authority.stages.AuthorityTickIntervalMilliseconds.max", "authority tick interval max", "ms"),
		new("authority.stages.AuthorityTickWorkMilliseconds.p99", "authority tick work p99", "ms"),
		new("authority.stages.AuthoritySimulateMilliseconds.p99", "authority simulate p99", "ms"),
		new("authority.stages.AuthorityDescribeMilliseconds.p99", "authority describe p99", "ms"),
		new("authority.stages.AuthoritySerializeMilliseconds.p99", "authority serialize p99", "ms"),
		new("authority.stages.AuthoritySendMilliseconds.p99", "authority send p99", "ms"),
		new("authority.stages.AuthoritySendMilliseconds.max", "authority send worst", "ms"),
		new("authority.stages.AuthorityTickIntervalMilliseconds.max", "authority step interval worst", "ms"),
		new("authority.stages.AuthorityTickWorkMilliseconds.max", "authority tick work worst", "ms"),
		new("authority.stages.AuthorityPublishMilliseconds.max", "authority publish worst", "ms"),
		new("authority.stages.AuthorityPresentationMilliseconds.p99", "authority presentation p99", "ms"),
		new("authority.stages.AuthorityPresentationMilliseconds.max", "authority presentation worst", "ms"),
		new("authority.stages.AuthorityPrepareMilliseconds.p99", "authority prepare p99", "ms"),
		new("authority.stages.AuthorityPrepareMilliseconds.max", "authority prepare worst", "ms"),
		new("authority.stages.AuthorityDescribeMilliseconds.max", "authority describe worst", "ms"),
		new("authority.stages.AuthoritySerializeMilliseconds.max", "authority serialize worst", "ms"),
		new("authority.stages.AuthorityRecordMilliseconds.p99", "authority record p99", "ms"),
		new("authority.stages.AuthorityRecordMilliseconds.max", "authority record worst", "ms"),
		new("authority.stages.AuthorityRecordMilliseconds.count", "authority record samples", "count"),
		new("authority.stages.AuthorityGcPausePerTickMilliseconds.p99", "collector pause in one authority step p99", "ms"),
		new("authority.stages.AuthorityGcPausePerTickMilliseconds.max", "worst collector pause in one authority step", "ms"),
		new("authority.stages.AuthorityAllocatedKilobytesPerTick.p50", "allocated per authority step p50", "KB"),
		new("authority.stages.AuthorityAllocatedKilobytesPerTick.p99", "allocated per authority step p99", "KB"),
		new("authority.stages.AuthorityAllocatedKilobytesPerTick.max", "allocated in one authority step", "KB"),
		new("authority.events.AuthorityTickCatchUpCalls.count", "authority tick catch-up calls", "count"),
		new("authority.gauges.AuthoritySequencesPerSecond.mean", "authority sequences per second", "/s"),
		new("authority.events.AuthorityBaselinesPublished.count", "authority baselines published", "count"),
		new("authority.events.AuthorityDeltaPublished.count", "authority deltas published", "count"),
		new("authority.events.AuthorityBaselinesBecausePeerAsked.count", "authority baselines because peer asked", "count"),
		new("authority.events.AuthorityBaselinesBecausePeerBehind.count", "authority baselines because peer behind", "count"),
		new("authority.events.AuthorityBaselinesBecauseAcknowledgementMissed.count", "authority baselines because ack missed", "count"),
		new("authority.events.AuthorityBaselinesBecauseDescriptionTooWide.count", "authority baselines because too wide", "count"),
		new("authority.events.AuthorityRepairDescriptionsSent.count", "authority repair descriptions sent", "count"),
		new("authority.events.AuthorityFragmentedDescriptions.count", "authority fragmented descriptions", "count"),
		new("client.stages.ClientPhysicsIntervalMilliseconds.p50", "client physics interval p50", "ms"),
		new("client.stages.ClientPhysicsIntervalMilliseconds.p99", "client physics interval p99", "ms"),
		new("client.stages.ClientPhysicsIntervalMilliseconds.max", "client physics interval max", "ms"),
		new("client.stages.ClientPhysicsTickMilliseconds.p99", "client physics tick p99", "ms"),
		new("client.stages.ClientDescriptionIntervalMilliseconds.p50", "description arrival interval p50", "ms"),
		new("client.stages.ClientDescriptionIntervalMilliseconds.p99", "description arrival interval p99", "ms"),
		new("client.stages.ClientDescriptionIntervalMilliseconds.max", "description arrival interval max", "ms"),
		new("client.stages.ClientNetworkPumpMilliseconds.p99", "client network pump p99", "ms"),
		new("client.stages.ClientNetworkPumpMilliseconds.max", "client network pump max", "ms"),
		new("client.stages.ClientDeserializeMilliseconds.p99", "client deserialize p99", "ms"),
		new("client.stages.ClientApplyMilliseconds.p99", "client apply p99", "ms"),
		new("client.stages.ClientRenderIntervalMilliseconds.p50", "client frame interval p50", "ms"),
		new("client.stages.ClientRenderIntervalMilliseconds.p99", "client frame interval p99", "ms"),
		new("client.stages.ClientGcPausePerFrameMilliseconds.p99", "collector pause in one frame p99", "ms"),
		new("client.stages.ClientGcPausePerFrameMilliseconds.max", "worst collector pause in one frame", "ms"),
		new("client.stages.ClientAllocatedKilobytesPerFrame.p50", "allocated per frame p50", "KB"),
		new("client.stages.ClientAllocatedKilobytesPerFrame.p99", "allocated per frame p99", "KB"),
		new("client.stages.ClientPlacementMilliseconds.p99", "client placement p99", "ms"),
		new("client.gauges.ClientFramesPerSecond.mean", "client frames per second", "fps"),
		new("client.gauges.ClientFramesPerSecond.min", "client frames per second min", "fps"),
		new("client.gauges.ClientManagedHeapMegabytes.max", "managed heap max", "MB"),
		new("client.gauges.ClientGcPauseMilliseconds.max", "collector pause total", "ms"),
		new("client.gauges.ClientGen0Collections.max", "gen0 collections", "count"),
		new("client.gauges.ClientGen2Collections.max", "gen2 collections", "count"),
		new("client.gauges.ClientPresentationDelaySequences.mean", "presentation delay mean", "seq"),
		new("client.gauges.ClientPresentationDelaySequences.p95", "presentation delay p95", "seq"),
		new("client.gauges.ClientPresentationDelaySequences.max", "presentation delay max", "seq"),
		new("client.gauges.ClientPresentationDelayMilliseconds.mean", "presentation delay mean", "ms"),
		new("client.gauges.ClientDrawnWorldAgeMilliseconds.mean", "age of the drawn world mean", "ms"),
		new("client.gauges.ClientDrawnWorldAgeMilliseconds.p95", "age of the drawn world p95", "ms"),
		new("client.gauges.ClientNewestDescriptionLeadSequences.mean", "newest description lead mean", "seq"),
		new("client.gauges.ClientCatchUpRatePercent.mean", "clock speed correction mean", "%"),
		new("client.gauges.ClientCatchUpRatePercent.max", "fastest the clock ran", "%"),
		new("client.events.ClientBaselinesApplied.count", "client baselines applied", "count"),
		new("client.events.ClientDeltasApplied.count", "client deltas applied", "count"),
		new("client.events.ClientBaselinesRequested.count", "client baselines requested", "count"),
		new("client.events.ClientBaselinesBecauseNoWorld.count", "baselines asked: no world yet", "count"),
		new("client.events.ClientBaselinesBecauseNoInitialBaseline.count", "baselines asked: no initial baseline", "count"),
		new("client.events.ClientBaselinesBecauseDeferredRetry.count", "baselines asked: deferred retry", "count"),
		new("client.events.ClientBaselinesBecauseBacklogTooDeep.count", "baselines asked: backlog too deep", "count"),
		new("client.events.ClientBaselinesBecauseQueuedRunOverflow.count", "baselines asked: queued run overflow", "count"),
		new("client.events.ClientBaselinesBecauseBaseAhead.count", "baselines asked: delta based ahead", "count"),
		new("client.events.ClientBaselinesBecauseRepairRunTooWide.count", "baselines asked: repair run too wide", "count"),
		new("client.events.ClientBaselinesBecauseRepairGraceExpired.count", "baselines asked: repair grace expired", "count"),
		new("client.events.ClientBaselinesBecauseSilence.count", "baselines asked: stream went silent", "count"),
		new("client.events.ClientBaselinesBecauseUnplaceableChange.count", "baselines asked: change unplaceable", "count"),
		new("client.events.ClientBaselinesBecauseSessionAsked.count", "baselines asked: session asked", "count"),
		new("client.events.ClientRepairsRequested.count", "client repairs requested", "count"),
		new("client.events.ClientDescriptionsArrived.count", "descriptions that reached this screen", "count"),
		new("client.events.ClientDescriptionsBehindApplied.count", "descriptions that arrived already superseded", "count"),
		new("client.events.ClientQueuedDescriptionsCleared.count", "held descriptions thrown away", "count"),
		new("client.events.ClientSequenceGaps.count", "description gaps seen", "count"),
		new("client.events.ClientWorstSkippedSequences.worst", "widest description gap", "seq"),
		new("client.events.ClientDescriptionRepeatsRequested.count", "client description repeats requested", "count"),
		new("client.events.ClientStarvations.count", "presentation starvations", "count"),
		new("client.events.ClientStarvationHoldMilliseconds.count", "stalls the screen recovered from", "count"),
		new("client.events.ClientStarvationHoldMilliseconds.worst", "longest stall held still", "ms"),
		new("client.events.ClientCatchUpDrainMilliseconds.count", "stalls drained through", "count"),
		new("client.events.ClientCatchUpDrainMilliseconds.worst", "longest stall drained through", "ms"),
		new("client.events.ClientExtrapolatedSamples.count", "samples drawn ahead of the cursor", "count"),
		new("client.events.ClientDescriptionsDiscardedByQueue.count", "descriptions discarded by queue", "count"),
		new("client.events.ClientDescriptionsSuperseded.count", "descriptions superseded while waiting", "count"),
		new("client.events.ClientDeltasHeldForRepair.count", "deltas held for repair", "count"),
		new("client.events.ClientRepairRunsAbandoned.count", "repair runs abandoned", "count"),
		new("client.events.ClientDeltasAppliedFromBacklog.count", "deltas applied from backlog", "count"),
		new("client.events.ClientPresentedJumps.count", "units drawn with a visible jump", "count"),
		new("client.events.ClientPresentedJumps.worst", "worst visible jump", "m"),
		new("client.events.ClientReleaseBackwardsSamples.count", "samples eased backwards", "count"),
		new("client.events.ClientReleaseBackwardsSamples.worst", "worst distance eased backwards", "m"),
		new("client.events.ClientTimelineResets.count", "units forced back onto the authority", "count"),
		new("client.events.ClientTimelineResets.worst", "worst forced snap", "m"),
		new("client.events.ClientRenderFramesOverBudget.count", "frames over 33.4 ms", "count"),
		new("client.events.ClientPhysicsTicksOverBudget.count", "physics ticks over 33.4 ms", "count"),
		new("client.events.LinkFramesDelivered.count", "link frames delivered", "count"),
		new("client.events.LinkFramesDropped.count", "link frames dropped", "count")
	};
}

public static class LagPerceptionReport
{
	private static readonly JsonDocumentOptions ParsingOptions = new() { AllowTrailingCommas = true };

	public static void WritePassReport(string rootDirectory, int pass, IReadOnlyList<LagScenario> scenarios)
	{
		StringBuilder report = new();
		report.AppendLine($"=== Lag perception, pass {pass} ===");
		report.AppendLine($"root: {rootDirectory}");
		foreach (LagScenario scenario in scenarios)
		{
			report.AppendLine();
			report.AppendLine($"-- {scenario.Name}: {scenario.Describe()} --");
			string directory = ScenarioDirectory(rootDirectory, pass, scenario);
			AppendScreenChecks(report, directory);
			Dictionary<string, double> metrics = ReadScenarioMetrics(directory);
			if (metrics.Count == 0)
			{
				report.AppendLine("  no lag metrics were written for this scenario");
				continue;
			}

			AppendTable(report, metrics);
		}

		File.WriteAllText(Path.Combine(rootDirectory, $"lag_report_pass{pass}.txt"), report.ToString());
		System.Console.WriteLine(report.ToString());
	}

	public static void WriteComparisonReport(string rootDirectory, int passCount, IReadOnlyList<LagScenario> scenarios)
	{
		StringBuilder comparison = new();
		comparison.AppendLine("=== Lag perception, pass to pass comparison ===");
		comparison.AppendLine($"passes: {passCount}");
		comparison.AppendLine();
		comparison.AppendLine("metric\tscenario\tpass1\tpass2\tabsolute delta\trelative delta");

		int comparedValues = 0;
		int driftedValues = 0;
		foreach (LagScenario scenario in scenarios)
		{
			Dictionary<string, double> first = ReadScenarioMetrics(ScenarioDirectory(rootDirectory, 1, scenario));
			if (passCount < 2) continue;

			Dictionary<string, double> second = ReadScenarioMetrics(ScenarioDirectory(rootDirectory, 2, scenario));
			foreach (LagMetricCatalog.Entry entry in LagMetricCatalog.Entries)
			{
				if (!first.TryGetValue(entry.Key, out double before) || !second.TryGetValue(entry.Key, out double after)) continue;

				double delta = after - before;
				double relative = Math.Abs(before) < 0.0001d ? (Math.Abs(delta) < 0.0001d ? 0d : double.PositiveInfinity) : delta / before;
				comparedValues++;
				if (Math.Abs(relative) > 0.35d) driftedValues++;

				comparison.AppendLine(string.Format(
					CultureInfo.InvariantCulture,
					"{0}\t{1}\t{2:F4}\t{3:F4}\t{4:+0.0000;-0.0000;0.0000}\t{5}",
					entry.Label,
					scenario.Name,
					before,
					after,
					delta,
					double.IsInfinity(relative) ? "n/a" : relative.ToString("+0.0%;-0.0%;0.0%", CultureInfo.InvariantCulture)));
			}
		}

		comparison.AppendLine();
		comparison.AppendLine(string.Format(
			CultureInfo.InvariantCulture,
			"{0} of {1} compared values moved by more than 35% between the two passes.",
			driftedValues,
			comparedValues));
		File.WriteAllText(Path.Combine(rootDirectory, "lag_comparison.txt"), comparison.ToString());
		System.Console.WriteLine(comparison.ToString());

		WriteSummaryJson(rootDirectory, passCount, scenarios);
	}

	private static void WriteSummaryJson(string rootDirectory, int passCount, IReadOnlyList<LagScenario> scenarios)
	{
		StringBuilder json = new();
		json.Append("{\"passes\":").Append(passCount).Append(",\"scenarios\":{");
		for (int scenarioIndex = 0; scenarioIndex < scenarios.Count; scenarioIndex++)
		{
			LagScenario scenario = scenarios[scenarioIndex];
			if (scenarioIndex > 0) json.Append(',');
			json.Append('"').Append(scenario.Name).Append("\":{");
			json.Append("\"roundTripMilliseconds\":").Append(scenario.AddedRoundTripMilliseconds);
			json.Append(",\"packetLossPercent\":").Append(scenario.PacketLossPercent);
			json.Append(",\"passes\":[");
			for (int pass = 1; pass <= passCount; pass++)
			{
				if (pass > 1) json.Append(',');
				json.Append('{');
				Dictionary<string, double> metrics = ReadScenarioMetrics(ScenarioDirectory(rootDirectory, pass, scenario));
				bool firstMetric = true;
				foreach (LagMetricCatalog.Entry entry in LagMetricCatalog.Entries)
				{
					if (!metrics.TryGetValue(entry.Key, out double value)) continue;
					if (!firstMetric) json.Append(',');
					firstMetric = false;
					json.Append('"').Append(entry.Key).Append("\":").Append(value.ToString("F4", CultureInfo.InvariantCulture));
				}

				json.Append('}');
			}

			json.Append("]}");
		}

		json.Append("}}");
		File.WriteAllText(Path.Combine(rootDirectory, "lag_summary.json"), json.ToString());
	}

	private static void AppendScreenChecks(StringBuilder report, string directory)
	{
		string presentationGaps = Path.Combine(directory, "ECS_client_presentation_gaps.txt");
		if (File.Exists(presentationGaps))
		{
			foreach (string line in File.ReadLines(presentationGaps))
			{
				if (!line.StartsWith("count=", StringComparison.Ordinal) && !line.StartsWith("tolerance_meters=", StringComparison.Ordinal) && !line.StartsWith("delay_sequences=", StringComparison.Ordinal)) continue;
				report.AppendLine($"  {line}");
			}
		}

		string replication = Path.Combine(directory, "ECS_client_replication.txt");
		if (File.Exists(replication))
		{
			foreach (string line in File.ReadLines(replication))
			{
				if (!line.StartsWith("buffer_starvations=", StringComparison.Ordinal) && !line.StartsWith("shadow_structural_failures=", StringComparison.Ordinal) && !line.StartsWith("fingerprint_mismatches=", StringComparison.Ordinal)) continue;
				report.AppendLine($"  {line}");
			}
		}

		string syncReport = Path.Combine(directory, "multiplayer_ecs_sync_report.txt");
		if (!File.Exists(syncReport)) return;

		foreach (string line in File.ReadLines(syncReport))
		{
			if (!line.StartsWith("Failures:", StringComparison.Ordinal)) continue;
			report.AppendLine($"  sync verification {line}");
		}
	}

	private static void AppendTable(StringBuilder report, Dictionary<string, double> metrics)
	{
		report.AppendLine("  metric\tvalue\tunit");
		foreach (LagMetricCatalog.Entry entry in LagMetricCatalog.Entries)
		{
			if (!metrics.TryGetValue(entry.Key, out double value)) continue;
			report.AppendLine($"  {entry.Label}\t{value.ToString("F3", CultureInfo.InvariantCulture)}\t{entry.Unit}");
		}
	}

	public static string ScenarioDirectory(string rootDirectory, int pass, LagScenario scenario)
	{
		return Path.Combine(rootDirectory, $"pass{pass}", scenario.Name);
	}

	public static Dictionary<string, double> ReadScenarioMetrics(string scenarioDirectory)
	{
		Dictionary<string, double> metrics = new(StringComparer.Ordinal);
		ReadProbeFile(Path.Combine(scenarioDirectory, "lag_authority.json"), "authority", metrics);
		ReadProbeFile(Path.Combine(scenarioDirectory, "lag_client.json"), "client", metrics);
		ReadRelayStats(Path.Combine(scenarioDirectory, "simulated_link.txt"), metrics);
		return metrics;
	}

	private static void ReadProbeFile(string path, string scope, Dictionary<string, double> metrics)
	{
		if (!File.Exists(path)) return;

		try
		{
			using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path), ParsingOptions);
			JsonElement root = document.RootElement;
			ReadSections(root, "stages", scope + ".stages", metrics);
			ReadSections(root, "events", scope + ".events", metrics);
			ReadSections(root, "gauges", scope + ".gauges", metrics);
		}
		catch (JsonException)
		{
		}
		catch (IOException)
		{
		}
	}

	private static void ReadSections(JsonElement root, string sectionName, string keyPrefix, Dictionary<string, double> metrics)
	{
		if (!root.TryGetProperty(sectionName, out JsonElement section)) return;

		foreach (JsonProperty entry in section.EnumerateObject())
		{
			foreach (JsonProperty value in entry.Value.EnumerateObject())
			{
				if (value.Value.ValueKind != JsonValueKind.Number) continue;
				metrics[$"{keyPrefix}.{entry.Name}.{value.Name}"] = value.Value.GetDouble();
			}
		}
	}

	private static void ReadRelayStats(string path, Dictionary<string, double> metrics)
	{
		if (!File.Exists(path)) return;

		foreach (string line in File.ReadLines(path))
		{
			int separator = line.IndexOf('=');
			if (separator <= 0) continue;
			if (!double.TryParse(line.AsSpan(separator + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out double value)) continue;
			metrics[$"relay.{line[..separator]}"] = value;
		}
	}
}
