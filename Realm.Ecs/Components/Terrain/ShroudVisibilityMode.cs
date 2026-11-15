namespace Realm.Ecs.Components.Terrain;

/// <summary>
/// Selects how unseen terrain is hidden: None disables the shroud entirely, Partial keeps every explored place remembered and hides only what nobody is watching, and Full keeps only the ground a player has explored.
/// </summary>
public enum ShroudVisibilityMode : byte
{
	None = 0,
	Partial = 1,
	Full = 2
}
