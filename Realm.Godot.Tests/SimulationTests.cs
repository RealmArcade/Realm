namespace Realm.Godot.Tests;

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using GdUnit4;
using Godot;
using Realm.Ecs.Services;
using Realm.Shared;

[TestSuite]
[RequireGodotRuntime]
public class SimulationTests
{
    [TestCase]
    public async Task TestMeleePathingAroundTree()
    {
        if (LobbyManager.Instance == null)
        {
            return;
        }

        SetupSinglePlayerLobby("melee");

        ISceneRunner runner = ISceneRunner.Load("res://Main.tscn");
        await runner.AwaitMillis(1000);

        GameHost gameHost = GameHost.Instance;
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
        if (LobbyManager.Instance == null)
        {
            return;
        }

        string tempMapDir = Path.Combine(Path.GetTempPath(), "Realm_Simulation_WasmTestMap");
        if (Directory.Exists(tempMapDir))
        {
            try { Directory.Delete(tempMapDir, true); } catch {}
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
        
        SetupSinglePlayerLobby(Path.GetFullPath("Realm.Godot/Maps/melee"));
        GameHost.PendingMapScriptPath = wasmPath;

        WasmLogTracker tracker = AttachWasmLogListener();

        ISceneRunner runner = ISceneRunner.Load("res://Main.tscn");
        await runner.AwaitMillis(1000);

        GameHost gameHost = GameHost.Instance;
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

        if (LobbyManager.Instance == null)
        {
            return;
        }
        LobbyManager.Instance.IsSinglePlayer = true;
        PropertyInfo isHostProp = typeof(LobbyManager).GetProperty("IsHost", BindingFlags.Public | BindingFlags.Instance);
        isHostProp?.SetValue(LobbyManager.Instance, true);

        ISceneRunner runner = ISceneRunner.Load("res://Main.tscn");
        await runner.AwaitMillis(1500);

        UIManager.Instance.TransitionTo(GameScreen.MapEditorHUD);
        await runner.AwaitMillis(2500);

        var hud = MapEditorHUD.Instance;
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

        global::Godot.Image finalImage = runner.Scene().GetViewport().GetTexture().GetImage();
        File.WriteAllBytes(Path.Combine(artifactDir, "ChaosArena_Wasm_Tested.png"), finalImage.SavePngToBuffer());

        VerifyAllUnitsKilled();
    }

    [TestCase]
    public void TestEnsureCsprojRepairsStaleAbsoluteReference()
    {
        string tempMapDir = Path.Combine(Path.GetTempPath(), "Realm_Simulation_StaleCsprojRepair");
        if (Directory.Exists(tempMapDir))
        {
            try { Directory.Delete(tempMapDir, true); } catch { }
        }
        Directory.CreateDirectory(tempMapDir);

        string staleCsproj =
            "<Project Sdk=\"Microsoft.NET.Sdk\">" + Environment.NewLine +
            "  <PropertyGroup>" + Environment.NewLine +
            "    <TargetFramework>net10.0</TargetFramework>" + Environment.NewLine +
            "  </PropertyGroup>" + Environment.NewLine +
            "  <ItemGroup>" + Environment.NewLine +
            "    <ProjectReference Include=\"C:/Users/SomeoneElse/source/repos/Realm/Realm.MapAPI/Realm.MapAPI.csproj\" />" + Environment.NewLine +
            "  </ItemGroup>" + Environment.NewLine +
            "</Project>";
        File.WriteAllText(Path.Combine(tempMapDir, "MapScript.csproj"), staleCsproj);

        MapWorkspaceService.EnsureCsproj(tempMapDir, "MapScript");

        string repaired = File.ReadAllText(Path.Combine(tempMapDir, "MapScript.csproj"));
        Assertions.AssertThat(!repaired.Contains("C:")).IsTrue();
        Assertions.AssertThat(!repaired.Contains("ProjectReference")).IsTrue();
        Assertions.AssertThat(repaired.Contains("lib/Realm.MapAPI.dll")).IsTrue();

        Assertions.AssertThat(File.Exists(Path.Combine(tempMapDir, "lib", "Realm.MapAPI.dll"))).IsTrue();
    }

    private void SetupSinglePlayerLobby(string mapName)
    {
        LobbyManager.Instance.IsSinglePlayer = true;
        
        PropertyInfo isHostProp = typeof(LobbyManager).GetProperty("IsHost", BindingFlags.Public | BindingFlags.Instance);
        isHostProp?.SetValue(LobbyManager.Instance, true);

        LobbyManager.Instance.IsGameStarted = true;
        LobbyManager.Instance.ActiveMapName = mapName;
        LobbyManager.Instance.PlayerList.Clear();

        LobbyManager.PlayerInfo playerInfo = new LobbyManager.PlayerInfo
        {
            PeerId = 1,
            Slot = 0,
            Name = LobbyManager.Instance.AuthenticatedUsername,
            Faction = "HUMAN",
            Team = "Team 1",
            Color = new global::Godot.Color(0.8f, 0.1f, 0.1f),
            IsHost = true,
            Latency = "0 ms",
            Jitter = "0 ms",
            PacketLoss = "0%",
            BinaryVersion = RealmVersion.GameBinaryVersion
        };

        PropertyInfo localPlayerProp = typeof(LobbyManager).GetProperty("LocalPlayer", BindingFlags.Public | BindingFlags.Instance);
        localPlayerProp?.SetValue(LobbyManager.Instance, playerInfo);

        LobbyManager.Instance.PlayerList.Add(playerInfo);
    }

    private Unit3D SetupWorkerAndMove(GameHost gameHost)
    {
        Unit3D worker = gameHost.AllUnits.FirstOrDefault(u => u.UnitId == "worker" && !u.IsEnemy);
        if (worker == null)
        {
            return null;
        }

        gameHost.SelectedUnits.Clear();
        gameHost.SelectedUnits.Add(worker);
        worker.IsSelected = true;
        InGameHUD.Instance?.RefreshUI(gameHost.SelectedUnits);

        System.Numerics.Vector3 destination = new System.Numerics.Vector3(-20f, 0f, -50f);
        Realm.MapAPI.IUnit unitWrapper = gameHost.GetUnitWrapper(worker.Entity);
        unitWrapper.MoveTo(destination);
        return worker;
    }

    private void VerifyPathFollowComponent(GameHost gameHost, Unit3D worker)
    {
        if (gameHost.EcsWorld.Has<PathFollow>(worker.Entity))
        {
            var pf = gameHost.EcsWorld.Get<PathFollow>(worker.Entity);
            global::Godot.GD.Print($"WAYPOINTS COUNT: {pf.WaypointCount}");
            for (int i = 0; i < pf.WaypointCount; i++)
            {
                global::Godot.GD.Print($"Waypoint {i}: {pf.Waypoints[i]}");
            }
        }
        else
        {
            global::Godot.GD.Print("NO PATHFOLLOW COMPONENT");
        }
    }

    private async Task TakeSimulationScreenshots(ISceneRunner runner, string dirName)
    {
        string tempDir = Path.Combine(Path.GetTempPath(), dirName);
        if (Directory.Exists(tempDir))
        {
            try { Directory.Delete(tempDir, true); } catch {}
        }
        Directory.CreateDirectory(tempDir);

        for (int i = 1; i <= 15; i++)
        {
            await runner.AwaitMillis(1000);

            global::Godot.Image image = runner.Scene().GetViewport().GetTexture().GetImage();
            string fileName = $"Simulation_Step_{i:00}.png";
            string filePath = Path.Combine(tempDir, fileName);
            image.SavePng(filePath);
        }
    }

    private string CompileWasmProgram(string tempMapDir)
    {
        var compileProcess = new System.Diagnostics.Process();
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
            worker = gameHost.AllUnits.FirstOrDefault(u => u.UnitId == "worker" && !u.IsEnemy);
            if (worker != null)
            {
                break;
            }
            await runner.AwaitMillis(50);
        }

        if (worker == null)
        {
            throw new Exception("Worker unit spawned by WASM sandbox was not found on host GameHost.");
        }
        return worker;
    }

