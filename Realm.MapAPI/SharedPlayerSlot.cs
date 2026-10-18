using System.Runtime.InteropServices;

namespace Realm.MapAPI;

/// <summary>
/// Fixed-layout, blittable structure representing shared player state in WebAssembly linear memory.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct SharedPlayerSlot
{
	/// <summary>
	/// Indicates whether this player slot is active (1 if active, 0 otherwise).
	/// </summary>
	public byte IsActive;

	/// <summary>
	/// Indicates whether this player slot is controlled by computer AI (1 if computer, 0 otherwise).
	/// </summary>
	public byte IsComputer;

	/// <summary>
	/// Reserved alignment padding byte.
	/// </summary>
	public byte Reserved0;

	/// <summary>
	/// Reserved alignment padding byte.
	/// </summary>
	public byte Reserved1;

	/// <summary>
	/// The team index assigned to this player slot.
	/// </summary>
	public int Team;

	/// <summary>
	/// The current gold resource amount for this player slot.
	/// </summary>
	public float Gold;

	/// <summary>
	/// The current wood resource amount for this player slot.
	/// </summary>
	public float Wood;

	/// <summary>
	/// The current stone resource amount for this player slot.
	/// </summary>
	public float Stone;

	/// <summary>
	/// The current population count consumed by living units for this player slot.
	/// </summary>
	public int CurrentPopulation;

	/// <summary>
	/// The maximum population supply limit for this player slot.
	/// </summary>
	public int MaxPopulation;

	/// <summary>
	/// The total number of enemy unit kills credited to this player slot.
	/// </summary>
	public int Kills;
}
