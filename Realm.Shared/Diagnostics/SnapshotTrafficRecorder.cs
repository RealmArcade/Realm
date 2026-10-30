using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Realm.Shared.Diagnostics;

public enum SnapshotBaselineReason
{
	None,
	Initial,
	Resync,
	Safety
}

/// <summary>
///     One file a traffic report is made of. The report is rendered where the recorder's counters are, and written by
///     whichever lane was given the files, so rendering and storing are separable.
/// </summary>
public sealed record SnapshotReportFile(string FileName, string Contents);

public class SnapshotTrafficRecorder
{
	private readonly Dictionary<int, PeerTraffic> _peers = new();
	private readonly List<int> _peerOrder = new(8);
	private readonly Dictionary<int, List<int>> _sentSequencesByPeer = new();
	private readonly Dictionary<int, double> _framingBySequence = new();
	private readonly Dictionary<int, int> _baselinesBySequence = new();
	private int _sequence;
	private int _firstSequence = -1;
	private int _lastSequence = -1;
	private long _firstWallTicks;
	private long _lastWallTicks;
	private long _firstPhysicsFrame = -1;
	private long _lastPhysicsFrame = -1;
	private double _slowestSequenceMilliseconds;
	private int _slowestSequence = -1;
	private int _slowestSequenceBaselineCount;

	public void BeginSequence(int sequence)
	{
		_sequence = sequence;
		if (_firstSequence < 0) _firstSequence = sequence;
		if (sequence > _lastSequence) _lastSequence = sequence;
	}

	public void RecordFrame(
		int peerId,
		int sequence,
		int serializedBytes,
		int wireBytes,
		bool isBaseline,
		SnapshotBaselineReason reason,
		double framingMilliseconds,
		long physicsFrame,
		long wallTicks)
	{
		if (wireBytes <= 0) return;

		if (!_peers.TryGetValue(peerId, out PeerTraffic? traffic))
		{
			traffic = new PeerTraffic();
			_peers[peerId] = traffic;
			_peerOrder.Add(peerId);
		}

		traffic.SnapshotBytes += wireBytes;
		traffic.SnapshotCount++;
		traffic.SerializedBytes += serializedBytes;
		traffic.FramingMilliseconds += framingMilliseconds;
		if (isBaseline)
		{
			traffic.BaselineBytes += wireBytes;
			traffic.BaselineCount++;
			traffic.SerializedBaselineBytes += serializedBytes;
			traffic.WireBaselineBytes += wireBytes;
			traffic.BaselineFramingMilliseconds += framingMilliseconds;
			if (framingMilliseconds > traffic.SlowestBaselineFramingMilliseconds) traffic.SlowestBaselineFramingMilliseconds = framingMilliseconds;
			switch (reason)
			{
				case SnapshotBaselineReason.Initial:
					traffic.InitialBaselineCount++;
					break;
				case SnapshotBaselineReason.Resync:
					traffic.ResyncBaselineCount++;
					break;
				case SnapshotBaselineReason.Safety:
					traffic.SafetyBaselineCount++;
					break;
			}
		}
		else
		{
			traffic.DeltaBytes += wireBytes;
			traffic.DeltaCount++;
			traffic.SerializedDeltaBytes += serializedBytes;
			traffic.WireDeltaBytes += wireBytes;
			traffic.DeltaFramingMilliseconds += framingMilliseconds;
			if (framingMilliseconds > traffic.SlowestDeltaFramingMilliseconds) traffic.SlowestDeltaFramingMilliseconds = framingMilliseconds;
		}

		if (!_sentSequencesByPeer.TryGetValue(peerId, out List<int>? sequences))
		{
			sequences = new List<int>(1024);
			_sentSequencesByPeer[peerId] = sequences;
		}

		sequences.Add(sequence);

		_framingBySequence.TryGetValue(sequence, out double sequenceMilliseconds);
		double sequenceTotal = sequenceMilliseconds + framingMilliseconds;
		_framingBySequence[sequence] = sequenceTotal;
		if (isBaseline)
		{
			_baselinesBySequence.TryGetValue(sequence, out int baselineCount);
			_baselinesBySequence[sequence] = baselineCount + 1;
		}

		if (sequenceTotal > _slowestSequenceMilliseconds)
		{
			_slowestSequenceMilliseconds = sequenceTotal;
			_slowestSequence = sequence;
			_baselinesBySequence.TryGetValue(sequence, out int framedBaselines);
			_slowestSequenceBaselineCount = framedBaselines;
		}

		if (_firstWallTicks == 0)
		{
			_firstWallTicks = wallTicks;
			_firstPhysicsFrame = physicsFrame;
		}

		_lastWallTicks = wallTicks;
		_lastPhysicsFrame = physicsFrame;
	}

