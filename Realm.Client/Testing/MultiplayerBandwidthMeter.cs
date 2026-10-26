using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Godot;

namespace Realm.Client.Testing;	/// <summary>
	/// Why the host sent a peer a full baseline. A baseline is the whole replicated world, so the mix of reasons is what
	/// states whether a peer is being recovered cheaply or is being re-sent everything over and over.
	/// </summary>
public enum BaselineSendReason
{
	None,
	Initial,
	Unacknowledged,
	Safety,
	Resync
}

public static class MultiplayerBandwidthMeter
{
	private static void AppendSectionBreakdown(StringBuilder text, string label, SectionTotals totals)
	{
		if (totals.SampleCount == 0)
		{
			text.AppendLine($"{label}: not sampled.");
			return;
		}

		double samples = totals.SampleCount;
		text.AppendLine(
			$"{label} average over {totals.SampleCount} sampled snapshot(s): "
			+ $"units {((double)totals.UnitsBytes / samples).ToString("F0", CultureInfo.InvariantCulture)} B for {(totals.UnitCount / samples).ToString("F1", CultureInfo.InvariantCulture)} unit(s), "
			+ $"components {((double)totals.ComponentBytes / samples).ToString("F0", CultureInfo.InvariantCulture)} B for {(totals.ComponentCount / samples).ToString("F1", CultureInfo.InvariantCulture)} delta(s), "
			+ $"cold payloads {((double)totals.ColdPayloadBytes / samples).ToString("F0", CultureInfo.InvariantCulture)} B for {(totals.ColdPayloadCount / samples).ToString("F1", CultureInfo.InvariantCulture)} payload(s), "
			+ $"abilities {((double)totals.AbilityBytes / samples).ToString("F0", CultureInfo.InvariantCulture)} B, "
			+ $"static text {((double)totals.StaticTextBytes / samples).ToString("F0", CultureInfo.InvariantCulture)} B, "
			+ $"catalog {((double)totals.CatalogBytes / samples).ToString("F0", CultureInfo.InvariantCulture)} B");
	}

	private static void AppendSectionJson(StringBuilder json, string label, SectionTotals totals)
	{
		double samples = Math.Max(1, totals.SampleCount);
		json.Append('"').Append(label).Append("\":{");
		json.Append("\"samples\":").Append(totals.SampleCount);
		json.Append(",\"unitsBytesPerSnapshot\":").Append(((double)totals.UnitsBytes / samples).ToString("F1", CultureInfo.InvariantCulture));
		json.Append(",\"componentBytesPerSnapshot\":").Append(((double)totals.ComponentBytes / samples).ToString("F1", CultureInfo.InvariantCulture));
		json.Append(",\"coldPayloadBytesPerSnapshot\":").Append(((double)totals.ColdPayloadBytes / samples).ToString("F1", CultureInfo.InvariantCulture));
		json.Append(",\"abilityBytesPerSnapshot\":").Append(((double)totals.AbilityBytes / samples).ToString("F1", CultureInfo.InvariantCulture));
		json.Append(",\"staticTextBytesPerSnapshot\":").Append(((double)totals.StaticTextBytes / samples).ToString("F1", CultureInfo.InvariantCulture));
		json.Append(",\"catalogBytesPerSnapshot\":").Append(((double)totals.CatalogBytes / samples).ToString("F1", CultureInfo.InvariantCulture));
		json.Append(",\"unitCountPerSnapshot\":").Append(((double)totals.UnitCount / samples).ToString("F1", CultureInfo.InvariantCulture));
		json.Append(",\"componentCountPerSnapshot\":").Append(((double)totals.ComponentCount / samples).ToString("F1", CultureInfo.InvariantCulture));
		json.Append('}');
	}

