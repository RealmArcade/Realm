using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Realm.Client.Services;
using Realm.Ecs.Replication;

namespace Realm.Client.Testing;

public sealed class MultiplayerEcsRunSettings
{
	public string GodotBinary { get; init; } = string.Empty;
	public string ProjectDirectory { get; init; } = string.Empty;
	public string MapFolder { get; init; } = string.Empty;
	public string WasmPath { get; init; } = string.Empty;
	public string OutputDirectory { get; init; } = string.Empty;
	public float DurationSeconds { get; init; } = 15f;
	public int Port { get; init; } = 8999;
	public int PingMilliseconds { get; init; }
	public int DrainMilliseconds => Math.Max(1500, PingMilliseconds * 4);
}

public sealed class MultiplayerEcsSyncReport
{
	private readonly StringBuilder _summary = new();
	private readonly List<string> _failures = new();

	public IReadOnlyList<string> Failures => _failures;
	public int PairedSnapshotCount { get; set; }
	public int ComparedUnitCount { get; set; }
	public int HostSnapshotCount { get; set; }
	public int ClientSnapshotCount { get; set; }

	public void AddSummaryLine(string line) => _summary.AppendLine(line);
	public void AddFailure(string failure) => _failures.Add(failure);

	public string Render()
	{
		StringBuilder builder = new();
		builder.AppendLine("=== Multiplayer ECS sync verification ===");
		builder.Append(_summary.ToString());
		builder.AppendLine($"Host snapshots: {HostSnapshotCount}");
		builder.AppendLine($"Client snapshots: {ClientSnapshotCount}");
		builder.AppendLine($"Paired snapshots: {PairedSnapshotCount}");
		builder.AppendLine($"Compared units: {ComparedUnitCount}");
		builder.AppendLine($"Failures: {_failures.Count}");
		foreach (string failure in _failures)
		{
			builder.AppendLine($"  - {failure}");
		}

		return builder.ToString();
	}
}

public static class MultiplayerEcsSyncTestSupport
{
	private const float BaselineTolerance = 0.0005f;
	private const float DeltaPositionTolerance = 1.05f;
	private const float DeltaRotationTolerance = 0.17f;
	private const float DeltaHealthTolerance = 1.05f;
	private const float PresentationPositionTolerance = 0.25f;
	private const float PresentationRotationTolerance = 0.2f;
	private const int StartupWarmupSequenceCount = 60;
	private const int SyncRecordVersion = 3;

	public static string ResolveGodotBinary()
	{
		string? fromEnvironment = System.Environment.GetEnvironmentVariable("GODOT_BIN");
		if (!string.IsNullOrEmpty(fromEnvironment) && File.Exists(fromEnvironment)) return fromEnvironment;

		const string fallback = @"C:\Program Files\Godot_v4.7-stable_mono_win64\Godot_v4.7-stable_mono_win64.exe";
		if (File.Exists(fallback)) return fallback;

		throw new FileNotFoundException("Godot binary was not found. Set the GODOT_BIN environment variable.");
	}

	public static string EnsureChaosArenaWasm(string mapFolder)
	{
		string projectPath = Path.Combine(mapFolder, "MapScript.csproj");
		if (!File.Exists(projectPath)) throw new FileNotFoundException($"Chaos Arena map script project was not found at {projectPath}.");

		string wasmPath = Path.Combine(mapFolder, "bin", "Release", "net10.0", "wasi-wasm", "publish", "MapScript.wasm");
		string mapScriptPath = Path.Combine(mapFolder, "MapScript.cs");
		if (File.Exists(wasmPath) && File.GetLastWriteTimeUtc(wasmPath) >= File.GetLastWriteTimeUtc(mapScriptPath)) return wasmPath;

		string wasiSdkPath = WasiSdkResolver.ResolveWasiSdkPath();
		var startInfo = new ProcessStartInfo("dotnet")
		{
			WorkingDirectory = mapFolder,
			UseShellExecute = false,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			CreateNoWindow = true
		};
		startInfo.ArgumentList.Add("publish");
		startInfo.ArgumentList.Add("MapScript.csproj");
		startInfo.ArgumentList.Add("-c");
		startInfo.ArgumentList.Add("Release");
		startInfo.ArgumentList.Add("-r");
		startInfo.ArgumentList.Add("wasi-wasm");
		startInfo.ArgumentList.Add($"-p:WASI_SDK_PATH={wasiSdkPath}");
		startInfo.EnvironmentVariables["WASI_SDK_PATH"] = wasiSdkPath;

		using Process process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start the Chaos Arena WASM build.");
		string output = process.StandardOutput.ReadToEnd();
		string error = process.StandardError.ReadToEnd();
		process.WaitForExit();
		if (process.ExitCode != 0 || !File.Exists(wasmPath))
		{
			throw new InvalidOperationException($"Chaos Arena WASM build failed (exit code {process.ExitCode}).\n{output}\n{error}");
		}

		return wasmPath;
	}

