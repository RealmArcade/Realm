using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Realm.Client.Core;
using Realm.Client.Network;
using Realm.Client.Services;
using Realm.Client.Services.MapHistory;
using Realm.Client.UI.MapEditor;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Services;
using Realm.Shared.Distribution;
using static Godot.Control;

namespace Realm.Client.UI;

public partial class MapEditorHUD
{
	public async Task ProceedToTestMap()
	{
		if (GameContext.Host == null) return;

		_wasmHasErrors = false;
		ShowWasmConsoleModal();
		MatchLoadTrace.Begin("test_map");
		GameHost? tracingHost = GameContext.Host;
		Action<string> logHandler = line => AppendWasmConsoleLog(line);
		GameLog.OnLine += logHandler;

		try
		{
			SaveEditorStateBeforeTest();
			MatchLoadTrace.Mark("editor_state_saved");
			await PrepareMapAndWorkspaceForTest();
			MatchLoadTrace.Mark("map_saved_and_compiled");

			if (_wasmHasErrors) return;

			if (!TryLocateCompiledWasm(out string wasmPath)) return;

			AppendWasmConsoleLog($"[WASM] Running {Path.GetFileName(wasmPath)} from {Path.GetDirectoryName(wasmPath)} ({MapWorkspaceService.ReadBuildFlavorDomain(GameContext.EditorService._editingDirectory) ?? "unrecorded"} build)");

			await LaunchTestModeEngine();
		}
		catch (Exception ex)
		{
			_wasmHasErrors = true;
			SetWasmConsoleStatus("❌ Error launching test mode", new Color(1.0f, 0.3f, 0.3f));
			AppendWasmConsoleLog($"[RUNTIME EXCEPTION] {ex}");
		}
		finally
		{
			if (ReferenceEquals(GameContext.Host, tracingHost) && MatchLoadTrace.IsTracing) MatchLoadTrace.WriteSummary("test_map_abandoned");
			GameLog.OnLine -= logHandler;
		}
	}

	private void SaveEditorStateBeforeTest()
	{
		SaveCameraState();
		SaveGameHostStates();
		SaveEditorWorldBeforeTest();
	}

	private void SaveEditorWorldBeforeTest()
	{
		GameContext.SessionState.EditorWorldState = MapStateSnapshot.CreateSnapshot();
		GameContext.SessionState.SavedEditorDirectory = GameContext.EditorService._editingDirectory;
		GameContext.SessionState.SavedEditorSourceFolder = GameContext.EditorService.CurrentSourceFolder;
	}

	private void SaveCameraState()
	{
		var host = GameContext.Host;
		if (host == null) return;

		var camera = host.MainCamera;
		if (camera == null) return;

		GameContext.SessionState.SavedCameraPosition = camera.Position;

		if (GameContext.World != null && GameContext.World.IsAlive(GameContext.Simulation.WorldEntity) && GameContext.World.Has<CameraState>(GameContext.Simulation.WorldEntity))
		{
			var state = GameContext.World.Get<CameraState>(GameContext.Simulation.WorldEntity);
			GameContext.SessionState.SavedTargetHeight = state.TargetHeight;
			GameContext.SessionState.SavedCurrentHeight = state.CurrentHeight;
			GameContext.SessionState.SavedTargetYaw = state.TargetYaw;
			GameContext.SessionState.SavedCurrentYaw = state.CurrentYaw;
			GameContext.SessionState.SavedTargetPitch = state.TargetPitch;
			GameContext.SessionState.SavedCurrentPitch = state.CurrentPitch;
			GameContext.SessionState.SavedIsTopDown = state.IsTopDown;
			GameContext.SessionState.SavedYawSwing = state.YawSwing;
			GameContext.SessionState.SavedPitchSwing = state.PitchSwing;
		}
	}

	private void SaveGameHostStates()
	{
		var host = GameContext.Host;
		if (host == null) return;

		GameContext.SessionState.SavedGridMode = GameContext.EditorService.Tools.GridMode;
		GameContext.SessionState.SavedActiveTool = host.ActiveEditorTool;
		GameContext.SessionState.SavedActivePlaceId = host.ActivePlaceId;
		GameContext.SessionState.SavedCameraBoundsVisible = GameContext.EditorService.Tools.CameraBoundsVisible;
		GameContext.SessionState.SavedDisableShadows = GameContext.EditorService.Tools.DisableShadows;
		GameContext.SessionState.SavedEditorHasUnsavedChanges = host.EditorHasUnsavedChanges;
		GameContext.SessionState.SavedEntityCategory = _entityPaletteController?.CurrentCategory ?? "";
		GameContext.SessionState.SavedBrushRadius = (float)(_brushPanel.SizeSlider?.Value ?? 1f);
		float val = (float)(_brushPanel.StrengthSlider?.Value ?? 1f);
		GameContext.SessionState.SavedBrushStrength = val;
	}

