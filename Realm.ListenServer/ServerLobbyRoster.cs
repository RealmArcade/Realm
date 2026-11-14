using Realm.Ecs;
using Realm.Shared.Network;
using Realm.Shared.Terrain;

namespace Realm.ListenServer;

/// <summary>
///     The roster an open lobby offers: the factions its map states, how many players it seats, and which of a seat's
///     faction, team and colour the map locked. It publishes itself into the guest's shared memory once per lobby tick,
///     which is what lets a map script read a slot without crossing the host boundary, and it is the only thing that may
///     change a seat those rules cover.
/// </summary>
public class ServerLobbyRoster
{
	private readonly ILobbySeatTable _lobby;
	private readonly ServerLog _log;
	private readonly List<string> _factions = new(EcsSharedMemory.MAX_LOBBY_FACTIONS);
	private readonly bool[] _factionLocked = new bool[EcsSharedMemory.MAX_PLAYERS];
	private readonly bool[] _teamLocked = new bool[EcsSharedMemory.MAX_PLAYERS];
	private readonly bool[] _colorLocked = new bool[EcsSharedMemory.MAX_PLAYERS];
	private readonly string?[] _teamSpawnCoordinates = new string?[EcsSharedMemory.MAX_PLAYERS];
	private readonly List<string> _boardCoordinates = new();
	private IntPtr _sharedMemoryBase = IntPtr.Zero;
	private uint _revision;
	private int _teamCount = LobbyLimits.DefaultTeamCount;
	private bool _teamsTogether = true;
	private bool _hostChoseTeamsTogether;
	private MatchShroudMode _shroudMode = MatchShroudMode.FromMap;

	public ServerLobbyRoster(ILobbySeatTable lobby, ServerLog log, int teamCount = LobbyLimits.DefaultTeamCount)
	{
		_lobby = lobby;
		_log = log;
		_teamCount = Math.Clamp(teamCount, 1, LobbyLimits.MaximumSeats);
	}

	public int FactionCount => _factions.Count;

	public int TeamCount => _teamCount;

	/// <summary>
	///     Whether the match groups each team's players into consecutive player slots when it starts. It begins at the
	///     default the map states and follows the host from the moment the host changes it.
	/// </summary>
	public bool TeamsTogether => _teamsTogether;

	/// <summary>
	///     The shroud mode the lobby plays unless its host chooses another: the mode the map's lobby script stated, or
	///     <see cref="MatchShroudMode.FromMap" /> while the map has stated nothing.
	/// </summary>
	public MatchShroudMode ShroudMode => _shroudMode;

	public int MaximumPlayerCount => _lobby.MaximumPlayers;

	public void SetTeamCount(int teamCount)
	{
		int clamped = Math.Clamp(teamCount, 1, LobbyLimits.MaximumSeats);
		if (clamped == _teamCount) return;

		_teamCount = clamped;
		_log.Write($"the lobby now offers {_teamCount} team(s)");
		Publish();
		_lobby.BroadcastState();
	}

	public bool AddFaction(string? factionName)
	{
		string name = NormaliseFactionName(factionName);
		if (name.Length == 0) return false;
		if (IndexOfFaction(name) >= 0) return true;
		if (_factions.Count >= EcsSharedMemory.MAX_LOBBY_FACTIONS)
		{
			_log.Write($"the lobby already offers {_factions.Count} faction(s); '{name}' is not added");
			return false;
		}

		_factions.Add(name);
		_log.Write($"lobby faction '{name}' added to the roster");
		Publish();
		return true;
	}

	public bool TryGetSeat(int slot, out LobbySeat seat) => _lobby.TryGetSeatBySlot(slot, out seat);

	public int IndexOfFaction(string? factionName)
	{
		if (string.IsNullOrWhiteSpace(factionName)) return -1;

		for (int index = 0; index < _factions.Count; index++)
		{
			if (string.Equals(_factions[index], factionName, StringComparison.OrdinalIgnoreCase)) return index;
		}

		return -1;
	}

	public string GetFactionName(int factionIndex)
	{
		return factionIndex >= 0 && factionIndex < _factions.Count ? _factions[factionIndex] : string.Empty;
	}

	public string[] GetFactionNames()
	{
		return _factions.ToArray();
	}

	public bool IsFactionLocked(int slot) => InRange(slot) && _factionLocked[slot];

	public bool IsTeamLocked(int slot) => InRange(slot) && _teamLocked[slot];

	public bool IsColorLocked(int slot) => InRange(slot) && _colorLocked[slot];

	public void SetFactionLocked(int slot, bool locked)
	{
		if (InRange(slot)) _factionLocked[slot] = locked;
	}

	public void SetTeamLocked(int slot, bool locked)
	{
		if (InRange(slot)) _teamLocked[slot] = locked;
	}

	public void SetColorLocked(int slot, bool locked)
	{
		if (InRange(slot)) _colorLocked[slot] = locked;
	}