	public static async Task RunAsync(MultiplayerEcsRunSettings settings)
	{
		Directory.CreateDirectory(settings.OutputDirectory);
		foreach (string file in Directory.GetFiles(settings.OutputDirectory))
		{
			File.Delete(file);
		}

		string hostListeningFile = Path.Combine(settings.OutputDirectory, "host_listening.txt");
		string hostReadyFile = Path.Combine(settings.OutputDirectory, "host_ready.txt");
		string clientReadyFile = Path.Combine(settings.OutputDirectory, "client_ready.txt");
		string hostDoneFile = Path.Combine(settings.OutputDirectory, "host_done.txt");
		string clientSequenceFile = Path.Combine(settings.OutputDirectory, "client_final_sequence.txt");

		using UdpDelayRelay? relay = settings.PingMilliseconds > 0 ? new UdpDelayRelay(settings.Port, settings.PingMilliseconds) : null;
		int clientConnectionPort = relay?.ListenPort ?? settings.Port;

		using HarnessProcess hostProcess = StartHarnessProcess(settings, "host", settings.Port, null, hostReadyFile, hostDoneFile);
		bool hostListening = await WaitForFileOrExitAsync(hostListeningFile, hostProcess.Process, TimeSpan.FromSeconds(120));
		if (!hostListening) throw new InvalidOperationException($"Host harness did not start listening.\n{hostProcess.ReadLog()}");

		using HarnessProcess clientProcess = StartHarnessProcess(settings, "client", clientConnectionPort, clientSequenceFile, clientReadyFile, hostDoneFile);
		bool clientReady = await WaitForFileOrExitAsync(clientReadyFile, clientProcess.Process, TimeSpan.FromSeconds(180));
		if (!clientReady) throw new InvalidOperationException($"Client harness did not become ready.\n{clientProcess.ReadLog()}");

		bool hostReady = await WaitForFileOrExitAsync(hostReadyFile, hostProcess.Process, TimeSpan.FromSeconds(180));
		if (!hostReady) throw new InvalidOperationException($"Host harness did not become ready.\n{hostProcess.ReadLog()}");

		TimeSpan shutdownWindow = TimeSpan.FromSeconds(settings.DurationSeconds * 4 + 300);
		bool hostExited = await WaitForExitAsync(hostProcess.Process, shutdownWindow);
		bool clientExited = await WaitForExitAsync(clientProcess.Process, shutdownWindow);

		if (!hostExited) hostProcess.Process.Kill(true);
		if (!clientExited) clientProcess.Process.Kill(true);

		if (!hostExited) throw new InvalidOperationException($"Host harness did not finish in time.\n{hostProcess.ReadLog()}");
		if (!clientExited) throw new InvalidOperationException($"Client harness did not finish in time.\n{clientProcess.ReadLog()}");

		if (hostProcess.Process.ExitCode != 0)
		{
			throw new InvalidOperationException($"Host harness exited with code {hostProcess.Process.ExitCode}.\n{hostProcess.ReadLog()}");
		}

		if (clientProcess.Process.ExitCode != 0)
		{
			throw new InvalidOperationException($"Client harness exited with code {clientProcess.Process.ExitCode}.\n{clientProcess.ReadLog()}");
		}

		if (relay != null)
		{
			File.WriteAllText(
				Path.Combine(settings.OutputDirectory, "relay_stats.txt"),
				$"forwarded={relay.ForwardedDatagramCount}\ndropped={relay.DroppedDatagramCount}");
		}
	}

