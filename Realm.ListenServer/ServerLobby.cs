using System.Numerics;
using System.Security.Cryptography;
using MemoryPack;
using Realm.Shared.Distribution;
using Realm.Shared.Maps;
using Realm.Shared.Network;
using Realm.Shared.Players;
using Realm.Shared.Services;

namespace Realm.ListenServer;

public class ServerLobby
{
	private readonly Action<int, NetworkFrameKind, byte[], FrameDelivery> _send;

	private readonly Action<int> _disconnect;

	private readonly Func<IReadOnlyList<MatchRosterSeat>, IReadOnlyList<(int PeerId, int Slot)>, IReadOnlyDictionary<string, int>, int, string?> _startMatch;

	private readonly Action _lobbyAbandoned;

	private readonly Action<string, int> _punchAddress;

	private readonly ServerLog _log;
	private readonly string _mapFolder;
	private readonly string _mapName;
	private readonly string _mapVersion;
	private readonly int _maximumPlayers;

	private readonly SortedDictionary<int, LobbySeat> _seats = new();
	private readonly Dictionary<int, int> _slotByPeer = new();
	private readonly List<LobbyChatMessage> _chatHistory = new();

	private const int MaximumChatHistory = 200;

	private const int MaximumSessionTokenLength = 64;

	private int _adminPeerId;
	private long _revision;
	private int _nextComputerNumber = 1;
	private bool _countdownActive;
	private double _countdownRemainingSeconds;
	private int _lastCountdownSeconds;
	private bool _matchStarted;

	public ServerLobby(ServerArguments arguments, ServerLog log, Action<int, NetworkFrameKind, byte[], FrameDelivery> send, Action<int> disconnect,
		Func<IReadOnlyList<MatchRosterSeat>, IReadOnlyList<(int PeerId, int Slot)>, IReadOnlyDictionary<string, int>, int, string?> startMatch, Action lobbyAbandoned, Action<string, int> punchAddress)
	{
		_log = log;
		_send = send;
		_disconnect = disconnect;
		_startMatch = startMatch;
		_lobbyAbandoned = lobbyAbandoned;
		_punchAddress = punchAddress;
		_mapFolder = arguments.MapPath;
		_mapName = ResolveMapName(arguments);
		_mapVersion = ResolveMapVersion(arguments);
		_maximumPlayers = ResolveMaximumPlayers(arguments);
	}

	public bool MatchStarted => _matchStarted;

	public int AdminPeerId => _adminPeerId;

	public string MapName => _mapName;

	public string MapVersion => _mapVersion;

	public int MaximumPlayers => _maximumPlayers;

	public bool HasHumanSeat
	{
		get
		{
			foreach (LobbySeat seat in _seats.Values)
			{
				if (seat.IsComputer || seat.IsDisconnected) continue;
				return true;
			}

			return false;
		}
	}

	public bool TryGetSeat(int peerId, out LobbySeat seat)
	{
		seat = default;
		if (!_slotByPeer.TryGetValue(peerId, out int slot)) return false;
		if (!_seats.TryGetValue(slot, out seat)) return false;
		return true;
	}

	private bool TryFindSeat(int seatPeerId, out LobbySeat seat)
	{
		if (TryGetSeat(seatPeerId, out seat)) return true;

		foreach (LobbySeat candidate in _seats.Values)
		{
			if (candidate.PeerId != seatPeerId) continue;
			seat = candidate;
			return true;
		}

		seat = default;
		return false;
	}

	public IReadOnlyList<LobbyChatMessage> ChatHistory => _chatHistory;

