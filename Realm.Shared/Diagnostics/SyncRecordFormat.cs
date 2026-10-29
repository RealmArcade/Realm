using System;
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

/// <summary>
///     The binary shape of the ECS sync records a match writes: the stream the authority writes as it publishes and the
///     stream a client writes as it applies. Both processes have to agree on it, so it lives beside the other contracts
///     they share rather than inside either one of them.
/// </summary>
public static class SyncRecordFormat
{
	/// <summary>Identifies a sync record stream, so a file that is not one is refused rather than misread.</summary>
	public const uint Magic = 0x504E5352;

	/// <summary>Which layout of the records a stream holds. A reader refuses a stream it was not built for.</summary>
	public const int Version = 12;

	/// <summary>
	///     The mask that says every peer could name a reference, so the digest is a statement every peer can be held to.
	///     Any other mask names only the peers that were told about the entity the reference points at, because the
	///     shroud is decided per peer: a reference the authority sent to one screen and withheld from another is a
	///     statement the first screen can be judged on and the second cannot.
	/// </summary>
	public const ulong NamedByEveryPeer = ulong.MaxValue;

	/// <summary>How many player slots a record describes, whatever the match actually seats.</summary>
	public const int PlayerSlotCount = 25;

	public const byte FlagDead = 1;
	public const byte FlagBuilding = 2;
	public const byte FlagDetailed = 4;
	public const byte FlagVisibleToRemoteClient = 8;
	public const byte FlagLocallyPredicted = 16;
	public const byte FlagCulled = 32;

	/// <summary>
	///     Says whether the writer can state one component and which peers could name the entity it points at. A component
	///     that names no entity is comparable by everyone; a reference is comparable only by the peers that were told about
	///     the entity it points at, because a peer that was never told holds no name for it. Returning false means the
	///     writer has no statement to make and the digest is left out; returning true with an empty mask means the writer
	///     holds a value that no peer was ever given a name for, which is a statement the reader must not hold anyone to
	///     either — the difference between the two is what keeps a name this screen kept from an earlier description out
	///     of the comparison instead of being reported as a component the host does not have.
	/// </summary>
	public delegate bool ComponentComparabilityMask(int componentTypeId, ReadOnlySpan<byte> payload, out ulong namedByPeers);

	/// <summary>What one unit looked like on one sequence, as either process describes it.</summary>
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

		/// <summary>Which peers this sequence described the unit to, as the bits of the record's peer mask.</summary>
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

	/// <summary>Writes the file header both processes start a stream with.</summary>
	public static void WriteHeader(BinaryWriter writer, bool isAuthority)
	{
		writer.Write(Magic);
		writer.Write(Version);
		writer.Write(isAuthority);
	}

	/// <summary>
	///     Writes one unit's state as every side of the match describes it: where it is, how healthy it is, how far the
	///     screen that received it has applied it, and which components the entity carries. The entity the components are
	///     hashed from is passed in because a client records the unit under the authority's identifier while holding it
	///     under its own.
	/// </summary>
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
		// A unit only some peers were told about is still a unit those peers can be held to, so the record states which
		// peers it reached rather than leaving a reader to assume it reached everyone or no one.
		writer.Write(unit.NamedByPeers);
		WriteComponentDigests(writer, world, digestEntity, scope, clientToServerEntityMap, namedBy);
	}

	/// <summary>
	///     Writes a hash per replicated component the entity carries. A hash rather than the payload, because a record is
	///     a fingerprint of one ECS at one instant: two processes agreeing on every hash hold the same world, and one that
	///     disagrees names the component that differs without either side having to send its values.
	/// </summary>
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

				// Asked before the reference is rewritten, because the writer's own value is the one it can name, and a
				// reference it cannot name is exactly the statement it must leave out.
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
			// Only a reference is ever scoped, so the byte is written for every entry and the mask after it only for the
			// one entry in a unit's component set that can be about the shroud rather than about the world.
			bool isScoped = _digestBuffer[index].NamedByMask != NamedByEveryPeer;
			writer.Write((byte)(isScoped ? 1 : 0));
			if (isScoped) writer.Write(_digestBuffer[index].NamedByMask);
		}
	}

	/// <summary>
	///     Writes the state of every player seat. An order names an entity as its own process numbers it, so a client's
	///     copy is rewritten through that entity reference before it is hashed; the authority has nothing to rewrite.
	/// </summary>
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

	/// <summary>
	///     Writes one ground loot item's state, under the identifier both processes know it by, and which peers this
	///     sequence described it to. An item is stated as the values a screen draws it from rather than as a component
	///     digest: the life an item has left counts down on the authority every tick, so the two processes are never
	///     exactly equal and a hash would call every run a divergence.
	/// </summary>
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

	/// <summary>
	///     Writes what both processes' navigation looked like: the grid, the mesh baked over it and the obstacle volumes
	///     the bake consumed. A prediction can only choose the route the authority chose while the two agree on these.
	/// </summary>
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

	/// <summary>
	///     The components whose payload starts with an entity reference. A client hashes its copy of one of these after
	///     rewriting the reference to the server identifier, so the two processes hash the same number.
	/// </summary>
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