	private sealed class PeerTotals
	{
		public long SnapshotBytes;
		public long BaselineBytes;
		public long DeltaBytes;
		public long SerializedBaselineBytes;
		public long WireBaselineBytes;
		public double FramingMilliseconds;
		public double SlowestFramingMilliseconds;
		public int FramedBaselineCount;
		public long SerializedDeltaBytes;
		public long WireDeltaBytes;
		public double DeltaFramingMilliseconds;
		public double SlowestDeltaFramingMilliseconds;
		public int FramedDeltaCount;
		public int SnapshotCount;
		public int BaselineCount;
		public int InitialBaselineCount;
		public int UnacknowledgedBaselineCount;
		public int SafetyBaselineCount;
		public int ResyncBaselineCount;
		public int WorstAckLagSequences = -1;
		public int WorstAckStallSequences = -1;
	}

	private sealed class SectionTotals
	{
		public long UnitsBytes;
		public long ComponentBytes;
		public long ColdPayloadBytes;
		public long AbilityBytes;
		public long StaticTextBytes;
		public long CatalogBytes;
		public long UnitCount;
		public long ComponentCount;
		public long ColdPayloadCount;
		public int SampleCount;

		public void Add(int unitsBytes, int componentBytes, int coldPayloadBytes, int abilityBytes, int staticTextBytes, int catalogBytes, int unitCount, int componentCount, int coldPayloadCount)
		{
			UnitsBytes += unitsBytes;
			ComponentBytes += componentBytes;
			ColdPayloadBytes += coldPayloadBytes;
			AbilityBytes += abilityBytes;
			StaticTextBytes += staticTextBytes;
			CatalogBytes += catalogBytes;
			UnitCount += unitCount;
			ComponentCount += componentCount;
			ColdPayloadCount += coldPayloadCount;
			SampleCount++;
		}
	}

	private static readonly Dictionary<string, long> _baselineComponentBytes = new();
	private static readonly Dictionary<string, long> _deltaComponentBytes = new();
	private static readonly Dictionary<int, PeerTotals> _peers = new();
	private static readonly Dictionary<int, List<int>> _sentSequences = new();
	private static readonly List<int> _peerOrder = new();
	private static readonly SectionTotals _baselineSections = new();
	private static readonly SectionTotals _deltaSections = new();

	public static bool IsRecording { get; set; }

	public static void Reset()
	{
		_peers.Clear();
		_peerOrder.Clear();
		_sentSequences.Clear();
		_framingMillisecondsBySequence.Clear();
		_framedBaselinesBySequence.Clear();
		_firstSequence = -1;
		_lastSequence = -1;
		_firstTicksMsec = 0;
		_lastTicksMsec = 0;
		_firstPhysicsFrames = 0;
		_lastPhysicsFrames = 0;
		SlowestSequenceMilliseconds = 0;
		SlowestSequence = -1;
		SlowestSequenceBaselineCount = 0;
		ResetSections(_baselineSections);
		ResetSections(_deltaSections);
		_baselineComponentBytes.Clear();
		_deltaComponentBytes.Clear();
	}

	private static void ResetSections(SectionTotals totals)
	{
		totals.UnitsBytes = 0;
		totals.ComponentBytes = 0;
		totals.ColdPayloadBytes = 0;
		totals.AbilityBytes = 0;
		totals.StaticTextBytes = 0;
		totals.CatalogBytes = 0;
		totals.UnitCount = 0;
		totals.ComponentCount = 0;
		totals.ColdPayloadCount = 0;
		totals.SampleCount = 0;
	}

	public static void RecordSnapshotComposition(bool isBaseline, int unitsBytes, int componentBytes, int coldPayloadBytes, int abilityBytes, int staticTextBytes, int catalogBytes, int unitCount, int componentCount, int coldPayloadCount)
	{
		SectionTotals totals = isBaseline ? _baselineSections : _deltaSections;
		totals.Add(unitsBytes, componentBytes, coldPayloadBytes, abilityBytes, staticTextBytes, catalogBytes, unitCount, componentCount, coldPayloadCount);
	}