	public string AdmitPeer(int peerId, string name, string sessionToken, bool proveAdmin, out bool isAdmin)
	{
		isAdmin = false;
		if (_matchStarted) return "server_rejected_lobby_is_closed";

		if (proveAdmin && _adminPeerId == 0)
		{
			_adminPeerId = peerId;
			isAdmin = true;
			_log.Write($"peer {peerId} administers the lobby: it proved the ticket this authority was started with");
		}

		LobbySeat seat = default;
		bool restored = false;

		if (!isAdmin && !string.IsNullOrEmpty(sessionToken))
		{
			foreach (var pair in _seats)
			{
				if (pair.Value.IsComputer) continue;
				if (!string.Equals(pair.Value.SessionToken, sessionToken, StringComparison.Ordinal)) continue;
				if (!pair.Value.IsDisconnected) continue;

				seat = pair.Value;
				_slotByPeer.Remove(seat.PeerId);
				restored = true;
				break;
			}
		}

		if (!restored)
		{
			int slot = FindFreeSlot(isAdmin ? 1 : 0);
			if (slot <= 0) return "server_rejected_no_free_slot";

			seat = new LobbySeat
			{
				Slot = slot,
				Name = DefaultName(name, slot, isAdmin),
				Faction = "HUMAN",
				TeamName = isAdmin ? "Team 1" : ResolveJoinTeamName(),
				ColorRgba = PackPaletteColor(slot),
				IsHost = isAdmin,
				SessionToken = LobbyLimits.Sanitize(sessionToken, MaximumSessionTokenLength)
			};
			seat.Team = ResolveTeamNumber(seat.TeamName, slot);
		}

		seat.PeerId = peerId;
		seat.IsHost = isAdmin || peerId == _adminPeerId;
		seat.IsDisconnected = false;
		if (!string.IsNullOrWhiteSpace(name)) seat.Name = LobbyLimits.Sanitize(name, LobbyLimits.MaximumPlayerNameLength);
		if (string.IsNullOrEmpty(seat.Name)) seat.Name = DefaultName(name, seat.Slot, seat.IsHost);

		_seats[seat.Slot] = seat;
		_slotByPeer[peerId] = seat.Slot;

		if (restored) _log.Write($"peer {peerId} took back lobby seat {seat.Slot} as '{seat.Name}'");
		else _log.Write($"peer {peerId} admitted to the lobby as seat {seat.Slot} ('{seat.Name}'), {(seat.IsHost ? "administrator" : "member")}");

		AddSystemChat($"{seat.Name} joined the lobby.");
		BroadcastState();
		return string.Empty;
	}

	public void HandleCommand(int peerId, LobbyCommand command)
	{
		if (_matchStarted) return;
		if (!TryGetSeat(peerId, out LobbySeat sender)) return;

		bool isAdmin = peerId == _adminPeerId;

		switch (command.Kind)
		{
			case LobbyCommandKind.SetReady:
				SetReady(sender, command.Flag);
				break;
			case LobbyCommandKind.UpdateSeat:
				UpdateSeat(peerId, isAdmin, sender, command);
				break;
			case LobbyCommandKind.AddComputer:
				if (!RequireAdmin(peerId, isAdmin, out int adminSlot, "add a computer player")) return;
				AddComputer(adminSlot);
				break;
			case LobbyCommandKind.Kick:
				if (!RequireAdmin(peerId, isAdmin, out _, "remove a player")) return;
				Kick(command.TargetPeerId);
				break;
			case LobbyCommandKind.StartCountdown:
				if (!RequireAdmin(peerId, isAdmin, out int starterSlot, "start the match")) return;
				StartCountdown(starterSlot, command.Flag);
				break;
			case LobbyCommandKind.CancelCountdown:
				CancelCountdown(sender);
				break;
			case LobbyCommandKind.SetSpectatorDelay:
				if (!RequireAdmin(peerId, isAdmin, out _, "change the spectator delay")) return;
				SetSpectatorDelay(command.Flag);
				break;
			case LobbyCommandKind.ReportMapReady:
				ReportMapReady(sender, command.Flag);
				break;
			case LobbyCommandKind.ReportLoadReady:
				ReportLoadReady(sender, command.Flag);
				break;
			case LobbyCommandKind.Chat:
				RelayChat(sender, command.Text);
				break;
			case LobbyCommandKind.Leave:
				RemoveSeat(peerId, $"{sender.Name} left the lobby.");
				break;
			case LobbyCommandKind.ReportHostStability:
				if (!isAdmin) return;
				ReportHostStability(command.Text);
				break;
			case LobbyCommandKind.PunchAddress:
				if (!isAdmin) return;
				if (string.IsNullOrWhiteSpace(command.Address) || command.Port <= 0 || command.Port > 65535) return;
				_punchAddress(LobbyLimits.Sanitize(command.Address, 64), command.Port);
				break;
			case LobbyCommandKind.RequestChatHistory:
				foreach (LobbyChatMessage line in _chatHistory)
				{
					_send(peerId, NetworkFrameKind.LobbyChat, MemoryPackSerializer.Serialize(line), FrameDelivery.Reliable);
				}
				break;
			default:
				_log.Write($"peer {peerId} sent a lobby command {(byte)command.Kind} this build does not accept");
				break;
		}
	}

