using Arch.Core;

namespace Realm.Ecs.Components.Combat;

/// <summary>
///     An intent component that signals a healer entity should heal a specific friendly target.
/// </summary>
public record struct HealingTarget(Entity Target);
