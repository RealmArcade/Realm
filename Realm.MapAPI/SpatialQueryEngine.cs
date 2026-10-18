using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Realm.MapAPI;

/// <summary>
/// Provides high-frequency in-guest spatial and entity query processing directly over shared WASM linear memory with zero host boundary crossings.
/// </summary>
public static unsafe class SpatialQueryEngine
{
	/// <summary>
	/// Scans the active entity bitset in WASM linear memory to retrieve all currently living unit handles.
	/// </summary>
	/// <returns>A read-only span of unit handles.</returns>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static ReadOnlySpan<UnitId> GetAllUnits()
	{
		Span<UnitId> buffer = QueryResultBuffer.AllocateSlice();
		int count = 0;

		ulong* activeBitset = EcsSharedMemory.GetActiveEntityBitset();
		byte* alive = EcsSharedMemory.Alive;

		for (int chunkIndex = 0; chunkIndex < EcsSharedMemory.BITSET_CHUNKS; chunkIndex++)
		{
			ulong word = activeBitset[chunkIndex];
			while (word != 0)
			{
				int bit = BitOperations.TrailingZeroCount(word);
				int entityId = (chunkIndex << 6) + bit;

				if (alive[entityId] != 0)
				{
					buffer[count++] = new UnitId(entityId);
				}

				word &= word - 1;
			}
		}

		return buffer.Slice(0, count);
	}

	/// <summary>
	/// Scans the active entity bitset in WASM linear memory to retrieve all living units owned by the specified player index.
	/// </summary>
	/// <param name="playerIndex">The zero-based player slot index.</param>
	/// <returns>A read-only span of unit handles owned by the player.</returns>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static ReadOnlySpan<UnitId> GetUnitsOwnedByPlayer(int playerIndex)
	{
		Span<UnitId> buffer = QueryResultBuffer.AllocateSlice();
		int count = 0;

		ulong* activeBitset = EcsSharedMemory.GetActiveEntityBitset();
		byte* alive = EcsSharedMemory.Alive;
		byte* ownersBase = (byte*)EcsSharedMemory.GetArrayBasePointer(ComponentIds.UnitOwnerPlayer);
		int ownerStride = EcsSharedMemory.ComponentStrides[ComponentIds.UnitOwnerPlayer];

		for (int chunkIndex = 0; chunkIndex < EcsSharedMemory.BITSET_CHUNKS; chunkIndex++)
		{
			ulong word = activeBitset[chunkIndex];
			while (word != 0)
			{
				int bit = BitOperations.TrailingZeroCount(word);
				int entityId = (chunkIndex << 6) + bit;

				if (alive[entityId] != 0 && ((UnitOwnerPlayer*)(ownersBase + entityId * ownerStride))->PlayerIndex == playerIndex)
				{
					buffer[count++] = new UnitId(entityId);
				}

				word &= word - 1;
			}
		}

		return buffer.Slice(0, count);
	}

	/// <summary>
	/// Scans the active entity bitset in WASM linear memory to retrieve all living units within a specified radius of a 3D position.
	/// </summary>
	/// <param name="center">The center position vector in 3D world space.</param>
	/// <param name="radius">The query radius distance.</param>
	/// <returns>A read-only span of unit handles inside the radius.</returns>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static ReadOnlySpan<UnitId> GetUnitsInRadius(Vector3 center, float radius)
	{
		Span<UnitId> buffer = QueryResultBuffer.AllocateSlice();
		int count = 0;
		float radiusSq = radius * radius;

		ulong* activeBitset = EcsSharedMemory.GetActiveEntityBitset();
		byte* alive = EcsSharedMemory.Alive;
		byte* positionsBase = (byte*)EcsSharedMemory.GetArrayBasePointer(ComponentIds.Position);
		int positionStride = EcsSharedMemory.ComponentStrides[ComponentIds.Position];

		for (int chunkIndex = 0; chunkIndex < EcsSharedMemory.BITSET_CHUNKS; chunkIndex++)
		{
			ulong word = activeBitset[chunkIndex];
			while (word != 0)
			{
				int bit = BitOperations.TrailingZeroCount(word);
				int entityId = (chunkIndex << 6) + bit;

				if (alive[entityId] != 0)
				{
					Vector3 pos = ((Position*)(positionsBase + entityId * positionStride))->Value;
					float dx = pos.X - center.X;
					float dy = pos.Y - center.Y;
					float dz = pos.Z - center.Z;
					if (dx * dx + dy * dy + dz * dz <= radiusSq)
					{
						buffer[count++] = new UnitId(entityId);
					}
				}

				word &= word - 1;
			}
		}

		return buffer.Slice(0, count);
	}
}
