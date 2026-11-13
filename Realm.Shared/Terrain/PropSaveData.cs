using System.Text.Json.Serialization;

namespace Realm.Shared.Terrain;

public class PropSaveData
{
	public string TemplateId { get; set; } = string.Empty;
	public float PosX { get; set; }
	public float PosY { get; set; }
	public float PosZ { get; set; }
	public float RotationY { get; set; }
	public float Scale { get; set; } = 1.0f;
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
	public int Level { get; set; }
}
