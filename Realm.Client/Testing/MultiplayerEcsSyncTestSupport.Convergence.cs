using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Realm.Client.Services;
using Realm.Ecs.Replication;
using Realm.Shared.Diagnostics;
using Realm.Shared.Network;
using Realm.Shared.Services;

namespace Realm.Client.Testing;

public static partial class MultiplayerEcsSyncTestSupport
{
	private static void AppendConvergenceReport(MultiplayerEcsSyncReport report, MultiplayerEcsRunSettings settings, int lastPairedSequence, int pairedSnapshots, int lostSequenceCount, int missingBaselineCount, string clientOutputDirectory)
	{
		string hostDonePath = Path.Combine(settings.OutputDirectory, "host_done.txt");
		string clientSequencePath = Path.Combine(clientOutputDirectory, "client_final_sequence.txt");
		report.AddSummaryLine($"Network profile: {settings.Profile} (one-way {settings.OneWayDelayMilliseconds} ms, jitter {settings.JitterMilliseconds} ms, loss {settings.PacketLossPercent}%)");
		report.AddSummaryLine($"Injected ping (round trip): {settings.PingMilliseconds} ms");

		string relayStatsPath = Path.Combine(settings.OutputDirectory, "relay_stats.txt");
		if (settings.PingMilliseconds > 0)
		{
			if (!File.Exists(relayStatsPath))
			{
				report.AddFailure("Relay statistics are missing, so the injected delay cannot be proven to have been exercised.");
			}
			else
			{
				string relayStats = File.ReadAllText(relayStatsPath).Replace('\n', ' ').Replace('\r', ' ').Trim();
				report.AddSummaryLine($"Relay datagrams: {relayStats}");
				if (relayStats.Contains("forwarded=0") || relayStats.Length == 0)
				{
					report.AddFailure("The delay relay forwarded no datagrams, so the ping setting had no effect.");
				}
			}
		}

		if (!TryReadSequence(hostDonePath, out int hostFinalSequence) || !TryReadSequence(clientSequencePath, out int clientFinalSequence))
		{
			report.AddFailure("Final sequence markers are missing, so convergence after the injected delay could not be measured.");
			return;
		}

		int injectedOneWayMilliseconds = Math.Max(settings.OneWayDelayMilliseconds, settings.PingMilliseconds / 2);
		int oneWayTicks = (int)MathF.Ceiling(injectedOneWayMilliseconds / 1000f * 30f);
		int allowedAppliedLagTicks = oneWayTicks + 2;
		int appliedLagTicks = Math.Max(0, lastPairedSequence - clientFinalSequence);
		int streamLagTicks = Math.Max(0, hostFinalSequence - lastPairedSequence);
		report.AddSummaryLine($"Host final sequence: {hostFinalSequence}");
		report.AddSummaryLine($"Client final sequence: {clientFinalSequence}, last snapshot the client received: {lastPairedSequence}");
		report.AddSummaryLine($"Convergence: {appliedLagTicks} received-but-unapplied sequences (allowed {allowedAppliedLagTicks}, expected one-way lag {oneWayTicks} for {injectedOneWayMilliseconds} ms), {streamLagTicks} sequences never received");

		if (appliedLagTicks > allowedAppliedLagTicks)
		{
			report.AddFailure($"The client received snapshot {lastPairedSequence} but only applied {clientFinalSequence} at shutdown, so {appliedLagTicks} received sequences were never applied (allowed {allowedAppliedLagTicks}). The pipeline stalled on snapshots it already had, which is a pipeline defect rather than injected loss: it paired {pairedSnapshots} snapshots, never received {lostSequenceCount} host sequences after startup and had {missingBaselineCount} baselines it never applied.");
		}

		if (hostFinalSequence - clientFinalSequence < -120)
		{
			report.AddFailure($"Client final sequence {clientFinalSequence} is ahead of the host final sequence {hostFinalSequence}.");
		}

	}