	private static HarnessProcess StartHarnessProcess(MultiplayerEcsRunSettings settings, string role, int connectionPort, string? sequenceFile, string readyFile, string doneFile)
	{
		var startInfo = new ProcessStartInfo(settings.GodotBinary)
		{
			WorkingDirectory = settings.ProjectDirectory,
			UseShellExecute = false,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			CreateNoWindow = true
		};
		startInfo.ArgumentList.Add("--headless");
		startInfo.ArgumentList.Add("--path");
		startInfo.ArgumentList.Add(settings.ProjectDirectory);
		startInfo.ArgumentList.Add("res://Testing/MultiplayerSimulationHarness.tscn");
		startInfo.ArgumentList.Add("--");
		startInfo.ArgumentList.Add($"--role={role}");
		startInfo.ArgumentList.Add($"--host=127.0.0.1");
		startInfo.ArgumentList.Add($"--port={connectionPort}");
		startInfo.ArgumentList.Add($"--map={settings.MapFolder}");
		startInfo.ArgumentList.Add($"--wasm={settings.WasmPath}");
		startInfo.ArgumentList.Add($"--output={settings.OutputDirectory}");
		startInfo.ArgumentList.Add($"--listening-file={Path.Combine(settings.OutputDirectory, "host_listening.txt")}");
		startInfo.ArgumentList.Add($"--ready-file={readyFile}");
		startInfo.ArgumentList.Add($"--done-file={doneFile}");
		startInfo.ArgumentList.Add($"--sequence-file={sequenceFile}");
		startInfo.ArgumentList.Add($"--drain-ms={settings.DrainMilliseconds}");
		startInfo.ArgumentList.Add($"--duration={settings.DurationSeconds.ToString(CultureInfo.InvariantCulture)}");

		HarnessProcess harnessProcess = new(Path.Combine(settings.OutputDirectory, $"{role}.log"));
		Process process = harnessProcess.Process;
		process.StartInfo = startInfo;
		process.OutputDataReceived += (_, args) =>
		{
			if (args.Data == null) return;
			harnessProcess.AppendLog(args.Data);
		};
		process.ErrorDataReceived += (_, args) =>
		{
			if (args.Data == null) return;
			harnessProcess.AppendLog($"[stderr] {args.Data}");
		};

		process.Start();
		process.BeginOutputReadLine();
		process.BeginErrorReadLine();
		return harnessProcess;
	}

	private sealed class HarnessProcess : IDisposable
	{
		private readonly StringBuilder _logBuilder = new();
		private readonly object _logLock = new();
		private readonly string _logPath;

		public HarnessProcess(string logPath)
		{
			_logPath = logPath;
			Process = new Process();
		}

		public Process Process { get; }

		public void AppendLog(string line)
		{
			lock (_logLock) _logBuilder.AppendLine(line);
		}

		public string ReadLog()
		{
			lock (_logLock) return _logBuilder.ToString();
		}

		public void Dispose()
		{
			try
			{
				lock (_logLock) File.WriteAllText(_logPath, _logBuilder.ToString());
			}
			catch
			{
			}

			Process.Dispose();
		}
	}

	private static async Task<bool> WaitForFileOrExitAsync(string filePath, Process process, TimeSpan timeout)
	{
		Stopwatch stopwatch = Stopwatch.StartNew();
		while (stopwatch.Elapsed < timeout)
		{
			if (File.Exists(filePath)) return true;
			if (process.HasExited) return false;
			await Task.Delay(200);
		}

		return false;
	}

	private static async Task<bool> WaitForExitAsync(Process process, TimeSpan timeout)
	{
		Task exitTask = process.WaitForExitAsync();
		Task completed = await Task.WhenAny(exitTask, Task.Delay(timeout));
		return completed == exitTask;
	}

