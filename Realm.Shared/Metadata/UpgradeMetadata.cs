namespace Realm.Shared.Metadata;

public class UpgradeMetadata
{
	public string TemplateID { get; set; } = string.Empty;
	public string Name { get; set; } = string.Empty;
	public string Description { get; set; } = string.Empty;
	public string? IconPath { get; set; }
	public float CostGold { get; set; }
	public float CostWood { get; set; }
	public float CostStone { get; set; }
	public float ResearchTime { get; set; }
	public string Requirement { get; set; } = string.Empty;
	public int MaxLevel { get; set; } = 1;
	public string[]? AffectedUnitIds { get; set; }
	public float MaxHpBonus { get; set; }
	public float DamageBonus { get; set; }
	public float ArmorBonus { get; set; }
	public float SpeedBonus { get; set; }
}