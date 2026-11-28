namespace Realm.MapGeneration;

public class TerrainReconstructionOptions
{
	public int Width { get; set; } = 128;

	public int Depth { get; set; } = 128;

	public float TierHeight { get; set; } = 3f;

	public float BaseHeightTier { get; set; } = 1f;

	public float QuadSize { get; set; } = 2f;

	public int Seed { get; set; }
	public float WaterSurfaceDrop { get; set; } = 0.35f;

	public float MinimumLakeFraction { get; set; } = 0.0006f;

	public float MinimumWaterFraction { get; set; } = 0.01f;

	public bool PlaceProps { get; set; } = true;

	public bool UseCellColourSplats { get; set; }

	public bool QuantizeHeights { get; set; } = true;
}
