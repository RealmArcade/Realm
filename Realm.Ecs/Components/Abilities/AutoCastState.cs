using System.Runtime.InteropServices;
namespace Realm.Ecs.Components.Abilities;

/// <summary>
///     Stores native auto-cast settings for a unit's abilities.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct AutoCastState
{
	public byte EnabledByte;

	public bool Enabled
	{
		get => EnabledByte != 0;
		set => EnabledByte = value ? (byte)1 : (byte)0;
	}

	/// <summary>
	///     Bitmask of active auto-cast ability slots.
	/// </summary>
	public uint ActiveAbilityBitmask;

	/// <summary>
	///     Initializes a new instance of the <see cref="AutoCastState"/> struct.
	/// </summary>
	/// <param name="enabled">Whether auto-cast is enabled.</param>
	/// <param name="activeAbilityBitmask">Bitmask of active ability slots.</param>
	public AutoCastState(bool enabled, uint activeAbilityBitmask = 1u)
	{
		EnabledByte = enabled ? (byte)1 : (byte)0;
		ActiveAbilityBitmask = activeAbilityBitmask;
	}
}
