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
