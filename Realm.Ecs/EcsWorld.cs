using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Realm.Ecs;

public delegate void EntityCallback(Entity entity);
public delegate void EntityCallback<T1>(Entity entity, ref T1 c1);
public delegate void EntityCallback<T1, T2>(Entity entity, ref T1 c1, ref T2 c2);
public delegate void EntityCallback<T1, T2, T3>(Entity entity, ref T1 c1, ref T2 c2, ref T3 c3);
public delegate void EntityCallback<T1, T2, T3, T4>(Entity entity, ref T1 c1, ref T2 c2, ref T3 c3, ref T4 c4);

public class EcsWorld : IDisposable
{
	public const int MAX_ENTITIES = 8192;
	public const int MAX_PLAYERS = 25;
	public const int MAX_COMPONENT_TYPES = 256;
	public const int BITSET_CHUNKS = MAX_ENTITIES / 64;

	private readonly int[] _freeList = new int[MAX_ENTITIES];
	private int _freeListCount = 0;
	private int _nextEntityId = 0;
	private int _count = 0;

	private readonly ComponentBuffer<byte> _alive = new ComponentBuffer<byte>(MAX_ENTITIES);
	private readonly ComponentBuffer<ComponentMask> _entityMasks = new ComponentBuffer<ComponentMask>(MAX_ENTITIES);
	private readonly ulong[][] _componentBitsets = new ulong[MAX_COMPONENT_TYPES][];
	private readonly IComponentBuffer[] _buffers = new IComponentBuffer[MAX_COMPONENT_TYPES];

	public int Count => _count;

	public ComponentBuffer<byte> AliveBuffer => _alive;
	public ComponentBuffer<ComponentMask> EntityMasksBuffer => _entityMasks;

	public void BindExternalAliveMemory(IntPtr pointer, int lengthInBytes)
	{
		_alive.BindExternalMemory(pointer, lengthInBytes);
	}

	public void BindExternalEntityMasksMemory(IntPtr pointer, int lengthInBytes)
	{
		_entityMasks.BindExternalMemory(pointer, lengthInBytes);
	}

	public Span<byte> GetAliveSpan() => _alive.AsSpan();
	public Span<ComponentMask> GetEntityMasksSpan() => _entityMasks.AsSpan();

	public void Dispose()
	{
		for (int index = 0; index < MAX_ENTITIES; index++)
		{
			if (_alive.Get(index) != 0)
			{
				ClearAllComponents(index);
				_alive.Set(index, (byte)0);
				_entityMasks.Set(index, ComponentMask.Empty);
			}
		}
		_count = 0;
		_freeListCount = 0;
		_nextEntityId = 0;
		ColdPayloadRegistry.ResetAll();
		StringRegistry.Reset();
	}

	public Entity Create()
	{
		int id;
		if (_freeListCount > 0)
		{
			id = _freeList[--_freeListCount];
		}
		else
		{
			if (_nextEntityId >= MAX_ENTITIES)
			{
				throw new InvalidOperationException($"Exceeded MAX_ENTITIES limit of {MAX_ENTITIES}");
			}
			id = _nextEntityId++;
		}

		_alive.Set(id, (byte)1);
		_entityMasks.Set(id, ComponentMask.Empty);
		_count++;
		return new Entity(id);
	}

	public Entity Create<T1>(in T1 c1) where T1 : unmanaged
	{
		var entity = Create();
		Add(entity, c1);
		return entity;
	}

	public Entity Create<T1, T2>(in T1 c1, in T2 c2) where T1 : unmanaged where T2 : unmanaged
	{
		var entity = Create();
		Add(entity, c1);
		Add(entity, c2);
		return entity;
	}

	public Entity Create<T1, T2, T3>(in T1 c1, in T2 c2, in T3 c3) where T1 : unmanaged where T2 : unmanaged where T3 : unmanaged
	{
		var entity = Create();
		Add(entity, c1);
		Add(entity, c2);
		Add(entity, c3);
		return entity;
	}

	public Entity Create<T1, T2, T3, T4>(in T1 c1, in T2 c2, in T3 c3, in T4 c4) where T1 : unmanaged where T2 : unmanaged where T3 : unmanaged where T4 : unmanaged
	{
		var entity = Create();
		Add(entity, c1);
		Add(entity, c2);
		Add(entity, c3);
		Add(entity, c4);
		return entity;
	}

	public Entity Create<T1, T2, T3, T4, T5>(in T1 c1, in T2 c2, in T3 c3, in T4 c4, in T5 c5) where T1 : unmanaged where T2 : unmanaged where T3 : unmanaged where T4 : unmanaged where T5 : unmanaged
	{
		var entity = Create();
		Add(entity, c1);
		Add(entity, c2);
		Add(entity, c3);
		Add(entity, c4);
		Add(entity, c5);
		return entity;
	}

