namespace Realm.MapAPI;

/// <summary>
/// Represents persistent hero progression state, including level, experience, skill points, and stats.
/// </summary>
public class HeroSaveData
{
	/// <summary>
	/// Gets or sets the archetype or unit type identifier of the hero.
	/// </summary>
	public string HeroTypeId { get; set; } = string.Empty;

	/// <summary>
	/// Gets or sets the display name of the hero.
	/// </summary>
	public string Name { get; set; } = string.Empty;

	/// <summary>
	/// Gets or sets the current level of the hero.
	/// </summary>
	public int Level { get; set; } = 1;

	/// <summary>
	/// Gets or sets the accumulated experience points of the hero.
	/// </summary>
	public float Experience { get; set; }

	/// <summary>
	/// Gets or sets the number of unspent or allocated skill points.
	/// </summary>
	public int SkillPoints { get; set; }

	/// <summary>
	/// Gets or sets the current health points of the hero.
	/// </summary>
	public float Health { get; set; }

	/// <summary>
	/// Gets or sets the maximum health points of the hero.
	/// </summary>
	public float MaxHealth { get; set; }

	/// <summary>
	/// Gets or sets the current mana points of the hero.
	/// </summary>
	public float Mana { get; set; }

	/// <summary>
	/// Gets or sets the maximum mana capacity of the hero.
	/// </summary>
	public float MaxMana { get; set; }

	/// <summary>
	/// Gets or sets the base attack damage of the hero.
	/// </summary>
	public float Damage { get; set; }

	/// <summary>
	/// Gets or sets the armor rating of the hero.
	/// </summary>
	public float Armor { get; set; }

	/// <summary>
	/// Gets or sets the movement speed of the hero.
	/// </summary>
	public float Speed { get; set; }

	/// <summary>
	/// Gets or sets custom arbitrary key-value metadata associated with the hero.
	/// </summary>
	public Dictionary<string, string> CustomData { get; set; } = new();
}