namespace Realm.MapGeneration;

public class MinimapRenderOptions
{
	public int Resolution { get; set; } = 512;

	public float QuadSize { get; set; } = 2f;

	public bool DrawProps { get; set; } = true;

	public bool DrawWater { get; set; } = true;

	public float WaterOpacity { get; set; } = 0.88f;
}
