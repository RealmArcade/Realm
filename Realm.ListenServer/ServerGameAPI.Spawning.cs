using System.Numerics;
using Realm.Ecs;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Terrain;
using Realm.Shared.Maps;
using Realm.MapAPI;

namespace Realm.ListenServer;

public partial class ServerGameAPI
{
	private readonly Dictionary<int, ServerTimer> _timers = new();
	private int _nextTimerHandle = 1;

	private struct ServerTimer
	{
		public float Interval;
		public float Remaining;
		public bool Repeating;
	}

	public UnitId SpawnUnitForPlayer(string unitTypeId, Vector2 position, int playerIndex, bool executeSpawnShader = true, bool bypassPopulation = false)
	{
		if (string.IsNullOrEmpty(unitTypeId)) return UnitId.Invalid;

		PlayerEntity owner = _map.GetPlayerEntity(playerIndex);
		if (owner.Value == Entity.Null)
		{
			_log.Write($"the map asked for '{unitTypeId}' for player {playerIndex}, which this map does not seat");
			return UnitId.Invalid;
		}

		return SpawnUnit(unitTypeId, new Vector3(position.X, 0f, position.Y), owner, playerIndex, bypassPopulation);
	}

	internal UnitId SpawnUnit(string unitTypeId, Vector3 position, PlayerEntity owner, int playerIndex, bool bypassPopulation)
	{
		float groundHeight = ReadTerrainHeight(position);
		var placed = new Vector3(position.X, groundHeight, position.Z);

		Entity spawned = UnitEntityFactory.CreateFromMetadata(_context.World, _map.Catalog, unitTypeId, placed, 0f, 0f, owner, bypassPopulation);
		if (spawned == Entity.Null)
		{
			_log.Write($"the map asked for '{unitTypeId}', which its templates do not define");
			return UnitId.Invalid;
		}

		_context.World.SetOrAdd(spawned, new UnitOwnerPlayer(playerIndex));
		_log.Write($"spawned '{unitTypeId}' as entity {spawned.Id} for player {playerIndex}");

		if (Metrics != null)
		{
			Metrics.RecordUnitSpawned(playerIndex);
			if (_map.Catalog.FindUnitOrBuilding(unitTypeId)?.IsHero == true)
			{
				Metrics.RecordHeroPick(playerIndex, unitTypeId);
			}
		}

		return new UnitId(spawned.Id);
	}

	private float ReadTerrainHeight(Vector3 position)
	{
		Entity worldEntity = _context.WorldEntity;
		if (worldEntity == Entity.Null || !_context.World.IsAlive(worldEntity)) return position.Y;
		if (!_context.World.TryGet<TerrainState>(worldEntity, out TerrainState terrain)) return position.Y;

		_context.TerrainNavMeshService.GetHeightAndNormal(in terrain, position.X, position.Z, out float height, out Vector3 normal);
		return height;
	}

	public void AdvanceTimers(float deltaSeconds)
	{
		if (_timers.Count == 0) return;

		_nextDueTimers.Clear();
		foreach (KeyValuePair<int, ServerTimer> entry in _timers)
		{
			ServerTimer timer = entry.Value;
			timer.Remaining -= deltaSeconds;
			if (timer.Remaining > 0f)
			{
				_timers[entry.Key] = timer;
				continue;
			}

			if (timer.Repeating)
			{
				timer.Remaining += timer.Interval;
				if (timer.Remaining <= 0f) timer.Remaining = timer.Interval;
				_timers[entry.Key] = timer;
			}
			else
			{
				_timers.Remove(entry.Key);
			}

			_nextDueTimers.Add(entry.Key);
		}

		for (int index = 0; index < _nextDueTimers.Count; index++)
		{
			OnTimerExpired?.Invoke(_nextDueTimers[index]);
		}
	}

	private readonly List<int> _nextDueTimers = new();

	public int ScheduleTimer(float delay)
	{
		int handle = _nextTimerHandle++;
		float seconds = Math.Max(0f, delay);
		_timers[handle] = new ServerTimer { Interval = seconds, Remaining = seconds, Repeating = false };
		return handle;
	}

	public int ScheduleRepeatingTimer(float interval)
	{
		int handle = _nextTimerHandle++;
		float seconds = Math.Max(0.01f, interval);
		_timers[handle] = new ServerTimer { Interval = seconds, Remaining = seconds, Repeating = true };
		return handle;
	}

	public void CancelTimer(int timerHandle)
	{
		_timers.Remove(timerHandle);
	}
}
