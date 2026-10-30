using System.Buffers;
using System.Collections.Concurrent;
using System.Text;
using System.Numerics;
using Realm.Ecs;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Tags;
using Realm.Ecs.Replication;
using Realm.Ecs.Services;
using Realm.Shared.Diagnostics;
using Realm.Shared.Maps;
using Realm.Shared.Network;

namespace Realm.ListenServer;

public class AuthoritySyncRecorder : IDisposable
{
	private static readonly QueryDescription RecordedUnitsQuery = new QueryDescription().WithAll<UnitOwnerPlayer, Health, Position>();
	private static readonly QueryDescription RecordedLootQuery = new QueryDescription().WithAll<GroundLoot>();

	private readonly EcsWorld _world;
	private readonly Entity _worldEntity;
	private readonly TerrainNavMeshService? _navMeshService;
	private readonly FileStream _recordStream;
	private readonly MemoryStream _staging;
	private readonly BinaryWriter _writer;
	private readonly BlockingCollection<(byte[] Buffer, int Length)> _pendingRecords = new(new ConcurrentQueue<(byte[] Buffer, int Length)>(), MaximumPendingRecords);
	private readonly Thread _recordWriterThread;
	private int _droppedRecordCount;
	private readonly Dictionary<int, RecordedUnit> _units = new(256);
	private readonly List<Entity> _worldUnits = new(256);
	private readonly List<(int UniqueId, Entity Entity)> _loot = new(64);
	private readonly List<int> _baselinePeerIds = new();
	private readonly List<int> _orderedEntityIds = new(256);

	private readonly Dictionary<int, ulong> _namedByPeerMask = new(256);

	private readonly Dictionary<int, ulong> _lootNamedByPeerMask = new(64);
	private readonly Dictionary<int, int> _peerSlotById = new(8);
	private readonly List<int> _describedPeerIds = new(8);
	private ulong _peersDescribedMask;
	private readonly SyncRecordFormat.ComponentComparabilityMask _namedBy;
	private readonly EntityCallback<UnitOwnerPlayer, Health, Position> _collectUnit;
	private readonly EntityCallback<GroundLoot> _collectLoot;
	private int _peerCount;
	private int _sequence;
	private long _tick;
	private bool _disposed;

	public AuthoritySyncRecorder(LoadedMap map, string folder, out string path)
	{
		_world = map.World;
		_worldEntity = map.WorldEntity;
		_navMeshService = map.Simulation.TerrainNavMeshService;
		_collectUnit = CollectUnit;
		_collectLoot = CollectLoot;
		_namedBy = ResolveNamedByMask;

		Directory.CreateDirectory(folder);
		path = Path.Combine(folder, "ECS_host_sync.bin");

		_recordStream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read, 1 << 16);
		_staging = new MemoryStream(1 << 16);
		_writer = new BinaryWriter(_staging, Encoding.UTF8, true);
		SyncRecordFormat.WriteHeader(_writer, true);
		_writer.Flush();
		EnqueueRecord();

