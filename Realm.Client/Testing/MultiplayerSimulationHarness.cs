using System;
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

namespace Realm.Client.Testing;

[GlobalClass]
public partial class MultiplayerSimulationHarness : Node
{
	private const int PlayableSlotCount = 12;
	private const string HostPlayerName = "HarnessHost";
	private const int HarnessMatchSeed = 12345;

	private string _role = "host";
	private string _hostAddress = "127.0.0.1";
	private int _port = 8999;
	private string _mapFolder = string.Empty;
	private string _outputDirectory = string.Empty;
	private string _listeningFile = string.Empty;
	private string _readyFile = string.Empty;
	private string _doneFile = string.Empty;
	private string _sequenceFile = string.Empty;
	private string _clientBuildId = string.Empty;
	private int _drainMilliseconds = 0;
	private int _finalSequence = -1;
	private float _durationSeconds = 15f;
	private bool _predictionEnabled;
	private bool _scenarioEnabled;
	private string _networkProfile = "Latency";
	private int _peerCount = 2;
	private int _peerIndex = 1;
	private float _dropAtSeconds;
	private float _pauseAtSeconds;
	private bool _probesEnabled;
	private string _waitForPeerReadyFile = string.Empty;
	private int _hardeningFailureCount;
	private int _recordedClientPeerId = -1;
	private ClientServerSession? _session;

	public override void _Ready()
	{
		_ = RunAsync();
	}

	private async Task RunAsync()
	{
		int exitCode = 1;
		try
		{
			ParseArguments();
			ShadowClientVerifier.IsEnabled = true;
			ShadowClientVerifier.Reset();
			exitCode = _role switch
			{
				"host" => await RunHostAsync(),
				_ => await RunClientAsync()
			};
		}
		catch (Exception exception)
		{
			GD.PrintErr($"[MP_HARNESS] Unhandled failure: {exception}");
		}
		finally
		{
			TryWriteDoneFile();
			GetTree().Quit(exitCode);
		}
	}

	private void ParseArguments()
	{
		foreach (string argument in OS.GetCmdlineUserArgs())
		{
			int separatorIndex = argument.IndexOf('=');
			if (separatorIndex <= 0) continue;

			string key = argument.Substring(0, separatorIndex);
			string value = argument.Substring(separatorIndex + 1);
			switch (key)
			{
				case "--role":
					_role = value;
					break;
				case "--host":
					_hostAddress = value;
					break;
				case "--port":
					_port = int.Parse(value);
					break;
				case "--map":
					_mapFolder = value;
					break;
					break;
				case "--output":
					_outputDirectory = value;
					break;
				case "--listening-file":
					_listeningFile = value;
					break;
				case "--ready-file":
					_readyFile = value;
					break;
				case "--done-file":
					_doneFile = value;
					break;
				case "--sequence-file":
					_sequenceFile = value;
					break;
				case "--client-build":
					_clientBuildId = value;
					break;
					break;
				case "--drain-ms":
					_drainMilliseconds = int.Parse(value);
					break;
				case "--duration":
					_durationSeconds = float.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
					break;
				case "--prediction":
					_predictionEnabled = value == "1";
					break;
				case "--scenario":
					_scenarioEnabled = value == "1";
					break;
				case "--profile":
					_networkProfile = value;
					break;
					break;
				case "--peers":
					_peerCount = int.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
					break;
				case "--peer-index":
					_peerIndex = int.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
					break;
				case "--wait-for-peer-ready":
					_waitForPeerReadyFile = value;
					break;
				case "--drop-at":
					_dropAtSeconds = float.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
					break;
				case "--pause-at":
					_pauseAtSeconds = float.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
					break;
				case "--probes":
					_probesEnabled = value == "1";
					break;
			}
		}

		if (string.IsNullOrEmpty(_outputDirectory)) throw new InvalidOperationException("Missing --output argument.");
	}

