namespace Realm.Shared;

public struct GlbMetadata
{
	public bool IsOptimized { get; set; }
	public string? RealmVersion { get; set; }
	public int MeshCount { get; set; }
	public int NodeCount { get; set; }
	public int MaterialCount { get; set; }
	public int ImageCount { get; set; }
	public int TotalTriangles { get; set; }
}