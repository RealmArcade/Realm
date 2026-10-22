using Realm.Ecs.Components.Core;

namespace Realm.Ecs.Common;

/// <summary>
/// Shared simulation constants for gameplay.
/// </summary>
public static class GameplayConstants
{
	public const int MaxUnitsLimit = 200;
	public const int MaxProjectilesLimit = 100;
	public const float PathfindingGridSize = 10f;
	public const float UnitAnimationStepIntervalSeconds = 1f / 30f;

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
	///     Authored CollisionRadius values at or below this are treated as "never authored"
	///     rather than as a real body size. Shared by movement separation and combat so both
	///     agree on when the default body size applies.
	/// </summary>
	public const float MinimumAuthoredCollisionRadius = 0.01f;

	/// <summary>
	///     Fraction of the combined collision radii at which units start being pushed apart.
	///     Lower values let units overlap and clump together more (and squeeze through gaps
	///     narrower than their collision diameter), higher values keep them spread out.
	///     Combat melee reach uses the same value so melee units can always close to a
	///     distance where they can attack.
	/// </summary>
	public const float CollisionSeparationFactor = 0.6f;

	/// <summary>
	///     Resolves the contact radius of a unit body: the authored CollisionRadius scaled by
	///     CollisionScale, or the default radius when the body size was never authored
	///     (missing, zero or negative). Combat melee reach must use the same value the movement
	///     separation uses, otherwise the two disagree on how wide a body is.
	/// </summary>
	public static float ResolveUnitContactRadius(float authoredRadius, float scale)
	{
		return ResolveContactRadius(authoredRadius, scale, DefaultCollisionRadius);
	}

	/// <summary>
	///     Resolves the contact radius of a body from its authored CollisionRadius and
	///     CollisionScale, falling back to the supplied default when the body size was never
	///     authored (missing, zero or negative).
	/// </summary>
	public static float ResolveContactRadius(float authoredRadius, float scale, float defaultRadius)
	{
		if (authoredRadius > MinimumAuthoredCollisionRadius) return authoredRadius * scale;
		return scale * defaultRadius;
	}

	/// <summary>
	///     Resolves the world-space contact radius of an entity from the shared collision
	///     components, so picking, placement and simulation all agree on how wide a body is.
	/// </summary>
	public static float ResolveBodyRadius(EcsWorld world, Entity entity, float defaultRadius)
	{
		if (world == null || !world.IsAlive(entity)) return 0f;

		float scale = world.Has<CollisionScale>(entity) ? world.Get<CollisionScale>(entity).Value : 1.0f;
		float authoredRadius = world.Has<CollisionRadius>(entity) ? world.Get<CollisionRadius>(entity).Value : 0f;
		return ResolveContactRadius(authoredRadius, scale, defaultRadius);
	}
}
