using Realm.Ecs;
using Realm.Shared.Network;

namespace Realm.ListenServer;

/// <summary>
///     The roster an open lobby offers: the factions its map states, how many players it seats, and which of a seat's
///     faction, team and colour the map locked. It publishes itself into the guest's shared memory once per lobby tick,
///     which is what lets a map script read a slot without crossing the host boundary, and it is the only thing that may
///     change a seat those rules cover.
/// </summary>
public class ServerLobbyRoster
{
	private readonly ServerLobby _lobby;
	private readonly ServerLog _log;
	private readonly List<string> _factions = new(EcsSharedMemory.MAX_LOBBY_FACTIONS);
	private readonly bool[] _factionLocked = new bool[EcsSharedMemory.MAX_PLAYERS];
	private readonly bool[] _teamLocked = new bool[EcsSharedMemory.MAX_PLAYERS];
	private readonly bool[] _colorLocked = new bool[EcsSharedMemory.MAX_PLAYERS];
	private IntPtr _sharedMemoryBase = IntPtr.Zero;
	private uint _revision;
	private int _teamCount = LobbyLimits.DefaultTeamCount;

	public ServerLobbyRoster(ServerLobby lobby, ServerLog log)
	{
		_lobby = lobby;
		_log = log;
	}

	public int FactionCount => _factions.Count;

	public int TeamCount => _teamCount;

	public int MaximumPlayerCount => _lobby.MaximumPlayers;

	public void SetTeamCount(int teamCount)
	{
		int clamped = Math.Clamp(teamCount, 1, LobbyLimits.MaximumSeats);
		if (clamped == _teamCount) return;

		_teamCount = clamped;
		_log.Write($"the lobby now offers {_teamCount} team(s)");
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
