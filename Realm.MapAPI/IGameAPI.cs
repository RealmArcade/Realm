using System.Numerics;

namespace Realm.MapAPI;

/// <summary>
/// Defines the safe, public API exposed to map scripts to interact with the game engine.
/// </summary>
public interface IGameAPI
{
	/// <summary>
	/// Gets the elapsed game time in seconds.
	/// </summary>
	float GameElapsedTime { get; }


	/// <summary>
	/// Spawns a resource node at the given position.
	/// </summary>
	/// <param name="resourceType">The type of resource node (e.g., "goldmine", "tree", "rock").</param>
	/// <param name="position">The coordinates in 3D world space.</param>
	/// <param name="amount">The initial amount of resources inside the node.</param>
	void SpawnResourceNode(string resourceType, Vector3 position, float amount);

	/// <summary>
	/// Retrieves all units currently alive in the game world.
	/// </summary>
	/// <returns>A read-only span of all active unit identifiers.</returns>
	ReadOnlySpan<UnitId> GetAllUnits();

	/// <summary>
	/// Retrieves all active units within the specified radius of a 3D position.
	/// </summary>
	/// <param name="center">The center point in 3D world space.</param>
	/// <param name="radius">The radius distance check.</param>
	/// <returns>A read-only span of unit identifiers within the radius.</returns>
	ReadOnlySpan<UnitId> GetUnitsInRadius(Vector3 center, float radius);

	/// <summary>
	/// Gets the number of resource nodes.
	/// </summary>
	int ResourceNodeCount { get; }

	/// <summary>
	/// Retrieves a resource node by its index.
	/// </summary>
	/// <param name="index">The zero-based index of the resource node.</param>
	/// <returns>The resource node identifier.</returns>
	ResourceNodeId GetResourceNode(int index);

	/// <summary>
	/// Retrieves all active resource nodes in the game world.
	/// </summary>
	/// <returns>A read-only span of all active resource node identifiers.</returns>
	ReadOnlySpan<ResourceNodeId> GetResourceNodes();

	/// <summary>
	/// Displays floating feedback text on the player's screen.
	/// </summary>
	/// <param name="text">The text message to display.</param>
	/// <param name="color">The color of the text in RGB format.</param>
	void ShowFeedbackText(string text, Vector3 color);

	/// <summary>
	/// Triggers the game victory sequence.
	/// </summary>
	void TriggerVictory();

	/// <summary>
	/// Triggers the game defeat sequence.
	/// </summary>
	void TriggerDefeat();

	/// <summary>
	/// Spawns a temporary visual target indicator ring at the specified position with the given color.
	/// </summary>
	/// <param name="position">The coordinates in 3D world space.</param>
	/// <param name="color">The color of the indicator ring in RGB format.</param>
	void SpawnTargetIndicator(Vector3 position, Vector3 color);

	/// <summary>
	/// Retrieves a unit by its unique runtime identifier.
	/// </summary>
	/// <param name="uniqueId">The unique ID of the unit to retrieve.</param>
	/// <returns>The unit identifier.</returns>
	UnitId GetUnitById(int uniqueId);

	/// <summary>
	/// Generates a new map directory containing boilerplate files for a custom map.
	/// </summary>
	/// <param name="mapName">The name of the new map script class and directory.</param>
	/// <param name="targetDirectory">The parent directory path where the map directory should be created. If left null or empty, it defaults to the standard user maps folder.</param>
	void GenerateMapDirectory(string mapName, string? targetDirectory = null);

	/// <summary>
	/// Triggered when a unit is spawned in the game world.
	/// </summary>
	event Action<UnitId>? OnUnitCreated;

	/// <summary>
	/// Triggered when a unit dies. Passes the dying unit and the killer unit if available.
	/// </summary>
	event Action<UnitId, UnitId>? OnUnitDied;

	/// <summary>
	/// Triggered when a unit takes damage. Passes the victim unit, the attacker unit, and the damage amount.
	/// </summary>
	event Action<UnitId, UnitId, float>? OnUnitDamaged;

	/// <summary>
	/// Triggered when a unit casts or channels a spell. Passes the caster unit, the spell ID, and the target position.
	/// </summary>
	event Action<UnitId, string, Vector3>? OnSpellCast;

	/// <summary>
	/// Triggered when a player types a chat message. Passes the message and the first selected unit if any.
	/// </summary>
	event Action<string, UnitId>? OnPlayerChatMessage;

	/// <summary>
	/// Triggered when the player selects a unit.
	/// </summary>
	event Action<UnitId>? OnUnitSelected;

	/// <summary>
	/// Triggered when a unit attacks another unit. Passes the attacker unit and the target unit.
	/// </summary>
	event Action<UnitId, UnitId>? OnUnitAttacked;

	/// <summary>
	/// Triggered when a ground loot item is collected by a unit. Passes the collected loot item and the collecting unit.
	/// </summary>
	event Action<LootItemId, UnitId>? OnLootItemCollected;

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
	/// Adds a buff to a unit.
	/// </summary>
	/// <param name="unit">The target unit identifier.</param>
	/// <param name="buffId">The identifier of the buff.</param>
	/// <param name="duration">The duration of the buff in seconds.</param>
	void AddBuff(UnitId unit, string buffId, float duration);

	/// <summary>
	/// Registers a stat modifier for a buff type.
	/// </summary>
	/// <param name="buffId">The identifier of the buff.</param>
	/// <param name="statName">The name of the stat (e.g. "Armor", "Attack", "MovementSpeed").</param>
	/// <param name="isPercentage">True for percentage multiplier, false for flat bonus.</param>
	/// <param name="value">The modifier value.</param>
	void RegisterBuffModifier(string buffId, string statName, bool isPercentage, float value);

	/// <summary>
	/// Registers the footprint radius used when placing or checking for overlap with a unit, building, or prop.
	/// </summary>
	/// <param name="definitionKey">A case-insensitive fragment that must be contained in the definition identifier for the rule to apply. Rules are evaluated in registration order and the first match wins.</param>
	/// <param name="radius">The base footprint radius in world units, before the object's scale is applied.</param>
	void RegisterPlacementRadius(string definitionKey, float radius);

	/// <summary>
	/// Registers a building definition as a drop-off point where workers deliver gathered resources.
	/// </summary>
	/// <param name="definitionKey">A case-insensitive fragment that must be contained in the building definition identifier.</param>
	void RegisterResourceDropoffBuilding(string definitionKey);

	/// <summary>
	/// Registers a unit definition fragment identifying workers; any building able to produce a matching unit is treated as a resource drop-off point.
	/// </summary>
	/// <param name="unitKey">A case-sensitive fragment that must be contained in the unit definition identifier in a building's build options.</param>
	void RegisterResourceWorkerUnit(string unitKey);

	/// <summary>
	/// Registers a building definition as a player's primary building, which camera shortcuts focus on.
	/// </summary>
	/// <param name="definitionId">The exact, case-insensitive building definition identifier.</param>
	void RegisterPrimaryBuilding(string definitionId);

	/// <summary>
	/// Declares which resource type a harvestable node prop yields.
	/// </summary>
	/// <param name="nodeDefinitionId">The prop definition identifier of the harvestable node (case-insensitive).</param>
	/// <param name="resourceType">The resource type gathered from the node, such as "gold", "wood" or "stone".</param>
	void RegisterResourceNodeType(string nodeDefinitionId, string resourceType);

	/// <summary>
	/// Executes an ability generically by ID on a unit targeting a position.
	/// </summary>
	/// <param name="unit">The unit casting the ability.</param>
	/// <param name="abilityId">The identifier of the ability.</param>
	/// <param name="targetPosition">The target position in 3D world space.</param>
	void CastAbility(UnitId unit, string abilityId, Vector3 targetPosition);

	/// <summary>
	/// Gets the remaining cooldown of a unit's ability in seconds.
	/// </summary>
	/// <param name="unit">The unit identifier.</param>
	/// <param name="abilityId">The identifier of the ability.</param>
	/// <returns>Remaining cooldown in seconds, or 0 if ready or not found.</returns>
	float GetAbilityCooldown(UnitId unit, string abilityId);

	/// <summary>
	/// Sets the cooldown of a unit's ability.
	/// </summary>
	/// <param name="unit">The unit identifier.</param>
	/// <param name="abilityId">The identifier of the ability.</param>
	/// <param name="cooldown">The cooldown duration in seconds.</param>
	void SetAbilityCooldown(UnitId unit, string abilityId, float cooldown);

	/// <summary>
	/// Removes a buff from a unit.
	/// </summary>
	/// <param name="unit">The target unit identifier.</param>
	/// <param name="buffId">The identifier of the buff to remove.</param>
	void RemoveBuff(UnitId unit, string buffId);

	/// <summary>
	/// Gets active modifiers on a unit.
	/// </summary>
	/// <param name="unit">The target unit identifier.</param>
	/// <returns>A collection of modifier description strings.</returns>
	IEnumerable<string> GetModifiers(UnitId unit);

	/// <summary>
	/// Spawns a visual projectile moving from a starting position to a target position.
	/// </summary>
	/// <param name="projectileTypeId">The type identifier of the projectile (e.g., "arrow", "fireball").</param>
	/// <param name="start">The starting 3D coordinates.</param>
	/// <param name="target">The ending 3D coordinates.</param>
	/// <param name="speed">The speed of the projectile in units per second.</param>
	void SpawnProjectile(string projectileTypeId, Vector3 start, Vector3 target, float speed);

	/// <summary>
	/// Displays or hides a custom leaderboard panel on the player's screen.
	/// </summary>
	/// <param name="title">The header title of the leaderboard.</param>
	/// <param name="visible">True to display the leaderboard, false to hide it.</param>
	void SetLeaderboardVisible(string title, bool visible);

	/// <summary>
	/// Sets or updates a value on the leaderboard.
	/// </summary>
	/// <param name="label">The row label (e.g. player name or score metric).</param>
	/// <param name="value">The value to display.</param>
	void SetLeaderboardValue(string label, string value);

	/// <summary>
	/// Clears all entries from the current leaderboard.
	/// </summary>
	void ClearLeaderboard();

	/// <summary>
	/// Displays or hides the multi-column stats summary table on all players' screens.
	/// </summary>
	/// <param name="title">Header title of the summary table.</param>
	/// <param name="visible">True to display the table, false to hide it.</param>
	void ShowSummaryTable(string title, bool visible);

	/// <summary>
	/// Configures custom column header labels for the stats summary table.
	/// Headers[0] labels the row identifier/player column. Subsequent elements label the metric columns.
	/// Headers must be passed already localized by the caller.
	/// </summary>
	/// <param name="columnHeaders">Array of header titles to display at the top of the table.</param>
	void SetSummaryTableHeaders(params string[] columnHeaders);

	/// <summary>
	/// Sets or updates a row in the stats summary table.
	/// </summary>
	/// <param name="rowKey">Unique row identifier or player label (corresponds to Headers[0]).</param>
	/// <param name="cellValues">Metric values corresponding to Headers[1..N].</param>
	void SetSummaryTableRow(string rowKey, params string[] cellValues);

