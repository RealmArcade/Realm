namespace Realm.Ecs.Components.Core;

/// <summary>
///     Represents the faction allegiance of a unit entity, distinguishing player-controlled
///     units from enemy-controlled units without coupling to Godot node state.
/// </summary>
public record struct UnitFaction(bool IsEnemy);
