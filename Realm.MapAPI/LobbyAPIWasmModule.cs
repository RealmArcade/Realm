using System;
using Realm.Ecs;

namespace Realm.MapAPI;

/// <summary>
///     The guest half of the lobby roster: reads are taken straight out of the shared roster the authority publishes, so
///     they cost no host call, and writes are placed as orders the authority applies to the lobby it is holding.
/// </summary>
public unsafe class LobbyAPI_WasmModule : ILobbyAPI
{
	private static IntPtr SharedMemory => EcsSharedMemory.g_realmSharedMemoryBase;

	/// <summary>How many player slots this lobby currently seats, as the authority last published it.</summary>
	public int MaximumPlayerCount => EcsSharedMemory.LobbyRosterHeaderAt(SharedMemory)->MaximumPlayerCount;

	/// <summary>
	///     The number of slots the shared roster carries, which is the highest player index this API accepts. Slots are
	///     indexed from one, so slot zero is never a player and every index outside one to this value minus one is refused.
	/// </summary>
	public int SlotCount => EcsSharedMemory.LobbyRosterHeaderAt(SharedMemory)->SlotCount;

	/// <summary>How many factions the map offers, in the order <see cref="GetFactionName" /> reports them.</summary>
	public int FactionCount => EcsSharedMemory.LobbyRosterHeaderAt(SharedMemory)->FactionCount;

	/// <summary>
	///     The name of one of the map's factions, read out of the shared roster without allocating a string so a script
	///     may call it inside its lobby tick.
	/// </summary>
	/// <param name="factionIndex">The faction's position in the lobby's faction table.</param>
	/// <returns>
	///     The faction's name, valid until the authority restates the roster, or an empty span for an index the lobby does
	///     not offer.
	/// </returns>
	public ReadOnlySpan<char> GetFactionName(int factionIndex)
	{
		if (factionIndex < 0 || factionIndex >= EcsSharedMemory.MAX_LOBBY_FACTIONS) return ReadOnlySpan<char>.Empty;
		if (factionIndex >= FactionCount) return ReadOnlySpan<char>.Empty;

		char* name = EcsSharedMemory.LobbyFactionNamesAt(SharedMemory) + factionIndex * EcsSharedMemory.LOBBY_FACTION_NAME_CHARACTERS;
		return new ReadOnlySpan<char>(name, MeasureName(name));
	}

	/// <summary>
	///     Adds a faction to the lobby's roster, which is how a map states the choices its players are offered. A blank name
	///     is refused without placing an order, and the authority does not add a name the roster already offers.
	/// </summary>
	/// <param name="factionName">The faction's name, at most 24 characters.</param>
	/// <returns>Whether the authority accepted the order and the faction is part of the roster afterwards.</returns>
	public bool AddFaction(string factionName)
	{
		return !string.IsNullOrWhiteSpace(factionName) && LobbyImports.AddFaction(factionName);
	}

	/// <summary>The faction table position of the slot's faction.</summary>
	/// <param name="playerIndex">The player slot.</param>
	/// <returns>The faction's position in the lobby's faction table, or -1 for a slot outside the roster or holding no faction.</returns>
	public int GetPlayerFactionIndex(int playerIndex)
	{
		PlayerRosterSlot* slot = ResolveSlot(playerIndex);
		return slot == null ? -1 : slot->FactionIndex;
	}

	/// <summary>The faction the slot plays as, read without allocating a string.</summary>
	/// <param name="playerIndex">The player slot.</param>
	/// <returns>The faction's name, or an empty span for a slot outside the roster or holding no faction.</returns>
	public ReadOnlySpan<char> GetPlayerFaction(int playerIndex)
	{
		return GetFactionName(GetPlayerFactionIndex(playerIndex));
	}

	/// <summary>
	///     Gives a player slot a faction from the lobby's roster. A slot outside the roster and a blank name are refused
	///     without placing an order; the authority refuses a slot whose faction is locked, a slot nobody holds and a
	///     faction the roster does not offer.
	/// </summary>
	/// <param name="playerIndex">The player slot.</param>
	/// <param name="factionName">The faction the slot should play as.</param>
	/// <returns>Whether the authority took the order.</returns>
	public bool SetPlayerFaction(int playerIndex, string factionName)
	{
		if (!IsSlotIndex(playerIndex) || string.IsNullOrWhiteSpace(factionName)) return false;
		return LobbyImports.SetPlayerFaction(playerIndex, factionName);
	}

