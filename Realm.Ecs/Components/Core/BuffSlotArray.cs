using System.Runtime.CompilerServices;

namespace Realm.Ecs.Components.Core;

/// <summary>
/// Holds the fixed number of timed status slots one entity can carry, sized for the busiest hand authored map.
/// </summary>
[InlineArray(Capacity)]
public struct BuffSlotArray
{
	public const int Capacity = 32;

	private BuffSlot _first;
}
