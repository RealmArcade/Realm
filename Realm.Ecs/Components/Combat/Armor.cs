using System.Runtime.InteropServices;

namespace Realm.Ecs.Components.Combat;

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