	/// <summary>Whether the slot's faction may no longer be changed.</summary>
	/// <param name="playerIndex">The player slot.</param>
	/// <returns>Whether the field is locked, or false for a slot outside the roster.</returns>
	public bool IsFactionLocked(int playerIndex)
	{
		PlayerRosterSlot* slot = ResolveSlot(playerIndex);
		return slot != null && slot->FactionLocked != 0;
	}

	/// <summary>Locks or unlocks the slot's faction, which is what stops a player changing it in the lobby.</summary>
	/// <param name="playerIndex">The player slot. An index outside the roster places no order.</param>
	/// <param name="locked">Whether the field is locked.</param>
	public void SetFactionLocked(int playerIndex, bool locked)
	{
		if (!IsSlotIndex(playerIndex)) return;
		LobbyImports.SetFactionLocked(playerIndex, locked);
	}

	/// <summary>The team number the slot plays on.</summary>
	/// <param name="playerIndex">The player slot.</param>
	/// <returns>The team number, where 0 means the slot plays on no team, or 0 for a slot outside the roster.</returns>
	public int GetPlayerTeam(int playerIndex)
	{
		PlayerRosterSlot* slot = ResolveSlot(playerIndex);
		return slot == null ? 0 : slot->TeamIndex;
	}

	/// <summary>
	///     Puts a player slot on a team. A slot outside the roster and a negative team number are refused without placing
	///     an order; the authority refuses a slot whose team is locked, a slot nobody holds and a team number outside the
	///     lobby's seats.
	/// </summary>
	/// <param name="playerIndex">The player slot.</param>
	/// <param name="teamIndex">The team number, or 0 for no team.</param>
	/// <returns>Whether the authority took the order.</returns>
	public bool SetPlayerTeam(int playerIndex, int teamIndex)
	{
		if (!IsSlotIndex(playerIndex) || teamIndex < 0) return false;
		return LobbyImports.SetPlayerTeam(playerIndex, teamIndex);
	}

	/// <summary>How many teams the lobby offers, which is the highest team number a slot may hold.</summary>
	public int TeamCount => EcsSharedMemory.LobbyRosterHeaderAt(SharedMemory)->TeamCount;

	/// <summary>
	///     States how many teams the lobby offers, which is what its players are offered as choices and what a slot's team
	///     may be numbered up to. A count of zero or less places no order.
	/// </summary>
	/// <param name="teamCount">The number of teams, from one up to the seats this lobby may serve.</param>
	public void SetTeamCount(int teamCount)
	{
		if (teamCount <= 0) return;
		LobbyImports.SetTeamCount(teamCount);
	}

	/// <summary>Whether the slot's team may no longer be changed.</summary>
	/// <param name="playerIndex">The player slot.</param>
	/// <returns>Whether the field is locked, or false for a slot outside the roster.</returns>
	public bool IsTeamLocked(int playerIndex)
	{
		PlayerRosterSlot* slot = ResolveSlot(playerIndex);
		return slot != null && slot->TeamLocked != 0;
	}

	/// <summary>Locks or unlocks the slot's team, which is what stops a player changing it in the lobby.</summary>
	/// <param name="playerIndex">The player slot. An index outside the roster places no order.</param>
	/// <param name="locked">Whether the field is locked.</param>
	public void SetTeamLocked(int playerIndex, bool locked)
	{
		if (!IsSlotIndex(playerIndex)) return;
		LobbyImports.SetTeamLocked(playerIndex, locked);
	}

	/// <summary>The slot's colour as the roster publishes it.</summary>
	/// <param name="playerIndex">The player slot.</param>
	/// <returns>The colour packed as 0xRRGGBBAA, or 0 for a slot outside the roster.</returns>
	public uint GetPlayerColor(int playerIndex)
	{
		PlayerRosterSlot* slot = ResolveSlot(playerIndex);
		return slot == null ? 0u : slot->ColorRgba;
	}

	/// <summary>
	///     Gives a player slot a colour. A slot outside the roster is refused without placing an order; the authority
	///     refuses a slot whose colour is locked and a slot nobody holds.
	/// </summary>
	/// <param name="playerIndex">The player slot.</param>
	/// <param name="colorRgba">The colour, packed as 0xRRGGBBAA.</param>
	/// <returns>Whether the authority took the order.</returns>
	public bool SetPlayerColor(int playerIndex, uint colorRgba)
	{
		if (!IsSlotIndex(playerIndex)) return false;
		return LobbyImports.SetPlayerColor(playerIndex, colorRgba);
	}

