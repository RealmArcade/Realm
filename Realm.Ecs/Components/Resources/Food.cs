using Realm.Ecs.Common;

namespace Realm.Ecs.Components.Resources;

/// <summary>
///     Represents the Food resource.
/// </summary>
[ResourceDefinition("Food", "Food", "Sustains units.", "res://icons/food.png")]
public readonly record struct Food;