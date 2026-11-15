using Realm.Shared.Serialization;
using Realm.Shared.Terrain;

namespace Realm.Shared.Metadata;

[SchemaDescription("A model a unit carries on a hand or a named socket, such as a sword, a shield or a banner, together with the effect that hangs off it.")]
public class AttachmentMetadata
{
	[SchemaDescription("The identifier a unit's object attachment table names this attachment by.")]
	[SchemaSensitive]
	public string AttachmentId { get; set; } = string.Empty;

	[SchemaDescription("The name the attachment shows when a player inspects what a unit is carrying.")]
	public string Name { get; set; } = string.Empty;

	[SchemaDescription("Path to the model drawn in the unit's hand or on its socket.")]
	[SchemaAsset(new[] { "rmesh" }, new[] { "Assets/models/items" })]
	public string ModelPath { get; set; } = string.Empty;

	[SchemaDescription("The multiplier applied to the attachment model's size so it fits the hand it is held in.")]
	public float Scale { get; set; } = 1.0f;

	[SchemaDescription("Where the attachment sits relative to the hand or socket it is parented to.")]
	public Vector3Data PositionOffset { get; set; }

	[SchemaDescription("How the attachment is turned relative to the hand or socket it is parented to, in degrees around each axis.")]
	public Vector3Data RotationOffset { get; set; }

	[SchemaDescription("The hand the attachment is held in when a unit's attachment table does not say otherwise.")]
	public string DefaultHand { get; set; } = "RightHand";

	[SchemaDescription("The visual effect that hangs off the attachment, such as the flame on a burning blade.")]
	[SchemaReference("Vfx")]
	public string? ChildVfxId { get; set; }

	[SchemaDescription("Whether the player's colour is left out of the attachment instead of being tinted over it.")]
	public bool IgnorePlayerColor { get; set; }

	[SchemaDescription("Where the attachment's effect sits relative to the attachment model.")]
	public Vector3Data ChildVfxPosition { get; set; }

	[SchemaDescription("How the attachment's effect is turned relative to the attachment model, in degrees around each axis.")]
	public Vector3Data ChildVfxRotation { get; set; }

	[SchemaDescription("The multiplier applied to the attachment's effect size.")]
	public Vector3Data ChildVfxScale { get; set; } = Vector3Data.One;

	[SchemaDescription("A sprite sheet icon drawn over the carrying unit, such as a status marker or an overhead badge.")]
	public BillboardIconMetadata? BillboardIcon { get; set; }

	[SchemaDescription("The map the attachment was imported from, kept so attribution survives a round trip through the editor.")]
	public string? Attribution_ImportedFromMap { get; set; }
}
