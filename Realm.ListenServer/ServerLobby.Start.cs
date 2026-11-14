using System.Numerics;
using System.Security.Cryptography;
using MemoryPack;
using Realm.Ecs;
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

		int seed = RandomNumberGenerator.GetInt32(1, int.MaxValue);
		var factionPicker = new Random(seed);
		string[] offeredFactions = Roster?.GetFactionNames() ?? Array.Empty<string>();

		var roster = new List<MatchRosterSeat>(_seats.Count);
		var assignments = new List<(int PeerId, int Slot)>(_seats.Count);
		var sessionSeats = new Dictionary<string, int>(_seats.Count, StringComparer.Ordinal);
		foreach (LobbySeat seat in _seats.Values)
		{
			string faction = ResolveSeatFaction(seat.Faction, offeredFactions, factionPicker);
			roster.Add(new MatchRosterSeat(seat.Slot, seat.AccountId, seat.Name, seat.Team, seat.TeamName, seat.IsComputer, (int)LobbyBotDifficultyRules.Clamp(seat.Difficulty), faction));
			if (!seat.IsComputer && seat.SessionToken.Length > 0) sessionSeats[seat.SessionToken] = seat.Slot;
			if (!seat.IsComputer && !seat.IsDisconnected && seat.PeerId > 0) assignments.Add((seat.PeerId, seat.Slot));
		}

		// Teams Together is a seating rule rather than a match parameter: the seats are permuted before the match reads
		// them, so every player index the match and its script see already reads as one block per team.
		if (Roster?.TeamsTogether == true)
		{
			GroupTeamsTogether(roster, assignments, sessionSeats);
			_log.Write("this lobby groups each team's players into consecutive player slots");
		}

		// The shroud the lobby plays is a match parameter, so it is written into the options the match reads before the
		// match exists: the host's own choice when it made one, and otherwise the mode the map's lobby script stated.
		MatchShroudMode shroudMode = EffectiveShroudMode;
		if (shroudMode != MatchShroudMode.FromMap && _matchOptions.Get(MatchOption.ShroudMode) == 0)
		{
			_matchOptions.Set(MatchOption.ShroudMode, (int)shroudMode);
			_log.Write($"this lobby plays with the shroud mode its map states: {shroudMode}");
		}

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

	/// <summary>
	///     Groups each team's players into consecutive player slots, which is what the lobby's Teams Together setting
	///     asks for: the seats of one team then hold 1 through n, the next team n+1 through m, and so on, so a match and
	///     the script that seats it read a side as one run of player indices. Seats are permuted rather than added, so
	///     nobody gains or loses a seat, and a seat on no team keeps the slot it already held.
	/// </summary>
	/// <param name="roster">The seats the match is being started with, in slot order.</param>
	/// <param name="assignments">The peer to slot assignments the match seats its connections by.</param>
	/// <param name="sessionSlots">The session token to slot assignments a reconnecting peer is recognised by.</param>
	private static void GroupTeamsTogether(List<MatchRosterSeat> roster, List<(int PeerId, int Slot)> assignments, Dictionary<string, int> sessionSlots)
	{
		var playing = new List<MatchRosterSeat>(roster.Count);
		for (int index = 0; index < roster.Count; index++)
		{
			if (roster[index].Team >= 1) playing.Add(roster[index]);
		}

		playing.Sort((left, right) => left.Team != right.Team ? left.Team.CompareTo(right.Team) : left.Slot.CompareTo(right.Slot));

		var slotByOldSlot = new Dictionary<int, int>(playing.Count);
		for (int index = 0; index < playing.Count; index++)
		{
			slotByOldSlot[playing[index].Slot] = index + 1;
		}

		for (int index = 0; index < roster.Count; index++)
		{
			if (!slotByOldSlot.TryGetValue(roster[index].Slot, out int groupedSlot)) continue;
			roster[index] = roster[index] with { Slot = groupedSlot };
		}

		roster.Sort((left, right) => left.Slot.CompareTo(right.Slot));

		for (int index = 0; index < assignments.Count; index++)
		{
			(int peerId, int slot) = assignments[index];
			if (slotByOldSlot.TryGetValue(slot, out int groupedSlot)) assignments[index] = (peerId, groupedSlot);
		}

		if (sessionSlots.Count == 0) return;

		var groupedSessions = new Dictionary<string, int>(sessionSlots.Count, StringComparer.Ordinal);
		foreach (var pair in sessionSlots)
		{
			groupedSessions[pair.Key] = slotByOldSlot.TryGetValue(pair.Value, out int groupedSlot) ? groupedSlot : pair.Value;
		}

		sessionSlots.Clear();
		foreach (var pair in groupedSessions) sessionSlots[pair.Key] = pair.Value;
	}

	/// <summary>
	///     Names the faction a seat plays the match as: the faction it holds, or, for a seat that left the choice to the
	///     match, one of the factions its map offers drawn from the match seed. A map that offers no factions leaves a
	///     random seat without one, exactly as a seat that chose nothing holds none.
	/// </summary>
	/// <param name="faction">The faction the seat holds in the lobby.</param>
	/// <param name="offeredFactions">The factions the lobby's map offers.</param>
	/// <param name="factionPicker">The draw this match resolves its random seats from.</param>
	/// <returns>The faction the seat enters the match with.</returns>
	private static string ResolveSeatFaction(string faction, IReadOnlyList<string> offeredFactions, Random factionPicker)
	{
		if (!LobbyLimits.IsRandomFaction(faction)) return faction;
		if (offeredFactions.Count == 0) return string.Empty;

		return offeredFactions[factionPicker.Next(offeredFactions.Count)];
	}
}
