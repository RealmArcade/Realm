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
	private const string ClientPlayerName = "HarnessClient";

	private string _role = "host";
	private string _hostAddress = "127.0.0.1";
	private int _port = 8999;
	private string _mapFolder = string.Empty;
	private string _wasmPath = string.Empty;
	private string _outputDirectory = string.Empty;
	private string _listeningFile = string.Empty;
	private string _readyFile = string.Empty;
	private string _doneFile = string.Empty;
	private string _sequenceFile = string.Empty;
	private string _clientBuildId = string.Empty;
	private string _clientTeam = string.Empty;
	private int _drainMilliseconds = 0;
	private int _finalSequence = -1;
	private float _durationSeconds = 15f;
	private bool _predictionEnabled;
	private bool _scenarioEnabled;
	private string _networkProfile = "Latency";
	private bool _loopback;
	private int _peerCount = 2;
	private int _peerIndex = 1;
	private float _dropAtSeconds;
	private bool _probesEnabled;
	private string _waitForPeerReadyFile = string.Empty;
	private int _hardeningFailureCount;
	private int _recordedClientPeerId = -1;
	private PlayerInfo? _hostSlotBaseline;

	private async Task ForgeLobbyIdentityAsync(LobbyManager lobby, int localPeerId)
	{
		await ToSignal(GetTree().CreateTimer(4.0f), SceneTreeTimer.SignalName.Timeout);

		try
		{
			RpcId(1, nameof(LobbyManager.UpdatePlayerSlot), 1, "FORGED", "FORGED", new Color(1f, 0f, 0f), "ForgedSlot");
			RpcId(1, nameof(LobbyManager.UpdateDiagnostics), 1, 999f, 999f, 999f, 999f, 99f, 99);
			lobby.SendChatMessage("Host", "forged chat from the peer harness");

			string markerPath = Path.Combine(_outputDirectory, "forged_rpc_sent.txt");
			File.WriteAllText(markerPath, $"peer {localPeerId} sent forged slot, diagnostics and chat for peer 1 at {DateTime.UtcNow:O}");
			GD.Print($"[MP_HARNESS] Client peer {localPeerId} sent forged lobby RPCs targeting peer 1.");
		}
		catch (Exception exception)
		{
			GD.PrintErr($"[MP_HARNESS] Failed to send forged lobby RPCs: {exception.Message}");
		}
	}

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
				"loopback" => await RunLoopbackAsync(),
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
				case "--wasm":
					_wasmPath = value;
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
				case "--client-team":
					_clientTeam = value;
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
				case "--loopback":
					_loopback = value == "1";
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
		lobby.IsGameStarted = false;
		SetPrivateProperty(lobby, "MaxPlayers", PlayableSlotCount);
		SetPrivateProperty(lobby, "ENetPort", _port);
		SetPrivateProperty(lobby, "ActiveLobbyId", $"direct-harness-{Guid.NewGuid():N}");

		lobby.PlayerList.Clear();
		var hostSlot = new PlayerInfo
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
		};
		lobby.PlayerList.Add(hostSlot);
		_hostSlotBaseline = new PlayerInfo
		{
			PeerId = hostSlot.PeerId,
			Slot = hostSlot.Slot,
			Name = hostSlot.Name,
			Faction = hostSlot.Faction,
			Team = hostSlot.Team,
			Color = hostSlot.Color,
			Latency = hostSlot.Latency
		};

		for (int slot = 2; slot <= ResolveAiPlayerCount() + 1; slot++)
		{
			lobby.PlayerList.Add(new PlayerInfo
			{
				PeerId = -(slot + 100),
				Slot = slot,
				Name = $"HarnessAI{slot - 1}",
				Faction = "AI",
				Team = $"Team {slot}",
				Color = PlayerColorConfig.GetColor(slot),
				IsHost = false,
				Latency = "0 ms",
				Jitter = "0 ms",
				PacketLoss = "0%",
				BinaryVersion = RealmVersion.GameBinaryVersion
			});
		}

		MultiplayerBandwidthMeter.Reset();
		MultiplayerBandwidthMeter.IsRecording = true;
		MethodInfo? createServerMethod = typeof(LobbyManager).GetMethod("TryCreateENetServerAsync", BindingFlags.NonPublic | BindingFlags.Instance);
		if (createServerMethod == null) throw new InvalidOperationException("LobbyManager.TryCreateENetServerAsync could not be resolved.");
		if (createServerMethod.Invoke(lobby, null) is not Task<bool> createServerTask) throw new InvalidOperationException("LobbyManager.TryCreateENetServerAsync did not return a task.");
		if (!await createServerTask) throw new InvalidOperationException($"Failed to host ENet server on port {_port}.");

		int expectedClientCount = Math.Max(1, _peerCount - 1);
		GD.Print($"[MP_HARNESS] Host listening on port {_port}. Waiting for {expectedClientCount} client peer(s).");
		if (!string.IsNullOrEmpty(_listeningFile)) File.WriteAllText(_listeningFile, DateTime.UtcNow.ToString("O"));

		bool clientConnected = await WaitUntilAsync(
			() => lobby.PlayerList.FindAll(player => player.PeerId > 1 && !player.IsDisconnected).Count >= expectedClientCount,
			60f);
		if (!clientConnected) throw new InvalidOperationException($"Timed out waiting for {expectedClientCount} client peer(s) to join the host lobby.");
		await ToSignal(GetTree().CreateTimer(1.0f), SceneTreeTimer.SignalName.Timeout);
		int joinedClientCount = lobby.PlayerList.FindAll(player => player.PeerId > 1 && !player.IsDisconnected).Count;
		GD.Print($"[MP_HARNESS] Starting the match with {joinedClientCount + 1} peers: {string.Join(", ", lobby.PlayerList.ConvertAll(player => $"{player.Name}#{player.PeerId}"))}.");
		if (!await StartSimulationAsync(lobby, true)) return 1;

		if (_probesEnabled) _ = RunCombatProbeAsync(true);

		GameHost gameHost = GameContext.Host ?? throw new InvalidOperationException("GameHost instance is unavailable.");

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

		LobbyManager hardeningLobby = GameContext.Lobby ?? throw new InvalidOperationException("LobbyManager autoload is unavailable.");
		_hardeningFailureCount = MultiplayerHardeningChecks.Run(_outputDirectory, true, hardeningLobby, _hostSlotBaseline);

		_finalSequence = ResolveSnapshotSequence();
		MultiplayerBandwidthMeter.BuildReport(_outputDirectory, gameHost.GameElapsedTime);
		GD.Print($"[MP_HARNESS] Host finished at game time {gameHost.GameElapsedTime:F2}s with {gameHost.AllUnits.Count} units and snapshot sequence {_finalSequence}.");
		MultiplayerSyncDiagnostics.Stop();
		PredictionDiagnostics.Stop();
		WriteFinalWorldSnapshot("ECS_host_final_full.bin");
		return 0;
	}

	private async Task<int> RunLoopbackAsync()
	{
		LobbyManager lobby = GameContext.Lobby ?? throw new InvalidOperationException("LobbyManager autoload is unavailable.");

		lobby.IsSinglePlayer = true;
		lobby.IsHost = true;
		lobby.IsGameStarted = false;
		SetPrivateProperty(lobby, "MaxPlayers", PlayableSlotCount);

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

		for (int slot = 2; slot <= ResolveAiPlayerCount() + 1; slot++)
		{
			lobby.PlayerList.Add(new PlayerInfo
			{
				PeerId = -(slot + 100),
				Slot = slot,
				Name = $"HarnessAI{slot - 1}",
				Faction = "AI",
				Team = $"Team {slot}",
				Color = PlayerColorConfig.GetColor(slot),
				IsHost = false,
				Latency = "0 ms",
				Jitter = "0 ms",
				PacketLoss = "0%",
				BinaryVersion = RealmVersion.GameBinaryVersion
			});
		}

		MultiplayerBandwidthMeter.Reset();
		MultiplayerBandwidthMeter.IsRecording = true;
		Multiplayer.MultiplayerPeer = new OfflineMultiplayerPeer();
		GD.Print("[MP_HARNESS] Loopback role: hosting and playing in one process over the in-memory command transport.");
		if (!await StartSimulationAsync(lobby, true)) return 1;

		GameHost gameHost = GameContext.Host ?? throw new InvalidOperationException("GameHost instance is unavailable.");
		ulong simulationDeadline = Time.GetTicksMsec() + (ulong)((_durationSeconds * 4f + 120f) * 1000f);
		while (Time.GetTicksMsec() < simulationDeadline)
		{
			if (gameHost.GameElapsedTime >= _durationSeconds) break;
			GD.Print($"[MP_HARNESS] Loopback game time {gameHost.GameElapsedTime:F2}s / {_durationSeconds:F0}s, units {gameHost.AllUnits.Count}, predicted {GameContext.MovementPredictionService?.TrackedUnitCount ?? 0}.");
			await ToSignal(GetTree().CreateTimer(1.0f), SceneTreeTimer.SignalName.Timeout);
		}

		_finalSequence = ResolveSnapshotSequence();
		bool finished = gameHost.GameElapsedTime >= _durationSeconds;
		GD.Print($"[MP_HARNESS] Loopback finished={finished} at game time {gameHost.GameElapsedTime:F2}s with {gameHost.AllUnits.Count} units and {GameContext.MovementPredictionService?.TrackedUnitCount ?? 0} predicted units.");
		MultiplayerSyncDiagnostics.Stop();
		PredictionDiagnostics.Stop();
		WriteFinalWorldSnapshot("ECS_loopback_final_full.bin");
		return finished ? 0 : 1;
	}

	private async Task<int> RunClientAsync()
	{
		LobbyManager lobby = GameContext.Lobby ?? throw new InvalidOperationException("LobbyManager autoload is unavailable.");

		lobby.IsSinglePlayer = false;
		SetPrivateProperty(lobby, "MaxPlayers", PlayableSlotCount);
		SetPrivateProperty(lobby, "ENetPort", _port);

		bool joined = await lobby.JoinByIpAsync(_hostAddress, _port);
		if (!joined) throw new InvalidOperationException($"Failed to connect to host at {_hostAddress}:{_port}.");

		int localPeerId = Multiplayer.GetUniqueId();
		RecordClientPeerId(localPeerId);
		GD.Print($"[MP_HARNESS] Client peer index {_peerIndex} connected with peer id {localPeerId}. Waiting for lobby sync.");

		bool playerSynced = await WaitUntilAsync(
			() => lobby.PlayerList.Exists(player => player.PeerId == localPeerId),
			30f);
		if (!playerSynced) throw new InvalidOperationException("Timed out waiting for the host to sync the lobby player list.");
		if (!string.IsNullOrEmpty(_clientTeam)) await ApplyClientTeamAsync(lobby, localPeerId);

		if (!string.IsNullOrEmpty(_waitForPeerReadyFile))
		{
			GD.Print($"[MP_HARNESS] Client peer index {_peerIndex} stays in the lobby until {_waitForPeerReadyFile} exists, so only two processes load the map at once.");
			bool previousPeerReady = await WaitUntilAsync(() => File.Exists(_waitForPeerReadyFile), 240f);
			if (!previousPeerReady) throw new InvalidOperationException($"Client peer index {_peerIndex} waited too long for the previous peer to become ready.");
		}

		if (!await StartSimulationAsync(lobby, false)) return 1;

		if (_probesEnabled) _ = RunCombatProbeAsync(false);

		GD.Print("[MP_HARNESS] Client simulation started. Recording until the host signals completion.");
		_scenarioTick = 0;
		_scenarioPass = 0;
		if (!string.IsNullOrEmpty(_clientBuildId) && !_scenarioEnabled)
		{
			await ToSignal(GetTree().CreateTimer(2.0f), SceneTreeTimer.SignalName.Timeout);
			IssueClientBuildRequest(GameContext.Host);
		}
		_ = ForgeLobbyIdentityAsync(lobby, localPeerId);
		if (_dropAtSeconds > 0f) _ = RunConnectionLossScenarioAsync(lobby);

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
	/// Drops this client's connection once mid-match and waits for it to rejoin, so the reconnect path is exercised the
	/// way a lost host would exercise it. The outcome is written for the verifier: the sequence the client had applied
	/// when it dropped, the first sequence it applied after rejoining, and whether it rejoined at all.
	/// </summary>
	private async Task RunConnectionLossScenarioAsync(LobbyManager lobby)
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
			lobby.SimulateConnectionLossForHarness();

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

	private async Task ApplyClientTeamAsync(LobbyManager lobby, int localPeerId)
	{
		PlayerInfo? localPlayer = lobby.PlayerList.Find(player => player.PeerId == localPeerId);
		if (localPlayer == null) return;

		lobby.UpdatePlayerSlot(localPeerId, localPlayer.Faction, _clientTeam, localPlayer.Color, localPlayer.Name);
		bool applied = await WaitUntilAsync(
			() => lobby.PlayerList.Exists(player => player.PeerId == localPeerId && player.Team == _clientTeam),
			10f);
		GD.Print($"[MP_HARNESS] Client team request '{_clientTeam}' applied={applied}.");
	}

	private async Task<bool> StartSimulationAsync(LobbyManager lobby, bool isHost)
	{
		lobby.ActiveMapName = _mapFolder;
		lobby.IsGameStarted = true;
		GameSettings.RecordReplays = false;
		GameSettings.MovementPrediction = _predictionEnabled;
		GameHost.PendingMapScriptPath = !string.IsNullOrEmpty(_wasmPath) && (isHost || _predictionEnabled) ? _wasmPath : null;

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
			bool clockReady = !isHost || (host != null && Math.Abs(host.GameElapsedTime) >= 0.01f);
			bool isReady = host != null && clockReady && host.AllUnits.Count > 0;
			readinessLogCounter++;
			if (!isReady && readinessLogCounter % 50 == 0)
			{
				int appliedSequence = GameContext.NetworkService?.LastAppliedSnapshotSequenceForDiagnostics ?? -1;
				bool hasBaseline = GameContext.World != null && GameContext.World.IsAlive(ECSContext.WorldEntity) && GameContext.World.Has<NetworkState>(ECSContext.WorldEntity)
					&& GameContext.World.Get<NetworkState>(ECSContext.WorldEntity).HasReceivedInitialBaseline;
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
		PredictionDiagnostics.Start(_outputDirectory, isHost, !isHost || _loopback);
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
		if (_role != "host" && _role != "loopback") return;
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

	private static int ResolveSnapshotSequence()
	{
		if (GameContext.World == null) return -1;
		Entity worldEntity = ECSContext.WorldEntity;
		if (worldEntity == Entity.Null || !GameContext.World.Has<NetworkState>(worldEntity)) return -1;
		return GameContext.World.Get<NetworkState>(worldEntity).SnapshotSequence;
	}

	private static void SetPrivateProperty(object target, string propertyName, object value)
	{
		PropertyInfo? property = target.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
		if (property == null) throw new InvalidOperationException($"Property {propertyName} was not found on {target.GetType().Name}.");
		property.SetValue(target, value);
	}
}
