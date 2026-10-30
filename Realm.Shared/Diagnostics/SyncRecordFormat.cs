using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using Realm.Ecs;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Resources;
using Realm.Ecs.Replication;
using Realm.Ecs.Services;

namespace Realm.Shared.Diagnostics;

public static class SyncRecordFormat
{
	public static readonly uint Magic = BinaryPrimitives.ReadUInt32LittleEndian("RSNP"u8);

	public const int Version = 12;

	public const ulong NamedByEveryPeer = ulong.MaxValue;

	public const int PlayerSlotCount = 25;

	public const byte FlagDead = 1;
	public const byte FlagBuilding = 2;
	public const byte FlagDetailed = 4;
	public const byte FlagVisibleToRemoteClient = 8;
	public const byte FlagCulled = 32;

	public delegate bool ComponentComparabilityMask(int componentTypeId, ReadOnlySpan<byte> payload, out ulong namedByPeers);

	public readonly struct SyncUnitRecord
	{
		public readonly int EntityId;
		public readonly int OwnerPlayerIndex;
		public readonly Vector3 Position;
		public readonly float RotationY;
		public readonly float CurrentHp;
		public readonly float MaxHp;
		public readonly Vector3 AppliedPosition;
		public readonly byte Flags;

		public readonly ulong NamedByPeers;

		public SyncUnitRecord(int entityId, int ownerPlayerIndex, Vector3 position, float rotationY, float currentHp, float maxHp, Vector3 appliedPosition, byte flags, ulong namedByPeers = NamedByEveryPeer)
		{
			EntityId = entityId;
			OwnerPlayerIndex = ownerPlayerIndex;
			Position = position;
			RotationY = rotationY;
			CurrentHp = currentHp;
			MaxHp = maxHp;
			AppliedPosition = appliedPosition;
			Flags = flags;
			NamedByPeers = namedByPeers;
		}
	}

	public static void WriteHeader(BinaryWriter writer, bool isAuthority)
	{
		writer.Write(Magic);
		writer.Write(Version);
		writer.Write(isAuthority);
	}

	public static void WriteUnit(BinaryWriter writer, in SyncUnitRecord unit, EcsWorld? world, Entity digestEntity, ReplicatedScope scope, IReadOnlyDictionary<int, int>? clientToServerEntityMap, ComponentComparabilityMask? namedBy = null)
	{
		writer.Write(unit.EntityId);
		writer.Write(unit.OwnerPlayerIndex);
		writer.Write(unit.Position.X);
		writer.Write(unit.Position.Y);
		writer.Write(unit.Position.Z);
		writer.Write(unit.RotationY);
		writer.Write(unit.CurrentHp);
		writer.Write(unit.MaxHp);
		writer.Write(unit.AppliedPosition.X);
		writer.Write(unit.AppliedPosition.Y);
		writer.Write(unit.AppliedPosition.Z);
		writer.Write(unit.Flags);
		writer.Write(unit.NamedByPeers);
		WriteComponentDigests(writer, world, digestEntity, scope, clientToServerEntityMap, namedBy);
	}

	public static void WriteComponentDigests(BinaryWriter writer, EcsWorld? world, Entity entity, ReplicatedScope scope, IReadOnlyDictionary<int, int>? clientToServerEntityMap, ComponentComparabilityMask? namedBy = null)
	{
		_digestBuffer.Clear();

		if (world != null && entity != Entity.Null && world.IsAlive(entity))
		{
			IReadOnlyList<IReplicatedComponentCodec> codecs = ReplicatedComponents.All;
			for (int index = 0; index < codecs.Count; index++)
			{
				IReplicatedComponentCodec codec = codecs[index];
				if (codec.Scope != scope) continue;

				int size = codec.SizeInBytes;
				if (_codecScratch.Length < size) Array.Resize(ref _codecScratch, size);
				Span<byte> payload = _codecScratch.AsSpan(0, size);
				payload.Clear();
				if (!codec.TryWrite(world, entity, payload)) continue;

				ulong namedByMask = NamedByEveryPeer;
				if (namedBy != null && !namedBy(codec.ComponentTypeId, payload, out namedByMask)) continue;

				NormalizeEntityReferences(payload, codec.ComponentTypeId, clientToServerEntityMap);
				_digestBuffer.Add(new ComponentDigest(codec.ComponentTypeId, HashPayload(payload), namedByMask));
			}
		}

		_digestBuffer.Sort(static (left, right) => left.ComponentTypeId.CompareTo(right.ComponentTypeId));
		writer.Write(_digestBuffer.Count);
		for (int index = 0; index < _digestBuffer.Count; index++)
		{
			writer.Write(_digestBuffer[index].ComponentTypeId);
			writer.Write(_digestBuffer[index].Hash);
			bool isScoped = _digestBuffer[index].NamedByMask != NamedByEveryPeer;
			writer.Write((byte)(isScoped ? 1 : 0));
			if (isScoped) writer.Write(_digestBuffer[index].NamedByMask);
		}
	}

