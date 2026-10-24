using System.Runtime.InteropServices;

namespace Realm.Ecs.Components.Core;

/// <summary>
/// Tracks which script zones currently contain a unit entity.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct OccupiedZones
{
	public OccupiedZoneMask Mask;

	public bool Contains(int zoneIndex)
	{
		if (zoneIndex < 0 || zoneIndex >= OccupiedZoneMask.Capacity) return false;

		return (Mask[zoneIndex >> 6] & (1UL << (zoneIndex & 63))) != 0;
	}

	public bool Add(int zoneIndex)
	{
		if (zoneIndex < 0 || zoneIndex >= OccupiedZoneMask.Capacity) return false;

		ref ulong word = ref Mask[zoneIndex >> 6];
		ulong bit = 1UL << (zoneIndex & 63);
		if ((word & bit) != 0) return false;

		word |= bit;
		return true;
	}

	public bool Remove(int zoneIndex)
	{
		if (zoneIndex < 0 || zoneIndex >= OccupiedZoneMask.Capacity) return false;

		ref ulong word = ref Mask[zoneIndex >> 6];
		ulong bit = 1UL << (zoneIndex & 63);
		if ((word & bit) == 0) return false;

		word &= ~bit;
		return true;
	}

	public void Clear()
	{
		Mask = default;
	}
}