	/// <summary>
	/// Clears all rows from the active stats summary table.
	/// </summary>
	void ClearSummaryTable();

	/// <summary>
	/// Retrieves the preferred language code of the specified player (e.g. "en", "es").
	/// </summary>
	/// <param name="playerIndex">Zero-based player index.</param>
	/// <returns>ISO language code string.</returns>
	string GetPlayerLanguage(int playerIndex);

	/// <summary>
	/// Translates a localization key using the player's preferred language with English fallback.
	/// </summary>
	/// <param name="key">Localization translation key.</param>
	/// <param name="playerIndex">Zero-based player slot index.</param>
	/// <returns>Translated text string.</returns>
	string Translate(string key, int playerIndex);

	/// <summary>
	/// Starts a countdown timer UI on the screen.
	/// </summary>
	/// <param name="duration">The duration in seconds.</param>
	/// <param name="label">The title or description of the countdown timer.</param>
	void StartCountdownTimer(float duration, string label);

	/// <summary>
	/// Stops and hides the active countdown timer.
	/// </summary>
	void StopCountdownTimer();

	/// <summary>
	/// Shakes the game camera to simulate an explosion or impact.
	/// </summary>
	/// <param name="intensity">The shake strength multiplier.</param>
	/// <param name="duration">The time in seconds the shake lasts.</param>
	void ShakeCamera(float intensity, float duration);

	/// <summary>
	/// Sets the time of day to a specific hour (0.0 to 24.0).
	/// </summary>
	/// <param name="time">The hour value.</param>
	void SetTimeOfDay(float time);

	/// <summary>
	/// Enables or disables the day-night cycle.
	/// </summary>
	/// <param name="enabled">True to enable, false to freeze the time of day.</param>
	void SetDayNightCycleEnabled(bool enabled);

	/// <summary>
	/// Kills the specified unit naturally, triggering death animations and bounties.
	/// </summary>
	/// <param name="unit">The unit to kill.</param>
	/// <param name="executeDespawnShader">True to execute the despawn or death shader effect if configured, false to skip.</param>
	/// <param name="playDeathAnimation">True to play the death animation if available, false to skip.</param>
	void KillUnit(UnitId unit, bool executeDespawnShader = true, bool playDeathAnimation = true);

	/// <summary>
	/// Instantly removes the specified unit from the game.
	/// </summary>
	/// <param name="unit">The unit to destroy.</param>
	/// <param name="executeDespawnShader">True to execute the despawn or death shader effect if configured, false to skip.</param>
	/// <param name="playDeathAnimation">True to play the death animation if available, false to skip.</param>
	void DestroyUnit(UnitId unit, bool executeDespawnShader = true, bool playDeathAnimation = true);


	/// <summary>
	/// Gets the total number of player slots in the current session (including empty slots).
	/// </summary>
	int PlayerCount { get; }

	/// <summary>
	/// Returns the display name of the player occupying the specified zero-based slot index.
	/// Returns an empty string if the slot is not occupied.
	/// </summary>
	/// <param name="playerIndex">Zero-based player slot index.</param>
	string GetPlayerName(int playerIndex);

	/// <summary>
	/// Sets the gold resource for the specified player slot.
	/// </summary>
	/// <param name="playerIndex">Zero-based player slot index.</param>
	/// <param name="amount">The new gold amount.</param>
	void SetPlayerGold(int playerIndex, float amount);

	/// <summary>
	/// Adds the specified amount to the gold resource for the given player slot.
	/// Negative values subtract gold.
	/// </summary>
	/// <param name="playerIndex">Zero-based player slot index.</param>
	/// <param name="delta">The amount to add (may be negative).</param>
	void AdjustPlayerGold(int playerIndex, float delta);

	/// <summary>
	/// Spawns a unit owned by the specified player slot at the given position.
	/// </summary>
	/// <param name="unitTypeId">The type identifier of the unit to spawn.</param>
	/// <param name="position">The spawn coordinates in 2D ground space (X, Z).</param>
	/// <param name="playerIndex">Zero-based player slot index that will own the unit.</param>
	/// <param name="executeSpawnShader">True to play the spawn shader effect if configured, false to skip.</param>
	/// <param name="bypassPopulation">True to bypass population cap limits when spawning, false to enforce population limits.</param>
	/// <returns>The spawned unit identifier.</returns>
	UnitId SpawnUnitForPlayer(string unitTypeId, Vector2 position, int playerIndex, bool executeSpawnShader = true, bool bypassPopulation = false);

	/// <summary>
	/// Retrieves all alive units owned by the specified player slot.
	/// </summary>
	/// <param name="playerIndex">Zero-based player slot index.</param>
	/// <returns>A read-only span of units belonging to that player.</returns>
	ReadOnlySpan<UnitId> GetUnitsOwnedByPlayer(int playerIndex);

	/// <summary>
	/// Triggers the defeat condition for the specified player slot, playing the defeat sequence only for that player.
	/// </summary>
	/// <param name="playerIndex">Zero-based player slot index.</param>
	/// <param name="reason">An optional human-readable reason string shown to the player.</param>
	void TriggerPlayerDefeat(int playerIndex, string reason = "");

	/// <summary>
	/// Triggers the victory condition for the specified player slot.
	/// </summary>
	/// <param name="playerIndex">Zero-based player slot index.</param>
	void TriggerPlayerVictory(int playerIndex);

	/// <summary>
	/// Broadcasts a text message to all players simultaneously.
	/// </summary>
	/// <param name="message">The message string to display.</param>
	void BroadcastMessage(string message);

	/// <summary>
	/// Sends a text message to a single player slot only.
	/// </summary>
	/// <param name="playerIndex">Zero-based player slot index.</param>
	/// <param name="message">The message string to display.</param>
	void SendMessageToPlayer(int playerIndex, string message);

	/// <summary>
	/// Triggered when a scheduled timer expires.
	/// </summary>
	event Action<int>? OnTimerExpired;

	/// <summary>
	/// Schedules a one-shot callback to fire after the specified delay (in seconds).
	/// </summary>
	/// <param name="delay">Delay in seconds before the callback is invoked.</param>
	/// <returns>A handle that can be passed to <see cref="CancelTimer"/> to cancel the timer before it fires.</returns>
	int ScheduleTimer(float delay);

	/// <summary>
	/// Schedules a repeating callback to fire every <paramref name="interval"/> seconds.
	/// </summary>
	/// <param name="interval">Interval in seconds between invocations.</param>
	/// <returns>A handle that can be passed to <see cref="CancelTimer"/> to stop the repeating timer.</returns>
	int ScheduleRepeatingTimer(float interval);

	/// <summary>
	/// Schedules a one-shot callback to fire after the specified delay (in seconds).
	/// </summary>
	int ScheduleTimer(float delay, Action callback)
	{
		int handle = ScheduleTimer(delay);
		Action<int>? handler = null;
		handler = (h) =>
		{
			if (h == handle)
			{
				callback();
				OnTimerExpired -= handler;
			}
		};
		OnTimerExpired += handler;
		return handle;
	}

	/// <summary>
	/// Schedules a repeating callback to fire every <paramref name="interval"/> seconds.
	/// </summary>
	int ScheduleRepeatingTimer(float interval, Action callback)
	{
		int handle = ScheduleRepeatingTimer(interval);
		OnTimerExpired += (h) =>
		{
			if (h == handle) callback();
		};
		return handle;
	}

	/// <summary>
	/// Cancels a previously scheduled timer, preventing any future invocations.
	/// </summary>
	/// <param name="timerHandle">The handle returned by <see cref="ScheduleTimer(float)"/> or <see cref="ScheduleRepeatingTimer(float)"/>.</param>
	void CancelTimer(int timerHandle);

	/// <summary>
	/// Returns a uniformly distributed random integer in the inclusive range [<paramref name="min"/>, <paramref name="max"/>].
	/// </summary>
	/// <param name="min">Inclusive lower bound.</param>
	/// <param name="max">Inclusive upper bound.</param>
	int RandomInt(int min, int max);

	/// <summary>
	/// Returns a uniformly distributed random float in the range [<paramref name="min"/>, <paramref name="max"/>].
	/// </summary>
	/// <param name="min">Inclusive lower bound.</param>
	/// <param name="max">Inclusive upper bound.</param>
	float RandomFloat(float min, float max);


	/// <summary>
	/// Returns the start location for the specified player slot in 3D world space.
	/// The Y component represents terrain height at that position.
	/// </summary>
	/// <param name="playerIndex">Zero-based player slot index.</param>
	/// <returns>The start position in 3D world space, or <see cref="Vector3.Zero"/> if the slot has no start location.</returns>
	Vector3 GetPlayerStartLocation(int playerIndex);

	/// <summary>
	/// Assigns the specified player slot to a numbered team.
	/// Players on the same team are treated as allies.
	/// </summary>
	/// <param name="playerIndex">Zero-based player slot index.</param>
	/// <param name="teamIndex">Zero-based team index.</param>
	void SetPlayerTeam(int playerIndex, int teamIndex);

	/// <summary>
	/// Configures whether two player slots treat each other as passive allies
	/// (shared vision, units do not attack each other).
	/// </summary>
	/// <param name="playerIndex">Zero-based index of the first player.</param>
	/// <param name="otherPlayerIndex">Zero-based index of the second player.</param>
	/// <param name="allied">True to make them allied; false to revert to enemies.</param>
	void SetPlayersAllied(int playerIndex, int otherPlayerIndex, bool allied);

	/// <summary>
	/// Returns true if two player slots treat each other as passive allies.
	/// </summary>
	/// <param name="playerIndex">Index of the first player.</param>
	/// <param name="otherPlayerIndex">Index of the second player.</param>
	bool ArePlayersAllied(int playerIndex, int otherPlayerIndex);

	/// <summary>
	/// Sets whether the specified player slot is active in the game session.
	/// </summary>
	/// <param name="playerIndex">Player slot index.</param>
	/// <param name="active">True if active, false otherwise.</param>
	void SetPlayerActive(int playerIndex, bool active);

	/// <summary>
	/// Kicks the specified player slot from the match.
	/// </summary>
	/// <param name="playerIndex">Player slot index.</param>
	/// <param name="reason">Optional reason message for the kick.</param>
	void KickPlayer(int playerIndex, string reason = "Kicked by host");

	/// <summary>
	/// Adds or configures an AI computer player in the specified slot.
	/// </summary>
	/// <param name="playerIndex">The player slot index.</param>
	/// <param name="name">Optional display name for the bot.</param>
	/// <param name="teamIndex">Optional team index (default -1 for unassigned).</param>
	/// <returns>True if successfully added or configured.</returns>
	bool AddComputerPlayer(int playerIndex, string? name = null, int teamIndex = -1);


	/// <summary>
	/// Applies an RGBA color tint to the specified unit's 3D mesh.
	/// Use <see cref="Vector3.One"/> (white) to reset to the default appearance.
	/// </summary>
	/// <param name="unit">The unit to tint.</param>
	/// <param name="color">RGB color values in the range [0, 1].</param>
	void SetUnitColor(UnitId unit, Vector3 color);

