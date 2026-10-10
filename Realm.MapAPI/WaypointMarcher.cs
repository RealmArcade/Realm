using System.Numerics;

namespace Realm.MapAPI;

/// <summary>
/// Manages automated lane movement and combat engagement for a unit marching along a sequence of waypoints.
/// </summary>
public class WaypointMarcher
{
	private readonly IUnit _unit;
	private readonly IReadOnlyList<Vector3> _waypoints;
	private readonly Vector3 _finalDestination;
	private readonly WaypointMarchConfig _config;
	private int _waypointIndex = 1;
	private bool _hasMovementOrder;
	private Vector3? _orderedDestination;

	/// <summary>
	/// Initializes a new instance of the <see cref="WaypointMarcher"/> class.
	/// </summary>
	/// <param name="unit">The unit to navigate along the waypoint sequence.</param>
	/// <param name="waypoints">The ordered list of waypoint positions to march through.</param>
	/// <param name="config">Optional configuration settings for arrival thresholds and attack cooldowns. If <see langword="null"/>, default settings are used.</param>
	public WaypointMarcher(
		IUnit unit,
		IReadOnlyList<Vector3> waypoints,
		WaypointMarchConfig? config = null)
	{
		_unit = unit;
		_waypoints = waypoints;
		_finalDestination = waypoints.Count > 0 ? waypoints[^1] : unit.Position;
		_config = config ?? WaypointMarchConfig.Default;
	}

	/// <summary>
	/// Gets a value indicating whether the managed unit is currently alive.
	/// </summary>
	public bool IsAlive => !_unit.IsDead;

	/// <summary>
	/// Updates the unit's march and combat behavior for the current simulation tick.
	/// </summary>
	/// <param name="api">The game API instance used to query targets and issue orders.</param>
	/// <param name="delta">The time elapsed since the previous simulation tick, in seconds.</param>
	public void Update(IGameAPI api, float delta)
	{
		if (!IsAlive || _waypointIndex >= _waypoints.Count)
			return;

		AdvanceWaypointIfReached();
		if (!_hasMovementOrder)
		{
			IssueLanePush(api);
		}
	}

	private void AdvanceWaypointIfReached()
	{
		var waypoint = _waypoints[_waypointIndex];
		if (HorizontalDistanceSquared(_unit.Position, waypoint) > _config.WaypointArrivalRadiusSquared)
			return;
		_waypointIndex++;
		_hasMovementOrder = false;
	}

	private void IssueLanePush(IGameAPI api)
	{
		var destination = _waypointIndex >= _waypoints.Count
			? _finalDestination
			: _waypoints[_waypointIndex];

		if (_orderedDestination.HasValue &&
		    HorizontalDistanceSquared(_orderedDestination.Value, destination) < 0.25f)
		{
			return;
		}

		api.IssueAttackMoveOrder(_unit, destination);
		_orderedDestination = destination;
		_hasMovementOrder = true;
	}

	private static float HorizontalDistanceSquared(Vector3 from, Vector3 to)
	{
		var dx = from.X - to.X;
		var dz = from.Z - to.Z;
		return dx * dx + dz * dz;
	}
}