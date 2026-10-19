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
	/// <summary>The maximum number of player entity slots allocated in shared memory (accounting for entity index 0).</summary>
	public const int MAX_PLAYER_ENTITIES = MAX_PLAYERS + 1;
	/// <summary>The maximum number of entity slots allocated in shared memory.</summary>
	public const int MAX_ENTITIES = 8192;
	/// <summary>The number of 64-bit words required to represent active entity bitsets for all entities.</summary>
	public const int BITSET_CHUNKS = MAX_ENTITIES / 64;
	/// <summary>The maximum number of distinct component types supported in the table directory.</summary>
	public const int MAX_COMPONENT_TYPES = 256;

	/// <summary>Offset to the component array base offsets directory table.</summary>
	public const int COMPONENT_ARRAY_BASE_OFFSETS_OFFSET = 0;
	/// <summary>Offset to the component strides directory table.</summary>
	public const int COMPONENT_STRIDES_OFFSET = 1024;
	/// <summary>Offset to the global random seed in the shared memory header.</summary>
	public const int RANDOM_SEED_OFFSET = 2044;
	/// <summary>Total byte size reserved for the shared memory header.</summary>
	public const int HEADER_SIZE = 2048;

	/// <summary>Offset to the active entity bitset word array.</summary>
	public const int ACTIVE_ENTITY_BITSET_OFFSET = 2048;
	/// <summary>Offset to the entity alive bytes array.</summary>
	public const int ALIVE_OFFSET = 3072;
	/// <summary>Offset to the entity component masks array.</summary>
	public const int ENTITY_MASKS_OFFSET = 11264;
	/// <summary>Offset at which contiguous component arrays begin.</summary>
	public const int COMPONENTS_START_OFFSET = 273408;

	/// <summary>Default offset to the entity positions array.</summary>
	public const int POSITION_OFFSET = 273408;
	/// <summary>Default offset to the entity health components array.</summary>
	public const int HEALTH_OFFSET = 371712;
	/// <summary>Default offset to the entity mana components array.</summary>
	public const int MANA_OFFSET = 535552;
	/// <summary>Default offset to the entity attack components array.</summary>
	public const int ATTACK_OFFSET = 633856;
	/// <summary>Default offset to the entity armor components array.</summary>
	public const int ARMOR_OFFSET = 1223680;
	/// <summary>Default offset to the entity movement stats array.</summary>
	public const int MOVEMENT_STATS_OFFSET = 1321984;
	/// <summary>Default offset to the entity unit owner player array.</summary>
	public const int UNIT_OWNER_PLAYER_OFFSET = 1485824;
	/// <summary>Default offset to the entity level array.</summary>
	public const int LEVEL_OFFSET = 1518592;
	/// <summary>Default offset to the entity experience array.</summary>
	public const int EXPERIENCE_OFFSET = 1551360;
	/// <summary>Default offset to the entity model scale array.</summary>
	public const int MODEL_SCALE_OFFSET = 1584128;
	/// <summary>Default offset to the entity rotation Y-axis array.</summary>
	public const int ROTATION_Y_OFFSET = 1616896;
	/// <summary>Default offset to the player resources array.</summary>
	public const int PLAYER_RESOURCES_OFFSET = 1649664;
	/// <summary>Default offset to the script player array.</summary>
	public const int SCRIPT_PLAYER_OFFSET = 1650912;
	/// <summary>Default offset to the player population array.</summary>
	public const int PLAYER_POPULATION_OFFSET = 1651612;
	/// <summary>Total byte capacity of the contiguous shared memory block.</summary>
	public const int TOTAL_SIZE_BYTES = 4194304;

	private static IntPtr _basePtr;
	private static int* _componentArrayBaseOffsets;
	private static int* _componentStrides;
	private static ulong* _activeEntityBitset;
	private static byte* _alive;
	private static ComponentMask* _entityMasks;

	/// <summary>
	/// The global static base address pointer for shared memory linear layout visible to native debuggers.
	/// </summary>
	public static IntPtr g_realmSharedMemoryBase = IntPtr.Zero;

	/// <summary>Gets the raw pointer to the component array base offsets directory table.</summary>
	public static int* ComponentArrayBaseOffsets => _componentArrayBaseOffsets != null ? _componentArrayBaseOffsets : (int*)(_basePtr + COMPONENT_ARRAY_BASE_OFFSETS_OFFSET);
	/// <summary>Gets the raw pointer to the component strides directory table.</summary>
	public static int* ComponentStrides => _componentStrides != null ? _componentStrides : (int*)(_basePtr + COMPONENT_STRIDES_OFFSET);
	/// <summary>Gets the raw pointer to the active entity bitset words array.</summary>
	public static ulong* ActiveEntityBitset => _activeEntityBitset;
	/// <summary>Gets the raw pointer to the entity alive bytes array.</summary>
	public static byte* Alive => _alive;
	/// <summary>Gets the raw pointer to the entity component masks array.</summary>
	public static ComponentMask* EntityMasks => _entityMasks;
	/// <summary>Gets the raw pointer to the entity position vectors array.</summary>
	public static Position* Position => (Position*)GetArrayBasePointer(ComponentIds.Position);
	/// <summary>Gets the raw pointer to the entity health components array.</summary>
	public static Health* Health => (Health*)GetArrayBasePointer(ComponentIds.Health);
	/// <summary>Gets the raw pointer to the entity mana components array.</summary>
	public static Mana* Mana => (Mana*)GetArrayBasePointer(ComponentIds.Mana);
	/// <summary>Gets the raw pointer to the entity attack components array.</summary>
	public static Attack* Attack => (Attack*)GetArrayBasePointer(ComponentIds.Attack);
	/// <summary>Gets the raw pointer to the entity armor components array.</summary>
	public static Armor* Armor => (Armor*)GetArrayBasePointer(ComponentIds.Armor);
	/// <summary>Gets the raw pointer to the entity movement stats components array.</summary>
	public static MovementStats* MovementStats => (MovementStats*)GetArrayBasePointer(ComponentIds.MovementStats);
	/// <summary>Gets the raw pointer to the unit owner player index array.</summary>
	public static UnitOwnerPlayer* UnitOwnerPlayer => (UnitOwnerPlayer*)GetArrayBasePointer(ComponentIds.UnitOwnerPlayer);
	/// <summary>Gets the raw pointer to the unit experience level array.</summary>
	public static Level* Level => (Level*)GetArrayBasePointer(ComponentIds.Level);
	/// <summary>Gets the raw pointer to the unit experience points array.</summary>
	public static Experience* Experience => (Experience*)GetArrayBasePointer(ComponentIds.Experience);
	/// <summary>Gets the raw pointer to the unit model scale array.</summary>
	public static ModelScale* ModelScale => (ModelScale*)GetArrayBasePointer(ComponentIds.ModelScale);
	/// <summary>Gets the raw pointer to the unit rotation Y-axis array.</summary>
	public static RotationY* RotationY => (RotationY*)GetArrayBasePointer(ComponentIds.RotationY);
	/// <summary>Gets the raw pointer to the player resources stockpile array.</summary>
	public static PlayerResources* PlayerResources => (PlayerResources*)GetArrayBasePointer(ComponentIds.PlayerResources);
	/// <summary>Gets the raw pointer to the script player state array.</summary>
	public static ScriptPlayer* ScriptPlayer => (ScriptPlayer*)GetArrayBasePointer(ComponentIds.ScriptPlayer);
	/// <summary>Gets the raw pointer to the player population limits array.</summary>
	public static PlayerPopulation* PlayerPopulation => (PlayerPopulation*)GetArrayBasePointer(ComponentIds.PlayerPopulation);

	/// <summary>
	/// Gets or sets the global random seed stored in shared memory between WebAssembly guest and host runtime contexts.
	/// </summary>
	public static int RandomSeed
	{
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		get => *(int*)(_basePtr + RANDOM_SEED_OFFSET);
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		set => *(int*)(_basePtr + RANDOM_SEED_OFFSET) = value;
	}

	static EcsSharedMemory()
	{
		_basePtr = Marshal.AllocHGlobal(TOTAL_SIZE_BYTES);
		g_realmSharedMemoryBase = _basePtr;
		new Span<byte>((void*)_basePtr, TOTAL_SIZE_BYTES).Clear();

		_componentArrayBaseOffsets = (int*)(_basePtr + COMPONENT_ARRAY_BASE_OFFSETS_OFFSET);
		_componentStrides = (int*)(_basePtr + COMPONENT_STRIDES_OFFSET);
		_activeEntityBitset = (ulong*)(_basePtr + ACTIVE_ENTITY_BITSET_OFFSET);
		_alive = (byte*)(_basePtr + ALIVE_OFFSET);
		_entityMasks = (ComponentMask*)(_basePtr + ENTITY_MASKS_OFFSET);

		int currentOffset = COMPONENTS_START_OFFSET;
		void RegisterComponent(int componentId, int byteSize, int stride)
		{
			_componentArrayBaseOffsets[componentId] = (int)(_basePtr + currentOffset);
			_componentStrides[componentId] = stride;
			currentOffset += byteSize;
		}

		RegisterComponent(ComponentIds.Position, MAX_ENTITIES * sizeof(Position), sizeof(Position));
		RegisterComponent(ComponentIds.Health, MAX_ENTITIES * sizeof(Health), sizeof(Health));
		RegisterComponent(ComponentIds.Mana, MAX_ENTITIES * sizeof(Mana), sizeof(Mana));
		RegisterComponent(ComponentIds.Attack, MAX_ENTITIES * sizeof(Attack), sizeof(Attack));
		RegisterComponent(ComponentIds.Armor, MAX_ENTITIES * sizeof(Armor), sizeof(Armor));
		RegisterComponent(ComponentIds.MovementStats, MAX_ENTITIES * sizeof(MovementStats), sizeof(MovementStats));
		RegisterComponent(ComponentIds.UnitOwnerPlayer, MAX_ENTITIES * sizeof(UnitOwnerPlayer), sizeof(UnitOwnerPlayer));
		RegisterComponent(ComponentIds.Level, MAX_ENTITIES * sizeof(Level), sizeof(Level));
		RegisterComponent(ComponentIds.Experience, MAX_ENTITIES * sizeof(Experience), sizeof(Experience));
		RegisterComponent(ComponentIds.ModelScale, MAX_ENTITIES * sizeof(ModelScale), sizeof(ModelScale));
		RegisterComponent(ComponentIds.RotationY, MAX_ENTITIES * sizeof(RotationY), sizeof(RotationY));
		RegisterComponent(ComponentIds.PlayerResources, MAX_PLAYER_ENTITIES * sizeof(PlayerResources), sizeof(PlayerResources));
		RegisterComponent(ComponentIds.ScriptPlayer, MAX_PLAYERS * sizeof(ScriptPlayer), sizeof(ScriptPlayer));
		RegisterComponent(ComponentIds.PlayerPopulation, MAX_PLAYER_ENTITIES * sizeof(PlayerPopulation), sizeof(PlayerPopulation));
	}

	/// <summary>
	/// Resolves the base pointer for a component array from the runtime table directory.
	/// </summary>
	/// <param name="componentId">The component type identifier.</param>
	/// <returns>A pointer to the base of the component array.</returns>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void* GetArrayBasePointer(int componentId)
	{
		int entry = ComponentArrayBaseOffsets[componentId];
		if (sizeof(IntPtr) == 4 && entry >= (int)_basePtr)
		{
			return (void*)entry;
		}
		return (void*)((byte*)_basePtr + entry);
	}

	/// <summary>
	/// Resolves the pointer to a component struct for a specific entity or player slot using dynamic stride arithmetic.
	/// </summary>
	/// <typeparam name="T">The unmanaged component struct type.</typeparam>
	/// <param name="componentId">The component type identifier.</param>
	/// <param name="id">The entity identifier or player slot index.</param>
	/// <returns>A typed pointer to the component instance in shared memory.</returns>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static T* GetComponent<T>(int componentId, int id) where T : unmanaged
	{
		byte* basePtr = (byte*)GetArrayBasePointer(componentId);
		int stride = ComponentStrides[componentId];
		return (T*)(basePtr + (id * stride));
	}

	/// <summary>
	/// Retrieves the linear memory offset of the shared memory region for the WebAssembly host.
	/// </summary>
	/// <returns>The 32-bit guest linear memory address offset.</returns>
	public static int GetRealmSharedMemoryBase()
	{
		return (int)_basePtr;
	}

	/// <summary>
	/// Sets the component array base offsets runtime table directory from the WebAssembly host.
	/// </summary>
	/// <param name="tableDirectoryGuestOffset">The 32-bit linear memory address of the table directory.</param>
	public static void SetRealmSharedMemoryTableDirectory(int tableDirectoryGuestOffset)
	{
		if (tableDirectoryGuestOffset != 0)
		{
			_componentArrayBaseOffsets = (int*)tableDirectoryGuestOffset;
		}
	}

	/// <summary>
	/// Sets the component strides runtime directory from the WebAssembly host.
	/// </summary>
	/// <param name="stridesGuestOffset">The 32-bit linear memory address of the strides directory.</param>
	public static void SetRealmSharedMemoryStrides(int stridesGuestOffset)
	{
		if (stridesGuestOffset != 0)
		{
			_componentStrides = (int*)stridesGuestOffset;
		}
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
