using System.Runtime.InteropServices;

namespace Realm.Ecs;

/// <summary>
///     The roster slot a lobby player occupies: the faction, team and colour the authority gave it, whether the player may
///     still change each of those, and whether anyone occupies it at all. It is published in shared memory so a map script
///     reads a slot without crossing the host boundary.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct PlayerRosterSlot
{
	/// <summary>The index into the lobby's faction table, or -1 when the slot was given no faction.</summary>
	public int FactionIndex;

	/// <summary>The team number the slot plays on, where 0 means it plays on no team.</summary>
	public int TeamIndex;

	/// <summary>The slot's colour packed as 0xRRGGBBAA, matching the colour the lobby publishes for its seat.</summary>
	public uint ColorRgba;

	/// <summary>Whether the slot's faction may no longer be changed.</summary>
	public byte FactionLocked;

	/// <summary>Whether the slot's team may no longer be changed.</summary>
	public byte TeamLocked;

	/// <summary>Whether the slot's colour may no longer be changed.</summary>
	public byte ColorLocked;

	/// <summary>Whether a player or a computer holds the slot.</summary>
	public byte Occupied;

	/// <summary>Whether the slot is held by a computer rather than by a person.</summary>
	public byte Computer;

	/// <summary>Whether the slot belongs to the player administering the lobby.</summary>
	public byte Host;

	/// <summary>Keeps the following slot four-byte aligned.</summary>
	public ushort Reserved;
}