	public void OnPeerDisconnected(int peerId)
	{
		if (_matchStarted) return;
		if (!TryGetSeat(peerId, out LobbySeat seat)) return;

		if (peerId == _adminPeerId)
		{
			_log.Write("the peer administering the lobby is gone; the lobby is being destroyed");
			_lobbyAbandoned();
			return;
		}

		seat.IsDisconnected = true;
		seat.IsReady = false;
		seat.IsMapReady = false;
		seat.IsLoadReady = false;
		_seats[seat.Slot] = seat;
		_slotByPeer.Remove(peerId);
		_log.Write($"lobby seat {seat.Slot} ('{seat.Name}') dropped and is held for its session identity");

		AddSystemChat($"{seat.Name} disconnected. Waiting for reconnect...");
		BroadcastState();
	}

	public void Tick(double deltaSeconds)
	{
		if (_matchStarted || !_countdownActive) return;

		_countdownRemainingSeconds -= Math.Max(0.0, deltaSeconds);
		int remaining = (int)Math.Ceiling(_countdownRemainingSeconds);
		if (remaining < 0) remaining = 0;
		if (remaining != _lastCountdownSeconds)
		{
			_lastCountdownSeconds = remaining;
			BroadcastState();
		}

		if (_countdownRemainingSeconds > 0) return;

		string? rejection = StartMatch();
		if (rejection == null) return;

		_countdownActive = false;
		AddSystemChat(rejection);
		BroadcastState();
	}

	public void SendStateTo(int peerId)
	{
		if (!TryGetSeat(peerId, out _)) return;

		_send(peerId, NetworkFrameKind.LobbyState, SerializeStateFor(DescribeState(), peerId), FrameDelivery.Reliable);
		foreach (LobbyChatMessage line in _chatHistory)
		{
			_send(peerId, NetworkFrameKind.LobbyChat, MemoryPackSerializer.Serialize(line), FrameDelivery.Reliable);
		}
	}

	public void BroadcastState()
	{
		LobbyStateMessage state = DescribeState();
		foreach (var pair in _slotByPeer)
		{
			_send(pair.Key, NetworkFrameKind.LobbyState, SerializeStateFor(state, pair.Key), FrameDelivery.Reliable);
		}
	}

	private byte[] SerializeStateFor(LobbyStateMessage state, int peerId)
	{
		LobbySeat[] seats = state.Seats;
		for (int index = 0; index < seats.Length; index++) seats[index].SessionToken = string.Empty;

		for (int index = 0; index < seats.Length; index++)
		{
			if (seats[index].PeerId != peerId) continue;
			if (_seats.TryGetValue(seats[index].Slot, out LobbySeat seat)) seats[index].SessionToken = seat.SessionToken;
			break;
		}

		return MemoryPackSerializer.Serialize(state);
	}

	private LobbyStateMessage DescribeState()
	{
		var seats = new LobbySeat[_seats.Count];
		int index = 0;
		foreach (LobbySeat seat in _seats.Values)
		{
			LobbySeat published = seat;
			published.SessionToken = string.Empty;
			seats[index++] = published;
		}

		return new LobbyStateMessage
		{
			Seats = seats,
			MapName = _mapName,
			MapVersion = _mapVersion,
			MaxPlayers = _maximumPlayers,
			SpectatorDelay = SpectatorDelay,
			HostStability = HostStability,
			CountdownActive = _countdownActive,
			CountdownRemainingSeconds = _countdownActive ? Math.Max(0, _lastCountdownSeconds) : 0,
			Revision = ++_revision
		};
	}

