using System;
using System.Runtime.CompilerServices;

namespace Realm.MapAPI;

/// <summary>
/// Provides ring-buffered unmanaged storage for WASM in-guest unit query results to prevent allocations and avoid overwriting outer query loops during nested calls.
/// </summary>
public static class QueryResultBuffer
{
	private const int RING_SLOTS = 16;
	private static readonly UnitId[] s_buffer = new UnitId[RealmSharedMemory.MAX_ENTITIES * RING_SLOTS];
	private static int s_ringIndex = 0;

	/// <summary>
	/// Allocates a non-overlapping slice from the static ring buffer for storing unit query result handles.
	/// </summary>
	/// <returns>A span pointing to the allocated ring buffer slot segment.</returns>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static Span<UnitId> AllocateSlice()
	{
		int slot = s_ringIndex;
		s_ringIndex = (s_ringIndex + 1) % RING_SLOTS;
		int offset = slot * RealmSharedMemory.MAX_ENTITIES;
		return new Span<UnitId>(s_buffer, offset, RealmSharedMemory.MAX_ENTITIES);
	}
}
