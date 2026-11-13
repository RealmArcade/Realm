using System.Text.Json.Serialization;

namespace Realm.Shared.Terrain;

public class UnitSaveData
{
	public string TemplateId { get; set; } = string.Empty;
	public float PosX { get; set; }
	public float PosY { get; set; }
	public float PosZ { get; set; }
	public float RotationY { get; set; }
	public float Scale { get; set; } = 1.0f;
	public int Player { get; set; }
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
	public int Level { get; set; }
}
