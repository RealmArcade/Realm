using Realm.Ecs.Common;

namespace Realm.Ecs.Components.Resources;

/// <summary>
///     Represents the Gold resource.
/// </summary>
[ResourceDefinition("Gold", "Gold", "A primary currency.", "res://icons/gold.png")]
public readonly record struct Gold;