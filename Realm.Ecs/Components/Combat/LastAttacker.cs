using System.Runtime.InteropServices;

namespace Realm.Ecs.Components.Combat;

/// <summary>
///     Represents the entity that last attacked/damaged this entity.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public record struct LastAttacker(Entity Value);