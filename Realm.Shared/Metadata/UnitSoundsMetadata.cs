using Realm.Shared.Serialization;

namespace Realm.Shared.Metadata;

[SchemaDescription("The voice lines and sound effects a unit plays, grouped by the moment that triggers them.")]
public class UnitSoundsMetadata
{
	[SchemaDescription("The voice set the unit speaks with, which its individual lines are drawn from.")]
	[SchemaAsset(new[] { "raud" }, new[] { "Assets/audio/sfx" })]
	public string? VoiceAsset { get; set; }

	[SchemaDescription("The lines played when the unit is selected.")]
	[SchemaAsset(new[] { "raud" }, new[] { "Assets/audio/sfx" })]
	public string[]? OnSelect { get; set; }

	[SchemaDescription("The lines played when the unit is ordered to move.")]
	[SchemaAsset(new[] { "raud" }, new[] { "Assets/audio/sfx" })]
	public string[]? OnMoveOrder { get; set; }

	[SchemaDescription("The lines played when the unit is ordered to attack.")]
	[SchemaAsset(new[] { "raud" }, new[] { "Assets/audio/sfx" })]
	public string[]? OnAttackOrder { get; set; }

	[SchemaDescription("The lines played when the unit is wounded.")]
	[SchemaAsset(new[] { "raud" }, new[] { "Assets/audio/sfx" })]
	public string[]? OnWounded { get; set; }

	[SchemaDescription("The lines played when the unit dies.")]
	[SchemaAsset(new[] { "raud" }, new[] { "Assets/audio/sfx" })]
	public string[]? OnDeath { get; set; }

	[SchemaDescription("The lines played when the unit finishes training and is ready.")]
	[SchemaAsset(new[] { "raud" }, new[] { "Assets/audio/sfx" })]
	public string[]? OnReady { get; set; }

	[SchemaDescription("The lines played when the unit casts a spell.")]
	[SchemaAsset(new[] { "raud" }, new[] { "Assets/audio/sfx" })]
	public string[]? OnSpellCast { get; set; }
}