	private async Task<int> RunHostAsync()
	{
		LobbyManager lobby = GameContext.Lobby ?? throw new InvalidOperationException("LobbyManager autoload is unavailable.");

		lobby.IsSinglePlayer = false;
		lobby.IsHost = true;
		lobby.IsGameStarted = true;
		lobby.ActiveMapName = _mapFolder;
		SetPrivateProperty(lobby, "MaxPlayers", PlayableSlotCount);
		SetPrivateProperty(lobby, "ActiveLobbyId", $"direct-harness-{Guid.NewGuid():N}");

		lobby.PlayerList.Clear();
		lobby.PlayerList.Add(new PlayerInfo
		{
			PeerId = 1,
			Slot = 1,
			Name = HostPlayerName,
			Faction = "HUMAN",
			Team = "Team 1",
			Color = PlayerColorConfig.GetColor(1),
			IsHost = true,
			Latency = "0 ms",
			Jitter = "0 ms",
			PacketLoss = "0%",
			BinaryVersion = RealmVersion.GameBinaryVersion
		});

		MultiplayerBandwidthMeter.Reset();
		MultiplayerBandwidthMeter.IsRecording = true;

		ClientServerSession session = StartAuthority();
		GD.Print($"[MP_HARNESS] The authority is listening on port {session.Port}; this process plays as one of its clients.");
		if (!string.IsNullOrEmpty(_listeningFile)) File.WriteAllText(_listeningFile, session.Port.ToString(System.Globalization.CultureInfo.InvariantCulture));

		int expectedClientCount = Math.Max(1, _peerCount - 1);
		bool clientsSeated = await WaitUntilAsync(() => CountSeatedPeers(session) >= expectedClientCount + 1, 240f);
		if (!clientsSeated)
		{
			throw new InvalidOperationException($"Timed out waiting for {expectedClientCount} client peer(s) to be seated; the authority recorded {CountSeatedPeers(session)} seat(s) including this process.");
		}

		if (!await StartSimulationAsync(lobby, true)) return 1;


		GameHost gameHost = GameContext.Host ?? throw new InvalidOperationException("GameHost instance is unavailable.");

		// The peer that started the authority is the one player this map gives starting units to, so it is also the one
		// that can be asked to spend one on a building. That order travels the same path a player's order does.
		if (!string.IsNullOrEmpty(_clientBuildId)) IssueClientBuildRequest(gameHost);

		// Every client joins the lobby before the map loads, but the clients load the map one at a time so a single
		// machine is not asked to compile and instantiate several at once. The match clock would otherwise run out for the
		// clients still waiting their turn, and they would record an empty stream and never acknowledge a snapshot.
		double matchStartGameTime = await WaitForClientsToStartAsync(gameHost);
		bool finished = false;
		ulong simulationDeadline = Time.GetTicksMsec() + (ulong)((_durationSeconds * 4f + 120f) * 1000f);
		while (Time.GetTicksMsec() < simulationDeadline)
		{
			if (gameHost.GameElapsedTime >= matchStartGameTime + _durationSeconds)
			{
				finished = true;
				break;
			}

			GD.Print($"[MP_HARNESS] Host game time {gameHost.GameElapsedTime:F2}s / {_durationSeconds:F0}s, units {gameHost.AllUnits.Count}, construction sites {CountConstructionSites(gameHost)}, snapshots {ResolveSnapshotSequence()}.");
			await ToSignal(GetTree().CreateTimer(1.0f), SceneTreeTimer.SignalName.Timeout);
		}

		if (!finished) throw new InvalidOperationException($"Host simulation did not reach {_durationSeconds}s of game time after every client started.");

		if (_drainMilliseconds > 0)
		{
			GD.Print($"[MP_HARNESS] Host draining for {_drainMilliseconds} ms so delayed clients can catch up.");
			await ToSignal(GetTree().CreateTimer(_drainMilliseconds / 1000f), SceneTreeTimer.SignalName.Timeout);
		}

		_hardeningFailureCount = MultiplayerHardeningChecks.Run(_outputDirectory, true);

		_finalSequence = ResolveSnapshotSequence();
		MultiplayerBandwidthMeter.BuildReport(_outputDirectory, gameHost.GameElapsedTime);
		GD.Print($"[MP_HARNESS] Host finished at game time {gameHost.GameElapsedTime:F2}s with {gameHost.AllUnits.Count} units and snapshot sequence {_finalSequence}.");
		MultiplayerSyncDiagnostics.Stop();
		PredictionDiagnostics.Stop();
		WriteFinalWorldSnapshot("ECS_host_final_full.bin");
		return 0;
	}

