using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using GdUnit4;
using Godot;
using Realm.Client.Core;
using Realm.Client.Network;
using Realm.Client.Services;
using Realm.Client.UI;
using Realm.Ecs.Services;
using Realm.MapAPI;
using Realm.Shared;
using Vector3 = System.Numerics.Vector3;

namespace Realm.Client.Tests;

[TestSuite]
[RequireGodotRuntime]
public partial class SimulationTests
{
	[TestCase]
	public async Task TestMeleePathingAroundTree()
	{
		if (GameContext.Lobby == null)
		{
			return;
		}

		SetupSinglePlayerLobby("melee");

		ISceneRunner runner = ISceneRunner.Load("res://GameHost.tscn");
		await runner.AwaitMillis(1000);

		GameHost gameHost = GameContext.Host;
		if (gameHost == null)
		{
			return;
		}

		Unit3D worker = SetupWorkerAndMove(gameHost);
		if (worker == null)
		{
			return;
		}

		await runner.AwaitMillis(100);
		VerifyPathFollowComponent(gameHost, worker);

		await TakeSimulationScreenshots(runner, "Realm_Simulation_NonWasm_Screenshots");
	}

	[TestCase]
	public async Task TestWasmMeleePathingAroundTree()
	{
		if (GameContext.Lobby == null)
		{
			return;
		}

		string tempMapDir = Path.Combine(Path.GetTempPath(), "Realm_Simulation_WasmTestMap");
		if (Directory.Exists(tempMapDir))
		{
			try
			{
				Directory.Delete(tempMapDir, true);
			}
			catch
			{
			}
		}

		Directory.CreateDirectory(tempMapDir);

		MapWorkspaceService.SetupWorkspace(tempMapDir, "TestWasmMap");

		string generatedCsproj = File.ReadAllText(Path.Combine(tempMapDir, "TestWasmMap.csproj"));
		Assertions.AssertThat(!generatedCsproj.Contains("C:")).IsTrue();
		Assertions.AssertThat(generatedCsproj.Contains("lib/Realm.MapAPI.dll")).IsTrue();
		Assertions.AssertThat(File.Exists(Path.Combine(tempMapDir, "lib", "Realm.MapAPI.dll"))).IsTrue();

		string mapScript = @"
namespace Realm.Maps;

using Realm.MapAPI;
using System;
using System.Numerics;

public class TestWasmMap : IWasmModule
{
    public void Initialize(IGameAPI api)
    {
        api.SpawnResourceNode(""tree"", new Vector3(-18f, 0f, -35f), 500f);
        api.SpawnResourceNode(""tree"", new Vector3(-22f, 0f, -36f), 500f);
        api.SpawnResourceNode(""tree"", new Vector3(-26f, 0f, -34f), 500f);

        var worker = api.SpawnUnit(""worker"", new Vector3(-16f, 0f, -20f), false);
        api.BroadcastMessage(""wasm_unit_created"");

        worker.MoveTo(new Vector3(-20f, 0f, -50f));
        api.BroadcastMessage(""wasm_move_command_given"");
    }

    public void Update(IGameAPI api, float delta) { }
}";
		File.WriteAllText(Path.Combine(tempMapDir, "MapScript.cs"), mapScript);

		string wasmPath = CompileWasmProgram(tempMapDir);

		SetupSinglePlayerLobby(Path.GetFullPath("Realm.ClientMaps/melee"));
		GameHost.PendingMapScriptPath = wasmPath;

		WasmLogTracker tracker = AttachWasmLogListener();

		ISceneRunner runner = ISceneRunner.Load("res://GameHost.tscn");
		await runner.AwaitMillis(1000);

		GameHost gameHost = GameContext.Host;
		if (gameHost == null)
		{
			WasmRuntime.OnWasmLog -= tracker.Listener;
			return;
		}

		await WaitAndVerifyWasmLogs(runner, tracker);

		Unit3D worker = await FindWorkerUnitAsync(gameHost, runner);
		await runner.AwaitMillis(100);
		VerifyWasmPathFollowComponent(gameHost, worker);

		await TakeSimulationScreenshots(runner, "Realm_Simulation_Wasm_Screenshots");
	}

