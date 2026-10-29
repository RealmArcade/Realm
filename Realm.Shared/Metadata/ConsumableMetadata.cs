namespace Realm.Shared.Metadata;

public class ConsumableMetadata
{
	public string TemplateID { get; set; } = string.Empty;

	public string? AbilityId { get; set; }

	public float CostGold { get; set; }
	public float CostWood { get; set; }
	public float CostStone { get; set; }

	public string TargetScope { get; set; } = "OwnUnit";

	public string SlotGroup { get; set; } = "consumable";

	public int MaxPerUnit { get; set; } = 1;

	public bool ClearsSlot { get; set; }

	public float PurchaseRange { get; set; } = 4f;

	public string CardId => string.IsNullOrWhiteSpace(AbilityId) ? TemplateID : AbilityId!;
}
