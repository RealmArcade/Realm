using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Godot;

namespace Realm.Client.Testing;

[GlobalClass]
public partial class PredictionHarnessRunner : Node
{
	private string _profile = "latency";
	private float _durationSeconds = 36f;
	private int _port = 28511;
	private string _outputDirectory = string.Empty;
	private string _mapFolder = @"c:\temp\ugc-ChaosArena";
	private string _wasmPath = string.Empty;
	private bool _negativeControl;
	private int _peerCount = 2;
	private int _botCount;
	private float _dropClientAtSeconds;

	public override void _Ready()
	{
		_ = RunAsync();
	}

	private async Task RunAsync()
	{
		int exitCode = 1;
		try
		{
			ParseArguments();
			if (string.IsNullOrEmpty(_outputDirectory)) _outputDirectory = Path.Combine(Path.GetTempPath(), "Realm_PredictionHarness");
			if (string.IsNullOrEmpty(_wasmPath)) _wasmPath = MultiplayerEcsSyncTestSupport.EnsureChaosArenaWasm(_mapFolder);

			ResolveNetworkProfile(out NetworkProfile profile, out int oneWayDelayMilliseconds, out int jitterMilliseconds, out int packetLossPercent);
			// A run that injects no delay behaves like the loopback case: prediction must converge on the next authoritative
			// sample rather than being given a window to converge in.
			bool zeroLatencyRun = oneWayDelayMilliseconds == 0 && jitterMilliseconds == 0 && packetLossPercent == 0;
			MultiplayerEcsRunSettings settings = new()
			{
				GodotBinary = MultiplayerEcsSyncTestSupport.ResolveGodotBinary(),
				ProjectDirectory = ProjectSettings.GlobalizePath("res://"),
				MapFolder = _mapFolder,
				WasmPath = _wasmPath,
				OutputDirectory = _outputDirectory,
				DurationSeconds = _durationSeconds,
				Port = _port,
				Profile = profile,
				PredictionEnabled = !_negativeControl,
				RunPredictionScenario = true,
				PeerCount = _peerCount,
				BotCount = _botCount,
				DropClientAtSeconds = _dropClientAtSeconds,
				OneWayDelayMilliseconds = oneWayDelayMilliseconds,
				JitterMilliseconds = jitterMilliseconds,
				PacketLossPercent = packetLossPercent
			};

			GD.Print($"[PREDICTION_HARNESS] Running profile {profile} with {_peerCount} peers, {_botCount} bot(s), negativeControl={_negativeControl} dropAt={_dropClientAtSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture)}s on port {_port} for {_durationSeconds:F0}s.");
			await MultiplayerEcsSyncTestSupport.RunAsync(settings);

			IReadOnlyList<string> clientDirectories = settings.ResolveClientDirectories();
			PredictionHarnessReport? firstPredictionReport = null;
			int predictionFailures = 0;
			int syncFailures = 0;
			for (int index = 0; index < clientDirectories.Count; index++)
			{
				string clientDirectory = clientDirectories[index];
				int peerId = index + 2;
				string suffix = index == 0 ? string.Empty : $"_peer{peerId}";

				PredictionHarnessReport predictionReport = PredictionHarnessVerification.Verify(settings, clientDirectory);
				firstPredictionReport ??= predictionReport;
				predictionFailures += predictionReport.Failures;
				string predictionText = predictionReport.RenderText();
				File.WriteAllText(Path.Combine(_outputDirectory, $"prediction_report{suffix}.txt"), predictionText);
				File.WriteAllText(Path.Combine(_outputDirectory, $"prediction_report{suffix}.json"), predictionReport.RenderJson());
				GD.Print($"[PREDICTION_HARNESS] === Peer {peerId} prediction report ===");
				GD.Print(predictionText);

				if (!zeroLatencyRun)
				{
					MultiplayerEcsSyncReport syncReport = MultiplayerEcsSyncTestSupport.Verify(settings, clientDirectory);
					string syncRendered = syncReport.Render();
					File.WriteAllText(Path.Combine(_outputDirectory, $"multiplayer_ecs_sync_report{suffix}.txt"), syncRendered);
					GD.Print($"[PREDICTION_HARNESS] === Peer {peerId} sync report ===");
					GD.Print(syncRendered);
					syncFailures += syncReport.Failures.Count;
				}
			}

			if (_negativeControl)
			{
				exitCode = WriteNegativeControlVerdict(firstPredictionReport!);
				return;
			}

			int hardeningFailures;
			int bandwidthFailures;
			int splitFailures;
			// Every check runs on every profile. Skipping them on a clean run made the clean profile the one that proved the
			// least, and the verdict's failure counts then disagreed with the reports it was reading.
			hardeningFailures = ReadHardeningFailures(_outputDirectory);
			bandwidthFailures = CheckBandwidthBudget(_outputDirectory);
			splitFailures = CheckSplitMatch(_outputDirectory, clientDirectories);
			bool passed = predictionFailures == 0 && syncFailures == 0 && hardeningFailures == 0 && bandwidthFailures == 0 && splitFailures == 0;
			exitCode = passed ? 0 : 1;
			WriteVerdictReport(settings, exitCode, predictionFailures, syncFailures, hardeningFailures, bandwidthFailures, splitFailures);
			GD.Print($"[PREDICTION_HARNESS] Verdict: {(passed ? "PASS" : "FAIL")} across {clientDirectories.Count + 1} peers (sync failures {syncFailures}, prediction failures {predictionFailures}, hardening failures {hardeningFailures}, bandwidth failures {bandwidthFailures}, split failures {splitFailures}).");
		}
		catch (Exception exception)
		{
			GD.PrintErr($"[PREDICTION_HARNESS] Unhandled failure: {exception}");
		}
		finally
		{
			GetTree().Quit(exitCode);
		}
	}

