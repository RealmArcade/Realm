using System.Runtime.InteropServices;

namespace Realm.Ecs.Components.Core;

/// <summary>
/// Tracks current and maximum food population unit capacities for a player.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct PlayerPopulation
{
	public int Current;
	public int Max;

	public PlayerPopulation(int current, int max)
	{
		Current = current;
		Max = max;
	}
}