	/// <summary>
	/// Sets the rotation angle or facing direction of the unit in radians or towards a target direction.
	/// </summary>
	/// <param name="unit">The unit whose facing direction is being set.</param>
	/// <param name="facingRadians">The target facing angle in radians around the vertical axis.</param>
	void SetUnitFacing(UnitId unit, float facingRadians);

	/// <summary>
	/// Sets the facing direction of the unit to look at the specified target position.
	/// </summary>
	/// <param name="unit">The unit to rotate.</param>
	/// <param name="targetPosition">The 3D coordinates in world space to face towards.</param>
	void SetUnitFacing(UnitId unit, Vector3 targetPosition)
	{
		Vector3 delta = targetPosition - unit.Position;
		float angle = MathF.Atan2(delta.X, delta.Z);
		SetUnitFacing(unit, angle);
	}

	/// <summary>
	/// Orders the unit to attack-move to the specified world position.
	/// The unit will attack any enemies it encounters along the way.
	/// </summary>
	/// <param name="unit">The unit to command.</param>
	/// <param name="destination">The destination position in world coordinates.</param>
	void IssueAttackMoveOrder(UnitId unit, Vector3 destination);

	/// <summary>
	/// Orders the unit to cast a targeted spell on a specific enemy unit.
	/// Does nothing if the unit does not have the specified ability.
	/// </summary>
	/// <param name="caster">The casting unit.</param>
	/// <param name="abilityId">The ability identifier string.</param>
	/// <param name="target">The target unit.</param>
	void IssueCastOrder(UnitId caster, string abilityId, UnitId target);

	/// <summary>
	/// Orders the unit to cast a ground-targeted spell at the specified world position.
	/// Does nothing if the unit does not have the specified ability.
	/// </summary>
	/// <param name="caster">The casting unit.</param>
	/// <param name="abilityId">The ability identifier string.</param>
	/// <param name="position">The target position in world coordinates.</param>
	void IssueCastOrderAt(UnitId caster, string abilityId, Vector3 position);

	/// <summary>
	/// Orders the unit to activate or deactivate an auto-cast ability.
	/// </summary>
	/// <param name="unit">The unit that owns the ability.</param>
	/// <param name="abilityId">The ability identifier string.</param>
	/// <param name="active">True to activate auto-cast; false to deactivate.</param>
	void SetAbilityAutoCast(UnitId unit, string abilityId, bool active);


	/// <summary>
	/// Sets whether the specified player slot is treated as a proxy (AI-controlled
	/// stand-in that mirrors another player's drafted units in combat).
	/// Proxy slots can be given computer-controller behaviour independent of the real player.
	/// </summary>
	/// <param name="playerIndex">Zero-based player slot index.</param>
	/// <param name="isProxy">True to enable proxy/computer control; false for user control.</param>
	void SetPlayerComputerControlled(int playerIndex, bool isProxy);


	/// <summary>
	/// Appends a new row to the leaderboard panel with the given label and value.
	/// Must call <see cref="SetLeaderboardVisible"/> first to make the panel visible.
	/// </summary>
	/// <param name="label">Row label text.</param>
	/// <param name="value">Row value text.</param>
	/// <param name="color">Optional RGB tint for the label text.</param>
	void AddLeaderboardRow(string label, string value, Vector3? color = null);

	/// <summary>
	/// Sets the wood (lumber) resource for the specified player slot.
	/// </summary>
	/// <param name="playerIndex">Zero-based player slot index.</param>
	/// <param name="amount">The new wood amount.</param>
	void SetPlayerWood(int playerIndex, float amount);

	/// <summary>
	/// Adds the specified amount to the wood resource for the given player slot.
	/// Negative values subtract wood.
	/// </summary>
	/// <param name="playerIndex">Zero-based player slot index.</param>
	/// <param name="delta">The amount to add (may be negative).</param>
	void AdjustPlayerWood(int playerIndex, float delta);


	/// <summary>
	/// Transfers ownership of the specified unit to a different player slot.
	/// </summary>
	/// <param name="unit">The unit to transfer.</param>
	/// <param name="playerIndex">Zero-based player slot index of the new owner.</param>
	void SetUnitOwner(UnitId unit, int playerIndex);

	/// <summary>
	/// Sets the maximum population capacity for the specified player slot.
	/// </summary>
	/// <param name="playerIndex">Zero-based player slot index.</param>
	/// <param name="max">The new population cap.</param>
	void SetPlayerMaxPopulation(int playerIndex, int max);


	/// <summary>
	/// Updates the countdown timer label without resetting the current timer.
	/// Useful for changing the displayed title mid-countdown.
	/// </summary>
	/// <param name="label">The new label text for the active countdown timer.</param>
	void SetCountdownTimerLabel(string label);


	/// <summary>
	/// Orders all units owned by the specified player slot to attack-move to the destination.
	/// </summary>
	/// <param name="playerIndex">Zero-based player slot index.</param>
	/// <param name="destination">The destination in world coordinates.</param>
	void IssueAttackMoveOrderToPlayer(int playerIndex, Vector3 destination);

	/// <summary>
	/// Returns the number of units owned by the specified player.
	/// </summary>
	/// <param name="playerIndex">Zero-based player slot index.</param>
	int CountUnitsOwnedByPlayer(int playerIndex);


	/// <summary>
	/// Defines a named rectangular zone in 2D map space (X/Z axes).
	/// Returns a zone handle that can be used with <see cref="OnUnitEnterZone"/>.
	/// </summary>
	/// <param name="minX">West boundary of the zone.</param>
	/// <param name="minZ">South boundary of the zone.</param>
	/// <param name="maxX">East boundary of the zone.</param>
	/// <param name="maxZ">North boundary of the zone.</param>
	/// <returns>An opaque zone identifier.</returns>
	int DefineZone(float minX, float minZ, float maxX, float maxZ);

	/// <summary>
	/// Returns the center position (in 3D world space) of the zone with the given handle.
	/// The Y component is terrain height at that point.
	/// </summary>
	/// <param name="zoneHandle">Zone handle returned by <see cref="DefineZone"/>.</param>
	Vector3 GetZoneCenter(int zoneHandle);

	/// <summary>
	/// Triggered whenever a unit enters the specified zone.
	/// The callback receives the unit that entered and the zone handle it entered.
	/// </summary>
	event Action<UnitId, int>? OnUnitEnterZone;


	/// <summary>
	/// Associates an integer route-state value with the specified unit.
	/// Useful for tracking which waypoint branch a unit is following.
	/// </summary>
	/// <param name="unit">The unit to tag.</param>
	/// <param name="state">An arbitrary integer value meaningful to the caller.</param>
	void SetUnitRouteState(UnitId unit, int state);

	/// <summary>
	/// Retrieves the integer route-state value previously set on the unit,
	/// or 0 if none has been set.
	/// </summary>
	/// <param name="unit">The unit to query.</param>
	int GetUnitRouteState(UnitId unit);


	/// <summary>
	/// Sets the level of the specified hero unit.
	/// Has no effect on non-hero units.
	/// </summary>
	/// <param name="unit">The hero unit whose level should be changed.</param>
	/// <param name="level">The new level value.</param>
	void SetUnitLevel(UnitId unit, int level);


	/// <summary>
	/// Triggered when a player leaves or disconnects from the game.
	/// The integer argument is the zero-based player slot index.
	/// </summary>
	event Action<int>? OnPlayerLeft;

	/// <summary>
	/// Sets the kill count for the specified player slot directly.
	/// </summary>
	/// <param name="playerIndex">Zero-based player slot index.</param>
	/// <param name="kills">The new kill count.</param>
	void SetPlayerKills(int playerIndex, int kills);


	/// <summary>
	/// Orders the unit to move to the specified destination without attacking anything en route.
	/// Unlike <see cref="IssueAttackMoveOrder(UnitId, Vector3)"/>, this is a pure positional move command.
	/// </summary>
	/// <param name="unit">The unit to command.</param>
	/// <param name="destination">The destination in world coordinates.</param>
	void IssueMoveOrder(UnitId unit, Vector3 destination);


	/// <summary>
	/// Adds or removes a spell-immunity buff from the specified unit.
	/// Spell-immune units cannot be targeted by magic-type attacks or abilities.
	/// </summary>
	/// <param name="unit">The unit to modify.</param>
	/// <param name="immune">True to make the unit spell-immune; false to remove the immunity.</param>
	void SetUnitSpellImmune(UnitId unit, bool immune);

	/// <summary>
	/// Checks if a named coordinate box exists.
	/// </summary>
	bool HasCoordinate(string coordinateName);

	/// <summary>
	/// Gets the minimum coordinates of a named coordinate box.
	/// </summary>
	Vector3 GetCoordinateMin(string coordinateName);

	/// <summary>
	/// Gets the maximum coordinates of a named coordinate box.
	/// </summary>
	Vector3 GetCoordinateMax(string coordinateName);

	/// <summary>
	/// Gets the bounding coordinates of a named coordinate box drawn in the map editor.
	/// </summary>
	/// <param name="coordinateName">The name of the coordinate box.</param>
	/// <param name="min">The minimum coordinates of the box.</param>
	/// <param name="max">The maximum coordinates of the box.</param>
	/// <returns>True if the coordinate box exists, false otherwise.</returns>
	bool TryGetCoordinate(string coordinateName, out Vector3 min, out Vector3 max)
	{
		if (HasCoordinate(coordinateName))
		{
			min = GetCoordinateMin(coordinateName);
			max = GetCoordinateMax(coordinateName);
			return true;
		}

		min = Vector3.Zero;
		max = Vector3.Zero;
		return false;
	}

	/// <summary>
	/// Checks if a 3D position is inside a named coordinate box.
	/// </summary>
	/// <param name="position">The position to check.</param>
	/// <param name="coordinateName">The name of the coordinate box.</param>
	/// <returns>True if the position is inside the coordinate box, false otherwise.</returns>
	bool IsPositionInCoordinate(Vector3 position, string coordinateName);

	/// <summary>
	/// Gets the bounding coordinate box drawn in the map editor by name.
	/// </summary>
	/// <param name="coordinateName">The name of the coordinate box.</param>
	/// <returns>A <see cref="Coordinate"/> representing the bounding region.</returns>
	Coordinate GetCoordinate(string coordinateName)
	{
		return new Coordinate(GetCoordinateMin(coordinateName), GetCoordinateMax(coordinateName));
	}

	/// <summary>
	/// Gets the bounding coordinates of a named coordinate box drawn in the map editor.
	/// </summary>
	/// <param name="coordinateName">The name of the coordinate box.</param>
	/// <param name="coordinate">The resulting coordinate box if found.</param>
	/// <returns>True if the coordinate box exists, false otherwise.</returns>
	bool TryGetCoordinate(string coordinateName, out Coordinate coordinate)
	{
		if (HasCoordinate(coordinateName))
		{
			coordinate = GetCoordinate(coordinateName);
			return true;
		}

		coordinate = default;
		return false;
	}