	public bool SpectatorDelay { get; private set; }

	public string HostStability { get; private set; } = "Excellent";

	private void SetReady(LobbySeat sender, bool ready)
	{
		if (sender.IsComputer) return;

		sender.IsReady = ready;
		_seats[sender.Slot] = sender;
		BroadcastState();
	}

	private void UpdateSeat(int peerId, bool isAdmin, LobbySeat sender, LobbyCommand command)
	{
		int targetPeerId = command.TargetPeerId != 0 ? command.TargetPeerId : peerId;
		if (!isAdmin && targetPeerId != peerId) return;

		if (!TryFindSeat(targetPeerId, out LobbySeat target))
		{
			_log.Write($"peer {peerId} asked to change a seat this lobby does not hold (target {targetPeerId}); the command is refused");
			return;
		}

		string name = LobbyLimits.Sanitize(command.Name, LobbyLimits.MaximumPlayerNameLength);
		if (!string.IsNullOrEmpty(name)) target.Name = name;

		string faction = LobbyLimits.Sanitize(command.Faction, LobbyLimits.MaximumFactionLength);
		if (!string.IsNullOrEmpty(faction)) target.Faction = faction;

		if (LobbyLimits.IsAcceptableTeamName(command.TeamName))
		{
			target.TeamName = LobbyLimits.Sanitize(command.TeamName, LobbyLimits.MaximumTeamNameLength);
			target.Team = ResolveTeamNumber(target.TeamName, target.Slot);
			if (string.Equals(target.TeamName, "Spectator", StringComparison.Ordinal)) target.IsReady = false;
		}

		if (command.ColorRgba != 0) target.ColorRgba = command.ColorRgba;

		_seats[target.Slot] = target;
		if (target.PeerId > 0) _slotByPeer[target.PeerId] = target.Slot;
		BroadcastState();
	}

	private void AddComputer(int adminSlot)
	{
		int slot = FindFreeSlot(0);
		if (slot <= 0)
		{
			AddSystemChat($"Cannot add an AI player: all {_maximumPlayers} playable slots are taken.");
			return;
		}

		var computer = new LobbySeat
		{
			PeerId = -100 - _nextComputerNumber,
			Slot = slot,
			Name = $"AI Bot {_nextComputerNumber}",
			Faction = "AI",
			TeamName = ResolveJoinTeamName(),
			ColorRgba = PackPaletteColor(slot),
			IsComputer = true,
			IsMapReady = true,
			IsLoadReady = true
		};
		computer.Team = ResolveTeamNumber(computer.TeamName, slot);

		_nextComputerNumber++;
		_seats[slot] = computer;
		_log.Write($"lobby seat {slot} is a computer player ('{computer.Name}')");

		AddSystemChat($"{computer.Name} added to the lobby.");
		BroadcastState();
	}

	private void Kick(int targetPeerId)
	{
		if (targetPeerId <= 0)
		{
			if (!TryGetComputerSeat(targetPeerId, out LobbySeat computer)) return;
			RemoveSeat(computer.PeerId, $"{computer.Name} removed from the lobby.");
			return;
		}

		if (targetPeerId == _adminPeerId) return;
		if (!TryFindSeat(targetPeerId, out LobbySeat target))
		{
			_log.Write($"the administrator asked to remove peer {targetPeerId}, which holds no seat in this lobby");
			return;
		}

		_log.Write($"peer {targetPeerId} ('{target.Name}') was removed from the lobby by its administrator");
		bool stillConnected = _slotByPeer.ContainsKey(target.PeerId);
		RemoveSeat(target.PeerId, $"{target.Name} was removed from the lobby.");

		if (!stillConnected) return;

		var kicked = new LobbyKickedMessage { Reason = "Kicked by Host" };
		_send(target.PeerId, NetworkFrameKind.LobbyKicked, MemoryPackSerializer.Serialize(kicked), FrameDelivery.Reliable);
		_disconnect(target.PeerId);
	}

