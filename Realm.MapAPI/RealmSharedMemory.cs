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

	private static readonly IntPtr _basePtr;
	private static readonly SharedPlayerSlot* _playerSlots;
	private static readonly SharedEntitySlot* _entitySlots;
	private static SharedPlayerSlot _dummyPlayerSlot;
	private static SharedEntitySlot _dummyEntitySlot;

	/// <summary>
	/// Gets the global base pointer symbol for shared memory linear layout.
	/// </summary>
	public static IntPtr g_realmSharedMemoryBase => _basePtr;

	/// <summary>
	/// Gets the raw pointer to the player slots array.
	/// </summary>
	public static SharedPlayerSlot* PlayerSlots => _playerSlots;

	/// <summary>
	/// Gets the raw pointer to the entity slots array.
	/// </summary>
	public static SharedEntitySlot* EntitySlots => _entitySlots;

	static RealmSharedMemory()
	{
		int playersSize = sizeof(SharedPlayerSlot) * MAX_PLAYERS;
		int entitiesSize = sizeof(SharedEntitySlot) * MAX_ENTITIES;
		int totalSize = playersSize + entitiesSize;

		_basePtr = Marshal.AllocHGlobal(totalSize);
		new Span<byte>((void*)_basePtr, totalSize).Clear();

		_playerSlots = (SharedPlayerSlot*)_basePtr;
		_entitySlots = (SharedEntitySlot*)(_basePtr + playersSize);
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
}