	public static void RecordComponentBytes(bool isBaseline, string codecName, int byteCount)
	{
		Dictionary<string, long> totals = isBaseline ? _baselineComponentBytes : _deltaComponentBytes;
		long existing = totals.TryGetValue(codecName, out long stored) ? stored : 0;
		totals[codecName] = existing + byteCount;
	}

	private static void AppendComponentRanking(StringBuilder text, string label, Dictionary<string, long> totals, int sampleCount)
	{
		if (sampleCount <= 0 || totals.Count == 0)
		{
			text.AppendLine($"{label} component bytes by type: not sampled.");
			return;
		}

		var ranked = new List<KeyValuePair<string, long>>(totals);
		ranked.Sort(static (left, right) => right.Value.CompareTo(left.Value));
		text.Append($"{label} component bytes per snapshot by type, largest first: ");
		int shown = 0;
		for (int index = 0; index < ranked.Count && shown < 6; index++)
		{
			if (ranked[index].Value == 0) continue;
			if (shown > 0) text.Append(", ");
			text.Append(ranked[index].Key).Append(' ').Append(((double)ranked[index].Value / sampleCount).ToString("F0", CultureInfo.InvariantCulture)).Append(" B");
			shown++;
		}

		text.AppendLine();
	}

	public static void RecordSnapshotSequence(int peerId, int sequence)
	{
		if (!_sentSequences.TryGetValue(peerId, out List<int>? sequences))
		{
			sequences = new List<int>(4096);
			_sentSequences[peerId] = sequences;
		}

		sequences.Add(sequence);
	}

	public static void RecordHostSnapshotSequence(int sequence)
	{
		ulong ticksMsec = Time.GetTicksMsec();
		ulong physicsFrames = Engine.GetPhysicsFrames();
		if (_firstSequence < 0)
		{
			_firstSequence = sequence;
			_firstTicksMsec = ticksMsec;
			_firstPhysicsFrames = physicsFrames;
		}

		_lastSequence = sequence;
		_lastTicksMsec = ticksMsec;
		_lastPhysicsFrames = physicsFrames;
	}

	private static int _firstSequence = -1;
	private static int _lastSequence = -1;
	private static ulong _firstTicksMsec;
	private static ulong _lastTicksMsec;
	private static ulong _firstPhysicsFrames;
	private static ulong _lastPhysicsFrames;

	public static void RecordSnapshotFraming(int peerId, bool isBaseline, int serializedByteCount, int wireByteCount, double milliseconds, int snapshotSequence)
	{
		if (serializedByteCount <= 0) return;
		if (!_peers.TryGetValue(peerId, out PeerTotals? totals))
		{
			totals = new PeerTotals();
			_peers[peerId] = totals;
			_peerOrder.Add(peerId);
		}

		if (isBaseline)
		{
			totals.FramedBaselineCount++;
			totals.SerializedBaselineBytes += serializedByteCount;
			totals.WireBaselineBytes += wireByteCount;
			totals.FramingMilliseconds += milliseconds;
			if (milliseconds > totals.SlowestFramingMilliseconds) totals.SlowestFramingMilliseconds = milliseconds;
		}
		else
		{
			totals.FramedDeltaCount++;
			totals.SerializedDeltaBytes += serializedByteCount;
			totals.WireDeltaBytes += wireByteCount;
			totals.DeltaFramingMilliseconds += milliseconds;
			if (milliseconds > totals.SlowestDeltaFramingMilliseconds) totals.SlowestDeltaFramingMilliseconds = milliseconds;
		}

		// Framing happens on the host tick, once per peer that is due a baseline. Peers that joined together are due one
		// on the same sequence, so what the host actually pays is the total at a sequence, not the cost of one frame.
		_framingMillisecondsBySequence.TryGetValue(snapshotSequence, out double sequenceMilliseconds);
		_framingMillisecondsBySequence[snapshotSequence] = sequenceMilliseconds + milliseconds;
		_framedBaselinesBySequence.TryGetValue(snapshotSequence, out int sequenceCount);
		_framedBaselinesBySequence[snapshotSequence] = sequenceCount + 1;
		double sequenceTotal = sequenceMilliseconds + milliseconds;
		if (sequenceTotal > SlowestSequenceMilliseconds)
		{
			SlowestSequenceMilliseconds = sequenceTotal;
			SlowestSequence = snapshotSequence;
			SlowestSequenceBaselineCount = sequenceCount + 1;
		}
	}

