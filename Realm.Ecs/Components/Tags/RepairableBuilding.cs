using System.Runtime.InteropServices;

namespace Realm.Ecs.Components.Tags;

/// <summary>
///     Tag marking a building the map lets fall dormant at zero health instead of being destroyed, so a repairing unit
///     can raise it back to working order. A building without it dies the moment its health runs out.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public readonly record struct RepairableBuilding;
