using System.Numerics;
using Realm.Ecs;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Tags;
using Realm.Ecs.Services;
using Realm.Shared.Diagnostics;

namespace Realm.ListenServer;

/// <summary>
///     Writes what the authority holds for every unit on every sequence it publishes: the order the unit is executing,
///     the route it is walking and where it actually is. A screen's prediction can only be judged against the process
///     that decided the movement, so a run that measures settled error or pathing parity asks the authority for this
///     stream.
/// </summary>
public class AuthoritySnapshotRecorder : IDisposable
{
	private static readonly QueryDescription RecordedUnitsQuery = new QueryDescription().WithAll<UnitOwnerPlayer, Health, Position>();

	private readonly EcsWorld _world;
	private readonly BinaryWriter _writer;
	private readonly List<Entity> _units = new(256);
	private readonly EntityCallback<UnitOwnerPlayer, Health, Position> _collectUnit;
	private bool _disposed;

	public AuthoritySnapshotRecorder(string folder, EcsWorld world, out string path)
	{
		_world = world;
		_collectUnit = CollectUnit;

		Directory.CreateDirectory(folder);
		path = Path.Combine(folder, "prediction_host.bin");

		FileStream stream = new(path, FileMode.Create, FileAccess.Write, FileShare.Read, 1 << 16);
		_writer = new BinaryWriter(stream);
		_writer.Write(PredictionRecordFormat.Magic);
		_writer.Write(PredictionRecordFormat.Version);
		_writer.Write(true);
		_writer.Flush();
	}

	/// <summary>
	///     States the world as it stands on one published sequence. Every unit the authority moves is recorded, so a
	///     screen that applied this sequence can be compared against the world it was told about rather than against
	///     another screen's copy of it.
	/// </summary>
	public void Record(int sequence)
	{
		if (_disposed) return;

		_units.Clear();
		_world.Query(in RecordedUnitsQuery, _collectUnit);

		_writer.Write(PredictionRecordFormat.KindHostSnapshot);
		_writer.Write(sequence);
		_writer.Write(_units.Count);

		for (int index = 0; index < _units.Count; index++) WriteUnit(_units[index]);

		_writer.Flush();
	}

	/// <summary>
	///     A building is a placement rather than a mover, and a dead unit is no longer where anybody can see it, so
	///     neither is part of what a screen's prediction is measured against.
	/// </summary>
	private void CollectUnit(Entity entity, ref UnitOwnerPlayer owner, ref Health health, ref Position position)
	{
		if (_world.Has<Building>(entity)) return;
		if (_world.Has<Dead>(entity)) return;

		_units.Add(entity);
	}

	/// <summary>
	///     Writes one unit exactly as the report reads it. The route is written from its first waypoint along with the
	///     index the unit has reached, because a plan keeps the waypoints it already walked and the reader skips past
	///     them.
	/// </summary>
	private void WriteUnit(Entity entity)
	{
		Vector3 position = _world.TryGet<Position>(entity, out Position readablePosition) ? readablePosition.Value : Vector3.Zero;
		int ownerPlayerIndex = _world.TryGet<UnitOwnerPlayer>(entity, out UnitOwnerPlayer owner) ? owner.PlayerIndex : -1;
		bool hasMoveTo = _world.Has<MoveTo>(entity);
		Vector3 moveTarget = hasMoveTo ? _world.Get<MoveTo>(entity).Target : Vector3.Zero;
		bool hasPathFollow = _world.Has<PathFollow>(entity);

		_writer.Write(entity.Id);
		_writer.Write(ownerPlayerIndex);
		_writer.Write(hasMoveTo);
		_writer.Write(hasPathFollow);

		if (!hasPathFollow)
		{
			_writer.Write(0);
			_writer.Write(0);
			WriteVector(position);
			WriteVector(moveTarget);
			_writer.Write(0);
			return;
		}

		ref PathFollow pathFollow = ref _world.Get<PathFollow>(entity);
		_writer.Write(pathFollow.CurrentWaypointIndex);
		_writer.Write(pathFollow.WaypointCount);
		WriteVector(position);
		WriteVector(moveTarget);

		int waypointCount = Math.Min(pathFollow.WaypointCount, PredictionRecordFormat.MaximumRecordedWaypoints);
		_writer.Write(waypointCount);
		for (int waypointIndex = 0; waypointIndex < waypointCount; waypointIndex++)
		{
			WriteVector(pathFollow.Waypoints[waypointIndex]);
		}
	}

	private void WriteVector(Vector3 value)
	{
		_writer.Write(value.X);
		_writer.Write(value.Y);
		_writer.Write(value.Z);
	}

	public void Dispose()
	{
		if (_disposed) return;
		_disposed = true;

		_writer.Flush();
		_writer.Dispose();
	}
}
