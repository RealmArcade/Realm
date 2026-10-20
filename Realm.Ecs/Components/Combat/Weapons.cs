using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Realm.Ecs.Components.Combat;

/// <summary>
/// Holds a collection of weapon slots for an entity.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct Weapons
{
	public int Handle;

	public Weapons(List<Weapon> slots)
	{
		Handle = ColdPayloadRegistry<List<Weapon>>.Register(slots);
	}

	public List<Weapon> Slots
	{
		get => ColdPayloadRegistry<List<Weapon>>.Get(Handle);
		set => Handle = ColdPayloadRegistry<List<Weapon>>.Register(value);
	}
}