	private void RemoveSeat(int peerId, string message)
	{
		if (!TryFindSeat(peerId, out LobbySeat seat)) return;

		_seats.Remove(seat.Slot);
		_slotByPeer.Remove(peerId);
		_log.Write($"lobby seat {seat.Slot} ('{seat.Name}') is gone");

		if (peerId == _adminPeerId)
		{
			AddSystemChat(message);
			_lobbyAbandoned();
			return;
		}

		AddSystemChat(message);
		BroadcastState();
	}

	private void StartCountdown(int starterSlot, bool ignoreReadiness)
	{
		if (_countdownActive) return;

		string? rejection = ValidateStart(ignoreReadiness);
		if (rejection != null)
		{
			SendSystemLineTo(starterSlot, rejection);
			return;
		}

		_countdownActive = true;
		_lastCountdownSeconds = LobbyLimits.CountdownSeconds;
		_countdownRemainingSeconds = LobbyLimits.CountdownSeconds;
		AddSystemChat($"Game starting in {LobbyLimits.CountdownSeconds} seconds on map: {_mapName}.");
		BroadcastState();
	}

	private string? ValidateStart(bool ignoreReadiness)
	{
		if (string.IsNullOrEmpty(_mapName)) return "This lobby has no map to play.";
		if (!Directory.Exists(_mapFolder)) return "The lobby's map folder is no longer available.";

		bool hasHuman = false;
		foreach (LobbySeat seat in _seats.Values)
		{
			if (seat.IsComputer) continue;
			if (seat.IsDisconnected) continue;

			hasHuman = true;
			if (!seat.IsMapReady) return "Some players are still downloading the map. Wait for them or remove them before starting the game.";
			if (!seat.IsLoadReady) return "Some players are still loading the match. Wait for them or remove them before starting the game.";
			if (!ignoreReadiness && !seat.IsHost && !seat.IsReady) return "Not all players are ready to start.";
		}

		return hasHuman ? null : "A match needs at least one connected player.";
	}

	private string? StartMatch()
	{
		string? rejection = ValidateStart(ignoreReadiness: true);
		if (rejection != null) return rejection;

		var roster = new List<MatchRosterSeat>(_seats.Count);
		var assignments = new List<(int PeerId, int Slot)>(_seats.Count);
		var sessionSeats = new Dictionary<string, int>(_seats.Count, StringComparer.Ordinal);
		foreach (LobbySeat seat in _seats.Values)
		{
			roster.Add(new MatchRosterSeat(seat.Slot, seat.Name, seat.Team, seat.TeamName, seat.IsComputer));
			if (!seat.IsComputer && seat.SessionToken.Length > 0) sessionSeats[seat.SessionToken] = seat.Slot;
			if (!seat.IsComputer && !seat.IsDisconnected && seat.PeerId > 0) assignments.Add((seat.PeerId, seat.Slot));
		}

		int seed = RandomNumberGenerator.GetInt32(1, int.MaxValue);
		rejection = _startMatch(roster, assignments, sessionSeats, seed);
		if (rejection != null) return rejection;

		_matchStarted = true;
		_countdownActive = false;

		var started = new LobbyMatchStartedMessage { MapName = _mapName, Seed = seed };
		byte[] payload = MemoryPackSerializer.Serialize(started);
		foreach (var pair in _slotByPeer)
		{
			_send(pair.Key, NetworkFrameKind.LobbyMatchStarted, payload, FrameDelivery.Reliable);
		}

		_log.Write($"the lobby started its match with {roster.Count} seat(s) and seed {seed}");

		var attached = new List<int>(_slotByPeer.Keys);
		var seated = new HashSet<int>();
		foreach (var assignment in assignments) seated.Add(assignment.PeerId);
		foreach (int peerId in attached)
		{
			if (seated.Contains(peerId)) continue;
			_log.Write($"peer {peerId} is attached but not seated in the match; ending its connection");
			_disconnect(peerId);
		}

		return null;
	}

