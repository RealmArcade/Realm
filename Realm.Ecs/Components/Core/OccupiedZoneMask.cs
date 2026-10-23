using System.Runtime.CompilerServices;

namespace Realm.Ecs.Components.Core;

/// <summary>
/// Holds one membership bit per script zone, sized for the busiest hand authored map.
/// </summary>
[InlineArray(WordCount)]
public struct OccupiedZoneMask
{
	public const int WordCount = 32;
	public const int Capacity = WordCount * 64;

	private ulong _first;
}