	public Entity Create<T1, T2, T3, T4, T5, T6>(in T1 c1, in T2 c2, in T3 c3, in T4 c4, in T5 c5, in T6 c6) where T1 : unmanaged where T2 : unmanaged where T3 : unmanaged where T4 : unmanaged where T5 : unmanaged where T6 : unmanaged
	{
		var entity = Create();
		Add(entity, c1);
		Add(entity, c2);
		Add(entity, c3);
		Add(entity, c4);
		Add(entity, c5);
		Add(entity, c6);
		return entity;
	}

	public Entity Create<T1, T2, T3, T4, T5, T6, T7>(in T1 c1, in T2 c2, in T3 c3, in T4 c4, in T5 c5, in T6 c6, in T7 c7) where T1 : unmanaged where T2 : unmanaged where T3 : unmanaged where T4 : unmanaged where T5 : unmanaged where T6 : unmanaged where T7 : unmanaged
	{
		var entity = Create();
		Add(entity, c1);
		Add(entity, c2);
		Add(entity, c3);
		Add(entity, c4);
		Add(entity, c5);
		Add(entity, c6);
		Add(entity, c7);
		return entity;
	}

	public Entity Create<T1, T2, T3, T4, T5, T6, T7, T8>(in T1 c1, in T2 c2, in T3 c3, in T4 c4, in T5 c5, in T6 c6, in T7 c7, in T8 c8) where T1 : unmanaged where T2 : unmanaged where T3 : unmanaged where T4 : unmanaged where T5 : unmanaged where T6 : unmanaged where T7 : unmanaged where T8 : unmanaged
	{
		var entity = Create();
		Add(entity, c1);
		Add(entity, c2);
		Add(entity, c3);
		Add(entity, c4);
		Add(entity, c5);
		Add(entity, c6);
		Add(entity, c7);
		Add(entity, c8);
		return entity;
	}

	public Entity Create<T1, T2, T3, T4, T5, T6, T7, T8, T9>(in T1 c1, in T2 c2, in T3 c3, in T4 c4, in T5 c5, in T6 c6, in T7 c7, in T8 c8, in T9 c9) where T1 : unmanaged where T2 : unmanaged where T3 : unmanaged where T4 : unmanaged where T5 : unmanaged where T6 : unmanaged where T7 : unmanaged where T8 : unmanaged where T9 : unmanaged
	{
		var entity = Create();
		Add(entity, c1);
		Add(entity, c2);
		Add(entity, c3);
		Add(entity, c4);
		Add(entity, c5);
		Add(entity, c6);
		Add(entity, c7);
		Add(entity, c8);
		Add(entity, c9);
		return entity;
	}

	public Entity Create<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(in T1 c1, in T2 c2, in T3 c3, in T4 c4, in T5 c5, in T6 c6, in T7 c7, in T8 c8, in T9 c9, in T10 c10) where T1 : unmanaged where T2 : unmanaged where T3 : unmanaged where T4 : unmanaged where T5 : unmanaged where T6 : unmanaged where T7 : unmanaged where T8 : unmanaged where T9 : unmanaged where T10 : unmanaged
	{
		var entity = Create();
		Add(entity, c1);
		Add(entity, c2);
		Add(entity, c3);
		Add(entity, c4);
		Add(entity, c5);
		Add(entity, c6);
		Add(entity, c7);
		Add(entity, c8);
		Add(entity, c9);
		Add(entity, c10);
		return entity;
	}

	public Entity Create<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>(in T1 c1, in T2 c2, in T3 c3, in T4 c4, in T5 c5, in T6 c6, in T7 c7, in T8 c8, in T9 c9, in T10 c10, in T11 c11) where T1 : unmanaged where T2 : unmanaged where T3 : unmanaged where T4 : unmanaged where T5 : unmanaged where T6 : unmanaged where T7 : unmanaged where T8 : unmanaged where T9 : unmanaged where T10 : unmanaged where T11 : unmanaged
	{
		var entity = Create();
		Add(entity, c1);
		Add(entity, c2);
		Add(entity, c3);
		Add(entity, c4);
		Add(entity, c5);
		Add(entity, c6);
		Add(entity, c7);
		Add(entity, c8);
		Add(entity, c9);
		Add(entity, c10);
		Add(entity, c11);
		return entity;
	}

	public Entity Create<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>(in T1 c1, in T2 c2, in T3 c3, in T4 c4, in T5 c5, in T6 c6, in T7 c7, in T8 c8, in T9 c9, in T10 c10, in T11 c11, in T12 c12) where T1 : unmanaged where T2 : unmanaged where T3 : unmanaged where T4 : unmanaged where T5 : unmanaged where T6 : unmanaged where T7 : unmanaged where T8 : unmanaged where T9 : unmanaged where T10 : unmanaged where T11 : unmanaged where T12 : unmanaged
	{
		var entity = Create();
		Add(entity, c1);
		Add(entity, c2);
		Add(entity, c3);
		Add(entity, c4);
		Add(entity, c5);
		Add(entity, c6);
		Add(entity, c7);
		Add(entity, c8);
		Add(entity, c9);
		Add(entity, c10);
		Add(entity, c11);
		Add(entity, c12);
		return entity;
	}

