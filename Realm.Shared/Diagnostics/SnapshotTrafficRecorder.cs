using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Realm.Shared.Diagnostics;

/// <summary>Why a peer was sent a baseline rather than a delta.</summary>
public enum SnapshotBaselineReason
{
	None,
	Initial,
	Resync,
	Safety
}

/// <summary>
///     What the authority actually put on the wire: one frame per peer per tick, how big it was before and after framing,
///     and how long framing took. The frames are built in the authority's own process, so this is the only place that
///     number exists; a run that measures what a screen receives is measuring a different leg of the same journey.
/// </summary>
/// <remarks>
///     Every method is called from the thread the match publishes on, which is why the bookkeeping is a plain dictionary
///     rather than a concurrent one: a peer's frame is measured on whichever worker described it, and the measurement is
///     handed back to the publishing thread with the frame itself.
/// </remarks>
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

	/// <summary>Opens the accounting for one published sequence.</summary>
	public void BeginSequence(int sequence)
	{
		_sequence = sequence;
		if (_firstSequence < 0) _firstSequence = sequence;
		if (sequence > _lastSequence) _lastSequence = sequence;
	}

	/// <summary>
	///     Records one peer's frame: the snapshot as it was serialized, the frame that was actually sent, and how long the
	///     serialization and framing took. Called once per peer per sequence, on the publishing thread.
	/// </summary>
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

		// Framing happens on the publishing thread once per peer that is due a baseline, so what the host pays is the
		// total at a sequence rather than the cost of any one frame.
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

	/// <summary>
	///     Writes what the authority sent into a folder: the per-peer frame sizes and framing cost as text and as JSON for
	///     a harness to read, and the sequences each peer was sent in the form the sync verifier attributes coverage with.
	/// </summary>
	public void WriteReport(string folder, double elapsedGameSeconds, int configuredPhysicsRateHz)
	{
		Directory.CreateDirectory(folder);

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

		WriteAtomically(Path.Combine(folder, "bandwidth_report.txt"), text.ToString());
		WriteAtomically(Path.Combine(folder, "bandwidth_report.json"), json.ToString());

		// The verifier reads coverage as "peerId sequence" so a sequence the host never sent to a peer is told apart from
		// one it sent and the transport dropped; a match that recorded nothing leaves the file stating that.
		StringBuilder coverage = new();
		foreach (KeyValuePair<int, List<int>> entry in _sentSequencesByPeer)
		{
			for (int index = 0; index < entry.Value.Count; index++) coverage.Append(entry.Key).Append(' ').Append(entry.Value[index]).Append('\n');
		}

		WriteAtomically(Path.Combine(folder, "host_sent_sequences.txt"), coverage.ToString());
	}

	/// <summary>
	///     Writes one report through a temporary file, so a reader never sees half of one. The run that reads these ends
	///     the authority by killing it, which can land in the middle of a write: a truncated coverage file reads as a host
	///     that stopped describing a peer, which is the one thing this file exists to rule out.
	/// </summary>
	private static void WriteAtomically(string path, string contents)
	{
		string temporaryPath = path + ".tmp";
		File.WriteAllText(temporaryPath, contents);
		File.Move(temporaryPath, path, overwrite: true);
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
