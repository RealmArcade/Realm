namespace Realm.Shared.Animation;

public class RanimRenderFrame
{
	public int Width { get; set; }
	public int Height { get; set; }
	public float Time { get; set; }
	public byte[] RgbaBytes { get; set; } = Array.Empty<byte>();
}