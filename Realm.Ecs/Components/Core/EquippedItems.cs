namespace Realm.Ecs.Components.Core;

/// <summary>
/// Holds identifiers of items currently equipped on an entity.
/// </summary>
public record struct EquippedItems(List<string> Value);
