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
using Realm.Shared.Reachability;
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

		MatchRosterSeat[] roster = ReadRoster(out MatchShroudMode rosterShroudMode, out LobbySettingSet rosterSettings);
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
			MatchShroudMode shroudMode = rosterShroudMode != MatchShroudMode.FromMap ? rosterShroudMode : _mapLobbyRoster?.ShroudMode ?? MatchShroudMode.FromMap;
			_simulation = new ServerSimulation(map, paths, _log, _arguments.Seed, _arguments.Cheats, _arguments.WasmDebug, _arguments.WorkspacePath, roster, _arguments.Cheats ? 1 : 0, preparedScript, shroudMode: shroudMode, lobbySettings: rosterSettings, teamSpawnCoordinates: _mapLobbyRoster?.CopyTeamSpawnCoordinates());
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
		_mapLobbyRoster = null;
		if (roster.Length == 0) return roster;

		var seats = new MatchRosterSeatTable(roster, _log);
		var publishedRoster = new ServerLobbyRoster(seats, _log, roster.Length);
		publishedRoster.BindBoardCoordinates(world.SaveData.Coordinates);
		lobbyScript = ServerLobbyScript.Start(world, paths, _log, publishedRoster, _arguments.WasmDebug, _arguments.WorkspacePath);
		_mapLobbyRoster = publishedRoster;

		return seats.ToMatchRosterSeats(roster).ToArray();
	}

	/// <summary>
	///     Sends one datagram from the lobby's game port to an address. A punch that names a reachability check states it, so
	///     the registry node can tell this lobby's punch from one a host sharing its public address sent at the same moment;
	///     a punch that names none only opens a path for a joining player, who is not running a check.
	/// </summary>
	/// <param name="address">The address to send from the game port to.</param>
	/// <param name="port">The port to send to.</param>
	/// <param name="probeId">The reachability check the punch is for, or empty for a punch that only opens a path.</param>
	private void PunchAddress(string address, int port, string probeId)
	{
		if (string.IsNullOrEmpty(probeId))
		{
			if (_transport.SendToAddress(address, port, ReachabilityPunch.Datagram)) _log.Write($"sent a punch from the game port to {address}:{port}");
			else _log.Write($"a punch from the game port to {address}:{port} could not be sent: this lobby's transport holds no socket to send it from");

			return;
		}

		Span<byte> datagram = stackalloc byte[ReachabilityPunch.MaximumDatagramBytes];
		int written = ReachabilityPunch.TryWrite(datagram, probeId);
		if (written <= 0)
		{
			_log.Write($"the punch for check '{probeId}' toward {address}:{port} could not be written, so nothing was sent");
			return;
		}

		if (!_transport.SendToAddress(address, port, datagram[..written])) _log.Write($"the punch for check '{probeId}' toward {address}:{port} could not be sent: this lobby's transport holds no socket to send it from");
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
			PunchAddress,
			peerId => _transport.TryReadPeerLink(peerId, out PeerLinkStatistics statistics) ? statistics : null,
			DescribeLobbyStartWarning);

		var roster = new ServerLobbyRoster(_lobby, _log);
		_lobby.Roster = roster;

		IsRunning = true;
		_mapHash = MapContentHash.Compute(_arguments.MapPath, _arguments.LogPath);
		MarkStartStage("lobby listening");
		_log.Write($"lobby opened for map '{_lobby.MapName}' version {_lobby.MapVersion} with {_lobby.OpenSlotCount} open player slot(s)");
		if (_transport.IsListeningOnAnotherPort) _log.Write($"port {_transport.RequestedPort} was already held on this machine, so the lobby listens on port {_transport.Port} instead; every address this lobby publishes states that port");
		if (_arguments.LoopbackOnly) _log.Write("this lobby listens on the loopback address only: nothing outside this screen can reach it");
		AnnounceReady();
	}
}
