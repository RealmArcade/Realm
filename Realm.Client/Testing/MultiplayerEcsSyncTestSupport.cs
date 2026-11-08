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

public enum NetworkProfile
{
	Clean,
	Latency,
	LatencyJitter,
	LatencyLoss
}

public class MultiplayerEcsRunSettings
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

public class MultiplayerEcsSyncReport
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

public static partial class MultiplayerEcsSyncTestSupport
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
		string scriptOutputDirectory = MapFileService.ResolveScriptOutputDirectory(mapFolder);
		string publishedPath = Path.Combine(scriptOutputDirectory, $"{assemblyName}.wasm");
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
		startInfo.ArgumentList.Add(MapFileService.ReleaseBuildFlavor);
		startInfo.ArgumentList.Add("-r");
		startInfo.ArgumentList.Add("wasi-wasm");
		startInfo.ArgumentList.Add("-o");
		startInfo.ArgumentList.Add(scriptOutputDirectory);
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

	private class HarnessProcess : IDisposable
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

}