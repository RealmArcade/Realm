namespace Realm.Ecs.Components.Terrain;

/// <summary>
///     The pathing code a terrain cell carries when a map authored nothing for it, derived from the cell's water
///     state. The client's editor and the authoritative process both derive codes here so a cell that was never
///     painted is traversable identically in both.
/// </summary>
public static class TerrainPathing
{
	public static readonly int ShallowWaterCode = (int)TerrainPathingFlags.ShallowWater | (int)TerrainPathingFlags.Flying;
	public static readonly int DeepWaterCode = (int)TerrainPathingFlags.DeepWater | (int)TerrainPathingFlags.Flying;
	public static readonly int GroundCode = (int)TerrainPathingFlags.Ground | (int)TerrainPathingFlags.Buildable | (int)TerrainPathingFlags.Flying;

	public static int DefaultCode(WaterType waterMode)
	{
		return waterMode switch
		{
			WaterType.Shallow => ShallowWaterCode,
			WaterType.Deep => DeepWaterCode,
			_ => GroundCode
		};
	}
}
