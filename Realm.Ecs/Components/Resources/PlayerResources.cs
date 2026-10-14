using Realm.Ecs.Common;

namespace Realm.Ecs.Components.Resources;

/// <summary>
///     A component holding all resource amounts for a player entity.
/// </summary>
public record struct PlayerResources(Dictionary<ResourceId, int> Value);