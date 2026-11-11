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
	private ClientServerSession JoinAuthority()
	{
		ClientNetworkChannel channel = GetNodeOrNull<ClientNetworkChannel>("/root/RealmNetwork") ?? throw new InvalidOperationException("This build has no channel to an authority.");
		GameContext.ServerSession?.Dispose();

		var session = new ClientServerSession(channel);
		if (!session.TryJoin(_hostAddress, _port, LocalizationManager.GetCurrentLanguageCode(), _mapFolder, out string failure))
		{
			throw new InvalidOperationException($"The authority at {_hostAddress}:{_port} could not be reached: {failure}");
		}

		GameContext.AttachServerSession(session);
		_session = session;
		return session;
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
		GameLog.LogToConsole($"[MP_HARNESS] Client peer index {_peerIndex} was seated as player {session.LocalPlayerSlot} with peer id {localPeerId}.");

		if (!string.IsNullOrEmpty(_waitForPeerReadyFile))
		{
			GameLog.LogToConsole($"[MP_HARNESS] Client peer index {_peerIndex} stays in the lobby until {_waitForPeerReadyFile} exists, so only two processes load the map at once.");
			bool previousPeerReady = await WaitUntilAsync(() => File.Exists(_waitForPeerReadyFile), 240f);
			if (!previousPeerReady) throw new InvalidOperationException($"Client peer index {_peerIndex} waited too long for the previous peer to become ready.");
		}

		MultiplayerBandwidthMeter.Reset();
		MultiplayerBandwidthMeter.IsRecording = true;
		if (!await StartSimulationAsync(lobby, false)) return 1;


		GameLog.LogToConsole("[MP_HARNESS] Client simulation started. Recording until the host signals completion.");
		if (!string.IsNullOrEmpty(_clientBuildId) && GameContext.Host is GameHost host)
		{
			await ToSignal(GetTree().CreateTimer(2.0f), SceneTreeTimer.SignalName.Timeout);
			IssueClientBuildRequest(host);
		}
		if (_dropAtSeconds > 0f) _ = RunConnectionLossScenarioAsync(session);
		if (_pauseAtSeconds > 0f) _ = RunPauseScenarioAsync(session);
		if (_discardAtSeconds > 0f) _ = RunSnapshotDiscardScenarioAsync();

		bool hostFinished = await WaitUntilAsync(() => File.Exists(_doneFile), _durationSeconds * 4f + 240f + (_drainMilliseconds / 1000f));
		if (!hostFinished) GameLog.LogError("[MP_HARNESS] Host completion marker was not observed before the timeout.");

		if (_drainMilliseconds > 0)
		{
			await ToSignal(GetTree().CreateTimer(_drainMilliseconds / 1000f), SceneTreeTimer.SignalName.Timeout);
		}

		_finalSequence = GameContext.NetworkService.LastAppliedSnapshotSequenceForDiagnostics;
		GameLog.LogToConsole($"[MP_HARNESS] Client stopped at applied snapshot sequence {_finalSequence} with game time {GameContext.Host?.GameElapsedTime ?? 0f:F2}s.");
		MultiplayerBandwidthMeter.WriteReport(_outputDirectory, GameContext.Host?.GameElapsedTime ?? 0f);
		MultiplayerSyncDiagnostics.Stop();
		LagProbe.WriteReport();
		TryWriteSequenceFile(_finalSequence);
		WriteFinalWorldSnapshot("ECS_client_final_full.bin");
		WriteWorldSummary($"world_summary_peer{_peerIndex}.json", "client");
		return hostFinished ? 0 : 1;
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
			GameLog.LogError("[MP_HARNESS] No building id is available for the client build request.");
			return;
		}

		int localPlayerIndex = GameContext.PerspectiveFilter.ControlledPlayerIndex;
		Unit3D? worker = gameHost.AllUnits.Find(unit => !unit.IsBuilding && unit.Player == localPlayerIndex && GameHost.UnitRegistry.TryGetValue(unit.UnitId, out var meta) && meta.BuildOptions is { Length: > 0 });
		worker ??= gameHost.AllUnits.Find(unit => !unit.IsBuilding && unit.Player == localPlayerIndex);
		if (worker == null)
		{
			GameLog.LogError($"[MP_HARNESS] No controllable worker was found for player index {localPlayerIndex}.");
			return;
		}

		gameHost.SelectedUnits.Clear();
		gameHost.SelectedUnits.Add(worker);
		var buildPosition = worker.GlobalPosition + new Vector3(4f, 0f, 4f);
		gameHost.PlaceBuildingAt(buildId, buildPosition);
		GameLog.LogToConsole($"[MP_HARNESS] Client requested build '{buildId}' with worker '{worker.UnitId}' at {buildPosition}.");
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
			GameLog.LogError($"[MP_HARNESS] Failed to write sequence marker: {exception.Message}");
		}
	}

}
