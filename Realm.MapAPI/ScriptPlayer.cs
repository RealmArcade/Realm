using System.Runtime.InteropServices;

namespace Realm.MapAPI;

/// <summary>
/// Represents the configuration and runtime properties of a player slot within a map script.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct ScriptPlayer
{
	/// <summary>Current gold resource amount.</summary>
	public float Gold;
	/// <summary>Current wood resource amount.</summary>
	public float Wood;
	/// <summary>Current stone resource amount.</summary>
	public float Stone;
	/// <summary>Backing byte indicating whether the player slot is active (1 if active, 0 otherwise).</summary>
	public byte ActiveByte;
	/// <summary>Backing byte indicating whether the player is controlled by AI (1 if AI, 0 if human).</summary>
	public byte IsComputerByte;
	/// <summary>Reserved alignment padding byte.</summary>
	public byte Reserved0;
	/// <summary>Reserved alignment padding byte.</summary>
	public byte Reserved1;
	/// <summary>Team identifier number.</summary>
	public int Team;
	/// <summary>String registry identifier for player display name.</summary>
	public int NameStringId;
	/// <summary>Total enemy unit kill count credited to this player.</summary>
	public int KillCount;

	/// <summary>
	/// Gets or sets a value indicating whether this player slot is active.
	/// </summary>
	public bool Active
	{
		get => ActiveByte != 0;
		set => ActiveByte = value ? (byte)1 : (byte)0;
	}

	/// <summary>
	/// Gets or sets a value indicating whether this player slot is controlled by computer AI.
	/// </summary>
	public bool IsComputer
	{
		get => IsComputerByte != 0;
		set => IsComputerByte = value ? (byte)1 : (byte)0;
	}

	/// <summary>
	/// Gets or sets the player display name.
	/// </summary>
	public string Name
	{
		get => StringRegistry.Get(NameStringId);
		set => NameStringId = StringRegistry.GetOrAdd(value);
	}
}