	private ClientServerSession StartAuthority()
	{
		ClientNetworkChannel channel = GetNodeOrNull<ClientNetworkChannel>("/root/RealmNetwork") ?? throw new InvalidOperationException("This build has no channel to an authority.");
		GameContext.ServerSession?.Dispose();

		var session = new ClientServerSession(channel);
		var start = new ServerMatchStart(
			_mapFolder,
			Path.GetFileName(Path.TrimEndingDirectorySeparator(_mapFolder)),
			HarnessMatchSeed,
			Path.Combine(_outputDirectory, "listen_server"),
			_outputDirectory,
			LocalizationManager.GetCurrentLanguageCode(),
			OpenToOthers: true);

		if (!session.TryStart(start, out string failure, _port)) throw new InvalidOperationException($"The authority could not be started on port {_port}: {failure}");

		GameContext.AttachServerSession(session);
		_session = session;
		return session;
	}

	private ClientServerSession JoinAuthority()
	{
		ClientNetworkChannel channel = GetNodeOrNull<ClientNetworkChannel>("/root/RealmNetwork") ?? throw new InvalidOperationException("This build has no channel to an authority.");
		GameContext.ServerSession?.Dispose();

		var session = new ClientServerSession(channel);
		if (!session.TryJoin(_hostAddress, _port, LocalizationManager.GetCurrentLanguageCode(), out string failure))
		{
			throw new InvalidOperationException($"The authority at {_hostAddress}:{_port} could not be reached: {failure}");
		}

		GameContext.AttachServerSession(session);
		_session = session;
		return session;
	}

	private static int CountSeatedPeers(ClientServerSession session)
	{
		if (!File.Exists(session.LogPath)) return 0;

		try
		{
			using var stream = new FileStream(session.LogPath, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite | System.IO.FileShare.Delete);
			using var reader = new StreamReader(stream);
			string log = reader.ReadToEnd();
			int count = 0;
			int index = log.IndexOf("seated as player", StringComparison.Ordinal);
			while (index >= 0)
			{
				count++;
				index = log.IndexOf("seated as player", index + 1, StringComparison.Ordinal);
			}

			return count;
		}
		catch (IOException)
		{
			return 0;
		}
	}

	private async Task<int> RunClientAsync()
	{
		LobbyManager lobby = GameContext.Lobby ?? throw new InvalidOperationException("LobbyManager autoload is unavailable.");

		lobby.IsSinglePlayer = false;
		lobby.IsHost = false;
		lobby.IsGameStarted = true;
		lobby.ActiveMapName = _mapFolder;
		SetPrivateProperty(lobby, "MaxPlayers", PlayableSlotCount);

		ClientServerSession session = JoinAuthority();
		bool seated = await WaitUntilAsync(() => session.LocalPlayerSlot >= 0, 120f);
		if (!seated) throw new InvalidOperationException($"The authority at {_hostAddress}:{_port} never seated this client: {session.TerminationReport ?? "it did not answer"}.");

		int localPeerId = session.LocalPeerId;
		RecordClientPeerId(localPeerId);
		GD.Print($"[MP_HARNESS] Client peer index {_peerIndex} was seated as player {session.LocalPlayerSlot} with peer id {localPeerId}.");

		if (!string.IsNullOrEmpty(_waitForPeerReadyFile))
		{
			GD.Print($"[MP_HARNESS] Client peer index {_peerIndex} stays in the lobby until {_waitForPeerReadyFile} exists, so only two processes load the map at once.");
			bool previousPeerReady = await WaitUntilAsync(() => File.Exists(_waitForPeerReadyFile), 240f);
			if (!previousPeerReady) throw new InvalidOperationException($"Client peer index {_peerIndex} waited too long for the previous peer to become ready.");
		}

		if (!await StartSimulationAsync(lobby, false)) return 1;


		GD.Print("[MP_HARNESS] Client simulation started. Recording until the host signals completion.");
		_scenarioTick = 0;
		_scenarioPass = 0;
		if (!string.IsNullOrEmpty(_clientBuildId) && !_scenarioEnabled)
		{
			await ToSignal(GetTree().CreateTimer(2.0f), SceneTreeTimer.SignalName.Timeout);
			IssueClientBuildRequest(GameContext.Host);
		}
		if (_dropAtSeconds > 0f) _ = RunConnectionLossScenarioAsync(session);
		if (_pauseAtSeconds > 0f) _ = RunPauseScenarioAsync(session);

		bool hostFinished = await WaitUntilAsync(() => File.Exists(_doneFile), _durationSeconds * 4f + 120f + (_drainMilliseconds / 1000f));
		if (!hostFinished) GD.PrintErr("[MP_HARNESS] Host completion marker was not observed before the timeout.");

		if (_drainMilliseconds > 0)
		{
			await ToSignal(GetTree().CreateTimer(_drainMilliseconds / 1000f), SceneTreeTimer.SignalName.Timeout);
		}

		_finalSequence = GameContext.NetworkService.LastAppliedSnapshotSequenceForDiagnostics;
		GD.Print($"[MP_HARNESS] Client stopped at applied snapshot sequence {_finalSequence} with game time {GameContext.Host?.GameElapsedTime ?? 0f:F2}s.");
		MultiplayerSyncDiagnostics.Stop();
		PredictionDiagnostics.Stop();
		TryWriteSequenceFile(_finalSequence);
		WriteFinalWorldSnapshot("ECS_client_final_full.bin");
		return hostFinished ? 0 : 1;
	}