	public static MultiplayerEcsSyncReport Verify(MultiplayerEcsRunSettings settings)
	{
		MultiplayerEcsSyncReport report = new();
		string outputDirectory = settings.OutputDirectory;
		AppendConvergenceReport(report, settings);
		string hostPath = Path.Combine(outputDirectory, "ECS_host_sync.bin");
		string clientPath = Path.Combine(outputDirectory, "ECS_client_sync.bin");

		if (!File.Exists(hostPath) || !File.Exists(clientPath))
		{
			report.AddFailure("Sync record files are missing. Host or client never started recording.");
			return report;
		}

		SortedDictionary<int, MultiplayerSnapshotRecord> hostRecords = ReadRecords(hostPath);
		SortedDictionary<int, MultiplayerSnapshotRecord> clientRecords = ReadRecords(clientPath);
		report.HostSnapshotCount = hostRecords.Count;
		report.ClientSnapshotCount = clientRecords.Count;

		if (hostRecords.Count == 0 || clientRecords.Count == 0)
		{
			report.AddFailure("No sync records were produced.");
			return report;
		}

		HashSet<int> clientSeenSequences = ReadClientSeenSequences(outputDirectory);
		TryReadSequence(Path.Combine(outputDirectory, "client_final_sequence.txt"), out int clientFinalAppliedSequence);
		int inFlightSequenceCount = 0;
		int supersededSequenceCount = 0;

		int firstPairedSequence = -1;
		List<int> missingBaselineSequences = new();
		List<string> mismatches = new();
		int pairedSnapshots = 0;
		int comparedUnits = 0;
		int offscreenMissingUnits = 0;
		int lastPairedSequence = -1;
		int unpairedSequenceCount = 0;
		List<int> lostSequences = new();
		List<int> startupCatchUpSequences = new();
		List<string> transientDigestMismatches = new();
		int transientDigestMismatchCount = 0;
		int comparedLootEntities = 0;

		foreach (KeyValuePair<int, MultiplayerSnapshotRecord> hostEntry in hostRecords)
		{
			if (!clientRecords.TryGetValue(hostEntry.Key, out MultiplayerSnapshotRecord? clientRecord))
			{
				if (clientFinalAppliedSequence >= 0 && hostEntry.Key > clientFinalAppliedSequence)
				{
					inFlightSequenceCount++;
					continue;
				}

				if (firstPairedSequence >= 0)
				{
					unpairedSequenceCount++;
					if (clientSeenSequences.Contains(hostEntry.Key)) supersededSequenceCount++;
					else if (hostEntry.Key <= firstPairedSequence + StartupWarmupSequenceCount) startupCatchUpSequences.Add(hostEntry.Key);
					else lostSequences.Add(hostEntry.Key);
					if (hostEntry.Value.IsBaseline) missingBaselineSequences.Add(hostEntry.Key);
				}

				continue;
			}

			if (firstPairedSequence < 0) firstPairedSequence = hostEntry.Key;
			lastPairedSequence = hostEntry.Key;
			pairedSnapshots++;

			if (hostEntry.Value.IsBaseline != clientRecord.IsBaseline)
			{
				AddMismatch(mismatches, mismatches.Count, $"seq {hostEntry.Key}: baseline flag host={hostEntry.Value.IsBaseline} client={clientRecord.IsBaseline}.");
			}

			List<string> digestTarget = hostEntry.Value.IsBaseline ? mismatches : transientDigestMismatches;
			int digestMismatchCountBefore = transientDigestMismatches.Count;
			if (hostEntry.Value.IsBaseline) comparedLootEntities += hostEntry.Value.Loot.Count;
			CompareSnapshotRecord(hostEntry.Value, clientRecord, mismatches, digestTarget, ref comparedUnits, ref offscreenMissingUnits);
			CompareScopeDigests(hostEntry.Value, clientRecord, digestTarget);
			transientDigestMismatchCount += transientDigestMismatches.Count - digestMismatchCountBefore;
		}

		report.PairedSnapshotCount = pairedSnapshots;
		report.ComparedUnitCount = comparedUnits;
		report.AddSummaryLine($"First paired sequence: {firstPairedSequence}");
		report.AddSummaryLine($"Last paired sequence: {lastPairedSequence}");
		int lostSequenceCount = lostSequences.Count;
		report.AddSummaryLine($"Host sequences missing on client after first pairing: {unpairedSequenceCount} (superseded by a newer snapshot: {supersededSequenceCount}, startup catch-up: {startupCatchUpSequences.Count}, never received after startup: {lostSequenceCount})");
		report.AddSummaryLine($"Snapshots still in flight when the client stopped recording: {inFlightSequenceCount} (after client sequence {clientFinalAppliedSequence})");
		report.AddSummaryLine($"Missing baseline sequences: {(missingBaselineSequences.Count == 0 ? "none" : string.Join(", ", missingBaselineSequences))}");
		report.AddSummaryLine($"Startup catch-up sequences lost while the client applied its first baseline: {startupCatchUpSequences.Count} of the first {StartupWarmupSequenceCount} sequences ({DescribeSequenceBursts(startupCatchUpSequences)})");
		report.AddSummaryLine($"Lost sequence bursts after startup: {DescribeSequenceBursts(lostSequences)}");
		report.AddSummaryLine($"Off-screen host units absent on the client as expected: {offscreenMissingUnits}");
		report.AddSummaryLine($"Loot entities compared at baselines: {comparedLootEntities}");
		report.AddSummaryLine($"Differences seen only on delta snapshots and healed by the next baseline: {transientDigestMismatchCount}");
		for (int index = 0; index < Math.Min(transientDigestMismatches.Count, 5); index++)
		{
			report.AddSummaryLine($"  {transientDigestMismatches[index]}");
		}

		if (pairedSnapshots < 3) report.AddFailure($"Only {pairedSnapshots} snapshot sequences were paired between host and client.");
		if (comparedUnits < 20) report.AddFailure($"Only {comparedUnits} unit states were compared, which is not enough to validate sync.");
		if (missingBaselineSequences.Count > 0)
		{
			report.AddFailure($"{missingBaselineSequences.Count} baseline snapshots produced by the host were never applied by the client.");
		}

		int tolerance = Math.Max(10, pairedSnapshots / 20);
		if (lostSequenceCount > tolerance)
		{
			report.AddFailure($"{lostSequenceCount} host snapshot sequences never reached the client after startup (allowed {tolerance}); {supersededSequenceCount} more were received but superseded by a newer snapshot. Per-tick snapshots are several kilobytes, so they span multiple unreliable fragments and any lost fragment drops the whole snapshot; only a spike far above that, or a lost baseline, means the transport regressed.");
		}
		else if (lostSequenceCount > 0)
		{
			report.AddSummaryLine($"Unreliable snapshot loss stayed within the fragmented-channel tolerance: {lostSequenceCount} of {pairedSnapshots + lostSequenceCount} sequences ({100.0 * lostSequenceCount / Math.Max(1, pairedSnapshots + lostSequenceCount):F1}%) were lost, all repaired by later snapshots and baselines.");
		}
		else
		{
			report.AddSummaryLine($"No unreliable snapshot was lost after startup; every host sequence in the steady-state window reached the client and {supersededSequenceCount} were superseded by a newer snapshot.");
		}

		if (mismatches.Count > 0)
		{
			report.AddFailure($"{mismatches.Count} synced state mismatches were detected.");
			report.AddSummaryLine("First mismatches:");
			foreach (string mismatch in mismatches) report.AddSummaryLine($"  {mismatch}");
		}

		ComparePresentationRecords(outputDirectory, hostRecords, report);
		return report;
	}

