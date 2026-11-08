using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Realm.Client.Services;
using Realm.Ecs.Replication;
using Realm.Shared.Diagnostics;
using Realm.Shared.Network;
using Realm.Shared.Services;

namespace Realm.Client.Testing;

public static partial class MultiplayerEcsSyncTestSupport
{
	private static SortedDictionary<int, MultiplayerSnapshotRecord> ReadRecords(string path, out string truncation)
	{
		SortedDictionary<int, MultiplayerSnapshotRecord> records = new();
		truncation = string.Empty;
		try
		{
			ReadRecordsInto(path, records);
		}
		catch (EndOfStreamException)
		{
			int lastSequence = -1;
			foreach (KeyValuePair<int, MultiplayerSnapshotRecord> entry in records) lastSequence = entry.Key;

			truncation = records.Count == 0
				? $"{Path.GetFileName(path)} ends inside its first record and holds nothing comparable"
				: $"{Path.GetFileName(path)} ends inside a record after {records.Count} complete ones, the last of them sequence {lastSequence}";
		}

		return records;
	}

	private static void ReadRecordsInto(string path, SortedDictionary<int, MultiplayerSnapshotRecord> records)
	{
		using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16);
		using BinaryReader reader = new(stream);

		uint magic = reader.ReadUInt32();
		if (magic != 0x504E5352) throw new InvalidDataException($"{Path.GetFileName(path)} does not contain Realm sync records.");

		int version = reader.ReadInt32();
		if (version != SyncRecordVersion) throw new InvalidDataException($"{Path.GetFileName(path)} uses unsupported record version {version}.");
		reader.ReadBoolean();

