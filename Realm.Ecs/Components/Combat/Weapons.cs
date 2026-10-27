using System;
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
	public int Count;

	public Weapons(IReadOnlyList<Weapon>? slots)
	{
		Handle = Intern(slots, out int count);
		Count = count;
	}

	public bool TryGet(int index, out Weapon weapon)
	{
		weapon = default;
		if (index < 0 || index >= Count) return false;
		if (!InternedValueStore.TryGetPayload(Handle, out byte kind, out ReadOnlySpan<byte> payload)) return false;
		if (kind != InternedValueStore.WeaponListKind) return false;

		ReadOnlySpan<Weapon> slots = MemoryMarshal.Cast<byte, Weapon>(payload);
		if (index >= slots.Length) return false;

		weapon = slots[index];
		return true;
	}

	public void CopyTo(List<Weapon> destination)
	{
		if (!InternedValueStore.TryGetPayload(Handle, out byte kind, out ReadOnlySpan<byte> payload)) return;
		if (kind != InternedValueStore.WeaponListKind) return;

		ReadOnlySpan<Weapon> slots = MemoryMarshal.Cast<byte, Weapon>(payload);
		for (int index = 0; index < slots.Length; index++)
		{
			destination.Add(slots[index]);
		}
	}

	public Weapon[] ToArray()
	{
		if (!InternedValueStore.TryGetPayload(Handle, out byte kind, out ReadOnlySpan<byte> payload)) return Array.Empty<Weapon>();
		if (kind != InternedValueStore.WeaponListKind) return Array.Empty<Weapon>();

		return MemoryMarshal.Cast<byte, Weapon>(payload).ToArray();
	}

	private static int Intern(IReadOnlyList<Weapon>? slots, out int count)
	{
		count = 0;
		if (slots == null || slots.Count == 0) return 0;

		var copy = new Weapon[slots.Count];
		for (int index = 0; index < slots.Count; index++)
		{
			copy[index] = slots[index];
		}

		count = copy.Length;
		return InternedValueStore.GetOrAddBytes(InternedValueStore.WeaponListKind, MemoryMarshal.AsBytes(copy.AsSpan()));
	}
}
