using System.Runtime.InteropServices;

namespace Realm.Ecs.Components.Tags;

/// <summary>
///     Tag marking a building that was reduced to zero health and is waiting to be repaired: it is invulnerable, its
///     charges stop recharging, it produces nothing and takes no deliveries until it stands whole again.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public readonly record struct Dormant;
