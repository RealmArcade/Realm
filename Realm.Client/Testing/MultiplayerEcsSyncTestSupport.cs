using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Realm.Client.Services;
using Realm.Ecs.Replication;
using Realm.Shared.Network;

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
	public int PeerCount { get; init; } = 2;
	/// <summary>
	/// How many seats the match is asked to play itself. They are part of the roster the lobby hands over, so a map's
	/// script seats them before it runs its own logic, exactly as it does for a player who connected.
	/// </summary>
	public int BotCount { get; init; }
	/// <summary>
	/// Game time at which the first client drops its connection on purpose. Zero disables the reconnect scenario.
	/// </summary>
	public float DropClientAtSeconds { get; init; }
	public float DiscardSnapshotAtSeconds { get; init; }
	/// <summary>
	/// How many host snapshot sequences the dropped client may miss before the reconnect counts as failed. At 30 snapshots
	/// per second this is roughly eight seconds: the reconnect handshake, a fresh baseline and the first deltas.
	/// </summary>
	public int ReconnectDeadlineSequences { get; init; } = 240;
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
	/// <summary>
	/// What "exact" can mean for a unit a baseline restates. The host declares a unit changed - and therefore says
	/// anything about it at all - only when it moved more than five centimetres, turned more than three degrees, or lost
	/// or gained a tenth of a hit point, so a peer legitimately holds a value inside that quantum and demanding 0.0005
	/// asked for precision the protocol never sends. Regenerating units drifted by a few hundredths and were reported as
	/// divergences on every eight-peer run.
	/// </summary>
	private const float BaselineTolerance = 0.1f;
	private const float DeltaPositionTolerance = 1.05f;
	private const float DeltaRotationTolerance = 0.17f;
	private const float DeltaHealthTolerance = 1.05f;
	private const float PresentationPositionTolerance = 0.25f;
	private const float PresentationRotationTolerance = 0.2f;
	/// <summary>
	/// The smallest movement the wire can state, so a unit the match describes to a peer is fresh to within this much on
	/// every tick it is described on. The match states one precision, which this is the resend step of.
	/// </summary>
	private const float StatedPositionThresholdMeters = 0.05f;
	private const float SnapshotRecordsPerSecond = 60f;
	/// <summary>
	/// A host step this large is a spawn, a teleport or a correction, not travel, so it is left out of the speed measured
	/// for the interpolation bound.
	/// </summary>
	private const float MaximumTravelStepMeters = 2f;
	private const int PlacementQuantityRendererFidelity = 0;
	private const int PlacementQuantityAuthority = 1;
	private const int PlacementQuantityCount = 2;
	/// <summary>
	/// How many consecutive records a measured distance may exceed its tolerance before it is reported: at the harness's
	/// sixty records per second this is half a second, which is longer than any legitimate catch-up the protocol allows.
	/// </summary>
	private const int PlacementLagAllowanceSequences = 30;
	private const int StartupWarmupSequenceCount = 60;
	private const int SyncRecordVersion = 8;

	public static string ResolveClientProjectDirectory()
	{
		DirectoryInfo? directory = new(AppContext.BaseDirectory);
		while (directory != null)
		{
			string candidate = Path.Combine(directory.FullName, "Realm.Client");
			if (File.Exists(Path.Combine(candidate, "project.godot"))) return candidate;
			directory = directory.Parent;
		}

		throw new DirectoryNotFoundException($"The Realm client project was not found above {AppContext.BaseDirectory}.");
	}

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

			using HarnessProcess hostProcess = StartHarnessProcess(settings, "host", settings.Port, null, hostReadyFile, hostDoneFile, settings.OutputDirectory, 1);
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
			Path.Combine(settings.OutputDirectory, "relay_stats_total.txt"),
			$"forwarded={forwarded}\ndropped={dropped}\nrelays={relays.Count}");
		for (int index = 0; index < relays.Count; index++)
		{
			File.WriteAllText(
				Path.Combine(settings.ResolveClientDirectory(index + 1), "relay_stats.txt"),
				$"forwarded={relays[index].ForwardedDatagramCount}\ndropped={relays[index].DroppedDatagramCount}\nrelays=1");
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
		startInfo.ArgumentList.Add($"--peers={Math.Max(2, settings.PeerCount)}");
		startInfo.ArgumentList.Add($"--peer-index={peerIndex}");
		startInfo.ArgumentList.Add($"--bots={(role == "host" ? settings.BotCount : 0)}");
		startInfo.ArgumentList.Add($"--wait-for-peer-ready={waitForPeerReadyFile}");
		// The scenarios are named after what they ask of a peer rather than after a seat in a particular room: the
		// reconnect drops the first client's connection and the repair makes the first client discard a snapshot, so a
		// room of two peers - one screen - runs both, and a room of three runs them on the screen they were written for.
		startInfo.ArgumentList.Add($"--drop-at={(role == "client" && peerIndex == 1 ? settings.DropClientAtSeconds : 0f).ToString(CultureInfo.InvariantCulture)}");
		startInfo.ArgumentList.Add($"--discard-at={(role == "client" && peerIndex == 1 ? settings.DiscardSnapshotAtSeconds : 0f).ToString(CultureInfo.InvariantCulture)}");

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
		// A baseline is decided per peer, so whether this client was sent one at a sequence comes from the record's peer
		// list rather than from the sequence's own flag, which now only means "a baseline for at least one peer".
		// The client's peer id is not constant: a connection that times out while the map loads reconnects with a new
		// id, so the harness appends every id the process used and a baseline counts as sent to this client when any of
		// them appears in the record. Attributing by the first id alone reported every later baseline as a mismatch.
		HashSet<int> clientPeerIds = ReadClientPeerIds(clientOutputDirectory);
		int inFlightSequenceCount = 0;
		int supersededSequenceCount = 0;

		ReconnectExpectation? reconnectExpectation = ReadReconnectExpectation(settings, clientOutputDirectory, report);
		HashSet<int>? sequencesSentToPeer = reconnectExpectation == null ? ReadHostSentSequences(outputDirectory, clientOutputDirectory) : null;
		List<int> notSentSequences = new();
		int reconnectOutageSequences = 0;
		int pairedSequencesAfterReconnect = 0;

		int firstPairedSequence = -1;
		List<int> missingBaselineSequences = new();
		List<int> supersededBaselineSequences = new();
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
		List<(int Sequence, int Count)> transientMismatchSequences = new();
		List<(int EntityId, string Field, int Sequence, string Message)> deltaDivergences = new();
		int comparedLootEntities = 0;
		int comparedNavigationBaselines = 0;
		int pathingMismatchCount = 0;
		int navMeshMissingCount = 0;
		int navMeshShapeDifferenceCount = 0;
		int navMeshAgreedRecords = 0;
		int navMeshDisagreementAfterAgreementCount = 0;
		int navMeshDisagreementBeforeAgreementCount = 0;
		int firstNavMeshAgreementSequence = -1;
		int lastAgreedHostPolyCount = 0;
		int lastAgreedHostGroundPolyCount = 0;
		int lastAgreedClientPolyCount = 0;
		int lastAgreedClientGroundPolyCount = 0;
		int lastAgreedObstacleVolumeCount = 0;
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

					// A sequence the scenario asked the peer to be away for was never going to be applied, so nothing it carried
					// - a baseline included - is a fault in the protocol.
					if (reconnectExpectation != null && reconnectExpectation.Contains(hostEntry.Key))
					{
						reconnectOutageSequences++;
						continue;
					}

					if (clientSeenSequences.Contains(hostEntry.Key)) supersededSequenceCount++;
					else if (hostEntry.Key <= firstPairedSequence + StartupWarmupSequenceCount) startupCatchUpSequences.Add(hostEntry.Key);
					else if (sequencesSentToPeer != null && !sequencesSentToPeer.Contains(hostEntry.Key)) notSentSequences.Add(hostEntry.Key);
					else lostSequences.Add(hostEntry.Key);
					// A baseline travels on its own reliable channel, so a burst of deltas can overtake it and the client then
					// rejects it as older than what it has already applied. That is the transport working, not a lost baseline:
					// the sequence reached the peer and the state it carried is already superseded. Only a baseline the peer never
					// saw is a missing baseline.
					if (ContainsAnyPeerId(hostEntry.Value.BaselinePeerIds, clientPeerIds))
					{
						if (clientSeenSequences.Contains(hostEntry.Key)) supersededBaselineSequences.Add(hostEntry.Key);
						else missingBaselineSequences.Add(hostEntry.Key);
					}
				}

				continue;
			}

			if (firstPairedSequence < 0) firstPairedSequence = hostEntry.Key;
			lastPairedSequence = hostEntry.Key;
			pairedSnapshots++;
			if (reconnectExpectation != null && hostEntry.Key > reconnectExpectation.ResumeSequence) pairedSequencesAfterReconnect++;

			bool isReconnectBaselineRecord = reconnectExpectation != null && hostEntry.Key == reconnectExpectation.ResumeBaselineSequence;
			bool hostSentBaselineToThisPeer = clientPeerIds.Count > 0
				? ContainsAnyPeerId(hostEntry.Value.BaselinePeerIds, clientPeerIds)
				: hostEntry.Value.IsBaseline;
			if (hostSentBaselineToThisPeer != clientRecord.IsBaseline && !isReconnectBaselineRecord)
			{
				AddMismatch(mismatches, mismatches.Count, $"seq {hostEntry.Key}: baseline flag host={hostSentBaselineToThisPeer} (peer {string.Join('/', clientPeerIds)}) client={clientRecord.IsBaseline}.");
			}

			List<string> digestTarget = hostSentBaselineToThisPeer ? mismatches : transientDigestMismatches;
			int digestMismatchCountBefore = transientDigestMismatches.Count;
			if (hostSentBaselineToThisPeer) comparedLootEntities += hostEntry.Value.Loot.Count;
			CompareSnapshotRecord(hostEntry.Value, clientRecord, hostSentBaselineToThisPeer, mismatches, digestTarget, ref comparedUnits, ref offscreenMissingUnits, deltaDivergences);
			CompareScopeDigests(hostEntry.Value, clientRecord, digestTarget);
			int transientAddedForSequence = transientDigestMismatches.Count - digestMismatchCountBefore;
			transientDigestMismatchCount += transientAddedForSequence;
			if (transientAddedForSequence > 0) transientMismatchSequences.Add((hostEntry.Key, transientAddedForSequence));

			if (hostSentBaselineToThisPeer)
			{
				if (previousHostNavigation != null && !IsSameNavigation(previousHostNavigation, hostEntry.Value.Navigation))
				{
					hostNavigationChangeSequences.Add(hostEntry.Key);
				}

				previousHostNavigation = hostEntry.Value.Navigation;
				comparedNavigationBaselines++;
				CompareNavigationState(hostEntry.Value.Sequence, hostEntry.Value.Navigation, clientRecord.Navigation, mismatches, navMeshObservations, ref pathingMismatchCount, ref navMeshMissingCount, ref navMeshShapeDifferenceCount);
			}

			// The mesh is also compared on every paired record, not only on baselines, because a mesh difference is not a
			// per-tick state change: it changes only when one side bakes. Once the two sides have agreed on a mesh, any later
			// difference means one side re-baked from different input. Baselines alone left the peers whose baseline flag the
			// host records imperfectly with no mesh verification at all.
			if (hostEntry.Value.Navigation.HasNavMesh && clientRecord.Navigation.HasNavMesh)
			{
				bool shapeMatches = hostEntry.Value.Navigation.PolyCount == clientRecord.Navigation.PolyCount
					&& hostEntry.Value.Navigation.GroundPolyCount == clientRecord.Navigation.GroundPolyCount
					&& hostEntry.Value.Navigation.PolyHash == clientRecord.Navigation.PolyHash;
				if (shapeMatches)
				{
					navMeshAgreedRecords++;
					lastAgreedHostPolyCount = hostEntry.Value.Navigation.PolyCount;
					lastAgreedHostGroundPolyCount = hostEntry.Value.Navigation.GroundPolyCount;
					lastAgreedClientPolyCount = clientRecord.Navigation.PolyCount;
					lastAgreedClientGroundPolyCount = clientRecord.Navigation.GroundPolyCount;
					lastAgreedObstacleVolumeCount = hostEntry.Value.Navigation.ObstacleVolumeCount;
					if (firstNavMeshAgreementSequence < 0) firstNavMeshAgreementSequence = hostEntry.Key;
				}
				else if (firstNavMeshAgreementSequence >= 0)
				{
					navMeshDisagreementAfterAgreementCount++;
					if (navMeshObservations.Count < 20)
					{
						navMeshObservations.Add($"seq {hostEntry.Key}: the mesh agreed at seq {firstNavMeshAgreementSequence} and differs here: host=(polys {hostEntry.Value.Navigation.PolyCount}, ground {hostEntry.Value.Navigation.GroundPolyCount}, hash {hostEntry.Value.Navigation.PolyHash:x8}, obstacle volumes {hostEntry.Value.Navigation.ObstacleVolumeCount}) client=(polys {clientRecord.Navigation.PolyCount}, ground {clientRecord.Navigation.GroundPolyCount}, hash {clientRecord.Navigation.PolyHash:x8}, obstacle volumes {clientRecord.Navigation.ObstacleVolumeCount}).");
					}
				}
				else
				{
					navMeshDisagreementBeforeAgreementCount++;
				}
			}
		}

		report.PairedSnapshotCount = pairedSnapshots;
		report.ComparedUnitCount = comparedUnits;
		report.AddSummaryLine($"First paired sequence: {firstPairedSequence}");
		report.AddSummaryLine($"Last paired sequence: {lastPairedSequence}");
		int lostSequenceCount = lostSequences.Count;
		report.AddSummaryLine($"Host sequences missing on client after first pairing: {unpairedSequenceCount} (superseded by a newer snapshot: {supersededSequenceCount}, startup catch-up: {startupCatchUpSequences.Count}, the host never sent them to this peer: {notSentSequences.Count}, sent but never received: {lostSequenceCount})");
		report.AddSummaryLine($"Snapshots still in flight when the client stopped recording: {inFlightSequenceCount} (after client sequence {clientFinalAppliedSequence})");
		report.AddSummaryLine($"Missing baseline sequences: {(missingBaselineSequences.Count == 0 ? "none" : string.Join(", ", missingBaselineSequences))}");
		report.AddSummaryLine($"Baselines the peer received but had already superseded with newer deltas: {supersededBaselineSequences.Count} ({DescribeSequenceList(supersededBaselineSequences)})");
		report.AddSummaryLine($"Startup catch-up sequences lost while the client applied its first baseline: {startupCatchUpSequences.Count} of the first {StartupWarmupSequenceCount} sequences ({DescribeSequenceBursts(startupCatchUpSequences)})");
		report.AddSummaryLine($"Lost sequence bursts after startup: {DescribeSequenceBursts(lostSequences)}");
		report.AddSummaryLine($"Off-screen host units absent on the client as expected: {offscreenMissingUnits}");
		report.AddSummaryLine($"Loot entities compared at baselines: {comparedLootEntities}");
		report.AddSummaryLine($"Navigation parity: compared {comparedNavigationBaselines} baselines, pathing grid mismatches {pathingMismatchCount}, baselines missing a navmesh {navMeshMissingCount}");
		report.AddSummaryLine($"Navmesh shape differences: {navMeshShapeDifferenceCount} of {comparedNavigationBaselines} baselines (a peer bakes the obstacle volumes the authority reports, so a peer's mesh must be the authority's mesh polygon for polygon)");
		report.AddSummaryLine($"Navmesh on every paired record: agreed on {navMeshAgreedRecords} records from seq {firstNavMeshAgreementSequence} onward, differed on {navMeshDisagreementBeforeAgreementCount} before the two sides first agreed (the peer's first bake) and on {navMeshDisagreementAfterAgreementCount} after; the agreed mesh is host {lastAgreedHostPolyCount} polygons ({lastAgreedHostGroundPolyCount} ground) and peer {lastAgreedClientPolyCount} polygons ({lastAgreedClientGroundPolyCount} ground) from {lastAgreedObstacleVolumeCount} obstacle volumes");
		for (int index = 0; index < Math.Min(navMeshObservations.Count, 3); index++)
		{
			report.AddSummaryLine($"  {navMeshObservations[index]}");
		}
		report.AddSummaryLine($"Host navigation changes between baselines: {hostNavigationChangeSequences.Count} ({DescribeSequenceList(hostNavigationChangeSequences)})");
		report.AddSummaryLine($"Differences seen only on delta snapshots and healed by the next baseline: {transientDigestMismatchCount}");
		for (int index = 0; index < transientMismatchSequences.Count; index++)
		{
			report.AddSummaryLine($"  seq {transientMismatchSequences[index].Sequence}: {transientMismatchSequences[index].Count} difference(s)");
		}

		for (int index = 0; index < Math.Min(transientDigestMismatches.Count, 16); index++)
		{
			report.AddSummaryLine($"  {transientDigestMismatches[index]}");
		}

		if (pairedSnapshots < 3) report.AddFailure($"Only {pairedSnapshots} snapshot sequences were paired between host and client.");
		if (comparedUnits < 20) report.AddFailure($"Only {comparedUnits} unit states were compared, which is not enough to validate sync.");
		if (missingBaselineSequences.Count > 0)
		{
			report.AddFailure($"{missingBaselineSequences.Count} baseline snapshots produced by the host were never applied by the client.");
		}

		ReportNotSentSequences(report, notSentSequences, firstPairedSequence);
		ReportDeltaDivergences(report, deltaDivergences, settings);

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

		if (reconnectExpectation != null)
		{
			reconnectExpectation.Report(report, reconnectOutageSequences, pairedSequencesAfterReconnect);
		}

		if (comparedNavigationBaselines == 0 && navMeshAgreedRecords == 0)
		{
			report.AddFailure("No snapshot carried a navigation digest, so navmesh parity was not verified.");
		}

		if (pathingMismatchCount > 0)
		{
			report.AddFailure($"{pathingMismatchCount} baselines had a terrain pathing grid that differs from the host's.");
		}

		if (navMeshMissingCount > 0)
		{
			report.AddFailure($"{navMeshMissingCount} baselines had no baked navmesh on one side, so prediction could not path.");
		}

		if (navMeshShapeDifferenceCount > 0)
		{
			report.AddFailure($"{navMeshShapeDifferenceCount} of {comparedNavigationBaselines} baselines had a mesh whose polygons differ from the host's, so the same path query can answer with a different corridor.");
		}

		if (navMeshAgreedRecords == 0)
		{
			report.AddFailure("The peer's navigation mesh never matched the host's on any paired record, so pathing parity rests on the pathing grid alone.");
		}

		if (navMeshDisagreementAfterAgreementCount > 0)
		{
			report.AddFailure($"{navMeshDisagreementAfterAgreementCount} paired records had a mesh that differed from the host's after the two sides had already agreed on one, so one side re-baked from different obstacle input.");
		}

		if (mismatches.Count > 0)
		{
			report.AddFailure($"{mismatches.Count} synced state mismatches were detected.");
			report.AddSummaryLine("First mismatches:");
			foreach (string mismatch in mismatches) report.AddSummaryLine($"  {mismatch}");
		}

		AppendConvergenceReport(report, settings, lastPairedSequence, pairedSnapshots, lostSequenceCount, missingBaselineSequences.Count, clientOutputDirectory);
		ComparePresentationRecords(clientOutputDirectory, hostRecords, report, settings);
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

	/// <summary>
	/// What the mid-match reconnect scenario recorded on the client: the sequence it had applied when the connection was
	/// dropped and the first sequence it applied after rejoining. Sequences in between are the outage the scenario asked
	/// for, so they are counted separately from snapshots the transport lost, and the state compared after the resume is
	/// what proves the client converged back to the authority.
	/// </summary>
	private sealed class ReconnectExpectation
	{
		public int DropSequence { get; init; }
		public int ResumeSequence { get; init; }
		public int ResumeBaselineSequence { get; init; }
		public int OutageBudget { get; init; }

		public bool Contains(int sequence) => sequence >= DropSequence && sequence <= ResumeSequence;

		public void Report(MultiplayerEcsSyncReport report, int outageSequences, int pairedSequencesAfterReconnect)
		{
			int missedSequences = Math.Max(0, ResumeSequence - DropSequence);
			report.AddSummaryLine($"Reconnect scenario: the client dropped at applied sequence {DropSequence} and resumed at {ResumeSequence}, a {missedSequences}-sequence outage (budget {OutageBudget}); {outageSequences} of those were confirmed host sequences it did not apply.");
			report.AddSummaryLine($"Reconnect scenario: {pairedSequencesAfterReconnect} host sequences were compared after the resume.");
			if (ResumeBaselineSequence > 0)
			{
				report.AddSummaryLine($"Reconnect scenario: sequence {ResumeBaselineSequence} was the baseline sent to this peer on reconnect; its baseline flag is excluded from the comparison because the host sends that baseline outside the per-tick fan-out.");
			}

			if (missedSequences > OutageBudget)
			{
				report.AddFailure($"The reconnect took {missedSequences} sequences, above the {OutageBudget}-sequence budget, so the client was blind for longer than the scenario allows.");
			}

			if (pairedSequencesAfterReconnect < 30)
			{
				report.AddFailure($"Only {pairedSequencesAfterReconnect} sequences were paired after the reconnect, which is not enough to show the client converged.");
			}
		}
	}

	private static ReconnectExpectation? ReadReconnectExpectation(MultiplayerEcsRunSettings settings, string clientDirectory, MultiplayerEcsSyncReport report)
	{
		if (settings.DropClientAtSeconds <= 0f) return null;

		// The scenario drops the connection of the first peer, so only that peer's record carries the drop.
		if (!string.Equals(Path.GetFullPath(clientDirectory), Path.GetFullPath(settings.ResolveClientDirectory(1)), StringComparison.OrdinalIgnoreCase)) return null;

		string path = Path.Combine(clientDirectory, "client_drop.txt");
		if (!File.Exists(path))
		{
			report.AddFailure("The reconnect scenario asked the client to drop its connection, but the client wrote no drop record.");
			return null;
		}

		var fields = new Dictionary<string, string>(StringComparer.Ordinal);
		foreach (string line in File.ReadAllLines(path))
		{
			int separator = line.IndexOf('=');
			if (separator <= 0) continue;
			fields[line.Substring(0, separator)] = line.Substring(separator + 1);
		}

		if (!fields.TryGetValue("dropped", out string? dropped) || dropped != "1")
		{
			fields.TryGetValue("reason", out string? reason);
			report.AddFailure($"The client never dropped its connection for the reconnect scenario: {reason ?? "no reason recorded"}.");
			return null;
		}

		if (!fields.TryGetValue("resumed", out string? resumed) || resumed != "1")
		{
			report.AddFailure("The client dropped its connection and never applied another snapshot, so the reconnect did not complete.");
			return null;
		}

		if (!int.TryParse(fields.GetValueOrDefault("drop_sequence"), out int dropSequence) || !int.TryParse(fields.GetValueOrDefault("resume_sequence"), out int resumeSequence))
		{
			report.AddFailure("The client's reconnect record does not name the sequences it dropped and resumed at.");
			return null;
		}

		int.TryParse(fields.GetValueOrDefault("resume_baseline_sequence"), out int resumeBaselineSequence);
		return new ReconnectExpectation
		{
			DropSequence = dropSequence,
			ResumeSequence = resumeSequence,
			ResumeBaselineSequence = resumeBaselineSequence,
			OutageBudget = settings.ReconnectDeadlineSequences
		};
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

		bool obstacleInputMatches = hostNavigation.ObstacleVolumeCount == clientNavigation.ObstacleVolumeCount
			&& hostNavigation.ObstacleHash == clientNavigation.ObstacleHash;
		bool navMeshShapeMatches = hostNavigation.PolyCount == clientNavigation.PolyCount
			&& hostNavigation.GroundPolyCount == clientNavigation.GroundPolyCount
			&& hostNavigation.PolyHash == clientNavigation.PolyHash;
		if (!navMeshShapeMatches)
		{
			navMeshShapeDifferenceCount++;
			if (navMeshObservations.Count < 20)
			{
				// The bake input is reported beside the result, because a mesh that differs while the volumes and the terrain
				// agree is a bake defect, and one that differs with different volumes is a missing transfer.
				navMeshObservations.Add($"seq {sequence}: navmesh host=(polys {hostNavigation.PolyCount}, ground {hostNavigation.GroundPolyCount}, hash {hostNavigation.PolyHash:x8}, obstacle volumes {hostNavigation.ObstacleVolumeCount}, input hash {hostNavigation.ObstacleHash:x8}) client=(polys {clientNavigation.PolyCount}, ground {clientNavigation.GroundPolyCount}, hash {clientNavigation.PolyHash:x8}, obstacle volumes {clientNavigation.ObstacleVolumeCount}, input hash {clientNavigation.ObstacleHash:x8}), obstacle inputs {(obstacleInputMatches ? "agree" : "differ")}.");
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

	private static float ResolvePresentationTolerance(NetworkProfile profile)
	{
		return profile switch
		{
			NetworkProfile.Clean => PresentationPositionTolerance,
			NetworkProfile.Latency => PresentationPositionTolerance,
			NetworkProfile.LatencyJitter => 0.35f,
			NetworkProfile.LatencyLoss => 0.6f,
			_ => PresentationPositionTolerance
		};
	}

	private static void ComparePresentationRecords(string outputDirectory, SortedDictionary<int, MultiplayerSnapshotRecord> hostRecords, MultiplayerEcsSyncReport report, MultiplayerEcsRunSettings settings)
	{
		NetworkProfile profile = settings.Profile;
		string presentationPath = Path.Combine(outputDirectory, "ECS_client_presentation.bin");
		if (!File.Exists(presentationPath))
		{
			report.AddFailure("Client presentation records are missing, so rendered placement parity was not verified.");
			return;
		}

		SortedDictionary<int, MultiplayerSnapshotRecord> presentedRecords = ReadRecords(presentationPath);
		string appliedPath = Path.Combine(outputDirectory, "ECS_client_sync.bin");
		if (!File.Exists(appliedPath))
		{
			report.AddFailure("Client applied-state records are missing, so rendered placement parity was not verified.");
			return;
		}

		SortedDictionary<int, MultiplayerSnapshotRecord> appliedRecords = ReadRecords(appliedPath);
		float placementTolerance = ResolvePresentationTolerance(profile);
		float fastestUnitSpeed = MeasureFastestUnitSpeed(hostRecords);
		float interpolationBufferSeconds = ResolveInterpolationBufferSeconds(settings);
		float interpolationLagMeters = fastestUnitSpeed * interpolationBufferSeconds;
		float detailedAuthorityTolerance = StatedPositionThresholdMeters + interpolationLagMeters + placementTolerance;
		var mismatches = new List<string>();
		int pairedSequences = 0;
		int comparedTransforms = 0;
		int mismatchCount = 0;
		int skippedPredictedTransforms = 0;
		int culledTransforms = 0;
		int notYetAppliedUnits = 0;
		int unitsNoPeerWasToldAbout = 0;
		int describedUnitsInPairedRecords = 0;
		int detailedTransforms = 0;
		int renderedBeyondTolerance = 0;
		float worstRenderedOffset = 0f;
		int rotationDeviationCount = 0;
		float largestRotationDeviation = 0f;
		var placementStreaks = new Dictionary<int, PlacementStreak[]>();
		var completedPlacementStreaks = new List<(int EntityId, int Quantity, PlacementStreak Streak)>();

		// One sample per physics tick. The client applies a snapshot when the packet arrives, so a tick that receives a
		// clump of them writes a record per snapshot, all of the same instant, and the follower and the renderer advance
		// once. Judging every record of a clump measured that one instant as dozens of consecutive samples; the last
		// record of each tick is the state that tick actually produced.
		Dictionary<ulong, int> lastSequencePerFrame = new();
		foreach (KeyValuePair<int, MultiplayerSnapshotRecord> presentedEntry in presentedRecords)
		{
			lastSequencePerFrame.TryGetValue(presentedEntry.Value.PhysicsFrame, out int lastSequence);
			if (presentedEntry.Key > lastSequence) lastSequencePerFrame[presentedEntry.Value.PhysicsFrame] = presentedEntry.Key;
		}

		int collapsedRepeatedSamples = 0;
		foreach (KeyValuePair<int, MultiplayerSnapshotRecord> hostEntry in hostRecords)
		{
			if (!presentedRecords.TryGetValue(hostEntry.Key, out MultiplayerSnapshotRecord? presentedRecord)) continue;
			if (lastSequencePerFrame[presentedRecord.PhysicsFrame] != hostEntry.Key)
			{
				collapsedRepeatedSamples++;
				continue;
			}

			pairedSequences++;
			describedUnitsInPairedRecords += hostEntry.Value.Units.Count;

			foreach (MultiplayerSnapshotUnit hostUnit in hostEntry.Value.Units.Values)
			{
				if ((hostUnit.Flags & 8) == 0)
				{
					unitsNoPeerWasToldAbout++;
					continue;
				}

				if (!presentedRecord.Units.TryGetValue(hostUnit.EntityId, out MultiplayerSnapshotUnit? presentedUnit))
				{
					// A unit the client has not applied yet has nothing to render: its spawn was in a snapshot that has not
					// arrived or could not be placed. The state comparison owns that question, so counting it here would report
					// one defect twice; an entity the client did apply but did not render is a rendering defect and fails below.
					bool appliedByClient = appliedRecords.TryGetValue(hostEntry.Key, out MultiplayerSnapshotRecord? missingUnitAppliedRecord)
						&& missingUnitAppliedRecord.Units.ContainsKey(hostUnit.EntityId);
					if (!appliedByClient)
					{
						notYetAppliedUnits++;
						continue;
					}

					mismatchCount++;
					if (mismatches.Count < 20) mismatches.Add($"seq {hostEntry.Key}: rendered entity {hostUnit.EntityId} is missing on the client, which had already applied that entity's state.");
					continue;
				}

				if ((presentedUnit.Flags & 16) != 0)
				{
					skippedPredictedTransforms++;
					continue;
				}

				comparedTransforms++;
				if ((presentedUnit.Flags & 32) != 0) culledTransforms++;				// The host states a unit at one precision, so the rendered transform is held to the authoritative position
				// directly: the description the client applied is the movement threshold plus the catch-up interpolation
				// allows, and nothing about where the peer's camera happens to be widens either.
				detailedTransforms++;


				PlacementOffset observed = new(
					MaxAxisOffset(hostUnit.PositionX, hostUnit.PositionY, hostUnit.PositionZ, presentedUnit.PositionX, presentedUnit.PositionY, presentedUnit.PositionZ),
					MaxAxisOffset(presentedUnit.AppliedPositionX, presentedUnit.AppliedPositionY, presentedUnit.AppliedPositionZ, presentedUnit.PositionX, presentedUnit.PositionY, presentedUnit.PositionZ),
					MaxAxisOffset(hostUnit.PositionX, hostUnit.PositionY, hostUnit.PositionZ, presentedUnit.AppliedPositionX, presentedUnit.AppliedPositionY, presentedUnit.AppliedPositionZ));

				if (observed.HostToRendered > placementTolerance) renderedBeyondTolerance++;
				if (observed.HostToRendered > worstRenderedOffset) worstRenderedOffset = observed.HostToRendered;

				TrackPlacementStreak(placementStreaks, completedPlacementStreaks, hostUnit.EntityId, PlacementQuantityRendererFidelity, true, hostEntry.Key, in observed, PresentationPositionTolerance);
				TrackPlacementStreak(placementStreaks, completedPlacementStreaks, hostUnit.EntityId, PlacementQuantityAuthority, true, hostEntry.Key, in observed, detailedAuthorityTolerance);

				if (observed.HostToRendered > placementTolerance)
				{
					float rotationDeviation = RotationDifference(hostUnit.RotationY, presentedUnit.RotationY);
					if (rotationDeviation > PresentationRotationTolerance) rotationDeviationCount++;
					if (rotationDeviation > largestRotationDeviation) largestRotationDeviation = rotationDeviation;
				}
			}
		}

		foreach (KeyValuePair<int, PlacementStreak[]> streakEntry in placementStreaks)
		{
			for (int quantity = 0; quantity < PlacementQuantityCount; quantity++)
			{
				PlacementStreak streak = streakEntry.Value[quantity];
				if (streak.Count <= PlacementLagAllowanceSequences) continue;

				completedPlacementStreaks.Add((streakEntry.Key, quantity, streak));
			}
		}

		// Every streak that crossed the allowance is reported, including the ones that healed before the client stopped
		// recording. Inspecting only the streaks still open at the last record is how a unit that spent a second and a
		// half five metres behind the authority went unreported.
		foreach ((int entityId, int quantity, PlacementStreak streak) in completedPlacementStreaks)
		{
			mismatchCount++;
			if (mismatches.Count < 20) mismatches.Add(DescribePlacementStreak(entityId, quantity, streak));
		}

		report.AddSummaryLine($"Presentation parity window: from {MultiplayerSyncDiagnostics.PresentationSettleSeconds:F1}s to {MultiplayerSyncDiagnostics.PresentationWindowSeconds:F1}s after a baseline, paired ticks: {pairedSequences}, compared rendered transforms: {comparedTransforms} ({detailedTransforms} of a stated position), placement mismatches: {mismatchCount}");
		report.AddSummaryLine($"Records sharing a physics tick with a later record of the same tick: {collapsedRepeatedSamples} (the follower and the renderer advance once per tick, so only the last record of a tick is the state that tick produced)");
		report.AddSummaryLine($"A rendered transform is judged by what the protocol promised it: within {detailedAuthorityTolerance:F2} m of the authority ({StatedPositionThresholdMeters:F2} m resend threshold + {interpolationLagMeters:F2} m interpolation catch-up + {placementTolerance:F2} m of {profile} allowance) and within {PresentationPositionTolerance:F2} m of the client's own applied state. The catch-up is the run's fastest {fastestUnitSpeed:F2} m/s over its {interpolationBufferSeconds:F2} s interpolation buffer.");
		report.AddSummaryLine($"Locally predicted rendered transforms excluded from placement parity: {skippedPredictedTransforms} (prediction intentionally leads the authoritative position).");
		report.AddSummaryLine($"Host units the client had not applied yet, so there was nothing to render: {notYetAppliedUnits} (the state comparison owns whether the client received them).");
		report.AddSummaryLine($"Rendered transforms outside the camera frustum: {culledTransforms} of {comparedTransforms} (culling freezes rotation but still advances position, so they stay comparable).");
		report.AddSummaryLine($"Rendered transforms more than {placementTolerance:F2} m from the authoritative position: {renderedBeyondTolerance} of {comparedTransforms}, worst {worstRenderedOffset:F2} m; a moving unit trails the authority by its interpolation buffer, so this figure is context rather than an assertion.");			report.AddSummaryLine($"Follower lags closed deliberately because interpolation could not account for them: {ReadFollowerCatchUpCount(outputDirectory)} (several descriptions can land between two physics ticks, and the excess over speed/rate is not something smoothing can absorb)");
		report.AddSummaryLine($"Rendered rotation deviations beyond {PresentationRotationTolerance:F2} rad: {rotationDeviationCount} of {comparedTransforms}, largest {largestRotationDeviation:F2} rad (off-frustum units keep their frozen transform until they enter the view)");
		// The authority marks a unit as comparable only when every seated peer was told about it, which a room of this size
		// with the shroud on almost never satisfies. That is a limit of what a single shared host record can say, not a
		// measurement of the client, so it is reported as unmeasured rather than as a defect.
		bool parityMeasureWasEmpty = comparedTransforms == 0 && (unitsNoPeerWasToldAbout > 0 || describedUnitsInPairedRecords == 0);
		if (parityMeasureWasEmpty)
		{
			report.AddSummaryLine($"Rendered placement parity was not measured: the {pairedSequences} paired host records described {describedUnitsInPairedRecords} units in total, of which {unitsNoPeerWasToldAbout} were comparable for a room this size, so there was no rendered transform to hold against an authority position.");
		}
		else if (pairedSequences == 0) report.AddFailure("No rendered presentation records paired with host snapshots, so placement parity was not verified.");
		else if (comparedTransforms < 20) report.AddFailure($"Only {comparedTransforms} rendered unit transforms were compared in the presentation window.");

		if (mismatchCount > 0)
		{			report.AddFailure($"{mismatchCount} rendered placement mismatches were detected inside the presentation window: a rendered transform, or the applied state behind it, stayed beyond its tolerance for more than {PlacementLagAllowanceSequences} consecutive records. Each entry names the quantity that left its bound and the three distances at its worst sequence.");
			report.AddSummaryLine("First presentation mismatches:");
			foreach (string mismatch in mismatches) report.AddSummaryLine($"  {mismatch}");
		}
	}

	/// <summary>
	/// The interpolation buffer the client derives from its measured round trip, reproduced here from the same formula
	/// (<see cref="NetworkService.ComputeDynamicInterpolationFactor" />) so the bound on a unit's visual lag follows the
	/// profile the run injected instead of a single constant that would be wrong on every profile but one. The round trip
	/// is read from both ways a profile can inject one: a latency profile states the ping it delays by, and a lossy profile
	/// states the one-way delay its relay applies in each direction. Reading only the one-way delay left the lagged run
	/// bounded as if it were played on the minimum buffer, four sequences short of the buffer the client had measured for
	/// itself, and three units moving at speed were reported for trailing by exactly that difference.
	/// </summary>
	private static float ResolveInterpolationBufferSeconds(MultiplayerEcsRunSettings settings)
	{
		float roundTripSeconds = MathF.Max(settings.PingMilliseconds, settings.OneWayDelayMilliseconds * 2f) / 1000f;
		return MathF.Max(MatchTiming.MinimumInterpolationBufferSeconds, roundTripSeconds * 1.5f);
	}

	/// <summary>
	/// Reads the count of descriptions the client could not glide to from the record it wrote at the end of the run. Zero
	/// when the file or the line is missing, which a run recorded before this counter existed would be.
	/// </summary>
	private static int ReadFollowerCatchUpCount(string clientDirectory)
	{
		string path = Path.Combine(clientDirectory, "ECS_client_replication.txt");
		if (!File.Exists(path)) return 0;

		foreach (string line in File.ReadLines(path))
		{
			const string prefix = "follower_catch_ups=";
			if (!line.StartsWith(prefix, StringComparison.Ordinal)) continue;
			return int.TryParse(line.AsSpan(prefix.Length), out int count) ? count : 0;
		}

		return 0;
	}

	/// <summary>
	/// The fastest travel the authority recorded, measured from consecutive host records. A first-order interpolation
	/// that chases the replicated position settles at speed divided by its rate, so the bound needs the speed the run
	/// actually reached rather than a guess.
	/// </summary>
	private static float MeasureFastestUnitSpeed(SortedDictionary<int, MultiplayerSnapshotRecord> hostRecords)
	{
		float fastest = 0f;
		MultiplayerSnapshotRecord? previous = null;
		foreach (MultiplayerSnapshotRecord record in hostRecords.Values)
		{
			if (previous != null && record.Sequence == previous.Sequence + 1)
			{
				foreach (MultiplayerSnapshotUnit unit in record.Units.Values)
				{
					if (!previous.Units.TryGetValue(unit.EntityId, out MultiplayerSnapshotUnit? before)) continue;
					float step = MaxAxisOffset(unit.PositionX, unit.PositionY, unit.PositionZ, before.PositionX, before.PositionY, before.PositionZ);
					if (step > MaximumTravelStepMeters) continue;
					float speed = step * SnapshotRecordsPerSecond;
					if (speed > fastest) fastest = speed;
				}
			}

			previous = record;
		}

		return fastest;
	}

	private static float MaxAxisOffset(float leftX, float leftY, float leftZ, float rightX, float rightY, float rightZ)
	{
		float offsetX = MathF.Abs(leftX - rightX);
		float offsetY = MathF.Abs(leftY - rightY);
		float offsetZ = MathF.Abs(leftZ - rightZ);
		return MathF.Max(offsetX, MathF.Max(offsetY, offsetZ));
	}

	/// <summary>
	/// Continues or restarts one measured distance for one unit, so a sustained departure is reported once with the
	/// sequence range and the worst sample in it instead of once per record. Passing <paramref name="tracked"/> false
	/// clears the measurement: a unit that crosses the detailed radius must not carry a streak earned at the other
	/// precision into the tolerance it is now judged by. A streak that ends is handed to <paramref name="completed"/>,
	/// because a departure that healed still happened and the run has to report it.
	/// </summary>
	private static void TrackPlacementStreak(Dictionary<int, PlacementStreak[]> streaks, List<(int EntityId, int Quantity, PlacementStreak Streak)> completed, int entityId, int quantity, bool tracked, int sequence, in PlacementOffset observed, float tolerance)
	{
		if (!streaks.TryGetValue(entityId, out PlacementStreak[]? perQuantity))
		{
			if (!tracked) return;
			perQuantity = new PlacementStreak[PlacementQuantityCount];
			streaks[entityId] = perQuantity;
		}

		ref PlacementStreak streak = ref perQuantity[quantity];
		float distance = observed.DistanceFor(quantity);
		if (!tracked || distance <= tolerance)
		{
			ClosePlacementStreak(completed, entityId, quantity, streak);
			streak = default;
			return;
		}

		if (streak.Count == 0 || sequence != streak.LastSequence + 1)
		{
			ClosePlacementStreak(completed, entityId, quantity, streak);
			streak = default;
			streak.FirstSequence = sequence;
		}

		streak.Count++;
		streak.LastSequence = sequence;
		if (distance > streak.WorstOffset)
		{
			streak.WorstOffset = distance;
			streak.WorstSequence = sequence;
			streak.WorstHostToRendered = observed.HostToRendered;
			streak.WorstAppliedToRendered = observed.AppliedToRendered;
			streak.WorstHostToApplied = observed.HostToApplied;
		}
	}

	/// <summary>
	/// Names the quantity that left its bound, so a red run says whether the renderer stopped following the client's own
	/// simulation or whether a unit the host described was rendered loosely.
	/// </summary>
	private static void ClosePlacementStreak(List<(int EntityId, int Quantity, PlacementStreak Streak)> completed, int entityId, int quantity, in PlacementStreak streak)
	{
		if (streak.Count <= PlacementLagAllowanceSequences) return;
		completed.Add((entityId, quantity, streak));
	}

	private static string DescribePlacementStreak(int entityId, int quantity, PlacementStreak streak)
	{
		string explanation = quantity switch
		{
			PlacementQuantityRendererFidelity => $"the rendered transform stayed {streak.WorstOffset:F2} m from the state the client had already applied, so the renderer stopped following the client's own simulation",
			_ => $"the host described this unit and the rendered transform stayed {streak.WorstOffset:F2} m from the authoritative position, beyond the {StatedPositionThresholdMeters:F2} m resend threshold plus the interpolation catch-up the protocol allows"
		};

		return $"entity {entityId} for {streak.Count} consecutive records (sequences {streak.FirstSequence}-{streak.LastSequence}): {explanation}. At sequence {streak.WorstSequence} the render was {streak.WorstHostToRendered:F2} m from the authority, {streak.WorstAppliedToRendered:F2} m from the applied state, and the applied state was {streak.WorstHostToApplied:F2} m from the authority.";
	}

	private readonly struct PlacementOffset
	{
		public readonly float HostToRendered;
		public readonly float AppliedToRendered;
		public readonly float HostToApplied;

		public PlacementOffset(float hostToRendered, float appliedToRendered, float hostToApplied)
		{
			HostToRendered = hostToRendered;
			AppliedToRendered = appliedToRendered;
			HostToApplied = hostToApplied;
		}

		public float DistanceFor(int quantity) => quantity switch
		{
			PlacementQuantityRendererFidelity => AppliedToRendered,
			_ => HostToRendered
		};
	}

	private struct PlacementStreak
	{
		public int Count;
		public int FirstSequence;
		public int LastSequence;
		public float WorstOffset;
		public int WorstSequence;
		public float WorstHostToRendered;
		public float WorstAppliedToRendered;
		public float WorstHostToApplied;
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

	/// <summary>
	/// How many consecutive delta records a field may stay divergent before it counts as divergence rather than a
	/// repair in progress. The authority describes a unit again when what it holds differs from what the peer was last
	/// told, so a single lost description is repaired by the next one that changes it; a lossy profile gets two windows
	/// because the record that carries the repair can be dropped too.
	/// </summary>
	private const int DeltaDivergenceRecordAllowance = 15;

	private static int ResolveDeltaDivergenceRecordAllowance(MultiplayerEcsRunSettings settings)
		=> settings.PacketLossPercent > 0
			? 2 * DeltaDivergenceRecordAllowance + 1
			: DeltaDivergenceRecordAllowance + 1;

	private static void ReportDeltaDivergences(MultiplayerEcsSyncReport report, List<(int EntityId, string Field, int Sequence, string Message)> divergences, MultiplayerEcsRunSettings settings)
	{
		if (divergences.Count == 0) return;

		int allowance = ResolveDeltaDivergenceRecordAllowance(settings);

		// A divergence counts against the retransmit window only while it is unbroken: two scattered single-record
		// differences an order apart are two repaired retransmits, not one long divergence.
		var streaks = new Dictionary<(int EntityId, string Field), (int Count, int FirstSequence, int LastSequence, string Message)>();
		var reportedStreaks = new List<(int EntityId, string Field, int Count, int FirstSequence, int LastSequence, string Message)>();
		for (int index = 0; index < divergences.Count; index++)
		{
			var divergence = divergences[index];
			var key = (divergence.EntityId, divergence.Field);
			if (streaks.TryGetValue(key, out var streak) && divergence.Sequence == streak.LastSequence + 1)
			{
				streaks[key] = (streak.Count + 1, streak.FirstSequence, divergence.Sequence, divergence.Message);
				continue;
			}

			if (streaks.TryGetValue(key, out var finished)) reportedStreaks.Add((key.EntityId, key.Field, finished.Count, finished.FirstSequence, finished.LastSequence, finished.Message));
			streaks[key] = (1, divergence.Sequence, divergence.Sequence, divergence.Message);
		}

		foreach (KeyValuePair<(int EntityId, string Field), (int Count, int FirstSequence, int LastSequence, string Message)> remaining in streaks)
		{
			reportedStreaks.Add((remaining.Key.EntityId, remaining.Key.Field, remaining.Value.Count, remaining.Value.FirstSequence, remaining.Value.LastSequence, remaining.Value.Message));
		}

		int healedCount = 0;
		foreach (var streak in reportedStreaks)
		{
			if (streak.Count > allowance)
			{
				report.AddFailure($"entity {streak.EntityId} {streak.Field} diverged on {streak.Count} consecutive delta records between sequences {streak.FirstSequence} and {streak.LastSequence}, beyond the {allowance}-record retransmit window; last observation: {streak.Message}");
				continue;
			}

			healedCount += streak.Count;
		}

		report.AddSummaryLine($"Unit fields that diverged on a delta record and healed inside the {allowance}-record retransmit window: {healedCount} (counted from {divergences.Count} observations)");
	}

	private static void CompareSnapshotRecord(MultiplayerSnapshotRecord hostRecord, MultiplayerSnapshotRecord clientRecord, bool exact, List<string> mismatches, List<string> digestMismatches, ref int comparedUnits, ref int offscreenMissingUnits, List<(int EntityId, string Field, int Sequence, string Message)> deltaDivergences)
	{
		int mismatchCountBefore = mismatches.Count;

		void ReportUnitField(int entityId, string field, string message)
		{
			if (exact)
			{
				mismatches.Add(message);
				return;
			}

			deltaDivergences.Add((entityId, field, hostRecord.Sequence, message));
		}

		foreach (MultiplayerSnapshotUnit hostUnit in hostRecord.Units.Values)
		{
			bool isDead = (hostUnit.Flags & 1) != 0;
			bool isVisibleToRemoteClient = (hostUnit.Flags & 8) != 0;
			bool isDetailedForThisClient = (hostUnit.Flags & 4) != 0;
			bool exactUnit = exact && isDetailedForThisClient;
			float positionTolerance = exactUnit ? BaselineTolerance : DeltaPositionTolerance;
			float rotationTolerance = exactUnit ? BaselineTolerance : DeltaRotationTolerance;
			float healthTolerance = exactUnit ? BaselineTolerance : DeltaHealthTolerance;

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

			if (!isVisibleToRemoteClient) continue;

			if (hostUnit.OwnerPlayerIndex != clientUnit.OwnerPlayerIndex)
			{
				ReportUnitField(hostUnit.EntityId, "owner", $"seq {hostRecord.Sequence}: entity {hostUnit.EntityId} owner slot host={hostUnit.OwnerPlayerIndex} client={clientUnit.OwnerPlayerIndex}.");
			}

			bool hostIsBuilding = (hostUnit.Flags & 2) != 0;
			bool clientIsBuilding = (clientUnit.Flags & 2) != 0;
			if (hostIsBuilding != clientIsBuilding)
			{
				ReportUnitField(hostUnit.EntityId, "building", $"seq {hostRecord.Sequence}: entity {hostUnit.EntityId} building host={hostIsBuilding} client={clientIsBuilding}.");
			}

			bool clientDead = (clientUnit.Flags & 1) != 0;
			if (isDead != clientDead)
			{
				ReportUnitField(hostUnit.EntityId, "dead", $"seq {hostRecord.Sequence}: entity {hostUnit.EntityId} dead host={isDead} client={clientDead}.");
			}

			if (isDead || clientDead) continue;

			float dx = hostUnit.PositionX - clientUnit.PositionX;
			float dy = hostUnit.PositionY - clientUnit.PositionY;
			float dz = hostUnit.PositionZ - clientUnit.PositionZ;
			if (MathF.Abs(dx) > positionTolerance || MathF.Abs(dy) > positionTolerance || MathF.Abs(dz) > positionTolerance)
			{
				ReportUnitField(hostUnit.EntityId, "position", $"seq {hostRecord.Sequence}: entity {hostUnit.EntityId} position host=({hostUnit.PositionX:F3},{hostUnit.PositionY:F3},{hostUnit.PositionZ:F3}) client=({clientUnit.PositionX:F3},{clientUnit.PositionY:F3},{clientUnit.PositionZ:F3}).");
			}

			if (RotationDifference(hostUnit.RotationY, clientUnit.RotationY) > rotationTolerance)
			{
				ReportUnitField(hostUnit.EntityId, "rotation", $"seq {hostRecord.Sequence}: entity {hostUnit.EntityId} rotation host={hostUnit.RotationY:F3} client={clientUnit.RotationY:F3}.");
			}

			if (MathF.Abs(hostUnit.CurrentHp - clientUnit.CurrentHp) > healthTolerance)
			{
				ReportUnitField(hostUnit.EntityId, "health", $"seq {hostRecord.Sequence}: entity {hostUnit.EntityId} health host={hostUnit.CurrentHp:F3} client={clientUnit.CurrentHp:F3}.");
			}

			if (MathF.Abs(hostUnit.MaxHp - clientUnit.MaxHp) > healthTolerance)
			{
				ReportUnitField(hostUnit.EntityId, "max health", $"seq {hostRecord.Sequence}: entity {hostUnit.EntityId} max health host={hostUnit.MaxHp:F3} client={clientUnit.MaxHp:F3}.");
			}

			CompareDigestSets(hostRecord.Sequence, $"entity {hostUnit.EntityId}", hostUnit.ComponentDigests, clientUnit.ComponentDigests, digestMismatches);
		}

		if (!exact) return;

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

	/// <summary>
	/// Reports whether one discarded snapshot cost the peer exactly one baseline. A repair that repeats describes the
	/// peer from scratch on every cooldown for a hole it no longer has, which is the difference between a protocol that
	/// heals and one that never stops healing.
	/// </summary>
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
		if (baselinesAfter != 1)
		{
			report.AddFailure($"A discarded snapshot produced {baselinesAfter} baselines instead of exactly one, so the repair either never happened or repeated after the hole was already filled.");
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
				if (line.Contains("is missing a snapshot it was sent", StringComparison.Ordinal)) repairCount++;
			}
		}

		report.AddSummaryLine($"Repair scenario: the authority logged {repairCount} repair(s) for the discarded snapshot.");
		if (repairCount != 1)
		{
			report.AddFailure($"The authority logged {repairCount} repairs for one discarded snapshot; a peer that misses one change is meant to be described from scratch once, not once per cooldown.");
		}
	}

	/// <summary>
	/// Finds the logs the authority itself wrote for this run. It writes beside the output directory it is given, so the
	/// harness wrapper's own stdout is a fallback rather than the place the repair is recorded.
	/// </summary>
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

	/// <summary>
	/// The sequences the host actually sent to this client's peer, so a sequence that never arrived can be attributed:
	/// the host stopping its stream is a liveness defect, while a sequence it sent and the client never saw is transport
	/// loss. The file is absent for the reconnect scenario, where the client's peer id changes mid-run and the coverage
	/// of the old id would mislabel the induced outage.
	/// </summary>
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

	/// <summary>
	/// A peer that has just joined is not streamed to until the host has an acknowledgement from it, so the sequences
	/// between its first pairing and the first sequence the host sent it are join latency. A gap after that is the host
	/// having stopped the stream, which is a liveness defect rather than transport loss.
	/// </summary>
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

	/// <summary>
	/// Every peer id the client process has connected with. A client that loses the host while the map is loading
	/// reconnects with a new id, and both ids describe the same process, so attribution by a single id mislabels every
	/// baseline the other connection received.
	/// </summary>
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
			int baselinePeerCount = reader.ReadInt32();
			HashSet<int> baselinePeerIds = new(baselinePeerCount);
			for (int index = 0; index < baselinePeerCount; index++) baselinePeerIds.Add(reader.ReadInt32());
			ulong physicsFrame = reader.ReadUInt64();
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
					AppliedPositionX = reader.ReadSingle(),
					AppliedPositionY = reader.ReadSingle(),
					AppliedPositionZ = reader.ReadSingle(),
					Flags = reader.ReadByte(),
					ComponentDigests = ReadComponentDigests(reader)
				};
				units[unit.EntityId] = unit;
			}

			MultiplayerSnapshotRecord record = new() { Sequence = sequence, PhysicsFrame = physicsFrame, IsBaseline = isBaseline, BaselinePeerIds = baselinePeerIds, Units = units };
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
				PolyHash = reader.ReadUInt32(),
				ObstacleVolumeCount = reader.ReadInt32(),
				ObstacleHash = reader.ReadUInt32()
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
		public ulong PhysicsFrame;
		public bool IsBaseline;
		public HashSet<int> BaselinePeerIds = new();
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
		public int ObstacleVolumeCount;
		public uint ObstacleHash;
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
		public float AppliedPositionX;
		public float AppliedPositionY;
		public float AppliedPositionZ;
		public byte Flags;
		public Dictionary<int, uint> ComponentDigests = new();
	}
}
