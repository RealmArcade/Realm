using Realm.Shared.Serialization;

namespace Realm.Shared.Metadata;

[SchemaDescription("One seat a map offers: which slot it is, what it is called and who may take it.")]
public class MapPlayerSlotConfig
{
	[SchemaDescription("The slot number a team's slot list and a placed unit refer to.")]
	public int SlotId { get; set; }

	[SchemaDescription("The name the slot shows in the lobby instead of a player name.")]
	public string Name { get; set; } = string.Empty;

	[SchemaDescription("The colour the slot's units, minimap markers and selection rings are drawn in.")]
	[SchemaControl(RealmSchemaControl.Color)]
	public string Color { get; set; } = string.Empty;

	[SchemaDescription("The faction the slot plays, which decides which templates it may train.")]
	public string Faction { get; set; } = string.Empty;

	[SchemaDescription("Whether the slot is taken by a person or played by the computer.")]
	public string Controller { get; set; } = "HumanPlayer";

	[SchemaDescription("Which computer player personality fills the slot when it is played by the computer.")]
	public string? AiType { get; set; }

	[SchemaDescription("The named coordinate the slot's starting units are placed at.")]
	public string? StartLocation { get; set; }

	[SchemaDescription("The decal drawn on the ground under the slot's starting area.")]
	[SchemaDictionaryReference("decals")]
	public string? CustomDecal { get; set; }
}
