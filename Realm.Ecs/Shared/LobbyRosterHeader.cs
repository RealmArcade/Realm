using System.Runtime.InteropServices;

namespace Realm.Ecs;

/// <summary>
///     The head of the lobby roster a map script reads from shared memory: how many player slots the lobby leaves open,
///     how many factions its map offers, how many teams its seats are numbered up to, and a counter that moves every time
///     the authority restates the roster.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct LobbyRosterHeader
{
	/// <summary>The number of player slots this lobby leaves open to a player.</summary>
	public int OpenSlotCount;

	/// <summary>The number of factions in the lobby's faction table.</summary>
	public int FactionCount;

	/// <summary>The number of teams this lobby offers, which is the highest team number a seat may hold.</summary>
	public int TeamCount;

	/// <summary>How many times the authority has restated the roster, so a reader can tell a fresh one from a stale one.</summary>
	public uint Revision;

	/// <summary>The number of player slots shared memory carries, which is what a reader may index up to.</summary>
	public int SlotCount;
}
