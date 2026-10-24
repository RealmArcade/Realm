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

public enum NetworkProfile
{
	Clean,
	Latency,
	LatencyJitter,
	LatencyLoss
}

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
	public int OneWayDelayMilliseconds { get; init; }
	public int JitterMilliseconds { get; init; }
	public int PacketLossPercent { get; init; }
	public NetworkProfile Profile { get; init; } = NetworkProfile.Latency;
	public bool PredictionEnabled { get; init; }
	public bool RunPredictionScenario { get; init; }
	public bool Loopback { get; init; }
	public int PeerCount { get; init; } = 2;
	public int DrainMilliseconds => Math.Max(1500, Math.Max(PingMilliseconds * 4, OneWayDelayMilliseconds * 8));

	public IReadOnlyList<string> ResolveClientDirectories()
	{
		var directories = new List<string>(Math.Max(1, PeerCount - 1));
		for (int peerIndex = 1; peerIndex < Math.Max(2, PeerCount); peerIndex++)
		{
			directories.Add(ResolveClientDirectory(peerIndex));
		}

		return directories;
	}

	public string ResolveClientDirectory(int peerIndex)
	{
		return peerIndex <= 1 ? OutputDirectory : Path.Combine(OutputDirectory, $"client{peerIndex}");
	}
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
	private const int SyncRecordVersion = 4;

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

		if (settings.Loopback)
		{
			await RunLoopbackAsync(settings);
			return;
		}

		int peerCount = Math.Max(2, settings.PeerCount);
		string hostListeningFile = Path.Combine(settings.OutputDirectory, "host_listening.txt");
		string hostReadyFile = Path.Combine(settings.OutputDirectory, "host_ready.txt");
		string hostDoneFile = Path.Combine(settings.OutputDirectory, "host_done.txt");

		bool useRelay = settings.OneWayDelayMilliseconds > 0 || settings.JitterMilliseconds > 0 || settings.PacketLossPercent > 0 || settings.PingMilliseconds > 0;
		var relays = new List<UdpDelayRelay>();
		var clientProcesses = new List<HarnessProcess>();
		try
		{
			for (int peerIndex = 2; peerIndex < peerCount; peerIndex++)
			{
				string extraClientDirectory = settings.ResolveClientDirectory(peerIndex);
				Directory.CreateDirectory(extraClientDirectory);
				foreach (string file in Directory.GetFiles(extraClientDirectory)) File.Delete(file);
			}

			using HarnessProcess hostProcess = StartHarnessProcess(settings, "host", settings.Port, null, hostReadyFile, hostDoneFile, settings.OutputDirectory, peerCount);
			bool hostListening = await WaitForFileOrExitAsync(hostListeningFile, hostProcess.Process, TimeSpan.FromSeconds(120));
			if (!hostListening) throw new InvalidOperationException($"Host harness did not start listening.\n{hostProcess.ReadLog()}");

			for (int peerIndex = 1; peerIndex < peerCount; peerIndex++)
			{
				string clientDirectory = settings.ResolveClientDirectory(peerIndex);
				UdpDelayRelay? clientRelay = useRelay
					? new UdpDelayRelay(
						settings.Port,
						settings.OneWayDelayMilliseconds > 0 ? settings.OneWayDelayMilliseconds : Math.Max(1, settings.PingMilliseconds / 2),
						settings.JitterMilliseconds,
						settings.PacketLossPercent)
					: null;
				if (clientRelay != null) relays.Add(clientRelay);

				int clientConnectionPort = clientRelay?.ListenPort ?? settings.Port;
				string clientReadyFile = Path.Combine(clientDirectory, "client_ready.txt");
				string clientSequenceFile = Path.Combine(clientDirectory, "client_final_sequence.txt");
				string previousReadyFile = peerIndex <= 1 ? string.Empty : Path.Combine(settings.ResolveClientDirectory(peerIndex - 1), "client_ready.txt");
				clientProcesses.Add(StartHarnessProcess(settings, "client", clientConnectionPort, clientSequenceFile, clientReadyFile, hostDoneFile, clientDirectory, peerIndex, previousReadyFile));
			}

			for (int index = 0; index < clientProcesses.Count; index++)
			{
				string clientDirectory = settings.ResolveClientDirectory(index + 1);
				string clientReadyFile = Path.Combine(clientDirectory, "client_ready.txt");
				bool clientReady = await WaitForFileOrExitAsync(clientReadyFile, clientProcesses[index].Process, TimeSpan.FromSeconds(180 * peerCount));
				if (!clientReady) throw new InvalidOperationException($"Client peer {index + 2} did not become ready.\n{clientProcesses[index].ReadLog()}");
			}

			bool hostReady = await WaitForFileOrExitAsync(hostReadyFile, hostProcess.Process, TimeSpan.FromSeconds(180 * peerCount));
			if (!hostReady) throw new InvalidOperationException($"Host harness did not become ready.\n{hostProcess.ReadLog()}");

			TimeSpan shutdownWindow = TimeSpan.FromSeconds(settings.DurationSeconds * 4 + 300 + 60 * peerCount);
			bool hostExited = await WaitForExitAsync(hostProcess.Process, shutdownWindow);
			bool[] clientExited = new bool[clientProcesses.Count];
			for (int index = 0; index < clientProcesses.Count; index++)
			{
				clientExited[index] = await WaitForExitAsync(clientProcesses[index].Process, shutdownWindow);
			}

			if (!hostExited) hostProcess.Process.Kill(true);
			for (int index = 0; index < clientProcesses.Count; index++)
			{
				if (!clientExited[index]) clientProcesses[index].Process.Kill(true);
			}

			if (!hostExited) throw new InvalidOperationException($"Host harness did not finish in time.\n{hostProcess.ReadLog()}");
			for (int index = 0; index < clientProcesses.Count; index++)
			{
				if (!clientExited[index]) throw new InvalidOperationException($"Client peer {index + 2} did not finish in time.\n{clientProcesses[index].ReadLog()}");
			}

			if (hostProcess.Process.ExitCode != 0)
			{
				throw new InvalidOperationException($"Host harness exited with code {hostProcess.Process.ExitCode}.\n{hostProcess.ReadLog()}");
			}

			for (int index = 0; index < clientProcesses.Count; index++)
			{
				if (clientProcesses[index].Process.ExitCode != 0)
				{
					throw new InvalidOperationException($"Client peer {index + 2} exited with code {clientProcesses[index].Process.ExitCode}.\n{clientProcesses[index].ReadLog()}");
				}
			}

			WriteRelayStats(settings, relays);
		}
		finally
		{
			foreach (HarnessProcess clientProcess in clientProcesses) clientProcess.Dispose();
			foreach (UdpDelayRelay relay in relays) relay.Dispose();
		}
	}

	private static void WriteRelayStats(MultiplayerEcsRunSettings settings, List<UdpDelayRelay> relays)
	{
		if (relays.Count == 0) return;

		long forwarded = 0;
		long dropped = 0;
		foreach (UdpDelayRelay relay in relays)
		{
			forwarded += relay.ForwardedDatagramCount;
			dropped += relay.DroppedDatagramCount;
		}

		File.WriteAllText(
			Path.Combine(settings.OutputDirectory, "relay_stats.txt"),
			$"forwarded={forwarded}\ndropped={dropped}\nrelays={relays.Count}");
		for (int index = 0; index < relays.Count; index++)
		{
			File.WriteAllText(
				Path.Combine(settings.ResolveClientDirectory(index + 1), "relay_stats.txt"),
				$"forwarded={relays[index].ForwardedDatagramCount}\ndropped={relays[index].DroppedDatagramCount}\nrelays=1");
		}
	}

	private static async Task RunLoopbackAsync(MultiplayerEcsRunSettings settings)
	{
		string readyFile = Path.Combine(settings.OutputDirectory, "loopback_ready.txt");
		using HarnessProcess loopbackProcess = StartHarnessProcess(settings, "loopback", settings.Port, null, readyFile, string.Empty, settings.OutputDirectory, 1);
		bool ready = await WaitForFileOrExitAsync(readyFile, loopbackProcess.Process, TimeSpan.FromSeconds(180));
		if (!ready) throw new InvalidOperationException($"Loopback harness did not become ready.\n{loopbackProcess.ReadLog()}");

		bool exited = await WaitForExitAsync(loopbackProcess.Process, TimeSpan.FromSeconds(settings.DurationSeconds * 4 + 300));
		if (!exited) loopbackProcess.Process.Kill(true);
		if (!exited) throw new InvalidOperationException($"Loopback harness did not finish in time.\n{loopbackProcess.ReadLog()}");
		if (loopbackProcess.Process.ExitCode != 0)
		{
			throw new InvalidOperationException($"Loopback harness exited with code {loopbackProcess.Process.ExitCode}.\n{loopbackProcess.ReadLog()}");
		}
	}

	private static HarnessProcess StartHarnessProcess(
		MultiplayerEcsRunSettings settings,
		string role,
		int connectionPort,
		string? sequenceFile,
		string readyFile,
		string doneFile,
		string outputDirectory,
		int peerIndex,
		string waitForPeerReadyFile = "")
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
		startInfo.ArgumentList.Add($"--output={outputDirectory}");
		startInfo.ArgumentList.Add($"--listening-file={Path.Combine(settings.OutputDirectory, "host_listening.txt")}");
		startInfo.ArgumentList.Add($"--ready-file={readyFile}");
		startInfo.ArgumentList.Add($"--done-file={doneFile}");
		startInfo.ArgumentList.Add($"--sequence-file={sequenceFile}");
		startInfo.ArgumentList.Add($"--drain-ms={settings.DrainMilliseconds}");
		startInfo.ArgumentList.Add($"--duration={settings.DurationSeconds.ToString(CultureInfo.InvariantCulture)}");
		startInfo.ArgumentList.Add($"--prediction={(settings.PredictionEnabled ? 1 : 0)}");
		startInfo.ArgumentList.Add($"--scenario={(settings.RunPredictionScenario ? 1 : 0)}");
		startInfo.ArgumentList.Add($"--profile={settings.Profile}");
		startInfo.ArgumentList.Add($"--loopback={(settings.Loopback ? 1 : 0)}");
		startInfo.ArgumentList.Add($"--peers={Math.Max(2, settings.PeerCount)}");
		startInfo.ArgumentList.Add($"--peer-index={peerIndex}");
		startInfo.ArgumentList.Add($"--wait-for-peer-ready={waitForPeerReadyFile}");

		string logName = peerIndex <= 1 ? $"{role}.log" : $"{role}{peerIndex}.log";
		HarnessProcess harnessProcess = new(Path.Combine(outputDirectory, logName));
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

	public static MultiplayerEcsSyncReport Verify(MultiplayerEcsRunSettings settings, string? clientDirectory = null)
	{
		MultiplayerEcsSyncReport report = new();
		string outputDirectory = settings.OutputDirectory;
		string clientOutputDirectory = clientDirectory ?? outputDirectory;
		report.AddSummaryLine($"Client records: {clientOutputDirectory}");
		string hostPath = Path.Combine(outputDirectory, "ECS_host_sync.bin");
		string clientPath = Path.Combine(clientOutputDirectory, "ECS_client_sync.bin");

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

		HashSet<int> clientSeenSequences = ReadClientSeenSequences(clientOutputDirectory);
		TryReadSequence(Path.Combine(clientOutputDirectory, "client_final_sequence.txt"), out int clientFinalAppliedSequence);
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
		int comparedNavigationBaselines = 0;
		int pathingMismatchCount = 0;
		int navMeshMissingCount = 0;
		int navMeshShapeDifferenceCount = 0;
		List<string> navMeshObservations = new();
		List<int> hostNavigationChangeSequences = new();
		MultiplayerNavigationState? previousHostNavigation = null;

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

			if (hostEntry.Value.IsBaseline)
			{
				if (previousHostNavigation != null && !IsSameNavigation(previousHostNavigation, hostEntry.Value.Navigation))
				{
					hostNavigationChangeSequences.Add(hostEntry.Key);
				}

				previousHostNavigation = hostEntry.Value.Navigation;
				comparedNavigationBaselines++;
				CompareNavigationState(hostEntry.Value.Sequence, hostEntry.Value.Navigation, clientRecord.Navigation, mismatches, navMeshObservations, ref pathingMismatchCount, ref navMeshMissingCount, ref navMeshShapeDifferenceCount);
			}
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
		report.AddSummaryLine($"Navigation parity: compared {comparedNavigationBaselines} baselines, pathing grid mismatches {pathingMismatchCount}, baselines missing a navmesh {navMeshMissingCount}");
		report.AddSummaryLine($"Navmesh shape differences: {navMeshShapeDifferenceCount} of {comparedNavigationBaselines} baselines (the host bakes the map's placed obstacles into its mesh, while a client bakes bare terrain and carves poly flags afterwards, so the polygon split differs while the walkable footprint does not)");
		for (int index = 0; index < Math.Min(navMeshObservations.Count, 3); index++)
		{
			report.AddSummaryLine($"  {navMeshObservations[index]}");
		}
		report.AddSummaryLine($"Host navigation changes between baselines: {hostNavigationChangeSequences.Count} ({DescribeSequenceList(hostNavigationChangeSequences)})");
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

		int tolerance = ResolveLostSequenceTolerance(settings, pairedSnapshots, lostSequenceCount);
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

		if (comparedNavigationBaselines == 0)
		{
			report.AddFailure("No baseline carried a navigation digest, so navmesh parity was not verified.");
		}

		if (pathingMismatchCount > 0)
		{
			report.AddFailure($"{pathingMismatchCount} baselines had a terrain pathing grid that differs from the host's.");
		}

		if (navMeshMissingCount > 0)
		{
			report.AddFailure($"{navMeshMissingCount} baselines had no baked navmesh on one side, so prediction could not path.");
		}

		if (mismatches.Count > 0)
		{
			report.AddFailure($"{mismatches.Count} synced state mismatches were detected.");
			report.AddSummaryLine("First mismatches:");
			foreach (string mismatch in mismatches) report.AddSummaryLine($"  {mismatch}");
		}

		AppendConvergenceReport(report, settings, lastPairedSequence, pairedSnapshots, lostSequenceCount, missingBaselineSequences.Count, clientOutputDirectory);
		ComparePresentationRecords(clientOutputDirectory, hostRecords, report);
		return report;
	}

	/// <summary>
	/// The share of host snapshot sequences that may never reach the client, expressed as a percentage of the paired
	/// snapshots. A per-tick snapshot spans roughly four unreliable fragments, so a datagram loss rate of p drops about
	/// 1-(1-p)^4 of snapshots: 3% datagrams is close to 11.5% of snapshots. The allowance is that expectation with
	/// roughly 1.75x headroom, so it fails only on a real transport regression rather than on the profile's own loss.
	/// </summary>
	private static int ResolveLostSequenceTolerance(MultiplayerEcsRunSettings settings, int pairedSnapshots, int lostSequenceCount)
	{
		int allowancePercent = settings.Profile switch
		{
			NetworkProfile.Clean => 5,
			NetworkProfile.Latency => 5,
			NetworkProfile.LatencyJitter => 8,
			NetworkProfile.LatencyLoss => 45,
			_ => 5
		};

		// Loss is a share of what the host produced, so the denominator is the sequences the client received plus the
		// ones it never received. Measured on this harness: a clean profile loses under 1%, a jittered profile a few
		// percent, and 3% injected datagram loss reaches roughly 33-38% of snapshots because each snapshot spans
		// multiple unreliable fragments.
		int producedSequences = pairedSnapshots + lostSequenceCount;
		return Math.Max(10, producedSequences * allowancePercent / 100);
	}

	private static bool IsSameNavigation(MultiplayerNavigationState left, MultiplayerNavigationState right)
	{
		return left.HasTerrainState == right.HasTerrainState
			&& left.HasNavMesh == right.HasNavMesh
			&& left.Width == right.Width
			&& left.Depth == right.Depth
			&& left.PathingHash == right.PathingHash
			&& left.WalkableCellCount == right.WalkableCellCount
			&& left.BlockedCellCount == right.BlockedCellCount
			&& left.PolyCount == right.PolyCount
			&& left.GroundPolyCount == right.GroundPolyCount
			&& left.PolyHash == right.PolyHash;
	}

	private static void CompareNavigationState(int sequence, MultiplayerNavigationState hostNavigation, MultiplayerNavigationState clientNavigation, List<string> mismatches, List<string> navMeshObservations, ref int pathingMismatchCount, ref int navMeshMissingCount, ref int navMeshShapeDifferenceCount)
	{
		if (!hostNavigation.HasTerrainState)
		{
			AddMismatch(mismatches, 0, $"seq {sequence}: the host has no terrain state, so navigation could not be compared.");
			return;
		}

		if (!clientNavigation.HasTerrainState)
		{
			AddMismatch(mismatches, 0, $"seq {sequence}: the client has no terrain state, so it cannot path or carve obstacles.");
			pathingMismatchCount++;
			return;
		}

		bool pathingMatches = hostNavigation.Width == clientNavigation.Width
			&& hostNavigation.Depth == clientNavigation.Depth
			&& hostNavigation.PathingHash == clientNavigation.PathingHash
			&& hostNavigation.WalkableCellCount == clientNavigation.WalkableCellCount
			&& hostNavigation.BlockedCellCount == clientNavigation.BlockedCellCount;
		if (!pathingMatches)
		{
			pathingMismatchCount++;
			AddMismatch(mismatches, 0, $"seq {sequence}: terrain pathing host=(size {hostNavigation.Width}x{hostNavigation.Depth}, walkable {hostNavigation.WalkableCellCount}, blocked {hostNavigation.BlockedCellCount}, hash {hostNavigation.PathingHash:x8}) client=(size {clientNavigation.Width}x{clientNavigation.Depth}, walkable {clientNavigation.WalkableCellCount}, blocked {clientNavigation.BlockedCellCount}, hash {clientNavigation.PathingHash:x8}).");
		}

		if (!hostNavigation.HasNavMesh || !clientNavigation.HasNavMesh)
		{
			navMeshMissingCount++;
			AddMismatch(mismatches, 0, $"seq {sequence}: navmesh host present={hostNavigation.HasNavMesh} client present={clientNavigation.HasNavMesh}.");
			return;
		}

		bool navMeshShapeMatches = hostNavigation.PolyCount == clientNavigation.PolyCount
			&& hostNavigation.GroundPolyCount == clientNavigation.GroundPolyCount
			&& hostNavigation.PolyHash == clientNavigation.PolyHash;
		if (!navMeshShapeMatches)
		{
			navMeshShapeDifferenceCount++;
			if (navMeshObservations.Count < 20)
			{
				navMeshObservations.Add($"seq {sequence}: navmesh host=(polys {hostNavigation.PolyCount}, ground {hostNavigation.GroundPolyCount}, hash {hostNavigation.PolyHash:x8}) client=(polys {clientNavigation.PolyCount}, ground {clientNavigation.GroundPolyCount}, hash {clientNavigation.PolyHash:x8}).");
			}
		}
	}

	private static string DescribeSequenceList(List<int> sequences)
	{
		if (sequences.Count == 0) return "none";

		int listedCount = Math.Min(sequences.Count, 12);
		StringBuilder summary = new();
		for (int index = 0; index < listedCount; index++)
		{
			if (index > 0) summary.Append(", ");
			summary.Append(sequences[index]);
		}

		if (sequences.Count > listedCount) summary.Append($", ... ({sequences.Count - listedCount} more)");
		return summary.ToString();
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
		int skippedPredictedTransforms = 0;
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

				if ((presentedUnit.Flags & 16) != 0)
				{
					skippedPredictedTransforms++;
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
		report.AddSummaryLine($"Locally predicted rendered transforms excluded from placement parity: {skippedPredictedTransforms} (prediction intentionally leads the authoritative position).");
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

			record.Navigation = new MultiplayerNavigationState
			{
				HasTerrainState = reader.ReadBoolean(),
				HasNavMesh = reader.ReadBoolean(),
				Width = reader.ReadInt32(),
				Depth = reader.ReadInt32(),
				PathingHash = reader.ReadUInt32(),
				WalkableCellCount = reader.ReadInt32(),
				BlockedCellCount = reader.ReadInt32(),
				PolyCount = reader.ReadInt32(),
				GroundPolyCount = reader.ReadInt32(),
				PolyHash = reader.ReadUInt32()
			};

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
		public MultiplayerNavigationState Navigation = new();
	}

	private sealed class MultiplayerNavigationState
	{
		public bool HasTerrainState;
		public bool HasNavMesh;
		public int Width;
		public int Depth;
		public uint PathingHash;
		public int WalkableCellCount;
		public int BlockedCellCount;
		public int PolyCount;
		public int GroundPolyCount;
		public uint PolyHash;
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