	private void CancelCountdown(LobbySeat sender)
	{
		if (!_countdownActive)
		{
			BroadcastState();
			return;
		}

		_countdownActive = false;
		_lastCountdownSeconds = 0;
		AddSystemChat($"{sender.Name} cancelled the countdown.");
		BroadcastState();
	}

	private void SetSpectatorDelay(bool enabled)
	{
		if (SpectatorDelay == enabled) return;
		SpectatorDelay = enabled;
		BroadcastState();
	}

	private void ReportMapReady(LobbySeat sender, bool ready)
	{
		if (sender.IsComputer) return;
		if (sender.IsMapReady == ready) return;

		sender.IsMapReady = ready;
		if (!ready) sender.IsLoadReady = false;
		_seats[sender.Slot] = sender;
		_log.Write($"lobby seat {sender.Slot} ('{sender.Name}') {(ready ? "holds" : "does not hold")} the map");
		BroadcastState();
	}

	private void ReportLoadReady(LobbySeat sender, bool ready)
	{
		if (sender.IsComputer) return;
		if (sender.IsLoadReady == ready) return;
		if (ready && !sender.IsMapReady) return;

		sender.IsLoadReady = ready;
		_seats[sender.Slot] = sender;
		_log.Write($"lobby seat {sender.Slot} ('{sender.Name}') {(ready ? "finished" : "has not finished")} preparing the match");
		BroadcastState();
	}

	private void ReportHostStability(string? stability)
	{
		string value = LobbyLimits.Sanitize(stability, 32);
		if (value.Length == 0 || string.Equals(value, HostStability, StringComparison.Ordinal)) return;

		HostStability = value;
		BroadcastState();
	}

	private void RelayChat(LobbySeat sender, string? message)
	{
		string text = LobbyLimits.Sanitize(message, LobbyLimits.MaximumChatMessageLength);
		if (text.Length == 0) return;

		var line = new LobbyChatMessage
		{
			SenderPeerId = sender.PeerId,
			SenderName = sender.Name,
			Message = text,
			TimestampUtcTicks = DateTime.UtcNow.Ticks
		};

		_chatHistory.Add(line);
		if (_chatHistory.Count > MaximumChatHistory) _chatHistory.RemoveAt(0);

		byte[] payload = MemoryPackSerializer.Serialize(line);
		foreach (var pair in _slotByPeer)
		{
			_send(pair.Key, NetworkFrameKind.LobbyChat, payload, FrameDelivery.Reliable);
		}
	}

	private void AddSystemChat(string message)
	{
		if (string.IsNullOrWhiteSpace(message)) return;

		var line = new LobbyChatMessage
		{
			SenderPeerId = 0,
			SenderName = "System",
			Message = LobbyLimits.Sanitize(message, LobbyLimits.MaximumChatMessageLength),
			TimestampUtcTicks = DateTime.UtcNow.Ticks
		};

		_chatHistory.Add(line);
		if (_chatHistory.Count > MaximumChatHistory) _chatHistory.RemoveAt(0);

		byte[] payload = MemoryPackSerializer.Serialize(line);
		foreach (var pair in _slotByPeer)
		{
			_send(pair.Key, NetworkFrameKind.LobbyChat, payload, FrameDelivery.Reliable);
		}
	}

	private void SendSystemLineTo(int slot, string message)
	{
		if (!_seats.TryGetValue(slot, out LobbySeat seat)) return;
		if (seat.PeerId <= 0) return;

		var line = new LobbyChatMessage
		{
			SenderPeerId = 0,
			SenderName = "System",
			Message = LobbyLimits.Sanitize(message, LobbyLimits.MaximumChatMessageLength),
			TimestampUtcTicks = DateTime.UtcNow.Ticks
		};

		_send(seat.PeerId, NetworkFrameKind.LobbyChat, MemoryPackSerializer.Serialize(line), FrameDelivery.Reliable);
	}

