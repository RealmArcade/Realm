using System.Runtime.CompilerServices;

namespace Realm.Ecs.Components.Core;

/// <summary>
/// Holds the fixed number of visual effect requests one tick can queue, sized for the busiest fight a map can stage.
/// </summary>
[InlineArray(Capacity)]
public struct VFXSlotArray
{
	public const int Capacity = 32;

	private VFXRequest _first;
}
