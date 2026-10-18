using System.Runtime.InteropServices;
namespace Realm.Ecs.Components.Tags;

/// <summary>
///     Tag marking a building entity as currently under construction and not yet fully built.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public readonly record struct UnderConstruction;