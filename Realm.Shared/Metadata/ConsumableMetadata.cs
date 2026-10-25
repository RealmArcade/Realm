namespace Realm.Shared.Metadata;

/// <summary>
/// A purchase a building sells and binds to one unit it is aimed at. The card the player clicks is
/// an ordinary ability template; this record only carries the commercial rules, so an ability can be
/// a consumable for one map and a plain spell for another.
/// </summary>
public class ConsumableMetadata
{
	public string TemplateID { get; set; } = string.Empty;

	/// <summary>Ability whose cast is this purchase. Defaults to the template id.</summary>
	public string? AbilityId { get; set; }

	public float CostGold { get; set; }
	public float CostWood { get; set; }
	public float CostStone { get; set; }

	/// <summary>OwnUnit, AnyUnit, AllyUnit or SelfBuilding.</summary>
	public string TargetScope { get; set; } = "OwnUnit";

	/// <summary>Units carry one attachment per group; a second purchase in the same group is refused.</summary>
	public string SlotGroup { get; set; } = "consumable";

	public int MaxPerUnit { get; set; } = 1;

	/// <summary>When true the purchase empties the group instead of filling it, which is how a purge is authored.</summary>
	public bool ClearsSlot { get; set; }

	public float PurchaseRange { get; set; } = 4f;

	public string CardId => string.IsNullOrWhiteSpace(AbilityId) ? TemplateID : AbilityId!;
}