	/// <summary>Whether the slot's colour may no longer be changed.</summary>
	/// <param name="playerIndex">The player slot.</param>
	/// <returns>Whether the field is locked, or false for a slot outside the roster.</returns>
	public bool IsColorLocked(int playerIndex)
	{
		PlayerRosterSlot* slot = ResolveSlot(playerIndex);
		return slot != null && slot->ColorLocked != 0;
	}

	/// <summary>Locks or unlocks the slot's colour, which is what stops a player changing it in the lobby.</summary>
	/// <param name="playerIndex">The player slot. An index outside the roster places no order.</param>
	/// <param name="locked">Whether the field is locked.</param>
	public void SetColorLocked(int playerIndex, bool locked)
	{
		if (!IsSlotIndex(playerIndex)) return;
		LobbyImports.SetColorLocked(playerIndex, locked);
	}

	/// <summary>Whether a player or a computer holds the slot.</summary>
	/// <param name="playerIndex">The player slot.</param>
	/// <returns>Whether the slot is taken, or false for a slot outside the roster.</returns>
	public bool IsOccupied(int playerIndex)
	{
		PlayerRosterSlot* slot = ResolveSlot(playerIndex);
		return slot != null && slot->Occupied != 0;
	}

	/// <summary>Whether the slot is held by a computer rather than by a person.</summary>
	/// <param name="playerIndex">The player slot.</param>
	/// <returns>Whether the slot is computer-held, or false for a slot outside the roster.</returns>
	public bool IsComputer(int playerIndex)
	{
		PlayerRosterSlot* slot = ResolveSlot(playerIndex);
		return slot != null && slot->Computer != 0;
	}

	/// <summary>Whether the slot belongs to the player administering the lobby.</summary>
	/// <param name="playerIndex">The player slot.</param>
	/// <returns>Whether the slot is the host's, or false for a slot outside the roster.</returns>
	public bool IsHost(int playerIndex)
	{
		PlayerRosterSlot* slot = ResolveSlot(playerIndex);
		return slot != null && slot->Host != 0;
	}

	/// <summary>
	///     Sets how many players the lobby seats. A count of zero or less places no order; the authority clamps it to the
	///     seats its map may serve and refuses a count smaller than the players already sitting in it.
	/// </summary>
	/// <param name="maximumPlayerCount">The number of player slots the lobby should seat.</param>
	public void SetMaximumPlayerCount(int maximumPlayerCount)
	{
		if (maximumPlayerCount <= 0) return;
		LobbyImports.SetMaximumPlayerCount(maximumPlayerCount);
	}

	/// <summary>
	///     States whether the lobby's Teams Together checkbox starts checked, which is how a map that plays its teams as
	///     sides says so. The host keeps the last word, so an order placed after the host has changed the checkbox leaves
	///     the host's choice alone.
	/// </summary>
	/// <param name="teamsTogether">Whether the checkbox starts checked.</param>
	public void SetTeamsTogetherDefault(bool teamsTogether)
	{
		LobbyImports.SetTeamsTogetherDefault(teamsTogether);
	}

	/// <summary>
	///     Assigns a board coordinate to a team, so every player of that team starts at a random point inside it instead of
	///     at the anchor the board states for that player slot. A team outside the lobby's teams and a coordinate this
	///     board does not author are both refused, and an empty name clears the team's assignment.
	/// </summary>
	/// <param name="teamIndex">The team number, from one up to the teams this lobby offers.</param>
	/// <param name="coordinateName">The board coordinate the team begins inside, or empty to clear the assignment.</param>
	/// <returns>Whether the authority took the order.</returns>
	public bool SetTeamSpawnCoordinate(int teamIndex, string coordinateName)
	{
		if (teamIndex <= 0) return false;
		return LobbyImports.SetTeamSpawnCoordinate(teamIndex, coordinateName ?? string.Empty);
	}

	private static bool IsSlotIndex(int playerIndex)
	{
		return playerIndex > 0 && playerIndex < EcsSharedMemory.LobbyRosterHeaderAt(SharedMemory)->SlotCount;
	}

	private static PlayerRosterSlot* ResolveSlot(int playerIndex)
	{
		if (!IsSlotIndex(playerIndex)) return null;
		return EcsSharedMemory.PlayerRosterSlotsAt(SharedMemory) + playerIndex;
	}

	private static int MeasureName(char* name)
	{
		int length = 0;
		while (length < EcsSharedMemory.LOBBY_FACTION_NAME_CHARACTERS && name[length] != '\0') length++;
		return length;
	}
}
