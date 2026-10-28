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

/// <summary>
///     Writes what the authority holds for every unit on every sequence it publishes. A client can only be judged
///     against the process that decided the world, so a run that checks whether a screen holds the same ECS as the
///     authority asks the authority for this stream; nothing else asks for it, so a normal match writes none.
/// </summary>
public class AuthoritySyncRecorder : IDisposable
{
	private static readonly QueryDescription RecordedUnitsQuery = new QueryDescription().WithAll<UnitOwnerPlayer, Health, Position>();
	private static readonly QueryDescription RecordedLootQuery = new QueryDescription().WithAll<GroundLoot>();

	private readonly EcsWorld _world;
	private readonly Entity _worldEntity;
	private readonly TerrainNavMeshService? _navMeshService;
	private readonly BinaryWriter _writer;
	private readonly Dictionary<int, RecordedUnit> _units = new(256);
	private readonly List<Entity> _worldUnits = new(256);
	private readonly List<(int UniqueId, Entity Entity)> _loot = new(64);
	private readonly List<int> _baselinePeerIds = new();
	private readonly List<int> _orderedEntityIds = new(256);
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

		Directory.CreateDirectory(folder);
		path = Path.Combine(folder, "ECS_host_sync.bin");

		FileStream stream = new(path, FileMode.Create, FileAccess.Write, FileShare.Read, 1 << 16);
		_writer = new BinaryWriter(stream);
		SyncRecordFormat.WriteHeader(_writer, true);
		_writer.Flush();
	}

	/// <summary>Opens the record for one sequence, before the snapshots that describe it have been built.</summary>
	public void BeginSequence(int sequence, long tick)
	{
		_sequence = sequence;
		_tick = tick;
		_peerCount = 0;
		_units.Clear();
		_baselinePeerIds.Clear();
	}

	/// <summary>
	///     Notes one peer's description of the sequence. A unit is counted as holding in every peer's copy when every
	///     snapshot built this tick mentions it, which is what decides whether a peer that does not hold it is missing it
	///     or was never told about it.
	/// </summary>
	public void AddPeerSnapshot(int peerId, in WorldSnapshot snapshot)
	{
		if (_disposed || snapshot.Units == null) return;

		_peerCount++;
		if (snapshot.IsBaseline) _baselinePeerIds.Add(peerId);

		UnitSection units = snapshot.Units;
		for (int index = 0; index < units.Count; index++)
		{
			UnitSnapshot unit = units[index];
			if (_units.TryGetValue(unit.EntityId, out RecordedUnit recorded))
			{
				recorded.PresentIn++;
				_units[unit.EntityId] = recorded;
				continue;
			}

			_units[unit.EntityId] = new RecordedUnit
			{
				Snapshot = unit,
				PresentIn = 1
			};
		}
	}

	/// <summary>
	///     Closes the record for the sequence just published. A sequence no peer was described for is still written: the
	///     stream is read against the sequences a screen applied, and a gap would hide the very ticks a peer was catching
	///     up on.
	/// </summary>
	public void EndSequence()
	{
		if (_disposed) return;

		_writer.Write(_sequence);
		_writer.Write(_baselinePeerIds.Count > 0);
		int recordedPeers = _baselinePeerIds.Count;
		_writer.Write(recordedPeers);
		for (int index = 0; index < recordedPeers; index++) _writer.Write(_baselinePeerIds[index]);
		_writer.Write((ulong)_tick);

		WriteUnits();
		SyncRecordFormat.WritePlayerScope(_writer, _world, _worldEntity, ReplicatedScope.Player, null);
		WriteLoot();
		SyncRecordFormat.WriteNavigationState(_writer, NavigationParityDigest.Capture(_world, _worldEntity, _navMeshService));
		_writer.Flush();
	}

	/// <summary>
	///     Writes every unit the sequence described, in the order a reader expects. A tick no peer was described for
	///     still records the units the authority holds, so the stream describes every sequence rather than only the ones
	///     somebody happened to be watching.
	/// </summary>
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
					PresentIn = 1
				};
			}
		}

		int unitCount = _units.Count;
		_writer.Write(unitCount);
		if (unitCount == 0) return;

		_orderedEntityIds.Clear();
		foreach (KeyValuePair<int, RecordedUnit> entry in _units) _orderedEntityIds.Add(entry.Key);
		_orderedEntityIds.Sort();

		for (int index = 0; index < _orderedEntityIds.Count; index++)
		{
			RecordedUnit recorded = _units[_orderedEntityIds[index]];
			bool isVisibleToEveryPeer = recorded.PresentIn >= _peerCount;

			// The match states a unit at one precision, so a peer that was told about a unit was told everything the
			// match holds about it. Reading the flag off the unit's own record said the same thing while the wire no
			// longer carries a second precision, and it is kept apart from visibility because a reader judges a
			// rendered transform against the precision it was promised rather than against how many peers it reached.
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

			SyncRecordFormat.SyncUnitRecord unit = new(recorded.Snapshot.EntityId, recorded.Snapshot.OwnerPlayerIndex, position, rotationY, currentHp, maxHp, position, flags);
			SyncRecordFormat.WriteUnit(_writer, in unit, _world, entity, ReplicatedScope.Unit, null);
		}
	}

	private void WriteLoot()
	{
		_loot.Clear();
		_world.Query(in RecordedLootQuery, _collectLoot);

		_writer.Write(_loot.Count);
		for (int index = 0; index < _loot.Count; index++)
		{
			(int uniqueId, Entity entity) = _loot[index];
			SyncRecordFormat.WriteLootItem(_writer, uniqueId, _world, entity, null);
		}
	}

	/// <summary>The state of one unit as this process holds it, for a tick no snapshot described it.</summary>
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
	}

	public void Dispose()
	{
		if (_disposed) return;
		_disposed = true;

		_writer.Flush();
		_writer.Dispose();
	}
}