	/// <summary>
	/// Writes one machine-readable verdict for the whole run, so a script does not have to join four reports and an exit
	/// code to know what happened. Every check that can fail contributes its own count and its own messages, and the exit
	/// code the process actually returns is recorded alongside them so the artifact and the exit status cannot disagree.
	/// </summary>
	/// <summary>
	/// Compares what every process in the match holds and what the run's own split checks reported. A match only works if
	/// a change the authority made reaches every screen, so disagreement between two screens is a failure even when each
	/// screen is internally consistent.
	/// </summary>
	private int CheckSplitMatch(string outputDirectory, IReadOnlyList<string> clientDirectories)
	{
		SplitWorldReport worldReport = MultiplayerSplitVerification.Verify(outputDirectory, clientDirectories);
		string worldText = worldReport.Render();
		File.WriteAllText(Path.Combine(outputDirectory, "split_world_report.txt"), worldText);
		GD.Print(worldText);

		int scenarioFailures = ReadReportFailures("split", Path.Combine(outputDirectory, "split_report.json"), out string scenarioText);
		if (scenarioText.Length > 0) GD.Print(scenarioText);
		return worldReport.Failures.Count + scenarioFailures;
	}

	/// <summary>Reads the checks the host harness ran while the match was being played.</summary>
	private static int ReadReportFailures(string name, string path, out string rendered)
	{
		rendered = string.Empty;
		if (!File.Exists(path))
		{
			GD.PrintErr($"[PREDICTION_HARNESS] {path} is missing, so the {name} checks did not run.");
			return 1;
		}

		try
		{
			using System.Text.Json.JsonDocument document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
			System.Text.Json.JsonElement root = document.RootElement;
			int failures = root.TryGetProperty("failures", out System.Text.Json.JsonElement failuresElement) ? failuresElement.GetInt32() : 0;

			var builder = new System.Text.StringBuilder();
			builder.AppendLine($"=== {name} checks ===");
			if (root.TryGetProperty("notes", out System.Text.Json.JsonElement notes) && notes.ValueKind == System.Text.Json.JsonValueKind.Array)
			{
				foreach (System.Text.Json.JsonElement note in notes.EnumerateArray()) builder.AppendLine($"  note: {note.GetString()}");
			}

			if (root.TryGetProperty("messages", out System.Text.Json.JsonElement messages) && messages.ValueKind == System.Text.Json.JsonValueKind.Array)
			{
				foreach (System.Text.Json.JsonElement message in messages.EnumerateArray())
				{
					builder.AppendLine($"  - {message.GetString()}");
					GD.PrintErr($"[PREDICTION_HARNESS] {name}: {message.GetString()}");
				}
			}

			builder.AppendLine($"Failures: {failures}");
			rendered = builder.ToString();
			return failures;
		}
		catch (Exception exception)
		{
			GD.PrintErr($"[PREDICTION_HARNESS] Could not read {path}: {exception.Message}");
			return 1;
		}
	}

