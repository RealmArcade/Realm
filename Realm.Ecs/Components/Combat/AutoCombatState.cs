using System.Runtime.InteropServices;
namespace Realm.Ecs.Components.Combat;

/// <summary>
///     Stores native automated combat AI settings for a unit.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct AutoCombatState
{
	public byte EnabledByte;

	public bool Enabled
	{
		get => EnabledByte != 0;
		set => EnabledByte = value ? (byte)1 : (byte)0;
	}

	/// <summary>
	///     Initializes a new instance of the <see cref="AutoCombatState"/> struct.
	/// </summary>
	/// <param name="enabled">Whether auto-combat is enabled.</param>
	public AutoCombatState(bool enabled)
	{
		EnabledByte = enabled ? (byte)1 : (byte)0;
	}
}
