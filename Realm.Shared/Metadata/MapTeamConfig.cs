namespace Realm.Shared.Metadata;

public class MapTeamConfig
{
	public string TeamName { get; set; } = string.Empty;
	public List<int> Slots { get; set; } = new();
}