	/// <summary>
	/// Checks if a 3D position is inside a coordinate bounding box.
	/// </summary>
	/// <param name="position">The position to check.</param>
	/// <param name="coordinate">The coordinate box to check against.</param>
	/// <returns>True if the position is inside the coordinate box, false otherwise.</returns>
	bool IsPositionInCoordinate(Vector3 position, Coordinate coordinate)
	{
		float minX = MathF.Min(coordinate.Min.X, coordinate.Max.X);
		float maxX = MathF.Max(coordinate.Min.X, coordinate.Max.X);
		float minZ = MathF.Min(coordinate.Min.Z, coordinate.Max.Z);
		float maxZ = MathF.Max(coordinate.Min.Z, coordinate.Max.Z);
		return position.X >= minX && position.X <= maxX && position.Z >= minZ && position.Z <= maxZ;
	}

	/// <summary>
	/// Spawns a new unit for a specific player at the center of the given coordinate region.
	/// </summary>
	/// <param name="unitTypeId">The type identifier of the unit to spawn.</param>
	/// <param name="coordinate">The coordinate region whose center will be used as spawn location.</param>
	/// <param name="playerIndex">The player index who will own the unit.</param>
	/// <param name="executeSpawnShader">True to play the spawn shader effect if configured, false to skip.</param>
	/// <param name="bypassPopulation">True to bypass population cap limits when spawning, false to enforce population limits.</param>
	/// <returns>The spawned <see cref="UnitId"/>.</returns>
	UnitId SpawnUnitForPlayer(string unitTypeId, Coordinate coordinate, int playerIndex, bool executeSpawnShader = true, bool bypassPopulation = false)
	{
		return SpawnUnitForPlayer(unitTypeId, new Vector2(coordinate.Center.X, coordinate.Center.Z), playerIndex, executeSpawnShader, bypassPopulation);
	}

	/// <summary>
	/// Orders a unit to move to the center of the specified coordinate region.
	/// </summary>
	/// <param name="unit">The unit to order.</param>
	/// <param name="destination">The coordinate region to move to.</param>
	void IssueMoveOrder(UnitId unit, Coordinate destination)
	{
		IssueMoveOrder(unit, destination.Center);
	}

	/// <summary>
	/// Orders a unit to attack-move to the center of the specified coordinate region.
	/// </summary>
	/// <param name="unit">The unit to order.</param>
	/// <param name="destination">The coordinate region to attack-move towards.</param>
	void IssueAttackMoveOrder(UnitId unit, Coordinate destination)
	{
		IssueAttackMoveOrder(unit, destination.Center);
	}

	/// <summary>
	/// Dynamically alters the movement-type pathing flags for a designated rectangular region on the terrain navmesh.
	/// </summary>
	/// <param name="minX">The minimum X coordinate in world space.</param>
	/// <param name="minZ">The minimum Z coordinate in world space.</param>
	/// <param name="maxX">The maximum X coordinate in world space.</param>
	/// <param name="maxZ">The maximum Z coordinate in world space.</param>
	/// <param name="pathingFlags">The movement-type pathing flags to allow or block (e.g. Ground, Flying, ShallowWater).</param>
	/// <param name="blocked">True to block/remove the specified pathing flags; false to allow/restore them.</param>
	void SetRegionPathing(float minX, float minZ, float maxX, float maxZ, PathingFlags pathingFlags, bool blocked);

	/// <summary>
	/// Dynamically alters the movement-type pathing flags for a named coordinate region.
	/// </summary>
	/// <param name="coordinateName">The name of the coordinate region defined in the map editor.</param>
	/// <param name="pathingFlags">The movement-type pathing flags to allow or block (e.g. Ground, Flying, ShallowWater).</param>
	/// <param name="blocked">True to block/remove the specified pathing flags; false to allow/restore them.</param>
	void SetRegionPathing(string coordinateName, PathingFlags pathingFlags, bool blocked)
	{
		if (TryGetCoordinate(coordinateName, out var coord))
		{
			SetRegionPathing(coord, pathingFlags, blocked);
		}
	}

	/// <summary>
	/// Dynamically alters the movement-type pathing flags for a coordinate bounding region.
	/// </summary>
	/// <param name="coordinate">The coordinate bounding region.</param>
	/// <param name="pathingFlags">The movement-type pathing flags to allow or block (e.g. Ground, Flying, ShallowWater).</param>
	/// <param name="blocked">True to block/remove the specified pathing flags; false to allow/restore them.</param>
	void SetRegionPathing(Coordinate coordinate, PathingFlags pathingFlags, bool blocked)
	{
		SetRegionPathing(
			MathF.Min(coordinate.Min.X, coordinate.Max.X),
			MathF.Min(coordinate.Min.Z, coordinate.Max.Z),
			MathF.Max(coordinate.Min.X, coordinate.Max.X),
			MathF.Max(coordinate.Min.Z, coordinate.Max.Z),
			pathingFlags,
			blocked);
	}

	/// <summary>
	/// Updates the traversability status of a rectangular world boundary region (alias for <see cref="SetRegionPathing(float, float, float, float, PathingFlags, bool)"/>).
	/// </summary>
	/// <param name="minX">The minimum X coordinate in world space.</param>
	/// <param name="minZ">The minimum Z coordinate in world space.</param>
	/// <param name="maxX">The maximum X coordinate in world space.</param>
	/// <param name="maxZ">The maximum Z coordinate in world space.</param>
	/// <param name="pathingFlags">The movement-type pathing flags to allow or block.</param>
	/// <param name="blocked">True to block the boundary; false to open/restore traversability.</param>
	void UpdateWorldBoundaryStatus(float minX, float minZ, float maxX, float maxZ, PathingFlags pathingFlags, bool blocked)
	{
		SetRegionPathing(minX, minZ, maxX, maxZ, pathingFlags, blocked);
	}

	/// <summary>
	/// Updates the traversability status of a named world boundary region (alias for <see cref="SetRegionPathing(string, PathingFlags, bool)"/>).
	/// </summary>
	/// <param name="coordinateName">The name of the coordinate region defined in the map editor.</param>
	/// <param name="pathingFlags">The movement-type pathing flags to allow or block.</param>
	/// <param name="blocked">True to block the boundary; false to open/restore traversability.</param>
	void UpdateWorldBoundaryStatus(string coordinateName, PathingFlags pathingFlags, bool blocked)
	{
		SetRegionPathing(coordinateName, pathingFlags, blocked);
	}

	/// <summary>
	/// Retrieves the combined active pathing flags across the specified rectangular region.
	/// </summary>
	/// <param name="minX">The minimum X coordinate in world space.</param>
	/// <param name="minZ">The minimum Z coordinate in world space.</param>
	/// <param name="maxX">The maximum X coordinate in world space.</param>
	/// <param name="maxZ">The maximum Z coordinate in world space.</param>
	/// <returns>A bitmask of <see cref="PathingFlags"/> representing traversability across the region.</returns>
	PathingFlags GetRegionPathing(float minX, float minZ, float maxX, float maxZ);

	/// <summary>
	/// Retrieves the combined active pathing flags across a named coordinate region.
	/// </summary>
	/// <param name="coordinateName">The name of the coordinate region defined in the map editor.</param>
	/// <returns>A bitmask of <see cref="PathingFlags"/> representing traversability across the region.</returns>
	PathingFlags GetRegionPathing(string coordinateName)
	{
		if (TryGetCoordinate(coordinateName, out var coord))
		{
			return GetRegionPathing(coord);
		}
		return PathingFlags.None;
	}

	/// <summary>
	/// Retrieves the combined active pathing flags across a coordinate bounding region.
	/// </summary>
	/// <param name="coordinate">The coordinate bounding region.</param>
	/// <returns>A bitmask of <see cref="PathingFlags"/> representing traversability across the region.</returns>
	PathingFlags GetRegionPathing(Coordinate coordinate)
	{
		return GetRegionPathing(
			MathF.Min(coordinate.Min.X, coordinate.Max.X),
			MathF.Min(coordinate.Min.Z, coordinate.Max.Z),
			MathF.Max(coordinate.Min.X, coordinate.Max.X),
			MathF.Max(coordinate.Min.Z, coordinate.Max.Z));
	}

	/// <summary>
	/// Checks whether the specified pathing flags are currently blocked in the designated rectangular region.
	/// </summary>
	/// <param name="minX">The minimum X coordinate in world space.</param>
	/// <param name="minZ">The minimum Z coordinate in world space.</param>
	/// <param name="maxX">The maximum X coordinate in world space.</param>
	/// <param name="maxZ">The maximum Z coordinate in world space.</param>
	/// <param name="pathingFlags">The movement-type pathing flags to test (defaults to Ground).</param>
	/// <returns>True if the specified flags are blocked; false if passable.</returns>
	bool IsRegionPathingBlocked(float minX, float minZ, float maxX, float maxZ, PathingFlags pathingFlags = PathingFlags.Ground)
	{
		return (GetRegionPathing(minX, minZ, maxX, maxZ) & pathingFlags) == 0;
	}

	/// <summary>
	/// Checks whether the specified pathing flags are currently blocked in a named coordinate region.
	/// </summary>
	/// <param name="coordinateName">The name of the coordinate region defined in the map editor.</param>
	/// <param name="pathingFlags">The movement-type pathing flags to test (defaults to Ground).</param>
	/// <returns>True if the specified flags are blocked; false if passable.</returns>
	bool IsRegionPathingBlocked(string coordinateName, PathingFlags pathingFlags = PathingFlags.Ground)
	{
		return (GetRegionPathing(coordinateName) & pathingFlags) == 0;
	}

	/// <summary>
	/// Checks whether the specified pathing flags are currently blocked in a coordinate bounding region.
	/// </summary>
	/// <param name="coordinate">The coordinate bounding region.</param>
	/// <param name="pathingFlags">The movement-type pathing flags to test (defaults to Ground).</param>
	/// <returns>True if the specified flags are blocked; false if passable.</returns>
	bool IsRegionPathingBlocked(Coordinate coordinate, PathingFlags pathingFlags = PathingFlags.Ground)
	{
		return (GetRegionPathing(coordinate) & pathingFlags) == 0;
	}

	/// <summary>
	/// Adds an ability to the specified unit type definition.
	/// </summary>
	/// <param name="unitTypeId">The type identifier of the unit/structure.</param>
	/// <param name="abilityId">The identifier of the ability to add.</param>
	void AddUnitTypeAbility(string unitTypeId, string abilityId);

	/// <summary>
	/// Retrieves the ordered list of ability identifiers configured for the specified unit or building type.
	/// </summary>
	/// <param name="unitTypeId">The unit or building type identifier.</param>
	/// <returns>A read-only list of ability identifiers configured for the unit type (never null).</returns>
	IReadOnlyList<string> GetUnitTypeAbilities(string unitTypeId);

	/// <summary>
	/// Retrieves the authored display name for the specified unit or building type.
	/// </summary>
	/// <param name="unitTypeId">The unit or building type identifier.</param>
	/// <returns>The localized or authored display name, or a fallback string if unauthored.</returns>
	string GetUnitName(string unitTypeId);

	/// <summary>
	/// Retrieves the authored display name for the specified ability type.
	/// </summary>
	/// <param name="abilityId">The ability type identifier.</param>
	/// <returns>The localized or authored display name, or a fallback string if unauthored.</returns>
	string GetAbilityName(string abilityId);