	public static void VerifySnapshotDiscardRepair(MultiplayerEcsRunSettings settings, string? clientDirectory, MultiplayerEcsSyncReport report)
	{
		if (settings.DiscardSnapshotAtSeconds <= 0f) return;

		string peerDirectory = clientDirectory ?? settings.ResolveClientDirectory(1);
		string path = Path.Combine(peerDirectory, "snapshot_discard.txt");
		if (!File.Exists(path))
		{
			report.AddFailure("The repair scenario asked a peer to discard a snapshot, but the peer wrote no discard record.");
			return;
		}

		var fields = new Dictionary<string, string>(StringComparer.Ordinal);
		foreach (string line in File.ReadAllLines(path))
		{
			int separator = line.IndexOf('=');
			if (separator <= 0) continue;
			fields[line.Substring(0, separator)] = line.Substring(separator + 1);
		}

		if (!fields.TryGetValue("discarded", out string? discarded) || discarded != "1")
		{
			report.AddFailure($"The peer never discarded a snapshot: {fields.GetValueOrDefault("reason") ?? "no reason recorded"}.");
			return;
		}

		int discardedSequence = int.Parse(fields.GetValueOrDefault("discarded_sequence", "-1"), NumberStyles.Integer, CultureInfo.InvariantCulture);
		int baselinesAfter = int.Parse(fields.GetValueOrDefault("baseline_count_after", "0"), NumberStyles.Integer, CultureInfo.InvariantCulture);
		string baselinesAfterText = fields.GetValueOrDefault("baselines_after") ?? string.Empty;

		report.AddSummaryLine($"Repair scenario: the peer discarded snapshot {discardedSequence}, leaving one clear bit in its acknowledgement mask; the authority described it from scratch {baselinesAfter} time(s) afterwards ({baselinesAfterText}).");

		// A description the match still holds is sent again by name, which is what the acknowledgement mask is for. A
		// baseline is the fallback for a run the ring no longer has, so zero is the healthy number and one is the worst a
		// single discard may cost. Repeating one every cooldown was the defect this asserts against.
		if (baselinesAfter > 1)
		{
			report.AddFailure($"A discarded snapshot produced {baselinesAfter} baselines; a description the match still holds is meant to be sent again by name, and one baseline is the fallback rather than the first answer.");
		}

		List<string> authorityLogs = ResolveAuthorityLogs(settings.OutputDirectory);
		if (authorityLogs.Count == 0)
		{
			report.AddFailure("The authority log is missing, so the repairs it logged could not be counted.");
			return;
		}

		int repairCount = 0;
		foreach (string log in authorityLogs)
		{
			foreach (string line in File.ReadLines(log))
			{
				if (line.Contains("are sent again", StringComparison.Ordinal)) repairCount++;
			}
		}

		int repairStormAllowance = Math.Max(8, (int)(2f * settings.DurationSeconds * Math.Max(1, settings.PeerCount)));
		report.AddSummaryLine($"Repair scenario: the authority logged {repairCount} repair(s) over the run (allowed {repairStormAllowance} in total, and a storm would be one per sequence).");
		if (baselinesAfter == 0 && repairCount == 0)
		{
			report.AddFailure("The peer discarded a snapshot and the authority neither sent it again by name nor described the world from scratch, so the hole it left was never filled.");
		}

		if (repairCount > repairStormAllowance)
		{
			report.AddFailure($"The authority logged {repairCount} repairs in a {settings.DurationSeconds:F0} s run for {settings.PeerCount} peers (allowed {repairStormAllowance}); a peer missing a change is meant to be described from scratch once per cooldown, not once per sequence.");
		}
	}

	private static List<string> ResolveAuthorityLogs(string outputDirectory)
	{
		var logs = new List<string>();
		string listenServerDirectory = Path.Combine(outputDirectory, "listen_server");
		if (Directory.Exists(listenServerDirectory)) logs.AddRange(Directory.GetFiles(listenServerDirectory, "*.log"));
		if (logs.Count > 0) return logs;

		string hostLogPath = Path.Combine(outputDirectory, "host.log");
		if (File.Exists(hostLogPath)) logs.Add(hostLogPath);
		return logs;
	}

	private static string DescribeSequenceBursts(List<int> sequences)
	{
		if (sequences.Count == 0) return "none";

		int burstCount = 0;
		int longestBurst = 0;
		int runLength = 0;
		int previousSequence = -2;
		StringBuilder summary = new();

		foreach (int sequence in sequences)
		{
			if (sequence == previousSequence + 1)
			{
				runLength++;
			}
			else
			{
				burstCount++;
				if (runLength > longestBurst) longestBurst = runLength;
				runLength = 1;
			}

			previousSequence = sequence;
		}

		if (runLength > longestBurst) longestBurst = runLength;

		int listedCount = Math.Min(sequences.Count, 12);
		for (int index = 0; index < listedCount; index++)
		{
			if (index > 0) summary.Append(", ");
			summary.Append(sequences[index]);
		}

		if (sequences.Count > listedCount) summary.Append($", ... ({sequences.Count - listedCount} more)");
		return $"{burstCount} bursts, longest run {longestBurst} sequence(s), sequences {summary}";
	}

