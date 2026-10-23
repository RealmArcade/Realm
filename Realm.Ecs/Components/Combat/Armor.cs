using System.Runtime.InteropServices;
using Realm.MapAPI;

namespace Realm.Ecs.Components.Combat;

/// <summary>
/// Defines flat and rated armor mitigation values and armor category for an entity.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct Armor
{
	public float FlatArmor;
	public float RatedArmor;
	public int StringId;

	public Armor(float flatArmor, float ratedArmor = 0f, string armorType = "unarmored")
	{
		FlatArmor = flatArmor;
		RatedArmor = ratedArmor;
		StringId = StringRegistry.GetOrAdd(armorType);
	}

	public readonly float Value => FlatArmor;

	public string ArmorType
	{
		get => StringRegistry.Get(StringId);
		set => StringId = StringRegistry.GetOrAdd(value);
	}
}