	/// <summary>
	/// Enables or disables automated combat AI for a specific unit instance.
	/// </summary>
	/// <param name="unit">The unit to modify.</param>
	/// <param name="enabled">True to enable autonomous combat AI; false to disable.</param>
	void EnableUnitAutoCombat(UnitId unit, bool enabled);

	/// <summary>
	/// Writes string content to a file in the whitelisted, map-specific subfolder in AppData saved_data directory.
	/// The target location is sandboxed to user://saved_data/{mapName}/{fileName}.
	/// </summary>
	/// <param name="fileName">The name of the file to write to. Must be a safe, relative file name without directory traversal characters.</param>
	/// <param name="content">The string content to write to the file.</param>
	void WriteSavedData(string fileName, string content);

	/// <summary>
	/// Reads string content from a file in the whitelisted, map-specific subfolder in AppData saved_data directory.
	/// The target location is sandboxed to user://saved_data/{sourceMapName}/{fileName}.
	/// </summary>
	/// <param name="fileName">The name of the file to read from. Must be a safe, relative file name without directory traversal characters.</param>
	/// <param name="sourceMapName">The name of the source map to read saved data from, or empty string to use the active map.</param>
	/// <returns>The string content read from the file, or an empty string if the file does not exist or has invalid path characters.</returns>
	string ReadSavedData(string fileName, string sourceMapName = "");

	/// <summary>
	/// Triggered when a unit buys or receives an item from a shop or altar.
	/// </summary>
	event Action<UnitId, string>? OnItemSold;

	/// <summary>
	/// Triggered when a structure finishes construction.
	/// </summary>
	event Action<UnitId>? OnConstructionFinished;

	/// <summary>
	/// Triggered when a unit is issued an order.
	/// </summary>
	event Action<UnitId, string, Vector3>? OnUnitOrdered;

	/// <summary>
	/// Gets the research level of a technology upgrade for the specified player slot.
	/// </summary>
	int GetPlayerTechLevel(int playerIndex, string techId);

	/// <summary>
	/// Sets the research level of a technology upgrade for the specified player slot.
	/// </summary>
	void SetPlayerTechLevel(int playerIndex, string techId, int level);

	/// <summary>
	/// Adds to the research level of a technology upgrade for the specified player slot.
	/// </summary>
	void AddPlayerTechLevel(int playerIndex, string techId, int delta = 1);

	/// <summary>
	/// Registers an ability with display metadata and execution behavior.
	/// </summary>
	/// <param name="abilityId">Unique identifier of the ability.</param>
	/// <param name="displayName">User-facing name of the ability.</param>
	/// <param name="tooltip">Detailed description displayed on hover.</param>
	/// <param name="iconPath">Resource path to the ability icon texture.</param>
	/// <param name="isInstant">True if the ability executes immediately without ground targeting.</param>
	void RegisterAbility(string abilityId, string displayName, string tooltip, string iconPath = "", bool isInstant = false);

	/// <summary>
	/// Sets whether an ability executes immediately without requiring ground targeting.
	/// </summary>
	/// <param name="abilityId">Unique identifier of the ability.</param>
	/// <param name="isInstant">True if instant cast, false for ground targeting.</param>
	void SetAbilityInstant(string abilityId, bool isInstant);

	/// <summary>
	/// Sets the visual icon texture path for an ability.
	/// </summary>
	/// <param name="abilityId">Unique identifier of the ability.</param>
	/// <param name="iconPath">Resource path to the icon texture.</param>
	void SetAbilityIcon(string abilityId, string iconPath);

	/// <summary>
	/// Sets the display tooltip for an ability.
	/// </summary>
	void SetAbilityTooltip(string abilityId, string tooltip);

	/// <summary>
	/// Sets the display tooltip for an item.
	/// </summary>
	void SetItemTooltip(string itemId, string tooltip);

	/// <summary>
	/// Configures the grid position (X, Y) of an ability in the command card interface.
	/// </summary>
	void SetAbilityGridPosition(string abilityId, int x, int y);

	/// <summary>
	/// Configures the hotkey binding string (e.g., "W", "E", "R") for an ability on the command card.
	/// </summary>
	/// <param name="abilityId">Unique identifier of the ability.</param>
	/// <param name="hotkey">The key string to trigger the ability.</param>
	void SetAbilityHotkey(string abilityId, string hotkey);

	/// <summary>
	/// Sets whether an ability on a unit is disabled or hidden in the UI.
	/// </summary>
	void SetAbilityState(UnitId unit, string abilityId, bool disabled, bool hidden);

	/// <summary>
	/// Sets the mana cost of a unit's ability.
	/// </summary>
	void SetAbilityManaCost(UnitId unit, string abilityId, float manaCost);

	/// <summary>
	/// Plays a named visual animation on the unit's 3D model and equips any configured hand attachments for that animation.
	/// </summary>
	/// <param name="unit">The unit to animate.</param>
	/// <param name="animationName">The animation name (e.g., "Idle", "Labor", "Attack").</param>
	void SetUnitAnimation(UnitId unit, string animationName);

	/// <summary>
	/// Attaches or removes a 3D model object on the specified hand bone of the unit's rigged skeleton.
	/// </summary>
	/// <param name="unit">The target unit.</param>
	/// <param name="hand">The target hand ("RightHand" or "LeftHand").</param>
	/// <param name="attachmentId">The attachment identifier or model filename, or null/empty to clear the hand attachment.</param>
	void SetUnitHandAttachment(UnitId unit, string hand, string? attachmentId);

	/// <summary>
	/// Orders the unit to move to a world position without attacking along the way.
	/// Generic alias for <see cref="IssueMoveOrder(UnitId, Vector3)"/>.
	/// </summary>
	void MoveUnit(UnitId unit, Vector3 destination) => IssueMoveOrder(unit, destination);

	/// <summary>
	/// Returns living units within <paramref name="radius"/> of <paramref name="center"/>.
	/// Generic alias for <see cref="GetUnitsInRadius(Vector3, float)"/>.
	/// </summary>
	ReadOnlySpan<UnitId> GetUnitsInRange(Vector3 center, float radius) =>
		GetUnitsInRadius(center, radius);

	/// <summary>
	/// Subtracts <paramref name="damage"/> from the target's health. Kills the target at 0 HP.
	/// Does not apply armor. Safe to call from any map script.
	/// </summary>
	void DealDamage(UnitId attacker, UnitId target, float damage)
	{
		if (!target.IsValid || target.IsDead || damage <= 0f)
			return;
		_ = attacker;
		float next = target.Health - damage;
		if (next <= 0f)
		{
			target.Health = 0f;
			KillUnit(target);
			return;
		}

		target.Health = next;
	}

	/// <summary>
	/// Spawns a replacement unit of the same type at <paramref name="position"/> for the given player.
	/// Use this when the previous instance may already be destroyed (typical hero respawn).
	/// </summary>
	/// <param name="unitTypeId">The type identifier of the unit to spawn.</param>
	/// <param name="position">The spawn coordinates in 3D world space.</param>
	/// <param name="playerIndex">The zero-based player index to own the revived unit.</param>
	/// <returns>The revived unit instance.</returns>
	UnitId ReviveUnit(string unitTypeId, Vector3 position, int playerIndex) =>
		SpawnUnitForPlayer(unitTypeId, new Vector2(position.X, position.Z), playerIndex);

	/// <summary>
	/// Destroys <paramref name="unit"/> and spawns a new copy of its type at <paramref name="position"/> with the same owner player index.
	/// </summary>
	/// <param name="unit">The existing unit to replace.</param>
	/// <param name="position">The spawn coordinates in 3D world space.</param>
	/// <returns>The revived unit instance.</returns>
	UnitId ReviveUnit(UnitId unit, Vector3 position)
	{
		string typeId = unit.TemplateID;
		int playerIndex = unit.Player;
		DestroyUnit(unit);
		return SpawnUnitForPlayer(typeId, new Vector2(position.X, position.Z), playerIndex);
	}

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
	/// Configures the AI decision profile for the specified player slot using a serialized JSON string.
	/// </summary>
	/// <param name="playerIndex">The zero-based player slot index.</param>
	/// <param name="profileJson">The serialized JSON string representing the bot profile weights and personality.</param>
	void SetPlayerBotProfile(int playerIndex, string profileJson);

	/// <summary>
	/// Retrieves the serialized JSON string of the active AI bot decision profile for the specified player slot.
	/// </summary>
	/// <param name="playerIndex">The zero-based player slot index.</param>
	/// <returns>The serialized JSON string representing the active bot profile, or an empty string if not configured.</returns>
	string GetPlayerBotProfile(int playerIndex);

	/// <summary>
	/// Sets the AI sub-genre provider for the specified player slot (e.g., "rts", "tower_defense", "auto_battler", "custom").
	/// </summary>
	/// <param name="playerIndex">The zero-based player slot index.</param>
	/// <param name="genreName">The identifier of the AI genre provider.</param>
	/// <param name="configJson">Optional JSON configuration parameters for the genre provider.</param>
	void SetPlayerBotGenre(int playerIndex, string genreName, string? configJson = null);

	/// <summary>
	/// Retrieves the identifier of the active AI sub-genre provider for the specified player slot.
	/// </summary>
	/// <param name="playerIndex">The zero-based player slot index.</param>
	/// <returns>The genre identifier string (e.g., "rts", "tower_defense", "auto_battler").</returns>
	string GetPlayerBotGenre(int playerIndex);

	/// <summary>
	/// Registers a map-defined custom candidate action for evaluation by the bot utility decision loop.
	/// </summary>
	/// <param name="playerIndex">The zero-based player slot index.</param>
	/// <param name="actionId">The unique action identifier.</param>
	/// <param name="intent">The action intent category (e.g., "Build", "Cast", "Transact", "Interact").</param>
	/// <param name="featureVector">The normalized feature vector scored against the AI weights.</param>
	/// <param name="position">The target world coordinate for the action.</param>
	/// <param name="payload">Optional metadata or argument string passed when the action executes.</param>
	void RegisterCustomBotDecision(int playerIndex, string actionId, string intent, float[] featureVector, Vector3 position, string payload = "")
	{
	}

	/// <summary>
	/// Clears all registered custom bot candidate actions for the specified player slot.
	/// </summary>
	/// <param name="playerIndex">The zero-based player slot index.</param>
	void ClearCustomBotDecisions(int playerIndex);

	/// <summary>
	/// Triggered when an autonomous AI bot executes a custom map-defined action.
	/// Parameters are: playerIndex, actionId, intent, targetPosition, payload.
	/// </summary>
	event Action<int, string, string, Vector3, string>? OnBotCustomActionExecuted;

	/// <summary>
	/// Executes an automated self-play genetic training simulation on CPU and returns the resulting serialized bot profile JSON.
	/// </summary>
	/// <param name="mapName">The identifier or name of the map being trained.</param>
	/// <param name="generations">The number of evolutionary generations to execute.</param>
	/// <param name="populationSize">The candidate population size per generation.</param>
	/// <param name="matchesPerEvaluation">The number of evaluation matches each candidate plays per generation.</param>
	/// <param name="genre">The AI sub-genre provider identifier (e.g. "rts", "tower_defense", "auto_battler").</param>
	/// <returns>The serialized JSON string of the best evolved bot profile.</returns>
	string TrainBotProfile(string mapName, int generations = 5, int populationSize = 8, int matchesPerEvaluation = 4, string genre = "rts");

