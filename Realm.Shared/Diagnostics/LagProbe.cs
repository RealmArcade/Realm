using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace Realm.Shared.Diagnostics;

public enum LagStage
{
	AuthorityTickIntervalMilliseconds,
	AuthorityTickWorkMilliseconds,
	AuthoritySimulateMilliseconds,
	AuthorityPublishMilliseconds,
	AuthorityDescribeMilliseconds,
	AuthorityPackMilliseconds,
	AuthorityCompressMilliseconds,
	AuthoritySerializeMilliseconds,
	AuthoritySendMilliseconds,
	AuthorityGcPausePerTickMilliseconds,
	AuthorityAllocatedKilobytesPerTick,
	AuthorityPresentationMilliseconds,
	AuthorityPrepareMilliseconds,
	AuthorityRecordMilliseconds,
	ClientPhysicsIntervalMilliseconds,
	ClientPhysicsTickMilliseconds,
	ClientNetworkPumpMilliseconds,
	ClientDeserializeMilliseconds,
	ClientApplyMilliseconds,
	ClientRenderIntervalMilliseconds,
	ClientGcPausePerFrameMilliseconds,
	ClientAllocatedKilobytesPerFrame,
	ClientDescriptionIntervalMilliseconds,
	ClientPlacementMilliseconds
}

public enum LagEvent
{
	AuthorityTicks,
	AuthorityTickCatchUpCalls,
	AuthorityBaselinesPublished,
	AuthorityDeltaPublished,
	AuthorityBaselinesBecausePeerBehind,
	AuthorityBaselinesBecausePeerAsked,
	AuthorityBaselinesBecauseAcknowledgementMissed,
	AuthorityBaselinesBecausePeerWasUndescribed,
	AuthorityBaselinesBecauseDescriptionTooWide,
	AuthorityPeerToldNothing,
	AuthorityRepairDescriptionsSent,
	AuthorityDuplicatedDescriptions,
	AuthorityFragmentedDescriptions,
	ClientBaselinesApplied,
	ClientDeltasApplied,
	ClientBaselinesRequested,
	ClientBaselinesBecauseNoWorld,
	ClientBaselinesBecauseNoInitialBaseline,
	ClientBaselinesBecauseDeferredRetry,
	ClientBaselinesBecauseBacklogTooDeep,
	ClientBaselinesBecauseQueuedRunOverflow,
	ClientBaselinesBecauseBaseAhead,
	ClientBaselinesBecauseRepairRunTooWide,
	ClientBaselinesBecauseRepairGraceExpired,
	ClientBaselinesBecauseSilence,
	ClientBaselinesBecauseUnplaceableChange,
	ClientBaselinesBecauseSessionAsked,
	ClientDescriptionRepeatsRequested,
	ClientRepairsRequested,
	ClientDescriptionsArrived,
	ClientDescriptionsBehindApplied,
	ClientQueuedDescriptionsCleared,
	ClientSequenceGaps,
	ClientWorstSkippedSequences,
	ClientStarvations,
	ClientStarvationHoldMilliseconds,
	ClientCatchUpDrainMilliseconds,
	ClientStarvationDepthSequences,
	ClientStarvationSilenceMilliseconds,
	ClientStarvationStepMilliseconds,
	ClientStarvationHeldSequences,
	ClientExtrapolatedSamples,
	ClientDescriptionsDiscardedByQueue,
	ClientDescriptionsSuperseded,
	ClientDeltasHeldForRepair,
	ClientRepairRunsAbandoned,
	ClientDeltasAppliedFromBacklog,
	ClientReleaseBackwardsSamples,
	ClientPresentedJumps,
	ClientTimelineResets,
	ClientRepeatedRequestsWithNoAnswer,
	ClientRenderFramesOverBudget,
	ClientPhysicsTicksOverBudget,
	LinkFramesDelivered,
	LinkFramesDropped
}

public enum LagGauge
{
	ClientPresentationDelaySequences,
	ClientPresentationDelayMilliseconds,
	ClientDrawnWorldAgeMilliseconds,
	ClientCatchUpRatePercent,
	ClientNewestDescriptionLeadSequences,
	ClientFramesPerSecond,
	ClientManagedHeapMegabytes,
	ClientGcPauseMilliseconds,
	ClientGen0Collections,
	ClientGen2Collections,
	AuthoritySequencesPerSecond
}

public static class LagProbe
{
	public const string OutputDirectoryVariable = "REALM_LAG_PROBE_DIR";

	private const int SampleCapacity = 1 << 15;
	private const int GaugeCapacity = 1 << 12;
	private const double GaugeSampleSeconds = 0.25;

