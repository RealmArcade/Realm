namespace Realm.Ecs.Components.Combat;

/// <summary>
///     Defines the flat and rated armor values and armor type of an entity.
/// </summary>
public record struct Armor(
	float FlatArmor,
	float RatedArmor = 0f,
	string ArmorType = "unarmored")
{
	public readonly float Value => FlatArmor;
}
