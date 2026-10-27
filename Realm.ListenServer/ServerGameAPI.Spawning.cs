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

	/// <summary>
	///     One scripted timer. A repeating timer keeps its interval; a one-shot keeps its handle until it fires, which is
	///     the moment the script is told about it.
	/// </summary>
	private struct ServerTimer
	{
		public float Interval;
		public float Remaining;
		public bool Repeating;
	}

	/// <summary>
	///     Creates the unit a map asked for, owned by the player it named and stamped with that ownership, which is what
	///     the authority describes a unit by. A map that declares no such template gets nothing, and is told so.
	/// </summary>
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

	/// <summary>
	///     The one path the authority creates a unit through: the shared factory builds it from the map's own statistics
	///     and the match stamps the ownership every other system reads.
	/// </summary>
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

		return new UnitId(spawned.Id);
	}

	/// <summary>The height of the ground a unit is placed on, so a scripted spawn stands on the terrain rather than under it.</summary>
	private float ReadTerrainHeight(Vector3 position)
	{
		Entity worldEntity = _context.WorldEntity;
		if (worldEntity == Entity.Null || !_context.World.IsAlive(worldEntity)) return position.Y;
		if (!_context.World.TryGet<TerrainState>(worldEntity, out TerrainState terrain)) return position.Y;

		_context.TerrainNavMeshService.GetHeightAndNormal(in terrain, position.X, position.Z, out float height, out Vector3 normal);
		return height;
	}

	/// <summary>Runs the timers a script scheduled and tells the script about the ones that came due.</summary>
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

	/// <summary>Schedules a timer that fires once, after the delay the script asked for.</summary>
	public int ScheduleTimer(float delay)
	{
		int handle = _nextTimerHandle++;
		float seconds = Math.Max(0f, delay);
		_timers[handle] = new ServerTimer { Interval = seconds, Remaining = seconds, Repeating = false };
		return handle;
	}

	/// <summary>Schedules a timer that fires every interval until the script cancels it.</summary>
	public int ScheduleRepeatingTimer(float interval)
	{
		int handle = _nextTimerHandle++;
		float seconds = Math.Max(0.01f, interval);
		_timers[handle] = new ServerTimer { Interval = seconds, Remaining = seconds, Repeating = true };
		return handle;
	}

	/// <summary>Stops a timer, whether it has fired or not.</summary>
	public void CancelTimer(int timerHandle)
	{
		_timers.Remove(timerHandle);
	}
}
