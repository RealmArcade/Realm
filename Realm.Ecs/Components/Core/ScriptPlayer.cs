using System.Runtime.InteropServices;

namespace Realm.Ecs.Components.Core;

/// <summary>
/// Represents the configuration and runtime properties of a player slot.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct ScriptPlayer
{
	public float Gold;
	public float Wood;
	public float Stone;
	public byte ActiveByte;
	public byte IsComputerByte;
	public byte Reserved0;
	public byte Reserved1;
	public int Team;
	public int NameStringId;
	public int KillCount;

	public bool Active
	{
		get => ActiveByte != 0;
		set => ActiveByte = value ? (byte)1 : (byte)0;
	}

	public bool IsComputer
	{
		get => IsComputerByte != 0;
		set => IsComputerByte = value ? (byte)1 : (byte)0;
	}

	public string Name
	{
		get => StringRegistry.Get(NameStringId);
		set => NameStringId = StringRegistry.GetOrAdd(value);
	}
}
