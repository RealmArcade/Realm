using System.Runtime.CompilerServices;

namespace Realm.Ecs.Components.Core;

/// <summary>
/// Holds one slot per script player addressable by the map API, covering slots zero through twenty four.
/// </summary>
[InlineArray(SlotCount)]
public struct PlayerSlotArray
{
	public const int SlotCount = 25;

	private ScriptPlayer _first;
}