	/// <summary>
	/// Sets the active environment lighting, fog, and atmospheric preset immediately without transition interpolation.
	/// </summary>
	/// <param name="presetId">The unique identifier of the environment preset configured in map metadata.</param>
	void SetEnvironmentPreset(string presetId);

	/// <summary>
	/// Smoothly transitions active environment lighting, fog, SSAO, and post-processing visual settings to the target preset over the specified duration.
	/// </summary>
	/// <param name="presetId">The unique identifier of the target environment preset configured in map metadata.</param>
	/// <param name="durationSeconds">The transition duration in seconds. If 0 or negative, the preset applies immediately.</param>
	void TransitionEnvironmentPreset(string presetId, float durationSeconds);

	/// <summary>
	/// Retrieves the unique identifier of the currently active environment preset.
	/// </summary>
	/// <returns>The preset identifier string.</returns>
	string GetCurrentEnvironmentPreset();

	/// <summary>
	/// Sets the active weather effect condition (e.g., "clear", "rain", "fog", "snow").
	/// </summary>
	/// <param name="weatherType">The name of the weather condition.</param>
	void SetWeather(string weatherType);

	/// <summary>
	/// Retrieves the name of the currently active weather condition.
	/// </summary>
	/// <returns>The weather name string.</returns>
	string GetWeather();

	/// <summary>
	/// Disables the vision shroud and fog of war, making the entire map and all entities fully visible.
	/// </summary>
	void DisableShroud();

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

	/// <summary>
	/// Retrieves a <see cref="PlayerId"/> identifier for the specified zero-based player slot index,
	/// providing fluent zero-allocation access to player state and actions.
	/// </summary>
	/// <param name="playerIndex">Zero-based player slot index.</param>
	/// <returns>A <see cref="PlayerId"/> bound to the specified player slot.</returns>
	PlayerId GetPlayer(int playerIndex) => new(playerIndex);

	/// <summary>
	/// Retrieves all active player slot identifiers in the current session.
	/// </summary>
	/// <param name="exclusiveUpperBound">Exclusive upper bound on the slot index. Pass a negative value to scan every slot in <see cref="PlayerCount"/>.</param>
	/// <returns>A read-only span of active <see cref="PlayerId"/> identifiers in ascending slot order.</returns>
	ReadOnlySpan<PlayerId> GetActivePlayers(int exclusiveUpperBound = -1);

	/// <summary>
	/// Returns the zero-based slot indices of all active players.
	/// </summary>
	/// <param name="exclusiveUpperBound">Exclusive upper bound on the slot index. Pass a negative value to scan every slot in <see cref="PlayerCount"/>.</param>
	/// <returns>An enumerable sequence of active player slot indices.</returns>
	IEnumerable<int> GetActivePlayerIndices(int exclusiveUpperBound = -1)
	{
		var players = GetActivePlayers(exclusiveUpperBound);
		var result = new List<int>(players.Length);
		for (int i = 0; i < players.Length; i++)
		{
			result.Add(players[i].Value);
		}
		return result;
	}

	/// <summary>
	/// Sets the stone resource for the specified player slot.
	/// </summary>
	/// <param name="playerIndex">Zero-based player slot index.</param>
	/// <param name="amount">The new stone amount.</param>
	void SetPlayerStone(int playerIndex, float amount);

	/// <summary>
	/// Adds the specified amount to the stone resource for the given player slot.
	/// Negative values subtract stone.
	/// </summary>
	/// <param name="playerIndex">Zero-based player slot index.</param>
	/// <param name="delta">The amount to add (may be negative).</param>
	void AdjustPlayerStone(int playerIndex, float delta);

	/// <summary>
	/// Smoothly pans the specified player's camera to the given 3D world position.
	/// </summary>
	/// <param name="playerIndex">Zero-based player slot index.</param>
	/// <param name="position">The target position in 3D world space.</param>
	/// <param name="duration">The pan duration in seconds.</param>
	void PanPlayerCameraTo(int playerIndex, Vector3 position, float duration = 0f);

	/// <summary>
	/// Shakes the specified player's camera to simulate an explosion or impact.
	/// </summary>
	/// <param name="playerIndex">Zero-based player slot index.</param>
	/// <param name="intensity">The shake strength multiplier.</param>
	/// <param name="duration">The time in seconds the shake lasts.</param>
	void ShakePlayerCamera(int playerIndex, float intensity, float duration);

	/// <summary>
	/// Restricts the specified player's camera movement limits in world space coordinates.
	/// Clamps the camera position immediately if currently out of bounds.
	/// </summary>
	/// <param name="playerIndex">Zero-based player slot index.</param>
	/// <param name="minX">The minimum X world boundary.</param>
	/// <param name="minZ">The minimum Z world boundary.</param>
	/// <param name="maxX">The maximum X world boundary.</param>
	/// <param name="maxZ">The maximum Z world boundary.</param>
	void SetPlayerCameraBounds(int playerIndex, float minX, float minZ, float maxX, float maxZ);

	/// <summary>
	/// Restricts the specified player's camera movement limits to the specified named coordinate marker.
	/// </summary>
	/// <param name="playerIndex">Zero-based player slot index.</param>
	/// <param name="coordinateName">The name of the authored coordinate marker defining the camera bounds.</param>
	void SetPlayerCameraBounds(int playerIndex, string coordinateName)
	{
		if (TryGetCoordinate(coordinateName, out var coord))
		{
			SetPlayerCameraBounds(playerIndex, coord);
		}
	}

	/// <summary>
	/// Restricts the specified player's camera movement limits to the specified coordinate bounding region.
	/// </summary>
	/// <param name="playerIndex">Zero-based player slot index.</param>
	/// <param name="coordinate">The coordinate bounding region defining the camera bounds.</param>
	void SetPlayerCameraBounds(int playerIndex, Coordinate coordinate)
	{
		SetPlayerCameraBounds(
			playerIndex,
			MathF.Min(coordinate.Min.X, coordinate.Max.X),
			MathF.Min(coordinate.Min.Z, coordinate.Max.Z),
			MathF.Max(coordinate.Min.X, coordinate.Max.X),
			MathF.Max(coordinate.Min.Z, coordinate.Max.Z));
	}

	/// <summary>
	/// Restricts the specified player's camera movement limits in world space coordinates (alias for <see cref="SetPlayerCameraBounds(int, float, float, float, float)"/>).
	/// </summary>
	/// <param name="playerIndex">Zero-based player slot index.</param>
	/// <param name="minX">The minimum X world boundary.</param>
	/// <param name="minZ">The minimum Z world boundary.</param>
	/// <param name="maxX">The maximum X world boundary.</param>
	/// <param name="maxZ">The maximum Z world boundary.</param>
	void SetMapCameraBoundaries(int playerIndex, float minX, float minZ, float maxX, float maxZ) =>
		SetPlayerCameraBounds(playerIndex, minX, minZ, maxX, maxZ);

	/// <summary>
	/// Resets the specified player's camera movement boundaries back to the map default boundaries defined in metadata.
	/// </summary>
	/// <param name="playerIndex">Zero-based player slot index.</param>
	void ResetPlayerCameraBounds(int playerIndex);

	/// <summary>
	/// Resets the specified player's camera movement boundaries back to the map default boundaries (alias for <see cref="ResetPlayerCameraBounds(int)"/>).
	/// </summary>
	/// <param name="playerIndex">Zero-based player slot index.</param>
	void ResetMapCameraBoundaries(int playerIndex) => ResetPlayerCameraBounds(playerIndex);

	/// <summary>
	/// Sets whether a specific built-in HUD element is visible for the designated player.
	/// Persists across HUD refreshes and reconnects.
	/// </summary>
	/// <param name="playerIndex">Zero-based player slot index.</param>
	/// <param name="element">The HUD element to show or hide.</param>
	/// <param name="visible">True to make the element visible; false to hide it.</param>
	void SetPlayerHudElementVisible(int playerIndex, HudElement element, bool visible);

	/// <summary>
	/// Toggles whether a specific built-in HUD element is visible for the designated player (alias for <see cref="SetPlayerHudElementVisible(int, HudElement, bool)"/>).
	/// </summary>
	/// <param name="playerIndex">Zero-based player slot index.</param>
	/// <param name="element">The HUD element to show or hide.</param>
	/// <param name="visible">True to make the element visible; false to hide it.</param>
	void ToggleUserInterfaceElement(int playerIndex, HudElement element, bool visible) =>
		SetPlayerHudElementVisible(playerIndex, element, visible);

	/// <summary>
	/// Selects the specified unit on the given player's client, clearing their previous selection.
	/// </summary>
	/// <param name="playerIndex">Zero-based player slot index.</param>
	/// <param name="unit">The unit to select.</param>
	void SelectPlayerUnit(int playerIndex, UnitId unit);

	/// <summary>
	/// Clears the unit selection on the specified player's client.
	/// </summary>
	/// <param name="playerIndex">Zero-based player slot index.</param>
	void ClearPlayerSelection(int playerIndex);

	/// <summary>
	/// Retrieves all units currently selected by the specified player.
	/// </summary>
	/// <param name="playerIndex">Zero-based player slot index.</param>
	/// <returns>A read-only span of units currently selected by that player.</returns>
	ReadOnlySpan<UnitId> GetPlayerSelectedUnits(int playerIndex);

	/// <summary>
	/// Displays floating feedback text on the specified player's screen.
	/// </summary>
	/// <param name="playerIndex">Zero-based player slot index.</param>
	/// <param name="text">The text message to display.</param>
	/// <param name="color">The color of the text in RGB format.</param>
	void ShowPlayerFeedbackText(int playerIndex, string text, Vector3 color);

	/// <summary>
	/// Spawns a minimap ping on the specified player's client at the given 3D world position.
	/// </summary>
	/// <param name="playerIndex">Zero-based player slot index.</param>
	/// <param name="position">The target position in 3D world space.</param>
	void PingPlayerMinimap(int playerIndex, Vector3 position);

	/// <summary>
	/// Plays the standard warning/alert audio sound on the specified player's client.
	/// </summary>
	/// <param name="playerIndex">Zero-based player slot index.</param>
	void PlayPlayerWarningSound(int playerIndex);

	/// <summary>
	/// Plays the standard button/command click audio sound on the specified player's client.
	/// </summary>
	/// <param name="playerIndex">Zero-based player slot index.</param>
	void PlayPlayerClickSound(int playerIndex);

	/// <summary>
	/// Enters building placement mode on the specified player's client for the given structure type.
	/// </summary>
	/// <param name="playerIndex">Zero-based player slot index.</param>
	/// <param name="unitTypeId">The unit type ID of the building to place.</param>
	void StartPlayerBuildingPlacement(int playerIndex, string unitTypeId);