		_recordWriterThread = new Thread(WritePendingRecords)
		{
			IsBackground = true,
			Name = "AuthoritySyncRecordWriter"
		};
		_recordWriterThread.Start();
	}

	private const int MaximumPendingRecords = 240;
	private const int ShutdownJoinMilliseconds = 5000;

	/// <summary>
	///     Hands the record this tick rendered to the writer thread. The record is rendered on the tick thread because
	///     rendering it reads the world, and only the file write is moved off; a record that arrives when the queue is
	///     full is counted and dropped rather than holding the tick for the disk.
	/// </summary>
	private void EnqueueRecord()
	{
		int length = (int)_staging.Length;
		if (length == 0) return;

		byte[] buffer = ArrayPool<byte>.Shared.Rent(length);
		Buffer.BlockCopy(_staging.GetBuffer(), 0, buffer, 0, length);
		_staging.SetLength(0);

		if (!_pendingRecords.TryAdd((buffer, length)))
		{
			ArrayPool<byte>.Shared.Return(buffer);
			Interlocked.Increment(ref _droppedRecordCount);
		}
	}

	private void WritePendingRecords()
	{
		foreach ((byte[] buffer, int length) in _pendingRecords.GetConsumingEnumerable())
		{
			try
			{
				_recordStream.Write(buffer, 0, length);

				// Flushed per record rather than left to the stream's own buffer, because this process is terminated where the
				// match ends: without it the buffer takes every record written since it last filled with it, and a reader has to
				// step over several cut-off records instead of one.
				_recordStream.Flush();
			}
			catch (IOException)
			{
				Interlocked.Increment(ref _droppedRecordCount);
			}
			finally
			{
				ArrayPool<byte>.Shared.Return(buffer);
			}
		}
	}

	public void BeginSequence(int sequence, long tick)
	{
		_sequence = sequence;
		_tick = tick;
		_peerCount = 0;
		_peersDescribedMask = 0;
		_peerSlotById.Clear();
		_describedPeerIds.Clear();
		_units.Clear();
		_lootNamedByPeerMask.Clear();
		_baselinePeerIds.Clear();
	}

	public void AddPeerSnapshot(int peerId, in WorldSnapshot snapshot)
	{
		if (_disposed || snapshot.Units == null) return;

		_peerCount++;
		if (_peerSlotById.TryAdd(peerId, _describedPeerIds.Count)) _describedPeerIds.Add(peerId);
		_peersDescribedMask |= PeerBit(peerId);
		if (snapshot.IsBaseline) _baselinePeerIds.Add(peerId);

		UnitSection units = snapshot.Units;
		for (int index = 0; index < units.Count; index++)
		{
			UnitSnapshot unit = units[index];
			if (_units.TryGetValue(unit.EntityId, out RecordedUnit recorded))
			{
				recorded.PresentIn++;
				recorded.NamedByMask |= PeerBit(peerId);
				_units[unit.EntityId] = recorded;
				continue;
			}

			_units[unit.EntityId] = new RecordedUnit
			{
				Snapshot = unit,
				PresentIn = 1,
				NamedByMask = PeerBit(peerId)
			};
		}

		if (snapshot.Loot == null) return;

		for (int index = 0; index < snapshot.Loot.Count; index++)
		{
			int uniqueId = snapshot.Loot[index].EntityId;
			_lootNamedByPeerMask[uniqueId] = _lootNamedByPeerMask.TryGetValue(uniqueId, out ulong describedTo) ? describedTo | PeerBit(peerId) : PeerBit(peerId);
		}
	}

	public void EndSequence()
	{
		if (_disposed) return;

		_writer.Write(_sequence);
		_writer.Write(_baselinePeerIds.Count > 0);
		int recordedPeers = _baselinePeerIds.Count;
		_writer.Write(recordedPeers);
		for (int index = 0; index < recordedPeers; index++) _writer.Write(_baselinePeerIds[index]);
		_writer.Write((ulong)_tick);

		_writer.Write(_describedPeerIds.Count);
		for (int index = 0; index < _describedPeerIds.Count; index++) _writer.Write(_describedPeerIds[index]);

		WriteUnits();
		SyncRecordFormat.WritePlayerScope(_writer, _world, _worldEntity, ReplicatedScope.Player, null);
		WriteLoot();
		SyncRecordFormat.WriteNavigationState(_writer, NavigationParityDigest.Capture(_world, _worldEntity, _navMeshService));
		_writer.Flush();
		EnqueueRecord();
	}

	private void WriteUnits()
	{
		if (_peerCount == 0)
		{
			_worldUnits.Clear();
			_world.Query(in RecordedUnitsQuery, _collectUnit);
			for (int index = 0; index < _worldUnits.Count; index++)
			{
				Entity entity = _worldUnits[index];
				_units[entity.Id] = new RecordedUnit
				{
					Snapshot = DescribeUnit(entity),
					PresentIn = 1,
					NamedByMask = SyncRecordFormat.NamedByEveryPeer
				};
			}
		}

		int unitCount = _units.Count;
		_writer.Write(unitCount);
		if (unitCount == 0) return;

		_orderedEntityIds.Clear();
		_namedByPeerMask.Clear();
		foreach (KeyValuePair<int, RecordedUnit> entry in _units)
		{
			_orderedEntityIds.Add(entry.Key);
			_namedByPeerMask[entry.Key] = entry.Value.NamedByMask == SyncRecordFormat.NamedByEveryPeer
				? entry.Value.NamedByMask
				: entry.Value.NamedByMask & _peersDescribedMask;
		}

		_orderedEntityIds.Sort();

		for (int index = 0; index < _orderedEntityIds.Count; index++)
		{
			RecordedUnit recorded = _units[_orderedEntityIds[index]];
			bool isVisibleToEveryPeer = (recorded.NamedByMask & _peersDescribedMask) == _peersDescribedMask;

			bool isDetailedForEveryPeer = isVisibleToEveryPeer;

			Entity entity = new(recorded.Snapshot.EntityId);
			bool alive = _world.IsAlive(entity);
			bool isDead = alive ? _world.Has<Dead>(entity) : recorded.Snapshot.IsDead;
			bool isBuilding = alive ? _world.Has<Building>(entity) : recorded.Snapshot.IsBuilding;

			Vector3 position = new(recorded.Snapshot.Position.X, recorded.Snapshot.Position.Y, recorded.Snapshot.Position.Z);
			if (alive && _world.Has<Position>(entity)) position = _world.Get<Position>(entity).Value;

			float rotationY = recorded.Snapshot.RotationY;
			if (alive && _world.Has<RotationY>(entity)) rotationY = _world.Get<RotationY>(entity).Value;

			float currentHp = recorded.Snapshot.CurrentHp;
			float maxHp = recorded.Snapshot.MaxHp;
			if (alive && _world.Has<Health>(entity))
			{
				Health health = _world.Get<Health>(entity);
				currentHp = health.Current;
				maxHp = health.Max;
			}

			byte flags = 0;
			if (isDead) flags |= SyncRecordFormat.FlagDead;
			if (isBuilding) flags |= SyncRecordFormat.FlagBuilding;
			if (isDetailedForEveryPeer) flags |= SyncRecordFormat.FlagDetailed;
			if (isVisibleToEveryPeer) flags |= SyncRecordFormat.FlagVisibleToRemoteClient;

			SyncRecordFormat.SyncUnitRecord unit = new(recorded.Snapshot.EntityId, recorded.Snapshot.OwnerPlayerIndex, position, rotationY, currentHp, maxHp, position, flags, recorded.NamedByMask);
			SyncRecordFormat.WriteUnit(_writer, in unit, _world, entity, ReplicatedScope.Unit, null, _namedBy);
		}
	}

	private bool ResolveNamedByMask(int componentTypeId, ReadOnlySpan<byte> payload, out ulong namedByPeers)
	{
		namedByPeers = SyncRecordFormat.NamedByEveryPeer;
		if (!ReferenceCodecs.TryGetValue(componentTypeId, out INormalizingComponentCodec? codec)) return true;

		int referencedServerEntityId = codec.ReadReferencedServerEntityId(payload);
		if (referencedServerEntityId < 0) return true;

		namedByPeers = _namedByPeerMask.TryGetValue(referencedServerEntityId, out ulong mask) ? mask : 0;
		return true;
	}

	private ulong PeerBit(int peerId)
	{
		int slot = _peerSlotById.TryGetValue(peerId, out int existing) ? existing : _describedPeerIds.Count;
		return slot < 64 ? 1UL << slot : 0;
	}

	private static readonly Dictionary<int, INormalizingComponentCodec> ReferenceCodecs = BuildReferenceCodecs();

	private static Dictionary<int, INormalizingComponentCodec> BuildReferenceCodecs()
	{
		var codecs = new Dictionary<int, INormalizingComponentCodec>();
		IReadOnlyList<IReplicatedComponentCodec> all = ReplicatedComponents.All;
		for (int index = 0; index < all.Count; index++)
		{
			if (all[index] is INormalizingComponentCodec normalizing) codecs[normalizing.ComponentTypeId] = normalizing;
		}

		return codecs;
	}

	private void WriteLoot()
	{
		_loot.Clear();
		_world.Query(in RecordedLootQuery, _collectLoot);

		_writer.Write(_loot.Count);
		for (int index = 0; index < _loot.Count; index++)
		{
			(int uniqueId, Entity entity) = _loot[index];
			ulong namedByPeers = _peerCount == 0
				? SyncRecordFormat.NamedByEveryPeer
				: _lootNamedByPeerMask.TryGetValue(uniqueId, out ulong describedTo) ? describedTo : 0UL;

			SyncRecordFormat.WriteLootItem(_writer, uniqueId, namedByPeers, _world, entity);
		}
	}

	private UnitSnapshot DescribeUnit(Entity entity)
	{
		Vector3 position = _world.TryGet<Position>(entity, out Position readablePosition) ? readablePosition.Value : Vector3.Zero;
		float rotationY = _world.TryGet<RotationY>(entity, out RotationY readableRotation) ? readableRotation.Value : 0f;
		int ownerPlayerIndex = _world.TryGet<UnitOwnerPlayer>(entity, out UnitOwnerPlayer owner) ? owner.PlayerIndex : -1;
		float currentHp = 0f;
		float maxHp = 0f;
		if (_world.TryGet<Health>(entity, out Health health))
		{
			currentHp = health.Current;
			maxHp = health.Max;
		}

		return new UnitSnapshot
		{
			EntityId = entity.Id,
			OwnerPlayerIndex = ownerPlayerIndex,
			Position = new NetworkVector3(position.X, position.Y, position.Z),
			RotationY = rotationY,
			CurrentHp = currentHp,
			MaxHp = maxHp,
			IsDead = _world.Has<Dead>(entity),
			IsBuilding = _world.Has<Building>(entity)
		};
	}

	private void CollectUnit(Entity entity, ref UnitOwnerPlayer owner, ref Health health, ref Position position)
	{
		_worldUnits.Add(entity);
	}

	private void CollectLoot(Entity entity, ref GroundLoot loot)
	{
		_loot.Add((loot.UniqueId, entity));
	}

	private struct RecordedUnit
	{
		public UnitSnapshot Snapshot;
		public int PresentIn;
		public ulong NamedByMask;
	}

	public int DroppedRecordCount => Volatile.Read(ref _droppedRecordCount);

	public void Dispose()
	{
		if (_disposed) return;
		_disposed = true;

		_pendingRecords.CompleteAdding();
		if (_recordWriterThread.IsAlive) _recordWriterThread.Join(ShutdownJoinMilliseconds);

		_writer.Dispose();
		_staging.Dispose();
		_recordStream.Dispose();
		_pendingRecords.Dispose();
	}
}
