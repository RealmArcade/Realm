namespace Realm.Ecs.Components.Stats;

/// <summary>
/// Represents the 6 core RPG attributes for an entity.
/// </summary>
internal record struct UnitAttributes(
	float Vitality = 0f,
	float Might = 0f,
	float Agility = 0f,
	float Finesse = 0f,
	float Focus = 0f,
	float Willpower = 0f)
{
	public readonly float VIT => Vitality;
	public readonly float MIG => Might;
	public readonly float AGI => Agility;
	public readonly float FIN => Finesse;
	public readonly float FOC => Focus;
	public readonly float WIL => Willpower;
}