	[TestCase]
	public async Task TestMapEditorTestButtonWasmExecution()
	{
		BypassMapEditorAgreement();

		if (GameContext.Lobby == null)
		{
			return;
		}

		GameContext.Lobby.IsSinglePlayer = true;
		PropertyInfo isHostProp = typeof(LobbyManager).GetProperty("IsHost", BindingFlags.Public | BindingFlags.Instance);
		isHostProp?.SetValue(GameContext.Lobby, true);

		ISceneRunner runner = ISceneRunner.Load("res://GameHost.tscn");
		await runner.AwaitMillis(1500);

		GameContext.UserInterface.TransitionTo(GameScreen.MapEditorHUD);
		await runner.AwaitMillis(2500);

		var hud = GameContext.MapEditorHud;
		if (hud == null)
		{
			throw new Exception("MapEditorHUD instance was null after transition.");
		}

		await LoadChaosArenaFolder(hud, runner);

		string artifactDir = @"C:\Users\devin\.gemini\antigravity-cli\brain\7000f492-ab70-4409-bc47-42fbecc00ce5";
		Directory.CreateDirectory(artifactDir);

		await hud.ProceedToTestMap();
		await runner.AwaitMillis(2000);

		await ExecuteTestMapSteps(runner, artifactDir);

		Image finalImage = runner.Scene().GetViewport().GetTexture().GetImage();
		File.WriteAllBytes(Path.Combine(artifactDir, "ChaosArena_Wasm_Tested.png"), finalImage.SavePngToBuffer());

		VerifyAllUnitsKilled();
	}

