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

/// <summary>roster faction, team and colour rules and seat lookup.</summary>
public partial class ServerLobby
	{
	private static bool InPlayerRange(int slot) => slot > 0 && slot < EcsSharedMemory.MAX_PLAYERS;

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
	///     Gives a player slot the colour its map's roster chose. A slot holding a seat takes it on the seat, which is what
	///     every screen reads, and a slot nobody holds keeps it as an override so the match still draws the players the map
	///     drives there. A colour another slot already holds is taken: this is the map's own assignment rather than a player
	///     picking from the palette, and a map that drives a player beside a real one wants both drawn as one side.
	/// </summary>
	/// <param name="slot">The player slot.</param>
	/// <param name="colorRgba">The colour, packed as 0xRRGGBBAA.</param>
	/// <returns>Whether the slot holds the colour afterwards.</returns>
	public bool ApplyRosterColor(int slot, uint colorRgba)
	{
		if (!InPlayerRange(slot)) return false;

		if (_seats.TryGetValue(slot, out LobbySeat seat))
		{
			if (seat.ColorRgba == colorRgba && _colorOverrides[slot] == 0) return true;

			seat.ColorRgba = colorRgba;
			_seats[slot] = seat;
			_colorOverrides[slot] = 0;
		}
		else
		{
			if (_colorOverrides[slot] == colorRgba) return true;

			_colorOverrides[slot] = colorRgba;
		}

		BroadcastState();
		return true;
	}

	/// <summary>
	///     States the colour a player slot's units are drawn in on one minimap scheme, or clears the statement when the
	///     colour is zero. It reaches every screen with the seats, so the minimap a player is looking at is drawn from the
	///     colour the map stated.
	/// </summary>
	/// <param name="slot">The player slot.</param>
	/// <param name="mode">Which of the minimap's two colourings the colour applies to.</param>
	/// <param name="colorRgba">The colour, packed as 0xRRGGBBAA, or zero to clear the statement.</param>
	/// <returns>Whether the lobby holds that statement afterwards.</returns>
	public bool ApplyRosterMinimapColor(int slot, MinimapColorMode mode, uint colorRgba)
	{
		if (!InPlayerRange(slot)) return false;
		if (mode != MinimapColorMode.TeamColors && mode != MinimapColorMode.PlayerColors) return false;

		if (mode == MinimapColorMode.TeamColors)
		{
			if (_minimapTeamColors[slot] == colorRgba) return true;
			_minimapTeamColors[slot] = colorRgba;
		}
		else
		{
			if (_minimapPlayerColors[slot] == colorRgba) return true;
			_minimapPlayerColors[slot] = colorRgba;
		}

		BroadcastState();
		return true;
	}

	/// <summary>The colour the lobby's map keeps for a player slot's units, or zero while it keeps none.</summary>
	/// <param name="slot">The player slot.</param>
	/// <returns>The colour, packed as 0xRRGGBBAA.</returns>
	public uint ColorOverrideOf(int slot) => InPlayerRange(slot) ? _colorOverrides[slot] : 0u;

	/// <summary>The minimap colour the lobby's map stated for one scheme, or zero while it stated none.</summary>
	/// <param name="slot">The player slot.</param>
	/// <param name="mode">Which of the minimap's two colourings the colour applies to.</param>
	/// <returns>The colour, packed as 0xRRGGBBAA.</returns>
	public uint MinimapColorOf(int slot, MinimapColorMode mode)
	{
		if (!InPlayerRange(slot)) return 0u;

		return mode == MinimapColorMode.TeamColors ? _minimapTeamColors[slot] : _minimapPlayerColors[slot];
	}

	/// <summary>Copies the overlays a player slot's units are drawn in, which is what every screen is told beside the seats.</summary>
	/// <param name="colors">The colour the slot's units are drawn in, per player index, zero for none.</param>
	/// <param name="minimapPlayerColors">The minimap's player-colour scheme, per player index, zero for none.</param>
	/// <param name="minimapTeamColors">The minimap's team-colour scheme, per player index, zero for none.</param>
	public void CopyColorOverlays(out uint[] colors, out uint[] minimapPlayerColors, out uint[] minimapTeamColors)
	{
		colors = (uint[])_colorOverrides.Clone();
		minimapPlayerColors = (uint[])_minimapPlayerColors.Clone();
		minimapTeamColors = (uint[])_minimapTeamColors.Clone();
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
