namespace Realm.Ecs.AI.Genres;

public class TowerTypeDefinition
{
	public string Id { get; set; } = "arrow_tower";
	public int Cost { get; set; } = 100;
	public float Damage { get; set; } = 25f;
	public float Range { get; set; } = 15f;
	public float AttackSpeed { get; set; } = 1.0f;
	public string DamageType { get; set; } = "Physical";
	public bool HasSlow { get; set; } = false;
	public string? NextUpgradeId { get; set; }
	public int UpgradeCost { get; set; } = 150;
}