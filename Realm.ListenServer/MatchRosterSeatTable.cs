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
	private readonly bool[] _openSlots = new bool[EcsSharedMemory.MAX_PLAYERS];
	private readonly uint[] _colorOverrides = new uint[EcsSharedMemory.MAX_PLAYERS];
	private readonly uint[] _minimapPlayerColors = new uint[EcsSharedMemory.MAX_PLAYERS];
	private readonly uint[] _minimapTeamColors = new uint[EcsSharedMemory.MAX_PLAYERS];

	public MatchRosterSeatTable(IReadOnlyList<MatchRosterSeat> roster, ServerLog log)
	{
		_log = log;

		for (int slot = 1; slot < _openSlots.Length; slot++) _openSlots[slot] = true;

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

	public int OpenSlotCount
	{
		get
		{
			int count = 0;
			for (int slot = 1; slot < _openSlots.Length; slot++)
			{
				if (_openSlots[slot]) count++;
			}

			return count;
		}
	}

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
			_log.Write($"seat {slot} was put on team {chosenTeam}, which needs a player slot this match leaves open; the order is refused");
			return false;
		}

		seat.Team = chosenTeam;
		seat.TeamName = TeamNameOf(chosenTeam);
		_seats[slot] = seat;
		return true;
	}

	public bool ApplyRosterColor(int slot, uint colorRgba)
	{
		if (slot <= 0 || slot >= _openSlots.Length) return false;

		if (!_seats.TryGetValue(slot, out LobbySeat seat))
		{
			_colorOverrides[slot] = colorRgba;
			return true;
		}

		seat.ColorRgba = colorRgba;
		_seats[slot] = seat;
		_colorOverrides[slot] = 0;
		return true;
	}

	/// <summary>
	///     Takes the minimap colour a map states for one of its players. A match started without a lobby has no screen to
	///     publish it to, so the statement is accepted and nothing is drawn from it.
	/// </summary>
	/// <param name="slot">The player slot.</param>
	/// <param name="mode">Which of the minimap's two colourings the colour applies to.</param>
	/// <param name="colorRgba">The colour, packed as 0xRRGGBBAA, or zero to clear the statement.</param>
	/// <returns>Whether the slot is one this table holds.</returns>
	public bool ApplyRosterMinimapColor(int slot, MinimapColorMode mode, uint colorRgba)
	{
		if (slot <= 0 || slot >= _openSlots.Length) return false;
		if (mode != MinimapColorMode.TeamColors && mode != MinimapColorMode.PlayerColors) return false;

		if (mode == MinimapColorMode.TeamColors)
		{
			_minimapTeamColors[slot] = colorRgba;
		}
		else
		{
			_minimapPlayerColors[slot] = colorRgba;
		}

		return true;
	}

	/// <summary>Reads the colour the table keeps for a slot nobody holds.</summary>
	/// <param name="slot">The player slot.</param>
	/// <returns>The colour, packed as 0xRRGGBBAA, or zero while the table keeps none.</returns>
	public uint ColorOverrideOf(int slot) => slot > 0 && slot < _openSlots.Length ? _colorOverrides[slot] : 0u;

	/// <summary>Reads the minimap colour the table holds for one scheme.</summary>
	/// <param name="slot">The player slot.</param>
	/// <param name="mode">Which of the minimap's two colourings the colour applies to.</param>
	/// <returns>The colour, packed as 0xRRGGBBAA, or zero while the table holds none.</returns>
	public uint MinimapColorOf(int slot, MinimapColorMode mode)
	{
		if (slot <= 0 || slot >= _openSlots.Length) return 0u;

		return mode == MinimapColorMode.TeamColors ? _minimapTeamColors[slot] : _minimapPlayerColors[slot];
	}

	/// <summary>
	///     Keeps every player index above a count for the match's map. Nobody is admitted to a started match, so the limit
	///     is accepted and closes those slots rather than refusing them.
	/// </summary>
	/// <param name="maximumPlayers">The highest player index a person may be seated in.</param>
	/// <returns>Whether the table holds that limit afterwards.</returns>
	public bool TrySetPlayerSlotLimit(int maximumPlayers)
	{
		if (maximumPlayers <= 0 || maximumPlayers >= _openSlots.Length) return false;

		for (int slot = 1; slot < _openSlots.Length; slot++)
		{
			if (slot <= maximumPlayers) continue;
			_openSlots[slot] = false;
		}

		return true;
	}

	public bool TrySetSlotOpen(int slot, bool open)
	{
		if (slot <= 0 || slot >= _openSlots.Length) return false;

		_openSlots[slot] = open;
		return true;
	}

	public bool TrySetOpenSlotCount(int count)
	{
		if (count <= 0 || count >= _openSlots.Length) return false;

		for (int slot = 1; slot < _openSlots.Length; slot++) _openSlots[slot] = slot <= count;
		return true;
	}

	/// <summary>Accepts the map's spectator delay default, which a match without a lobby has no switch to apply it to.</summary>
	/// <param name="enabled">Whether the delay is on.</param>
	public void SetSpectatorDelayDefault(bool enabled)
	{
	}

	/// <summary>
	///     Accepts a match setting a map declared. A match that was started without an open lobby shows no settings to
	///     anybody, so the declaration lands nowhere: it is accepted rather than refused, because a map that runs the same
	///     lobby rules over a roster it was handed is not doing anything wrong.
	/// </summary>
	/// <param name="settingId">The identifier the map declared the setting under.</param>
	/// <param name="control">How the host edits the setting.</param>
	/// <param name="label">The label the host reads.</param>
	/// <param name="tooltip">The sentence that explains the setting, or empty for none.</param>
	/// <param name="defaultValue">The value the setting holds until the host changes it.</param>
	public void DeclareMatchSetting(int settingId, LobbySettingControl control, string label, string tooltip, int defaultValue)
	{
	}

	/// <summary>Accepts one option of a match setting a map declared, which this table has no screen to show.</summary>
	/// <param name="settingId">The identifier the map declared the setting under.</param>
	/// <param name="optionLabel">The text the host reads in the dropdown.</param>
	public void DeclareMatchSettingOption(int settingId, string optionLabel)
	{
	}

	/// <summary>Accepts the values a count setting may hold, which this table has no screen to show.</summary>
	/// <param name="settingId">The identifier the map declared the setting under.</param>
	/// <param name="minimum">The lowest value the host may choose.</param>
	/// <param name="maximum">The highest value the host may choose.</param>
	public void DeclareMatchSettingRange(int settingId, int minimum, int maximum)
	{
	}

	/// <summary>Reads the value one declared setting holds, which this table has no declaration to answer from.</summary>
	/// <param name="settingId">The identifier the map declared the setting under.</param>
	/// <returns>Zero.</returns>
	public int GetMatchSettingValue(int settingId)
	{
		return 0;
	}

	/// <summary>
	///     Reads the roster the match starts on: the seats this table still holds, with the teams, team names and
	///     factions the map's lobby rules left them on. A seat the map unseated by closing its slot is dropped.
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
			if (!IsSlotOpen(seat.Slot)) continue;

			seated.Add(original with { Team = seat.Team, TeamName = seat.TeamName, Faction = seat.Faction });
		}

		seated.Sort(static (left, right) => left.Slot.CompareTo(right.Slot));
		return seated;
	}

public bool IsSlotOpen(int slot) => slot > 0 && slot < _openSlots.Length && _openSlots[slot];

	private bool CanSeatPlayTeam(int slot, int team, int heldTeam)
	{
		if (team < 1) return true;
		if (heldTeam >= 1) return true;

		return IsSlotOpen(slot);
	}

	private static string TeamNameOf(int team) => team < 1 ? LobbyLimits.SpectatorTeamName : $"Team {team}";
}