	private void WriteVerdictReport(MultiplayerEcsRunSettings settings, int exitCode, int predictionFailures, int syncFailures, int hardeningFailures, int bandwidthFailures, int splitFailures)
	{
		try
		{
			var checks = new List<object>();
			var failureMessages = new List<string>();
			// The reports are written to the run directory under the same per-peer suffix, so the verdict has to read them
			// from there. Reading them from each client's record directory found nothing and labelled every peer after the
			// first as missing, which hid their failures behind a present=false entry.
			for (int clientIndex = 0; clientIndex < Math.Max(1, _peerCount - 1); clientIndex++)
			{
				string suffix = clientIndex == 0 ? string.Empty : $"_peer{clientIndex + 2}";
				CollectReport(checks, failureMessages, $"prediction{suffix}", Path.Combine(_outputDirectory, $"prediction_report{suffix}.json"));
			}

			CollectReport(checks, failureMessages, "hardening", Path.Combine(_outputDirectory, "hardening_report.json"));
			CollectReport(checks, failureMessages, "split", Path.Combine(_outputDirectory, "split_report.json"));
			checks.Add(new { name = "sync", present = true, failures = syncFailures });
			checks.Add(new { name = "splitWorld", present = true, failures = splitFailures });
				var payload = new
				{
					profile = _profile,
				peers = _peerCount,
				loopback = settings.OneWayDelayMilliseconds == 0 && settings.JitterMilliseconds == 0 && settings.PacketLossPercent == 0,
				durationSeconds = _durationSeconds,
				predictionFailures,
				syncFailures,					hardeningFailures,
					bandwidthFailures,
					splitFailures,
					exitCode,
				verdict = exitCode == 0 ? "PASS" : "FAIL",
				checks,
				failureMessages
			};

			File.WriteAllText(Path.Combine(_outputDirectory, "verdict.json"), System.Text.Json.JsonSerializer.Serialize(payload, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
		}
		catch (Exception exception)
		{
			GD.PrintErr($"[PREDICTION_HARNESS] Could not write verdict.json: {exception.Message}");
		}
	}

	private static void CollectReport(List<object> checks, List<string> failureMessages, string name, string path)
	{
		if (!File.Exists(path))
		{
			checks.Add(new { name, present = false });
			failureMessages.Add($"{name}: the report {Path.GetFileName(path)} is missing");
			return;
		}

		try
		{
			using System.Text.Json.JsonDocument document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
			System.Text.Json.JsonElement root = document.RootElement;
			int failures = root.TryGetProperty("failures", out System.Text.Json.JsonElement failuresElement) ? failuresElement.GetInt32() : 0;
			if (root.TryGetProperty("failures", out _) && root.TryGetProperty("failureMessages", out System.Text.Json.JsonElement messagesElement) && messagesElement.ValueKind == System.Text.Json.JsonValueKind.Array)
			{
				foreach (System.Text.Json.JsonElement message in messagesElement.EnumerateArray()) failureMessages.Add($"{name}: {message.GetString()}");
			}

			if (root.TryGetProperty("failureMessages", out System.Text.Json.JsonElement altMessages) && altMessages.ValueKind == System.Text.Json.JsonValueKind.Array && !root.TryGetProperty("failures", out _))
			{
				foreach (System.Text.Json.JsonElement message in altMessages.EnumerateArray()) failureMessages.Add($"{name}: {message.GetString()}");
			}

			checks.Add(new { name, present = true, failures });
		}
		catch (Exception exception)
		{
			checks.Add(new { name, present = false });
			failureMessages.Add($"{name}: {Path.GetFileName(path)} could not be read ({exception.Message})");
		}
	}

	private const double ClientBandwidthBudgetBytesPerSecond = 40000;
	private const double ClientBandwidthRegressionCeilingBytesPerSecond = 450000;

	private static int CheckBandwidthBudget(string outputDirectory)
	{
		string path = Path.Combine(outputDirectory, "bandwidth_report.json");
		if (!File.Exists(path))
		{
			GD.PrintErr($"[PREDICTION_HARNESS] {path} is missing, so per-client bandwidth was not measured.");
			return 1;
		}

		try
		{
			using System.Text.Json.JsonDocument document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
			double worstBytesPerSecond = document.RootElement.GetProperty("worstClientBytesPerSecond").GetDouble();
			double hostTotalBytesPerSecond = document.RootElement.GetProperty("hostTotalBytesPerSecond").GetDouble();
			int peerCount = document.RootElement.GetProperty("peers").GetArrayLength();
			double budgetRatio = worstBytesPerSecond / ClientBandwidthBudgetBytesPerSecond;
			GD.Print($"[PREDICTION_HARNESS] Bandwidth: worst client {worstBytesPerSecond:F0} B/s, host total {hostTotalBytesPerSecond:F0} B/s across {peerCount} client peer(s). Target {ClientBandwidthBudgetBytesPerSecond:F0} B/s per client, measured {budgetRatio:F1}x of it; regression ceiling {ClientBandwidthRegressionCeilingBytesPerSecond:F0} B/s.");
			if (worstBytesPerSecond > ClientBandwidthRegressionCeilingBytesPerSecond)
			{
				GD.PrintErr($"[PREDICTION_HARNESS] A client received {worstBytesPerSecond:F0} B/s of snapshots, above the {ClientBandwidthRegressionCeilingBytesPerSecond:F0} B/s regression ceiling.");
				return 1;
			}

			return 0;
		}
		catch (Exception exception)
		{
			GD.PrintErr($"[PREDICTION_HARNESS] Could not read {path}: {exception.Message}");
			return 1;
		}
	}

	private static int ReadHardeningFailures(string outputDirectory)
	{
		string path = Path.Combine(outputDirectory, "hardening_report.json");
		if (!File.Exists(path))
		{
			GD.PrintErr($"[PREDICTION_HARNESS] {path} is missing, so the hardening checks did not run.");
			return -1;
		}

		try
		{
			using System.Text.Json.JsonDocument document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
			return document.RootElement.GetProperty("failures").GetInt32();
		}
		catch (Exception exception)
		{
			GD.PrintErr($"[PREDICTION_HARNESS] Could not read {path}: {exception.Message}");
			return -1;
		}
	}

	private int WriteNegativeControlVerdict(PredictionHarnessReport report)
	{
		bool reproduced = report.Failures > 0 && report.FailureMessages.Exists(message => message.Contains("predict", StringComparison.OrdinalIgnoreCase));
		var builder = new System.Text.StringBuilder();
		builder.AppendLine("=== Prediction negative control ===");
		builder.AppendLine("Prediction was disabled for this run. The harness must reproduce the input-latency defect, so a failing prediction verdict is the expected outcome.");
		builder.AppendLine($"Prediction failures observed: {report.Failures}");
		foreach (string message in report.FailureMessages) builder.AppendLine($"  - {message}");
		builder.AppendLine($"Verdict: {(reproduced ? "PASS" : "FAIL")} (the defect was {(reproduced ? "reproduced" : "not reproduced, so the harness cannot detect a missing prediction path")}).");
		string text = builder.ToString();
		File.WriteAllText(Path.Combine(_outputDirectory, "negative_control_report.txt"), text);
		GD.Print(text);
		return reproduced ? 0 : 1;
	}

	private void ParseArguments()
	{
		foreach (string argument in OS.GetCmdlineUserArgs())
		{
			int separatorIndex = argument.IndexOf('=');
			if (separatorIndex <= 0) continue;

			string key = argument.Substring(0, separatorIndex);
			string value = argument.Substring(separatorIndex + 1);
			switch (key)
			{
				case "--profile":
					_profile = value.ToLowerInvariant();
					break;
				case "--duration":
					_durationSeconds = float.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
					break;
				case "--port":
					_port = int.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
					break;
				case "--out":
					_outputDirectory = value;
					break;
				case "--map":
					_mapFolder = value;
					break;
				case "--wasm":
					_wasmPath = value;
					break;
				case "--negative-control":
					_negativeControl = value == "1";
					break;
				case "--peers":
					_peerCount = int.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
					break;
				case "--bots":
					_botCount = int.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
					break;
				case "--drop-at":
					_dropClientAtSeconds = float.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
					break;
			}
		}
	}

	private void ResolveNetworkProfile(out NetworkProfile profile, out int oneWayDelayMilliseconds, out int jitterMilliseconds, out int packetLossPercent)
	{
		switch (_profile)
		{
			case "clean":
				profile = NetworkProfile.Clean;
				oneWayDelayMilliseconds = 0;
				jitterMilliseconds = 0;
				packetLossPercent = 0;
				break;
			case "latency":
				profile = NetworkProfile.Latency;
				oneWayDelayMilliseconds = 150;
				jitterMilliseconds = 0;
				packetLossPercent = 0;
				break;
			case "latency-jitter":
				profile = NetworkProfile.LatencyJitter;
				oneWayDelayMilliseconds = 120;
				jitterMilliseconds = 40;
				packetLossPercent = 0;
				break;
			case "latency-loss":
				profile = NetworkProfile.LatencyLoss;
				oneWayDelayMilliseconds = 120;
				jitterMilliseconds = 25;
				packetLossPercent = 3;
				break;
			default:
				throw new ArgumentOutOfRangeException(nameof(_profile), $"Unknown network profile '{_profile}'.");
		}
	}
}