	/// <summary>
	/// Pauses the match once from this peer and resumes it again, and records what this screen saw, so a test can prove
	/// the pause travelled: the peer asked, the authority decided, and every screen was told the state it holds.
	/// </summary>
	private async Task RunPauseScenarioAsync(ClientServerSession session)
	{
		string path = Path.Combine(_outputDirectory, "pause.txt");
		try
		{
			bool reachedPauseTime = await WaitUntilAsync(() => (GameContext.Host?.GameElapsedTime ?? 0f) >= _pauseAtSeconds, 180f);
			if (!reachedPauseTime)
			{
				File.WriteAllText(path, "paused=0\nreason=game time never reached the pause point\n");
				return;
			}

			int obstacles = session.LatestNavMeshObstacles?.Positions?.Length ?? -1;
			GD.Print($"[MP_HARNESS] This client holds {obstacles} obstacle volumes from the match.");

			bool requested = session.RequestPause(true);
			bool paused = await WaitUntilAsync(() => GameContext.NetworkService.IsPaused, 30f);
			GD.Print($"[MP_HARNESS] Pause requested={requested}; the match reports paused={paused} by player {GameContext.NetworkService.PausedByPlayerIndex}.");

			await ToSignal(GetTree().CreateTimer(2.0f), SceneTreeTimer.SignalName.Timeout);

			bool resumeRequested = session.RequestPause(false);
			bool resumed = await WaitUntilAsync(() => !GameContext.NetworkService.IsPaused, 30f);
			GD.Print($"[MP_HARNESS] Resume requested={resumeRequested}; the match reports paused={GameContext.NetworkService.IsPaused}.");

			File.WriteAllText(
				path,
				$"paused={(paused ? 1 : 0)}\nresumed={(resumed ? 1 : 0)}\nobstacle_volumes={obstacles}\nrequested_by={GameContext.NetworkService.PausedByPlayerIndex}\n");
		}
		catch (Exception exception)
		{
			GD.PrintErr($"[MP_HARNESS] Pause scenario failed: {exception.Message}");
			try
			{
				File.WriteAllText(path, $"paused=0\nreason={exception.Message}\n");
			}
			catch
			{
			}
		}
	}

