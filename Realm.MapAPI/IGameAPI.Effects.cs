// Extracted from IGameAPI.cs (task 6 decomposition).
using System.Numerics;


namespace Realm.MapAPI;


/// <summary>
/// Defines the safe, public API exposed to map scripts to interact with the game engine.
/// </summary>
public partial interface IGameAPI
{
	/// <summary>
	/// Spawns floating text in the 3D world that drifts upwards and fades out.
	/// </summary>
	/// <param name="text">The text to display.</param>
	/// <param name="position">The starting 3D world position.</param>
	/// <param name="color">The color of the text in RGB format.</param>
	/// <param name="duration">The time in seconds before the text fully fades out.</param>
	void CreateFloatingText(string text, Vector3 position, Vector3 color, float duration);

	/// <summary>
	/// Spawns a 3D visual effect at the specified position.
	/// </summary>
	/// <param name="effectTypeId">The identifier of the visual effect (e.g., "fireblast", "lightning", "holylight").</param>
	/// <param name="position">The coordinates in 3D world space.</param>
	/// <param name="scale">The visual scale multiplier.</param>
	void SpawnVisualEffect(string effectTypeId, Vector3 position, float scale = 1.0f);

	/// <summary>
	/// Spawns an overhead or area barrage volley of impact visual effects over time within a target area.
	/// </summary>
	/// <param name="effectTypeId">The type identifier of the impact visual effect.</param>
	/// <param name="targetAreaCenter">The center point of the barrage target area in 3D world space.</param>
	/// <param name="radius">The radius of the barrage volley impact area.</param>
	/// <param name="count">The total number of projectile/effect impacts to spawn.</param>
	/// <param name="intervalSeconds">The time interval in seconds between individual barrage impacts.</param>
	void SpawnBarrageVolley(string effectTypeId, Vector3 targetAreaCenter, float radius, int count, float intervalSeconds);

	/// <summary>
	/// Spawns a multi-segment ribbon beam that chains sequentially between target positions.
	/// </summary>
	/// <param name="effectTypeId">The type identifier or texture asset name of the beam effect.</param>
	/// <param name="points">An array of 3D positions through which the beam chains.</param>
	/// <param name="jumpDelay">Delay in seconds between beam segments extending to the next point.</param>
	/// <param name="forkCount">The number of extra fork/branch lines emitting from each node.</param>
	/// <param name="fadeLifetime">Duration in seconds before the beam fully fades out after reaching final point.</param>
	/// <param name="width">The width/thickness of the beam ribbon.</param>
	/// <param name="color">Optional RGB tint for the beam emission color.</param>
	void SpawnChainBeam(string effectTypeId, Vector3[] points, float jumpDelay = 0.05f, int forkCount = 0, float fadeLifetime = 0.4f, float width = 0.35f, Vector3? color = null)
	{
	}

	/// <summary>
	/// Spawns a moving planar ground shockwave along a directional vector.
	/// </summary>
	/// <param name="position">The starting center position in 3D world space.</param>
	/// <param name="direction">The direction vector along which the shockwave advances.</param>
	/// <param name="maxRadius">The maximum forward travel distance / radius.</param>
	/// <param name="speed">The speed of propagation in units per second.</param>
	/// <param name="duration">Total lifespan in seconds before fading out.</param>
	/// <param name="color">Optional RGB color tint for the shockwave.</param>
	void SpawnGroundShockwave(Vector3 position, Vector3 direction, float maxRadius = 8.0f, float speed = 12.0f, float duration = 1.0f, Vector3? color = null)
	{
	}

	/// <summary>
	/// Spawns an expanding ground ring / crack visual effect that conforms to terrain contours.
	/// </summary>
	/// <param name="position">The center position in 3D world space.</param>
	/// <param name="maxRadius">The maximum expansion radius in world units.</param>
	/// <param name="speed">The expansion speed in units per second.</param>
	/// <param name="duration">Total lifespan in seconds before fading out.</param>
	/// <param name="color">Optional RGB color tint for the ground ring.</param>
	void SpawnExpandingGroundRing(Vector3 position, float maxRadius = 8.0f, float speed = 12.0f, float duration = 1.0f, Vector3? color = null);

	/// <summary>
	/// Draws a visual line on the terrain surface between two ground coordinates.
	/// The line automatically conforms to the terrain height mesh along its path.
	/// </summary>
	/// <param name="start">The starting ground position (X, Z coordinates on the map plane).</param>
	/// <param name="end">The ending ground position (X, Z coordinates on the map plane).</param>
	/// <param name="color">Optional RGB color tint for the line.</param>
	/// <param name="duration">Duration in seconds before the line fully fades out.</param>
	/// <param name="width">The visual width/thickness of the line in world units.</param>
	void DrawLineOnTerrain(Vector2 start, Vector2 end, Vector3? color = null, float duration = 1.0f, float width = 0.2f);

