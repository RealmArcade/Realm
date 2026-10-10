namespace Realm.Shared.Metadata;

public class ItemMetadata
{
	public string TemplateID { get; set; } = string.Empty;
	public string Name { get; set; } = string.Empty;
	public string Description { get; set; } = string.Empty;
	public string ItemClass { get; set; } = "consumable";
	public float CostGold { get; set; }
	public string? UseAbility { get; set; }
	public int ChargeCount { get; set; }
	public string? CooldownLink { get; set; }
	public bool CanDrop { get; set; }
	public int ItemLevel { get; set; }
	public string? IconPath { get; set; }
	public string[]? PassiveStatusEffects { get; set; }
	public string[]? GrantedWeapons { get; set; }
	public bool IsContainer { get; set; }
	public int ContainerSize { get; set; }
	public string Requirements { get; set; } = string.Empty;
}