	private async Task PrepareMapAndWorkspaceForTest()
	{
		var host = GameContext.Host;
		if (host == null) return;

		MapWorkspaceService.EnsureWasmEntryPoint(GameContext.EditorService._editingDirectory);
		string tempTerrainPath = Path.Combine(GameContext.EditorService._editingDirectory, "terrain.json");
		host.SaveMapToFile(tempTerrainPath, performReload: false);
		host.EditorHasUnsavedChanges = false;
		InvalidateMetadataCache();

		if (!ApplyMapReferencesOnSave(GameContext.EditorService._editingDirectory))
		{
			SetWasmConsoleStatus("❌ Referenced maps could not be applied", new Color(1.0f, 0.3f, 0.3f));
			return;
		}

		await GameContext.MapHistoryService.CreateVersionAsync(
			GameContext.EditorService._editingDirectory,
			MapVersionKind.BeforePlaytest,
			null,
			GameContext.EditorService.CurrentSourceFolder);

		if (OperatingSystem.IsWindows())
		{
			SetWasmConsoleStatus("Auto-saving modified files in VSCode...", UIStyle.ColorCyanGlow);
			AppendWasmConsoleLog("[VSCode] Requesting auto-save of all open workspace files...");
			await GameContext.VSCode.SaveAllOpenFilesAsync();
		}

		SetWasmConsoleStatus("Compiling WASM map script...", UIStyle.ColorCyanGlow);
		AppendWasmConsoleLog("=== WASM COMPILATION PIPELINE STARTED ===");
		AppendWasmConsoleLog("[DEBUG] The map script runs in the authority process, not in this editor. Attach with the workspace's 'Attach to Realm Authority (Map Script WASM)' configuration; the authority reports its PID and the JIT ELF it exported once it is running.");
		AppendWasmConsoleLog("[DEBUG] " + DebuggingHelpText.GetTitle());
		foreach (string line in DebuggingHelpText.GetLines())
		{
			AppendWasmConsoleLog("[DEBUG] " + line);
		}

		await CompileWorkspaceScriptAsync(GameContext.EditorService._editingDirectory, isDebug: true);

		if (_wasmHasErrors)
		{
			SetWasmConsoleStatus("❌ WASM Compilation Failed", new Color(1.0f, 0.3f, 0.3f));
			AppendWasmConsoleLog("[ERROR] Map script compilation failed. Test mode aborted.");
		}
	}

	private bool TryLocateCompiledWasm(out string wasmPath)
	{
		string editingDirectory = GameContext.EditorService._editingDirectory;
		wasmPath = MapWorkspaceService.SearchForWasmInDirectoryDomain(Path.Combine(editingDirectory, "bin")) ?? string.Empty;

		if (string.IsNullOrEmpty(wasmPath) || !File.Exists(wasmPath))
		{
			_wasmHasErrors = true;
			SetWasmConsoleStatus("❌ WASM Compilation Failed: Output binary missing", new Color(1.0f, 0.3f, 0.3f));
			AppendWasmConsoleLog($"[ERROR] Could not find the compiled WASM in {MapWorkspaceService.GetScriptOutputDirectory(editingDirectory)}. Test mode aborted.");
			return false;
		}

		return true;
	}

	private async Task LaunchTestModeEngine()
	{
		SetWasmConsoleStatus("Launching test mode...", UIStyle.ColorCyanGlow);
		AppendWasmConsoleLog("=== LAUNCHING GAME ENGINE ===");

		if (GameContext.UserInterface != null)
		{
			await GameContext.UserInterface.ApplyWindowSettings(GameSettings.WindowModeIdx, GameSettings.ResolutionIdx);
		}

		GameContext.SessionState.IsTestMode = true;
		ShadowClientVerifier.IsEnabled = true;

		SetWasmConsoleStatus("Starting the authority that runs this map...", UIStyle.ColorCyanGlow);
		AppendWasmConsoleLog("[AUTHORITY] Starting the process the map script runs in; the editor stays responsive while it loads.");

		bool started = false;
		if (GameContext.Lobby != null)
		{
			started = await GameContext.Lobby.HostSinglePlayerGame(GameContext.EditorService._editingDirectory, "Test Map", debugMapScript: true);
		}

		if (!started)
		{
			_wasmHasErrors = true;
			SetWasmConsoleStatus("❌ Test mode could not start", new Color(1f, 0.3f, 0.3f));
			AppendWasmConsoleLog($"[ERROR] {GameContext.Lobby?.LastHostError ?? "this build has no lobby to start a match with"}");
			AppendAuthorityDiagnostics();
			return;
		}

		MatchLoadTrace.Mark("authority_started");
		MatchLoadTrace.WriteSummary("authority_started");

		GameContext.Host?.ExitMapEditorMode();

		if (GameContext.UserInterface != null)
		{
			GameContext.UserInterface.TransitionTo(GameScreen.InGameHUD);
		}

		int authorityProcessId = GameContext.ServerSession?.AuthorityProcessId ?? 0;
		if (authorityProcessId > 0)
		{
			string attachHint = $"[DEBUG] Authority process ID: {authorityProcessId}. Attach to it in CodeLLDB with the workspace's map script configuration.";
			AppendWasmConsoleLog(attachHint);
			GameLog.LogToConsole(attachHint);
		}

		await Task.Delay(500);
		CloseWasmConsoleModal();
	}

	private const int AuthorityDiagnosticsLineCount = 20;

	private void AppendAuthorityDiagnostics()
	{
		string consolePath = GameContext.Lobby?.LastAuthorityConsoleLogPath ?? string.Empty;
		if (consolePath.Length == 0) return;

		try
		{
			if (!File.Exists(consolePath))
			{
				AppendWasmConsoleLog($"[AUTHORITY] It wrote no console output; its log is '{GameContext.Lobby?.LastAuthorityLogPath}'.");
				return;
			}

			string[] lines = File.ReadAllLines(consolePath);
			int first = Math.Max(0, lines.Length - AuthorityDiagnosticsLineCount);
			AppendWasmConsoleLog($"=== AUTHORITY CONSOLE ({consolePath}, last {lines.Length - first} of {lines.Length} line(s)) ===");
			for (int index = first; index < lines.Length; index++) AppendWasmConsoleLog(lines[index]);
		}
		catch (IOException exception)
		{
			AppendWasmConsoleLog($"[AUTHORITY] Its console output could not be read: {exception.Message}");
		}
		catch (UnauthorizedAccessException exception)
		{
			AppendWasmConsoleLog($"[AUTHORITY] Its console output could not be read: {exception.Message}");
		}
	}
}