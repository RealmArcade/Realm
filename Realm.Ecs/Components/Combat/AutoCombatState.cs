using System.Runtime.InteropServices;
namespace Realm.Ecs.Components.Combat;

/// <summary>
///     Stores native automated combat AI settings for a unit.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct AutoCombatState
{
	/// <summary>
	///     Indicates whether automated combat AI is active for the unit.
	/// </summary>
	public bool Enabled;

	/// <summary>
	///     Initializes a new instance of the <see cref="AutoCombatState"/> struct.
	/// </summary>
	/// <param name="enabled">Whether auto-combat is enabled.</param>
	public AutoCombatState(bool enabled)
	{
		Enabled = enabled;
	}
}