	private static readonly int _stageCount = Enum.GetValues<LagStage>().Length;
	private static readonly int _eventCount = Enum.GetValues<LagEvent>().Length;
	private static readonly int _gaugeCount = Enum.GetValues<LagGauge>().Length;
	private static readonly double[] _stageSamples = new double[Enum.GetValues<LagStage>().Length * SampleCapacity];
	private static readonly int[] _stageCounts = new int[Enum.GetValues<LagStage>().Length];
	private static readonly long[] _eventCounts = new long[Enum.GetValues<LagEvent>().Length];
	private static readonly double[] _eventWorst = new double[Enum.GetValues<LagEvent>().Length];
	private static readonly float[] _gaugeSamples = new float[Enum.GetValues<LagGauge>().Length * GaugeCapacity];
	private static readonly int[] _gaugeCounts = new int[Enum.GetValues<LagGauge>().Length];
	private static readonly double[] _gaugeLastSeconds = new double[Enum.GetValues<LagGauge>().Length];
	private static readonly double[] _gaugeStartedSeconds = new double[Enum.GetValues<LagGauge>().Length];

	private const int ArrivalCapacity = 4096;

	private static readonly int[] _arrivalSequences = CreateArrivalSequences();
	private static readonly double[] _arrivalSeconds = new double[ArrivalCapacity];

	private static readonly int[] _startupStageCounts = new int[Enum.GetValues<LagStage>().Length];
	private static readonly int[] _startupGaugeCounts = new int[Enum.GetValues<LagGauge>().Length];
	private static readonly long[] _startupEventCounts = new long[Enum.GetValues<LagEvent>().Length];
	private static readonly double[] _startupEventWorst = new double[Enum.GetValues<LagEvent>().Length];
	private static readonly double[] _steadyEventWorst = new double[Enum.GetValues<LagEvent>().Length];

	private static int _startupMarked;

	private static int _enabled;
	private static string _outputDirectory = string.Empty;
	private static string _role = string.Empty;

	public static bool IsEnabled => Volatile.Read(ref _enabled) != 0;

	public static string Role => _role;

	public static void TryEnableFromEnvironment(string role)
	{
		string? directory = Environment.GetEnvironmentVariable(OutputDirectoryVariable);
		if (string.IsNullOrWhiteSpace(directory)) return;

		Enable(directory, role);
	}

	public static void Enable(string outputDirectory, string role)
	{
		if (IsEnabled) return;

		_outputDirectory = outputDirectory;
		_role = role;
		Array.Clear(_stageCounts);
		Array.Clear(_eventCounts);
		Array.Clear(_eventWorst);
		Array.Clear(_gaugeCounts);
		Array.Clear(_gaugeLastSeconds);
		Array.Clear(_gaugeStartedSeconds);
		Array.Clear(_startupStageCounts);
		Array.Clear(_startupGaugeCounts);
		Array.Clear(_startupEventCounts);
		Array.Clear(_startupEventWorst);
		Array.Clear(_steadyEventWorst);
		Volatile.Write(ref _startupMarked, 0);
		Volatile.Write(ref _enabled, 1);
	}

	public static void Disable()
	{
		Volatile.Write(ref _enabled, 0);
	}

	private static int[] CreateArrivalSequences()
	{
		int[] sequences = new int[ArrivalCapacity];
		Array.Fill(sequences, -1);
		return sequences;
	}

	public static double NowSeconds() => System.Diagnostics.Stopwatch.GetTimestamp() / (double)System.Diagnostics.Stopwatch.Frequency;

	public static void NoteDescriptionArrival(int sequence, double seconds)
	{
		if (!IsEnabled || sequence < 0) return;

		int slot = sequence % ArrivalCapacity;
		_arrivalSequences[slot] = sequence;
		_arrivalSeconds[slot] = seconds;
	}

	public static void ObserveDrawnSequence(int sequence, double seconds)
	{
		if (!IsEnabled || sequence < 0) return;

		int slot = sequence % ArrivalCapacity;
		if (_arrivalSequences[slot] != sequence) return;

		ObserveGauge(LagGauge.ClientDrawnWorldAgeMilliseconds, (seconds - _arrivalSeconds[slot]) * 1000d, seconds);
	}

	public static long Timestamp() => System.Diagnostics.Stopwatch.GetTimestamp();

	public static double ElapsedMilliseconds(long startedTimestamp)
	{
		return (System.Diagnostics.Stopwatch.GetTimestamp() - startedTimestamp) * 1000d / System.Diagnostics.Stopwatch.Frequency;
	}

