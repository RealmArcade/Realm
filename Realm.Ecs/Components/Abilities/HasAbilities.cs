using System.Runtime.InteropServices;
namespace Realm.Ecs.Components.Abilities;

/// <summary>
///     A tag component indicating that an entity has abilities.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public readonly record struct HasAbilities;