	private static void ComparePresentationRecords(string outputDirectory, SortedDictionary<int, MultiplayerSnapshotRecord> hostRecords, MultiplayerEcsSyncReport report)
	{
		string presentationPath = Path.Combine(outputDirectory, "ECS_client_presentation.bin");
		if (!File.Exists(presentationPath))
		{
			report.AddFailure("Client presentation records are missing, so rendered placement parity was not verified.");
			return;
		}

		SortedDictionary<int, MultiplayerSnapshotRecord> presentedRecords = ReadRecords(presentationPath);
		var mismatches = new List<string>();
		int pairedSequences = 0;
		int comparedTransforms = 0;
		int mismatchCount = 0;
		int rotationDeviationCount = 0;
		float largestRotationDeviation = 0f;

		foreach (KeyValuePair<int, MultiplayerSnapshotRecord> hostEntry in hostRecords)
		{
			if (!presentedRecords.TryGetValue(hostEntry.Key, out MultiplayerSnapshotRecord? presentedRecord)) continue;
			pairedSequences++;

			foreach (MultiplayerSnapshotUnit hostUnit in hostEntry.Value.Units.Values)
			{
				if ((hostUnit.Flags & 8) == 0) continue;

				if (!presentedRecord.Units.TryGetValue(hostUnit.EntityId, out MultiplayerSnapshotUnit? presentedUnit))
				{
					mismatchCount++;
					if (mismatches.Count < 20) mismatches.Add($"seq {hostEntry.Key}: rendered entity {hostUnit.EntityId} is missing on the client.");
					continue;
				}

				comparedTransforms++;
				float dx = hostUnit.PositionX - presentedUnit.PositionX;
				float dy = hostUnit.PositionY - presentedUnit.PositionY;
				float dz = hostUnit.PositionZ - presentedUnit.PositionZ;
				if (MathF.Abs(dx) > PresentationPositionTolerance || MathF.Abs(dy) > PresentationPositionTolerance || MathF.Abs(dz) > PresentationPositionTolerance)
				{
					mismatchCount++;
					if (mismatches.Count < 20)
					{
						mismatches.Add($"seq {hostEntry.Key}: rendered entity {hostUnit.EntityId} position host=({hostUnit.PositionX:F2},{hostUnit.PositionY:F2},{hostUnit.PositionZ:F2}) client=({presentedUnit.PositionX:F2},{presentedUnit.PositionY:F2},{presentedUnit.PositionZ:F2}).");
					}
				}

				float rotationDeviation = RotationDifference(hostUnit.RotationY, presentedUnit.RotationY);
				if (rotationDeviation > PresentationRotationTolerance) rotationDeviationCount++;
				if (rotationDeviation > largestRotationDeviation) largestRotationDeviation = rotationDeviation;
			}
		}

		report.AddSummaryLine($"Presentation parity window: first {MultiplayerSyncDiagnostics.PresentationWindowSeconds:F1}s, paired sequences: {pairedSequences}, compared rendered transforms: {comparedTransforms}, placement mismatches: {mismatchCount}");
		report.AddSummaryLine($"Rendered rotation deviations beyond {PresentationRotationTolerance:F2} rad: {rotationDeviationCount} of {comparedTransforms}, largest {largestRotationDeviation:F2} rad (off-frustum units keep their frozen transform until they enter the view)");
		if (pairedSequences == 0) report.AddFailure("No rendered presentation records paired with host snapshots, so placement parity was not verified.");
		else if (comparedTransforms < 20) report.AddFailure($"Only {comparedTransforms} rendered unit transforms were compared in the presentation window.");

		if (mismatchCount > 0)
		{
			report.AddFailure($"{mismatchCount} rendered placement mismatches were detected in the first {MultiplayerSyncDiagnostics.PresentationWindowSeconds:F1}s.");
			report.AddSummaryLine("First presentation mismatches:");
			foreach (string mismatch in mismatches) report.AddSummaryLine($"  {mismatch}");
		}
	}

