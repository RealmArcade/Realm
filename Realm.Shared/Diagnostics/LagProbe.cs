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
	AuthoritySerializeMilliseconds,
	AuthoritySendMilliseconds,
	ClientPhysicsIntervalMilliseconds,
	ClientPhysicsTickMilliseconds,
	ClientNetworkPumpMilliseconds,
	ClientDeserializeMilliseconds,
	ClientApplyMilliseconds,
	ClientRenderIntervalMilliseconds,
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
	ClientSequenceGaps,
	ClientWorstSkippedSequences,
	ClientStarvations,
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
			if (observed == current) return;
			current = observed;
		}
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

	public static string RenderTextReport()
	{
		StringBuilder report = new();
		report.AppendLine("=== Lag probe ===");
		report.AppendLine($"role: {(string.IsNullOrEmpty(_role) ? "unset" : _role)}");
		report.AppendLine("stage: count mean_ms p50_ms p95_ms p99_ms max_ms");
		for (int index = 0; index < _stageCount; index++)
		{
			LagStage stage = (LagStage)index;
			int count = Math.Min(Volatile.Read(ref _stageCounts[index]), SampleCapacity);
			if (count <= 0) continue;

			double[] sorted = new double[count];
			Array.Copy(_stageSamples, index * SampleCapacity, sorted, 0, count);
			Array.Sort(sorted);
			double mean = 0d;
			for (int slot = 0; slot < count; slot++) mean += sorted[slot];
			mean /= count;

			report.AppendLine(string.Format(
				CultureInfo.InvariantCulture,
				"{0}: {1} {2:F3} {3:F3} {4:F3} {5:F3} {6:F3}",
				stage,
				count,
				mean,
				Percentile(sorted, 0.50),
				Percentile(sorted, 0.95),
				Percentile(sorted, 0.99),
				sorted[count - 1]));
		}

		report.AppendLine("event: count worst");
		for (int index = 0; index < _eventCount; index++)
		{
			long count = Interlocked.Read(ref _eventCounts[index]);
			double worst = Volatile.Read(ref _eventWorst[index]);
			if (count == 0 && worst <= 0d) continue;

			report.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0}: {1} {2:F3}", (LagEvent)index, count, worst));
		}

		report.AppendLine("gauge: samples mean min max p50 p95 changes");
		for (int index = 0; index < _gaugeCount; index++)
		{
			int count = Math.Min(Volatile.Read(ref _gaugeCounts[index]), GaugeCapacity);
			if (count <= 0) continue;

			float[] sorted = new float[count];
			Array.Copy(_gaugeSamples, index * GaugeCapacity, sorted, 0, count);
			float mean = 0f;
			float minimum = float.MaxValue;
			float maximum = float.MinValue;
			int changes = 0;
			float previous = sorted[0];
			for (int slot = 0; slot < count; slot++)
			{
				float value = sorted[slot];
				mean += value;
				if (value < minimum) minimum = value;
				if (value > maximum) maximum = value;
				if (MathF.Abs(value - previous) >= 0.5f) changes++;
				previous = value;
			}

			mean /= count;
			Array.Sort(sorted);
			report.AppendLine(string.Format(
				CultureInfo.InvariantCulture,
				"{0}: {1} {2:F3} {3:F3} {4:F3} {5:F3} {6:F3} {7}",
				(LagGauge)index,
				count,
				mean,
				minimum,
				maximum,
				Percentile(sorted, 0.50),
				Percentile(sorted, 0.95),
				changes));
		}

		return report.ToString();
	}

	public static string RenderJson()
	{
		StringBuilder json = new();
		json.Append("{\"role\":\"").Append(_role).Append("\",\"stages\":{");
		bool firstStage = true;
		for (int index = 0; index < _stageCount; index++)
		{
			int count = Math.Min(Volatile.Read(ref _stageCounts[index]), SampleCapacity);
			if (count <= 0) continue;

			double[] sorted = new double[count];
			Array.Copy(_stageSamples, index * SampleCapacity, sorted, 0, count);
			Array.Sort(sorted);
			double mean = 0d;
			for (int slot = 0; slot < count; slot++) mean += sorted[slot];
			mean /= count;

			if (!firstStage) json.Append(',');
			firstStage = false;
			json.Append('"').Append((LagStage)index).Append("\":{");
			json.Append("\"count\":").Append(count);
			json.Append(",\"mean\":").Append(mean.ToString("F4", CultureInfo.InvariantCulture));
			json.Append(",\"p50\":").Append(Percentile(sorted, 0.50).ToString("F4", CultureInfo.InvariantCulture));
			json.Append(",\"p95\":").Append(Percentile(sorted, 0.95).ToString("F4", CultureInfo.InvariantCulture));
			json.Append(",\"p99\":").Append(Percentile(sorted, 0.99).ToString("F4", CultureInfo.InvariantCulture));
			json.Append(",\"max\":").Append(sorted[count - 1].ToString("F4", CultureInfo.InvariantCulture));
			json.Append('}');
		}

		json.Append("},\"events\":{");
		bool firstEvent = true;
		for (int index = 0; index < _eventCount; index++)
		{
			long count = Interlocked.Read(ref _eventCounts[index]);
			double worst = Volatile.Read(ref _eventWorst[index]);
			if (count == 0 && worst <= 0d) continue;

			if (!firstEvent) json.Append(',');
			firstEvent = false;
			json.Append('"').Append((LagEvent)index).Append("\":{");
			json.Append("\"count\":").Append(count);
			json.Append(",\"worst\":").Append(worst.ToString("F4", CultureInfo.InvariantCulture));
			json.Append('}');
		}

		json.Append("},\"gauges\":{");
		bool firstGauge = true;
		for (int index = 0; index < _gaugeCount; index++)
		{
			int count = Math.Min(Volatile.Read(ref _gaugeCounts[index]), GaugeCapacity);
			if (count <= 0) continue;

			float[] sorted = new float[count];
			Array.Copy(_gaugeSamples, index * GaugeCapacity, sorted, 0, count);
			float mean = 0f;
			float minimum = float.MaxValue;
			float maximum = float.MinValue;
			for (int slot = 0; slot < count; slot++)
			{
				mean += sorted[slot];
				if (sorted[slot] < minimum) minimum = sorted[slot];
				if (sorted[slot] > maximum) maximum = sorted[slot];
			}

			mean /= count;
			Array.Sort(sorted);
			if (!firstGauge) json.Append(',');
			firstGauge = false;
			json.Append('"').Append((LagGauge)index).Append("\":{");
			json.Append("\"count\":").Append(count);
			json.Append(",\"mean\":").Append(mean.ToString("F4", CultureInfo.InvariantCulture));
			json.Append(",\"min\":").Append(minimum.ToString("F4", CultureInfo.InvariantCulture));
			json.Append(",\"max\":").Append(maximum.ToString("F4", CultureInfo.InvariantCulture));
			json.Append(",\"p50\":").Append(Percentile(sorted, 0.50).ToString("F4", CultureInfo.InvariantCulture));
			json.Append(",\"p95\":").Append(Percentile(sorted, 0.95).ToString("F4", CultureInfo.InvariantCulture));
			json.Append('}');
		}

		json.Append("}}");
		return json.ToString();
	}

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
