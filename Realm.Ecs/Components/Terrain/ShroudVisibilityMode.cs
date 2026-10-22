namespace Realm.Ecs.Components.Terrain;

/// <summary>
/// Selects how unseen terrain is hidden: ExplorationShroud keeps a permanent exploration memory, VisionShroud starts with the whole map explored so only live vision dims terrain, and Revealed disables the shroud entirely.
/// </summary>
public enum ShroudVisibilityMode : byte
{
	ExplorationShroud = 0,
	VisionShroud = 1,
	Revealed = 2
}
