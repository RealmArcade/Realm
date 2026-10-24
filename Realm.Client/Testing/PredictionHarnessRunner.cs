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
	private float _durationSeconds = 30f;
	private int _port = 28511;
	private string _outputDirectory = string.Empty;
	private string _mapFolder = @"c:\temp\ugc-ChaosArena";
	private string _wasmPath = string.Empty;
	private bool _loopback;
	private bool _negativeControl;
	private int _peerCount = 2;

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
				Loopback = _loopback,
				PeerCount = _peerCount,
				OneWayDelayMilliseconds = oneWayDelayMilliseconds,
				JitterMilliseconds = jitterMilliseconds,
				PacketLossPercent = packetLossPercent
			};

			GD.Print($"[PREDICTION_HARNESS] Running profile {profile} with {_peerCount} peers loopback={_loopback} negativeControl={_negativeControl} on port {_port} for {_durationSeconds:F0}s.");
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

				if (!_loopback)
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

			int hardeningFailures = ReadHardeningFailures(_outputDirectory);
			bool passed = predictionFailures == 0 && syncFailures == 0 && hardeningFailures == 0;
			exitCode = passed ? 0 : 1;
			GD.Print($"[PREDICTION_HARNESS] Verdict: {(passed ? "PASS" : "FAIL")} across {clientDirectories.Count + 1} peers (sync failures {syncFailures}, prediction failures {predictionFailures}, hardening failures {hardeningFailures}).");
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
				case "--loopback":
					_loopback = value == "1";
					break;
				case "--negative-control":
					_negativeControl = value == "1";
					break;
				case "--peers":
					_peerCount = int.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
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