	private static void CompareScopeDigests(MultiplayerSnapshotRecord hostRecord, MultiplayerSnapshotRecord clientRecord, List<string> mismatches)
	{
		foreach (KeyValuePair<int, Dictionary<int, uint>> hostPlayer in hostRecord.Players)
		{
			if (!clientRecord.Players.TryGetValue(hostPlayer.Key, out Dictionary<int, uint>? clientDigests))
			{
				AddMismatch(mismatches, 0, $"seq {hostRecord.Sequence}: player slot {hostPlayer.Key} has no applied state on the client.");
				continue;
			}

			CompareDigestSets(hostRecord.Sequence, $"player slot {hostPlayer.Key}", hostPlayer.Value, clientDigests, mismatches);
		}

		CompareDigestSets(hostRecord.Sequence, "world entity", hostRecord.WorldDigests, clientRecord.WorldDigests, mismatches);

		foreach (KeyValuePair<int, Dictionary<int, uint>> hostLoot in hostRecord.Loot)
		{
			if (!clientRecord.Loot.TryGetValue(hostLoot.Key, out Dictionary<int, uint>? clientDigests))
			{
				AddMismatch(mismatches, 0, $"seq {hostRecord.Sequence}: loot {hostLoot.Key} has no applied state on the client.");
				continue;
			}

			CompareDigestSets(hostRecord.Sequence, $"loot {hostLoot.Key}", hostLoot.Value, clientDigests, mismatches);
		}

		foreach (int clientLootId in clientRecord.Loot.Keys)
		{
			if (hostRecord.Loot.ContainsKey(clientLootId)) continue;
			AddMismatch(mismatches, 0, $"seq {hostRecord.Sequence}: the client holds loot {clientLootId} that the host does not.");
		}
	}

	private static void CompareDigestSets(int sequence, string owner, Dictionary<int, uint> hostDigests, Dictionary<int, uint> clientDigests, List<string> mismatches)
	{
		const int mismatchCountBefore = 0;
		foreach (KeyValuePair<int, uint> hostDigest in hostDigests)
		{
			string componentName = DescribeComponent(hostDigest.Key);
			if (!clientDigests.TryGetValue(hostDigest.Key, out uint clientHash))
			{
				AddMismatch(mismatches, mismatchCountBefore, $"seq {sequence}: {owner} carries {componentName} on the host but not in the client's applied ECS.");
				continue;
			}

			if (clientHash != hostDigest.Value)
			{
				AddMismatch(mismatches, mismatchCountBefore, $"seq {sequence}: {owner} {componentName} differs between the host ECS ({hostDigest.Value:x8}) and the client's applied ECS ({clientHash:x8}).");
			}
		}

		foreach (KeyValuePair<int, uint> clientDigest in clientDigests)
		{
			if (hostDigests.ContainsKey(clientDigest.Key)) continue;
			AddMismatch(mismatches, mismatchCountBefore, $"seq {sequence}: {owner} carries {DescribeComponent(clientDigest.Key)} in the client's applied ECS but not on the host.");
		}
	}

	private static string DescribeComponent(int componentTypeId)
	{
		IReadOnlyList<IReplicatedComponentCodec> codecs = ReplicatedComponents.All;
		for (int index = 0; index < codecs.Count; index++)
		{
			if (codecs[index].ComponentTypeId != componentTypeId) continue;
			return codecs[index].ComponentName;
		}

		return $"component {componentTypeId}";
	}