		int previousSequence = -1;
		while (stream.Position < stream.Length)
		{
			int sequence = reader.ReadInt32();
			if (sequence <= previousSequence)
			{
				throw new InvalidDataException($"{Path.GetFileName(path)} reads sequence {sequence} after {previousSequence}; it is not the layout this reader expects.");
			}

			previousSequence = sequence;
			bool isBaseline = reader.ReadBoolean();
			int baselinePeerCount = reader.ReadInt32();
			HashSet<int> baselinePeerIds = new(baselinePeerCount);
			for (int index = 0; index < baselinePeerCount; index++) baselinePeerIds.Add(reader.ReadInt32());
			ulong physicsFrame = reader.ReadUInt64();
			int describedPeerCount = reader.ReadInt32();
			List<int> describedPeerIds = new(describedPeerCount);
			for (int index = 0; index < describedPeerCount; index++) describedPeerIds.Add(reader.ReadInt32());
			int unitCount = reader.ReadInt32();
			Dictionary<int, MultiplayerSnapshotUnit> units = new(unitCount);

			for (int index = 0; index < unitCount; index++)
			{
				MultiplayerSnapshotUnit unit = new()
				{
					EntityId = reader.ReadInt32(),
					OwnerPlayerIndex = reader.ReadInt32(),
					PositionX = reader.ReadSingle(),
					PositionY = reader.ReadSingle(),
					PositionZ = reader.ReadSingle(),
					RotationY = reader.ReadSingle(),
					CurrentHp = reader.ReadSingle(),
					MaxHp = reader.ReadSingle(),
					AppliedPositionX = reader.ReadSingle(),
					AppliedPositionY = reader.ReadSingle(),
					AppliedPositionZ = reader.ReadSingle(),
					Flags = reader.ReadByte(),
					NamedByPeers = reader.ReadUInt64(),
					ComponentDigests = ReadComponentDigests(reader)
				};
				units[unit.EntityId] = unit;
			}

			MultiplayerSnapshotRecord record = new() { Sequence = sequence, PhysicsFrame = physicsFrame, IsBaseline = isBaseline, BaselinePeerIds = baselinePeerIds, DescribedPeerIds = describedPeerIds, Units = units };
			int playerCount = reader.ReadInt32();
			for (int index = 0; index < playerCount; index++)
			{
				record.Players[reader.ReadInt32()] = ReadComponentDigests(reader);
			}

			record.WorldDigests = ReadComponentDigests(reader);
			int lootCount = reader.ReadInt32();
			for (int index = 0; index < lootCount; index++)
			{
				record.Loot[reader.ReadInt32()] = new MultiplayerLootState
				{
					NamedByPeers = reader.ReadUInt64(),
					ItemTypeId = reader.ReadString(),
					Charges = reader.ReadInt32(),
					Lifetime = reader.ReadSingle(),
					RemainingLifetime = reader.ReadSingle(),
					PickupRadius = reader.ReadSingle(),
					AutoPickup = reader.ReadBoolean(),
					OwnerPlayerIndex = reader.ReadInt32(),
					PositionX = reader.ReadSingle(),
					PositionY = reader.ReadSingle(),
					PositionZ = reader.ReadSingle()
				};
			}

			record.Navigation = new MultiplayerNavigationState
			{
				HasTerrainState = reader.ReadBoolean(),
				HasNavMesh = reader.ReadBoolean(),
				Width = reader.ReadInt32(),
				Depth = reader.ReadInt32(),
				PathingHash = reader.ReadUInt32(),
				WalkableCellCount = reader.ReadInt32(),
				BlockedCellCount = reader.ReadInt32(),
				PolyCount = reader.ReadInt32(),
				GroundPolyCount = reader.ReadInt32(),
				PolyHash = reader.ReadUInt32(),
				ObstacleVolumeCount = reader.ReadInt32(),
				ObstacleHash = reader.ReadUInt32()
			};

			records[sequence] = record;
		}
	}

	private static Dictionary<int, ComponentDigestEntry> ReadComponentDigests(BinaryReader reader)
	{
		int count = reader.ReadInt32();
		Dictionary<int, ComponentDigestEntry> digests = new(count);
		for (int index = 0; index < count; index++)
		{
			int componentTypeId = reader.ReadInt32();
			uint hash = reader.ReadUInt32();
			bool isScoped = reader.ReadByte() != 0;
			ulong namedByMask = isScoped ? reader.ReadUInt64() : SyncRecordFormat.NamedByEveryPeer;
			digests[componentTypeId] = new ComponentDigestEntry(hash, namedByMask);
		}

		return digests;
	}

	private readonly struct ComponentDigestEntry
	{
		public readonly uint Hash;
		public readonly ulong NamedByMask;

		public ComponentDigestEntry(uint hash, ulong namedByMask)
		{
			Hash = hash;
			NamedByMask = namedByMask;
		}

		public bool IsScoped => NamedByMask != SyncRecordFormat.NamedByEveryPeer;

		public bool IsNameableBy(ulong peerMask) => (NamedByMask & peerMask) != 0;
	}

	private class ReferenceDigestCoverage
	{
		public int Compared;
		public int Skipped;

		public void RecordCompared() => Compared++;
		public void RecordSkipped() => Skipped++;
	}

	private static ulong BuildPeerMask(List<int> describedPeerIds, HashSet<int> peerIds)
	{
		ulong mask = 0;
		for (int slot = 0; slot < describedPeerIds.Count && slot < 64; slot++)
		{
			if (peerIds.Contains(describedPeerIds[slot])) mask |= 1UL << slot;
		}

		return mask;
	}

	private class MultiplayerSnapshotRecord
	{
		public int Sequence;
		public ulong PhysicsFrame;
		public bool IsBaseline;
		public HashSet<int> BaselinePeerIds = new();
		public List<int> DescribedPeerIds = new();
		public Dictionary<int, MultiplayerSnapshotUnit> Units = new();
		public Dictionary<int, Dictionary<int, ComponentDigestEntry>> Players = new();
		public Dictionary<int, ComponentDigestEntry> WorldDigests = new();
		public Dictionary<int, MultiplayerLootState> Loot = new();
		public MultiplayerNavigationState Navigation = new();
	}

	private class MultiplayerLootState
	{
		public ulong NamedByPeers;
		public string ItemTypeId = string.Empty;
		public int Charges;
		public float Lifetime;
		public float RemainingLifetime;
		public float PickupRadius;
		public bool AutoPickup;
		public int OwnerPlayerIndex;
		public float PositionX;
		public float PositionY;
		public float PositionZ;
	}

	private class MultiplayerNavigationState
	{
		public bool HasTerrainState;
		public bool HasNavMesh;
		public int Width;
		public int Depth;
		public uint PathingHash;
		public int WalkableCellCount;
		public int BlockedCellCount;
		public int PolyCount;
		public int GroundPolyCount;
		public uint PolyHash;
		public int ObstacleVolumeCount;
		public uint ObstacleHash;
	}

	private class MultiplayerSnapshotUnit
	{
		public int EntityId;
		public int OwnerPlayerIndex;
		public float PositionX;
		public float PositionY;
		public float PositionZ;
		public float RotationY;
		public float CurrentHp;
		public float MaxHp;
		public float AppliedPositionX;
		public float AppliedPositionY;
		public float AppliedPositionZ;
		public byte Flags;
		public ulong NamedByPeers = SyncRecordFormat.NamedByEveryPeer;
		public Dictionary<int, ComponentDigestEntry> ComponentDigests = new();
	}
}
