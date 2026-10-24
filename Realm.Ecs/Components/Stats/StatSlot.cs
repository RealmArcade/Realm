using System.Runtime.InteropServices;

namespace Realm.Ecs.Components.Stats;

/// <summary>
/// Describes one stat and the value an entity currently has for it.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct StatSlot
{
	public int StatStringId;
	public float Value;
}