	private bool RequireAdmin(int peerId, bool isAdmin, out int slot, string what)
	{
		slot = 0;
		if (isAdmin) return true;

		_log.Write($"peer {peerId} tried to {what} without administering the lobby; the command is refused");
		return false;
	}

	private bool TryGetComputerSeat(int seatPeerId, out LobbySeat seat)
	{
		foreach (LobbySeat candidate in _seats.Values)
		{
			if (!candidate.IsComputer || candidate.PeerId != seatPeerId) continue;
			seat = candidate;
			return true;
		}

		seat = default;
		return false;
	}

	private int FindFreeSlot(int preferredSlot)
	{
		if (preferredSlot > 0 && preferredSlot <= _maximumPlayers && !_seats.ContainsKey(preferredSlot)) return preferredSlot;

		for (int slot = 1; slot <= _maximumPlayers; slot++)
		{
			if (!_seats.ContainsKey(slot)) return slot;
		}

		return -1;
	}

	private string ResolveJoinTeamName()
	{
		int teamOne = 0;
		int teamTwo = 0;
		foreach (LobbySeat seat in _seats.Values)
		{
			if (seat.IsComputer) continue;
			if (string.Equals(seat.TeamName, "Team 1", StringComparison.Ordinal)) teamOne++;
			else if (string.Equals(seat.TeamName, "Team 2", StringComparison.Ordinal)) teamTwo++;
		}

		return teamTwo < teamOne ? "Team 2" : "Team 1";
	}

	private static string DefaultName(string? name, int slot, bool isHost)
	{
		string sanitized = LobbyLimits.Sanitize(name, LobbyLimits.MaximumPlayerNameLength);
		if (sanitized.Length > 0) return sanitized;
		return isHost ? "Host" : $"Player {slot}";
	}

	private static int ResolveTeamNumber(string? teamName, int slot)
	{
		if (!string.IsNullOrWhiteSpace(teamName) && teamName.StartsWith("Team ", StringComparison.Ordinal)
			&& int.TryParse(teamName.AsSpan(5), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int team) && team > 0)
		{
			return team;
		}

		return slot;
	}

	private static uint PackPaletteColor(int slot)
	{
		PlayerColorEntry entry = PlayerColorPalette.Palette[Math.Clamp(slot, 0, PlayerColorPalette.Count - 1)];
		uint red = (uint)Math.Clamp(MathF.Round(entry.Red * 255f), 0f, 255f);
		uint green = (uint)Math.Clamp(MathF.Round(entry.Green * 255f), 0f, 255f);
		uint blue = (uint)Math.Clamp(MathF.Round(entry.Blue * 255f), 0f, 255f);
		return (red << 24) | (green << 16) | (blue << 8) | 255u;
	}

	private static string ResolveMapName(ServerArguments arguments)
	{
		if (!string.IsNullOrWhiteSpace(arguments.MapName)) return arguments.MapName;
		string fallback = Path.GetFileName(Path.TrimEndingDirectorySeparator(arguments.MapPath));
		return string.IsNullOrEmpty(fallback) ? "map" : fallback;
	}

	private string ResolveMapVersion(ServerArguments arguments)
	{
		try
		{
			MapManifest manifest = MapFileService.LoadManifest(arguments.MapPath);
			if (!string.IsNullOrWhiteSpace(manifest.Version)) return manifest.Version;
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
		{
			_log.Write($"the lobby could not read its map's manifest: {exception.Message}");
		}

		return "1.0.0";
	}

	private int ResolveMaximumPlayers(ServerArguments arguments)
	{
		if (arguments.MaxPlayers > 0) return Math.Min(arguments.MaxPlayers, LobbyLimits.MaximumSeats);

		try
		{
			var metadata = MapFileService.LoadMetadata(arguments.MapPath);
			int slots = metadata.MapProperties?.GetPlayablePlayerSlots() ?? 0;
			if (slots > 0) return Math.Min(slots, LobbyLimits.MaximumSeats);
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
		{
			_log.Write($"the lobby could not read its map's metadata: {exception.Message}");
		}

		return 8;
	}

}
