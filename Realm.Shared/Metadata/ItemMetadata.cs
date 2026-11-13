namespace Realm.Shared.Metadata;

public class ItemMetadata
{
	public string TemplateID { get; set; } = string.Empty;
	public string Name { get; set; } = string.Empty;
	public string Description { get; set; } = string.Empty;
	public string ItemClass { get; set; } = "consumable";
	public float CostGold { get; set; }
	public float CostWood { get; set; }
	public float CostStone { get; set; }
	public string TargetScope { get; set; } = "OwnUnit";
	public string SlotGroup { get; set; } = "consumable";
	public int MaxPerUnit { get; set; } = 1;
	public bool ClearsSlot { get; set; }
	public float PurchaseRange { get; set; } = 4f;
	public string? UseAbility { get; set; }
	public string? AbilityId { get; set; }

	public string CardId => string.IsNullOrWhiteSpace(AbilityId) ? TemplateID : AbilityId;
	public int ChargeCount { get; set; }
	public int MaxStacks { get; set; } = 1;
	public string? ResourceType { get; set; }
	public string? CooldownLink { get; set; }
	public bool CanDrop { get; set; }
	public int ItemLevel { get; set; }
	public string? IconPath { get; set; }
	public string? ModelPath { get; set; }
	public string[]? PassiveStatusEffects { get; set; }
	public string[]? GrantedWeapons { get; set; }
	public string[]? GrantedAbilities { get; set; }
	public string[]? GrantedUpgrades { get; set; }
	public bool IsContainer { get; set; }
	public int ContainerSize { get; set; }
	public string Requirements { get; set; } = string.Empty;

	public string? Attribution_ImportedFromMap { get; set; }
}