namespace Realm.Shared.Animation;

public class RanimRenderOptions
{
	public int Width { get; set; } = 128;
	public int Height { get; set; } = 128;
	public float Fps { get; set; } = 12.0f;
	public int? MaxFrameCount { get; set; }
	public RanimOutputFormat Format { get; set; } = RanimOutputFormat.Webp;
	public float Scale { get; set; } = 1.0f;
	public bool DrawBorder { get; set; } = true;
	public bool DrawShadow { get; set; } = true;
	public string? ModelPath { get; set; }
	public byte[]? ModelBytes { get; set; }
	public int Quality { get; set; } = 95;
	public bool Lossless { get; set; }
}