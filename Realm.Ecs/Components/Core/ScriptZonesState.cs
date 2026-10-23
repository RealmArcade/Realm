using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Realm.MapAPI;

namespace Realm.Ecs.Components.Core;

/// <summary>
/// Holds the spatial trigger zones a map registered, as positions into one immutable interned payload.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct ScriptZonesState
{
	public int Handle;
	public int Count;

	public ScriptZonesState(IReadOnlyList<ZoneBounds>? zones)
	{
		this = default;
		if (zones == null || zones.Count == 0) return;

		var buffer = new ZoneBounds[zones.Count];
		for (int index = 0; index < zones.Count; index++)
		{
			buffer[index] = zones[index];
		}

		Apply(buffer);
	}

	public void Add(in ZoneBounds zone)
	{
		ReadOnlySpan<ZoneBounds> existing = GetZones();
		var buffer = new ZoneBounds[existing.Length + 1];
		existing.CopyTo(buffer);
		buffer[existing.Length] = zone;

		Apply(buffer);
	}

	public ZoneBounds GetZone(int zoneIndex)
	{
		if (zoneIndex < 0) return default;

		ReadOnlySpan<ZoneBounds> zones = GetZones();
		return zoneIndex < zones.Length ? zones[zoneIndex] : default;
	}

	public ReadOnlySpan<ZoneBounds> GetZones()
	{
		return GetZones(Handle);
	}

	public static ReadOnlySpan<ZoneBounds> GetZones(int handle)
	{
		if (handle == 0) return ReadOnlySpan<ZoneBounds>.Empty;
		if (!InternedValueStore.TryGetPayload(handle, out byte kind, out ReadOnlySpan<byte> payload)) return ReadOnlySpan<ZoneBounds>.Empty;
		if (kind != InternedValueStore.ZoneListKind) return ReadOnlySpan<ZoneBounds>.Empty;

		return MemoryMarshal.Cast<byte, ZoneBounds>(payload);
	}

	public void Clear()
	{
		Handle = 0;
		Count = 0;
	}

	private void Apply(ReadOnlySpan<ZoneBounds> zones)
	{
		Handle = InternedValueStore.GetOrAddBytes(InternedValueStore.ZoneListKind, MemoryMarshal.AsBytes(zones));
		Count = Handle == 0 ? 0 : zones.Length;
	}
}
