namespace Realm.EditorAPI;

/// <summary>
/// Describes the authoritative terrain grid dimensions and spacing used by map manipulation operations.
/// </summary>
/// <param name="Width">Number of terrain cells along the X axis.</param>
/// <param name="Depth">Number of terrain cells along the Z axis.</param>
/// <param name="QuadSize">World-unit size of one terrain cell.</param>
/// <param name="CellSize">Authoring cell size used when snapping editor placements.</param>
/// <param name="TierHeight">World-unit height of one integer height tier.</param>
public readonly record struct TerrainGridInfo(int Width, int Depth, float QuadSize, float CellSize, float TierHeight);
