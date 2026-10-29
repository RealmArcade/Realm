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
	public int PeerCount { get; init; } = 2;
	public int BotCount { get; init; }
	public float DropClientAtSeconds { get; init; }
	public float DiscardSnapshotAtSeconds { get; init; }
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

	public int ComparedLootCount { get; set; }
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
		builder.AppendLine($"Compared loot items: {ComparedLootCount}");
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
	private const float BaselineTolerance = 0.1f;
	private const float DeltaPositionTolerance = 1.05f;
	private const float DeltaRotationTolerance = 0.17f;
	private const float DeltaHealthTolerance = 1.05f;
	private const float LootPositionToleranceMeters = 0.1f;

	private const float LootRemainingLifetimeToleranceSeconds = 0.5f;
	private const float PresentationPositionTolerance = 0.25f;
	private const float PresentationRotationTolerance = 0.2f;
	private const float StatedPositionThresholdMeters = 0.05f;
	private const float SnapshotRecordsPerSecond = 60f;
	private const float MaximumTravelStepMeters = 2f;
	private const int PlacementQuantityRendererFidelity = 0;
	private const int PlacementQuantityAuthority = 1;
	private const int PlacementQuantityCount = 2;
	private const int PlacementLagAllowanceSequences = 30;
	private const int StartupWarmupSequenceCount = 60;
	private const int SyncRecordVersion = SyncRecordFormat.Version;

	private const float BackwardMotionAuthorityMeters = 0.05f;

	private const float BackwardMotionToleranceMeters = 0.02f;

	private readonly record struct TrackedPresentedPosition(int Sequence, float AuthorityX, float AuthorityZ, float PresentedX, float PresentedZ);

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

	public static string ResolveTestMapFolder(string environmentVariable, params string[] candidates)
	{
		string? fromEnvironment = System.Environment.GetEnvironmentVariable(environmentVariable);
		if (!string.IsNullOrEmpty(fromEnvironment) && Directory.Exists(fromEnvironment)) return fromEnvironment;

		for (int index = 0; index < candidates.Length; index++)
		{
			if (Directory.Exists(candidates[index])) return candidates[index];
		}

		throw new DirectoryNotFoundException($"No folder for this test map was found: set {environmentVariable} or provide one of {string.Join(", ", candidates)}.");
	}

	public static string EnsureMapWasm(string mapFolder)
	{
		string? projectPath = Directory.EnumerateFiles(mapFolder, "*.csproj").FirstOrDefault();
		if (projectPath == null) throw new FileNotFoundException($"A map script project was not found in {mapFolder}.");

		string assemblyName = Path.GetFileNameWithoutExtension(projectPath);
		string mapScriptPath = Path.Combine(mapFolder, "MapScript.cs");
		string publishedPath = Path.Combine(mapFolder, "bin", "Release", "net10.0", "wasi-wasm", "publish", $"{assemblyName}.wasm");
		string shippedPath = Path.Combine(mapFolder, $"{assemblyName}.wasm");

		if (File.Exists(shippedPath) && (!File.Exists(mapScriptPath) || File.GetLastWriteTimeUtc(shippedPath) >= File.GetLastWriteTimeUtc(mapScriptPath))) return shippedPath;
		if (File.Exists(publishedPath) && File.GetLastWriteTimeUtc(publishedPath) >= File.GetLastWriteTimeUtc(mapScriptPath)) return publishedPath;

		string wasmPath = publishedPath;
		string wasiSdkPath = WasiSdkResolver.ResolveWasiSdkPathWithoutGodot();
		var startInfo = new ProcessStartInfo("dotnet")
		{
			WorkingDirectory = mapFolder,
			UseShellExecute = false,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			CreateNoWindow = true
		};
		startInfo.ArgumentList.Add("publish");
		startInfo.ArgumentList.Add(Path.GetFileName(projectPath));
		startInfo.ArgumentList.Add("-c");
		startInfo.ArgumentList.Add("Release");
		startInfo.ArgumentList.Add("-r");
		startInfo.ArgumentList.Add("wasi-wasm");
		startInfo.ArgumentList.Add($"-p:WASI_SDK_PATH={wasiSdkPath}");
		startInfo.EnvironmentVariables["WASI_SDK_PATH"] = wasiSdkPath;

		using Process process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Failed to start the WASM build of {mapFolder}.");
		string output = process.StandardOutput.ReadToEnd();
		string error = process.StandardError.ReadToEnd();
		process.WaitForExit();
		if (process.ExitCode != 0 || !File.Exists(wasmPath))
		{
			throw new InvalidOperationException($"The WASM build of {mapFolder} failed (exit code {process.ExitCode}).\n{output}\n{error}");
		}

		return wasmPath;
	}

	public static (string MapFolder, string WasmPath) EnsureLootSpawningMap(string sourceMapFolder)
	{
		string sourceScriptPath = Path.Combine(sourceMapFolder, "MapScript.cs");
		if (!File.Exists(sourceScriptPath)) throw new FileNotFoundException($"A map script was not found at {sourceScriptPath}.");

		string mapFolder = Path.Combine(Path.GetTempPath(), "Realm_LootSpawningMap");
		string scriptPath = Path.Combine(mapFolder, "MapScript.cs");
		bool needsCopy = !File.Exists(scriptPath)
			|| File.GetLastWriteTimeUtc(scriptPath) < File.GetLastWriteTimeUtc(sourceScriptPath)
			|| !File.ReadAllText(scriptPath).Contains(LootSpawnerCall, StringComparison.Ordinal);

		if (needsCopy)
		{
			DeleteMapCopy(mapFolder);
			CopyMapFolder(sourceMapFolder, mapFolder);
			PatchLootSpawningScript(scriptPath);
		}

		return (mapFolder, EnsureMapWasm(mapFolder));
	}

	private const string LootSpawnerCall = "        SpawnSyncTestLoot(api, delta);\n";

	private const string LootSpawnerBody = @"
    private float _syncTestLootElapsed;

    private void SpawnSyncTestLoot(IGameAPI api, float delta)
    {
        _syncTestLootElapsed += delta;
        if (_syncTestLootElapsed < 3f) return;
        _syncTestLootElapsed = 0f;

        int spawned = 0;
        foreach (KeyValuePair<int, TileState> entry in tileStatesByUnitId)
        {
            if (entry.Value.Kind != TileKind.Hero) continue;

            api.SpawnLootItem(ItemIds.VitalityRing, entry.Value.AnchorPosition + new Vector3(6f, 0f, 0f), 1, 0f);
            spawned++;
            if (spawned >= 4) break;
        }
    }