	/// <summary>
	/// Returns a uniformly random element of the given list.
	/// </summary>
	/// <typeparam name="T">The element type.</typeparam>
	/// <param name="items">The list to pick from.</param>
	/// <returns>A random element, or the default value of <typeparamref name="T"/> if the list is empty.</returns>
	T? PickRandom<T>(IReadOnlyList<T> items)
	{
		if (items.Count == 0)
			return default;
		return items[RandomInt(0, items.Count - 1)];
	}

	/// <summary>
	/// Shuffles the given list in place using a Fisher-Yates shuffle driven by <see cref="RandomInt"/>.
	/// </summary>
	/// <typeparam name="T">The element type.</typeparam>
	/// <param name="items">The list to shuffle.</param>
	void Shuffle<T>(IList<T> items)
	{
		for (int index = items.Count - 1; index > 0; index--)
		{
			int swapIndex = RandomInt(0, index);
			(items[index], items[swapIndex]) = (items[swapIndex], items[index]);
		}
	}

	/// <summary>
	/// Returns a random alive unit within the radius that satisfies the optional filter.
	/// </summary>
	/// <param name="center">The center point in 3D world space.</param>
	/// <param name="radius">The search radius.</param>
	/// <param name="filter">Optional predicate that returns true for eligible units.</param>
	/// <returns>A random matching unit, or UnitId.Invalid if none qualify.</returns>
	UnitId GetRandomUnitInRadius(Vector3 center, float radius, Func<UnitId, bool>? filter = null)
	{
		var units = GetUnitsInRadius(center, radius);
		int matchCount = 0;
		for (int i = 0; i < units.Length; i++)
		{
			var u = units[i];
			if (u.IsDead)
				continue;
			if (filter != null && !filter(u))
				continue;
			matchCount++;
		}

		if (matchCount == 0)
			return UnitId.Invalid;

		int pickIndex = RandomInt(0, matchCount - 1);
		int current = 0;
		for (int i = 0; i < units.Length; i++)
		{
			var u = units[i];
			if (u.IsDead)
				continue;
			if (filter != null && !filter(u))
				continue;
			if (current == pickIndex)
				return u;
			current++;
		}

		return UnitId.Invalid;
	}

	/// <summary>
	/// Restores health to the unit, clamped to its maximum health. Dead units are ignored.
	/// </summary>
	/// <param name="unit">The unit to heal.</param>
	/// <param name="amount">The amount of health to restore.</param>
	void HealUnit(UnitId unit, float amount)
	{
		if (!unit.IsValid || unit.IsDead || amount <= 0f)
			return;
		unit.Health = MathF.Min(unit.MaxHealth, unit.Health + amount);
	}

	/// <summary>
	/// Exchanges the world positions of two units.
	/// </summary>
	/// <param name="first">The first unit.</param>
	/// <param name="second">The second unit.</param>
	void SwapUnitPositions(UnitId first, UnitId second)
	{
		Vector3 firstPosition = first.Position;
		Vector3 secondPosition = second.Position;
		first.Teleport(secondPosition);
		second.Teleport(firstPosition);
	}

	/// <summary>
	/// Returns a point on a ring around a center, useful for distributing player bases evenly.
	/// </summary>
	/// <param name="center">The ring center.</param>
	/// <param name="radius">The ring radius.</param>
	/// <param name="index">The zero-based slot index around the ring.</param>
	/// <param name="count">The total number of slots on the ring.</param>
	/// <returns>The position of the slot on the X/Z plane, preserving the center's Y.</returns>
	Vector3 GetRingPosition(Vector3 center, float radius, int index, int count)
	{
		float angle = MathF.Tau * index / Math.Max(1, count);
		return new Vector3(center.X + MathF.Cos(angle) * radius, center.Y, center.Z + MathF.Sin(angle) * radius);
	}

	/// <summary>
	/// Returns the standard hex color code string (RRGGBB) corresponding to the specified player slot.
	/// </summary>
	/// <param name="playerIndex">Zero-based player slot index.</param>
	/// <returns>A 6-character hex color string.</returns>
	string GetPlayerColorHex(int playerIndex);

	/// <summary>
	/// Returns the player name formatted with rich text / BBCode markup matching their assigned player slot color.
	/// </summary>
	/// <param name="playerIndex">Zero-based player slot index.</param>
	/// <returns>The color-formatted player name string.</returns>
	string FormatColoredPlayerName(int playerIndex)
	{
		string name = GetPlayerName(playerIndex);
		string color = GetPlayerColorHex(playerIndex);
		return $"[color=#{color}]{name}[/color]";
	}
	/// <summary>
	/// Spawns a pickup loot item at the specified 3D world space coordinates.
	/// </summary>
	/// <param name="itemTypeId">The identifier of the item type from the item registry.</param>
	/// <param name="position">The coordinates in 3D world space where the item will be spawned.</param>
	/// <param name="charges">The initial charge count or stacked quantity for the item.</param>
	/// <param name="lifetime">The duration in seconds before the item automatically despawns, or 0 if indefinite.</param>
	/// <returns>The spawned loot item identifier.</returns>
	LootItemId SpawnLootItem(string itemTypeId, Vector3 position, int charges = 1, float lifetime = 0f);

	/// <summary>
	/// Spawns a pickup loot item at the specified 3D world space coordinates.
	/// </summary>
	/// <param name="itemTypeId">The identifier of the item type from the item registry.</param>
	/// <param name="position">The coordinates in 3D world space where the item will be spawned.</param>
	/// <param name="charges">The initial charge count or stacked quantity for the item.</param>
	/// <param name="lifetime">The duration in seconds before the item automatically despawns, or 0 if indefinite.</param>
	/// <returns>The spawned loot item identifier.</returns>
	LootItemId SpawnLootItemAtLocation(string itemTypeId, Vector3 position, int charges = 1, float lifetime = 0f) =>
		SpawnLootItem(itemTypeId, position, charges, lifetime);

	/// <summary>
	/// Spawns a pickup loot item at the specified named coordinate marker.
	/// </summary>
	/// <param name="itemTypeId">The identifier of the item type from the item registry.</param>
	/// <param name="coordinateName">The name of the authored coordinate marker where the item will be spawned.</param>
	/// <param name="charges">The initial charge count or stacked quantity for the item.</param>
	/// <param name="lifetime">The duration in seconds before the item automatically despawns, or 0 if indefinite.</param>
	/// <returns>The spawned loot item identifier.</returns>
	LootItemId SpawnLootItem(string itemTypeId, string coordinateName, int charges = 1, float lifetime = 0f) =>
		TryGetCoordinate(coordinateName, out var coord)
			? SpawnLootItem(itemTypeId, coord.Center, charges, lifetime)
			: LootItemId.Invalid;

	/// <summary>
	/// Spawns a pickup loot item at the specified coordinate.
	/// </summary>
	/// <param name="itemTypeId">The identifier of the item type from the item registry.</param>
	/// <param name="coordinate">The coordinate marker where the item will be spawned.</param>
	/// <param name="charges">The initial charge count or stacked quantity for the item.</param>
	/// <param name="lifetime">The duration in seconds before the item automatically despawns, or 0 if indefinite.</param>
	/// <returns>The spawned loot item identifier.</returns>
	LootItemId SpawnLootItem(string itemTypeId, Coordinate coordinate, int charges = 1, float lifetime = 0f) =>
		SpawnLootItem(itemTypeId, coordinate.Center, charges, lifetime);

	/// <summary>
	/// Retrieves an active ground loot item by its unique runtime identifier.
	/// </summary>
	/// <param name="uniqueId">The unique ID of the loot item entity.</param>
	/// <returns>The loot item identifier.</returns>
	LootItemId GetLootItemById(int uniqueId);

	/// <summary>
	/// Retrieves all active ground loot items in the game world.
	/// </summary>
	/// <returns>A read-only span of all active ground loot item identifiers.</returns>
	ReadOnlySpan<LootItemId> GetAllLootItems();

	/// <summary>
	/// Retrieves all active ground loot items within the specified radial distance of a 3D position.
	/// </summary>
	/// <param name="center">The center point in 3D world space coordinates.</param>
	/// <param name="radius">The radial distance check.</param>
	/// <returns>A read-only span of ground loot item identifiers within the radius.</returns>
	ReadOnlySpan<LootItemId> GetLootItemsInRadius(Vector3 center, float radius);

	/// <summary>
	/// Removes and despawns a ground loot item from the game world by its unique identifier.
	/// </summary>
	/// <param name="lootItemId">The unique identifier of the loot item to remove.</param>
	/// <returns>True if the loot item was found and removed; otherwise, false.</returns>
	bool RemoveLootItem(int lootItemId);

	/// <summary>
	/// Despawns and removes a ground loot item from the game world by its unique identifier.
	/// </summary>
	/// <param name="lootItemId">The unique identifier of the loot item to despawn.</param>
	/// <returns>True if the loot item was found and despawned; otherwise, false.</returns>
	bool DespawnLootItem(int lootItemId) => RemoveLootItem(lootItemId);

	/// <summary>
	/// Attempts to collect a ground loot item using the specified unit.
	/// </summary>
	/// <param name="lootItemId">The unique identifier of the ground loot item to collect.</param>
	/// <param name="unit">The collecting unit.</param>
	/// <returns>True if the unit successfully collected the loot item into inventory; otherwise, false.</returns>
	bool CollectLootItem(int lootItemId, UnitId unit);

	/// <summary>
	/// Attempts to collect a ground loot item using the most suitable eligible unit owned by the specified player.
	/// </summary>
	/// <param name="lootItemId">The unique identifier of the ground loot item to collect.</param>
	/// <param name="playerIndex">The zero-based player slot index whose unit should collect the item.</param>
	/// <returns>True if an eligible unit owned by the player successfully collected the item; otherwise, false.</returns>
	bool CollectLootItemByPlayer(int lootItemId, int playerIndex);

	/// <summary>
	/// Sets the current health points of the specified unit.
	/// </summary>
	/// <param name="unit">The unit identifier.</param>
	/// <param name="value">The new health value.</param>
	void SetUnitHealth(UnitId unit, float value);

	/// <summary>
	/// Sets the maximum health points capacity of the specified unit.
	/// </summary>
	/// <param name="unit">The unit identifier.</param>
	/// <param name="value">The new maximum health capacity.</param>
	void SetUnitMaxHealth(UnitId unit, float value);

	/// <summary>
	/// Sets the current mana points of the specified unit.
	/// </summary>
	/// <param name="unit">The unit identifier.</param>
	/// <param name="value">The new mana value.</param>
	void SetUnitMana(UnitId unit, float value);

	/// <summary>
	/// Sets the maximum mana capacity of the specified unit.
	/// </summary>
	/// <param name="unit">The unit identifier.</param>
	/// <param name="value">The new maximum mana capacity.</param>
	void SetUnitMaxMana(UnitId unit, float value);

	/// <summary>
	/// Sets the mana regeneration rate of the specified unit per second.
	/// </summary>
	/// <param name="unit">The unit identifier.</param>
	/// <param name="value">The new mana regeneration rate.</param>
	void SetUnitManaRegen(UnitId unit, float value);

