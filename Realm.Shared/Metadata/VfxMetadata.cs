namespace Realm.Shared.Metadata;

public class VfxMetadata
{
	public int Columns { get; set; } = 1;
	public int Rows { get; set; } = 1;
	public float Fps { get; set; } = 20.0f;
	public bool SubframeBlend { get; set; }
	public string? TexturePath { get; set; }
}