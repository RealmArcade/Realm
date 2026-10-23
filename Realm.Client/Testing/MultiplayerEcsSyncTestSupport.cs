using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Realm.Client.Services;

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

		using HarnessProcess hostProcess = StartHarnessProcess(settings, "host", hostReadyFile, hostDoneFile);
		bool hostListening = await WaitForFileOrExitAsync(hostListeningFile, hostProcess.Process, TimeSpan.FromSeconds(120));
		if (!hostListening) throw new InvalidOperationException($"Host harness did not start listening.\n{hostProcess.ReadLog()}");

		using HarnessProcess clientProcess = StartHarnessProcess(settings, "client", clientReadyFile, hostDoneFile);
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
	}

	private static HarnessProcess StartHarnessProcess(MultiplayerEcsRunSettings settings, string role, string readyFile, string doneFile)
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
		startInfo.ArgumentList.Add($"--port={settings.Port}");
		startInfo.ArgumentList.Add($"--map={settings.MapFolder}");
		startInfo.ArgumentList.Add($"--wasm={settings.WasmPath}");
		startInfo.ArgumentList.Add($"--output={settings.OutputDirectory}");
		startInfo.ArgumentList.Add($"--listening-file={Path.Combine(settings.OutputDirectory, "host_listening.txt")}");
		startInfo.ArgumentList.Add($"--ready-file={readyFile}");
		startInfo.ArgumentList.Add($"--done-file={doneFile}");
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

	public static MultiplayerEcsSyncReport Verify(string outputDirectory)
	{
		MultiplayerEcsSyncReport report = new();
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

		int firstPairedSequence = -1;
		List<int> missingBaselineSequences = new();
		List<string> mismatches = new();
		int pairedSnapshots = 0;
		int comparedUnits = 0;
		int lastPairedSequence = -1;
		int unpairedSequenceCount = 0;

		foreach (KeyValuePair<int, MultiplayerSnapshotRecord> hostEntry in hostRecords)
		{
			if (!clientRecords.TryGetValue(hostEntry.Key, out MultiplayerSnapshotRecord? clientRecord))
			{
				if (firstPairedSequence >= 0)
				{
					unpairedSequenceCount++;
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

			CompareSnapshotRecord(hostEntry.Value, clientRecord, mismatches, ref comparedUnits);
		}

		report.PairedSnapshotCount = pairedSnapshots;
		report.ComparedUnitCount = comparedUnits;
		report.AddSummaryLine($"First paired sequence: {firstPairedSequence}");
		report.AddSummaryLine($"Last paired sequence: {lastPairedSequence}");
		report.AddSummaryLine($"Host sequences missing on client after first pairing: {unpairedSequenceCount}");
		report.AddSummaryLine($"Missing baseline sequences: {(missingBaselineSequences.Count == 0 ? "none" : string.Join(", ", missingBaselineSequences))}");

		if (pairedSnapshots < 3) report.AddFailure($"Only {pairedSnapshots} snapshot sequences were paired between host and client.");
		if (comparedUnits < 20) report.AddFailure($"Only {comparedUnits} unit states were compared, which is not enough to validate sync.");
		if (missingBaselineSequences.Count > 0)
		{
			report.AddFailure($"{missingBaselineSequences.Count} baseline snapshots produced by the host were never applied by the client.");
		}

		int tolerance = Math.Max(10, pairedSnapshots / 50);
		if (unpairedSequenceCount > tolerance)
		{
			report.AddFailure($"{unpairedSequenceCount} host snapshot sequences were never applied by the client (allowed {tolerance}).");
		}

		if (mismatches.Count > 0)
		{
			report.AddFailure($"{mismatches.Count} synced state mismatches were detected.");
			report.AddSummaryLine("First mismatches:");
			foreach (string mismatch in mismatches) report.AddSummaryLine($"  {mismatch}");
		}

		return report;
	}

	private static void CompareSnapshotRecord(MultiplayerSnapshotRecord hostRecord, MultiplayerSnapshotRecord clientRecord, List<string> mismatches, ref int comparedUnits)
	{
		int mismatchCountBefore = mismatches.Count;
		bool exact = hostRecord.IsBaseline;
		float positionTolerance = exact ? BaselineTolerance : DeltaPositionTolerance;
		float rotationTolerance = exact ? BaselineTolerance : DeltaRotationTolerance;
		float healthTolerance = exact ? BaselineTolerance : DeltaHealthTolerance;

		foreach (MultiplayerSnapshotUnit hostUnit in hostRecord.Units.Values)
		{
			bool isDead = (hostUnit.Flags & 1) != 0;

			if (!clientRecord.Units.TryGetValue(hostUnit.EntityId, out MultiplayerSnapshotUnit? clientUnit))
			{
				if (!isDead)
				{
					AddMismatch(mismatches, mismatchCountBefore, $"seq {hostRecord.Sequence}: client is missing live entity {hostUnit.EntityId} (owner slot {hostUnit.OwnerPlayerIndex}).");
				}

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

	private static SortedDictionary<int, MultiplayerSnapshotRecord> ReadRecords(string path)
	{
		SortedDictionary<int, MultiplayerSnapshotRecord> records = new();
		using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16);
		using BinaryReader reader = new(stream);

		uint magic = reader.ReadUInt32();
		if (magic != 0x504E5352) throw new InvalidDataException($"{Path.GetFileName(path)} does not contain Realm sync records.");

		int version = reader.ReadInt32();
		if (version != 1) throw new InvalidDataException($"{Path.GetFileName(path)} uses unsupported record version {version}.");
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
					Flags = reader.ReadByte()
				};
				units[unit.EntityId] = unit;
			}

			records[sequence] = new MultiplayerSnapshotRecord { Sequence = sequence, IsBaseline = isBaseline, Units = units };
		}

		return records;
	}

	private sealed class MultiplayerSnapshotRecord
	{
		public int Sequence;
		public bool IsBaseline;
		public Dictionary<int, MultiplayerSnapshotUnit> Units = new();
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
	}
}
