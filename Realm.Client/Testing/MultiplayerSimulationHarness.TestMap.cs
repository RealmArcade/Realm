using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Godot;
using Realm.Client.Core;
using Realm.Client.Network;
using Realm.Client.Services;
using Realm.Client.UI;
using Realm.Ecs;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Resources;
using Realm.Ecs.Services;
using Realm.Shared;
using Realm.Shared.Maps;

namespace Realm.Client.Testing;


public partial class MultiplayerSimulationHarness : Node
{
	private async Task<int> RunTestMapAsync()
	{
		LobbyManager lobby = GameContext.Lobby ?? throw new InvalidOperationException("LobbyManager autoload is unavailable.");

		GameContext.SessionState.IsTestMode = true;
		lobby.IsSinglePlayer = true;
		lobby.IsHost = true;
		lobby.IsGameStarted = true;
		lobby.ActiveMapName = _mapFolder;

		string? mapDirectory = GameHost.ResolveMapDirectory(_mapFolder);
		GameLog.LogToConsole($"[TESTMAP] map '{_mapFolder}' resolved to '{mapDirectory}'.");
		if (string.IsNullOrEmpty(mapDirectory)) return 1;

		if (_realFlow)
		{
			return await RunRealTestMapFlowAsync(mapDirectory);
		}

		if (_editorFirst)
		{
			GameContext.SessionState.IsMapEditorMode = true;
			PackedScene editorScene = GD.Load<PackedScene>("res://GameHost.tscn");
			Node editorHost = editorScene.Instantiate();
			editorHost.Name = "GameHost";
			AddChild(editorHost);
			await ToSignal(GetTree().CreateTimer(4.0f), SceneTreeTimer.SignalName.Timeout);
			DescribeTerrain(-1);

			GameContext.Host?.ExitMapEditorMode();
			RemoveChild(editorHost);
			editorHost.QueueFree();
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		}

		if (_startAuthority)
		{
			ClientNetworkChannel channel = GetNodeOrNull<ClientNetworkChannel>("/root/RealmNetwork") ?? throw new InvalidOperationException("This build has no channel to an authority.");
			GameContext.ServerSession?.Dispose();
			var session = new ClientServerSession(channel);
			var start = new ServerMatchStart(mapDirectory, "Test Map", HarnessMatchSeed, Path.Combine(_outputDirectory, "listen_server"), _outputDirectory, LocalizationManager.GetCurrentLanguageCode(), AuthorityRecordDirectory: _outputDirectory);
			MatchStartResult started = await session.TryStartAsync(start, _port);
			if (!started.Started) throw new InvalidOperationException($"The authority could not be started: {started.Failure}");
			GameContext.AttachServerSession(session);
			_session = session;
			GameLog.LogToConsole($"[TESTMAP] the authority is listening on port {session.Port} with its log at {session.LogPath}.");
		}

		PackedScene scene = GD.Load<PackedScene>("res://GameHost.tscn");
		Node gameHost = scene.Instantiate();
		gameHost.Name = "GameHost";
		AddChild(gameHost);

		for (int second = 0; second < 12; second++)
		{
			await ToSignal(GetTree().CreateTimer(1.0f), SceneTreeTimer.SignalName.Timeout);
			DescribeTerrain(second);
		}

		return 0;
	}

	private async Task<int> RunProbeAsync()
	{
		_probeOwnsProcess = true;
		GameLog.LogToConsole("[PROBE] watching the map the playtest loads.");
		for (int second = 0; second < 30; second++)
		{
			await ToSignal(GetTree().CreateTimer(1.0f), SceneTreeTimer.SignalName.Timeout);
			DescribeTerrain(second);
			GameHost? current = GameContext.Host;
			int appliedSequence = GameContext.NetworkService?.LastAppliedSnapshotSequenceForDiagnostics ?? -1;
			GameLog.LogToConsole($"[PROBE] {second}s: session={(GameContext.ServerSession == null ? "none" : "attached")} started={GameContext.Lobby?.IsGameStarted} sequence={appliedSequence} units={current?.AllUnits.Count ?? -1} slot={GameContext.ServerSession?.LocalPlayerSlot ?? -1}");
		}

		GetTree().Quit(0);
		return 0;
	}

	private async Task<int> RunRealTestMapFlowAsync(string mapDirectory)
	{
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		var probeScene = GD.Load<PackedScene>("res://Testing/MultiplayerSimulationHarness.tscn");
		Node probe = probeScene.Instantiate();
		probe.Name = "TestMapProbe";
		SetPrivateProperty(probe, "Role", "probe");
		GetTree().Root.AddChild(probe);

		GameContext.SessionState.IsMapEditorMode = true;
		PackedScene editorScene = GD.Load<PackedScene>("res://GameHost.tscn");
		Node editorHost = editorScene.Instantiate();
		editorHost.Name = "GameHost";
		AddChild(editorHost);
		await ToSignal(GetTree().CreateTimer(4.0f), SceneTreeTimer.SignalName.Timeout);

		GameContext.SessionState.IsTestMode = true;
		GameLog.LogToConsole($"[TESTMAP] asking the lobby for a single-player match of '{_mapFolder}' ('{mapDirectory}').");
		bool playtestStarted = GameContext.Lobby != null && await GameContext.Lobby.HostSinglePlayerGame(_mapFolder, "Test Map");
		GameContext.Host?.ExitMapEditorMode();
		GameLog.LogToConsole($"[TESTMAP] the lobby was asked; started={playtestStarted} error={GameContext.Lobby?.LastHostError}; the probe reports what follows.");

		_probeOwnsProcess = true;
		return 0;
	}

