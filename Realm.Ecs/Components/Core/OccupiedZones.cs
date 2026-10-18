using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Realm.Ecs.Components.Core;

/// <summary>
/// Tracks which script zones are currently occupied by a unit entity.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct OccupiedZones
{
	public int Handle;

	public OccupiedZones(HashSet<int> zoneIds)
	{
		Handle = ColdPayloadRegistry<HashSet<int>>.Register(zoneIds);
	}

	public HashSet<int> ZoneIds
	{
		get => ColdPayloadRegistry<HashSet<int>>.Get(Handle)!;
		set => Handle = ColdPayloadRegistry<HashSet<int>>.Register(value);
	}
}
