using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace Realm.Client.Testing;

public static class MultiplayerBandwidthMeter
{
	private static long _receivedSnapshotBytes;
	private static long _receivedBaselineBytes;
	private static int _receivedSnapshotCount;
	private static int _receivedBaselineCount;

	public static bool IsRecording { get; set; }

	public static void Reset()
	{
		_receivedSnapshotBytes = 0;
		_receivedBaselineBytes = 0;
		_receivedSnapshotCount = 0;
		_receivedBaselineCount = 0;
	}

	public static void RecordReceivedSnapshot(int byteCount, bool isBaseline)
	{
		if (byteCount <= 0) return;
		_receivedSnapshotBytes += byteCount;
		_receivedSnapshotCount++;
		if (!isBaseline) return;

		_receivedBaselineBytes += byteCount;
		_receivedBaselineCount++;
	}

	public static int WriteReport(string outputDirectory, double elapsedSeconds)
	{
		double duration = Math.Max(0.001, elapsedSeconds);
		double bytesPerSecond = _receivedSnapshotBytes / duration;
		double baselineBytesPerSecond = _receivedBaselineBytes / duration;
		double deltaBytesPerSecond = (_receivedSnapshotBytes - _receivedBaselineBytes) / duration;
		double averagePayload = _receivedSnapshotCount == 0 ? 0 : (double)_receivedSnapshotBytes / _receivedSnapshotCount;
		double snapshotRate = _receivedSnapshotCount / duration;

		StringBuilder text = new();
		text.AppendLine("=== Snapshot bandwidth this screen received ===");
		text.AppendLine($"Measured over {duration.ToString("F2", CultureInfo.InvariantCulture)} s of match time.");
		text.AppendLine(
			$"this screen: {bytesPerSecond.ToString("F0", CultureInfo.InvariantCulture)} B/s received "
			+ $"({(bytesPerSecond * 8 / 1000).ToString("F1", CultureInfo.InvariantCulture)} kbit/s) over {_receivedSnapshotCount} snapshot(s) "
			+ $"at {snapshotRate.ToString("F1", CultureInfo.InvariantCulture)} per second, average payload "
			+ $"{averagePayload.ToString("F0", CultureInfo.InvariantCulture)} B");
		text.AppendLine(
			$"  deltas {deltaBytesPerSecond.ToString("F0", CultureInfo.InvariantCulture)} B/s, baselines "
			+ $"{baselineBytesPerSecond.ToString("F0", CultureInfo.InvariantCulture)} B/s over {_receivedBaselineCount} baseline(s)");

		StringBuilder json = new();
		json.Append('{');
		json.Append("\"elapsedSeconds\":").Append(duration.ToString("F3", CultureInfo.InvariantCulture));
		json.Append(",\"receivedBytesPerSecond\":").Append(bytesPerSecond.ToString("F1", CultureInfo.InvariantCulture));
		json.Append(",\"deltaBytesPerSecond\":").Append(deltaBytesPerSecond.ToString("F1", CultureInfo.InvariantCulture));
		json.Append(",\"baselineBytesPerSecond\":").Append(baselineBytesPerSecond.ToString("F1", CultureInfo.InvariantCulture));
		json.Append(",\"averagePayloadBytes\":").Append(averagePayload.ToString("F1", CultureInfo.InvariantCulture));
		json.Append(",\"snapshotCount\":").Append(_receivedSnapshotCount);
		json.Append(",\"baselineCount\":").Append(_receivedBaselineCount);
		json.Append(",\"snapshotRatePerSecond\":").Append(snapshotRate.ToString("F2", CultureInfo.InvariantCulture));
		json.Append('}');

		Directory.CreateDirectory(outputDirectory);
		File.WriteAllText(Path.Combine(outputDirectory, "screen_traffic.txt"), text.ToString());
		File.WriteAllText(Path.Combine(outputDirectory, "screen_traffic.json"), json.ToString());
		return _receivedSnapshotCount;
	}
}