	private static void DescribeTerrain(int second)
	{
		GameHost? host = GameContext.Host;
		RuntimeTerrain? terrain = host?.GroundTerrain;
		if (terrain == null)
		{
			GameLog.LogToConsole($"[TESTMAP] {second}s: no ground terrain node.");
			return;
		}

		int chunks = terrain.Chunks.Count;
		int visibleChunks = 0;
		int surfaces = 0;
		int vertices = 0;
		foreach (TerrainChunk chunk in terrain.Chunks)
		{
			if (chunk.MeshInstance != null && GodotObject.IsInstanceValid(chunk.MeshInstance))
			{
				if (chunk.MeshInstance.Visible) visibleChunks++;
				ArrayMesh? mesh = chunk.MeshInstance.Mesh as ArrayMesh;
				if (mesh == null) continue;
				surfaces += mesh.GetSurfaceCount();
				for (int surface = 0; surface < mesh.GetSurfaceCount(); surface++) vertices += mesh.SurfaceGetArrays(surface)[(int)Mesh.ArrayType.Vertex].AsVector3Array().Length;
			}
		}

		Godot.Vector3 camera = host!.MainCamera?.GlobalPosition ?? Godot.Vector3.Zero;
		GameLog.LogToConsole($"[TESTMAP] {second}s: grid={terrain.Width}x{terrain.Depth} quad={terrain.QuadSize} nodeVisible={terrain.Visible} at={terrain.GlobalPosition} chunks={chunks} visibleChunks={visibleChunks} surfaces={surfaces} vertices={vertices} camera={camera} world={GameContext.World != null}");
	}

	private async Task<bool> StartSimulationAsync(LobbyManager lobby, bool isHost)
	{
		lobby.ActiveMapName = _mapFolder;
		lobby.IsGameStarted = true;
		GameSettings.RecordReplays = false;

		PackedScene scene = GD.Load<PackedScene>("res://GameHost.tscn");
		Node gameHost = scene.Instantiate();
		gameHost.Name = "GameHost";
		AddChild(gameHost);


		int readinessLogCounter = 0;
		bool ready = await WaitUntilAsync(() =>
		{
			GameHost? host = GameContext.Host;
			int appliedSequence = GameContext.NetworkService?.LastAppliedSnapshotSequenceForDiagnostics ?? -1;
			bool hasBaseline = GameContext.World != null && GameContext.World.IsAlive(GameContext.Simulation.WorldEntity) && GameContext.World.Has<NetworkState>(GameContext.Simulation.WorldEntity)
				&& GameContext.World.Get<NetworkState>(GameContext.Simulation.WorldEntity).HasReceivedInitialBaseline;
			bool isReady = host != null && appliedSequence >= 0 && hasBaseline;
			readinessLogCounter++;
			if (!isReady && readinessLogCounter % 50 == 0)
			{
				GameLog.LogToConsole($"[MP_HARNESS] Readiness wait ({(isHost ? "host" : "client")}): elapsed={host?.GameElapsedTime ?? 0f:F2} units={host?.AllUnits.Count ?? 0} appliedSequence={appliedSequence} baseline={hasBaseline} peerPresent={Multiplayer.MultiplayerPeer != null}");
			}

			return isReady;
		}, 120f);

		if (!ready)
		{
			GameLog.LogError("[MP_HARNESS] Game simulation did not become ready in time.");
			return false;
		}

		MultiplayerSyncDiagnostics.Start(isHost ? Path.Combine(_outputDirectory, "host") : _outputDirectory);
		PrintReplicatedCodecTable(isHost);
		TryWriteReadyFile();
		GameHost currentHost = GameContext.Host!;
		GameLog.LogToConsole($"[MP_HARNESS] {(isHost ? "Host" : "Client")} simulation ready at game time {currentHost.GameElapsedTime:F2}s with {currentHost.AllUnits.Count} units.");
		GameLog.LogToConsole($"[MP_HARNESS] Active workspace '{MapWorkspaceService.GetActiveWorkspacePath()}' resolved from map directory '{GameContext.MapStorageService?.CurrentMapDirectory}'.");
		return true;
	}

	private static void PrintReplicatedCodecTable(bool isHost)
	{
		foreach (var codec in Realm.Ecs.Replication.ReplicatedComponents.All)
		{
			GameLog.LogToConsole($"[CODECS] role={(isHost ? "host" : "client")} id={codec.ComponentTypeId} name={codec.ComponentName} size={codec.SizeInBytes} scope={codec.Scope}");
		}

		GameLog.LogToConsole($"[CODECS] role={(isHost ? "host" : "client")} registryCount={Realm.Ecs.ComponentTypeRegistry.Count} scriptPlayersId={Realm.Ecs.ComponentType<ScriptPlayersState>.Id}");
	}

}
