using Realm.Ecs.Common;

namespace Realm.Ecs.Components.Resources;

/// <summary>
///     Represents the Stone resource.
/// </summary>
[ResourceDefinition("Stone", "Stone", "A building material.", "res://icons/stone.png")]
public readonly record struct Stone;