	/// <summary>
	/// Drops this client's connection once mid-match and waits for it to rejoin, so the reconnect path is exercised the
	/// way a lost host would exercise it. The outcome is written for the verifier: the sequence the client had applied
	/// when it dropped, the first sequence it applied after rejoining, and whether it rejoined at all.
	/// </summary>
	private async Task RunConnectionLossScenarioAsync(ClientServerSession session)
	{
		string path = Path.Combine(_outputDirectory, "client_drop.txt");
		try
		{
			bool reachedDropTime = await WaitUntilAsync(() => (GameContext.Host?.GameElapsedTime ?? 0f) >= _dropAtSeconds, 120f);
			if (!reachedDropTime)
			{
				File.WriteAllText(path, "dropped=0\nreason=game time never reached the drop point\n");
				return;
			}

			int dropSequence = GameContext.NetworkService.LastAppliedSnapshotSequenceForDiagnostics;
			float dropGameTime = GameContext.Host?.GameElapsedTime ?? 0f;
			GD.Print($"[MP_HARNESS] Dropping the client connection at game time {dropGameTime:F2}s, applied sequence {dropSequence}.");
			session.DropConnectionForHarness();

			bool resumed = await WaitUntilAsync(
				() => GameContext.NetworkService.LastAppliedSnapshotSequenceForDiagnostics > dropSequence,
				60f);

			int resumeSequence = GameContext.NetworkService.LastAppliedSnapshotSequenceForDiagnostics;
			int resumeBaselineSequence = GameContext.NetworkService.LastAppliedBaselineSequenceForDiagnostics;
			float resumeGameTime = GameContext.Host?.GameElapsedTime ?? 0f;
			File.WriteAllText(
				path,
				$"dropped=1\nresumed={(resumed ? 1 : 0)}\ndrop_sequence={dropSequence}\nresume_sequence={resumeSequence}\nresume_baseline_sequence={resumeBaselineSequence}\noutage_sequences={Math.Max(0, resumeSequence - dropSequence)}\ndrop_game_time={dropGameTime.ToString(System.Globalization.CultureInfo.InvariantCulture)}\nresume_game_time={resumeGameTime.ToString(System.Globalization.CultureInfo.InvariantCulture)}\n");
			GD.Print($"[MP_HARNESS] Client connection loss scenario resumed={resumed} at sequence {resumeSequence} after {Math.Max(0, resumeSequence - dropSequence)} sequences.");
		}
		catch (Exception exception)
		{
			GD.PrintErr($"[MP_HARNESS] Connection loss scenario failed: {exception.Message}");
			try
			{
				File.WriteAllText(path, $"dropped=1\nresumed=0\nreason={exception.Message}\n");
			}
			catch
			{
			}
		}
	}

	private async Task<bool> StartSimulationAsync(LobbyManager lobby, bool isHost)
	{
		lobby.ActiveMapName = _mapFolder;
		lobby.IsGameStarted = true;
		GameSettings.RecordReplays = false;
		GameSettings.MovementPrediction = _predictionEnabled;

		PackedScene scene = GD.Load<PackedScene>("res://GameHost.tscn");
		Node gameHost = scene.Instantiate();
		gameHost.Name = "GameHost";
		AddChild(gameHost);

		GameSettings.MovementPrediction = _predictionEnabled;
		GD.Print($"[MP_HARNESS] Prediction requested={_predictionEnabled} developmentBuild={Realm.Client.Utils.PathUtils.IsDevelopmentBuild} movementPrediction={GameSettings.MovementPrediction} role={(isHost ? "host" : "client")}.");

		int readinessLogCounter = 0;
		bool ready = await WaitUntilAsync(() =>
		{
			GameHost? host = GameContext.Host;
			int appliedSequence = GameContext.NetworkService?.LastAppliedSnapshotSequenceForDiagnostics ?? -1;
			bool hasBaseline = GameContext.World != null && GameContext.World.IsAlive(GameContext.Simulation.WorldEntity) && GameContext.World.Has<NetworkState>(GameContext.Simulation.WorldEntity)
				&& GameContext.World.Get<NetworkState>(GameContext.Simulation.WorldEntity).HasReceivedInitialBaseline;
			// A peer is playing the match once the authority has described a world to it and it has applied it. How many
			// units that world holds is the map's business, and a map may give a player nothing at all until it drafts.
			bool isReady = host != null && appliedSequence >= 0 && hasBaseline;
			readinessLogCounter++;
			if (!isReady && readinessLogCounter % 50 == 0)
			{
				GD.Print($"[MP_HARNESS] Readiness wait ({(isHost ? "host" : "client")}): elapsed={host?.GameElapsedTime ?? 0f:F2} units={host?.AllUnits.Count ?? 0} appliedSequence={appliedSequence} baseline={hasBaseline} peerPresent={Multiplayer.MultiplayerPeer != null}");
			}

			return isReady;
		}, 120f);

		if (!ready)
		{
			GD.PrintErr("[MP_HARNESS] Game simulation did not become ready in time.");
			return false;
		}

		MultiplayerSyncDiagnostics.Start(_outputDirectory, isHost);
		PredictionDiagnostics.Start(_outputDirectory, isHost, !isHost);
		PrintReplicatedCodecTable(isHost);
		TryWriteReadyFile();
		GameHost currentHost = GameContext.Host!;
		GD.Print($"[MP_HARNESS] {(isHost ? "Host" : "Client")} simulation ready at game time {currentHost.GameElapsedTime:F2}s with {currentHost.AllUnits.Count} units.");
		GD.Print($"[MP_HARNESS] Active workspace '{MapWorkspaceService.GetActiveWorkspacePath()}' resolved from map directory '{GameContext.MapStorageService?.CurrentMapDirectory}'.");
		return true;
	}