	/// <summary>
	/// Sets the base attack damage of the specified unit.
	/// </summary>
	/// <param name="unit">The unit identifier.</param>
	/// <param name="value">The new attack damage.</param>
	void SetUnitDamage(UnitId unit, float value);

	/// <summary>
	/// Sets the attack range of the specified unit.
	/// </summary>
	/// <param name="unit">The unit identifier.</param>
	/// <param name="value">The new attack range.</param>
	void SetUnitRange(UnitId unit, float value);

	/// <summary>
	/// Sets the armor defense rating of the specified unit.
	/// </summary>
	/// <param name="unit">The unit identifier.</param>
	/// <param name="value">The new armor rating.</param>
	void SetUnitArmor(UnitId unit, float value);

	/// <summary>
	/// Sets the movement speed of the specified unit.
	/// </summary>
	/// <param name="unit">The unit identifier.</param>
	/// <param name="value">The new movement speed.</param>
	void SetUnitSpeed(UnitId unit, float value);

	/// <summary>
	/// Sets the attack speed multiplier of the specified unit.
	/// </summary>
	/// <param name="unit">The unit identifier.</param>
	/// <param name="value">The new attack speed multiplier.</param>
	void SetUnitAttackSpeed(UnitId unit, float value);

	/// <summary>
	/// Retrieves the display name of the specified unit.
	/// </summary>
	/// <param name="unit">The unit identifier.</param>
	/// <returns>The unit display name.</returns>
	string GetUnitInstanceName(UnitId unit);

	/// <summary>
	/// Sets the display name of the specified unit.
	/// </summary>
	/// <param name="unit">The unit identifier.</param>
	/// <param name="value">The new display name.</param>
	void SetUnitName(UnitId unit, string value);

	/// <summary>
	/// Retrieves the canonical archetype template identifier of the specified unit.
	/// </summary>
	/// <param name="unit">The unit identifier.</param>
	/// <returns>The archetype template identifier string.</returns>
	string GetUnitTemplateId(UnitId unit);

	/// <summary>
	/// Sets the accumulated experience points of the specified hero unit.
	/// </summary>
	/// <param name="unit">The unit identifier.</param>
	/// <param name="value">The new experience points value.</param>
	void SetUnitExperience(UnitId unit, float value);

	/// <summary>
	/// Sets the visual scaling multiplier of the specified unit model.
	/// </summary>
	/// <param name="unit">The unit identifier.</param>
	/// <param name="value">The new visual scale factor.</param>
	void SetUnitScale(UnitId unit, float value);

	/// <summary>
	/// Sets whether the specified unit is invulnerable to all damage.
	/// </summary>
	/// <param name="unit">The unit identifier.</param>
	/// <param name="value">True for invulnerability; otherwise, false.</param>
	void SetUnitInvulnerable(UnitId unit, bool value);

	/// <summary>
	/// Issues an attack order targeting a specific enemy unit.
	/// </summary>
	/// <param name="unit">The attacking unit.</param>
	/// <param name="target">The target enemy unit.</param>
	void IssueAttackOrder(UnitId unit, UnitId target);

	/// <summary>
	/// Issues a resource gathering order targeting a harvestable resource node.
	/// </summary>
	/// <param name="unit">The harvesting unit.</param>
	/// <param name="resourceNode">The target resource node.</param>
	void IssueGatherOrder(UnitId unit, ResourceNodeId resourceNode);

	/// <summary>
	/// Orders the unit to stop its current action immediately.
	/// </summary>
	/// <param name="unit">The unit identifier.</param>
	void IssueStopOrder(UnitId unit);

	/// <summary>
	/// Orders the unit to hold its ground and defend itself without moving.
	/// </summary>
	/// <param name="unit">The unit identifier.</param>
	void IssueHoldPositionOrder(UnitId unit);

	/// <summary>
	/// Teleports the specified unit to the given coordinates in 3D world space.
	/// </summary>
	/// <param name="unit">The unit to teleport.</param>
	/// <param name="position">The destination coordinates in 3D world space.</param>
	void TeleportUnit(UnitId unit, Vector3 position);

	/// <summary>
	/// Stuns the specified unit for the given duration in seconds.
	/// </summary>
	/// <param name="unit">The unit identifier.</param>
	/// <param name="duration">The stun duration in seconds.</param>
	void StunUnit(UnitId unit, float duration);

	/// <summary>
	/// Silences the specified unit, preventing ability casting for the given duration in seconds.
	/// </summary>
	/// <param name="unit">The unit identifier.</param>
	/// <param name="duration">The silence duration in seconds.</param>
	void SilenceUnit(UnitId unit, float duration);

	/// <summary>
	/// Adds an item archetype to the specified unit's inventory.
	/// </summary>
	/// <param name="unit">The unit identifier.</param>
	/// <param name="itemId">The item definition identifier.</param>
	/// <returns>True if added successfully; otherwise, false.</returns>
	bool AddItemToUnit(UnitId unit, string itemId);

	/// <summary>
	/// Removes an item archetype from the specified unit's inventory.
	/// </summary>
	/// <param name="unit">The unit identifier.</param>
	/// <param name="itemId">The item definition identifier.</param>
	/// <returns>True if removed successfully; otherwise, false.</returns>
	bool RemoveItemFromUnit(UnitId unit, string itemId);

	/// <summary>
	/// Returns true if the specified unit's inventory carries the item.
	/// </summary>
	/// <param name="unit">The unit identifier.</param>
	/// <param name="itemId">The item definition identifier.</param>
	/// <returns>True if carried; otherwise, false.</returns>
	bool UnitHasItem(UnitId unit, string itemId);

	/// <summary>
	/// Retrieves the item archetype identifiers currently held in the unit's inventory.
	/// </summary>
	/// <param name="unit">The unit identifier.</param>
	/// <returns>A read-only collection of item archetype identifiers.</returns>
	IReadOnlyList<string> GetUnitItems(UnitId unit);

	/// <summary>
	/// Returns true if the specified unit currently has an active buff with the given identifier.
	/// </summary>
	/// <param name="unit">The unit identifier.</param>
	/// <param name="buffId">The buff identifier.</param>
	/// <returns>True if active; otherwise, false.</returns>
	bool UnitHasBuff(UnitId unit, string buffId);

	/// <summary>
	/// Stores custom string metadata associated with the specified unit.
	/// </summary>
	/// <param name="unit">The unit identifier.</param>
	/// <param name="key">The custom data key.</param>
	/// <param name="value">The custom data value.</param>
	void SetUnitCustomData(UnitId unit, string key, string value);

	/// <summary>
	/// Retrieves custom string metadata associated with the specified unit.
	/// </summary>
	/// <param name="unit">The unit identifier.</param>
	/// <param name="key">The custom data key.</param>
	/// <returns>The stored string value, or null if not found.</returns>
	string? GetUnitCustomData(UnitId unit, string key);

	/// <summary>
	/// Removes custom string metadata associated with the specified unit.
	/// </summary>
	/// <param name="unit">The unit identifier.</param>
	/// <param name="key">The custom data key.</param>
	/// <returns>True if found and removed; otherwise, false.</returns>
	bool RemoveUnitCustomData(UnitId unit, string key);

	/// <summary>
	/// Returns true if custom string metadata exists for the given key on the specified unit.
	/// </summary>
	/// <param name="unit">The unit identifier.</param>
	/// <param name="key">The custom data key.</param>
	/// <returns>True if data exists; otherwise, false.</returns>
	bool UnitHasCustomData(UnitId unit, string key);

	/// <summary>
	/// Retrieves the item type identifier of the specified ground loot item.
	/// </summary>
	/// <param name="item">The loot item identifier.</param>
	/// <returns>The item type identifier.</returns>
	string GetLootItemTypeId(LootItemId item);

	/// <summary>
	/// Retrieves the 3D world space position of the specified ground loot item.
	/// </summary>
	/// <param name="item">The loot item identifier.</param>
	/// <returns>The 3D coordinates of the loot item.</returns>
	Vector3 GetLootItemPosition(LootItemId item);

	/// <summary>
	/// Retrieves the remaining charge count for the specified ground loot item.
	/// </summary>
	/// <param name="item">The loot item identifier.</param>
	/// <returns>The charge count.</returns>
	int GetLootItemCharges(LootItemId item);

	/// <summary>
	/// Retrieves the configured lifespan duration of the specified ground loot item in seconds.
	/// </summary>
	/// <param name="item">The loot item identifier.</param>
	/// <returns>The lifespan duration in seconds.</returns>
	float GetLootItemLifetime(LootItemId item);

	/// <summary>
	/// Retrieves the pickup trigger radius of the specified ground loot item in world units.
	/// </summary>
	/// <param name="item">The loot item identifier.</param>
	/// <returns>The pickup trigger radius.</returns>
	float GetLootItemPickupRadius(LootItemId item);

	/// <summary>
	/// Returns true if the specified ground loot item automatically triggers pickup upon proximity.
	/// </summary>
	/// <param name="item">The loot item identifier.</param>
	/// <returns>True if auto-pickup is enabled; otherwise, false.</returns>
	bool IsLootItemAutoPickup(LootItemId item);

	/// <summary>
	/// Retrieves the owning player slot index restricted to collect the specified loot item, or -1 for unowned items.
	/// </summary>
	/// <param name="item">The loot item identifier.</param>
	/// <returns>The owning player slot index, or -1.</returns>
	int GetLootItemOwner(LootItemId item);

	/// <summary>
	/// Returns true if the specified ground loot item has already despawned.
	/// </summary>
	/// <param name="item">The loot item identifier.</param>
	/// <returns>True if despawned; otherwise, false.</returns>
	bool IsLootItemDespawned(LootItemId item);

	/// <summary>
	/// Retrieves the resource type identifier provided by the specified resource node (e.g. "gold", "wood", "stone").
	/// </summary>
	/// <param name="node">The resource node identifier.</param>
	/// <returns>The resource type string.</returns>
	string GetResourceNodeType(ResourceNodeId node);

	/// <summary>
	/// Retrieves the 3D world space position of the specified resource node.
	/// </summary>
	/// <param name="node">The resource node identifier.</param>
	/// <returns>The 3D coordinates of the resource node.</returns>
	Vector3 GetResourceNodePosition(ResourceNodeId node);

	/// <summary>
	/// Retrieves the remaining harvestable resource amount in the specified resource node.
	/// </summary>
	/// <param name="node">The resource node identifier.</param>
	/// <returns>The remaining resource amount.</returns>
	float GetResourceNodeAmount(ResourceNodeId node);

	/// <summary>
	/// Sets the remaining harvestable resource amount in the specified resource node.
	/// </summary>
	/// <param name="node">The resource node identifier.</param>
	/// <param name="value">The new resource amount.</param>
	void SetResourceNodeAmount(ResourceNodeId node, float value);

	/// <summary>
	/// Returns true if the specified resource node is fully depleted of resources.
	/// </summary>
	/// <param name="node">The resource node identifier.</param>
	/// <returns>True if depleted; otherwise, false.</returns>
	bool IsResourceNodeDepleted(ResourceNodeId node);
}