namespace Realm.Shared.Metadata;

public class MapPlayerSlotConfig
{
	public int SlotId { get; set; }
	public string Name { get; set; } = string.Empty;
	public string Color { get; set; } = string.Empty;
	public string Faction { get; set; } = string.Empty;
	public string Controller { get; set; } = "HumanPlayer";
	public string? AiType { get; set; }
	public string? StartLocation { get; set; }
	public string? CustomDecal { get; set; }
}