";

	private static void PatchLootSpawningScript(string scriptPath)
	{
		string script = File.ReadAllText(scriptPath).Replace("\r\n", "\n");
		const string updateOpening = "    public void Update(IGameAPI api, float delta)\n    {\n";
		int updateIndex = script.IndexOf(updateOpening, StringComparison.Ordinal);
		if (updateIndex < 0) throw new InvalidOperationException($"The map script no longer states the Update method the loot fixture is inserted into ({scriptPath}).");

		script = script.Insert(updateIndex + updateOpening.Length, LootSpawnerCall);
		script = script.Insert(script.LastIndexOf('}'), LootSpawnerBody);
		File.WriteAllText(scriptPath, script);
	}

	private static void DeleteMapCopy(string mapFolder)
	{
		if (!Directory.Exists(mapFolder)) return;

		foreach (string file in Directory.GetFiles(mapFolder, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
		Directory.Delete(mapFolder, true);
	}

	private static void CopyMapFolder(string sourceFolder, string targetFolder)
	{
		Directory.CreateDirectory(targetFolder);
		foreach (string file in Directory.GetFiles(sourceFolder)) File.Copy(file, Path.Combine(targetFolder, Path.GetFileName(file)));

		foreach (string directory in Directory.GetDirectories(sourceFolder))
		{
			string name = Path.GetFileName(directory);
			if (name == "bin" || name == "obj") continue;
			CopyMapFolder(directory, Path.Combine(targetFolder, name));
		}
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
		startInfo.ArgumentList.Add($"--profile={settings.Profile}");
		startInfo.ArgumentList.Add($"--peers={Math.Max(2, settings.PeerCount)}");
		startInfo.ArgumentList.Add($"--peer-index={peerIndex}");
		startInfo.ArgumentList.Add($"--bots={(role == "host" ? settings.BotCount : 0)}");
		startInfo.ArgumentList.Add($"--wait-for-peer-ready={waitForPeerReadyFile}");
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
		HashSet<int> clientPeerIds = ReadClientPeerIds(clientOutputDirectory);
		int inFlightSequenceCount = 0;
		int supersededSequenceCount = 0;

		ReconnectExpectation? reconnectExpectation = ReadReconnectExpectation(settings, clientOutputDirectory, report);
		HashSet<int>? sequencesSentToPeer = reconnectExpectation == null ? ReadHostSentSequences(outputDirectory, clientOutputDirectory) : null;
		List<int> notSentSequences = new();

		int lastRecordedSendSequence = sequencesSentToPeer is { Count: > 0 } ? MaxOf(sequencesSentToPeer) : -1;
		int unrecordedSequenceCount = 0;
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
		ReferenceDigestCoverage referenceCoverage = new();
		int comparedNavigationBaselines = 0;
		int pathingMismatchCount = 0;
		int navMeshMissingCount = 0;
		int navMeshShapeDifferenceCount = 0;
		int navMeshAgreedRecords = 0;
		int navMeshDisagreementAfterAgreementCount = 0;
		int navMeshDisagreementBeforeAgreementCount = 0;
		int navMeshAwaitingObstacleInputCount = 0;
		int navMeshObstacleInputRun = 0;
		int longestObstacleInputRun = 0;
		int firstNavMeshAgreementSequence = -1;
		int lastAgreedHostPolyCount = 0;
		int lastAgreedHostGroundPolyCount = 0;
		int lastAgreedClientPolyCount = 0;
		int lastAgreedClientGroundPolyCount = 0;
		int lastAgreedObstacleVolumeCount = 0;
		List<string> navMeshObservations = new();
		List<int> hostNavigationChangeSequences = new();
		MultiplayerNavigationState? previousHostNavigation = null;
		int navMeshObstacleInputAllowance = ResolveObstacleInputAllowanceSequences(settings);

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

					if (reconnectExpectation != null && reconnectExpectation.Contains(hostEntry.Key))
					{
						reconnectOutageSequences++;
						continue;
					}

					if (clientSeenSequences.Contains(hostEntry.Key)) supersededSequenceCount++;
					else if (hostEntry.Key <= firstPairedSequence + StartupWarmupSequenceCount) startupCatchUpSequences.Add(hostEntry.Key);
					else if (sequencesSentToPeer != null && hostEntry.Key > lastRecordedSendSequence) unrecordedSequenceCount++;
					else if (sequencesSentToPeer != null && !sequencesSentToPeer.Contains(hostEntry.Key)) notSentSequences.Add(hostEntry.Key);
					else lostSequences.Add(hostEntry.Key);
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
			ulong clientPeerMask = BuildPeerMask(hostEntry.Value.DescribedPeerIds, clientPeerIds);
			if (hostSentBaselineToThisPeer)
			{
				foreach (MultiplayerLootState describedLoot in hostEntry.Value.Loot.Values)
				{
					if (describedLoot.NamedByPeers == SyncRecordFormat.NamedByEveryPeer || (describedLoot.NamedByPeers & clientPeerMask) != 0) comparedLootEntities++;
				}
			}

			CompareSnapshotRecord(hostEntry.Value, clientRecord, hostSentBaselineToThisPeer, mismatches, digestTarget, ref comparedUnits, ref offscreenMissingUnits, deltaDivergences, clientPeerMask, referenceCoverage);
			CompareScopeDigests(hostEntry.Value, clientRecord, clientPeerMask, digestTarget);
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

			if (hostEntry.Value.Navigation.HasNavMesh && clientRecord.Navigation.HasNavMesh)
			{
				bool shapeMatches = hostEntry.Value.Navigation.PolyCount == clientRecord.Navigation.PolyCount
					&& hostEntry.Value.Navigation.GroundPolyCount == clientRecord.Navigation.GroundPolyCount
					&& hostEntry.Value.Navigation.PolyHash == clientRecord.Navigation.PolyHash;
				if (shapeMatches)
				{
					navMeshObstacleInputRun = 0;
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
					bool obstacleInputDiffers = hostEntry.Value.Navigation.ObstacleVolumeCount != clientRecord.Navigation.ObstacleVolumeCount
						|| hostEntry.Value.Navigation.ObstacleHash != clientRecord.Navigation.ObstacleHash;
					if (obstacleInputDiffers && ++navMeshObstacleInputRun <= navMeshObstacleInputAllowance)
					{
						navMeshAwaitingObstacleInputCount++;
						if (navMeshObstacleInputRun > longestObstacleInputRun) longestObstacleInputRun = navMeshObstacleInputRun;
						continue;
					}

					navMeshDisagreementAfterAgreementCount++;
					if (navMeshObservations.Count < 20)
					{
						navMeshObservations.Add($"seq {hostEntry.Key}: the mesh agreed at seq {firstNavMeshAgreementSequence} and differs here: host=(polys {hostEntry.Value.Navigation.PolyCount}, ground {hostEntry.Value.Navigation.GroundPolyCount}, hash {hostEntry.Value.Navigation.PolyHash:x8}, obstacle volumes {hostEntry.Value.Navigation.ObstacleVolumeCount}, volumes {hostEntry.Value.Navigation.ObstacleHash:x8}) client=(polys {clientRecord.Navigation.PolyCount}, ground {clientRecord.Navigation.GroundPolyCount}, hash {clientRecord.Navigation.PolyHash:x8}, obstacle volumes {clientRecord.Navigation.ObstacleVolumeCount}, volumes {clientRecord.Navigation.ObstacleHash:x8}) after {navMeshObstacleInputRun} record(s) of differing obstacle input.");
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
		report.ComparedLootCount = comparedLootEntities;
		report.AddSummaryLine($"First paired sequence: {firstPairedSequence}");
		report.AddSummaryLine($"Last paired sequence: {lastPairedSequence}");
		int lostSequenceCount = lostSequences.Count;
		report.AddSummaryLine($"Host sequences missing on client after first pairing: {unpairedSequenceCount} (superseded by a newer snapshot: {supersededSequenceCount}, startup catch-up: {startupCatchUpSequences.Count}, the host never sent them to this peer: {notSentSequences.Count}, after the host's coverage record ended: {unrecordedSequenceCount}, sent but never received: {lostSequenceCount})");
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
		report.AddSummaryLine($"Navmesh waiting for the authority's obstacle volumes: {navMeshAwaitingObstacleInputCount} records, longest run {longestObstacleInputRun} of {navMeshObstacleInputAllowance} allowed (the authority compares volumes once a second and a peer bakes the set it was last told, so a difference over a changed set is the report travelling rather than a divergent bake)");
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
			report.AddFailure($"{navMeshMissingCount} baselines had no baked navmesh on one side, so a route could not be planned there.");
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

		report.AddSummaryLine(
			$"Reference digests: compared {referenceCoverage.Compared} and passed over {referenceCoverage.Skipped} whose target the authority did not describe to this peer (a reference is a statement only the peers it was sent to can be held to).");

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

	private static int ResolveLostSequenceTolerance(MultiplayerEcsRunSettings settings, int pairedSnapshots, int lostSequenceCount)
	{
		int allowancePercent = settings.Profile switch
		{
			NetworkProfile.Clean => 8,
			NetworkProfile.Latency => 12,
			NetworkProfile.LatencyJitter => 15,
			NetworkProfile.LatencyLoss => 45,
			_ => 12
		};

		int producedSequences = pairedSnapshots + lostSequenceCount;
		return Math.Max(10, producedSequences * allowancePercent / 100);
	}

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

		Dictionary<ulong, int> lastSequencePerFrame = new();
		foreach (KeyValuePair<int, MultiplayerSnapshotRecord> presentedEntry in presentedRecords)
		{
			lastSequencePerFrame.TryGetValue(presentedEntry.Value.PhysicsFrame, out int lastSequence);
			if (presentedEntry.Key > lastSequence) lastSequencePerFrame[presentedEntry.Value.PhysicsFrame] = presentedEntry.Key;
		}

		var previousPresented = new Dictionary<int, TrackedPresentedPosition>();
		int backwardRenderCount = 0;
		float worstBackwardMeters = 0f;

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

				comparedTransforms++;
				if ((presentedUnit.Flags & 32) != 0) culledTransforms++;
				detailedTransforms++;


				PlacementOffset observed = new(
					MaxAxisOffset(hostUnit.PositionX, hostUnit.PositionY, hostUnit.PositionZ, presentedUnit.PositionX, presentedUnit.PositionY, presentedUnit.PositionZ),
					MaxAxisOffset(presentedUnit.AppliedPositionX, presentedUnit.AppliedPositionY, presentedUnit.AppliedPositionZ, presentedUnit.PositionX, presentedUnit.PositionY, presentedUnit.PositionZ),
					MaxAxisOffset(hostUnit.PositionX, hostUnit.PositionY, hostUnit.PositionZ, presentedUnit.AppliedPositionX, presentedUnit.AppliedPositionY, presentedUnit.AppliedPositionZ));

				if (observed.HostToRendered > placementTolerance) renderedBeyondTolerance++;
				if (observed.HostToRendered > worstRenderedOffset) worstRenderedOffset = observed.HostToRendered;

				if (previousPresented.TryGetValue(hostUnit.EntityId, out TrackedPresentedPosition previous) && hostEntry.Key - previous.Sequence == 1)
				{
					float authorityDeltaX = hostUnit.PositionX - previous.AuthorityX;
					float authorityDeltaZ = hostUnit.PositionZ - previous.AuthorityZ;
					float authorityDistance = MathF.Sqrt(authorityDeltaX * authorityDeltaX + authorityDeltaZ * authorityDeltaZ);
					if (authorityDistance > BackwardMotionAuthorityMeters)
					{
						float presentedDeltaX = presentedUnit.PositionX - previous.PresentedX;
						float presentedDeltaZ = presentedUnit.PositionZ - previous.PresentedZ;
						float alongAuthority = (presentedDeltaX * authorityDeltaX + presentedDeltaZ * authorityDeltaZ) / authorityDistance;
						if (alongAuthority < -BackwardMotionToleranceMeters)
						{
							backwardRenderCount++;
							if (-alongAuthority > worstBackwardMeters) worstBackwardMeters = -alongAuthority;
						}
					}
				}

				previousPresented[hostUnit.EntityId] = new TrackedPresentedPosition(hostEntry.Key, hostUnit.PositionX, hostUnit.PositionZ, presentedUnit.PositionX, presentedUnit.PositionZ);

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

		foreach ((int entityId, int quantity, PlacementStreak streak) in completedPlacementStreaks)
		{
			mismatchCount++;
			if (mismatches.Count < 20) mismatches.Add(DescribePlacementStreak(entityId, quantity, streak));
		}

		report.AddSummaryLine($"Presentation parity window: from {MultiplayerSyncDiagnostics.PresentationSettleSeconds:F1}s to {MultiplayerSyncDiagnostics.PresentationWindowSeconds:F1}s after a baseline, paired ticks: {pairedSequences}, compared rendered transforms: {comparedTransforms} ({detailedTransforms} of a stated position), placement mismatches: {mismatchCount}");
		report.AddSummaryLine($"Records sharing a physics tick with a later record of the same tick: {collapsedRepeatedSamples} (the cursor and the renderer advance once per tick, so only the last record of a tick is the state that tick produced)");
		report.AddSummaryLine($"A rendered transform is judged by what the protocol promised it: within {detailedAuthorityTolerance:F2} m of the authority ({StatedPositionThresholdMeters:F2} m resend threshold + {interpolationLagMeters:F2} m of buffer at the run's fastest {fastestUnitSpeed:F2} m/s + {placementTolerance:F2} m of {profile} allowance) and within {PresentationPositionTolerance:F2} m of the client's own applied state. The buffer bound is the deepest delay the client may render from, {interpolationBufferSeconds:F2} s; the run reports the delay it actually held.");
		report.AddSummaryLine($"Host units the client had not applied yet, so there was nothing to render: {notYetAppliedUnits} (the state comparison owns whether the client received them).");
		report.AddSummaryLine($"Rendered transforms outside the camera frustum: {culledTransforms} of {comparedTransforms} (culling freezes rotation but still advances position, so they stay comparable).");
		report.AddSummaryLine($"Rendered transforms more than {placementTolerance:F2} m from the authoritative position: {renderedBeyondTolerance} of {comparedTransforms}, worst {worstRenderedOffset:F2} m; this is context rather than an assertion, because the renderer may legally be that far behind.");
		report.AddSummaryLine($"Buffer starvations: {ReadClientCounter(outputDirectory, "buffer_starvations=")} (the cursor reached the end of what had arrived, which is what deepens the delay), final delay {ReadClientCounter(outputDirectory, "interpolation_delay_sequences=")} sequences, samples drawn past the newest description: {ReadClientCounter(outputDirectory, "extrapolated_samples=")}");

		int lastHostSequence = 0;
		foreach (int hostSequence in hostRecords.Keys) lastHostSequence = hostSequence;

		float matchSeconds = lastHostSequence / (float)MatchTiming.SnapshotRateHz;
		if (settings.DurationSeconds > 0f && matchSeconds > 0f)
		{
			float simulatedFasterThanRealTime = matchSeconds / settings.DurationSeconds;
			report.AddSummaryLine($"Simulation pace: {matchSeconds:F1} s of match time in a {settings.DurationSeconds:F0} s run, {simulatedFasterThanRealTime:F1} times real time (an uncapped run simulates as fast as the machine allows, so every latency figure covers that much more of the match than it would in a game)");
		}
		report.AddSummaryLine($"Rendered rotation deviations beyond {PresentationRotationTolerance:F2} rad: {rotationDeviationCount} of {comparedTransforms}, largest {largestRotationDeviation:F2} rad (off-frustum units keep their frozen transform until they enter the view)");
		report.AddSummaryLine($"Rendered transforms that moved against the authority's own direction of travel: {backwardRenderCount} of {comparedTransforms}, worst {worstBackwardMeters:F2} m behind where it was drawn a sequence earlier (the cursor is the only thing that moves a unit, so this is what running out of world and being placed back on the newest description looks like)");
		report.AddSummaryLine($"Drawn positions further from the navigation mesh than {MultiplayerSyncDiagnostics.OffNavMeshToleranceMeters:F2} m: {ReadClientCounter(outputDirectory, "off_navmesh_units=")}, worst {ReadClientMetric(outputDirectory, "worst_off_navmesh_meters="):F2} m (a unit is drawn on the line joining two descriptions, so a corner of a building is how that line leaves the mesh)");
		bool parityMeasureWasEmpty = comparedTransforms == 0 && (unitsNoPeerWasToldAbout > 0 || describedUnitsInPairedRecords == 0);
		if (parityMeasureWasEmpty)
		{
			report.AddSummaryLine($"Rendered placement parity was not measured: the {pairedSequences} paired host records described {describedUnitsInPairedRecords} units in total, of which {unitsNoPeerWasToldAbout} were comparable for a room this size, so there was no rendered transform to hold against an authority position.");
		}
		else if (pairedSequences == 0) report.AddFailure("No rendered presentation records paired with host snapshots, so placement parity was not verified.");
		else if (comparedTransforms < 20) report.AddFailure($"Only {comparedTransforms} rendered unit transforms were compared in the presentation window.");

		int offNavMeshUnits = ReadClientCounter(outputDirectory, "off_navmesh_units=");
		if (offNavMeshUnits > 0)
		{
			report.AddFailure($"{offNavMeshUnits} drawn unit positions were more than {MultiplayerSyncDiagnostics.OffNavMeshToleranceMeters:F2} m from the navigation mesh this screen baked, worst {ReadClientMetric(outputDirectory, "worst_off_navmesh_meters="):F2} m, so the presentation put a unit inside geometry.");
		}

		int clientStarvations = ReadClientCounter(outputDirectory, "buffer_starvations=");
		if (backwardRenderCount > 0 && clientStarvations == 0)
		{
			report.AddFailure($"{backwardRenderCount} rendered transforms moved against the authority's direction of travel, worst {worstBackwardMeters:F2} m, and the buffer never ran out of world, so the cursor was not the cause.");
		}

		if (mismatchCount > 0)
		{
			report.AddFailure($"{mismatchCount} rendered placement mismatches were detected inside the presentation window: a rendered transform, or the applied state behind it, stayed beyond its tolerance for more than {PlacementLagAllowanceSequences} consecutive records. Each entry names the quantity that left its bound and the three distances at its worst sequence.");
			report.AddSummaryLine("First presentation mismatches:");
			foreach (string mismatch in mismatches) report.AddSummaryLine($"  {mismatch}");
		}
	}

	private static int ResolveObstacleInputAllowanceSequences(MultiplayerEcsRunSettings settings)
	{
		float oneWaySeconds = MathF.Max(settings.OneWayDelayMilliseconds, settings.PingMilliseconds / 2f) / 1000f;
		int linkSequences = (int)MathF.Ceiling((oneWaySeconds + (settings.JitterMilliseconds / 1000f)) * MatchTiming.SnapshotRateHz);
		return Math.Min(90, MatchTiming.SnapshotRateHz + linkSequences + 4);
	}

	private static float ResolveInterpolationBufferSeconds(MultiplayerEcsRunSettings settings)
	{
		return SnapshotTimeline.MaximumDelaySequences * MatchTiming.SnapshotIntervalSeconds;
	}

	private static float ReadClientMetric(string clientDirectory, string prefix)
	{
		string path = Path.Combine(clientDirectory, "ECS_client_replication.txt");
		if (!File.Exists(path)) return 0f;

		foreach (string line in File.ReadLines(path))
		{
			if (!line.StartsWith(prefix, StringComparison.Ordinal)) continue;
			return float.TryParse(line.AsSpan(prefix.Length), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float value) ? value : 0f;
		}

		return 0f;
	}

	private static int ReadClientCounter(string clientDirectory, string prefix)
	{
		string path = Path.Combine(clientDirectory, "ECS_client_replication.txt");
		if (!File.Exists(path)) return 0;

		foreach (string line in File.ReadLines(path))
		{
			if (!line.StartsWith(prefix, StringComparison.Ordinal)) continue;
			return int.TryParse(line.AsSpan(prefix.Length), out int count) ? count : 0;
		}

		return 0;
	}

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

	private static void CompareScopeDigests(MultiplayerSnapshotRecord hostRecord, MultiplayerSnapshotRecord clientRecord, ulong clientPeerMask, List<string> mismatches)
	{
		foreach (KeyValuePair<int, Dictionary<int, ComponentDigestEntry>> hostPlayer in hostRecord.Players)
		{
			if (!clientRecord.Players.TryGetValue(hostPlayer.Key, out Dictionary<int, ComponentDigestEntry>? clientDigests))
			{
				AddMismatch(mismatches, 0, $"seq {hostRecord.Sequence}: player slot {hostPlayer.Key} has no applied state on the client.");
				continue;
			}

			CompareDigestSets(hostRecord.Sequence, $"player slot {hostPlayer.Key}", hostPlayer.Value, clientDigests, SyncRecordFormat.NamedByEveryPeer, mismatches);
		}

		CompareDigestSets(hostRecord.Sequence, "world entity", hostRecord.WorldDigests, clientRecord.WorldDigests, SyncRecordFormat.NamedByEveryPeer, mismatches);

		CompareLootRecords(hostRecord, clientRecord, clientPeerMask, mismatches);
	}

	private static void CompareLootRecords(MultiplayerSnapshotRecord hostRecord, MultiplayerSnapshotRecord clientRecord, ulong clientPeerMask, List<string> mismatches)
	{
		foreach (KeyValuePair<int, MultiplayerLootState> hostLoot in hostRecord.Loot)
		{
			bool describedToThisPeer = hostLoot.Value.NamedByPeers == SyncRecordFormat.NamedByEveryPeer || (hostLoot.Value.NamedByPeers & clientPeerMask) != 0;
			if (!describedToThisPeer) continue;

			if (!clientRecord.Loot.TryGetValue(hostLoot.Key, out MultiplayerLootState? clientLoot))
			{
				AddMismatch(mismatches, 0, $"seq {hostRecord.Sequence}: loot {hostLoot.Key} ('{hostLoot.Value.ItemTypeId}') has no applied state on the client although the host described it to this peer.");
				continue;
			}

			CompareLootValues(hostRecord.Sequence, hostLoot.Key, hostLoot.Value, clientLoot, mismatches);
		}

		foreach (int clientLootId in clientRecord.Loot.Keys)
		{
			if (hostRecord.Loot.ContainsKey(clientLootId)) continue;
			AddMismatch(mismatches, 0, $"seq {hostRecord.Sequence}: the client holds loot {clientLootId} that the host does not.");
		}
	}

	private static void CompareLootValues(int sequence, int uniqueId, MultiplayerLootState hostLoot, MultiplayerLootState clientLoot, List<string> mismatches)
	{
		if (!string.Equals(hostLoot.ItemTypeId, clientLoot.ItemTypeId, StringComparison.Ordinal))
		{
			AddMismatch(mismatches, 0, $"seq {sequence}: loot {uniqueId} is '{hostLoot.ItemTypeId}' on the host and '{clientLoot.ItemTypeId}' on the client.");
		}

		if (hostLoot.Charges != clientLoot.Charges) AddMismatch(mismatches, 0, $"seq {sequence}: loot {uniqueId} holds {hostLoot.Charges} charges on the host and {clientLoot.Charges} on the client.");
		if (hostLoot.OwnerPlayerIndex != clientLoot.OwnerPlayerIndex) AddMismatch(mismatches, 0, $"seq {sequence}: loot {uniqueId} belongs to slot {hostLoot.OwnerPlayerIndex} on the host and {clientLoot.OwnerPlayerIndex} on the client.");
		if (hostLoot.AutoPickup != clientLoot.AutoPickup) AddMismatch(mismatches, 0, $"seq {sequence}: loot {uniqueId} takes itself up on the host={hostLoot.AutoPickup} and on the client={clientLoot.AutoPickup}.");

		float remainingLifeDifference = MathF.Abs(hostLoot.RemainingLifetime - clientLoot.RemainingLifetime);
		if (remainingLifeDifference > LootRemainingLifetimeToleranceSeconds)
		{
			AddMismatch(mismatches, 0, $"seq {sequence}: loot {uniqueId} has {hostLoot.RemainingLifetime:F2}s left on the host and {clientLoot.RemainingLifetime:F2}s on the client, {remainingLifeDifference:F2}s apart.");
		}

		float displacement = System.Numerics.Vector3.Distance(
			new System.Numerics.Vector3(hostLoot.PositionX, hostLoot.PositionY, hostLoot.PositionZ),
			new System.Numerics.Vector3(clientLoot.PositionX, clientLoot.PositionY, clientLoot.PositionZ));
		if (displacement > LootPositionToleranceMeters)
		{
			AddMismatch(mismatches, 0, $"seq {sequence}: loot {uniqueId} lies {displacement:F2} m from where the host described it.");
		}
	}

	private static void CompareDigestSets(int sequence, string owner, Dictionary<int, ComponentDigestEntry> hostDigests, Dictionary<int, ComponentDigestEntry> clientDigests, ulong peerMask, List<string> mismatches, ReferenceDigestCoverage? coverage = null)
	{
		const int mismatchCountBefore = 0;
		foreach (KeyValuePair<int, ComponentDigestEntry> hostDigest in hostDigests)
		{
			if (!hostDigest.Value.IsNameableBy(peerMask))
			{
				if (hostDigest.Value.IsScoped) coverage?.RecordSkipped();
				continue;
			}

			if (hostDigest.Value.IsScoped) coverage?.RecordCompared();

			string componentName = DescribeComponent(hostDigest.Key);
			if (!clientDigests.TryGetValue(hostDigest.Key, out ComponentDigestEntry clientDigest))
			{
				AddMismatch(mismatches, mismatchCountBefore, $"seq {sequence}: {owner} carries {componentName} on the host but not in the client's applied ECS.");
				continue;
			}

			if (clientDigest.Hash != hostDigest.Value.Hash)
			{
				AddMismatch(mismatches, mismatchCountBefore, $"seq {sequence}: {owner} {componentName} differs between the host ECS ({hostDigest.Value.Hash:x8}) and the client's applied ECS ({clientDigest.Hash:x8}).");
			}
		}

		foreach (KeyValuePair<int, ComponentDigestEntry> clientDigest in clientDigests)
		{
			if (hostDigests.TryGetValue(clientDigest.Key, out ComponentDigestEntry hostDigest) && !hostDigest.IsNameableBy(peerMask)) continue;
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

	private const int DeltaDivergenceRecordAllowance = 15;

	private static int ResolveDeltaDivergenceRecordAllowance(MultiplayerEcsRunSettings settings)
		=> settings.PacketLossPercent > 0
			? 2 * DeltaDivergenceRecordAllowance + 1
			: DeltaDivergenceRecordAllowance + 1;

	private static void ReportDeltaDivergences(MultiplayerEcsSyncReport report, List<(int EntityId, string Field, int Sequence, string Message)> divergences, MultiplayerEcsRunSettings settings)
	{
		if (divergences.Count == 0) return;

		int allowance = ResolveDeltaDivergenceRecordAllowance(settings);

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

	private static void CompareSnapshotRecord(MultiplayerSnapshotRecord hostRecord, MultiplayerSnapshotRecord clientRecord, bool exact, List<string> mismatches, List<string> digestMismatches, ref int comparedUnits, ref int offscreenMissingUnits, List<(int EntityId, string Field, int Sequence, string Message)> deltaDivergences, ulong clientPeerMask, ReferenceDigestCoverage coverage)
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
			bool isDescribedToThisPeer = (hostUnit.NamedByPeers & clientPeerMask) != 0;
			bool isDetailedForThisClient = (hostUnit.Flags & 4) != 0;
			bool exactUnit = exact && isDetailedForThisClient;
			float positionTolerance = exactUnit ? BaselineTolerance : DeltaPositionTolerance;
			float rotationTolerance = exactUnit ? BaselineTolerance : DeltaRotationTolerance;
			float healthTolerance = exactUnit ? BaselineTolerance : DeltaHealthTolerance;

			if (!clientRecord.Units.TryGetValue(hostUnit.EntityId, out MultiplayerSnapshotUnit? clientUnit))
			{
				if (isDead) continue;

				if (!isDescribedToThisPeer)
				{
					offscreenMissingUnits++;
					continue;
				}

				List<string> missingEntityTarget = hostRecord.IsBaseline ? mismatches : digestMismatches;
				AddMismatch(missingEntityTarget, 0, $"seq {hostRecord.Sequence}: client is missing live entity {hostUnit.EntityId} (owner slot {hostUnit.OwnerPlayerIndex}) that the host sent to it.");
				continue;
			}

			comparedUnits++;

			if (isDescribedToThisPeer)
			{
				CompareDigestSets(hostRecord.Sequence, $"entity {hostUnit.EntityId}", hostUnit.ComponentDigests, clientUnit.ComponentDigests, clientPeerMask, digestMismatches, coverage);
			}

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

		int repairStormAllowance = Math.Max(8, (int)(2f * settings.DurationSeconds * Math.Max(1, settings.PeerCount)));
		report.AddSummaryLine($"Repair scenario: the authority logged {repairCount} repair(s) over the run, of which the discard accounts for the baseline asserted above (allowed {repairStormAllowance} in total, and a storm would be one per sequence).");
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
			int describedPeerCount = reader.ReadInt32();
			List<int> describedPeerIds = new(describedPeerCount);
			for (int index = 0; index < describedPeerCount; index++) describedPeerIds.Add(reader.ReadInt32());
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
					NamedByPeers = reader.ReadUInt64(),
					ComponentDigests = ReadComponentDigests(reader)
				};
				units[unit.EntityId] = unit;
			}

			MultiplayerSnapshotRecord record = new() { Sequence = sequence, PhysicsFrame = physicsFrame, IsBaseline = isBaseline, BaselinePeerIds = baselinePeerIds, DescribedPeerIds = describedPeerIds, Units = units };
			int playerCount = reader.ReadInt32();
			for (int index = 0; index < playerCount; index++)
			{
				record.Players[reader.ReadInt32()] = ReadComponentDigests(reader);
			}

			record.WorldDigests = ReadComponentDigests(reader);
			int lootCount = reader.ReadInt32();
			for (int index = 0; index < lootCount; index++)
			{
				record.Loot[reader.ReadInt32()] = new MultiplayerLootState
				{
					NamedByPeers = reader.ReadUInt64(),
					ItemTypeId = reader.ReadString(),
					Charges = reader.ReadInt32(),
					Lifetime = reader.ReadSingle(),
					RemainingLifetime = reader.ReadSingle(),
					PickupRadius = reader.ReadSingle(),
					AutoPickup = reader.ReadBoolean(),
					OwnerPlayerIndex = reader.ReadInt32(),
					PositionX = reader.ReadSingle(),
					PositionY = reader.ReadSingle(),
					PositionZ = reader.ReadSingle()
				};
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

	private static Dictionary<int, ComponentDigestEntry> ReadComponentDigests(BinaryReader reader)
	{
		int count = reader.ReadInt32();
		Dictionary<int, ComponentDigestEntry> digests = new(count);
		for (int index = 0; index < count; index++)
		{
			int componentTypeId = reader.ReadInt32();
			uint hash = reader.ReadUInt32();
			bool isScoped = reader.ReadByte() != 0;
			ulong namedByMask = isScoped ? reader.ReadUInt64() : SyncRecordFormat.NamedByEveryPeer;
			digests[componentTypeId] = new ComponentDigestEntry(hash, namedByMask);
		}

		return digests;
	}

	private readonly struct ComponentDigestEntry
	{
		public readonly uint Hash;
		public readonly ulong NamedByMask;

		public ComponentDigestEntry(uint hash, ulong namedByMask)
		{
			Hash = hash;
			NamedByMask = namedByMask;
		}

		public bool IsScoped => NamedByMask != SyncRecordFormat.NamedByEveryPeer;

		public bool IsNameableBy(ulong peerMask) => (NamedByMask & peerMask) != 0;
	}

	private sealed class ReferenceDigestCoverage
	{
		public int Compared;
		public int Skipped;

		public void RecordCompared() => Compared++;
		public void RecordSkipped() => Skipped++;
	}

	private static ulong BuildPeerMask(List<int> describedPeerIds, HashSet<int> peerIds)
	{
		ulong mask = 0;
		for (int slot = 0; slot < describedPeerIds.Count && slot < 64; slot++)
		{
			if (peerIds.Contains(describedPeerIds[slot])) mask |= 1UL << slot;
		}

		return mask;
	}

	private sealed class MultiplayerSnapshotRecord
	{
		public int Sequence;
		public ulong PhysicsFrame;
		public bool IsBaseline;
		public HashSet<int> BaselinePeerIds = new();
		public List<int> DescribedPeerIds = new();
		public Dictionary<int, MultiplayerSnapshotUnit> Units = new();
		public Dictionary<int, Dictionary<int, ComponentDigestEntry>> Players = new();
		public Dictionary<int, ComponentDigestEntry> WorldDigests = new();
		public Dictionary<int, MultiplayerLootState> Loot = new();
		public MultiplayerNavigationState Navigation = new();
	}

	private sealed class MultiplayerLootState
	{
		public ulong NamedByPeers;
		public string ItemTypeId = string.Empty;
		public int Charges;
		public float Lifetime;
		public float RemainingLifetime;
		public float PickupRadius;
		public bool AutoPickup;
		public int OwnerPlayerIndex;
		public float PositionX;
		public float PositionY;
		public float PositionZ;
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
		public ulong NamedByPeers = SyncRecordFormat.NamedByEveryPeer;
		public Dictionary<int, ComponentDigestEntry> ComponentDigests = new();
	}
}