	public static void Add(LagStage stage, double milliseconds)
	{
		if (!IsEnabled) return;
		if (!(milliseconds >= 0d)) return;

		int stageIndex = (int)stage;
		int slot = Interlocked.Increment(ref _stageCounts[stageIndex]) - 1;
		if (slot >= SampleCapacity) return;

		_stageSamples[stageIndex * SampleCapacity + slot] = milliseconds;
	}

	public static void SetCount(LagEvent lagEvent, long amount)
	{
		if (!IsEnabled) return;

		Interlocked.Exchange(ref _eventCounts[(int)lagEvent], amount);
	}

	public static void Count(LagEvent lagEvent, int amount = 1)
	{
		if (!IsEnabled) return;

		Interlocked.Add(ref _eventCounts[(int)lagEvent], amount);
	}

	public static void RecordWorst(LagEvent lagEvent, double magnitude)
	{
		if (!IsEnabled) return;

		int index = (int)lagEvent;
		Interlocked.Increment(ref _eventCounts[index]);

		double current = Volatile.Read(ref _eventWorst[index]);
		while (magnitude > current)
		{
			double observed = Interlocked.CompareExchange(ref _eventWorst[index], magnitude, current);
			if (observed == current) break;
			current = observed;
		}

		// A worst is not a counter, so the steady window keeps its own: the mark states what had already happened, and a
		// peak from the startup window must not be reported as one the session went on to pay.
		if (Volatile.Read(ref _startupMarked) == 0 || !(magnitude > _steadyEventWorst[index])) return;

		_steadyEventWorst[index] = magnitude;
	}

	public static void ObserveGauge(LagGauge gauge, double value, double clockSeconds)
	{
		if (!IsEnabled) return;
		if (double.IsNaN(value) || double.IsInfinity(value)) return;

		int gaugeIndex = (int)gauge;
		if (_gaugeStartedSeconds[gaugeIndex] <= 0d) _gaugeStartedSeconds[gaugeIndex] = clockSeconds;
		if (clockSeconds - _gaugeLastSeconds[gaugeIndex] < GaugeSampleSeconds) return;

		_gaugeLastSeconds[gaugeIndex] = clockSeconds;
		int slot = Interlocked.Increment(ref _gaugeCounts[gaugeIndex]) - 1;
		if (slot >= GaugeCapacity) return;

		_gaugeSamples[gaugeIndex * GaugeCapacity + slot] = (float)value;
	}

	public static long EventCount(LagEvent lagEvent) => Interlocked.Read(ref _eventCounts[(int)lagEvent]);

	public static double EventWorst(LagEvent lagEvent) => Volatile.Read(ref _eventWorst[(int)lagEvent]);

	public static double StageMeanMilliseconds(LagStage stage)
	{
		int index = (int)stage;
		int count = Math.Min(Volatile.Read(ref _stageCounts[index]), SampleCapacity);
		if (count <= 0) return 0d;

		double total = 0d;
		for (int slot = 0; slot < count; slot++) total += _stageSamples[index * SampleCapacity + slot];
		return total / count;
	}

	/// <summary>
	///     Draws a line under a match's first seconds, once. Everything sampled up to this call is kept and reported as
	///     the startup window, and the report from here on is the steady window, so a join cost is never read as a
	///     cadence the session keeps while the cost itself stays visible.
	/// </summary>
	public static void MarkStartupComplete()
	{
		if (!IsEnabled) return;
		if (Interlocked.Exchange(ref _startupMarked, 1) != 0) return;

		for (int index = 0; index < _startupStageCounts.Length; index++)
		{
			_startupStageCounts[index] = Math.Min(Volatile.Read(ref _stageCounts[index]), SampleCapacity);
		}

		for (int index = 0; index < _startupGaugeCounts.Length; index++)
		{
			_startupGaugeCounts[index] = Math.Min(Volatile.Read(ref _gaugeCounts[index]), GaugeCapacity);
		}

		for (int index = 0; index < _startupEventCounts.Length; index++)
		{
			_startupEventCounts[index] = Interlocked.Read(ref _eventCounts[index]);
			_startupEventWorst[index] = Volatile.Read(ref _eventWorst[index]);
		}
	}

	private static (int Start, int End) StageWindow(int stageIndex, bool startup)
	{
		int count = Math.Min(Volatile.Read(ref _stageCounts[stageIndex]), SampleCapacity);
		int mark = Math.Min(_startupStageCounts[stageIndex], count);
		return startup ? (0, mark) : (mark, count);
	}

	private static (int Start, int End) GaugeWindow(int gaugeIndex, bool startup)
	{
		int count = Math.Min(Volatile.Read(ref _gaugeCounts[gaugeIndex]), GaugeCapacity);
		int mark = Math.Min(_startupGaugeCounts[gaugeIndex], count);
		return startup ? (0, mark) : (mark, count);
	}