	public void Destroy(Entity entity)
	{
		int id = entity.Id;
		if (id < 0 || id >= MAX_ENTITIES || _alive.Get(id) == 0) return;

		ClearAllComponents(id);

		_alive.Set(id, (byte)0);
		_entityMasks.Set(id, ComponentMask.Empty);
		_freeList[_freeListCount++] = id;
		_count--;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool IsAlive(Entity entity)
	{
		int id = entity.Id;
		return id >= 0 && id < MAX_ENTITIES && _alive.Get(id) != 0;
	}

	public ref T Add<T>(Entity entity, in T component = default) where T : unmanaged
	{
		int id = entity.Id;
		int cId = ComponentType<T>.Id;
		var buffer = GetOrCreateBuffer<T>(cId);
		buffer.Set(id, component);

		int chunk = id >> 6;
		ulong bit = 1UL << (id & 63);
		_componentBitsets[cId][chunk] |= bit;

		_entityMasks.Set(id, _entityMasks.Get(id) | ComponentMask.FromId(cId));
		return ref buffer.Get(id);
	}

	public ref T AddOrGet<T>(Entity entity, in T component = default) where T : unmanaged
	{
		if (Has<T>(entity))
		{
			return ref Get<T>(entity);
		}
		return ref Add(entity, component);
	}

	public void Set<T>(Entity entity, in T component) where T : unmanaged
	{
		int id = entity.Id;
		int cId = ComponentType<T>.Id;
		var buffer = GetOrCreateBuffer<T>(cId);
		buffer.Set(id, component);

		int chunk = id >> 6;
		ulong bit = 1UL << (id & 63);
		_componentBitsets[cId][chunk] |= bit;

		_entityMasks.Set(id, _entityMasks.Get(id) | ComponentMask.FromId(cId));
	}

	public void SetOrAdd<T>(Entity entity, in T component) where T : unmanaged
	{
		Set<T>(entity, component);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public ref T Get<T>(Entity entity) where T : unmanaged
	{
		int id = entity.Id;
		int cId = ComponentType<T>.Id;
		var buffer = (ComponentBuffer<T>)_buffers[cId];
		return ref buffer.Get(id);
	}

	public bool TryGet<T>(Entity entity, out T component) where T : unmanaged
	{
		if (Has<T>(entity))
		{
			component = Get<T>(entity);
			return true;
		}
		component = default;
		return false;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool Has<T>(Entity entity) where T : unmanaged
	{
		int id = entity.Id;
		if (id < 0 || id >= MAX_ENTITIES || _alive.Get(id) == 0) return false;
		int cId = ComponentType<T>.Id;
		int chunk = id >> 6;
		ulong bit = 1UL << (id & 63);
		return _componentBitsets[cId] != null && (_componentBitsets[cId][chunk] & bit) != 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool Has(Entity entity, ComponentMask mask)
	{
		int id = entity.Id;
		if (id < 0 || id >= MAX_ENTITIES || _alive.Get(id) == 0) return false;
		return _entityMasks.Get(id).HasAll(mask);
	}

	public void Remove<T>(Entity entity) where T : unmanaged
	{
		int id = entity.Id;
		if (id < 0 || id >= MAX_ENTITIES || _alive.Get(id) == 0) return;
		int cId = ComponentType<T>.Id;
		if (_componentBitsets[cId] == null) return;

		int chunk = id >> 6;
		ulong bit = 1UL << (id & 63);
		_componentBitsets[cId][chunk] &= ~bit;

		_buffers[cId]?.Clear(id);
		_entityMasks.Set(id, ClearMaskBit(_entityMasks.Get(id), cId));
	}

	public ComponentBuffer<T> GetBuffer<T>() where T : unmanaged
	{
		int cId = ComponentType<T>.Id;
		return GetOrCreateBuffer<T>(cId);
	}

	public void BindExternalComponentMemory<T>(IntPtr pointer, int lengthInBytes) where T : unmanaged
	{
		int cId = ComponentType<T>.Id;
		var buffer = GetOrCreateBuffer<T>(cId);
		buffer.BindExternalMemory(pointer, lengthInBytes);
	}

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
	public QueryEnumerable Query<T>() where T : unmanaged => Query(ComponentMask.Of<T>());
	public QueryEnumerable Query<T1, T2>() where T1 : unmanaged where T2 : unmanaged => Query(ComponentMask.Of<T1, T2>());
	public QueryEnumerable Query<T1, T2, T3>() where T1 : unmanaged where T2 : unmanaged where T3 : unmanaged => Query(ComponentMask.Of<T1, T2, T3>());
	public QueryEnumerable Query<T1, T2, T3, T4>() where T1 : unmanaged where T2 : unmanaged where T3 : unmanaged where T4 : unmanaged => Query(ComponentMask.Of<T1, T2, T3, T4>());

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

	private ComponentBuffer<T> GetOrCreateBuffer<T>(int cId) where T : unmanaged
	{
		if (_buffers[cId] == null)
		{
			var buffer = new ComponentBuffer<T>();
			AttachCleanupHandler(buffer);
			_buffers[cId] = buffer;
			_componentBitsets[cId] = new ulong[BITSET_CHUNKS];
		}
		return (ComponentBuffer<T>)_buffers[cId];
	}

	private static void AttachCleanupHandler<T>(ComponentBuffer<T> buffer) where T : unmanaged
	{
		if (typeof(T) == typeof(Realm.Ecs.Components.Stats.Stats))
		{
			var b = (ComponentBuffer<Realm.Ecs.Components.Stats.Stats>)(object)buffer;
			b.ClearHandler = id => ColdPayloadRegistry<Dictionary<Realm.Ecs.Common.StatId, float>>.Free(b.Get(id).Handle);
		}
		else if (typeof(T) == typeof(Realm.Ecs.Components.Core.ModifierState))
		{
			var b = (ComponentBuffer<Realm.Ecs.Components.Core.ModifierState>)(object)buffer;
			b.ClearHandler = id => ColdPayloadRegistry<List<Realm.Ecs.Components.Stats.StatModifier>>.Free(b.Get(id).Handle);
		}
		else if (typeof(T) == typeof(Realm.Ecs.Components.Core.SpellCooldowns))
		{
			var b = (ComponentBuffer<Realm.Ecs.Components.Core.SpellCooldowns>)(object)buffer;
			b.ClearHandler = id => ColdPayloadRegistry<Dictionary<string, float>>.Free(b.Get(id).Handle);
		}
		else if (typeof(T) == typeof(Realm.Ecs.Components.Core.OccupiedZones))
		{
			var b = (ComponentBuffer<Realm.Ecs.Components.Core.OccupiedZones>)(object)buffer;
			b.ClearHandler = id => ColdPayloadRegistry<HashSet<int>>.Free(b.Get(id).Handle);
		}
		else if (typeof(T) == typeof(Realm.Ecs.Components.Core.ProductionQueue))
		{
			var b = (ComponentBuffer<Realm.Ecs.Components.Core.ProductionQueue>)(object)buffer;
			b.ClearHandler = id => ColdPayloadRegistry<List<string>>.Free(b.Get(id).Handle);
		}
		else if (typeof(T) == typeof(Realm.Ecs.Components.Core.ScriptPlayersState))
		{
			var b = (ComponentBuffer<Realm.Ecs.Components.Core.ScriptPlayersState>)(object)buffer;
			b.ClearHandler = id => ColdPayloadRegistry<Realm.Ecs.Components.Core.ScriptPlayer[]>.Free(b.Get(id).Handle);
		}
		else if (typeof(T) == typeof(Realm.Ecs.Components.Core.ScriptZonesState))
		{
			var b = (ComponentBuffer<Realm.Ecs.Components.Core.ScriptZonesState>)(object)buffer;
			b.ClearHandler = id => ColdPayloadRegistry<List<Realm.Ecs.Components.Core.ZoneBounds>>.Free(b.Get(id).Handle);
		}
		else if (typeof(T) == typeof(Realm.Ecs.Components.Core.SummaryTableState))
		{
			var b = (ComponentBuffer<Realm.Ecs.Components.Core.SummaryTableState>)(object)buffer;
			b.ClearHandler = id =>
			{
				ref var s = ref b.Get(id);
				ColdPayloadRegistry<string[]>.Free(s.HeadersHandle);
				ColdPayloadRegistry<Dictionary<string, string[]>>.Free(s.RowsHandle);
			};
		}
		else if (typeof(T) == typeof(Realm.Ecs.Components.Core.VFXQueue))
		{
			var b = (ComponentBuffer<Realm.Ecs.Components.Core.VFXQueue>)(object)buffer;
			b.ClearHandler = id => ColdPayloadRegistry<List<Realm.Ecs.Components.Core.VFXRequest>>.Free(b.Get(id).Handle);
		}
		else if (typeof(T) == typeof(Realm.Ecs.Components.Terrain.ShroudState))
		{
			var b = (ComponentBuffer<Realm.Ecs.Components.Terrain.ShroudState>)(object)buffer;
			b.ClearHandler = id => ColdPayloadRegistry<byte[,]>.Free(b.Get(id).GridHandle);
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