	public bool TrySetPlayerFaction(int slot, string? factionName)
	{
		if (!InRange(slot) || _factionLocked[slot]) return false;
		if (!_lobby.TryGetSeatBySlot(slot, out LobbySeat seat) || seat.IsComputer && string.IsNullOrWhiteSpace(factionName)) return false;

		int factionIndex = IndexOfFaction(factionName);
		if (factionIndex < 0)
		{
			_log.Write($"seat {slot} was offered faction '{factionName}', which this lobby's roster does not offer; the order is refused");
			return false;
		}

		return _lobby.ApplyRosterFaction(slot, _factions[factionIndex]);
	}

	public bool TrySetPlayerTeam(int slot, int teamIndex)
	{
		if (!InRange(slot) || _teamLocked[slot]) return false;
		if (teamIndex < 0 || teamIndex > _teamCount) return false;

		return _lobby.ApplyRosterTeam(slot, teamIndex);
	}

	public bool TrySetPlayerColor(int slot, uint colorRgba)
	{
		if (!InRange(slot) || _colorLocked[slot]) return false;
		return _lobby.ApplyRosterColor(slot, colorRgba);
	}

	public bool TrySetMaximumPlayerCount(int maximumPlayerCount)
	{
		return _lobby.TrySetMaximumPlayers(maximumPlayerCount);
	}

	/// <summary>Hides one match setting from the lobby's host, or shows it again.</summary>
	/// <param name="setting">The setting being hidden or shown.</param>
	/// <param name="hidden">Whether the setting is hidden.</param>
	public void SetMatchSettingHidden(LobbySetting setting, bool hidden)
	{
		_lobby.SetSettingHidden(setting, hidden);
	}

	/// <summary>States whether late joiners wait before the board is described to them.</summary>
	/// <param name="enabled">Whether the delay is on.</param>
	public void SetSpectatorDelayDefault(bool enabled)
	{
		_lobby.SetSpectatorDelayDefault(enabled);
	}

