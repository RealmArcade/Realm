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

	public int MaximumPlayerCount => EcsSharedMemory.LobbyRosterHeaderAt(SharedMemory)->MaximumPlayerCount;

	public int SlotCount => EcsSharedMemory.LobbyRosterHeaderAt(SharedMemory)->SlotCount;

	public int FactionCount => EcsSharedMemory.LobbyRosterHeaderAt(SharedMemory)->FactionCount;

	public ReadOnlySpan<char> GetFactionName(int factionIndex)
	{
		if (factionIndex < 0 || factionIndex >= EcsSharedMemory.MAX_LOBBY_FACTIONS) return ReadOnlySpan<char>.Empty;
		if (factionIndex >= FactionCount) return ReadOnlySpan<char>.Empty;

		char* name = EcsSharedMemory.LobbyFactionNamesAt(SharedMemory) + factionIndex * EcsSharedMemory.LOBBY_FACTION_NAME_CHARACTERS;
		return new ReadOnlySpan<char>(name, MeasureName(name));
	}

	public bool AddFaction(string factionName)
	{
		return !string.IsNullOrWhiteSpace(factionName) && LobbyImports.AddFaction(factionName);
	}

	public int GetPlayerFactionIndex(int playerIndex)
	{
		PlayerRosterSlot* slot = ResolveSlot(playerIndex);
		return slot == null ? -1 : slot->FactionIndex;
	}

	public ReadOnlySpan<char> GetPlayerFaction(int playerIndex)
	{
		return GetFactionName(GetPlayerFactionIndex(playerIndex));
	}

	public bool SetPlayerFaction(int playerIndex, string factionName)
	{
		if (!IsSlotIndex(playerIndex) || string.IsNullOrWhiteSpace(factionName)) return false;
		return LobbyImports.SetPlayerFaction(playerIndex, factionName);
	}

	public bool IsFactionLocked(int playerIndex)
	{
		PlayerRosterSlot* slot = ResolveSlot(playerIndex);
		return slot != null && slot->FactionLocked != 0;
	}

	public void SetFactionLocked(int playerIndex, bool locked)
	{
		if (!IsSlotIndex(playerIndex)) return;
		LobbyImports.SetFactionLocked(playerIndex, locked);
	}

	public int GetPlayerTeam(int playerIndex)
	{
		PlayerRosterSlot* slot = ResolveSlot(playerIndex);
		return slot == null ? 0 : slot->TeamIndex;
	}

	public bool SetPlayerTeam(int playerIndex, int teamIndex)
	{
		if (!IsSlotIndex(playerIndex) || teamIndex < 0) return false;
		return LobbyImports.SetPlayerTeam(playerIndex, teamIndex);
	}

	public int TeamCount => EcsSharedMemory.LobbyRosterHeaderAt(SharedMemory)->TeamCount;

	public void SetTeamCount(int teamCount)
	{
		if (teamCount <= 0) return;
		LobbyImports.SetTeamCount(teamCount);
	}

	public bool IsTeamLocked(int playerIndex)
	{
		PlayerRosterSlot* slot = ResolveSlot(playerIndex);
		return slot != null && slot->TeamLocked != 0;
	}

	public void SetTeamLocked(int playerIndex, bool locked)
	{
		if (!IsSlotIndex(playerIndex)) return;
		LobbyImports.SetTeamLocked(playerIndex, locked);
	}

	public uint GetPlayerColor(int playerIndex)
	{
		PlayerRosterSlot* slot = ResolveSlot(playerIndex);
		return slot == null ? 0u : slot->ColorRgba;
	}

	public bool SetPlayerColor(int playerIndex, uint colorRgba)
	{
		if (!IsSlotIndex(playerIndex)) return false;
		return LobbyImports.SetPlayerColor(playerIndex, colorRgba);
	}

	public bool IsColorLocked(int playerIndex)
	{
		PlayerRosterSlot* slot = ResolveSlot(playerIndex);
		return slot != null && slot->ColorLocked != 0;
	}

	public void SetColorLocked(int playerIndex, bool locked)
	{
		if (!IsSlotIndex(playerIndex)) return;
		LobbyImports.SetColorLocked(playerIndex, locked);
	}

	public bool IsOccupied(int playerIndex)
	{
		PlayerRosterSlot* slot = ResolveSlot(playerIndex);
		return slot != null && slot->Occupied != 0;
	}

	public bool IsComputer(int playerIndex)
	{
		PlayerRosterSlot* slot = ResolveSlot(playerIndex);
		return slot != null && slot->Computer != 0;
	}

	public bool IsHost(int playerIndex)
	{
		PlayerRosterSlot* slot = ResolveSlot(playerIndex);
		return slot != null && slot->Host != 0;
	}

	public void SetMaximumPlayerCount(int maximumPlayerCount)
	{
		if (maximumPlayerCount <= 0) return;
		LobbyImports.SetMaximumPlayerCount(maximumPlayerCount);
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
