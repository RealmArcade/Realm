using System;
using System.Collections.Generic;
using Realm.Ecs;
using Realm.Shared.Maps;
using Realm.Shared.Network;

namespace Realm.ListenServer;

/// <summary>
///     The seats a match started without a lobby was handed, held still so its map's lobby rules can state the roster
///     they would state to an open lobby. Nobody is admitted and nothing is shown, so the orders a map places land on the
///     seats that are already there and the table reads back as the roster the match starts on.
/// </summary>
public class MatchRosterSeatTable : ILobbySeatTable
{
	private readonly ServerLog _log;
	private readonly Dictionary<int, LobbySeat> _seats = new();

	public MatchRosterSeatTable(IReadOnlyList<MatchRosterSeat> roster, ServerLog log, int maximumPlayers)
	{
		_log = log;
		MaximumPlayers = Math.Clamp(maximumPlayers, 1, LobbyLimits.MaximumSeats);

		bool hostAssigned = false;
		for (int index = 0; index < roster.Count; index++)
		{
			MatchRosterSeat seat = roster[index];
			if (seat.Slot <= 0 || seat.Slot >= EcsSharedMemory.MAX_PLAYERS) continue;

			_seats[seat.Slot] = new LobbySeat
			{
				Slot = seat.Slot,
				AccountId = seat.AccountId,
				Name = seat.Name,
				Faction = seat.Faction,
				Team = seat.Team,
				TeamName = TeamNameOf(seat.Team),
				IsComputer = seat.IsComputer,
				IsHost = !seat.IsComputer && !hostAssigned,
				IsReady = true,
				IsMapReady = true,
				IsLoadReady = true,
				SessionToken = string.Empty
			};

			if (!seat.IsComputer) hostAssigned = true;
		}
	}

	public int MaximumPlayers { get; private set; }

	public bool TryGetSeatBySlot(int slot, out LobbySeat seat) => _seats.TryGetValue(slot, out seat);

	public void BroadcastState()
	{
	}

	public bool ApplyRosterFaction(int slot, string faction)
	{
		if (!_seats.TryGetValue(slot, out LobbySeat seat)) return false;

		seat.Faction = faction;
		_seats[slot] = seat;
		return true;
	}

	public bool ApplyRosterTeam(int slot, int team)
	{
		if (!_seats.TryGetValue(slot, out LobbySeat seat)) return false;

		int chosenTeam = Math.Max(0, team);
		if (!CanSeatPlayTeam(slot, chosenTeam, seat.Team))
		{
			_log.Write($"seat {slot} was put on team {chosenTeam}, which needs a free player seat this match does not have; the order is refused");
			return false;
		}

		seat.Team = chosenTeam;
		seat.TeamName = TeamNameOf(chosenTeam);
		_seats[slot] = seat;
		return true;
	}

	public bool ApplyRosterColor(int slot, uint colorRgba)
	{
		if (!_seats.TryGetValue(slot, out LobbySeat seat)) return false;

		seat.ColorRgba = colorRgba;
		_seats[slot] = seat;
		return true;
	}

	public bool TrySetMaximumPlayers(int maximumPlayerCount)
	{
		int clamped = Math.Clamp(maximumPlayerCount, 1, LobbyLimits.MaximumSeats);
		if (clamped == MaximumPlayers) return true;

		foreach (var pair in _seats)
		{
			if (pair.Value.Team < 1) continue;
			if (pair.Key <= clamped) continue;
			_log.Write($"the match cannot seat {clamped} player(s) while seat {pair.Key} is playing; the count is refused");
			return false;
		}

		MaximumPlayers = clamped;
		return true;
	}

	/// <summary>
	///     Reads the roster the match starts on: the seats this table still holds, with the teams, team names and
	///     factions the map's lobby rules left them on. A seat the map unseated by seating fewer players is dropped.
	/// </summary>
	/// <param name="roster">The roster the match was handed, which states each seat's identity and bot difficulty.</param>
	/// <returns>The seats the match plays, in slot order.</returns>
	public List<MatchRosterSeat> ToMatchRosterSeats(IReadOnlyList<MatchRosterSeat> roster)
	{
		var seated = new List<MatchRosterSeat>(roster.Count);
		for (int index = 0; index < roster.Count; index++)
		{
			MatchRosterSeat original = roster[index];
			if (!_seats.TryGetValue(original.Slot, out LobbySeat seat)) continue;
			if (seat.Slot > MaximumPlayers) continue;

			seated.Add(original with { Team = seat.Team, TeamName = seat.TeamName, Faction = seat.Faction });
		}

		seated.Sort(static (left, right) => left.Slot.CompareTo(right.Slot));
		return seated;
	}

	private bool CanSeatPlayTeam(int slot, int team, int heldTeam)
	{
		if (team < 1) return true;
		if (heldTeam >= 1) return true;
		if (slot > MaximumPlayers) return false;

		int playingSeats = 0;
		foreach (LobbySeat seat in _seats.Values)
		{
			if (seat.Team >= 1) playingSeats++;
		}

		return playingSeats < MaximumPlayers;
	}

	private static string TeamNameOf(int team) => team < 1 ? LobbyLimits.SpectatorTeamName : $"Team {team}";
}
