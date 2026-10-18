using System.Runtime.InteropServices;

namespace Realm.Ecs.Components.Combat;

/// <summary>
///     An intent component that signals a healer entity should heal a specific friendly target.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public record struct HealingTarget(Entity Target);