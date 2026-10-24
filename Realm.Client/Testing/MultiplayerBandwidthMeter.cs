using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Realm.Client.Testing;

public static class MultiplayerBandwidthMeter
{
	private sealed class PeerTotals
	{
		public long SnapshotBytes;
		public long BaselineBytes;
		public long DeltaBytes;
		public int SnapshotCount;
		public int BaselineCount;
	}

	private static readonly Dictionary<int, PeerTotals> _peers = new();
	private static readonly List<int> _peerOrder = new();

	public static void Reset()
	{
		_peers.Clear();
		_peerOrder.Clear();
	}

	public static void RecordSnapshotPayload(int peerId, int byteCount, bool isBaseline)
	{
		if (byteCount <= 0) return;
		if (!_peers.TryGetValue(peerId, out PeerTotals? totals))
		{
			totals = new PeerTotals();
			_peers[peerId] = totals;
			_peerOrder.Add(peerId);
		}

		totals.SnapshotBytes += byteCount;
		totals.SnapshotCount++;
		if (isBaseline)
		{
			totals.BaselineBytes += byteCount;
			totals.BaselineCount++;
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

			text.AppendLine(
				$"peer {peerId}: {bytesPerSecond.ToString("F0", CultureInfo.InvariantCulture)} B/s total "
				+ $"({(bytesPerSecond * 8 / 1000).ToString("F1", CultureInfo.InvariantCulture)} kbit/s), "
				+ $"baseline {baselineBytesPerSecond.ToString("F0", CultureInfo.InvariantCulture)} B/s over {totals.BaselineCount} baselines, "
				+ $"delta {deltaBytesPerSecond.ToString("F0", CultureInfo.InvariantCulture)} B/s over {totals.SnapshotCount - totals.BaselineCount} deltas, "
				+ $"average payload {averagePayloadBytes.ToString("F0", CultureInfo.InvariantCulture)} B");

			if (index > 0) json.Append(',');
			json.Append('{');
			json.Append("\"peerId\":").Append(peerId);
			json.Append(",\"bytesPerSecond\":").Append(bytesPerSecond.ToString("F1", CultureInfo.InvariantCulture));
			json.Append(",\"baselineBytesPerSecond\":").Append(baselineBytesPerSecond.ToString("F1", CultureInfo.InvariantCulture));
			json.Append(",\"deltaBytesPerSecond\":").Append(deltaBytesPerSecond.ToString("F1", CultureInfo.InvariantCulture));
			json.Append(",\"averagePayloadBytes\":").Append(averagePayloadBytes.ToString("F1", CultureInfo.InvariantCulture));
			json.Append(",\"snapshotCount\":").Append(totals.SnapshotCount);
			json.Append('}');
		}

		text.AppendLine($"Worst client: {worstBytesPerSecond.ToString("F0", CultureInfo.InvariantCulture)} B/s; host total: {totalBytesPerSecond.ToString("F0", CultureInfo.InvariantCulture)} B/s.");
		json.Append(']');
		json.Append(",\"worstClientBytesPerSecond\":").Append(worstBytesPerSecond.ToString("F1", CultureInfo.InvariantCulture));
		json.Append(",\"hostTotalBytesPerSecond\":").Append(totalBytesPerSecond.ToString("F1", CultureInfo.InvariantCulture));
		json.Append('}');

		Directory.CreateDirectory(outputDirectory);
		File.WriteAllText(Path.Combine(outputDirectory, "bandwidth_report.txt"), text.ToString());
		File.WriteAllText(Path.Combine(outputDirectory, "bandwidth_report.json"), json.ToString());
		return _peerOrder.Count;
	}
}
