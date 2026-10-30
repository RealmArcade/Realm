using System.Runtime.InteropServices;

namespace Realm.Ecs;

/// <summary>
///     The head of the lobby roster a map script reads from shared memory: how many players the lobby seats, how many
///     factions its map offers, and a counter that moves every time the authority restates the roster.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct LobbyRosterHeader
{
	/// <summary>The number of player slots this lobby seats.</summary>
	public int MaximumPlayerCount;

	/// <summary>The number of factions in the lobby's faction table.</summary>
	public int FactionCount;

	/// <summary>How many times the authority has restated the roster, so a reader can tell a fresh one from a stale one.</summary>
	public uint Revision;

	/// <summary>The number of player slots shared memory carries, which is what a reader may index up to.</summary>
	public int SlotCount;
}