	private static void PrintReplicatedCodecTable(bool isHost)
	{
		foreach (var codec in Realm.Ecs.Replication.ReplicatedComponents.All)
		{
			GD.Print($"[CODECS] role={(isHost ? "host" : "client")} id={codec.ComponentTypeId} name={codec.ComponentName} size={codec.SizeInBytes} scope={codec.Scope}");
		}

		GD.Print($"[CODECS] role={(isHost ? "host" : "client")} registryCount={Realm.Ecs.ComponentTypeRegistry.Count} scriptPlayersId={Realm.Ecs.ComponentType<ScriptPlayersState>.Id}");
	}

	private int ResolveAiPlayerCount()
	{
		return Math.Max(0, PlayableSlotCount - Math.Max(2, _peerCount));
	}

	private static int CountConstructionSites(GameHost gameHost)
	{
		int count = 0;
		foreach (var unit in gameHost.AllUnits)
		{
			if (GameContext.World != null && GameContext.World.Has<ConstructionState>(unit.Entity)) count++;
		}

		return count;
	}

	private void IssueClientBuildRequest(GameHost gameHost)
	{
		string buildId = _clientBuildId;
		if (buildId == "auto")
		{
			buildId = string.Empty;
			foreach (var key in GameHost.BuildingRegistry.Keys)
			{
				string candidate = key.ToString();
				if (string.IsNullOrEmpty(candidate)) continue;
				buildId = candidate;
				break;
			}
		}
		if (string.IsNullOrEmpty(buildId))
		{
			GD.PrintErr("[MP_HARNESS] No building id is available for the client build request.");
			return;
		}

		int localPlayerIndex = GameContext.PerspectiveFilter.ControlledPlayerIndex;
		Unit3D worker = gameHost.AllUnits.Find(unit => !unit.IsBuilding && unit.Player == localPlayerIndex && GameHost.UnitRegistry.TryGetValue(unit.UnitId, out var meta) && meta.BuildOptions is { Length: > 0 });
		worker ??= gameHost.AllUnits.Find(unit => !unit.IsBuilding && unit.Player == localPlayerIndex);
		if (worker == null)
		{
			GD.PrintErr($"[MP_HARNESS] No controllable worker was found for player index {localPlayerIndex}.");
			return;
		}

		gameHost.SelectedUnits.Clear();
		gameHost.SelectedUnits.Add(worker);
		var buildPosition = worker.GlobalPosition + new Vector3(4f, 0f, 4f);
		gameHost.PlaceBuildingAt(buildId, buildPosition);
		GD.Print($"[MP_HARNESS] Client requested build '{buildId}' with worker '{worker.UnitId}' at {buildPosition}.");
	}

	public override void _PhysicsProcess(double delta)
	{
		// The session has to be read before a match scene exists too, because the seat a client is given is what decides
		// when it may load the map. Once the match runs, it pumps the same queue and this call finds nothing to do.
		_session?.Pump();
		RecordClientPeerId(Multiplayer.GetUniqueId());
		if (!_scenarioEnabled) return;
		AimClientCameraAtNearestRemoteGroup(delta);
		RunPredictionScenarioTick(delta);
	}