	private static SampleStatistics DescribeStage(int stageIndex, bool startup)
	{
		(int start, int end) = StageWindow(stageIndex, startup);
		int count = end - start;
		if (count <= 0) return default;

		double[] sorted = new double[count];
		Array.Copy(_stageSamples, stageIndex * SampleCapacity + start, sorted, 0, count);
		double total = 0d;
		for (int slot = 0; slot < count; slot++) total += sorted[slot];
		double mean = total / count;
		Array.Sort(sorted);

		return new SampleStatistics(count, mean, sorted[0], sorted[count - 1], Percentile(sorted, 0.50), Percentile(sorted, 0.95), Percentile(sorted, 0.99), 0);
	}

	private static SampleStatistics DescribeGauge(int gaugeIndex, bool startup)
	{
		(int start, int end) = GaugeWindow(gaugeIndex, startup);
		int count = end - start;
		if (count <= 0) return default;

		float[] samples = new float[count];
		Array.Copy(_gaugeSamples, gaugeIndex * GaugeCapacity + start, samples, 0, count);

		float mean = 0f;
		float minimum = float.MaxValue;
		float maximum = float.MinValue;
		int changes = 0;
		float previous = samples[0];
		for (int slot = 0; slot < count; slot++)
		{
			float value = samples[slot];
			mean += value;
			if (value < minimum) minimum = value;
			if (value > maximum) maximum = value;
			if (MathF.Abs(value - previous) >= 0.5f) changes++;
			previous = value;
		}

		mean /= count;
		Array.Sort(samples);

		return new SampleStatistics(count, mean, minimum, maximum, Percentile(samples, 0.50), Percentile(samples, 0.95), 0d, changes);
	}

	private static long EventCountIn(LagEvent lagEvent, bool startup)
	{
		int index = (int)lagEvent;
		long count = Interlocked.Read(ref _eventCounts[index]);
		long mark = Math.Min(_startupEventCounts[index], count);
		return startup ? mark : count - mark;
	}

	private static double EventWorstIn(LagEvent lagEvent, bool startup)
	{
		int index = (int)lagEvent;
		if (Volatile.Read(ref _startupMarked) == 0) return Volatile.Read(ref _eventWorst[index]);
		return startup ? _startupEventWorst[index] : Volatile.Read(ref _steadyEventWorst[index]);
	}

	private readonly struct SampleStatistics
	{
		public SampleStatistics(int count, double mean, double minimum, double maximum, double p50, double p95, double p99, int changes)
		{
			Count = count;
			Mean = mean;
			Minimum = minimum;
			Maximum = maximum;
			P50 = p50;
			P95 = p95;
			P99 = p99;
			Changes = changes;
		}

		public int Count { get; }

		public double Mean { get; }

		public double Minimum { get; }

		public double Maximum { get; }

		public double P50 { get; }

		public double P95 { get; }

		public double P99 { get; }

		public int Changes { get; }
	}

	public static string RenderTextReport()
	{
		StringBuilder report = new();
		report.AppendLine("=== Lag probe ===");
		report.AppendLine($"role: {(string.IsNullOrEmpty(_role) ? "unset" : _role)}");
		AppendTextWindow(report, "startup", true);
		AppendTextWindow(report, "steady", false);
		return report.ToString();
	}

	private static void AppendTextWindow(StringBuilder report, string heading, bool startup)
	{
		report.AppendLine($"window: {heading}");
		report.AppendLine("stage: count mean_ms p50_ms p95_ms p99_ms max_ms");
		for (int index = 0; index < _stageCount; index++)
		{
			SampleStatistics statistics = DescribeStage(index, startup);
			if (statistics.Count <= 0) continue;

			report.AppendLine(string.Format(
				CultureInfo.InvariantCulture,
				"{0}: {1} {2:F3} {3:F3} {4:F3} {5:F3} {6:F3}",
				(LagStage)index,
				statistics.Count,
				statistics.Mean,
				statistics.P50,
				statistics.P95,
				statistics.P99,
				statistics.Maximum));
		}

		report.AppendLine("event: count worst");
		for (int index = 0; index < _eventCount; index++)
		{
			LagEvent lagEvent = (LagEvent)index;
			long count = EventCountIn(lagEvent, startup);
			double worst = EventWorstIn(lagEvent, startup);
			if (count == 0 && worst <= 0d) continue;

			report.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0}: {1} {2:F3}", lagEvent, count, worst));
		}

