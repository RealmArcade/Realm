using System.Text.Json.Serialization;
using Realm.Shared.Terrain;

namespace Realm.Shared.Metadata;

public class HandAttachmentOrientation
{
	public float PositionX { get; set; }
	public float PositionY { get; set; }
	public float PositionZ { get; set; }
	public float PitchX { get; set; }
	public float YawY { get; set; }
	public float RollZ { get; set; }
	public float Scale { get; set; }
	public float ScaleX { get; set; }
	public float ScaleY { get; set; }
	public float ScaleZ { get; set; }
	public float NormalOffset { get; set; }
	public string? ParentAttachmentId { get; set; }

	[JsonIgnore]
	public Vector3Data Position => new(PositionX, PositionY, PositionZ);
	[JsonIgnore]
	public Vector3Data RotationDegrees => new(PitchX, YawY, RollZ);
	[JsonIgnore]
	public Vector3Data ScaleVector => new(
		ScaleX > 0.0001f ? ScaleX : (Scale > 0f ? Scale : 1.0f),
		ScaleY > 0.0001f ? ScaleY : (Scale > 0f ? Scale : 1.0f),
		ScaleZ > 0.0001f ? ScaleZ : (Scale > 0f ? Scale : 1.0f));

	public HandAttachmentOrientation Clone() => new()
	{
		PositionX = PositionX,
		PositionY = PositionY,
		PositionZ = PositionZ,
		PitchX = PitchX,
		YawY = YawY,
		RollZ = RollZ,
		Scale = Scale,
		ScaleX = ScaleX,
		ScaleY = ScaleY,
		ScaleZ = ScaleZ,
		NormalOffset = NormalOffset,
		ParentAttachmentId = ParentAttachmentId
	};
}