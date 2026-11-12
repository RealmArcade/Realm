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
using Realm.Shared.Diagnostics;
using Realm.Shared.Maps;

namespace Realm.Client.Testing;


public partial class MultiplayerSimulationHarness : Node
{
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

		ClientServerSession session = await StartAuthorityAsync();
		GameLog.LogToConsole($"[MP_HARNESS] The authority is listening on port {session.Port}; this process plays as one of its clients.");
		if (!string.IsNullOrEmpty(_listeningFile)) File.WriteAllText(_listeningFile, session.Port.ToString(System.Globalization.CultureInfo.InvariantCulture));

		int expectedClientCount = Math.Max(0, _peerCount - 1);
		bool clientsSeated = await WaitUntilAsync(() => CountSeatedPeers(session) >= expectedClientCount + 1, 240f);
		if (!clientsSeated)
		{
			throw new InvalidOperationException($"Timed out waiting for {expectedClientCount} client peer(s) to be seated; the authority recorded {CountSeatedPeers(session)} seat(s) including this process.");
		}

		if (!await StartSimulationAsync(lobby, true)) return 1;

		GameHost gameHost = GameContext.Host ?? throw new InvalidOperationException("GameHost instance is unavailable.");
		await RunSplitScenarioAsync(lobby, gameHost);

		if (!string.IsNullOrEmpty(_clientBuildId)) IssueClientBuildRequest(gameHost);

		double matchStartGameTime = await WaitForClientsToStartAsync(gameHost);
		if (_unlockTiles) await IssueUnlockTilesAsync();
		bool finished = false;
		ulong simulationDeadline = Time.GetTicksMsec() + (ulong)((_durationSeconds * 4f + 120f) * 1000f);
		while (Time.GetTicksMsec() < simulationDeadline)
		{
			if (gameHost.GameElapsedTime >= matchStartGameTime + _durationSeconds)
			{
				finished = true;
				break;
			}

			GameLog.LogToConsole($"[MP_HARNESS] Host game time {gameHost.GameElapsedTime:F2}s / {_durationSeconds:F0}s, units {gameHost.AllUnits.Count}, construction sites {CountConstructionSites(gameHost)}, snapshots {ResolveSnapshotSequence()}.");
			await ToSignal(GetTree().CreateTimer(1.0f), SceneTreeTimer.SignalName.Timeout);
		}

		if (!finished) throw new InvalidOperationException($"Host simulation did not reach {_durationSeconds}s of game time after every client started.");

		if (_drainMilliseconds > 0)
		{
			GameLog.LogToConsole($"[MP_HARNESS] Host draining for {_drainMilliseconds} ms so delayed clients can catch up.");
			await ToSignal(GetTree().CreateTimer(_drainMilliseconds / 1000f), SceneTreeTimer.SignalName.Timeout);
		}

		// The authority writes what it measured as the match ends, so it has to be parked before this process exits: a
		// child authority that is killed with its parent takes the tick and publish numbers of the whole run with it.
		await PauseAuthorityAndWaitForItsReportAsync();

		_hardeningFailureCount = MultiplayerHardeningChecks.Run(_outputDirectory, true, _session?.LogPath ?? string.Empty);

		_finalSequence = ResolveSnapshotSequence();
		MultiplayerBandwidthMeter.WriteReport(_outputDirectory, gameHost.GameElapsedTime);
		WriteSimulatedLinkStats();
		GameLog.LogToConsole($"[MP_HARNESS] Host finished at game time {gameHost.GameElapsedTime:F2}s with {gameHost.AllUnits.Count} units and snapshot sequence {_finalSequence}.");
		MultiplayerSyncDiagnostics.Stop();
		LagProbe.WriteReport();
		WriteFinalWorldSnapshot("ECS_host_final_full.bin");
		WriteWorldSummary("world_summary_host.json", "host");
		return 0;
	}

	private async Task<ClientServerSession> StartAuthorityAsync()
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
			Cheats: _cheatsEnabled,
			OpenToOthers: true,
			Roster: BuildHarnessRoster(),
			AuthorityRecordDirectory: _outputDirectory,
			LoopbackOnly: true);

		GameLog.LogToConsole($"[MP_HARNESS] This screen's link to the authority is simulated at {SimulatedLink.FromEnvironment().Describe()}.");

		MatchStartResult started = await session.TryStartAsync(start, _port);
		if (!started.Started) throw new InvalidOperationException($"The authority could not be started on port {_port}: {started.Failure}");

		GameContext.AttachServerSession(session);
		_session = session;
		return session;
	}

	private List<MatchRosterSeat> BuildHarnessRoster()
	{
		var roster = new List<MatchRosterSeat>(_peerCount + _botCount);
		for (int slot = 1; slot <= _peerCount; slot++)
		{
			string name = slot == 1 ? HostPlayerName : $"HarnessPeer{slot}";
			roster.Add(new MatchRosterSeat(slot, 0, name, slot, $"Team {slot}", false));
		}

		for (int index = 0; index < _botCount; index++)
		{
			int slot = _peerCount + index + 1;
			roster.Add(new MatchRosterSeat(slot, 0, $"Bot {slot}", slot, $"Team {slot}", true));
		}

		return roster;
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

	private static int CountConstructionSites(GameHost gameHost)
	{
		int count = 0;
		foreach (var unit in gameHost.AllUnits)
		{
			if (GameContext.World != null && GameContext.World.Has<ConstructionState>(unit.Entity)) count++;
		}

		return count;
	}

	private async Task<double> WaitForClientsToStartAsync(GameHost gameHost)
	{
		int expectedClients = Math.Max(0, _peerCount - 1);
		if (expectedClients == 0)
		{
			GameLog.LogToConsole($"[MP_HARNESS] This screen is the only client; the match window starts at its game time {gameHost.GameElapsedTime:F2}s.");
			return gameHost.GameElapsedTime;
		}

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
				GameLog.LogToConsole($"[MP_HARNESS] All {expectedClients} client(s) report a running simulation at host game time {gameHost.GameElapsedTime:F2}s; the match window starts here.");
				return gameHost.GameElapsedTime;
			}

			await ToSignal(GetTree().CreateTimer(0.5f), SceneTreeTimer.SignalName.Timeout);
		}

		GameLog.LogError($"[MP_HARNESS] Only some clients reported a running simulation within {timeoutSeconds:F0}s; the match window starts now and later clients will record a short stream.");
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
				GameLog.LogError($"[MP_HARNESS] Condition evaluation failed: {exception.Message}");
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
			GameLog.LogError($"[MP_HARNESS] Failed to export final world snapshot: {exception.Message}");
		}
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
			GameLog.LogError($"[MP_HARNESS] Failed to write completion marker: {exception.Message}");
		}
	}

	private static int ResolveSnapshotSequence() => GameContext.NetworkService?.LastAppliedSnapshotSequenceForDiagnostics ?? -1;

}
