using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Realm.MapAPI;

/// <summary>
/// Manages the zero-allocation shared linear memory region between WebAssembly guest and host runtime contexts.
/// </summary>
public static unsafe class RealmSharedMemory
{
	/// <summary>
	/// The maximum number of player slots allocated in shared memory.
	/// </summary>
	public const int MAX_PLAYERS = 25;

	/// <summary>
	/// The maximum number of entity slots allocated in shared memory.
	/// </summary>
	public const int MAX_ENTITIES = 8192;

	/// <summary>
	/// The number of 64-bit words required to represent active entity bitsets for all entities.
	/// </summary>
	public const int BITSET_CHUNKS = MAX_ENTITIES / 64;

	private static IntPtr _basePtr;
	private static SharedPlayerSlot* _playerSlots;
	private static SharedEntitySlot* _entitySlots;
	private static ulong* _activeEntityBitset;
	private static SharedPlayerSlot _dummyPlayerSlot;
	private static SharedEntitySlot _dummyEntitySlot;

	/// <summary>
	/// The global static base address pointer for shared memory linear layout visible to native debuggers.
	/// </summary>
	public static IntPtr g_realmSharedMemoryBase = IntPtr.Zero;

	/// <summary>
	/// Gets the raw pointer to the player slots array.
	/// </summary>
	public static SharedPlayerSlot* PlayerSlots => _playerSlots;

	/// <summary>
	/// Gets the raw pointer to the entity slots array.
	/// </summary>
	public static SharedEntitySlot* EntitySlots => _entitySlots;

	/// <summary>
	/// Gets the raw pointer to the active entity bitset words array.
	/// </summary>
	public static ulong* ActiveEntityBitset => _activeEntityBitset;

	static RealmSharedMemory()
	{
		int playersSize = sizeof(SharedPlayerSlot) * MAX_PLAYERS;
		int entitiesSize = sizeof(SharedEntitySlot) * MAX_ENTITIES;
		int bitsetSize = sizeof(ulong) * BITSET_CHUNKS;
		int totalSize = playersSize + entitiesSize + bitsetSize;

		_basePtr = Marshal.AllocHGlobal(totalSize);
		g_realmSharedMemoryBase = _basePtr;
		new Span<byte>((void*)_basePtr, totalSize).Clear();

		_playerSlots = (SharedPlayerSlot*)_basePtr;
		_entitySlots = (SharedEntitySlot*)(_basePtr + playersSize);
		_activeEntityBitset = (ulong*)(_basePtr + playersSize + entitiesSize);
	}

	/// <summary>
	/// Native entry point exported to WebAssembly host to retrieve the linear memory offset of the shared memory region.
	/// </summary>
	/// <returns>The 32-bit guest linear memory address offset.</returns>
	[UnmanagedCallersOnly(EntryPoint = "get_realm_shared_memory_base")]
	public static int GetRealmSharedMemoryBase()
	{
		return (int)_basePtr;
	}

	/// <summary>
	/// Retrieves a reference to the shared player slot at the specified index.
	/// </summary>
	/// <param name="index">The zero-based player slot index.</param>
	/// <returns>A reference to the shared player slot structure.</returns>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static ref SharedPlayerSlot GetPlayerSlot(int index)
	{
		if (index < 0 || index >= MAX_PLAYERS)
		{
			return ref _dummyPlayerSlot;
		}
		return ref _playerSlots[index];
	}

	/// <summary>
	/// Retrieves a reference to the shared entity slot at the specified index.
	/// </summary>
	/// <param name="index">The entity slot index.</param>
	/// <returns>A reference to the shared entity slot structure.</returns>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static ref SharedEntitySlot GetEntitySlot(int index)
	{
		if (index < 0 || index >= MAX_ENTITIES)
		{
			return ref _dummyEntitySlot;
		}
		return ref _entitySlots[index];
	}

	/// <summary>
	/// Retrieves the raw pointer to the active entity bitset array in shared memory.
	/// </summary>
	/// <returns>A pointer to the array of 64-bit words representing active entity bitsets.</returns>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static ulong* GetActiveEntityBitset()
	{
		return _activeEntityBitset;
	}
}