	private static bool TryReadSequence(string path, out int sequence)
	{
		sequence = -1;
		if (!File.Exists(path)) return false;
		string text = File.ReadAllText(path).Trim();
		return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out sequence);
	}

	private static float RotationDifference(float left, float right)
	{
		float difference = left - right;
		while (difference > MathF.PI) difference -= 2f * MathF.PI;
		while (difference < -MathF.PI) difference += 2f * MathF.PI;
		return MathF.Abs(difference);
	}

	private static void AddMismatch(List<string> mismatches, int mismatchCountBefore, string message)
	{
		const int maximumMismatches = 40;
		if (mismatches.Count - mismatchCountBefore >= maximumMismatches) return;
		mismatches.Add(message);
	}

	private static int MaxOf(HashSet<int> values)
	{
		int maximum = int.MinValue;
		foreach (int value in values)
		{
			if (value > maximum) maximum = value;
		}

		return maximum;
	}

	private static HashSet<int>? ReadHostSentSequences(string outputDirectory, string clientDirectory)
	{
		string peerIdPath = Path.Combine(clientDirectory, "client_peer_id.txt");
		string coveragePath = Path.Combine(outputDirectory, "host_sent_sequences.txt");
		if (!File.Exists(peerIdPath) || !File.Exists(coveragePath)) return null;
		HashSet<int> peerIds = ReadClientPeerIds(clientDirectory);
		if (peerIds.Count == 0) return null;

		var sequences = new HashSet<int>();
		foreach (string line in File.ReadLines(coveragePath))
		{
			int separator = line.IndexOf(' ');
			if (separator <= 0) continue;
			if (!int.TryParse(line.AsSpan(0, separator), NumberStyles.Integer, CultureInfo.InvariantCulture, out int sentPeerId)) continue;
			if (!peerIds.Contains(sentPeerId)) continue;
			if (int.TryParse(line.AsSpan(separator + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int sequence)) sequences.Add(sequence);
		}

		return sequences;
	}

	private static void ReportNotSentSequences(MultiplayerEcsSyncReport report, List<int> notSentSequences, int firstPairedSequence)
	{
		if (notSentSequences.Count == 0) return;

		int joinAllowanceSequence = firstPairedSequence < 0 ? 0 : firstPairedSequence + 240;
		var sustained = new List<int>();
		foreach (int sequence in notSentSequences)
		{
			if (sequence > joinAllowanceSequence) sustained.Add(sequence);
		}

		if (sustained.Count == 0)
		{
			report.AddSummaryLine($"Sequences the host never sent to this peer ({notSentSequences.Count}) all fall inside the join window, before the host had an acknowledgement to stream against.");
			return;
		}

		report.AddFailure($"the host stopped sending snapshots to this peer for {sustained.Count} sequences after the join window, starting at sequence {sustained[0]}; a peer the host no longer streams to can never converge.");
	}

	private static HashSet<int> ReadClientPeerIds(string clientDirectory)
	{
		var peerIds = new HashSet<int>();
		string path = Path.Combine(clientDirectory, "client_peer_id.txt");
		if (!File.Exists(path)) return peerIds;

		foreach (string line in File.ReadLines(path))
		{
			if (int.TryParse(line.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int peerId) && peerId > 0) peerIds.Add(peerId);
		}

		return peerIds;
	}

	private static bool ContainsAnyPeerId(HashSet<int> recordedPeerIds, HashSet<int> clientPeerIds)
	{
		foreach (int recordedPeerId in recordedPeerIds)
		{
			if (clientPeerIds.Contains(recordedPeerId)) return true;
		}

		return false;
	}

	private static HashSet<int> ReadClientSeenSequences(string outputDirectory)
	{
		var seenSequences = new HashSet<int>();
		string path = Path.Combine(outputDirectory, "ECS_client_seen_sequences.bin");
		if (!File.Exists(path)) return seenSequences;

		try
		{
			using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
			using BinaryReader reader = new(stream);
			int count = reader.ReadInt32();
			for (int index = 0; index < count; index++) seenSequences.Add(reader.ReadInt32());
		}
		catch (Exception exception)
		{
			GameLog.LogToConsole($"[MultiplayerEcsSync] Could not read seen snapshot sequences: {exception.Message}");
		}

		return seenSequences;
	}

	/// <summary>
	///     Reads a run's records, and reports the file as ending inside a record rather than failing the comparison for it.
	///     This log is written while the match runs and the process behind it is terminated where the match ends, so the final
	///     record is cut off wherever that happened: the records before it are complete, and they are what the comparison is
	///     made from. A record whose sequence does not advance is the file read against the wrong layout, which still fails.
	/// </summary>
}