	[TestCase]
	public async Task TestChaosArenaPerformanceBenchmark()
	{
		if (GameContext.Lobby == null)
		{
			return;
		}

		GameSettings.QualityIdx = GraphicsQuality.Low;
		GameSettings.Vsync = false;
		DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
		Engine.MaxFps = 0;

		GameContext.SessionState.IsTestMode = true;
		GameContext.PerformanceTrackingService.IsFunctionProfilingEnabled = true;
		GameContext.PerformanceTrackingService.ResetFunctionProfiling();

		string profilePath = System.Environment.GetEnvironmentVariable("REALM_PERF_PROFILE_PATH") ?? @"c:\temp\realm_chaos_performance_profile_log.txt";
		string? profileDir = Path.GetDirectoryName(Path.GetFullPath(profilePath));
		if (!string.IsNullOrEmpty(profileDir) && !Directory.Exists(profileDir))
		{
			Directory.CreateDirectory(profileDir);
		}

		string chaosArenaDir = @"D:\git\realm\ugc-ChaosArena";
		string wasmPath = Path.Combine(chaosArenaDir, "bin", "Release", "net10.0", "wasi-wasm", "publish", "MapScript.wasm");

		GameContext.Lobby.IsSinglePlayer = true;
		PropertyInfo isHostProp = typeof(LobbyManager).GetProperty("IsHost", BindingFlags.Public | BindingFlags.Instance);
		isHostProp?.SetValue(GameContext.Lobby, true);
		GameContext.Lobby.IsGameStarted = true;
		GameContext.Lobby.ActiveMapName = chaosArenaDir;
		GameContext.Lobby.PlayerList.Clear();

		GameContext.Lobby.PlayerList.Add(new PlayerInfo
		{
			PeerId = 1,
			Slot = 1,
			Name = "Human_Player",
			Faction = "HUMAN",
			Team = "Team 1",
			Color = new Color(0.8f, 0.1f, 0.1f),
			IsHost = true,
			Latency = "0 ms",
			Jitter = "0 ms",
			PacketLoss = "0%",
			BinaryVersion = RealmVersion.GameBinaryVersion
		});

		for (int slot = 2; slot <= 12; slot++)
		{
			GameContext.Lobby.PlayerList.Add(new PlayerInfo
			{
				PeerId = -(slot + 100),
				Slot = slot,
				Name = $"AI_Player_{slot}",
				Faction = "HUMAN",
				Team = $"Team {slot}",
				Color = new Color((slot * 0.17f) % 1f, (slot * 0.31f) % 1f, (slot * 0.53f) % 1f),
				IsHost = false,
				Latency = "0 ms",
				Jitter = "0 ms",
				PacketLoss = "0%",
				BinaryVersion = RealmVersion.GameBinaryVersion
			});
		}

		GameHost.PendingMapScriptPath = wasmPath;
		GameContext.UserInterface?.TransitionTo(GameScreen.InGameHUD);

		ISceneRunner runner = ISceneRunner.Load("res://GameHost.tscn");
		await runner.AwaitMillis(500);

		GameHost gameHost = GameContext.Host;
		if (gameHost == null)
		{
			throw new Exception("GameContext.Host was null.");
		}

		GameContext.PerformanceTrackingService.IsFunctionProfilingEnabled = true;
		GameContext.PerformanceTrackingService.ResetFunctionProfiling();

		var wasmRuntimeInit = GameContext.MapScriptService?.ActiveMapScript as WasmRuntime;
		wasmRuntimeInit?.ResetApiProfiling();

		var camera = gameHost.MainCamera;
		if (camera != null)
		{
			camera.Position = new Godot.Vector3(0f, 55f, 45f);
		}

		using GpuSampler gpuSampler = new GpuSampler();

		var currentProcess = Process.GetCurrentProcess();
		int processorCount = Math.Max(1, System.Environment.ProcessorCount);
		TimeSpan startCpuTime = currentProcess.TotalProcessorTime;
		TimeSpan lastCpuTime = startCpuTime;
		var snapshotRows = new List<string>();

		int nextReportSec = 10;
		var sw = Stopwatch.StartNew();
		ulong lastFrames = Engine.GetProcessFrames();
		double lastWallSec = 0.0;

		try
		{
			while (gameHost.GameElapsedTime < 120.5f)
			{
				await runner.SimulateFrames(30);
				float gameSec = gameHost.GameElapsedTime;
				if (gameSec >= nextReportSec)
				{
					ulong curFrames = Engine.GetProcessFrames();
					double curWallSec = sw.Elapsed.TotalSeconds;
					double deltaWall = Math.Max(0.0001, curWallSec - lastWallSec);
					double avgFpsWindow = (curFrames - lastFrames) / deltaWall;
					double engineFps = Engine.GetFramesPerSecond();
					int unitCount = gameHost.AllUnits.Count;

					TimeSpan curCpuTime = currentProcess.TotalProcessorTime;
					double deltaCpuSec = (curCpuTime - lastCpuTime).TotalSeconds;
					double intervalCpuPercent = (deltaCpuSec / (deltaWall * processorCount)) * 100.0;
					double cumulativeCpuPercent = ((curCpuTime - startCpuTime).TotalSeconds / (curWallSec * processorCount)) * 100.0;
					lastCpuTime = curCpuTime;

					float gpuPercent = gpuSampler.SampleGpu();

					double rasterObjects = Performance.GetMonitor(Performance.Monitor.RenderTotalObjectsInFrame);
					double rasterPrimitives = Performance.GetMonitor(Performance.Monitor.RenderTotalPrimitivesInFrame);
					double rasterDrawCalls = Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame);
					double godotObjects = Performance.GetMonitor(Performance.Monitor.ObjectCount);
					double godotNodes = Performance.GetMonitor(Performance.Monitor.ObjectNodeCount);
					double godotOrphanNodes = Performance.GetMonitor(Performance.Monitor.ObjectOrphanNodeCount);
					double godotFps = Performance.GetMonitor(Performance.Monitor.TimeFps);
					double godotPhysicsProcessMs = Performance.GetMonitor(Performance.Monitor.TimePhysicsProcess) * 1000.0;
					double godotProcessMs = Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000.0;
					double vramMb = Performance.GetMonitor(Performance.Monitor.RenderVideoMemUsed) / (1024.0 * 1024.0);
					double textureMemMb = Performance.GetMonitor(Performance.Monitor.RenderTextureMemUsed) / (1024.0 * 1024.0);
					double bufferMemMb = Performance.GetMonitor(Performance.Monitor.RenderBufferMemUsed) / (1024.0 * 1024.0);

					var tickList = GameContext.PerformanceTrackingService.TrackerTickDurations;
					var apiList = GameContext.PerformanceTrackingService.TrackerApiDurations;
					float recentTickAvg = 0f;
					if (tickList != null && tickList.Count > 0)
					{
						int startIdx = Math.Max(0, tickList.Count - 300);
						float sum = 0f;
						for (int i = startIdx; i < tickList.Count; i++) sum += tickList[i];
						recentTickAvg = sum / (tickList.Count - startIdx);
					}
					float recentApiAvg = 0f;
					if (apiList != null && apiList.Count > 0)
					{
						int startIdx = Math.Max(0, apiList.Count - 300);
						float sum = 0f;
						for (int i = startIdx; i < apiList.Count; i++) sum += apiList[i];
						recentApiAvg = sum / (apiList.Count - startIdx);
					}

					string snapshotRow = $"[{gameSec,5:F1}s] Units={unitCount,3} | FPS(Win={avgFpsWindow,5:F1}, Eng={engineFps,5:F1}) | CPU={intervalCpuPercent,5:F1}% (Cumul={cumulativeCpuPercent,5:F1}%) | GPU={gpuPercent,5:F1}% | DrawCalls={rasterDrawCalls,4:N0} | Primitives={rasterPrimitives,6:N0} | Objects={rasterObjects,4:N0} | Nodes={godotNodes,4:N0} | Orphans={godotOrphanNodes,2:N0} | PhysMs={godotPhysicsProcessMs,5:F2} | ProcMs={godotProcessMs,5:F2} | VRAM={vramMb,5:F1}MB | TickAvg={recentTickAvg,5:F2}ms | ApiAvg={recentApiAvg,5:F2}ms";
					snapshotRows.Add(snapshotRow);

					lastFrames = curFrames;
					lastWallSec = curWallSec;
					nextReportSec += 10;

					WriteProfileReport(profilePath, currentProcess, processorCount, sw, gameHost, snapshotRows, cumulativeCpuPercent, gpuPercent, gpuSampler.LatestBreakdown, rasterObjects, rasterPrimitives, rasterDrawCalls, godotObjects, godotNodes, godotOrphanNodes, godotFps, godotPhysicsProcessMs, godotProcessMs, vramMb, textureMemMb, bufferMemMb);
				}
			}
		}
		finally
		{
			GameContext.PerformanceTrackingService.IsFunctionProfilingEnabled = false;
			GameContext.SessionState.IsTestMode = false;
			gpuSampler.Dispose();
		}
	}

	private void WriteProfileReport(
		string profilePath,
		Process currentProcess,
		int processorCount,
		Stopwatch wallStopwatch,
		GameHost gameHost,
		List<string> snapshotRows,
		double cumulativeCpuPercent,
		float gpuPercent,
		string gpuBreakdown,
		double rasterObjects,
		double rasterPrimitives,
		double rasterDrawCalls,
		double godotObjects,
		double godotNodes,
		double godotOrphanNodes,
		double godotFps,
		double godotPhysicsProcessMs,
		double godotProcessMs,
		double vramMb,
		double textureMemMb,
		double bufferMemMb)
	{
		var reportBuilder = new StringBuilder();
		reportBuilder.AppendLine("================================================================================");
		reportBuilder.AppendLine("                      REALM CHAOS ARENA PERFORMANCE PROFILE                     ");
		reportBuilder.AppendLine("================================================================================");
		reportBuilder.AppendLine($"Generated At: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
		reportBuilder.AppendLine($"Machine: {System.Environment.MachineName} ({System.Environment.OSVersion}) | Logical Cores: {processorCount}");
		reportBuilder.AppendLine($"Process ID: {currentProcess.Id} | Wall-clock Duration: {wallStopwatch.Elapsed.TotalSeconds:F1}s | Simulated Game Time: {gameHost.GameElapsedTime:F1}s");
		reportBuilder.AppendLine();

		reportBuilder.AppendLine("=== Hardware & Rendering Environment ===");
		reportBuilder.AppendLine($"Video Adapter Name: {RenderingServer.GetVideoAdapterName()}");
		reportBuilder.AppendLine($"Video Adapter Vendor: {RenderingServer.GetVideoAdapterVendor()}");
		reportBuilder.AppendLine($"Video Adapter Type: {RenderingServer.GetVideoAdapterType()}");
		reportBuilder.AppendLine($"Video Adapter API Version: {RenderingServer.GetVideoAdapterApiVersion()}");
		reportBuilder.AppendLine();

		reportBuilder.AppendLine("=== Periodic Performance Snapshots ===");
		for (int i = 0; i < snapshotRows.Count; i++)
		{
			reportBuilder.AppendLine(snapshotRows[i]);
		}
		reportBuilder.AppendLine();

		reportBuilder.AppendLine("=== Performance Metric Summary ===");
		reportBuilder.AppendLine($"CPU % Utilization (Cumulative Average): {cumulativeCpuPercent:F2}%");
		reportBuilder.AppendLine($"GPU % Utilization (Latest Sample): {gpuPercent:F2}%");
		if (!string.IsNullOrEmpty(gpuBreakdown))
		{
			reportBuilder.AppendLine($"GPU % Utilization Breakdown: {gpuBreakdown}");
		}
		reportBuilder.AppendLine($"Raster.TotalObjectsDrawn: {rasterObjects:N0}");
		reportBuilder.AppendLine($"Raster.TotalPrimitivesDrawn: {rasterPrimitives:N0}");
		reportBuilder.AppendLine($"Raster.TotalDrawCalls: {rasterDrawCalls:N0}");
		reportBuilder.AppendLine($"Godot.ObjectCount: {godotObjects:N0}");
		reportBuilder.AppendLine($"Godot.Nodes: {godotNodes:N0}");
		reportBuilder.AppendLine($"Godot.OrphanNodes: {godotOrphanNodes:N0}");
		reportBuilder.AppendLine($"Godot.FPS: {godotFps:F1}");
		reportBuilder.AppendLine($"Godot.PhysicsProcessMilliseconds: {godotPhysicsProcessMs:F3} ms");
		reportBuilder.AppendLine($"Godot.ProcessMilliseconds: {godotProcessMs:F3} ms");
		reportBuilder.AppendLine($"VRAM Used: {vramMb:F2} MB");
		reportBuilder.AppendLine($"Texture Memory Used: {textureMemMb:F2} MB");
		reportBuilder.AppendLine($"Buffer Memory Used: {bufferMemMb:F2} MB");
		reportBuilder.AppendLine();

		reportBuilder.AppendLine(GameContext.PerformanceTrackingService.GetFunctionProfilingBreakdown());
		reportBuilder.AppendLine();

		var wasmRuntime = GameContext.MapScriptService?.ActiveMapScript as WasmRuntime;
		if (wasmRuntime != null)
		{
			reportBuilder.AppendLine("=== WASM VM API Call Profiling Breakdown ===");
			reportBuilder.AppendLine(wasmRuntime.GetApiCallProfilingBreakdown(topCount: 50));
			reportBuilder.AppendLine();
		}

		var renderingDevice = RenderingServer.GetRenderingDevice();
		if (renderingDevice != null)
		{
			try
			{
				reportBuilder.AppendLine("=== Rendering Device Performance Report ===");
				string perfReport = renderingDevice.GetPerfReport();
				if (!string.IsNullOrWhiteSpace(perfReport))
				{
					reportBuilder.AppendLine($"Driver Perf Report: {perfReport} (Note: 'lazily alloc' is Vulkan transient memory used on mobile TBDR architectures; desktop discrete GPUs report 0)");
				}
				reportBuilder.AppendLine();
			}
			catch
			{
			}
		}

		File.WriteAllText(profilePath, reportBuilder.ToString());
	}

	[GeneratedRegex(@"_luid_([0-9a-fA-F_xX]+?)_phys_")]
	private static partial Regex GpuLuidRegex();

	private partial class GpuSampler : IDisposable
	{
		private readonly int _processId;
		private readonly string _pidPrefix;
		private readonly Type? _categoryType;
		private readonly Type? _counterType;
		private readonly MethodInfo? _getInstanceNamesMethod;
		private readonly MethodInfo? _nextValueMethod;
		private readonly Dictionary<string, (string Luid, IDisposable Counter, MethodInfo NextValueMethod)> _counters = new(StringComparer.OrdinalIgnoreCase);

		public string LatestBreakdown { get; private set; } = string.Empty;

		public GpuSampler()
		{
			_processId = Process.GetCurrentProcess().Id;
			_pidPrefix = $"pid_{_processId}_";

			if (!OperatingSystem.IsWindows())
			{
				return;
			}

			try
			{
				Assembly? asm = LoadPerformanceCounterAssembly();
				_categoryType = asm?.GetType("System.Diagnostics.PerformanceCounterCategory")
					?? Type.GetType("System.Diagnostics.PerformanceCounterCategory, System.Diagnostics.PerformanceCounter")
					?? Type.GetType("System.Diagnostics.PerformanceCounterCategory, System");
				_counterType = asm?.GetType("System.Diagnostics.PerformanceCounter")
					?? Type.GetType("System.Diagnostics.PerformanceCounter, System.Diagnostics.PerformanceCounter")
					?? Type.GetType("System.Diagnostics.PerformanceCounter, System");

				_getInstanceNamesMethod = _categoryType?.GetMethod("GetInstanceNames", Type.EmptyTypes);
				_nextValueMethod = _counterType?.GetMethod("NextValue", Type.EmptyTypes);

				RefreshCounters();
			}
			catch
			{
			}
		}

		private static Assembly? LoadPerformanceCounterAssembly()
		{
			try
			{
				return Assembly.Load("System.Diagnostics.PerformanceCounter");
			}
			catch
			{
			}

			string[] searchPaths =
			[
				Path.Combine(AppContext.BaseDirectory, "System.Diagnostics.PerformanceCounter.dll"),
				@"C:\Program Files\dotnet\shared\Microsoft.WindowsDesktop.App\10.0.12\System.Diagnostics.PerformanceCounter.dll",
				@"C:\Program Files\dotnet\shared\Microsoft.WindowsDesktop.App\9.0.21\System.Diagnostics.PerformanceCounter.dll",
				@"C:\Program Files\dotnet\shared\Microsoft.WindowsDesktop.App\8.0.32\System.Diagnostics.PerformanceCounter.dll",
				Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile), @".nuget\packages\system.diagnostics.performancecounter\9.0.7\runtimes\win\lib\net9.0\System.Diagnostics.PerformanceCounter.dll")
			];

			for (int i = 0; i < searchPaths.Length; i++)
			{
				if (File.Exists(searchPaths[i]))
				{
					try
					{
						return Assembly.LoadFrom(searchPaths[i]);
					}
					catch
					{
					}
				}
			}

			return null;
		}

		private void RefreshCounters()
		{
			if (_categoryType == null || _counterType == null || _getInstanceNamesMethod == null || _nextValueMethod == null)
			{
				return;
			}

			try
			{
				object? category = Activator.CreateInstance(_categoryType, "GPU Engine");
				if (category == null)
				{
					return;
				}

				string[]? instanceNames = _getInstanceNamesMethod.Invoke(category, null) as string[];
				if (instanceNames == null)
				{
					return;
				}

				for (int i = 0; i < instanceNames.Length; i++)
				{
					string instanceName = instanceNames[i];
					if (instanceName.StartsWith(_pidPrefix, StringComparison.OrdinalIgnoreCase) &&
						!_counters.ContainsKey(instanceName))
					{
						string luid = ExtractLuid(instanceName);
						try
						{
							object? counter = Activator.CreateInstance(_counterType, "GPU Engine", "Utilization Percentage", instanceName, true);
							if (counter is IDisposable disposableCounter)
							{
								try
								{
									_nextValueMethod.Invoke(counter, null);
									_counters[instanceName] = (luid, disposableCounter, _nextValueMethod);
								}
								catch
								{
									disposableCounter.Dispose();
								}
							}
						}
						catch
						{
						}
					}
				}
			}
			catch
			{
			}
		}

		private static string ExtractLuid(string instanceName)
		{
			Match match = GpuLuidRegex().Match(instanceName);
			if (match.Success)
			{
				return match.Groups[1].Value;
			}

			int luidIdx = instanceName.IndexOf("_luid_", StringComparison.OrdinalIgnoreCase);
			if (luidIdx >= 0)
			{
				int start = luidIdx + 6;
				int physIdx = instanceName.IndexOf("_phys_", start, StringComparison.OrdinalIgnoreCase);
				if (physIdx > start)
				{
					return instanceName.Substring(start, physIdx - start);
				}
			}

			return "default";
		}

		public float SampleGpu()
		{
			RefreshCounters();

			if (_counters.Count == 0)
			{
				LatestBreakdown = "0 GPU counters";
				return 0f;
			}

			var luidSums = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);

			foreach (var kvp in _counters)
			{
				string luid = kvp.Value.Luid;
				try
				{
					object? val = kvp.Value.NextValueMethod.Invoke(kvp.Value.Counter, null);
					if (val is float f && !float.IsNaN(f) && !float.IsInfinity(f))
					{
						luidSums[luid] = luidSums.GetValueOrDefault(luid, 0f) + Math.Max(0f, f);
					}
				}
				catch
				{
				}
			}

			if (luidSums.Count == 0)
			{
				LatestBreakdown = "0 active GPU counters";
				return 0f;
			}

			float maxGpu = 0f;
			var breakdownParts = new List<string>();

			foreach (var kvp in luidSums.OrderBy(p => p.Key))
			{
				float clamped = Math.Clamp(kvp.Value, 0f, 100f);
				if (clamped > maxGpu)
				{
					maxGpu = clamped;
				}
				breakdownParts.Add($"{kvp.Key}: {clamped:F2}%");
			}

			LatestBreakdown = $"{maxGpu:F2}% [Max] ({string.Join(", ", breakdownParts)})";
			return maxGpu;
		}

		public void Dispose()
		{
			foreach (var kvp in _counters)
			{
				try
				{
					kvp.Value.Counter.Dispose();
				}
				catch
				{
				}
			}
			_counters.Clear();
		}
	}

	private void SetupSinglePlayerLobby(string mapName)
	{
		GameContext.Lobby.IsSinglePlayer = true;

		PropertyInfo isHostProp = typeof(LobbyManager).GetProperty("IsHost", BindingFlags.Public | BindingFlags.Instance);
		isHostProp?.SetValue(GameContext.Lobby, true);

		GameContext.Lobby.IsGameStarted = true;
		GameContext.Lobby.ActiveMapName = mapName;
		GameContext.Lobby.PlayerList.Clear();

		PlayerInfo playerInfo = new PlayerInfo
		{
			PeerId = 1,
			Slot = 0,
			Name = GameContext.Lobby.AuthenticatedUsername,
			Faction = "HUMAN",
			Team = "Team 1",
			Color = new Color(0.8f, 0.1f, 0.1f),
			IsHost = true,
			Latency = "0 ms",
			Jitter = "0 ms",
			PacketLoss = "0%",
			BinaryVersion = RealmVersion.GameBinaryVersion
		};

		GameContext.Lobby.PlayerList.Add(playerInfo);
	}

	private Unit3D SetupWorkerAndMove(GameHost gameHost)
	{
		Unit3D worker = GameContext.Host.AllUnits.FirstOrDefault(u => u.UnitId == "worker" && u.Player == 1);
		if (worker == null)
		{
			return null;
		}

		GameContext.Host.SelectedUnits.Clear();
		GameContext.Host.SelectedUnits.Add(worker);
		worker.IsSelected = true;
		GameContext.InGameHud?.RefreshUI(GameContext.Host.SelectedUnits);

		Vector3 destination = new Vector3(-20f, 0f, -50f);
		var unitWrapper = GameContext.SimulationService.GetUnitWrapper(worker.Entity);
		unitWrapper.MoveTo(destination);
		return worker;
	}

	private void VerifyPathFollowComponent(GameHost gameHost, Unit3D worker)
	{
		if (GameContext.World.Has<PathFollow>(worker.Entity))
		{
			var pf = GameContext.World.Get<PathFollow>(worker.Entity);
			GD.Print($"WAYPOINTS COUNT: {pf.WaypointCount}");
			for (int i = 0; i < pf.WaypointCount; i++)
			{
				GD.Print($"Waypoint {i}: {pf.Waypoints[i]}");
			}
		}
		else
		{
			GD.Print("NO PATHFOLLOW COMPONENT");
		}
	}

	private async Task TakeSimulationScreenshots(ISceneRunner runner, string dirName)
	{
		string tempDir = Path.Combine(Path.GetTempPath(), dirName);
		if (Directory.Exists(tempDir))
		{
			try
			{
				Directory.Delete(tempDir, true);
			}
			catch
			{
			}
		}

		Directory.CreateDirectory(tempDir);

		for (int i = 1; i <= 15; i++)
		{
			await runner.AwaitMillis(1000);

			Image image = runner.Scene().GetViewport().GetTexture().GetImage();
			string fileName = $"Simulation_Step_{i:00}.png";
			string filePath = Path.Combine(tempDir, fileName);
			image.SavePng(filePath);
		}
	}

	private string CompileWasmProgram(string tempMapDir)
	{
		var compileProcess = new Process();
		string resolvedWasiSdk = WasiSdkResolver.ResolveWasiSdkPath();
		compileProcess.StartInfo.FileName = "dotnet";
		compileProcess.StartInfo.Arguments = $"publish \"TestWasmMap.csproj\" -c Release -r wasi-wasm -p:WASI_SDK_PATH=\"{resolvedWasiSdk}\"";
		compileProcess.StartInfo.EnvironmentVariables["WASI_SDK_PATH"] = resolvedWasiSdk;
		compileProcess.StartInfo.WorkingDirectory = tempMapDir;
		compileProcess.StartInfo.CreateNoWindow = true;
		compileProcess.StartInfo.UseShellExecute = false;
		compileProcess.StartInfo.RedirectStandardOutput = false;
		compileProcess.StartInfo.RedirectStandardError = false;
		compileProcess.Start();
		compileProcess.WaitForExit();
		if (compileProcess.ExitCode != 0)
		{
			throw new Exception($"Wasm compilation failed (exit code {compileProcess.ExitCode})");
		}

		string wasmPath = Directory.GetFiles(Path.Combine(tempMapDir, "bin"), "*.wasm", SearchOption.AllDirectories).OrderByDescending(f => File.GetLastWriteTimeUtc(f)).FirstOrDefault();
		if (string.IsNullOrEmpty(wasmPath) || !File.Exists(wasmPath))
		{
			throw new FileNotFoundException("Compiled WASM file not found in build directory.");
		}

		return wasmPath;
	}

	private class WasmLogTracker
	{
		public bool UnitCreatedLogged;
		public bool MoveCommandLogged;
		public Action<string> Listener;
	}

	private WasmLogTracker AttachWasmLogListener()
	{
		var tracker = new WasmLogTracker();
		tracker.Listener = msg =>
		{
			if (msg.Contains("wasm_unit_created")) tracker.UnitCreatedLogged = true;
			if (msg.Contains("wasm_move_command_given")) tracker.MoveCommandLogged = true;
		};
		WasmRuntime.OnWasmLog += tracker.Listener;
		return tracker;
	}

	private async Task WaitAndVerifyWasmLogs(ISceneRunner runner, WasmLogTracker tracker)
	{
		for (int i = 0; i < 100; i++)
		{
			if (tracker.UnitCreatedLogged && tracker.MoveCommandLogged)
			{
				break;
			}

			await runner.AwaitMillis(50);
		}

		WasmRuntime.OnWasmLog -= tracker.Listener;

		if (!tracker.UnitCreatedLogged)
		{
			throw new Exception("WASM sandbox failed to notify unit creation via debug log.");
		}

		if (!tracker.MoveCommandLogged)
		{
			throw new Exception("WASM sandbox failed to notify move command issue via debug log.");
		}
	}

	private async Task<Unit3D> FindWorkerUnitAsync(GameHost gameHost, ISceneRunner runner)
	{
		Unit3D worker = null;
		for (int i = 0; i < 50; i++)
		{
			worker = GameContext.Host.AllUnits.FirstOrDefault(u => u.UnitId == "worker" && u.Player == 1);
			if (worker != null)
			{
				break;
			}

			await runner.AwaitMillis(50);
		}

		if (worker == null)
		{
			throw new Exception("Worker unit spawned by WASM sandbox was not found on host Realm.Client.Core.");
		}

		return worker;
	}

	private void VerifyWasmPathFollowComponent(GameHost gameHost, Unit3D worker)
	{
		if (GameContext.World.Has<PathFollow>(worker.Entity))
		{
			var pf = GameContext.World.Get<PathFollow>(worker.Entity);
			GD.Print($"WASM WAYPOINTS COUNT: {pf.WaypointCount}");
			for (int i = 0; i < pf.WaypointCount; i++)
			{
				GD.Print($"Wasm Waypoint {i}: {pf.Waypoints[i]}");
			}

			if (pf.WaypointCount <= 0)
			{
				throw new Exception("Worker has PathFollow component but waypoint count is 0.");
			}
		}
		else
		{
			GD.Print("WASM NO PATHFOLLOW COMPONENT");
			throw new Exception("PathFollow component missing on unit under WASM sandbox.");
		}
	}

	private void BypassMapEditorAgreement()
	{
		var field = typeof(MapEditorHUD).GetField("_agreementShownThisSession", BindingFlags.NonPublic | BindingFlags.Static);
		if (field != null)
		{
			field.SetValue(null, true);
		}
	}

	private async Task LoadChaosArenaFolder(MapEditorHUD hud, ISceneRunner runner)
	{
		string chaosArenaFolder = Path.GetFullPath("../ugc-ChaosArena");
		if (!Directory.Exists(chaosArenaFolder))
		{
			chaosArenaFolder = Path.GetFullPath("D:/git/Realm/ugc-ChaosArena");
		}

		bool loadOk = hud.LoadMapFolder(chaosArenaFolder);
		if (!loadOk)
		{
			throw new Exception($"Failed to load map folder '{chaosArenaFolder}' into editor.");
		}

		await runner.AwaitMillis(1000);

		if (GameContext.Host == null || GameContext.Host.AllUnits.Count == 0)
		{
			throw new Exception("Units from terrain.json were not loaded into editor Realm.Client.Core.");
		}

		int initialUnitCount = GameContext.Host.AllUnits.Count;
		GD.Print($"Editor loaded {initialUnitCount} units from map terrain.json.");
	}

	private async Task ExecuteTestMapSteps(ISceneRunner runner, string artifactDir)
	{
		for (int step = 0; step < 50; step++)
		{
			await runner.AwaitMillis(200);

			if (step % 5 == 0)
			{
				Image img = runner.Scene().GetViewport().GetTexture().GetImage();
				File.WriteAllBytes(Path.Combine(artifactDir, $"ChaosArena_Wasm_Step_{step:00}.png"), img.SavePngToBuffer());
			}

			if (GameContext.Host != null && GameContext.Host.AllUnits.Count == 0)
			{
				break;
			}
		}
	}

	private void VerifyAllUnitsKilled()
	{
		int aliveUnits = GameContext.Host?.AllUnits.Count ?? 0;
		GD.Print($"After WASM execution, alive units count = {aliveUnits}");
		if (aliveUnits > 0)
		{
			throw new Exception($"WASM map script failed to kill all units! Alive units remaining: {aliveUnits}");
		}
	}
}