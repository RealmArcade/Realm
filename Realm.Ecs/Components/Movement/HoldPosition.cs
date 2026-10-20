using System.Runtime.InteropServices;
namespace Realm.Ecs.Components.Movement;

/// <summary>
///     Tag indicating the entity is holding its position and should not move to chase targets.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public readonly record struct HoldPosition;