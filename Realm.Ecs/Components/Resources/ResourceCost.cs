using Realm.Ecs.Common;

namespace Realm.Ecs.Components.Resources;

/// <summary>
///     Specifies a cost in terms of a specific resource.
/// </summary>
public record struct ResourceCost(ResourceId ResourceTypeId, int Amount);