	public static void WritePlayerScope(BinaryWriter writer, EcsWorld? world, Entity worldEntity, ReplicatedScope scope, IReadOnlyDictionary<int, int>? clientToServerEntityMap)
	{
		Entity[]? playerEntities = null;
		if (world != null && worldEntity != Entity.Null && world.IsAlive(worldEntity) && world.Has<NetworkMappingState>(worldEntity))
		{
			playerEntities = world.Get<NetworkMappingState>(worldEntity).PlayerEntities;
		}

		int playerCount = playerEntities?.Length ?? 0;
		if (playerCount > PlayerSlotCount) playerCount = PlayerSlotCount;
		writer.Write(playerCount);
		for (int playerIndex = 0; playerIndex < playerCount; playerIndex++)
		{
			writer.Write(playerIndex);
			WriteComponentDigests(writer, world, playerEntities![playerIndex], scope, clientToServerEntityMap);
		}

		WriteComponentDigests(writer, world, worldEntity, ReplicatedScope.World, clientToServerEntityMap);
	}

	public static void WriteLootItem(BinaryWriter writer, int uniqueId, ulong namedByPeers, EcsWorld? world, Entity entity)
	{
		writer.Write(uniqueId);
		writer.Write(namedByPeers);

		GroundLoot loot = world != null && world.IsAlive(entity) && world.TryGet(entity, out GroundLoot readLoot) ? readLoot : default;
		Vector3 position = world != null && world.IsAlive(entity) && world.TryGet(entity, out Position readPosition) ? readPosition.Value : Vector3.Zero;

		writer.Write(loot.ItemTypeId ?? string.Empty);
		writer.Write(loot.Charges);
		writer.Write(loot.Lifetime);
		writer.Write(loot.RemainingLifetime);
		writer.Write(loot.PickupRadius);
		writer.Write(loot.AutoPickup);
		writer.Write(loot.OwnerPlayerIndex);
		writer.Write(position.X);
		writer.Write(position.Y);
		writer.Write(position.Z);
	}

	public static void WriteNavigationState(BinaryWriter writer, in NavigationParityDigest digest)
	{
		writer.Write(digest.HasTerrainState);
		writer.Write(digest.HasNavMesh);
		writer.Write(digest.Width);
		writer.Write(digest.Depth);
		writer.Write(digest.PathingHash);
		writer.Write(digest.WalkableCellCount);
		writer.Write(digest.BlockedCellCount);
		writer.Write(digest.PolyCount);
		writer.Write(digest.GroundPolyCount);
		writer.Write(digest.PolyHash);
		writer.Write(digest.ObstacleVolumeCount);
		writer.Write(digest.ObstacleHash);
	}

	private static void NormalizeEntityReferences(Span<byte> payload, int componentTypeId, IReadOnlyDictionary<int, int>? clientToServerEntityMap)
	{
		if (clientToServerEntityMap == null) return;
		if (!_entityReferencePayloadOffsets.TryGetValue(componentTypeId, out int offset)) return;
		if (payload.Length < offset + sizeof(int)) return;

		int localEntityId = MemoryMarshal.Read<int>(payload.Slice(offset, sizeof(int)));
		int serverEntityId = -1;
		if (localEntityId >= 0 && !clientToServerEntityMap.TryGetValue(localEntityId, out serverEntityId)) serverEntityId = -1;
		MemoryMarshal.Write(payload.Slice(offset, sizeof(int)), in serverEntityId);
	}

	private static uint HashPayload(ReadOnlySpan<byte> payload)
	{
		uint hash = 2166136261u;
		for (int index = 0; index < payload.Length; index++)
		{
			hash ^= payload[index];
			hash *= 16777619u;
		}

		return hash;
	}

	private readonly struct ComponentDigest
	{
		public readonly int ComponentTypeId;
		public readonly uint Hash;
		public readonly ulong NamedByMask;

		public ComponentDigest(int componentTypeId, uint hash, ulong namedByMask)
		{
			ComponentTypeId = componentTypeId;
			Hash = hash;
			NamedByMask = namedByMask;
		}
	}

	private static readonly Dictionary<int, int> _entityReferencePayloadOffsets = new()
	{
		{ ComponentType<AttackTarget>.Id, 0 },
		{ ComponentType<LastAttacker>.Id, 0 },
		{ ComponentType<Follow>.Id, 0 },
		{ ComponentType<BuildTask>.Id, 0 }
	};

	private static readonly List<ComponentDigest> _digestBuffer = new();
	private static byte[] _codecScratch = new byte[1024];
}