	public static double SlowestSequenceMilliseconds { get; private set; }
	public static int SlowestSequence { get; private set; } = -1;
	public static int SlowestSequenceBaselineCount { get; private set; }

	private static readonly Dictionary<int, double> _framingMillisecondsBySequence = new();
	private static readonly Dictionary<int, int> _framedBaselinesBySequence = new();

	public static void RecordSnapshotPayload(int peerId, int byteCount, bool isBaseline, BaselineSendReason reason = BaselineSendReason.None, int ackLagSequences = 0, int ackStallSequences = -1)
	{
		if (byteCount <= 0) return;
		if (!_peers.TryGetValue(peerId, out PeerTotals? totals))
		{
			totals = new PeerTotals();
			_peers[peerId] = totals;
			_peerOrder.Add(peerId);
		}

		// The stall is an every-tick property of the peer rather than a property of the baselines it was sent, so it is
		// recorded on every payload; dividing the resync count by it is what tells a late peer from a stuck one.
		if (ackStallSequences > totals.WorstAckStallSequences) totals.WorstAckStallSequences = ackStallSequences;

		totals.SnapshotBytes += byteCount;
		totals.SnapshotCount++;
		if (isBaseline)
		{
			totals.BaselineBytes += byteCount;
			totals.BaselineCount++;
			switch (reason)
			{
				case BaselineSendReason.Initial:
					totals.InitialBaselineCount++;
					break;
				case BaselineSendReason.Unacknowledged:
					totals.UnacknowledgedBaselineCount++;
					break;
				case BaselineSendReason.Safety:
					totals.SafetyBaselineCount++;
					break;
				case BaselineSendReason.Resync:
					totals.ResyncBaselineCount++;
					break;
			}

			if (ackLagSequences > totals.WorstAckLagSequences) totals.WorstAckLagSequences = ackLagSequences;
		}
		else
		{
			totals.DeltaBytes += byteCount;
		}
	}

