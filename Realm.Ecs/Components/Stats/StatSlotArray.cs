using System.Runtime.CompilerServices;

namespace Realm.Ecs.Components.Stats;

/// <summary>
/// Holds the fixed number of stats one entity can expose, sized for the busiest hand authored map.
/// </summary>
[InlineArray(Capacity)]
public struct StatSlotArray
{
	public const int Capacity = 32;

	private StatSlot _first;
}
