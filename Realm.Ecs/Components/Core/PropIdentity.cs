namespace Realm.Ecs.Components.Core;

/// <summary>
///     Identifies the type category of a prop entity (e.g. "tree", "rock", "goldmine", "pillar", "flag"),
///     decoupling the prop's logical identity from its Godot scene node.
/// </summary>
public record struct PropIdentity(string PropId);
