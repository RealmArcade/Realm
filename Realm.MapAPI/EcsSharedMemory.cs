using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Realm.MapAPI;

/// <summary>
/// Manages the zero-allocation Structure-of-Arrays (SoA) shared linear memory region between WebAssembly guest and host runtime contexts.
/// </summary>
public static unsafe class EcsSharedMemory
{
	/// <summary>The maximum number of player slots allocated in shared memory.</summary>
	public const int MAX_PLAYERS = 25;
	/// <summary>The maximum number of entity slots allocated in shared memory.</summary>
	public const int MAX_ENTITIES = 8192;
	/// <summary>The number of 64-bit words required to represent active entity bitsets for all entities.</summary>
	public const int BITSET_CHUNKS = MAX_ENTITIES / 64;

	/// <summary>Offset to the active entity bitset word array.</summary>
	public const int ACTIVE_ENTITY_BITSET_OFFSET = 0;
	/// <summary>Offset to the entity alive bytes array.</summary>
	public const int ALIVE_OFFSET = 1024;
	/// <summary>Offset to the entity component masks array.</summary>
	public const int ENTITY_MASKS_OFFSET = 9216;
	/// <summary>Offset to the entity positions array.</summary>
	public const int POSITION_OFFSET = 271360;
	/// <summary>Offset to the entity health components array.</summary>
	public const int HEALTH_OFFSET = 369664;
	/// <summary>Offset to the entity mana components array.</summary>
	public const int MANA_OFFSET = 533504;
	/// <summary>Offset to the entity attack components array.</summary>
	public const int ATTACK_OFFSET = 631808;
	/// <summary>Offset to the entity armor components array.</summary>
	public const int ARMOR_OFFSET = 1221632;
	/// <summary>Offset to the entity movement stats array.</summary>
	public const int MOVEMENT_STATS_OFFSET = 1319936;
	/// <summary>Offset to the entity unit owner player array.</summary>
	public const int UNIT_OWNER_PLAYER_OFFSET = 1483776;
	/// <summary>Offset to the entity level array.</summary>
	public const int LEVEL_OFFSET = 1516544;
	/// <summary>Offset to the entity experience array.</summary>
	public const int EXPERIENCE_OFFSET = 1549312;
	/// <summary>Offset to the entity model scale array.</summary>
	public const int MODEL_SCALE_OFFSET = 1582080;
	/// <summary>Offset to the entity rotation Y-axis array.</summary>
	public const int ROTATION_Y_OFFSET = 1614848;
	/// <summary>Offset to the player resources array.</summary>
	public const int PLAYER_RESOURCES_OFFSET = 1647616;
	/// <summary>Offset to the script player array.</summary>
	public const int SCRIPT_PLAYER_OFFSET = 1648816;
	/// <summary>Offset to the player population array.</summary>
	public const int PLAYER_POPULATION_OFFSET = 1649516;
	/// <summary>Total byte capacity of the contiguous shared memory block.</summary>
	public const int TOTAL_SIZE_BYTES = 1649716;

	private static IntPtr _basePtr;
	private static ulong* _activeEntityBitset;
	private static byte* _alive;
	private static ComponentMask* _entityMasks;
	private static Position* _positions;
	private static Health* _healths;
	private static Mana* _manas;
	private static Attack* _attacks;
	private static Armor* _armors;
	private static MovementStats* _movementStats;
	private static UnitOwnerPlayer* _unitOwnerPlayers;
	private static Level* _levels;
	private static Experience* _experiences;
	private static ModelScale* _modelScales;
	private static RotationY* _rotationYs;
	private static PlayerResources* _playerResources;
	private static ScriptPlayer* _scriptPlayers;
	private static PlayerPopulation* _playerPopulations;

	/// <summary>
	/// The global static base address pointer for shared memory linear layout visible to native debuggers.
	/// </summary>
	public static IntPtr g_realmSharedMemoryBase = IntPtr.Zero;

