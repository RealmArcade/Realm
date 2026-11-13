using System.Text.Json.Serialization;
using Realm.Shared.Serialization;

namespace Realm.Shared.Metadata;

[SchemaDescription("One animation a unit plays for a state, together with the attachments it should be holding while that animation runs.")]
[JsonConverter(typeof(UnitAnimationEntryJsonConverter))]
public struct UnitAnimationEntry
{
	[SchemaDescription("The animation asset played, or the name of an animation the model already carries.")]
	[SchemaAsset(new[] { "ranim" }, new[] { "Assets/animations" })]
	public string Animation { get; set; }

	[SchemaDescription("The attachment placed in the unit's right hand while this animation runs, which lets a unit sheathe a weapon for a bow shot.")]
	[SchemaReference("Attachments")]
	public string? RightHandAttachment { get; set; }

	[SchemaDescription("The attachment placed in the unit's left hand while this animation runs.")]
	[SchemaReference("Attachments")]
	public string? LeftHandAttachment { get; set; }
}