	/// <summary>
	/// Appends a peer id the client process is connected with. A client that loses the host while the map loads rejoins
	/// with a new id, and both ids belong to this process; keeping only the first one made every baseline the second
	/// connection received look like one the host never sent to this peer.
	/// </summary>
	private void RecordClientPeerId(int peerId)
	{
		if (_role != "client" || peerId <= 0 || peerId == _recordedClientPeerId) return;

		_recordedClientPeerId = peerId;
		try
		{
			File.AppendAllText(Path.Combine(_outputDirectory, "client_peer_id.txt"), peerId.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\n");
		}
		catch (IOException)
		{
		}
	}

	/// <summary>
	/// Waits until every client process has reported that its simulation is running, and returns the host's game time at
	/// that moment. The match window is measured from here so a client that was still loading the map is not handed a
	/// stream that has already finished.
	/// </summary>
	private async Task<double> WaitForClientsToStartAsync(GameHost gameHost)
	{
		int expectedClients = Math.Max(1, _peerCount - 1);
		float timeoutSeconds = 60f + 90f * expectedClients;
		ulong deadline = Time.GetTicksMsec() + (ulong)(timeoutSeconds * 1000f);
		while (Time.GetTicksMsec() < deadline)
		{
			int readyClients = 0;
			for (int peerIndex = 1; peerIndex <= expectedClients; peerIndex++)
			{
				string clientDirectory = peerIndex == 1 ? _outputDirectory : Path.Combine(_outputDirectory, $"client{peerIndex}");
				if (File.Exists(Path.Combine(clientDirectory, "client_ready.txt"))) readyClients++;
			}

			if (readyClients >= expectedClients)
		{
				GD.Print($"[MP_HARNESS] All {expectedClients} client(s) report a running simulation at host game time {gameHost.GameElapsedTime:F2}s; the match window starts here.");
				return gameHost.GameElapsedTime;
			}

			await ToSignal(GetTree().CreateTimer(0.5f), SceneTreeTimer.SignalName.Timeout);
		}

		GD.PrintErr($"[MP_HARNESS] Only some clients reported a running simulation within {timeoutSeconds:F0}s; the match window starts now and later clients will record a short stream.");
		return gameHost.GameElapsedTime;
	}

	private async Task<bool> WaitUntilAsync(Func<bool> condition, float timeoutSeconds)
	{
		ulong deadline = Time.GetTicksMsec() + (ulong)(timeoutSeconds * 1000f);
		while (Time.GetTicksMsec() < deadline)
		{
			try
			{
				if (condition()) return true;
			}
			catch (Exception exception)
			{
				GD.PrintErr($"[MP_HARNESS] Condition evaluation failed: {exception.Message}");
			}

			await ToSignal(GetTree().CreateTimer(0.1f), SceneTreeTimer.SignalName.Timeout);
		}

		return false;
	}

	private void WriteFinalWorldSnapshot(string fileName)
	{
		try
		{
			if (GameContext.World == null) return;
			byte[] snapshot = GameContext.World.ExportBinarySnapshot();
			File.WriteAllBytes(Path.Combine(_outputDirectory, fileName), snapshot);
		}
		catch (Exception exception)
		{
			GD.PrintErr($"[MP_HARNESS] Failed to export final world snapshot: {exception.Message}");
		}
	}

	private void TryWriteReadyFile()
	{
		if (string.IsNullOrEmpty(_readyFile)) return;
		File.WriteAllText(_readyFile, DateTime.UtcNow.ToString("O"));
	}

	private void TryWriteDoneFile()
	{
		if (_role != "host") return;
		if (string.IsNullOrEmpty(_doneFile)) return;
		try
		{
			File.WriteAllText(_doneFile, _finalSequence.ToString(System.Globalization.CultureInfo.InvariantCulture));
		}
		catch (Exception exception)
		{
			GD.PrintErr($"[MP_HARNESS] Failed to write completion marker: {exception.Message}");
		}
	}

	private void TryWriteSequenceFile(int sequence)
	{
		if (string.IsNullOrEmpty(_sequenceFile)) return;
		try
		{
			File.WriteAllText(_sequenceFile, sequence.ToString(System.Globalization.CultureInfo.InvariantCulture));
		}
		catch (Exception exception)
		{
			GD.PrintErr($"[MP_HARNESS] Failed to write sequence marker: {exception.Message}");
		}
	}

	/// <summary>
	/// The newest snapshot this process has applied. Every process in a match is a client now, so this reads what it
	/// applied rather than the counter the process that used to simulate the world published.
	/// </summary>
	private static int ResolveSnapshotSequence() => GameContext.NetworkService?.LastAppliedSnapshotSequenceForDiagnostics ?? -1;

	private static void SetPrivateProperty(object target, string propertyName, object value)
	{
		PropertyInfo? property = target.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
		if (property == null) throw new InvalidOperationException($"Property {propertyName} was not found on {target.GetType().Name}.");
		property.SetValue(target, value);
	}
}