	private static void CompareSnapshotRecord(MultiplayerSnapshotRecord hostRecord, MultiplayerSnapshotRecord clientRecord, List<string> mismatches, List<string> digestMismatches, ref int comparedUnits, ref int offscreenMissingUnits)
	{
		int mismatchCountBefore = mismatches.Count;
		bool exact = hostRecord.IsBaseline;
		float positionTolerance = exact ? BaselineTolerance : DeltaPositionTolerance;
		float rotationTolerance = exact ? BaselineTolerance : DeltaRotationTolerance;
		float healthTolerance = exact ? BaselineTolerance : DeltaHealthTolerance;

		foreach (MultiplayerSnapshotUnit hostUnit in hostRecord.Units.Values)
		{
			bool isDead = (hostUnit.Flags & 1) != 0;
			bool isVisibleToRemoteClient = (hostUnit.Flags & 8) != 0;

			if (!clientRecord.Units.TryGetValue(hostUnit.EntityId, out MultiplayerSnapshotUnit? clientUnit))
			{
				if (isDead) continue;

				if (!isVisibleToRemoteClient)
				{
					offscreenMissingUnits++;
					continue;
				}

				List<string> missingEntityTarget = hostRecord.IsBaseline ? mismatches : digestMismatches;
				AddMismatch(missingEntityTarget, 0, $"seq {hostRecord.Sequence}: client is missing live entity {hostUnit.EntityId} (owner slot {hostUnit.OwnerPlayerIndex}) that the host sent to it.");
				continue;
			}

			comparedUnits++;

			if (hostUnit.OwnerPlayerIndex != clientUnit.OwnerPlayerIndex)
			{
				AddMismatch(mismatches, mismatchCountBefore, $"seq {hostRecord.Sequence}: entity {hostUnit.EntityId} owner slot host={hostUnit.OwnerPlayerIndex} client={clientUnit.OwnerPlayerIndex}.");
			}

			bool hostIsBuilding = (hostUnit.Flags & 2) != 0;
			bool clientIsBuilding = (clientUnit.Flags & 2) != 0;
			if (hostIsBuilding != clientIsBuilding)
			{
				AddMismatch(mismatches, mismatchCountBefore, $"seq {hostRecord.Sequence}: entity {hostUnit.EntityId} building host={hostIsBuilding} client={clientIsBuilding}.");
			}

			bool clientDead = (clientUnit.Flags & 1) != 0;
			if (isDead != clientDead)
			{
				AddMismatch(mismatches, mismatchCountBefore, $"seq {hostRecord.Sequence}: entity {hostUnit.EntityId} dead host={isDead} client={clientDead}.");
			}

			if (isDead || clientDead) continue;

			float dx = hostUnit.PositionX - clientUnit.PositionX;
			float dy = hostUnit.PositionY - clientUnit.PositionY;
			float dz = hostUnit.PositionZ - clientUnit.PositionZ;
			if (MathF.Abs(dx) > positionTolerance || MathF.Abs(dy) > positionTolerance || MathF.Abs(dz) > positionTolerance)
			{
				AddMismatch(mismatches, mismatchCountBefore, $"seq {hostRecord.Sequence}: entity {hostUnit.EntityId} position host=({hostUnit.PositionX:F3},{hostUnit.PositionY:F3},{hostUnit.PositionZ:F3}) client=({clientUnit.PositionX:F3},{clientUnit.PositionY:F3},{clientUnit.PositionZ:F3}).");
			}

			if (RotationDifference(hostUnit.RotationY, clientUnit.RotationY) > rotationTolerance)
			{
				AddMismatch(mismatches, mismatchCountBefore, $"seq {hostRecord.Sequence}: entity {hostUnit.EntityId} rotation host={hostUnit.RotationY:F3} client={clientUnit.RotationY:F3}.");
			}

			if (MathF.Abs(hostUnit.CurrentHp - clientUnit.CurrentHp) > healthTolerance)
			{
				AddMismatch(mismatches, mismatchCountBefore, $"seq {hostRecord.Sequence}: entity {hostUnit.EntityId} health host={hostUnit.CurrentHp:F3} client={clientUnit.CurrentHp:F3}.");
			}

			if (MathF.Abs(hostUnit.MaxHp - clientUnit.MaxHp) > healthTolerance)
			{
				AddMismatch(mismatches, mismatchCountBefore, $"seq {hostRecord.Sequence}: entity {hostUnit.EntityId} max health host={hostUnit.MaxHp:F3} client={clientUnit.MaxHp:F3}.");
			}

			CompareDigestSets(hostRecord.Sequence, $"entity {hostUnit.EntityId}", hostUnit.ComponentDigests, clientUnit.ComponentDigests, digestMismatches);
		}

		if (!hostRecord.IsBaseline) return;

		foreach (MultiplayerSnapshotUnit clientUnit in clientRecord.Units.Values)
		{
			bool clientDead = (clientUnit.Flags & 1) != 0;
			if (clientDead) continue;
			if (hostRecord.Units.ContainsKey(clientUnit.EntityId)) continue;

			AddMismatch(mismatches, mismatchCountBefore, $"seq {hostRecord.Sequence}: client tracks live entity {clientUnit.EntityId} that the host baseline does not contain.");
		}
	}

