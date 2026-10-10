namespace Realm.Ecs.Components.Movement;

/// <summary>
///     Bitmask of terrain types this unit can traverse during pathfinding.
///     Uses the same bit layout as <see cref="Realm.Ecs.Components.Terrain.TerrainPathingFlags"/>
///     (bit 1 = shallow water, bit 2 = deep water, bit 4 = flying/air, bit 8 = ground).
/// </summary>
public record struct PathingFlags(int Value);
