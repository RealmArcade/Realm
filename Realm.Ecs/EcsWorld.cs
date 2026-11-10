using System;
using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Realm.Ecs.Services;

namespace Realm.Ecs;

public delegate void EntityCallback(Entity entity);
public delegate void EntityCallback<T1>(Entity entity, ref T1 c1);
public delegate void EntityCallback<T1, T2>(Entity entity, ref T1 c1, ref T2 c2);
public delegate void EntityCallback<T1, T2, T3>(Entity entity, ref T1 c1, ref T2 c2, ref T3 c3);
public delegate void EntityCallback<T1, T2, T3, T4>(Entity entity, ref T1 c1, ref T2 c2, ref T3 c3, ref T4 c4);

/// <summary>
///     The entities and component arrays of one world. Owned by a single thread: entity creation, destruction and
///     component structure are written only by that thread, and parallel work inside a tick may read the world and write
///     only the slots it was given. Workers announce themselves with <see cref="EnterWorkerLease" /> so a structural write
///     from a forked lane fails loudly instead of corrupting a buffer a worker is reading.
/// </summary>
public partial class EcsWorld : IDisposable
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

	/// <summary>The base of the shared memory region this world is bound to, or <see cref="IntPtr.Zero" /> when it owns no shared region.</summary>
	public IntPtr SharedMemoryBasePointer { get; private set; }

	/// <summary>The linear memory offset the bound shared region sits at inside the WebAssembly guest.</summary>
	public int SharedMemoryGuestBaseOffset { get; private set; }

	private readonly object _componentBufferCreationLock = new object();
	private int _activeWorkerLeases;

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
	///     editor save its world, hand the process to a playtest world, and restore itself afterwards. It is read from
	///     lanes that do not own it, so it is published and read through <see cref="Volatile" /> rather than assigned.
	/// </summary>
	public static EcsWorld? Ambient => Volatile.Read(ref _ambientWorld);

	private static EcsWorld? _ambientWorld;

	/// <summary>
	///     The cold payload and shroud grid handles this world owns. Handles are allocation indices into these stores,
	///     so they are never sent over the wire and never shared with another world.
	/// </summary>
	public WorldPayloadStores PayloadStores { get; } = new WorldPayloadStores();

	/// <summary>
	///     Hands the session to the given world. A tool that deliberately holds more than one world calls this before
	///     touching each one so its components resolve handles in the world they belong to.
	/// </summary>
	public static void SetAmbient(EcsWorld? world) => Volatile.Write(ref _ambientWorld, world);

	public EcsWorld()
	{
		Volatile.Write(ref _ambientWorld, this);
	}

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
		if (ReferenceEquals(Ambient, this)) SetAmbient(null);
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
		AssertNotForkedIntoWorkers("Entity creation");

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

	public void Destroy(Entity entity)
	{
		AssertNotForkedIntoWorkers("Entity destruction");

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
}
