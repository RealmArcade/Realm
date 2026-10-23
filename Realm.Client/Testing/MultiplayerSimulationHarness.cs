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
	private const int AiPlayerCount = 10;
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
	private int _drainMilliseconds = 0;
	private int _finalSequence = -1;
	private float _durationSeconds = 15f;

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
			exitCode = _role == "host" ? await RunHostAsync() : await RunClientAsync();
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
				case "--drain-ms":
					_drainMilliseconds = int.Parse(value);
					break;
				case "--duration":
					_durationSeconds = float.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
					break;
			}
		}

		if (string.IsNullOrEmpty(_outputDirectory)) throw new InvalidOperationException("Missing --output argument.");
	}

	private async Task<int> RunHostAsync()
	{
		LobbyManager lobby = LobbyManager.Instance ?? throw new InvalidOperationException("LobbyManager autoload is unavailable.");

		lobby.IsSinglePlayer = false;
		lobby.IsHost = true;
		lobby.IsGameStarted = false;
		SetPrivateProperty(lobby, "MaxPlayers", PlayableSlotCount);
		SetPrivateProperty(lobby, "ENetPort", _port);
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

		for (int slot = 2; slot <= 1 + AiPlayerCount; slot++)
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

		MethodInfo? createServerMethod = typeof(LobbyManager).GetMethod("TryCreateENetServerAsync", BindingFlags.NonPublic | BindingFlags.Instance);
		if (createServerMethod == null) throw new InvalidOperationException("LobbyManager.TryCreateENetServerAsync could not be resolved.");
		if (createServerMethod.Invoke(lobby, null) is not Task<bool> createServerTask) throw new InvalidOperationException("LobbyManager.TryCreateENetServerAsync did not return a task.");
		if (!await createServerTask) throw new InvalidOperationException($"Failed to host ENet server on port {_port}.");

		GD.Print($"[MP_HARNESS] Host listening on port {_port}. Waiting for the client peer.");
		if (!string.IsNullOrEmpty(_listeningFile)) File.WriteAllText(_listeningFile, DateTime.UtcNow.ToString("O"));

		bool clientConnected = await WaitUntilAsync(
			() => lobby.PlayerList.Exists(player => player.PeerId > 1 && !player.IsDisconnected),
			60f);
		if (!clientConnected) throw new InvalidOperationException("Timed out waiting for the client peer to join the host lobby.");
		if (!await StartSimulationAsync(lobby, true)) return 1;

		GameHost gameHost = GameHost.Instance ?? throw new InvalidOperationException("GameHost instance is unavailable.");
		bool finished = false;
		ulong simulationDeadline = Time.GetTicksMsec() + (ulong)((_durationSeconds * 4f + 120f) * 1000f);
		while (Time.GetTicksMsec() < simulationDeadline)
		{
			if (gameHost.GameElapsedTime >= _durationSeconds)
			{
				finished = true;
				break;
			}

			GD.Print($"[MP_HARNESS] Host game time {gameHost.GameElapsedTime:F2}s / {_durationSeconds:F0}s, units {gameHost.AllUnits.Count}, construction sites {CountConstructionSites(gameHost)}, snapshots {ResolveSnapshotSequence()}.");
			await ToSignal(GetTree().CreateTimer(1.0f), SceneTreeTimer.SignalName.Timeout);
		}

		if (!finished) throw new InvalidOperationException($"Host simulation did not reach {_durationSeconds}s of game time.");

		if (_drainMilliseconds > 0)
		{
			GD.Print($"[MP_HARNESS] Host draining for {_drainMilliseconds} ms so delayed clients can catch up.");
			await ToSignal(GetTree().CreateTimer(_drainMilliseconds / 1000f), SceneTreeTimer.SignalName.Timeout);
		}

		_finalSequence = ResolveSnapshotSequence();
		GD.Print($"[MP_HARNESS] Host finished at game time {gameHost.GameElapsedTime:F2}s with {gameHost.AllUnits.Count} units and snapshot sequence {_finalSequence}.");
		MultiplayerSyncDiagnostics.Stop();
		WriteFinalWorldSnapshot("ECS_host_final_full.bin");
		return 0;
	}

	private async Task<int> RunClientAsync()
	{
		LobbyManager lobby = LobbyManager.Instance ?? throw new InvalidOperationException("LobbyManager autoload is unavailable.");

		lobby.IsSinglePlayer = false;
		SetPrivateProperty(lobby, "MaxPlayers", PlayableSlotCount);
		SetPrivateProperty(lobby, "ENetPort", _port);

		bool joined = await lobby.JoinByIpAsync(_hostAddress, _port);
		if (!joined) throw new InvalidOperationException($"Failed to connect to host at {_hostAddress}:{_port}.");

		int localPeerId = Multiplayer.GetUniqueId();
		GD.Print($"[MP_HARNESS] Client connected with peer id {localPeerId}. Waiting for lobby sync.");

		bool playerSynced = await WaitUntilAsync(
			() => lobby.PlayerList.Exists(player => player.PeerId == localPeerId),
			30f);
		if (!playerSynced) throw new InvalidOperationException("Timed out waiting for the host to sync the lobby player list.");
		if (!await StartSimulationAsync(lobby, false)) return 1;

		GD.Print("[MP_HARNESS] Client simulation started. Recording until the host signals completion.");
		if (!string.IsNullOrEmpty(_clientBuildId))
		{
			await ToSignal(GetTree().CreateTimer(2.0f), SceneTreeTimer.SignalName.Timeout);
			IssueClientBuildRequest(GameHost.Instance);
		}
		bool hostFinished = await WaitUntilAsync(() => File.Exists(_doneFile), _durationSeconds * 4f + 120f + (_drainMilliseconds / 1000f));
		if (!hostFinished) GD.PrintErr("[MP_HARNESS] Host completion marker was not observed before the timeout.");

		if (_drainMilliseconds > 0)
		{
			await ToSignal(GetTree().CreateTimer(_drainMilliseconds / 1000f), SceneTreeTimer.SignalName.Timeout);
		}

		_finalSequence = GameContext.NetworkService.LastAppliedSnapshotSequenceForDiagnostics;
		GD.Print($"[MP_HARNESS] Client stopped at applied snapshot sequence {_finalSequence} with game time {GameHost.Instance?.GameElapsedTime ?? 0f:F2}s.");
		MultiplayerSyncDiagnostics.Stop();
		TryWriteSequenceFile(_finalSequence);
		WriteFinalWorldSnapshot("ECS_client_final_full.bin");
		return hostFinished ? 0 : 1;
	}

	private async Task<bool> StartSimulationAsync(LobbyManager lobby, bool isHost)
	{
		lobby.ActiveMapName = _mapFolder;
		lobby.IsGameStarted = true;
		GameSettings.RecordReplays = false;
		GameHost.PendingMapScriptPath = isHost && !string.IsNullOrEmpty(_wasmPath) ? _wasmPath : null;

		PackedScene scene = GD.Load<PackedScene>("res://GameHost.tscn");
		Node gameHost = scene.Instantiate();
		gameHost.Name = "GameHost";
		AddChild(gameHost);

		bool ready = await WaitUntilAsync(() =>
		{
			GameHost? host = GameHost.Instance;
			return host != null && Math.Abs(host.GameElapsedTime) >= 0.01f && host.AllUnits.Count > 0;
		}, 120f);

		if (!ready)
		{
			GD.PrintErr("[MP_HARNESS] Game simulation did not become ready in time.");
			return false;
		}

		MultiplayerSyncDiagnostics.Start(_outputDirectory, isHost);
		TryWriteReadyFile();
		GameHost currentHost = GameHost.Instance!;
		GD.Print($"[MP_HARNESS] {(isHost ? "Host" : "Client")} simulation ready at game time {currentHost.GameElapsedTime:F2}s with {currentHost.AllUnits.Count} units.");
		GD.Print($"[MP_HARNESS] Active workspace '{MapWorkspaceService.GetActiveWorkspacePath()}' resolved from map directory '{GameContext.MapStorageService?.CurrentMapDirectory}'.");
		return true;
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
