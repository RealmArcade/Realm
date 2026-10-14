using Realm.Ecs.Common;

namespace Realm.Ecs.Components.Resources;

/// <summary>
///     Represents the Wood resource.
/// </summary>
[ResourceDefinition("Wood", "Wood", "A building material.", "res://icons/wood.png")]
public readonly record struct Wood;