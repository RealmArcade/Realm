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
using Realm.Shared.Metadata;
using Realm.Shared.Network;
using Realm.Shared.Simulation;

namespace Realm.ListenServer;

/// <summary>construction, start and the lobby bring-up.</summary>
public partial class ListenServerHost
	{
	public ListenServerHost(ServerArguments arguments, ServerLog log, IServerTransport transport)
	{
		_arguments = arguments;
		_log = log;
		_transport = transport;
	}

	public bool IsRunning { get; private set; }

	public bool ShutdownRequested { get; private set; }

	public int Port => _transport.Port;

	/// <summary>
	///     The map this match was started on, hashed the same way every other process hashes it. The screen that put the
	///     match here is told it in the answer to its start command, so its handshake states the map it expects rather than
	///     recomputing one it just handed over.
	/// </summary>
	public string MapHash => _mapHash;

	public ServerLog Log => _log;

	public void Start()
	{
		if (IsRunning) return;
		if (!Directory.Exists(_arguments.MapPath))
		{
			throw new DirectoryNotFoundException($"The map folder '{_arguments.MapPath}' does not exist.");
		}

		_startClock.Restart();

		if (_arguments.Lobby)
		{
			StartLobby();
			return;
		}

		_scriptLogSink = _log.Write;
		WasmScriptHost.OnWasmLog += _scriptLogSink;
		LagProbe.TryEnableFromEnvironment("authority");

		MatchRosterSeat[] roster = ReadRoster(out MatchOptionSet rosterOptions);
		var paths = ServerMatchPaths.From(_arguments);
		LoadedMap map = MapWorldLoader.LoadShell(_arguments.MapPath);
		MarkStartStage("map loaded");
		ServerLobbyScript? lobbyScript = null;
		WasmScriptHost? preparedScript = null;
		try
		{
			roster = ApplyMapLobbyRules(map, paths, roster, out lobbyScript);
			MapWorldLoader.Seat(map, roster);
			preparedScript = lobbyScript?.DetachHost();
			_simulation = new ServerSimulation(map, paths, _log, _arguments.Seed, _arguments.Cheats, _arguments.WasmDebug, _arguments.WorkspacePath, roster, _arguments.Cheats ? 1 : 0, preparedScript, matchOptions: rosterOptions);
			preparedScript = null;
			lobbyScript = null;
			MarkStartStage("simulation built");
			WarmSnapshotFraming();
			_simulation.PeerDropped += DisconnectPeer;
			_log.Write($"map '{paths.MapName}' keeps its saved data under '{_simulation.MapSavedDataDirectory()}'");
			_log.Write(_simulation.Describe());
			for (int index = 0; index < map.Warnings.Count; index++)
			{
				_log.Write($"map warning: {map.Warnings[index]}");
			}

			if (_arguments.Cheats)
			{
				_log.Write("cheats are enabled: this match is reachable on the loopback address only and seats the one player it was started for");
			}

			if (_arguments.WasmDebug)
			{
				_log.Write($"map script debug mode is on: pid={Environment.ProcessId} workspace='{_arguments.WorkspacePath}'");
			}

			if (!_transport.TryStart(EcsWorld.MAX_PLAYERS, _arguments.Cheats, this, out string failure))
			{
				throw new IOException($"the match could not listen for peers: {failure}");
			}

			MarkStartStage("listening");

			if (_arguments.RecordAuthorityPath.Length > 0)
			{
				_syncRecorder = new AuthoritySyncRecorder(map, _arguments.RecordAuthorityPath, out string syncRecordPath);
				_log.Write($"this match records the world it publishes into '{syncRecordPath}'");
				_trafficRecorder = new SnapshotTrafficRecorder();
				_reportWriter = new ServerReportWriter();
			}

			IsRunning = true;
			ResetPauseAllowances();
			_mapHash = MapContentHash.Compute(_arguments.MapPath, _arguments.LogPath);
			MarkStartStage("ready to be joined");
			AnnounceReady();
		}
		catch
		{
			preparedScript?.Dispose();
			lobbyScript?.Dispose();
			_simulation?.Dispose();
			_simulation = null;
			throw;
		}
	}

	/// <summary>
	///     Runs the map's own lobby rules over a match that starts without a lobby. The seats the match was handed are the
	///     seats those rules apply to, so a map states the roster it would state to an open lobby - its factions, its team
	///     count and, for a match that was started for one screen, which players share a side - and the match starts on the
	///     roster the map left behind. Nothing is shown and nobody is admitted, so the acceptance and the dismissal an open
	///     lobby would have are this one pass.
	/// </summary>
	/// <param name="world">The map, loaded but not yet seated, which the map's lobby rules run on.</param>
	/// <param name="paths">Where this match's map, data and logs live.</param>
	/// <param name="roster">The seats the match was handed.</param>
	/// <param name="lobbyScript">The warmed script the match takes over, when the map brought one.</param>
	/// <returns>The seats the match plays, as the map's rules left them.</returns>
	private MatchRosterSeat[] ApplyMapLobbyRules(LoadedMap world, ServerMatchPaths paths, MatchRosterSeat[] roster, out ServerLobbyScript? lobbyScript)
	{
		lobbyScript = null;
		if (roster.Length == 0) return roster;

		int maximumPlayers = world.Metadata.MapProperties?.GetPlayablePlayerSlots() ?? MapInfoMetadata.DefaultPlayablePlayerSlots;
		var seats = new MatchRosterSeatTable(roster, _log, maximumPlayers);
		var publishedRoster = new ServerLobbyRoster(seats, _log, roster.Length);
		lobbyScript = ServerLobbyScript.Start(world, paths, _log, publishedRoster, _arguments.WasmDebug, _arguments.WorkspacePath);

		return seats.ToMatchRosterSeats(roster).ToArray();
	}

	private void StartLobby()
	{
		if (!_transport.TryStart(EcsWorld.MAX_PLAYERS, loopbackOnly: _arguments.LoopbackOnly, this, out string failure))
		{
			throw new IOException($"the lobby could not listen for peers: {failure}");
		}

		_mapShares = new ServerMapShares(_arguments.MapPath, _log);
		_lobby = new ServerLobby(
			_arguments,
			_log,
			(peerId, kind, payload, delivery) => _transport.Send(peerId, kind, payload, delivery),
			peerId => _transport.Disconnect(peerId),
			StartMatchFromLobby,
			Stop,
			(address, port) => _transport.SendToAddress(address, port, Encoding.UTF8.GetBytes("PUNCH")),
			peerId => _transport.TryReadPeerLink(peerId, out PeerLinkStatistics statistics) ? statistics : null,
			DescribeLobbyStartWarning);

		var roster = new ServerLobbyRoster(_lobby, _log);
		_lobby.Roster = roster;

		IsRunning = true;
		_mapHash = MapContentHash.Compute(_arguments.MapPath, _arguments.LogPath);
		MarkStartStage("lobby listening");
		_log.Write($"lobby opened for map '{_lobby.MapName}' version {_lobby.MapVersion} with {_lobby.MaximumPlayers} playable slot(s)");
		if (_arguments.LoopbackOnly) _log.Write("this lobby listens on the loopback address only: nothing outside this screen can reach it");
		AnnounceReady();
	}
}
