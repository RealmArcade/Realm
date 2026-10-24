using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Realm.Ecs.Components.Core;

/// <summary>
/// Stores the simulation state for all script players, one slot per player index the map API can address.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct ScriptPlayersState
{
	public const int SlotCount = PlayerSlotArray.SlotCount;

	public PlayerSlotArray Slots;

	public ScriptPlayersState(IReadOnlyList<ScriptPlayer>? players)
	{
		this = default;
		if (players == null) return;

		int copyCount = players.Count < SlotCount ? players.Count : SlotCount;
		for (int index = 0; index < copyCount; index++)
		{
			Slots[index] = players[index];
		}
	}

	public bool IsValidSlot(int playerIndex)
	{
		return playerIndex >= 0 && playerIndex < SlotCount;
	}

	public ScriptPlayer Get(int playerIndex)
	{
		return IsValidSlot(playerIndex) ? Slots[playerIndex] : default;
	}

	public void Set(int playerIndex, in ScriptPlayer player)
	{
		if (!IsValidSlot(playerIndex)) return;

		Slots[playerIndex] = player;
	}

	public void SetName(int playerIndex, string? name)
	{
		if (!IsValidSlot(playerIndex)) return;

		Slots[playerIndex].Name = name;
	}

	public void SetActive(int playerIndex, bool active)
	{
		if (!IsValidSlot(playerIndex)) return;

		Slots[playerIndex].Active = active;
	}

	public void SetComputerControlled(int playerIndex, bool isComputer)
	{
		if (!IsValidSlot(playerIndex)) return;

		Slots[playerIndex].IsComputer = isComputer;
	}

	public void SetTeam(int playerIndex, int team, int teamNameStringId)
	{
		if (!IsValidSlot(playerIndex)) return;

		Slots[playerIndex].Team = team;
		Slots[playerIndex].TeamNameStringId = teamNameStringId;
	}

	public void SetGold(int playerIndex, float gold)
	{
		if (!IsValidSlot(playerIndex)) return;

		Slots[playerIndex].Gold = gold;
	}

	public void SetWood(int playerIndex, float wood)
	{
		if (!IsValidSlot(playerIndex)) return;

		Slots[playerIndex].Wood = wood;
	}

	public void SetStone(int playerIndex, float stone)
	{
		if (!IsValidSlot(playerIndex)) return;

		Slots[playerIndex].Stone = stone;
	}

	public void SetKills(int playerIndex, int kills)
	{
		if (!IsValidSlot(playerIndex)) return;

		Slots[playerIndex].KillCount = kills;
	}
}
