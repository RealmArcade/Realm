using Realm.Shared.Serialization;

namespace Realm.Shared.Metadata;

[SchemaDescription("One team a map seats players on, which decides who is allied with whom.")]
public class MapTeamConfig
{
	[SchemaDescription("The name the team shows in the lobby and on the scoreboard.")]
	public string TeamName { get; set; } = string.Empty;

	[SchemaDescription("The slots that make up the team. Slots on the same team start allied.")]
	public List<int> Slots { get; set; } = new();
}
