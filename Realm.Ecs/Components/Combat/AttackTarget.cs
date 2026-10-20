using System.Runtime.InteropServices;

namespace Realm.Ecs.Components.Combat;

/// <summary>
///     An intent component that signals an entity should attack a specific target.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public record struct AttackTarget(Entity Target);