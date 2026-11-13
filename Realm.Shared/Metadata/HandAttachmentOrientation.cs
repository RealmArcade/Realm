using System.Text.Json.Serialization;
using Realm.Shared.Serialization;
using Realm.Shared.Terrain;

namespace Realm.Shared.Metadata;

[SchemaDescription("How one attachment is placed on the socket it is carried on, both where it sits and how it is turned and scaled.")]
public class HandAttachmentOrientation
{
	[SchemaDescription("How far the attachment is moved along the socket's sideways axis.")]
	[SchemaUnit("m")]
	public float PositionX { get; set; }

	[SchemaDescription("How far the attachment is moved up or down from the socket.")]
	[SchemaUnit("m")]
	public float PositionY { get; set; }

	[SchemaDescription("How far the attachment is moved along the socket's forward axis.")]
	[SchemaUnit("m")]
	public float PositionZ { get; set; }

	[SchemaDescription("How far the attachment is tipped forwards or backwards on the socket.")]
	[SchemaUnit("°")]
	public float PitchX { get; set; }

	[SchemaDescription("How far the attachment is turned around the socket.")]
	[SchemaUnit("°")]
	public float YawY { get; set; }

	[SchemaDescription("How far the attachment is rolled around its own forward axis.")]
	[SchemaUnit("°")]
	public float RollZ { get; set; }

	[SchemaDescription("The multiplier applied to the attachment on every axis, used when the per-axis scale fields are left at zero.")]
	public float Scale { get; set; }

	[SchemaDescription("The multiplier applied to the attachment's width, which overrides the uniform scale when it is set.")]
	public float ScaleX { get; set; }

	[SchemaDescription("The multiplier applied to the attachment's height, which overrides the uniform scale when it is set.")]
	public float ScaleY { get; set; }

	[SchemaDescription("The multiplier applied to the attachment's depth, which overrides the uniform scale when it is set.")]
	public float ScaleZ { get; set; }

	[SchemaDescription("How far the attachment is lifted along the surface normal, so it does not sink into what it is held against.")]
	[SchemaUnit("m")]
	public float NormalOffset { get; set; }

	[SchemaDescription("The attachment this one hangs off, which lets a scope sit on a rifle rather than on the hand.")]
	[SchemaReference("Attachments")]
	public string? ParentAttachmentId { get; set; }

	[JsonIgnore] public Vector3Data Position => new(PositionX, PositionY, PositionZ);
	[JsonIgnore] public Vector3Data RotationDegrees => new(PitchX, YawY, RollZ);

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