	public SnapshotReportFile[] BuildReport(double elapsedGameSeconds, int configuredPhysicsRateHz)
	{
		double duration = Math.Max(0.001, elapsedGameSeconds);
		StringBuilder text = new();
		StringBuilder json = new();
		text.AppendLine("=== Authority snapshot frames ===");
		text.AppendLine($"Measured over {duration.ToString("F2", CultureInfo.InvariantCulture)} s of match time across {_peerOrder.Count} peer(s).");
		json.Append('{');
		json.Append("\"elapsedSeconds\":").Append(duration.ToString("F3", CultureInfo.InvariantCulture));
		json.Append(",\"peers\":[");

		double worstBytesPerSecond = 0;
		double totalBytesPerSecond = 0;
		double deltaWireBytesPerSecond = 0;
		double deltaFramingMillisecondsPerSecond = 0;
		for (int index = 0; index < _peerOrder.Count; index++)
		{
			int peerId = _peerOrder[index];
			PeerTraffic traffic = _peers[peerId];

			double bytesPerSecond = traffic.SnapshotBytes / duration;
			double deltaBytesPerSecond = traffic.DeltaBytes / duration;
			double averagePayload = traffic.SnapshotCount == 0 ? 0 : (double)traffic.SnapshotBytes / traffic.SnapshotCount;
			double averageSerializedDelta = traffic.DeltaCount == 0 ? 0 : (double)traffic.SerializedDeltaBytes / traffic.DeltaCount;
			double averageWireDelta = traffic.DeltaCount == 0 ? 0 : (double)traffic.WireDeltaBytes / traffic.DeltaCount;
			double averageDeltaFraming = traffic.DeltaCount == 0 ? 0 : traffic.DeltaFramingMilliseconds / traffic.DeltaCount;
			double averageSerializedBaseline = traffic.BaselineCount == 0 ? 0 : (double)traffic.SerializedBaselineBytes / traffic.BaselineCount;
			double averageWireBaseline = traffic.BaselineCount == 0 ? 0 : (double)traffic.WireBaselineBytes / traffic.BaselineCount;
			double averageBaselineFraming = traffic.BaselineCount == 0 ? 0 : traffic.BaselineFramingMilliseconds / traffic.BaselineCount;
			double deltaRatio = traffic.SerializedDeltaBytes == 0 ? 0 : (double)traffic.WireDeltaBytes / traffic.SerializedDeltaBytes;
			double baselineRatio = traffic.SerializedBaselineBytes == 0 ? 0 : (double)traffic.WireBaselineBytes / traffic.SerializedBaselineBytes;
			double framingMillisecondsPerSecond = traffic.FramingMilliseconds / duration;

			if (bytesPerSecond > worstBytesPerSecond) worstBytesPerSecond = bytesPerSecond;
			totalBytesPerSecond += bytesPerSecond;
			deltaWireBytesPerSecond += deltaBytesPerSecond;
			deltaFramingMillisecondsPerSecond += traffic.DeltaFramingMilliseconds / duration;

			text.AppendLine(
				$"peer {peerId}: {Format(bytesPerSecond, "F0")} B/s ({Format(bytesPerSecond * 8 / 1000, "F1")} kbit/s), "
				+ $"average frame {Format(averagePayload, "F0")} B over {traffic.SnapshotCount} frame(s): "
				+ $"{traffic.DeltaCount} delta(s) at {Format(averageWireDelta, "F0")} B, {traffic.BaselineCount} baseline(s) at {Format(averageWireBaseline, "F0")} B");
			text.AppendLine(
				$"  delta framing: {Format(averageSerializedDelta, "F0")} B serialized to {Format(averageWireDelta, "F0")} B on the wire "
				+ $"({Format(deltaRatio * 100, "F1")} % of serialized), {Format(averageDeltaFraming, "F3")} ms average and "
				+ $"{Format(traffic.SlowestDeltaFramingMilliseconds, "F3")} ms slowest");
			text.AppendLine(
				$"  baseline framing: {Format(averageSerializedBaseline, "F0")} B serialized to {Format(averageWireBaseline, "F0")} B on the wire "
				+ $"({Format(baselineRatio * 100, "F1")} % of serialized), {Format(averageBaselineFraming, "F3")} ms average and "
				+ $"{Format(traffic.SlowestBaselineFramingMilliseconds, "F3")} ms slowest")
				;
			text.AppendLine(
				$"  baselines by reason: {traffic.InitialBaselineCount} initial, {traffic.ResyncBaselineCount} resync, {traffic.SafetyBaselineCount} safety; "
				+ $"{Format(framingMillisecondsPerSecond, "F1")} ms spent framing over the whole run");

			if (index > 0) json.Append(',');
			json.Append('{');
			json.Append("\"peerId\":").Append(peerId);
			json.Append(",\"bytesPerSecond\":").Append(Format(bytesPerSecond, "F1"));
			json.Append(",\"deltaBytesPerSecond\":").Append(Format(deltaBytesPerSecond, "F1"));
			json.Append(",\"averagePayloadBytes\":").Append(Format(averagePayload, "F1"));
			json.Append(",\"averageSerializedDeltaBytes\":").Append(Format(averageSerializedDelta, "F1"));
			json.Append(",\"averageWireDeltaBytes\":").Append(Format(averageWireDelta, "F1"));
			json.Append(",\"averageDeltaFramingMilliseconds\":").Append(Format(averageDeltaFraming, "F3"));
			json.Append(",\"slowestDeltaFramingMilliseconds\":").Append(Format(traffic.SlowestDeltaFramingMilliseconds, "F3"));
			json.Append(",\"averageSerializedBaselineBytes\":").Append(Format(averageSerializedBaseline, "F1"));
			json.Append(",\"averageWireBaselineBytes\":").Append(Format(averageWireBaseline, "F1"));
			json.Append(",\"averageBaselineFramingMilliseconds\":").Append(Format(averageBaselineFraming, "F3"));
			json.Append(",\"snapshotCount\":").Append(traffic.SnapshotCount);
			json.Append(",\"initialBaselineCount\":").Append(traffic.InitialBaselineCount);
			json.Append(",\"resyncBaselineCount\":").Append(traffic.ResyncBaselineCount);
			json.Append(",\"safetyBaselineCount\":").Append(traffic.SafetyBaselineCount);
			json.Append('}');
		}

		double wallSeconds = (_lastWallTicks - _firstWallTicks) / (double)System.Diagnostics.Stopwatch.Frequency;
		double snapshotRatePerGameSecond = _firstSequence < 0 || duration <= 0 ? 0 : (_lastSequence - _firstSequence) / duration;
		double physicsRatePerWallSecond = wallSeconds <= 0 || _firstPhysicsFrame < 0 ? 0 : (_lastPhysicsFrame - _firstPhysicsFrame) / wallSeconds;

		text.AppendLine(
			$"Worst peer: {Format(worstBytesPerSecond, "F0")} B/s; all peers together: {Format(totalBytesPerSecond, "F0")} B/s "
			+ $"(deltas {Format(deltaWireBytesPerSecond, "F0")} B/s, framed in {Format(deltaFramingMillisecondsPerSecond, "F2")} ms per second).");
		text.AppendLine(
			$"Slowest sequence spent framing baselines: {Format(_slowestSequenceMilliseconds, "F2")} ms for {_slowestSequenceBaselineCount} baseline(s) at sequence {_slowestSequence}.");
		if (wallSeconds > 0 && physicsRatePerWallSecond > 0)
		{
			text.AppendLine(
				$"Host timeline: {_lastSequence - _firstSequence} sequence(s) over {Format(duration, "F2")} match seconds and {Format(wallSeconds, "F2")} wall seconds: "
				+ $"{Format(snapshotRatePerGameSecond, "F1")} per match second, {Format((_lastSequence - _firstSequence) / Math.Max(0.001, wallSeconds), "F1")} per wall second, "
				+ $"{Format(physicsRatePerWallSecond, "F1")} physics ticks per wall second against a configured {configuredPhysicsRateHz} Hz.");
		}

		json.Append(']');
		json.Append(",\"worstClientBytesPerSecond\":").Append(Format(worstBytesPerSecond, "F1"));
		json.Append(",\"hostTotalBytesPerSecond\":").Append(Format(totalBytesPerSecond, "F1"));
		json.Append(",\"deltaBytesPerSecond\":").Append(Format(deltaWireBytesPerSecond, "F1"));
		json.Append(",\"deltaFramingMillisecondsPerSecond\":").Append(Format(deltaFramingMillisecondsPerSecond, "F2"));
		json.Append(",\"slowestSequenceFramingMilliseconds\":").Append(Format(_slowestSequenceMilliseconds, "F3"));
		json.Append(",\"slowestSequenceBaselineCount\":").Append(_slowestSequenceBaselineCount);
		json.Append(",\"snapshotRatePerGameSecond\":").Append(Format(snapshotRatePerGameSecond, "F2"));
		json.Append(",\"physicsRatePerWallSecond\":").Append(Format(physicsRatePerWallSecond, "F2"));
		json.Append(",\"configuredPhysicsRate\":").Append(configuredPhysicsRateHz);
		json.Append('}');

		StringBuilder coverage = new();
		foreach (KeyValuePair<int, List<int>> entry in _sentSequencesByPeer)
		{
			for (int index = 0; index < entry.Value.Count; index++) coverage.Append(entry.Key).Append(' ').Append(entry.Value[index]).Append('\n');
		}

		return
		[
			new SnapshotReportFile("bandwidth_report.txt", text.ToString()),
			new SnapshotReportFile("bandwidth_report.json", json.ToString()),
			new SnapshotReportFile("host_sent_sequences.txt", coverage.ToString())
		];
	}

	private static string Format(double value, string format) => value.ToString(format, CultureInfo.InvariantCulture);

	private sealed class PeerTraffic
	{
		public long SnapshotBytes;
		public long SerializedBytes;
		public long BaselineBytes;
		public long DeltaBytes;
		public long SerializedBaselineBytes;
		public long WireBaselineBytes;
		public long SerializedDeltaBytes;
		public long WireDeltaBytes;
		public double FramingMilliseconds;
		public double BaselineFramingMilliseconds;
		public double DeltaFramingMilliseconds;
		public double SlowestBaselineFramingMilliseconds;
		public double SlowestDeltaFramingMilliseconds;
		public int SnapshotCount;
		public int BaselineCount;
		public int DeltaCount;
		public int InitialBaselineCount;
		public int ResyncBaselineCount;
		public int SafetyBaselineCount;
	}
}
