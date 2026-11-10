using System.Numerics;
using System.Security.Cryptography;
using MemoryPack;
using Realm.Shared.Distribution;
using Realm.Shared.Maps;
using Realm.Shared.Network;
using Realm.Shared.Players;
using Realm.Shared.Services;

namespace Realm.ListenServer;

/// <summary>the start warning, countdown and the match hand-off.</summary>
public partial class ServerLobby
	{
	private bool TryDescribeStartWarning(out string warning)
	{
		warning = string.Empty;

		string? described = _readStartWarning();
		if (string.IsNullOrWhiteSpace(described)) return false;

		warning = LobbyLimits.Sanitize(described, LobbyLimits.MaximumStartWarningLength);
		return warning.Length > 0;
	}

	/// <summary>
	///     Sends the map's start warning to the host alone, because the host is the only player who can start or abandon
	///     the match it warns about.
	/// </summary>
	/// <param name="peerId">The host's connection.</param>
	/// <param name="warning">The text the host has to accept.</param>
	private void SendStartWarning(int peerId, string warning)
	{
		_log.Write($"the lobby's map asks its host to read a warning before the match starts; the countdown waits for it");

		var message = new LobbyStartWarningMessage { Message = warning };
		_send(peerId, NetworkFrameKind.LobbyStartWarning, MemoryPackSerializer.Serialize(message), FrameDelivery.Reliable);
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

		bool hasPlayingPlayer = false;
		foreach (LobbySeat seat in _seats.Values)
		{
			if (seat.IsComputer) continue;
			if (seat.IsDisconnected) continue;
			if (seat.Team < 1) continue;

			hasPlayingPlayer = true;
			if (!seat.IsMapReady) return "Some players are still downloading the map. Wait for them or remove them before starting the game.";
			if (!seat.IsLoadReady) return "Some players are still loading the match. Wait for them or remove them before starting the game.";
			if (!ignoreReadiness && !seat.IsHost && !seat.IsReady) return "Not all players are ready to start.";
		}

		return hasPlayingPlayer ? null : "A match needs at least one connected player.";
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
			roster.Add(new MatchRosterSeat(seat.Slot, seat.AccountId, seat.Name, seat.Team, seat.TeamName, seat.IsComputer, (int)LobbyBotDifficultyRules.Clamp(seat.Difficulty)));
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
}
