using System;
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
				OneWayDelayMilliseconds = oneWayDelayMilliseconds,
				JitterMilliseconds = jitterMilliseconds,
				PacketLossPercent = packetLossPercent
			};

			GD.Print($"[PREDICTION_HARNESS] Running profile {profile} loopback={_loopback} negativeControl={_negativeControl} on port {_port} for {_durationSeconds:F0}s.");
			await MultiplayerEcsSyncTestSupport.RunAsync(settings);

			PredictionHarnessReport predictionReport = PredictionHarnessVerification.Verify(settings);
			string predictionText = predictionReport.RenderText();
			File.WriteAllText(Path.Combine(_outputDirectory, "prediction_report.txt"), predictionText);
			File.WriteAllText(Path.Combine(_outputDirectory, "prediction_report.json"), predictionReport.RenderJson());
			GD.Print(predictionText);

			if (_negativeControl)
			{
				exitCode = WriteNegativeControlVerdict(predictionReport);
				return;
			}

			int syncFailures = 0;
			if (!_loopback)
			{
				MultiplayerEcsSyncReport syncReport = MultiplayerEcsSyncTestSupport.Verify(settings);
				string syncRendered = syncReport.Render();
				File.WriteAllText(Path.Combine(_outputDirectory, "multiplayer_ecs_sync_report.txt"), syncRendered);
				GD.Print(syncRendered);
				syncFailures = syncReport.Failures.Count;
			}

			bool passed = predictionReport.Failures == 0 && syncFailures == 0;
			exitCode = passed ? 0 : 1;
			GD.Print($"[PREDICTION_HARNESS] Verdict: {(passed ? "PASS" : "FAIL")} (sync failures {syncFailures}, prediction failures {predictionReport.Failures}).");
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
