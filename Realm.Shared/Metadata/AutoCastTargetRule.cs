using System.Text.Json.Serialization;

namespace Realm.Shared.Metadata;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AutoCastTargetRule
{
	Self,
	NearestEnemy,
	MostWoundedAlly,
	LowestHealthAlly,
	AllyWithoutEffect,
}
