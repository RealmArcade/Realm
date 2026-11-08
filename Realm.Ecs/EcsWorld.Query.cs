// Extracted from EcsWorld.cs (task 6 decomposition).
using System;
using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Realm.Ecs.Services;

namespace Realm.Ecs;

public partial class EcsWorld
{
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public ulong GetMatchingChunk(QueryDescription query, int chunkIndex)
	{
		ulong match = GetMatchingChunk(query.All, chunkIndex);
		if (match == 0 || query.None == ComponentMask.Empty) return match;

		ulong noneMatch = GetAnyMatchingChunk(query.None, chunkIndex);
		return match & ~noneMatch;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public ulong GetMatchingChunk(ComponentMask mask, int chunkIndex)
	{
		ulong result = ~0UL;

		ulong m0 = mask.Mask0;
		while (m0 != 0)
		{
			int bit = BitOperations.TrailingZeroCount(m0);
			ulong[]? bitset = _componentBitsets[bit];
			if (bitset == null) return 0;
			result &= bitset[chunkIndex];
			if (result == 0) return 0;
			m0 &= m0 - 1;
		}

		ulong m1 = mask.Mask1;
		while (m1 != 0)
		{
			int bit = BitOperations.TrailingZeroCount(m1);
			ulong[]? bitset = _componentBitsets[64 + bit];
			if (bitset == null) return 0;
			result &= bitset[chunkIndex];
			if (result == 0) return 0;
			m1 &= m1 - 1;
		}

		ulong m2 = mask.Mask2;
		while (m2 != 0)
		{
			int bit = BitOperations.TrailingZeroCount(m2);
			ulong[]? bitset = _componentBitsets[128 + bit];
			if (bitset == null) return 0;
			result &= bitset[chunkIndex];
			if (result == 0) return 0;
			m2 &= m2 - 1;
		}

		ulong m3 = mask.Mask3;
		while (m3 != 0)
		{
			int bit = BitOperations.TrailingZeroCount(m3);
			ulong[]? bitset = _componentBitsets[192 + bit];
			if (bitset == null) return 0;
			result &= bitset[chunkIndex];
			if (result == 0) return 0;
			m3 &= m3 - 1;
		}

		return result;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private ulong GetAnyMatchingChunk(ComponentMask mask, int chunkIndex)
	{
		ulong result = 0UL;

		ulong m0 = mask.Mask0;
		while (m0 != 0)
		{
			int bit = BitOperations.TrailingZeroCount(m0);
			ulong[]? bitset = _componentBitsets[bit];
			if (bitset != null) result |= bitset[chunkIndex];
			m0 &= m0 - 1;
		}

		ulong m1 = mask.Mask1;
		while (m1 != 0)
		{
			int bit = BitOperations.TrailingZeroCount(m1);
			ulong[]? bitset = _componentBitsets[64 + bit];
			if (bitset != null) result |= bitset[chunkIndex];
			m1 &= m1 - 1;
		}

		ulong m2 = mask.Mask2;
		while (m2 != 0)
		{
			int bit = BitOperations.TrailingZeroCount(m2);
			ulong[]? bitset = _componentBitsets[128 + bit];
			if (bitset != null) result |= bitset[chunkIndex];
			m2 &= m2 - 1;
		}

		ulong m3 = mask.Mask3;
		while (m3 != 0)
		{
			int bit = BitOperations.TrailingZeroCount(m3);
			ulong[]? bitset = _componentBitsets[192 + bit];
			if (bitset != null) result |= bitset[chunkIndex];
			m3 &= m3 - 1;
		}

		return result;
	}

	public QueryEnumerable Query(in QueryDescription query) => new QueryEnumerable(this, query);
	public QueryEnumerable Query(ComponentMask mask) => new QueryEnumerable(this, mask);
	public QueryEnumerable Query<T>() where T : unmanaged => Query(ComponentMasks.Of<T>());
	public QueryEnumerable Query<T1, T2>() where T1 : unmanaged where T2 : unmanaged => Query(ComponentMasks.Of<T1, T2>());
	public QueryEnumerable Query<T1, T2, T3>() where T1 : unmanaged where T2 : unmanaged where T3 : unmanaged => Query(ComponentMasks.Of<T1, T2, T3>());
	public QueryEnumerable Query<T1, T2, T3, T4>() where T1 : unmanaged where T2 : unmanaged where T3 : unmanaged where T4 : unmanaged => Query(ComponentMasks.Of<T1, T2, T3, T4>());

	public int CountEntities(in QueryDescription query)
	{
		int count = 0;
		for (int chunkIndex = 0; chunkIndex < BITSET_CHUNKS; chunkIndex++)
		{
			ulong active = _activeEntityBitset[chunkIndex];
			if (active == 0) continue;
			ulong match = active & GetMatchingChunk(query, chunkIndex);
			if (match != 0)
			{
				count += BitOperations.PopCount(match);
			}
		}
		return count;
	}

	public void Query(in QueryDescription query, EntityCallback callback)
	{
		foreach (var entity in Query(query))
		{
			callback(entity);
		}
	}

	public void Query<T1>(in QueryDescription query, EntityCallback<T1> callback) where T1 : unmanaged
	{
		var b1 = GetOrCreateBuffer<T1>(ComponentType<T1>.Id);
		foreach (var entity in Query(query))
		{
			callback(entity, ref b1.Get(entity.Id));
		}
	}

	public void Query<T1, T2>(in QueryDescription query, EntityCallback<T1, T2> callback) where T1 : unmanaged where T2 : unmanaged
	{
		var b1 = GetOrCreateBuffer<T1>(ComponentType<T1>.Id);
		var b2 = GetOrCreateBuffer<T2>(ComponentType<T2>.Id);
		foreach (var entity in Query(query))
		{
			callback(entity, ref b1.Get(entity.Id), ref b2.Get(entity.Id));
		}
	}

	public void Query<T1, T2, T3>(in QueryDescription query, EntityCallback<T1, T2, T3> callback) where T1 : unmanaged where T2 : unmanaged where T3 : unmanaged
	{
		var b1 = GetOrCreateBuffer<T1>(ComponentType<T1>.Id);
		var b2 = GetOrCreateBuffer<T2>(ComponentType<T2>.Id);
		var b3 = GetOrCreateBuffer<T3>(ComponentType<T3>.Id);
		foreach (var entity in Query(query))
		{
			callback(entity, ref b1.Get(entity.Id), ref b2.Get(entity.Id), ref b3.Get(entity.Id));
		}
	}

	public void Query<T1, T2, T3, T4>(in QueryDescription query, EntityCallback<T1, T2, T3, T4> callback) where T1 : unmanaged where T2 : unmanaged where T3 : unmanaged where T4 : unmanaged
	{
		var b1 = GetOrCreateBuffer<T1>(ComponentType<T1>.Id);
		var b2 = GetOrCreateBuffer<T2>(ComponentType<T2>.Id);
		var b3 = GetOrCreateBuffer<T3>(ComponentType<T3>.Id);
		var b4 = GetOrCreateBuffer<T4>(ComponentType<T4>.Id);
		foreach (var entity in Query(query))
		{
			callback(entity, ref b1.Get(entity.Id), ref b2.Get(entity.Id), ref b3.Get(entity.Id), ref b4.Get(entity.Id));
		}
	}
}
