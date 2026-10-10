using Realm.Ecs.Archetypes;

namespace Realm.Ecs.Definitions;

/// <summary>
///     Represents the complete structure of a game map definition, primarily containing
///     unit archetypes for a specific map.
/// </summary>
public class MapDefinition
{
	public List<UnitArchetype> Units { get; set; } = new();
	public MapProperties MapProperties { get; set; } = new();
}