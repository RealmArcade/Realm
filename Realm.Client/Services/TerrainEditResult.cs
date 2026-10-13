namespace Realm.Client.Services;

public struct TerrainEditResult
{
	public bool HeightsModified;
	public bool SplatModified;
	public bool PathingModified;
	public int MinX;
	public int MinZ;
	public int MaxX;
	public int MaxZ;
}