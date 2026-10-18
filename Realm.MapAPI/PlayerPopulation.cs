using System.Runtime.InteropServices;

namespace Realm.MapAPI;

/// <summary>
/// Tracks the current unit population count and maximum capacity limit for a player.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct PlayerPopulation
{
	/// <summary>Current population count consumed by living units.</summary>
	public int Current;
	/// <summary>Maximum population supply capacity.</summary>
	public int Max;

	/// <summary>
	/// Initializes a new instance of the <see cref="PlayerPopulation"/> struct.
	/// </summary>
	/// <param name="current">Current population consumed.</param>
	/// <param name="max">Maximum population capacity.</param>
	public PlayerPopulation(int current, int max)
	{
		Current = current;
		Max = max;
	}
}
