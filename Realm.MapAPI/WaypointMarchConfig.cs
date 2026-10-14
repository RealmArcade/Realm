namespace Realm.MapAPI;

/// <summary>
/// Configuration settings for automated waypoint navigation and lane combat behavior.
/// </summary>
public readonly struct WaypointMarchConfig
{
	/// <summary>
	/// Gets the squared distance threshold within which a unit is considered to have arrived at a waypoint.
	/// </summary>
	public float WaypointArrivalRadiusSquared { get; }

	/// <summary>
	/// Gets the cooldown interval in seconds between attacks during a march.
	/// </summary>
	public float AttackCooldownSeconds { get; }

	/// <summary>
	/// Initializes a new instance of the <see cref="WaypointMarchConfig"/> struct.
	/// </summary>
	/// <param name="waypointArrivalRadiusSquared">The squared arrival radius used to determine when a waypoint is reached.</param>
	/// <param name="attackCooldownSeconds">The attack cooldown duration in seconds.</param>
	public WaypointMarchConfig(float waypointArrivalRadiusSquared, float attackCooldownSeconds)
	{
		WaypointArrivalRadiusSquared = waypointArrivalRadiusSquared;
		AttackCooldownSeconds = attackCooldownSeconds;
	}

	/// <summary>
	/// Gets the default waypoint march configuration.
	/// </summary>
	public static WaypointMarchConfig Default => new(4f, 1.2f);
}