	private static void AppendConvergenceReport(MultiplayerEcsSyncReport report, MultiplayerEcsRunSettings settings)
	{
		string hostDonePath = Path.Combine(settings.OutputDirectory, "host_done.txt");
		string clientSequencePath = Path.Combine(settings.OutputDirectory, "client_final_sequence.txt");
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

		int oneWayTicks = (int)MathF.Ceiling(settings.PingMilliseconds / 2f * 60f / 1000f);
		int allowedLagTicks = oneWayTicks + 120;
		int lagTicks = hostFinalSequence - clientFinalSequence;
		report.AddSummaryLine($"Host final sequence: {hostFinalSequence}");
		report.AddSummaryLine($"Client final sequence: {clientFinalSequence}");
		report.AddSummaryLine($"Convergence lag: {lagTicks} sequences (allowed {allowedLagTicks}, expected one-way lag {oneWayTicks})");

		if (lagTicks > allowedLagTicks)
		{
			report.AddFailure($"Client did not converge after the injected {settings.PingMilliseconds} ms delay: {lagTicks} sequences behind the host (allowed {allowedLagTicks}).");
		}

		if (lagTicks < -120)
		{
			report.AddFailure($"Client final sequence {clientFinalSequence} is ahead of the host final sequence {hostFinalSequence}.");
		}
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
			System.Console.WriteLine($"[MultiplayerEcsSync] Could not read seen snapshot sequences: {exception.Message}");
		}

		return seenSequences;
	}

	private static SortedDictionary<int, MultiplayerSnapshotRecord> ReadRecords(string path)
	{
		SortedDictionary<int, MultiplayerSnapshotRecord> records = new();
		using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16);
		using BinaryReader reader = new(stream);

		uint magic = reader.ReadUInt32();
		if (magic != 0x504E5352) throw new InvalidDataException($"{Path.GetFileName(path)} does not contain Realm sync records.");

		int version = reader.ReadInt32();
		if (version != SyncRecordVersion) throw new InvalidDataException($"{Path.GetFileName(path)} uses unsupported record version {version}.");
		reader.ReadBoolean();

		while (stream.Position < stream.Length)
		{
			int sequence = reader.ReadInt32();
			bool isBaseline = reader.ReadBoolean();
			int unitCount = reader.ReadInt32();
			Dictionary<int, MultiplayerSnapshotUnit> units = new(unitCount);

			for (int index = 0; index < unitCount; index++)
			{
				MultiplayerSnapshotUnit unit = new()
				{
					EntityId = reader.ReadInt32(),
					OwnerPlayerIndex = reader.ReadInt32(),
					PositionX = reader.ReadSingle(),
					PositionY = reader.ReadSingle(),
					PositionZ = reader.ReadSingle(),
					RotationY = reader.ReadSingle(),
					CurrentHp = reader.ReadSingle(),
					MaxHp = reader.ReadSingle(),
					Flags = reader.ReadByte(),
					ComponentDigests = ReadComponentDigests(reader)
				};
				units[unit.EntityId] = unit;
			}

			MultiplayerSnapshotRecord record = new() { Sequence = sequence, IsBaseline = isBaseline, Units = units };
			int playerCount = reader.ReadInt32();
			for (int index = 0; index < playerCount; index++)
			{
				record.Players[reader.ReadInt32()] = ReadComponentDigests(reader);
			}

			record.WorldDigests = ReadComponentDigests(reader);
			int lootCount = reader.ReadInt32();
			for (int index = 0; index < lootCount; index++)
			{
				record.Loot[reader.ReadInt32()] = ReadComponentDigests(reader);
			}

			records[sequence] = record;
		}

		return records;
	}

	private static Dictionary<int, uint> ReadComponentDigests(BinaryReader reader)
	{
		int count = reader.ReadInt32();
		Dictionary<int, uint> digests = new(count);
		for (int index = 0; index < count; index++)
		{
			digests[reader.ReadInt32()] = reader.ReadUInt32();
		}

		return digests;
	}

	private sealed class MultiplayerSnapshotRecord
	{
		public int Sequence;
		public bool IsBaseline;
		public Dictionary<int, MultiplayerSnapshotUnit> Units = new();
		public Dictionary<int, Dictionary<int, uint>> Players = new();
		public Dictionary<int, uint> WorldDigests = new();
		public Dictionary<int, Dictionary<int, uint>> Loot = new();
	}

	private sealed class MultiplayerSnapshotUnit
	{
		public int EntityId;
		public int OwnerPlayerIndex;
		public float PositionX;
		public float PositionY;
		public float PositionZ;
		public float RotationY;
		public float CurrentHp;
		public float MaxHp;
		public byte Flags;
		public Dictionary<int, uint> ComponentDigests = new();
	}
}
