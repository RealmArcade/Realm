using Realm.Shared.Serialization;

namespace Realm.Shared.Terrain;

public class MapSaveData
{
	public int Width { get; set; }
	public int Depth { get; set; }

	public List<UnitSaveData>? Units { get; set; }
	public List<PropSaveData>? Props { get; set; }
	public List<DecalSaveData>? Decals { get; set; }
	public List<VfxSaveData>? Vfx { get; set; }
	public float? CameraBoundsLeft { get; set; }
	public float? CameraBoundsRight { get; set; }
	public float? CameraBoundsTop { get; set; }
	public float? CameraBoundsBottom { get; set; }
	public string? SkyboxPath { get; set; }
	public List<CoordinateSaveData>? Coordinates { get; set; }

	public static string GenerateJsonSchema()
	{
		return RealmJsonSchemaExporter.GenerateJsonSchema(typeof(MapSaveData));
	}
}