	/// <summary>
	/// Spawns an expanding 3D spherical / dome burst shockwave effect.
	/// </summary>
	/// <param name="position">The center position in 3D world space.</param>
	/// <param name="maxRadius">The maximum burst radius in world units.</param>
	/// <param name="speed">The burst expansion speed in units per second.</param>
	/// <param name="duration">Total lifespan in seconds before fading out.</param>
	/// <param name="color">Optional RGB color tint for the burst sphere.</param>
	void SpawnExpandingBurstSphere(Vector3 position, float maxRadius = 6.0f, float speed = 15.0f, float duration = 0.8f, Vector3? color = null);

	/// <summary>
	/// Attaches a persistent volume shield or aura visual effect to a unit.
	/// </summary>
	/// <param name="unit">The target unit identifier.</param>
	/// <param name="auraVfxId">The identifier of the visual effect or attachment configuration.</param>
	/// <param name="scale">The visual scale multiplier.</param>
	void AttachPersistentAura(UnitId unit, string auraVfxId, float scale = 1.0f);

	/// <summary>
	/// Spawns a visual projectile moving from a starting position to a target position. A shot an ability fires belongs in
	/// that ability's template as ProjectileId instead, which lets the engine fly it and report its landing through
	/// <see cref="OnSpellImpact"/>; a projectile spawned here is only ever seen.
	/// </summary>
	/// <param name="projectileTypeId">The type identifier of the projectile (e.g., "arrow", "fireball").</param>
	/// <param name="start">The starting 3D coordinates.</param>
	/// <param name="target">The ending 3D coordinates.</param>
	/// <param name="speed">The speed of the projectile in units per second.</param>
	void SpawnProjectile(string projectileTypeId, Vector3 start, Vector3 target, float speed);

	/// <summary>
	/// Triggers a transient mesh deformation or hit impulse on a unit.
	/// </summary>
	/// <param name="unit">The target unit.</param>
	/// <param name="strength">The peak strength of the impulse deformation.</param>
	/// <param name="duration">The duration of the impulse oscillation in seconds.</param>
	/// <param name="frequency">The oscillation frequency in Hz.</param>
	void TriggerMeshImpulse(UnitId unit, float strength = 1.0f, float duration = 0.5f, float frequency = 12.0f);

	/// <summary>
	/// Triggers a transient mesh deformation or harvest shake impulse on a resource node.
	/// </summary>
	/// <param name="node">The target resource node.</param>
	/// <param name="strength">The peak strength of the impulse deformation.</param>
	/// <param name="duration">The duration of the impulse oscillation in seconds.</param>
	/// <param name="frequency">The oscillation frequency in Hz.</param>
	void TriggerResourceMeshImpulse(ResourceNodeId node, float strength = 1.0f, float duration = 0.5f, float frequency = 12.0f);

	/// <summary>
	/// Creates a persistent, stationary text label in the 3D world that stays in place until destroyed.
	/// Unlike <see cref="CreateFloatingText"/> it does not drift or fade.
	/// </summary>
	/// <param name="text">The initial text to display.</param>
	/// <param name="position">The 3D world position of the label.</param>
	/// <param name="color">The color of the text in RGB format.</param>
	/// <param name="fontSize">The font size of the label. The default of 48 matches <see cref="CreateFloatingText"/>.</param>
	/// <returns>A handle used with <see cref="SetStaticText"/>, <see cref="SetStaticTextVisible"/> and <see cref="DestroyStaticText"/>.</returns>
	int CreateStaticText(string text, Vector3 position, Vector3 color, int fontSize = 48);

	/// <summary>
	/// Changes the text displayed by a static world label.
	/// </summary>
	/// <param name="handle">The handle returned by <see cref="CreateStaticText"/>.</param>
	/// <param name="text">The new text to display.</param>
	void SetStaticText(int handle, string text);

	/// <summary>
	/// Changes the color of a static world label without recreating it.
	/// </summary>
	/// <param name="handle">The handle returned by <see cref="CreateStaticText"/>.</param>
	/// <param name="color">The new text color in RGB format.</param>
	void SetStaticTextColor(int handle, Vector3 color);

	/// <summary>
	/// Shows or hides a static world label without destroying it.
	/// </summary>
	/// <param name="handle">The handle returned by <see cref="CreateStaticText"/>.</param>
	/// <param name="visible">True to show the label, false to hide it.</param>
	void SetStaticTextVisible(int handle, bool visible);

	/// <summary>
	/// Destroys a static world label.
	/// </summary>
	/// <param name="handle">The handle returned by <see cref="CreateStaticText"/>.</param>
	void DestroyStaticText(int handle);
}
