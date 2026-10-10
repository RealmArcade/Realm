namespace Realm.Ecs.Components.Terrain;

/// <summary>
///     Bit flags describing how a terrain cell can be traversed or used.
///     Stored as raw <see cref="int"/> values in <see cref="TerrainState.PathingCodes"/>.
///     Keep the bit values in sync with the pathing overlay shader in <c>EditableTerrain</c>.
/// </summary>
[Flags]
public enum TerrainPathingFlags
{
	None = 0,
	ShallowWater = 1 << 0,
	DeepWater = 1 << 1,
	Flying = 1 << 2,
	Ground = 1 << 3,
	Buildable = 1 << 5,
	All = 0xFF,
}

