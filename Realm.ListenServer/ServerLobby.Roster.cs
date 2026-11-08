using System.Numerics;
using System.Security.Cryptography;
using MemoryPack;
using Realm.Shared.Distribution;
using Realm.Shared.Maps;
using Realm.Shared.Network;
using Realm.Shared.Players;
using Realm.Shared.Services;

namespace Realm.ListenServer;

/// <summary>roster faction, team and colour rules and seat lookup.</summary>
public partial class ServerLobby
	{
	public bool ApplyRosterFaction(int slot, string faction)
	{
		if (!_seats.TryGetValue(slot, out LobbySeat seat)) return false;
		seat.Faction = faction;
		_seats[slot] = seat;
		BroadcastState();
		return true;
	}

	/// <summary>
	///     Puts the seat on a team its map's roster chose, where team zero means it plays on no team.
	/// </summary>
	/// <param name="slot">The seat's slot.</param>
	/// <param name="team">The team number.</param>
	/// <returns>Whether the seat holds the team afterwards.</returns>
	public bool ApplyRosterTeam(int slot, int team)
	{
		if (!_seats.TryGetValue(slot, out LobbySeat seat)) return false;

		int chosenTeam = Math.Max(0, team);
		if (!CanSeatPlayTeam(slot, chosenTeam, seat.Team))
		{
			_log.Write($"seat {slot} was put on team {chosenTeam}, which needs a free player seat this lobby does not have; the order is refused");
			return false;
		}

		seat.Team = chosenTeam;
		seat.TeamName = chosenTeam == 0 ? LobbyLimits.SpectatorTeamName : $"Team {chosenTeam}";
		if (chosenTeam == 0) seat.IsReady = false;
		_seats[slot] = seat;
		BroadcastState();
		return true;
	}

	/// <summary>
	///     Gives the seat a colour its map's roster chose.
	/// </summary>
	/// <param name="slot">The seat's slot.</param>
	/// <param name="colorRgba">The colour, packed as 0xRRGGBBAA.</param>
	/// <returns>Whether the seat holds the colour afterwards.</returns>
	public bool ApplyRosterColor(int slot, uint colorRgba)
	{
		if (!_seats.TryGetValue(slot, out LobbySeat seat)) return false;
		seat.ColorRgba = colorRgba;
		_seats[slot] = seat;
		BroadcastState();
		return true;
	}

	/// <summary>
	///     Seats a different number of players, which is what a map's roster states once its lobby is open.
	/// </summary>
	/// <param name="maximumPlayers">The number of player slots to seat.</param>
	/// <returns>Whether the lobby seats that many players afterwards.</returns>
	public bool TrySetMaximumPlayers(int maximumPlayers)
	{
		int clamped = Math.Clamp(maximumPlayers, 1, LobbyLimits.MaximumSeats);
		if (clamped == _maximumPlayers) return true;

		foreach (var pair in _seats)
		{
			if (pair.Value.Team < 1) continue;
			if (pair.Key <= clamped) continue;
			_log.Write($"the lobby cannot seat {clamped} player(s) while seat {pair.Key} is playing; the count is refused");
			return false;
		}

		_maximumPlayers = clamped;
		_log.Write($"the lobby now seats {_maximumPlayers} player(s)");
		BroadcastState();
		return true;
	}

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
}
