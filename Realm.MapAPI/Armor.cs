using System.Runtime.InteropServices;

namespace Realm.MapAPI;

/// <summary>
/// Defines flat and rated armor mitigation values and armor category for an entity.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct Armor
{
	/// <summary>Flat armor value directly mitigating damage.</summary>
	public float FlatArmor;
	/// <summary>Rated armor value providing percentage-based diminishing return damage mitigation.</summary>
	public float RatedArmor;
	/// <summary>String registry identifier for the armor category.</summary>
	public int StringId;

	/// <summary>
	/// Initializes a new instance of the <see cref="Armor"/> struct.
	/// </summary>
	/// <param name="flatArmor">Flat armor mitigation.</param>
	/// <param name="ratedArmor">Rated armor mitigation.</param>
	/// <param name="armorType">Armor category name.</param>
	public Armor(float flatArmor, float ratedArmor = 0f, string armorType = "unarmored")
	{
		FlatArmor = flatArmor;
		RatedArmor = ratedArmor;
		StringId = StringRegistry.GetOrAdd(armorType);
	}

	/// <summary>
	/// Gets the effective flat armor rating (alias for <see cref="FlatArmor"/>).
	/// </summary>
	public readonly float Value => FlatArmor;

	/// <summary>
	/// Gets or sets the armor category name.
	/// </summary>
	public string ArmorType
	{
		get => StringRegistry.Get(StringId);
		set => StringId = StringRegistry.GetOrAdd(value);
	}
}