		report.AppendLine("gauge: samples mean min max p50 p95 changes");
		for (int index = 0; index < _gaugeCount; index++)
		{
			SampleStatistics statistics = DescribeGauge(index, startup);
			if (statistics.Count <= 0) continue;

			report.AppendLine(string.Format(
				CultureInfo.InvariantCulture,
				"{0}: {1} {2:F3} {3:F3} {4:F3} {5:F3} {6:F3} {7}",
				(LagGauge)index,
				statistics.Count,
				statistics.Mean,
				statistics.Minimum,
				statistics.Maximum,
				statistics.P50,
				statistics.P95,
				statistics.Changes));
		}
	}

	public static string RenderJson()
	{
		StringBuilder json = new();
		json.Append("{\"role\":\"").Append(_role).Append('"');
		AppendJsonWindow(json, "stages", "events", "gauges", false);
		AppendJsonWindow(json, "startupStages", "startupEvents", "startupGauges", true);
		json.Append('}');
		return json.ToString();
	}

	private static void AppendJsonWindow(StringBuilder json, string stageSection, string eventSection, string gaugeSection, bool startup)
	{
		json.Append(",\"").Append(stageSection).Append("\":{");
		bool first = true;
		for (int index = 0; index < _stageCount; index++)
		{
			SampleStatistics statistics = DescribeStage(index, startup);
			if (statistics.Count <= 0) continue;

			if (!first) json.Append(',');
			first = false;
			json.Append('"').Append((LagStage)index).Append("\":{");
			json.Append("\"count\":").Append(statistics.Count);
			json.Append(",\"mean\":").Append(Number(statistics.Mean));
			json.Append(",\"p50\":").Append(Number(statistics.P50));
			json.Append(",\"p95\":").Append(Number(statistics.P95));
			json.Append(",\"p99\":").Append(Number(statistics.P99));
			json.Append(",\"max\":").Append(Number(statistics.Maximum));
			json.Append('}');
		}

		json.Append("},\"").Append(eventSection).Append("\":{");
		first = true;
		for (int index = 0; index < _eventCount; index++)
		{
			LagEvent lagEvent = (LagEvent)index;
			long count = EventCountIn(lagEvent, startup);
			double worst = EventWorstIn(lagEvent, startup);
			if (count == 0 && worst <= 0d) continue;

			if (!first) json.Append(',');
			first = false;
			json.Append('"').Append(lagEvent).Append("\":{");
			json.Append("\"count\":").Append(count);
			json.Append(",\"worst\":").Append(Number(worst));
			json.Append('}');
		}

		json.Append("},\"").Append(gaugeSection).Append("\":{");
		first = true;
		for (int index = 0; index < _gaugeCount; index++)
		{
			SampleStatistics statistics = DescribeGauge(index, startup);
			if (statistics.Count <= 0) continue;

			if (!first) json.Append(',');
			first = false;
			json.Append('"').Append((LagGauge)index).Append("\":{");
			json.Append("\"count\":").Append(statistics.Count);
			json.Append(",\"mean\":").Append(Number(statistics.Mean));
			json.Append(",\"min\":").Append(Number(statistics.Minimum));
			json.Append(",\"max\":").Append(Number(statistics.Maximum));
			json.Append(",\"p50\":").Append(Number(statistics.P50));
			json.Append(",\"p95\":").Append(Number(statistics.P95));
			json.Append('}');
		}

		json.Append('}');
	}

	private static string Number(double value) => value.ToString("F4", CultureInfo.InvariantCulture);

	public static bool WriteReport()
	{
		if (!IsEnabled || string.IsNullOrEmpty(_outputDirectory)) return false;

		try
		{
			Directory.CreateDirectory(_outputDirectory);
			File.WriteAllText(Path.Combine(_outputDirectory, $"lag_{_role}.txt"), RenderTextReport());
			File.WriteAllText(Path.Combine(_outputDirectory, $"lag_{_role}.json"), RenderJson());
			return true;
		}
		catch (IOException)
		{
			return false;
		}
		catch (UnauthorizedAccessException)
		{
			return false;
		}
	}

	private static double Percentile(double[] sorted, double fraction)
	{
		if (sorted.Length == 0) return 0d;

		int index = (int)Math.Clamp(Math.Ceiling(fraction * sorted.Length) - 1d, 0d, sorted.Length - 1);
		return sorted[index];
	}

	private static double Percentile(float[] sorted, double fraction)
	{
		if (sorted.Length == 0) return 0d;

		int index = (int)Math.Clamp(Math.Ceiling(fraction * sorted.Length) - 1d, 0d, sorted.Length - 1);
		return sorted[index];
	}
}
