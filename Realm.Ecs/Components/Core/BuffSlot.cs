using System.Runtime.InteropServices;

namespace Realm.Ecs.Components.Core;

/// <summary>
/// Describes one timed status effect: which effect is active, how long it has left and how long it originally lasts.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct BuffSlot
{
	public int StatusStringId;
	public float Remaining;
	public float Duration;
}
