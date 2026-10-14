namespace Realm.Ecs.AI.Genres;

public class AutoBattlerUnitDef
{
	public string Id { get; set; } = "warrior_tier1";
	public int Cost { get; set; } = 1;
	public string PrimaryTrait { get; set; } = "Warrior";
	public string OriginTrait { get; set; } = "Human";
	public bool IsFrontline { get; set; } = true;
}