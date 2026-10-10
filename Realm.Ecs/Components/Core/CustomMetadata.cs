namespace Realm.Ecs.Components.Core;

/// <summary>
///     Holds custom key-value metadata for the entity.
/// </summary>
public record struct CustomMetadata(Dictionary<string, object> Value);
