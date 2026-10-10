namespace Realm.Ecs.Common;

/// <summary>
/// Shared simulation constants for gameplay.
/// </summary>
public static class GameplayConstants
{
	public const int MaxUnitsLimit = 200;
	public const int MaxProjectilesLimit = 100;
	public const float PathfindingGridSize = 10f;

	/// <summary>
	///     Contact radius assumed when a unit has no authored CollisionRadius, expressed as a
	///     fraction of its collision scale. Shared by the movement separation system and the
	///     combat melee reach so both agree on how close units can get.
	/// </summary>
	public const float DefaultCollisionRadius = 1.2f;

	/// <summary>
	///     Contact radius assumed for props (trees, rocks, ...) that have no authored
	///     CollisionRadius, expressed as a fraction of their collision scale.
	/// </summary>
	public const float DefaultPropCollisionRadius = 1.5f;

	/// <summary>
	///     Fraction of the combined collision radii at which units start being pushed apart.
	///     Lower values let units overlap and clump together more (and squeeze through gaps
	///     narrower than their collision diameter), higher values keep them spread out.
	///     Combat melee reach uses the same value so melee units can always close to a
	///     distance where they can attack.
	/// </summary>
	public const float CollisionSeparationFactor = 0.6f;
}
