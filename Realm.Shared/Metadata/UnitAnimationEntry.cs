using System.Text.Json.Serialization;

namespace Realm.Shared.Metadata;

[JsonConverter(typeof(UnitAnimationEntryJsonConverter))]
public struct UnitAnimationEntry
{
	public string Animation { get; set; }
	public string? RightHandAttachment { get; set; }
	public string? LeftHandAttachment { get; set; }
}