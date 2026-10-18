using System.Runtime.InteropServices;
namespace Realm.Ecs.Components.Tags;

/// <summary>
///     Tag indicating that a spawned unit bypasses the player population cap.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public readonly record struct BypassPopulationTag;