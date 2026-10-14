namespace Realm.Shared.Terrain;

public class CoordinateSaveData
{
	public string Name { get; set; } = string.Empty;
	public float MinX { get; set; }
	public float MinZ { get; set; }
	public float MaxX { get; set; }
	public float MaxZ { get; set; }
}