using Realm.Shared.Terrain;

namespace Realm.Shared.Metadata;

public class BillboardIconMetadata
{
	public string TexturePath { get; set; } = string.Empty;
	public int Columns { get; set; } = 1;
	public int Rows { get; set; } = 1;
	public float FramesPerSecond { get; set; } = 12.0f;
	public bool Loop { get; set; } = true;
	public string Socket { get; set; } = "Overhead";
	public Vector3Data PositionOffset { get; set; }
	public float WorldHeight { get; set; } = 1.0f;
	public bool KeepConstantScreenSize { get; set; } = true;
	public bool NoDepthTest { get; set; } = false;

	public BillboardIconMetadata Clone()
	{
		return new BillboardIconMetadata
		{
			TexturePath = TexturePath,
			Columns = Columns,
			Rows = Rows,
			FramesPerSecond = FramesPerSecond,
			Loop = Loop,
			Socket = Socket,
			PositionOffset = PositionOffset,
			WorldHeight = WorldHeight,
			KeepConstantScreenSize = KeepConstantScreenSize,
			NoDepthTest = NoDepthTest
		};
	}
}
