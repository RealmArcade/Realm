using System.Buffers;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using MemoryPack;
using Realm.Ecs;
using Realm.Ecs.Replication;
using Realm.Ecs.Services;
using Realm.Shared;
using Realm.Shared.Diagnostics;
using Realm.Shared.Maps;
using Realm.Shared.Network;
using Realm.Shared.Simulation;

namespace Realm.ListenServer;

/// <summary>turning a lobby roster into a running match.</summary>
public partial class ListenServerHost
	{
	private string? StartMatchFromLobby(IReadOnlyList<MatchRosterSeat> roster, IReadOnlyList<(int PeerId, int Slot)> assignments, IReadOnlyDictionary<string, int> sessionSlots, int seed)
	{
		if (_simulation != null) return "This lobby has already started its match.";

		_startClock.Restart();
		_scriptLogSink = _log.Write;
		WasmScriptHost.OnWasmLog += _scriptLogSink;

		LoadedMap? worldOwnedHere = null;
		WasmScriptHost? preparedScriptHost = _lobbyScript?.DetachHost();
		try
		{
			worldOwnedHere = TakePreparedLobbyWorldOrLoad(roster);
			MarkStartStage("map loaded");
			var paths = ServerMatchPaths.From(_arguments);
			int hostPlayerSlot = ResolveLobbyHostPlayerSlot(assignments);
			_simulation = new ServerSimulation(worldOwnedHere, paths, _log, seed, _arguments.Cheats, _arguments.WasmDebug, _arguments.WorkspacePath, roster, hostPlayerSlot, preparedScriptHost, matchOptions: _lobby?.MatchOptions, teamSpawnCoordinates: _lobby?.Roster?.CopyTeamSpawnCoordinates());
			preparedScriptHost = null;
			if (hostPlayerSlot > 0) _log.Write($"player slot {hostPlayerSlot} hosts this match: its seat is the one that administered the lobby");

			worldOwnedHere = null;
			MarkStartStage("simulation built");
			WarmSnapshotFraming();
			_simulation.PeerDropped += DisconnectPeer;
			_log.Write($"map '{paths.MapName}' keeps its saved data under '{_simulation.MapSavedDataDirectory()}'");
			_log.Write(_simulation.Describe());
		}
		catch (Exception exception)
		{
			_log.Write($"the lobby could not start its match: {exception}");
			preparedScriptHost?.Dispose();
			worldOwnedHere?.Dispose();
			_simulation?.Dispose();
			_simulation = null;

			_preparedLobbyWorldAttempted = false;
			if (_scriptLogSink != null)
			{
				WasmScriptHost.OnWasmLog -= _scriptLogSink;
				_scriptLogSink = null;
			}

			return "The match could not start; the authority's log explains why.";
		}

		ResetPauseAllowances();

		_lobbySessionSlots.Clear();
		foreach (var pair in sessionSlots)
		{
			_lobbySessionSlots[pair.Key] = pair.Value;
			_sessionTokenSlots[pair.Key] = pair.Value;
		}

		for (int index = 0; index < assignments.Count; index++)
		{
			(int peerId, int slot) = assignments[index];
			if (!_simulation.SeatPeerAtSlot(peerId, slot))
			{
				_log.Write($"peer {peerId} could not be given lobby seat {slot}; it will not be part of the match");
				continue;
			}

			BeginSeatedPeerBookkeeping(peerId, slot, _simulation.TickCount);
			if (_peerLanguages.TryGetValue(peerId, out string? language)) _simulation.SetPlayerLanguage(slot, language);

			// A lobby connection carries its Steam account from the hello it was admitted with, so the match inherits it: the
			// seat does not say hello again when the lobby hands it over.
			if (_lobby != null && _lobby.TryGetSeat(peerId, out LobbySeat lobbySeat)) _simulation.SetPeerSteamId(peerId, lobbySeat.SteamId);

			var assignment = new PeerRosterAssignment
			{
				PeerId = peerId,
				PlayerSlot = slot,
				Team = _simulation.ReadTeam(slot)
			};

			_transport.Send(peerId, NetworkFrameKind.Roster, MemoryPackSerializer.Serialize(assignment));
			_simulation.RequestBaseline(peerId);
			SendNavMeshObstacles(peerId);
			SendPauseState(peerId);
			SendPauseAllowance(peerId, string.Empty);
		}

		var unseated = new List<int>();
		foreach (int peerId in _connectedPeers)
		{
			if (!_seatedPeers.ContainsKey(peerId)) unseated.Add(peerId);
		}

		for (int index = 0; index < unseated.Count; index++)
		{
			_log.Write($"peer {unseated[index]} was attached to the lobby but is not seated in the match; ending its connection");
			_transport.Disconnect(unseated[index]);
		}

		_log.Write($"the lobby handed {_seatedPeers.Count} connection(s) to the match; the port it was reached on belongs to the match now");
		MarkStartStage("match ready to describe");
		return null;
	}

	/// <summary>
	///     The seat a match is hosted from: the lobby's administrator, named by the assignment it was seated under. A lobby
	///     whose administrator was not seated (it dropped before the countdown ended) hosts the match from nowhere, which
	///     is what a map reads to refuse an administrative command.
	/// </summary>
	private int ResolveLobbyHostPlayerSlot(IReadOnlyList<(int PeerId, int Slot)> assignments)
	{
		int adminPeerId = _lobby?.AdminPeerId ?? 0;
		if (adminPeerId <= 0) return 0;

		for (int index = 0; index < assignments.Count; index++)
		{
			if (assignments[index].PeerId != adminPeerId) continue;
			return assignments[index].Slot;
		}

		return 0;
	}

	private MatchRosterSeat[] ReadRoster(out MatchOptionSet matchOptions)
	{
		matchOptions = new MatchOptionSet();
		if (string.IsNullOrWhiteSpace(_arguments.RosterPath)) return Array.Empty<MatchRosterSeat>();

		if (!MatchRosterFile.TryRead(_arguments.RosterPath, out MatchRosterSeat[] roster, out matchOptions, out string error))
		{
			throw new InvalidDataException(error);
		}

		int people = 0;
		int computers = 0;
		for (int index = 0; index < roster.Length; index++)
		{
			if (roster[index].IsComputer) computers++;
			else people++;
		}

		_log.Write($"the match was started for {people} player(s) and {computers} computer(s) on the roster");
		if (!matchOptions.IsEmpty) _log.Write($"the roster states {matchOptions.Count} match option(s), which the match plays instead of the map's own defaults");
		return roster;
	}

	private void AnnounceReady()
	{
		if (_readyAnnounced) return;
		_readyAnnounced = true;

		if (_mapHash.Length == 0) _mapHash = MapContentHash.Compute(_arguments.MapPath, _arguments.LogPath);
		string wireTableHash = ReplicatedComponents.ComputeWireTableHash();

		_log.Write($"ready build={RealmVersion.GameBuildNumber} mapHash={_mapHash} wireTableHash={wireTableHash}");
	}
}