	/// <summary>Gets the raw pointer to the active entity bitset words array.</summary>
	public static ulong* ActiveEntityBitset => _activeEntityBitset;
	/// <summary>Gets the raw pointer to the entity alive bytes array.</summary>
	public static byte* Alive => _alive;
	/// <summary>Gets the raw pointer to the entity component masks array.</summary>
	public static ComponentMask* EntityMasks => _entityMasks;
	/// <summary>Gets the raw pointer to the entity position vectors array.</summary>
	public static Position* Position => _positions;
	/// <summary>Gets the raw pointer to the entity health components array.</summary>
	public static Health* Health => _healths;
	/// <summary>Gets the raw pointer to the entity mana components array.</summary>
	public static Mana* Mana => _manas;
	/// <summary>Gets the raw pointer to the entity attack components array.</summary>
	public static Attack* Attack => _attacks;
	/// <summary>Gets the raw pointer to the entity armor components array.</summary>
	public static Armor* Armor => _armors;
	/// <summary>Gets the raw pointer to the entity movement stats components array.</summary>
	public static MovementStats* MovementStats => _movementStats;
	/// <summary>Gets the raw pointer to the unit owner player index array.</summary>
	public static UnitOwnerPlayer* UnitOwnerPlayer => _unitOwnerPlayers;
	/// <summary>Gets the raw pointer to the unit experience level array.</summary>
	public static Level* Level => _levels;
	/// <summary>Gets the raw pointer to the unit experience points array.</summary>
	public static Experience* Experience => _experiences;
	/// <summary>Gets the raw pointer to the unit model scale array.</summary>
	public static ModelScale* ModelScale => _modelScales;
	/// <summary>Gets the raw pointer to the unit rotation Y-axis array.</summary>
	public static RotationY* RotationY => _rotationYs;
	/// <summary>Gets the raw pointer to the player resources stockpile array.</summary>
	public static PlayerResources* PlayerResources => _playerResources;
	/// <summary>Gets the raw pointer to the script player state array.</summary>
	public static ScriptPlayer* ScriptPlayer => _scriptPlayers;
	/// <summary>Gets the raw pointer to the player population limits array.</summary>
	public static PlayerPopulation* PlayerPopulation => _playerPopulations;

	static EcsSharedMemory()
	{
		_basePtr = Marshal.AllocHGlobal(TOTAL_SIZE_BYTES);
		g_realmSharedMemoryBase = _basePtr;
		new Span<byte>((void*)_basePtr, TOTAL_SIZE_BYTES).Clear();

		_activeEntityBitset = (ulong*)(_basePtr + ACTIVE_ENTITY_BITSET_OFFSET);
		_alive = (byte*)(_basePtr + ALIVE_OFFSET);
		_entityMasks = (ComponentMask*)(_basePtr + ENTITY_MASKS_OFFSET);
		_positions = (Position*)(_basePtr + POSITION_OFFSET);
		_healths = (Health*)(_basePtr + HEALTH_OFFSET);
		_manas = (Mana*)(_basePtr + MANA_OFFSET);
		_attacks = (Attack*)(_basePtr + ATTACK_OFFSET);
		_armors = (Armor*)(_basePtr + ARMOR_OFFSET);
		_movementStats = (MovementStats*)(_basePtr + MOVEMENT_STATS_OFFSET);
		_unitOwnerPlayers = (UnitOwnerPlayer*)(_basePtr + UNIT_OWNER_PLAYER_OFFSET);
		_levels = (Level*)(_basePtr + LEVEL_OFFSET);
		_experiences = (Experience*)(_basePtr + EXPERIENCE_OFFSET);
		_modelScales = (ModelScale*)(_basePtr + MODEL_SCALE_OFFSET);
		_rotationYs = (RotationY*)(_basePtr + ROTATION_Y_OFFSET);
		_playerResources = (PlayerResources*)(_basePtr + PLAYER_RESOURCES_OFFSET);
		_scriptPlayers = (ScriptPlayer*)(_basePtr + SCRIPT_PLAYER_OFFSET);
		_playerPopulations = (PlayerPopulation*)(_basePtr + PLAYER_POPULATION_OFFSET);
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
	/// Retrieves the raw pointer to the active entity bitset array in shared memory.
	/// </summary>
	/// <returns>A pointer to the array of 64-bit words representing active entity bitsets.</returns>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static ulong* GetActiveEntityBitset()
	{
		return _activeEntityBitset;
	}

	/// <summary>
	/// Checks whether an entity has a specific component bit set in its component mask.
	/// </summary>
	/// <param name="entityId">The entity identifier.</param>
	/// <param name="componentId">The component type identifier (0-255).</param>
	/// <returns>True if the component bit is present; otherwise false.</returns>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool HasComponent(int entityId, int componentId)
	{
		if (entityId <= 0 || entityId >= MAX_ENTITIES) return false;
		ref ComponentMask mask = ref _entityMasks[entityId];
		int word = componentId >> 6;
		int bit = componentId & 63;
		ulong bitVal = 1UL << bit;
		return word switch
		{
			0 => (mask.Mask0 & bitVal) != 0,
			1 => (mask.Mask1 & bitVal) != 0,
			2 => (mask.Mask2 & bitVal) != 0,
			3 => (mask.Mask3 & bitVal) != 0,
			_ => false
		};
	}
}
