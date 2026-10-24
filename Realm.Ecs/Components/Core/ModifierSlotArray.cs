using System.Runtime.CompilerServices;
using Realm.Ecs.Components.Stats;

namespace Realm.Ecs.Components.Core;

/// <summary>
/// Holds the fixed number of stat modifiers one entity can stack, sized for the busiest hand authored map.
/// </summary>
[InlineArray(Capacity)]
public struct ModifierSlotArray
{
	public const int Capacity = 64;

	private StatModifier _first;
}