	/// <summary>
	///     Points the roster at the coordinates this board authors, which is what a team's spawn assignment is checked
	///     against: a team seated in ground the board does not describe is a team the match could not place.
	/// </summary>
	/// <param name="coordinates">The board's coordinates, or nothing when the board authors none.</param>
	public void BindBoardCoordinates(IReadOnlyList<CoordinateSaveData>? coordinates)
	{
		_boardCoordinates.Clear();
		if (coordinates == null) return;

		for (int index = 0; index < coordinates.Count; index++)
		{
			string name = coordinates[index].Name;
			if (string.IsNullOrWhiteSpace(name)) continue;
			if (_boardCoordinates.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;

			_boardCoordinates.Add(name);
		}
	}

	/// <summary>
	///     States the value the lobby's Teams Together checkbox starts at. The host keeps the last word: a default stated
	///     after the host has changed the checkbox is remembered for the next lobby rather than undoing the host's choice.
	/// </summary>
	/// <param name="teamsTogether">Whether the checkbox starts checked.</param>
	public void SetTeamsTogetherDefault(bool teamsTogether)
	{
		if (_hostChoseTeamsTogether) return;
		if (_teamsTogether == teamsTogether) return;

		_teamsTogether = teamsTogether;
		_log.Write($"the lobby's map states teams together: {(teamsTogether ? "on" : "off")}");
		_lobby.BroadcastState();
	}

	/// <summary>
	///     Takes the host's own answer for the Teams Together checkbox, which overrules the default its map stated and
	///     holds for the rest of the lobby.
	/// </summary>
	/// <param name="teamsTogether">Whether the match should group each team's players.</param>
	/// <returns>Whether the lobby now holds that setting.</returns>
	public bool TrySetTeamsTogether(bool teamsTogether)
	{
		_hostChoseTeamsTogether = true;
		if (_teamsTogether == teamsTogether) return true;

		_teamsTogether = teamsTogether;
		_log.Write($"the lobby's host set teams together {(teamsTogether ? "on" : "off")}");
		_lobby.BroadcastState();
		return true;
	}

	/// <summary>
	///     States the shroud mode a lobby starts on, which is how a map says whether its board is played with vision limited
	///     or with the whole thing visible. The host keeps the last word: a mode the host chose is what the lobby plays, and
	///     this is the answer the lobby returns to when the host clears that choice.
	/// </summary>
	/// <param name="mode">The mode the lobby should start on.</param>
	public void SetShroudModeDefault(MatchShroudMode mode)
	{
		if (!Enum.IsDefined(mode)) return;
		if (_shroudMode == mode) return;

		_shroudMode = mode;
		_log.Write($"the lobby's map states the shroud mode: {mode}");
		_lobby.BroadcastState();
	}

	/// <summary>
	///     Assigns a team the board coordinate its players begin inside, or clears that assignment when the name is empty.
	///     A team the lobby does not offer and a coordinate this board does not author are both refused.
	/// </summary>
	/// <param name="teamIndex">The team number.</param>
	/// <param name="coordinateName">The board coordinate, or empty to clear the team's assignment.</param>
	/// <returns>Whether the lobby holds that assignment afterwards.</returns>
	public bool SetTeamSpawnCoordinate(int teamIndex, string? coordinateName)
	{
		if (teamIndex <= 0 || teamIndex >= EcsSharedMemory.MAX_PLAYERS) return false;

		string name = (coordinateName ?? string.Empty).Trim();
		if (name.Length == 0)
		{
			_teamSpawnCoordinates[teamIndex] = null;
			return true;
		}

		string? authored = FindBoardCoordinate(name);
		if (authored == null)
		{
			_log.Write($"team {teamIndex} was seated in coordinate '{name}', which this board does not author; the order is refused");
			return false;
		}

		_teamSpawnCoordinates[teamIndex] = authored;
		_log.Write($"team {teamIndex} begins inside coordinate '{authored}'");
		return true;
	}

	/// <summary>
	///     Reads the coordinate every team begins inside, as the match seats them, so a match that starts from this lobby
	///     places each team on the ground the map stated for it.
	/// </summary>
	/// <returns>One entry per team number, null for a team the map seated at the board's own anchors.</returns>
	public string?[] CopyTeamSpawnCoordinates()
	{
		return (string?[])_teamSpawnCoordinates.Clone();
	}

	private string? FindBoardCoordinate(string coordinateName)
	{
		for (int index = 0; index < _boardCoordinates.Count; index++)
		{
			if (string.Equals(_boardCoordinates[index], coordinateName, StringComparison.OrdinalIgnoreCase)) return _boardCoordinates[index];
		}

		return null;
	}

	/// <summary>
	///     Points the roster at the guest region it publishes into. A lobby whose world is not prepared yet has nowhere to
	///     publish, so nothing is written until it does.
	/// </summary>
	/// <param name="sharedMemoryBase">The base of the guest's shared memory region.</param>
	public void BindSharedMemory(IntPtr sharedMemoryBase)
	{
		_sharedMemoryBase = sharedMemoryBase;
		Publish();
	}

	/// <summary>
	///     Restates the roster in shared memory: the header, the faction table, and every seat the lobby holds. A map
	///     script reads this copy, so it is restated once per lobby tick rather than only when something changes.
	/// </summary>
	public unsafe void Publish()
	{
		IntPtr sharedMemoryBase = _sharedMemoryBase;
		if (sharedMemoryBase == IntPtr.Zero) return;

		LobbyRosterHeader* header = EcsSharedMemory.LobbyRosterHeaderAt(sharedMemoryBase);
		header->MaximumPlayerCount = _lobby.MaximumPlayers;
		header->FactionCount = _factions.Count;
		header->TeamCount = _teamCount;
		header->SlotCount = EcsSharedMemory.MAX_PLAYERS;
		header->Revision = ++_revision;

		char* names = EcsSharedMemory.LobbyFactionNamesAt(sharedMemoryBase);
		for (int index = 0; index < EcsSharedMemory.MAX_LOBBY_FACTIONS; index++)
		{
			char* name = names + index * EcsSharedMemory.LOBBY_FACTION_NAME_CHARACTERS;
			int length = 0;
			if (index < _factions.Count)
			{
				string faction = _factions[index];
				length = Math.Min(faction.Length, EcsSharedMemory.LOBBY_FACTION_NAME_CHARACTERS - 1);
				faction.AsSpan(0, length).CopyTo(new Span<char>(name, length));
			}

			for (int rest = length; rest < EcsSharedMemory.LOBBY_FACTION_NAME_CHARACTERS; rest++) name[rest] = '\0';
		}

		PlayerRosterSlot* slots = EcsSharedMemory.PlayerRosterSlotsAt(sharedMemoryBase);
		for (int slot = 0; slot < EcsSharedMemory.MAX_PLAYERS; slot++)
		{
			PlayerRosterSlot* entry = slots + slot;
			entry->FactionLocked = (byte)(_factionLocked[slot] ? 1 : 0);
			entry->TeamLocked = (byte)(_teamLocked[slot] ? 1 : 0);
			entry->ColorLocked = (byte)(_colorLocked[slot] ? 1 : 0);

			if (!_lobby.TryGetSeatBySlot(slot, out LobbySeat seat))
			{
				entry->Occupied = 0;
				entry->Computer = 0;
				entry->Host = 0;
				entry->FactionIndex = -1;
				entry->TeamIndex = 0;
				entry->ColorRgba = 0;
				continue;
			}

			entry->Occupied = 1;
			entry->Computer = (byte)(seat.IsComputer ? 1 : 0);
			entry->Host = (byte)(seat.IsHost ? 1 : 0);
			entry->FactionIndex = IndexOfFaction(seat.Faction);
			entry->TeamIndex = seat.Team;
			entry->ColorRgba = seat.ColorRgba;
		}
	}

	private static bool InRange(int slot)
	{
		return slot > 0 && slot < EcsSharedMemory.MAX_PLAYERS;
	}

	private static string NormaliseFactionName(string? factionName)
	{
		string sanitized = LobbyLimits.Sanitize(factionName, EcsSharedMemory.LOBBY_FACTION_NAME_CHARACTERS - 1);
		return sanitized.Trim();
	}
}