	public static int BuildReport(string outputDirectory, double elapsedSeconds)
	{
		double duration = Math.Max(0.001, elapsedSeconds);
		StringBuilder text = new();
		StringBuilder json = new();
		text.AppendLine("=== Host snapshot bandwidth per client ===");
		text.AppendLine($"Measured over {duration.ToString("F2", CultureInfo.InvariantCulture)} s of game time with {_peerOrder.Count} client peer(s).");
		json.Append('{');
		json.Append("\"elapsedSeconds\":").Append(duration.ToString("F3", CultureInfo.InvariantCulture));
		json.Append(",\"peers\":[");

		double worstBytesPerSecond = 0;
		double totalBytesPerSecond = 0;
		for (int index = 0; index < _peerOrder.Count; index++)
		{
			int peerId = _peerOrder[index];
			PeerTotals totals = _peers[peerId];
			double bytesPerSecond = totals.SnapshotBytes / duration;
			double baselineBytesPerSecond = totals.BaselineBytes / duration;
			double deltaBytesPerSecond = totals.DeltaBytes / duration;
			double averagePayloadBytes = totals.SnapshotCount == 0 ? 0 : (double)totals.SnapshotBytes / totals.SnapshotCount;
			if (bytesPerSecond > worstBytesPerSecond) worstBytesPerSecond = bytesPerSecond;
			totalBytesPerSecond += bytesPerSecond;

			double averageSerializedBaseline = totals.FramedBaselineCount == 0 ? 0 : (double)totals.SerializedBaselineBytes / totals.FramedBaselineCount;
			double averageWireBaseline = totals.FramedBaselineCount == 0 ? 0 : (double)totals.WireBaselineBytes / totals.FramedBaselineCount;
			double baselineFramingRatio = totals.SerializedBaselineBytes == 0 ? 0 : (double)totals.WireBaselineBytes / totals.SerializedBaselineBytes;
			double averageFramingMilliseconds = totals.FramedBaselineCount == 0 ? 0 : totals.FramingMilliseconds / totals.FramedBaselineCount;
			double averageSerializedDelta = totals.FramedDeltaCount == 0 ? 0 : (double)totals.SerializedDeltaBytes / totals.FramedDeltaCount;
			double averageWireDelta = totals.FramedDeltaCount == 0 ? 0 : (double)totals.WireDeltaBytes / totals.FramedDeltaCount;
			double deltaFramingRatio = totals.SerializedDeltaBytes == 0 ? 0 : (double)totals.WireDeltaBytes / totals.SerializedDeltaBytes;
			double averageDeltaFramingMilliseconds = totals.FramedDeltaCount == 0 ? 0 : totals.DeltaFramingMilliseconds / totals.FramedDeltaCount;
			double framingMillisecondsPerSecond = totals.FramingMilliseconds + totals.DeltaFramingMilliseconds;
			text.AppendLine(
				$"peer {peerId}: {bytesPerSecond.ToString("F0", CultureInfo.InvariantCulture)} B/s total "
				+ $"({(bytesPerSecond * 8 / 1000).ToString("F1", CultureInfo.InvariantCulture)} kbit/s), "
				+ $"baseline {baselineBytesPerSecond.ToString("F0", CultureInfo.InvariantCulture)} B/s over {totals.BaselineCount} baselines, "
				+ $"delta {deltaBytesPerSecond.ToString("F0", CultureInfo.InvariantCulture)} B/s over {totals.SnapshotCount - totals.BaselineCount} deltas, "
				+ $"average payload {averagePayloadBytes.ToString("F0", CultureInfo.InvariantCulture)} B");
			text.AppendLine(
				$"  baseline framing: {averageSerializedBaseline.ToString("F0", CultureInfo.InvariantCulture)} B serialized to "
				+ $"{averageWireBaseline.ToString("F0", CultureInfo.InvariantCulture)} B on the wire "
				+ $"({(baselineFramingRatio * 100).ToString("F1", CultureInfo.InvariantCulture)} % of serialized), "
				+ $"{averageFramingMilliseconds.ToString("F2", CultureInfo.InvariantCulture)} ms average and "
				+ $"{totals.SlowestFramingMilliseconds.ToString("F2", CultureInfo.InvariantCulture)} ms slowest of {totals.FramedBaselineCount} framed");
			text.AppendLine(
				$"  delta framing: {averageSerializedDelta.ToString("F0", CultureInfo.InvariantCulture)} B serialized to "
				+ $"{averageWireDelta.ToString("F0", CultureInfo.InvariantCulture)} B on the wire "
				+ $"({(deltaFramingRatio * 100).ToString("F1", CultureInfo.InvariantCulture)} % of serialized) per delta, "
				+ $"{averageDeltaFramingMilliseconds.ToString("F3", CultureInfo.InvariantCulture)} ms average and "
				+ $"{totals.SlowestDeltaFramingMilliseconds.ToString("F3", CultureInfo.InvariantCulture)} ms slowest of {totals.FramedDeltaCount} framed, "
				+ $"{framingMillisecondsPerSecond.ToString("F1", CultureInfo.InvariantCulture)} ms spent framing over the whole run");
			string worstAckLag = totals.WorstAckLagSequences < 0 ? "never acknowledged" : $"{totals.WorstAckLagSequences} sequences";
			string worstAckStall = totals.WorstAckStallSequences < 0 ? "never acknowledged" : $"{totals.WorstAckStallSequences} sequences";
			text.AppendLine(
				$"  baselines by reason: {totals.InitialBaselineCount} initial, {totals.UnacknowledgedBaselineCount} unacknowledged, {totals.SafetyBaselineCount} safety, {totals.ResyncBaselineCount} resync; worst acknowledgement lag {worstAckLag}; longest stretch without a new acknowledgement {worstAckStall}");

			if (index > 0) json.Append(',');
			json.Append('{');
			json.Append("\"peerId\":").Append(peerId);
			json.Append(",\"bytesPerSecond\":").Append(bytesPerSecond.ToString("F1", CultureInfo.InvariantCulture));
			json.Append(",\"baselineBytesPerSecond\":").Append(baselineBytesPerSecond.ToString("F1", CultureInfo.InvariantCulture));
			json.Append(",\"averageSerializedBaselineBytes\":").Append(averageSerializedBaseline.ToString("F1", CultureInfo.InvariantCulture));
			json.Append(",\"averageWireBaselineBytes\":").Append(averageWireBaseline.ToString("F1", CultureInfo.InvariantCulture));
			json.Append(",\"averageFramingMilliseconds\":").Append(averageFramingMilliseconds.ToString("F3", CultureInfo.InvariantCulture));
			json.Append(",\"slowestFramingMilliseconds\":").Append(totals.SlowestFramingMilliseconds.ToString("F3", CultureInfo.InvariantCulture));
			json.Append(",\"averageSerializedDeltaBytes\":").Append(averageSerializedDelta.ToString("F1", CultureInfo.InvariantCulture));
			json.Append(",\"averageWireDeltaBytes\":").Append(averageWireDelta.ToString("F1", CultureInfo.InvariantCulture));
			json.Append(",\"framingMillisecondsPerSecond\":").Append(framingMillisecondsPerSecond.ToString("F2", CultureInfo.InvariantCulture));
			json.Append(",\"deltaBytesPerSecond\":").Append(deltaBytesPerSecond.ToString("F1", CultureInfo.InvariantCulture));
			json.Append(",\"averagePayloadBytes\":").Append(averagePayloadBytes.ToString("F1", CultureInfo.InvariantCulture));
			json.Append(",\"snapshotCount\":").Append(totals.SnapshotCount);
			json.Append(",\"initialBaselineCount\":").Append(totals.InitialBaselineCount);
			json.Append(",\"unacknowledgedBaselineCount\":").Append(totals.UnacknowledgedBaselineCount);
			json.Append(",\"safetyBaselineCount\":").Append(totals.SafetyBaselineCount);
			json.Append(",\"resyncBaselineCount\":").Append(totals.ResyncBaselineCount);
			json.Append(",\"worstAckLagSequences\":").Append(totals.WorstAckLagSequences);
			json.Append(",\"worstAckStallSequences\":").Append(totals.WorstAckStallSequences);
			json.Append('}');
		}

		text.AppendLine($"Worst client: {worstBytesPerSecond.ToString("F0", CultureInfo.InvariantCulture)} B/s; host total: {totalBytesPerSecond.ToString("F0", CultureInfo.InvariantCulture)} B/s.");
		text.AppendLine(
			$"Slowest host tick spent framing baselines: {SlowestSequenceMilliseconds.ToString("F2", CultureInfo.InvariantCulture)} ms for "
			+ $"{SlowestSequenceBaselineCount} baseline(s) at sequence {SlowestSequence}.");

		// A per-second figure is only meaningful if the rate it is divided by is the rate the host actually ticks at, so the
		// report states the observed snapshot rate next to the game clock it was divided by and the engine's physics rate.
		double wallSeconds = (_lastTicksMsec - _firstTicksMsec) / 1000.0;
		double snapshotRatePerGameSecond = _firstSequence < 0 || duration <= 0 ? 0 : (_lastSequence - _firstSequence) / duration;
		double snapshotRatePerWallSecond = wallSeconds <= 0 ? 0 : (_lastSequence - _firstSequence) / wallSeconds;
		double physicsRatePerWallSecond = wallSeconds <= 0 ? 0 : (_lastPhysicsFrames - _firstPhysicsFrames) / wallSeconds;
		text.AppendLine(
			$"Host timeline: {_lastSequence - _firstSequence} snapshot sequence(s) over {duration.ToString("F2", CultureInfo.InvariantCulture)} game seconds "
			+ $"and {wallSeconds.ToString("F2", CultureInfo.InvariantCulture)} wall seconds: {snapshotRatePerGameSecond.ToString("F1", CultureInfo.InvariantCulture)} per game second, "
			+ $"{snapshotRatePerWallSecond.ToString("F1", CultureInfo.InvariantCulture)} per wall second, "
			+ $"{physicsRatePerWallSecond.ToString("F1", CultureInfo.InvariantCulture)} physics ticks per wall second against a configured {Engine.PhysicsTicksPerSecond} Hz.");
		AppendSectionBreakdown(text, "baseline", _baselineSections);
		AppendSectionBreakdown(text, "delta", _deltaSections);
		AppendComponentRanking(text, "baseline", _baselineComponentBytes, _baselineSections.SampleCount);
		AppendComponentRanking(text, "delta", _deltaComponentBytes, _deltaSections.SampleCount);
		json.Append(']');
		json.Append(",\"worstClientBytesPerSecond\":").Append(worstBytesPerSecond.ToString("F1", CultureInfo.InvariantCulture));
		json.Append(",\"slowestSequenceFramingMilliseconds\":").Append(SlowestSequenceMilliseconds.ToString("F3", CultureInfo.InvariantCulture));
		json.Append(",\"slowestSequenceBaselineCount\":").Append(SlowestSequenceBaselineCount);
		json.Append(",\"snapshotRatePerGameSecond\":").Append(snapshotRatePerGameSecond.ToString("F2", CultureInfo.InvariantCulture));
		json.Append(",\"snapshotRatePerWallSecond\":").Append(snapshotRatePerWallSecond.ToString("F2", CultureInfo.InvariantCulture));
		json.Append(",\"physicsRatePerWallSecond\":").Append(physicsRatePerWallSecond.ToString("F2", CultureInfo.InvariantCulture));
		json.Append(",\"configuredPhysicsRate\":").Append(Engine.PhysicsTicksPerSecond);
		json.Append(",\"hostTotalBytesPerSecond\":").Append(totalBytesPerSecond.ToString("F1", CultureInfo.InvariantCulture));
		json.Append(",\"sections\":{");
		AppendSectionJson(json, "baseline", _baselineSections);
		json.Append(',');
		AppendSectionJson(json, "delta", _deltaSections);
		json.Append('}');
		json.Append('}');

		Directory.CreateDirectory(outputDirectory);
		text.AppendLine("Sampled composition counts every sampled snapshot once, not once per peer; a unit's component headers are counted per snapshot. Section sizes are the serialized snapshot, before a baseline is framed for the wire.");
		File.WriteAllText(Path.Combine(outputDirectory, "bandwidth_report.txt"), text.ToString());
		File.WriteAllText(Path.Combine(outputDirectory, "bandwidth_report.json"), json.ToString());

		StringBuilder coverage = new();
		foreach (KeyValuePair<int, List<int>> entry in _sentSequences)
		{
			for (int index = 0; index < entry.Value.Count; index++) coverage.Append(entry.Key).Append(' ').Append(entry.Value[index]).Append('\n');
		}

		File.WriteAllText(Path.Combine(outputDirectory, "host_sent_sequences.txt"), coverage.ToString());
		return _peerOrder.Count;
	}
}
