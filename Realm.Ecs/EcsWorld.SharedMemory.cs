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
	/// <summary>
	///     States that this thread is about to do parallel work over the world and may only read it. Structural writes
	///     attempted while a lease is held are the defect this counter exists to catch, so it is asserted rather than
	///     enforced at run time.
	/// </summary>
	public void EnterWorkerLease() => Interlocked.Increment(ref _activeWorkerLeases);

	public void ExitWorkerLease() => Interlocked.Decrement(ref _activeWorkerLeases);

	public bool IsForkedIntoWorkers => Volatile.Read(ref _activeWorkerLeases) != 0;

	[Conditional("DEBUG")]
	private void AssertNotForkedIntoWorkers(string operation)
	{
		Debug.Assert(
			Volatile.Read(ref _activeWorkerLeases) == 0,
			operation + " mutated the world while parallel workers held it; structural changes belong to the owner thread between fork and join");
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

		SharedMemoryBasePointer = basePtr;
		SharedMemoryGuestBaseOffset = guestBaseOffset;

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
}
