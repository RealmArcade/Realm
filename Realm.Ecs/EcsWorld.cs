using System;
using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Realm.Ecs.Services;

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
	private readonly ulong[] _activeEntityBitset = new ulong[BITSET_CHUNKS];
	private unsafe ulong* _externalActiveEntityBitset;

	public int Count => _count;

	public ComponentBuffer<byte> AliveBuffer => _alive;
	public ComponentBuffer<ComponentMask> EntityMasksBuffer => _entityMasks;
	public ulong[] ActiveEntityBitset => _activeEntityBitset;
	public unsafe ulong* ExternalActiveEntityBitset => _externalActiveEntityBitset;

	/// <summary>
	///     The per-match simulation that owns this world, or null for a bare world. Services reach their tuning, combat
	///     config and WASM evaluation engine through it, so two worlds in one process never read each other's settings.
	/// </summary>
	public SimulationContext? Context { get; set; }

	/// <summary>
	///     The world the current session runs in. Exactly one world is alive per process at a time, so a pure-data
	///     component reaches its own world's handle stores through this instead of holding a world reference. A world
	///     becomes ambient when it is constructed and stops being ambient when it is disposed, which is what lets the
	///     editor save its world, hand the process to a playtest world, and restore itself afterwards.
	/// </summary>
	public static EcsWorld? Ambient { get; private set; }

	/// <summary>
	///     The cold payload and shroud grid handles this world owns. Handles are allocation indices into these stores,
	///     so they are never sent over the wire and never shared with another world.
	/// </summary>
	public WorldPayloadStores PayloadStores { get; } = new WorldPayloadStores();

	/// <summary>
	///     Hands the session to the given world. A tool that deliberately holds more than one world calls this before
	///     touching each one so its components resolve handles in the world they belong to.
	/// </summary>
	public static void SetAmbient(EcsWorld? world) => Ambient = world;

	public EcsWorld()
	{
		Ambient = this;
	}

	public void BindExternalAliveMemory(IntPtr pointer, int lengthInBytes)
	{
		_alive.BindExternalMemory(pointer, lengthInBytes);
	}

	public void BindExternalEntityMasksMemory(IntPtr pointer, int lengthInBytes)
	{
		_entityMasks.BindExternalMemory(pointer, lengthInBytes);
	}

	public unsafe void BindSharedMemory(IntPtr basePtr, int guestBaseOffset = 0)
	{
		if (basePtr == IntPtr.Zero) return;

		_externalActiveEntityBitset = (ulong*)(basePtr + EcsSharedMemory.ACTIVE_ENTITY_BITSET_OFFSET);
		new ReadOnlySpan<ulong>(_activeEntityBitset).CopyTo(new Span<ulong>(_externalActiveEntityBitset, BITSET_CHUNKS));

		_alive.BindExternalMemory(basePtr + EcsSharedMemory.ALIVE_OFFSET, MAX_ENTITIES * sizeof(byte), MAX_ENTITIES);
		_entityMasks.BindExternalMemory(basePtr + EcsSharedMemory.ENTITY_MASKS_OFFSET, MAX_ENTITIES * sizeof(ComponentMask), MAX_ENTITIES);

		int* tableDir = (int*)(basePtr + EcsSharedMemory.COMPONENT_ARRAY_BASE_OFFSETS_OFFSET);
		int* stridesDir = (int*)(basePtr + EcsSharedMemory.COMPONENT_STRIDES_OFFSET);
		int baseAddr = guestBaseOffset != 0 ? guestBaseOffset : (int)basePtr;

		int currentOffset = EcsSharedMemory.COMPONENTS_START_OFFSET;
		void BindComponent<T>(int componentId, int count) where T : unmanaged
		{
			int offset = currentOffset;
			tableDir[componentId] = baseAddr + offset;
			stridesDir[componentId] = sizeof(T);
			BindExternalComponentMemory<T>(basePtr + offset, count * sizeof(T), count);
			currentOffset += count * sizeof(T);
		}

		BindComponent<Position>(ComponentIds.Position, MAX_ENTITIES);
		BindComponent<PreviousPosition>(ComponentIds.PreviousPosition, MAX_ENTITIES);
		BindComponent<Health>(ComponentIds.Health, MAX_ENTITIES);
		BindComponent<Mana>(ComponentIds.Mana, MAX_ENTITIES);
		BindComponent<Attack>(ComponentIds.Attack, MAX_ENTITIES);
		BindComponent<Resistances>(ComponentIds.Resistances, MAX_ENTITIES);
		BindComponent<MovementStats>(ComponentIds.MovementStats, MAX_ENTITIES);
		BindComponent<UnitOwnerPlayer>(ComponentIds.UnitOwnerPlayer, MAX_ENTITIES);
		BindComponent<Level>(ComponentIds.Level, MAX_ENTITIES);
		BindComponent<Experience>(ComponentIds.Experience, MAX_ENTITIES);
		BindComponent<ModelScale>(ComponentIds.ModelScale, MAX_ENTITIES);
		BindComponent<RotationY>(ComponentIds.RotationY, MAX_ENTITIES);
		BindComponent<PreviousRotationY>(ComponentIds.PreviousRotationY, MAX_ENTITIES);
		BindComponent<PlayerResources>(ComponentIds.PlayerResources, MAX_PLAYERS + 1);
		BindComponent<ScriptPlayer>(ComponentIds.ScriptPlayer, MAX_PLAYERS);
		BindComponent<PlayerPopulation>(ComponentIds.PlayerPopulation, MAX_PLAYERS + 1);
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
		Array.Clear(_activeEntityBitset);
		PayloadStores.Reset();
		if (ReferenceEquals(Ambient, this)) Ambient = null;
		unsafe
		{
			if (_externalActiveEntityBitset != null)
			{
				new Span<ulong>(_externalActiveEntityBitset, BITSET_CHUNKS).Clear();
			}
		}
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
		int chunk = id >> 6;
		ulong bit = 1UL << (id & 63);
		_activeEntityBitset[chunk] |= bit;
		unsafe
		{
			if (_externalActiveEntityBitset != null)
			{
				_externalActiveEntityBitset[chunk] |= bit;
			}
		}
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
		int chunk = id >> 6;
		ulong bitNot = ~(1UL << (id & 63));
		_activeEntityBitset[chunk] &= bitNot;
		unsafe
		{
			if (_externalActiveEntityBitset != null)
			{
				_externalActiveEntityBitset[chunk] &= bitNot;
			}
		}

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

	public void BindExternalComponentMemory<T>(IntPtr pointer, int lengthInBytes, int capacity = -1) where T : unmanaged
	{
		int cId = ComponentType<T>.Id;
		var buffer = GetOrCreateBuffer<T>(cId);
		buffer.BindExternalMemory(pointer, lengthInBytes, capacity);
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

	private const int SnapshotFormatVersion = 1;

	private int ComputeSnapshotByteCount()
	{
		const int headerSize = 32;
		int freeListBytes = _freeListCount * sizeof(int);
		return headerSize + freeListBytes + EcsSharedMemory.TOTAL_SIZE_BYTES;
	}

	public unsafe int ExportBinarySnapshot(Span<byte> destination)
	{
		int headerSize = 32;
		int freeListBytes = _freeListCount * sizeof(int);
		int sharedMemoryBytes = EcsSharedMemory.TOTAL_SIZE_BYTES;

		int totalBytesNeeded = headerSize + freeListBytes + sharedMemoryBytes;
		if (destination.Length < totalBytesNeeded)
		{
			return -totalBytesNeeded;
		}

		Span<byte> headerSpan = destination.Slice(0, headerSize);
		BinaryPrimitives.WriteUInt32LittleEndian(headerSpan.Slice(0, 4), 0x53434552);
		BinaryPrimitives.WriteInt32LittleEndian(headerSpan.Slice(4, 4), SnapshotFormatVersion);
		BinaryPrimitives.WriteInt32LittleEndian(headerSpan.Slice(8, 4), _count);
		BinaryPrimitives.WriteInt32LittleEndian(headerSpan.Slice(12, 4), _nextEntityId);
		BinaryPrimitives.WriteInt32LittleEndian(headerSpan.Slice(16, 4), _freeListCount);
		BinaryPrimitives.WriteInt32LittleEndian(headerSpan.Slice(20, 4), MAX_PLAYERS);
		BinaryPrimitives.WriteInt32LittleEndian(headerSpan.Slice(24, 4), _nextEntityId);
		BinaryPrimitives.WriteInt32LittleEndian(headerSpan.Slice(28, 4), sharedMemoryBytes);

		int offset = headerSize;

		if (_freeListCount > 0)
		{
			Span<byte> freeListDst = destination.Slice(offset, freeListBytes);
			MemoryMarshal.AsBytes(_freeList.AsSpan(0, _freeListCount)).CopyTo(freeListDst);
			offset += freeListBytes;
		}

		Span<byte> sharedDst = destination.Slice(offset, sharedMemoryBytes);
		if (_externalActiveEntityBitset != null)
		{
			new ReadOnlySpan<byte>((void*)_externalActiveEntityBitset, sharedMemoryBytes).CopyTo(sharedDst);
		}
		else
		{
			sharedDst.Clear();
			MemoryMarshal.AsBytes(_activeEntityBitset.AsSpan()).CopyTo(sharedDst.Slice(EcsSharedMemory.ACTIVE_ENTITY_BITSET_OFFSET, BITSET_CHUNKS * sizeof(ulong)));
			_alive.AsSpan().CopyTo(sharedDst.Slice(EcsSharedMemory.ALIVE_OFFSET, MAX_ENTITIES));
			MemoryMarshal.AsBytes(_entityMasks.AsSpan()).CopyTo(sharedDst.Slice(EcsSharedMemory.ENTITY_MASKS_OFFSET, MAX_ENTITIES * sizeof(ComponentMask)));
			fixed (byte* dstPtr = sharedDst)
			{
				int* tableDir = (int*)(dstPtr + EcsSharedMemory.COMPONENT_ARRAY_BASE_OFFSETS_OFFSET);
				int baseAddr = (int)_externalActiveEntityBitset;
				CopyBufferToSpan<Position>(sharedDst, EcsSharedMemory.POSITION_OFFSET, MAX_ENTITIES);
				CopyBufferToSpan<PreviousPosition>(sharedDst, tableDir[ComponentIds.PreviousPosition] != 0 ? tableDir[ComponentIds.PreviousPosition] - baseAddr : EcsSharedMemory.POSITION_OFFSET, MAX_ENTITIES);
				CopyBufferToSpan<Health>(sharedDst, EcsSharedMemory.HEALTH_OFFSET, MAX_ENTITIES);
				CopyBufferToSpan<Mana>(sharedDst, EcsSharedMemory.MANA_OFFSET, MAX_ENTITIES);
				CopyBufferToSpan<Attack>(sharedDst, EcsSharedMemory.ATTACK_OFFSET, MAX_ENTITIES);
				CopyBufferToSpan<Resistances>(sharedDst, EcsSharedMemory.ARMOR_OFFSET, MAX_ENTITIES);
				CopyBufferToSpan<MovementStats>(sharedDst, EcsSharedMemory.MOVEMENT_STATS_OFFSET, MAX_ENTITIES);
				CopyBufferToSpan<UnitOwnerPlayer>(sharedDst, EcsSharedMemory.UNIT_OWNER_PLAYER_OFFSET, MAX_ENTITIES);
				CopyBufferToSpan<Level>(sharedDst, EcsSharedMemory.LEVEL_OFFSET, MAX_ENTITIES);
				CopyBufferToSpan<Experience>(sharedDst, EcsSharedMemory.EXPERIENCE_OFFSET, MAX_ENTITIES);
				CopyBufferToSpan<ModelScale>(sharedDst, EcsSharedMemory.MODEL_SCALE_OFFSET, MAX_ENTITIES);
				CopyBufferToSpan<RotationY>(sharedDst, EcsSharedMemory.ROTATION_Y_OFFSET, MAX_ENTITIES);
				CopyBufferToSpan<PreviousRotationY>(sharedDst, tableDir[ComponentIds.PreviousRotationY] != 0 ? tableDir[ComponentIds.PreviousRotationY] - baseAddr : EcsSharedMemory.ROTATION_Y_OFFSET, MAX_ENTITIES);
			}
			CopyBufferToSpan<PlayerResources>(sharedDst, EcsSharedMemory.PLAYER_RESOURCES_OFFSET, MAX_PLAYERS + 1);
			CopyBufferToSpan<ScriptPlayer>(sharedDst, EcsSharedMemory.SCRIPT_PLAYER_OFFSET, MAX_PLAYERS);
			CopyBufferToSpan<PlayerPopulation>(sharedDst, EcsSharedMemory.PLAYER_POPULATION_OFFSET, MAX_PLAYERS + 1);
		}
		offset += sharedMemoryBytes;
		return offset;
	}

	private unsafe void CopyBufferToSpan<T>(Span<byte> sharedDst, int offset, int count) where T : unmanaged
	{
		int cId = ComponentType<T>.Id;
		if (_buffers[cId] is ComponentBuffer<T> buffer)
		{
			int elements = Math.Min(buffer.Capacity, count);
			int bytesToCopy = elements * sizeof(T);
			MemoryMarshal.AsBytes(buffer.AsSpan().Slice(0, elements)).CopyTo(sharedDst.Slice(offset, bytesToCopy));
		}
	}

	public unsafe byte[] ExportBinarySnapshot()
	{
		byte[] buffer = new byte[ComputeSnapshotByteCount()];
		ExportBinarySnapshot(buffer);
		return buffer;
	}

	public unsafe void ImportBinarySnapshot(ReadOnlySpan<byte> snapshotPayload)
	{
		if (snapshotPayload.Length < 32) return;

		uint magic = BinaryPrimitives.ReadUInt32LittleEndian(snapshotPayload.Slice(0, 4));
		if (magic != 0x53434552) return;

		int version = BinaryPrimitives.ReadInt32LittleEndian(snapshotPayload.Slice(4, 4));
		if (version != SnapshotFormatVersion) return;
		int count = BinaryPrimitives.ReadInt32LittleEndian(snapshotPayload.Slice(8, 4));
		int nextEntityId = BinaryPrimitives.ReadInt32LittleEndian(snapshotPayload.Slice(12, 4));
		int freeListCount = BinaryPrimitives.ReadInt32LittleEndian(snapshotPayload.Slice(16, 4));
		int playerSlotCount = BinaryPrimitives.ReadInt32LittleEndian(snapshotPayload.Slice(20, 4));
		int activeEntityMax = BinaryPrimitives.ReadInt32LittleEndian(snapshotPayload.Slice(24, 4));
		int sharedMemoryBytes = BinaryPrimitives.ReadInt32LittleEndian(snapshotPayload.Slice(28, 4));
		if (sharedMemoryBytes <= 0)
		{
			sharedMemoryBytes = EcsSharedMemory.TOTAL_SIZE_BYTES;
		}

		_count = count;
		_nextEntityId = nextEntityId;
		_freeListCount = freeListCount;

		int offset = 32;

		if (_freeListCount > 0)
		{
			int freeListBytes = _freeListCount * sizeof(int);
			ReadOnlySpan<int> srcFreeList = MemoryMarshal.Cast<byte, int>(snapshotPayload.Slice(offset, freeListBytes));
			srcFreeList.CopyTo(_freeList);
			offset += freeListBytes;
		}

		if (snapshotPayload.Length < offset + sharedMemoryBytes) return;

		ReadOnlySpan<byte> sharedSrc = snapshotPayload.Slice(offset, sharedMemoryBytes);

		if (_externalActiveEntityBitset != null)
		{
			Span<byte> sharedDst = new Span<byte>((void*)_externalActiveEntityBitset, sharedMemoryBytes);
			sharedSrc.CopyTo(sharedDst);
			ReadOnlySpan<ulong> bitsetSrc = MemoryMarshal.Cast<byte, ulong>(sharedSrc.Slice(EcsSharedMemory.ACTIVE_ENTITY_BITSET_OFFSET, BITSET_CHUNKS * sizeof(ulong)));
			bitsetSrc.CopyTo(_activeEntityBitset);
		}
		else
		{
			fixed (byte* srcPtr = sharedSrc)
			{
				ReadOnlySpan<ulong> bitsetSrc = MemoryMarshal.Cast<byte, ulong>(sharedSrc.Slice(EcsSharedMemory.ACTIVE_ENTITY_BITSET_OFFSET, BITSET_CHUNKS * sizeof(ulong)));
				bitsetSrc.CopyTo(_activeEntityBitset);

				ReadOnlySpan<byte> aliveSrc = sharedSrc.Slice(EcsSharedMemory.ALIVE_OFFSET, MAX_ENTITIES);
				aliveSrc.CopyTo(_alive.AsSpan());

				ReadOnlySpan<ComponentMask> masksSrc = MemoryMarshal.Cast<byte, ComponentMask>(sharedSrc.Slice(EcsSharedMemory.ENTITY_MASKS_OFFSET, MAX_ENTITIES * sizeof(ComponentMask)));
				masksSrc.CopyTo(_entityMasks.AsSpan());

				int* tableDir = (int*)(srcPtr + EcsSharedMemory.COMPONENT_ARRAY_BASE_OFFSETS_OFFSET);
				int* stridesDir = (int*)(srcPtr + EcsSharedMemory.COMPONENT_STRIDES_OFFSET);
				int posBase = tableDir[ComponentIds.Position];
				int baseShift = posBase >= EcsSharedMemory.COMPONENTS_START_OFFSET ? posBase - EcsSharedMemory.COMPONENTS_START_OFFSET : 0;
				int srcLen = sharedSrc.Length;

				RestoreComponentFromTable<Position>(srcPtr, tableDir, stridesDir, baseShift, srcLen, ComponentIds.Position, MAX_ENTITIES);
				RestoreComponentFromTable<PreviousPosition>(srcPtr, tableDir, stridesDir, baseShift, srcLen, ComponentIds.PreviousPosition, MAX_ENTITIES);
				RestoreComponentFromTable<Health>(srcPtr, tableDir, stridesDir, baseShift, srcLen, ComponentIds.Health, MAX_ENTITIES);
				RestoreComponentFromTable<Mana>(srcPtr, tableDir, stridesDir, baseShift, srcLen, ComponentIds.Mana, MAX_ENTITIES);
				RestoreComponentFromTable<Attack>(srcPtr, tableDir, stridesDir, baseShift, srcLen, ComponentIds.Attack, MAX_ENTITIES);
				RestoreComponentFromTable<Resistances>(srcPtr, tableDir, stridesDir, baseShift, srcLen, ComponentIds.Resistances, MAX_ENTITIES);
				RestoreComponentFromTable<MovementStats>(srcPtr, tableDir, stridesDir, baseShift, srcLen, ComponentIds.MovementStats, MAX_ENTITIES);
				RestoreComponentFromTable<UnitOwnerPlayer>(srcPtr, tableDir, stridesDir, baseShift, srcLen, ComponentIds.UnitOwnerPlayer, MAX_ENTITIES);
				RestoreComponentFromTable<Level>(srcPtr, tableDir, stridesDir, baseShift, srcLen, ComponentIds.Level, MAX_ENTITIES);
				RestoreComponentFromTable<Experience>(srcPtr, tableDir, stridesDir, baseShift, srcLen, ComponentIds.Experience, MAX_ENTITIES);
				RestoreComponentFromTable<ModelScale>(srcPtr, tableDir, stridesDir, baseShift, srcLen, ComponentIds.ModelScale, MAX_ENTITIES);
				RestoreComponentFromTable<RotationY>(srcPtr, tableDir, stridesDir, baseShift, srcLen, ComponentIds.RotationY, MAX_ENTITIES);
				RestoreComponentFromTable<PreviousRotationY>(srcPtr, tableDir, stridesDir, baseShift, srcLen, ComponentIds.PreviousRotationY, MAX_ENTITIES);
				RestoreComponentFromTable<PlayerResources>(srcPtr, tableDir, stridesDir, baseShift, srcLen, ComponentIds.PlayerResources, MAX_PLAYERS + 1);
				RestoreComponentFromTable<ScriptPlayer>(srcPtr, tableDir, stridesDir, baseShift, srcLen, ComponentIds.ScriptPlayer, MAX_PLAYERS);
				RestoreComponentFromTable<PlayerPopulation>(srcPtr, tableDir, stridesDir, baseShift, srcLen, ComponentIds.PlayerPopulation, MAX_PLAYERS + 1);
			}
		}

	}

	private unsafe void RestoreComponentFromTable<T>(byte* srcPtr, int* tableDir, int* stridesDir, int baseShift, int srcLen, int componentId, int count) where T : unmanaged
	{
		int rawBase = tableDir[componentId];
		if (rawBase == 0) return;
		int compOffset = rawBase - baseShift;
		int srcStride = stridesDir != null ? stridesDir[componentId] : sizeof(T);
		if (srcStride <= 0) srcStride = sizeof(T);
		if (compOffset >= 0 && compOffset + (count * srcStride) <= srcLen)
		{
			int cId = ComponentType<T>.Id;
			var buffer = GetOrCreateBuffer<T>(cId);
			int elements = Math.Min(buffer.Capacity, count);
			if (srcStride == sizeof(T))
			{
				new ReadOnlySpan<T>(srcPtr + compOffset, elements).CopyTo(buffer.AsSpan().Slice(0, elements));
			}
			else
			{
				int bytesToCopy = Math.Min(sizeof(T), srcStride);
				var dstSpan = buffer.AsSpan();
				for (int i = 0; i < elements; i++)
				{
					fixed (T* dstPtr = &dstSpan[i])
					{
						Buffer.MemoryCopy(srcPtr + compOffset + (i * srcStride), dstPtr, sizeof(T), bytesToCopy);
					}
				}
			}
		}
	}

	private unsafe void CopySpanToBuffer<T>(ReadOnlySpan<byte> sharedSrc, int offset, int count) where T : unmanaged
	{
		int cId = ComponentType<T>.Id;
		var buffer = GetOrCreateBuffer<T>(cId);
		int elements = Math.Min(buffer.Capacity, count);
		int bytesToCopy = elements * sizeof(T);
		ReadOnlySpan<T> src = MemoryMarshal.Cast<byte, T>(sharedSrc.Slice(offset, bytesToCopy));
		src.CopyTo(buffer.AsSpan().Slice(0, elements));
	}
}