    private void VerifyWasmPathFollowComponent(GameHost gameHost, Unit3D worker)
    {
        if (gameHost.EcsWorld.Has<PathFollow>(worker.Entity))
        {
            var pf = gameHost.EcsWorld.Get<PathFollow>(worker.Entity);
            global::Godot.GD.Print($"WASM WAYPOINTS COUNT: {pf.WaypointCount}");
            for (int i = 0; i < pf.WaypointCount; i++)
            {
                global::Godot.GD.Print($"Wasm Waypoint {i}: {pf.Waypoints[i]}");
            }
            if (pf.WaypointCount <= 0)
            {
                throw new Exception("Worker has PathFollow component but waypoint count is 0.");
            }
        }
        else
        {
            global::Godot.GD.Print("WASM NO PATHFOLLOW COMPONENT");
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
        string chaosArenaFolder = Path.GetFullPath("../Realm_ChaosArena");
        if (!Directory.Exists(chaosArenaFolder))
        {
            chaosArenaFolder = Path.GetFullPath("D:/git/Realm/Realm_ChaosArena");
        }

        bool loadOk = hud.LoadMapFolder(chaosArenaFolder);
        if (!loadOk)
        {
            throw new Exception($"Failed to load map folder '{chaosArenaFolder}' into editor.");
        }
        await runner.AwaitMillis(1000);

        if (GameHost.Instance == null || GameHost.Instance.AllUnits.Count == 0)
        {
            throw new Exception("Units from terrain.json were not loaded into editor GameHost.");
        }
        int initialUnitCount = GameHost.Instance.AllUnits.Count;
        global::Godot.GD.Print($"Editor loaded {initialUnitCount} units from map terrain.json.");
    }

    private async Task ExecuteTestMapSteps(ISceneRunner runner, string artifactDir)
    {
        for (int step = 0; step < 50; step++)
        {
            await runner.AwaitMillis(200);

            if (step % 5 == 0)
            {
                global::Godot.Image img = runner.Scene().GetViewport().GetTexture().GetImage();
                File.WriteAllBytes(Path.Combine(artifactDir, $"ChaosArena_Wasm_Step_{step:00}.png"), img.SavePngToBuffer());
            }

            if (GameHost.Instance != null && GameHost.Instance.AllUnits.Count == 0)
            {
                break;
            }
        }
    }

    private void VerifyAllUnitsKilled()
    {
        int aliveUnits = GameHost.Instance?.AllUnits.Count ?? 0;
        global::Godot.GD.Print($"After WASM execution, alive units count = {aliveUnits}");
        if (aliveUnits > 0)
        {
            throw new Exception($"WASM map script failed to kill all units! Alive units remaining: {aliveUnits}");
        }
    }
}

