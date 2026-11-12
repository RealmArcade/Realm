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
	private ComponentBuffer<T> GetOrCreateBuffer<T>(int cId) where T : unmanaged
	{
		IComponentBuffer? published = Volatile.Read(ref _buffers[cId]);
		if (published != null) return (ComponentBuffer<T>)published;

		return CreateComponentBuffer<T>(cId);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private ComponentBuffer<T> CreateComponentBuffer<T>(int cId) where T : unmanaged
	{
		AssertNotForkedIntoWorkers("Creating a component buffer");

		lock (_componentBufferCreationLock)
		{
			if (_buffers[cId] != null) return (ComponentBuffer<T>)_buffers[cId];

			var buffer = new ComponentBuffer<T>();
			AttachCleanupHandler(buffer);

			// The bitset is published before the buffer that indexes it, so a reader that sees the buffer can never
			// find the membership bitset missing beside it.
			Volatile.Write(ref _componentBitsets[cId], new ulong[BITSET_CHUNKS]);
			Volatile.Write(ref _buffers[cId], buffer);
			return buffer;
		}
	}

	private static void AttachCleanupHandler<T>(ComponentBuffer<T> buffer) where T : unmanaged
	{
		if (typeof(T) == typeof(Realm.Ecs.Components.Core.SummaryTableState))
		{
			var b = (ComponentBuffer<Realm.Ecs.Components.Core.SummaryTableState>)(object)buffer;
			b.ClearHandler = id =>
			{
				ref var s = ref b.Get(id);
				ColdPayloadRegistry<string[]>.Free(s.HeadersHandle);
				ColdPayloadRegistry<Dictionary<string, string[]>>.Free(s.RowsHandle);
			};
		}
		else if (typeof(T) == typeof(Realm.Ecs.Components.Terrain.ShroudState))
		{
			var b = (ComponentBuffer<Realm.Ecs.Components.Terrain.ShroudState>)(object)buffer;
			b.ClearHandler = id => Realm.Ecs.Components.Terrain.ShroudGridRegistry.Release(b.Get(id).GridHandle);
		}
		else if (typeof(T) == typeof(Realm.Ecs.Components.Terrain.TerrainColorsState))
		{
			var b = (ComponentBuffer<Realm.Ecs.Components.Terrain.TerrainColorsState>)(object)buffer;
			b.ClearHandler = id => ColdPayloadRegistry<string[]>.Free(b.Get(id).Handle);
		}
		else if (typeof(T) == typeof(Realm.Ecs.Components.Terrain.TerrainState))
		{
			var b = (ComponentBuffer<Realm.Ecs.Components.Terrain.TerrainState>)(object)buffer;
			b.ClearHandler = id =>
			{
				ref var s = ref b.Get(id);
				ColdPayloadRegistry<Realm.Ecs.Components.Terrain.TerrainCell[,]>.Free(s.CellsHandle);
				ColdPayloadRegistry<int[,]>.Free(s.PathingCodesHandle);
				ColdPayloadRegistry<DotRecast.Detour.DtNavMesh>.Free(s.NavMeshHandle);
				ColdPayloadRegistry<DotRecast.Detour.DtNavMeshQuery>.Free(s.NavMeshQueryHandle);
				ColdPayloadRegistry<Realm.Ecs.Components.Terrain.TerrainSwatchConfig[]>.Free(s.SwatchConfigsHandle);
			};
		}
		else if (typeof(T) == typeof(Realm.Ecs.Components.Core.CustomMetadata))
		{
			var b = (ComponentBuffer<Realm.Ecs.Components.Core.CustomMetadata>)(object)buffer;
			b.ClearHandler = id => ColdPayloadRegistry<Dictionary<string, object>>.Free(b.Get(id).Handle);
		}
		else if (typeof(T) == typeof(Realm.Ecs.Components.Core.LeaderboardState))
		{
			var b = (ComponentBuffer<Realm.Ecs.Components.Core.LeaderboardState>)(object)buffer;
			b.ClearHandler = id => ColdPayloadRegistry<Realm.Ecs.Common.LeaderboardBoards>.Free(b.Get(id).BoardsHandle);
		}
		else if (typeof(T) == typeof(Realm.Ecs.Components.Core.NetworkMappingState))
		{
			var b = (ComponentBuffer<Realm.Ecs.Components.Core.NetworkMappingState>)(object)buffer;
			b.ClearHandler = id =>
			{
				ref var s = ref b.Get(id);
				ColdPayloadRegistry<Dictionary<int, Entity>>.Free(s.ServerToClientHandle);
				ColdPayloadRegistry<Dictionary<int, int>>.Free(s.ClientToServerHandle);
				ColdPayloadRegistry<Dictionary<int, Entity>>.Free(s.PeerIdToPlayerHandle);
				ColdPayloadRegistry<Entity[]>.Free(s.PlayerEntitiesHandle);
			};
		}
	}

	private void ClearAllComponents(int id)
	{
		ComponentMask mask = _entityMasks.Get(id);
		int chunk = id >> 6;
		ulong bitNot = ~(1UL << (id & 63));

		ClearMaskBits(id, mask.Mask0, 0, chunk, bitNot);
		ClearMaskBits(id, mask.Mask1, 64, chunk, bitNot);
		ClearMaskBits(id, mask.Mask2, 128, chunk, bitNot);
		ClearMaskBits(id, mask.Mask3, 192, chunk, bitNot);
	}

	private void ClearMaskBits(int entityId, ulong maskWord, int baseId, int chunk, ulong bitNot)
	{
		while (maskWord != 0)
		{
			int bit = BitOperations.TrailingZeroCount(maskWord);
			int cId = baseId + bit;
			if (_componentBitsets[cId] != null)
			{
				_componentBitsets[cId][chunk] &= bitNot;
			}
			_buffers[cId]?.Clear(entityId);
			maskWord &= maskWord - 1;
		}
	}

	private static ComponentMask ClearMaskBit(ComponentMask mask, int componentId)
	{
		int word = componentId >> 6;
		int bit = componentId & 63;
		ulong bitNot = ~(1UL << bit);

		return word switch
		{
			0 => new ComponentMask(mask.Mask0 & bitNot, mask.Mask1, mask.Mask2, mask.Mask3),
			1 => new ComponentMask(mask.Mask0, mask.Mask1 & bitNot, mask.Mask2, mask.Mask3),
			2 => new ComponentMask(mask.Mask0, mask.Mask1, mask.Mask2 & bitNot, mask.Mask3),
			3 => new ComponentMask(mask.Mask0, mask.Mask1, mask.Mask2, mask.Mask3 & bitNot),
			_ => mask
		};
	}
}
