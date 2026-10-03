using System.Numerics;

namespace Realm.MapAPI;

/// <summary>
/// Defines the safe, public API exposed to map scripts to interact with the game engine.
/// </summary>
public interface IGameAPI
{
    /// <summary>
    /// Gets or sets the player's gold resource amount.
    /// </summary>
    float Gold { get; set; }

    /// <summary>
    /// Gets or sets the player's wood resource amount.
    /// </summary>
    float Wood { get; set; }

    /// <summary>
    /// Gets or sets the player's stone resource amount.
    /// </summary>
    float Stone { get; set; }

    /// <summary>
    /// Gets the elapsed game time in seconds.
    /// </summary>
    float GameElapsedTime { get; }

    /// <summary>
    /// Spawns a unit or building of the specified type at the given position.
    /// </summary>
    /// <param name="unitTypeId">The type identifier of the unit to spawn.</param>
    /// <param name="position">The spawn coordinates in 3D world space.</param>
    /// <param name="isEnemy">True if the unit should belong to the enemy, false if friendly.</param>
    /// <param name="bypassPopulation">True to spawn the unit without consuming the player's population limit, false to consume it normally.</param>
    /// <param name="executeSpawnShader">True to play the spawn shader effect if configured, false to skip.</param>
    /// <returns>A reference to the spawned unit.</returns>
    IUnit SpawnUnit(string unitTypeId, Vector3 position, bool isEnemy, bool bypassPopulation = false, bool executeSpawnShader = true);


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
    /// <returns>A collection of all active units.</returns>
    IEnumerable<IUnit> GetAllUnits();

    /// <summary>
    /// Retrieves all active units within the specified radius of a 3D position.
    /// </summary>
    /// <param name="center">The center point in 3D world space.</param>
    /// <param name="radius">The radius distance check.</param>
    /// <returns>A collection of units within the radius.</returns>
    IEnumerable<IUnit> GetUnitsInRadius(Vector3 center, float radius);

    /// <summary>
    /// Gets the number of resource nodes.
    /// </summary>
    int ResourceNodeCount { get; }

    /// <summary>
    /// Retrieves a resource node by its index.
    /// </summary>
    IResourceNode GetResourceNode(int index);

    /// <summary>
    /// Retrieves all active resource nodes in the game world.
    /// </summary>
    /// <returns>A collection of all active resource nodes.</returns>
    IEnumerable<IResourceNode> GetResourceNodes()
    {
        int count = ResourceNodeCount;
        for (int i = 0; i < count; i++)
            yield return GetResourceNode(i);
    }

    /// <summary>
    /// Displays floating feedback text on the player's screen.
    /// </summary>
    /// <param name="text">The text message to display.</param>
    /// <param name="color">The color of the text in RGB format.</param>
    void ShowFeedbackText(string text, Vector3 color);

    /// <summary>
    /// Plays the standard warning/alert audio sound.
    /// </summary>
    void PlayWarningSound();

    /// <summary>
    /// Plays the standard button/command click audio sound.
    /// </summary>
    void PlayClickSound();

    /// <summary>
    /// Triggers the game victory sequence.
    /// </summary>
    void TriggerVictory();

    /// <summary>
    /// Triggers the game defeat sequence.
    /// </summary>
    void TriggerDefeat();

    /// <summary>
    /// Gets the castle unit belonging to the specified faction.
    /// </summary>
    /// <param name="isEnemy">True to get the enemy castle, false for the player castle.</param>
    /// <returns>The castle unit, or null if it has been destroyed.</returns>
    IUnit? GetCastle(bool isEnemy);

    /// <summary>
    /// Spawns a temporary visual target indicator ring at the specified position with the given color.
    /// </summary>
    /// <param name="position">The coordinates in 3D world space.</param>
    /// <param name="color">The color of the indicator ring in RGB format.</param>
    void SpawnTargetIndicator(Vector3 position, Vector3 color);

    /// <summary>
    /// Gets or sets the player's maximum population capacity.
    /// </summary>
    int MaxPopulation { get; set; }

    /// <summary>
    /// Gets the player's current population.
    /// </summary>
    int CurrentPopulation { get; }

    /// <summary>
    /// Retrieves a unit by its unique runtime identifier.
    /// </summary>
    /// <param name="uniqueId">The unique ID of the unit to retrieve.</param>
    /// <returns>The unit wrapper if alive, or null.</returns>
    IUnit? GetUnitById(int uniqueId);

    /// <summary>
    /// Retrieves all units that are currently selected by the player.
    /// </summary>
    /// <returns>A collection of currently selected units.</returns>
    IEnumerable<IUnit> GetSelectedUnits();

    /// <summary>
    /// Spawns a minimap ping at the specified 3D position in world space.
    /// </summary>
    /// <param name="position">The target position in 3D world space.</param>
    void PingMinimap(Vector3 position);

    /// <summary>
    /// Enters building placement mode for the specified structure type.
    /// </summary>
    /// <param name="unitTypeId">The unit type ID of the building to place.</param>
    void StartBuildingPlacement(string unitTypeId);

    /// <summary>
    /// Generates a new map directory containing boilerplate files for a custom map.
    /// </summary>
    /// <param name="mapName">The name of the new map script class and directory.</param>
    /// <param name="targetDirectory">The parent directory path where the map directory should be created. If left null or empty, it defaults to the standard user maps folder.</param>
    void GenerateMapDirectory(string mapName, string? targetDirectory = null);

    /// <summary>
    /// Triggered when a unit is spawned in the game world.
    /// </summary>
    event Action<IUnit>? OnUnitCreated;

    /// <summary>
    /// Triggered when a unit dies. Passes the dying unit and the killer unit if available.
    /// </summary>
    event Action<IUnit, IUnit?>? OnUnitDied;

    /// <summary>
    /// Triggered when a unit takes damage. Passes the victim unit, the attacker unit, and the damage amount.
    /// </summary>
    event Action<IUnit, IUnit, float>? OnUnitDamaged;

    /// <summary>
    /// Triggered when a unit casts or channels a spell. Passes the caster unit, the spell ID, and the target position.
    /// </summary>
    event Action<IUnit?, string, Vector3>? OnSpellCast;

    /// <summary>
    /// Triggered when a player types a chat message. Passes the message and the first selected unit if any.
    /// </summary>
    event Action<string, IUnit?>? OnPlayerChatMessage;

    /// <summary>
    /// Triggered when the player selects a unit.
    /// </summary>
    event Action<IUnit>? OnUnitSelected;

    /// <summary>
    /// Triggered when a unit attacks another unit. Passes the attacker unit and the target unit.
    /// </summary>
    event Action<IUnit, IUnit>? OnUnitAttacked;

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
    /// Adds a buff to a unit.
    /// </summary>
    /// <param name="unit">The target unit.</param>
    /// <param name="buffId">The identifier of the buff.</param>
    /// <param name="duration">The duration of the buff in seconds.</param>
    void AddBuff(IUnit unit, string buffId, float duration);

    /// <summary>
    /// Registers a stat modifier for a buff type.
    /// </summary>
    /// <param name="buffId">The identifier of the buff.</param>
    /// <param name="statName">The name of the stat (e.g. "Armor", "Attack", "MovementSpeed").</param>
    /// <param name="isPercentage">True for percentage multiplier, false for flat bonus.</param>
    /// <param name="value">The modifier value.</param>
    void RegisterBuffModifier(string buffId, string statName, bool isPercentage, float value);

    /// <summary>
    /// Executes an ability generically by ID on a unit targeting a position.
    /// </summary>
    /// <param name="unit">The unit casting the ability.</param>
    /// <param name="abilityId">The identifier of the ability.</param>
    /// <param name="targetPosition">The target position in 3D world space.</param>
    void CastAbility(IUnit unit, string abilityId, Vector3 targetPosition);

    /// <summary>
    /// Gets the remaining cooldown of a unit's ability in seconds.
    /// </summary>
    /// <param name="unit">The unit to check.</param>
    /// <param name="abilityId">The identifier of the ability.</param>
    /// <returns>Remaining cooldown in seconds, or 0 if ready or not found.</returns>
    float GetAbilityCooldown(IUnit unit, string abilityId);

    /// <summary>
    /// Sets the cooldown of a unit's ability.
    /// </summary>
    /// <param name="unit">The unit.</param>
    /// <param name="abilityId">The identifier of the ability.</param>
    /// <param name="cooldown">The cooldown duration in seconds.</param>
    void SetAbilityCooldown(IUnit unit, string abilityId, float cooldown);

    /// <summary>
    /// Removes a buff from a unit.
    /// </summary>
    /// <param name="unit">The target unit.</param>
    /// <param name="buffId">The identifier of the buff to remove.</param>
    void RemoveBuff(IUnit unit, string buffId);

    /// <summary>
    /// Gets active modifiers on a unit.
    /// </summary>
    /// <param name="unit">The target unit.</param>
    /// <returns>A collection of modifier description strings.</returns>
    IEnumerable<string> GetModifiers(IUnit unit);

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
    /// <param name="playerIndex">Zero-based player index, or -1 for current local player.</param>
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
    /// Smoothly pans the game camera to the specified position.
    /// </summary>
    /// <param name="position">The target position in 3D world space.</param>
    /// <param name="duration">The pan time in seconds.</param>
    void PanCameraTo(Vector3 position, float duration);

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
    void KillUnit(IUnit unit, bool executeDespawnShader = true, bool playDeathAnimation = true);

    /// <summary>
    /// Instantly removes the specified unit from the game.
    /// </summary>
    /// <param name="unit">The unit to destroy.</param>
    /// <param name="executeDespawnShader">True to execute the despawn or death shader effect if configured, false to skip.</param>
    /// <param name="playDeathAnimation">True to play the death animation if available, false to skip.</param>
    void DestroyUnit(IUnit unit, bool executeDespawnShader = true, bool playDeathAnimation = true);



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
    /// Returns true if the specified player slot is occupied by an active human or AI participant.
    /// </summary>
    /// <param name="playerIndex">Zero-based player slot index.</param>
    bool IsPlayerActive(int playerIndex);

    /// <summary>
    /// Gets the gold resource for the specified player slot.
    /// </summary>
    /// <param name="playerIndex">Zero-based player slot index.</param>
    float GetPlayerGold(int playerIndex);

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
    /// <param name="position">The spawn coordinates in 3D world space.</param>
    /// <param name="playerIndex">Zero-based player slot index that will own the unit.</param>
    /// <param name="executeSpawnShader">True to play the spawn shader effect if configured, false to skip.</param>
    /// <returns>A reference to the spawned unit.</returns>
    IUnit SpawnUnitForPlayer(string unitTypeId, Vector3 position, int playerIndex, bool executeSpawnShader = true);

    /// <summary>
    /// Retrieves all alive units owned by the specified player slot.
    /// </summary>
    /// <param name="playerIndex">Zero-based player slot index.</param>
    /// <returns>A lazily-evaluated sequence of units belonging to that player.</returns>
    IEnumerable<IUnit> GetUnitsOwnedByPlayer(int playerIndex);

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
        OnTimerExpired += (h) => { if (h == handle) callback(); };
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
    /// Returns the team index that the specified player slot belongs to.
    /// </summary>
    /// <param name="playerIndex">Zero-based player slot index.</param>
    int GetPlayerTeam(int playerIndex);

    /// <summary>
    /// Configures whether two player slots treat each other as passive allies
    /// (shared vision, units do not attack each other).
    /// </summary>
    /// <param name="playerIndex">Zero-based index of the first player.</param>
    /// <param name="otherPlayerIndex">Zero-based index of the second player.</param>
    /// <param name="allied">True to make them allied; false to revert to enemies.</param>
    void SetPlayersAllied(int playerIndex, int otherPlayerIndex, bool allied);

    /// <summary>
    /// Returns true if the specified player slot is controlled by a computer AI.
    /// </summary>
    /// <param name="playerIndex">Zero-based player slot index.</param>
    bool IsPlayerComputer(int playerIndex);



    /// <summary>
    /// Applies an RGBA color tint to the specified unit's 3D mesh.
    /// Use <see cref="Vector3.One"/> (white) to reset to the default appearance.
    /// </summary>
    /// <param name="unit">The unit to tint.</param>
    /// <param name="color">RGB color values in the range [0, 1].</param>
    void SetUnitColor(IUnit unit, Vector3 color);



    /// <summary>
    /// Retrieves all alive units within the specified radius of a 3D position
    /// that satisfy the given filter predicate.
    /// </summary>
    /// <param name="center">The center point in 3D world space.</param>
    /// <param name="radius">The search radius.</param>
    /// <param name="filter">A predicate function that returns true for units to include.</param>
    /// <returns>A lazily-evaluated sequence of matching units.</returns>
    IEnumerable<IUnit> GetUnitsInRadius(Vector3 center, float radius, Func<IUnit, bool> filter)
        => GetUnitsInRadius(center, radius).Where(filter);

    /// <summary>
    /// Retrieves all alive units owned by the specified player slot
    /// that satisfy the given filter predicate.
    /// </summary>
    /// <param name="playerIndex">Zero-based player slot index.</param>
    /// <param name="filter">A predicate that returns true for units to include.</param>
    /// <returns>A lazily-evaluated sequence of matching units.</returns>
    IEnumerable<IUnit> GetUnitsOwnedByPlayer(int playerIndex, Func<IUnit, bool> filter)
        => GetUnitsOwnedByPlayer(playerIndex).Where(filter);



    /// <summary>
    /// Orders the unit to attack-move to the specified world position.
    /// The unit will attack any enemies it encounters along the way.
    /// </summary>
    /// <param name="unit">The unit to command.</param>
    /// <param name="destination">The destination position in world coordinates.</param>
    void IssueAttackMoveOrder(IUnit unit, Vector3 destination);

    /// <summary>
    /// Orders the unit to cast a targeted spell on a specific enemy unit.
    /// Does nothing if the unit does not have the specified ability.
    /// </summary>
    /// <param name="caster">The casting unit.</param>
    /// <param name="abilityId">The ability identifier string.</param>
    /// <param name="target">The target unit.</param>
    void IssueCastOrder(IUnit caster, string abilityId, IUnit target);

    /// <summary>
    /// Orders the unit to cast a ground-targeted spell at the specified world position.
    /// Does nothing if the unit does not have the specified ability.
    /// </summary>
    /// <param name="caster">The casting unit.</param>
    /// <param name="abilityId">The ability identifier string.</param>
    /// <param name="position">The target position in world coordinates.</param>
    void IssueCastOrderAt(IUnit caster, string abilityId, Vector3 position);

    /// <summary>
    /// Orders the unit to activate or deactivate an auto-cast ability.
    /// </summary>
    /// <param name="unit">The unit that owns the ability.</param>
    /// <param name="abilityId">The ability identifier string.</param>
    /// <param name="active">True to activate auto-cast; false to deactivate.</param>
    void SetAbilityAutoCast(IUnit unit, string abilityId, bool active);



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
    /// Gets the wood (lumber) resource for the specified player slot.
    /// </summary>
    /// <param name="playerIndex">Zero-based player slot index.</param>
    float GetPlayerWood(int playerIndex);

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
    void SetUnitOwner(IUnit unit, int playerIndex);



    /// <summary>
    /// Gets the current population count for the specified player slot.
    /// </summary>
    /// <param name="playerIndex">Zero-based player slot index.</param>
    int GetPlayerCurrentPopulation(int playerIndex);

    /// <summary>
    /// Gets the maximum population capacity for the specified player slot.
    /// </summary>
    /// <param name="playerIndex">Zero-based player slot index.</param>
    int GetPlayerMaxPopulation(int playerIndex);

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
    /// Returns the number of units owned by the specified player that match the optional filter.
    /// </summary>
    /// <param name="playerIndex">Zero-based player slot index.</param>
    /// <param name="filter">Optional predicate; pass null to count all units.</param>
    int CountUnitsOwnedByPlayer(int playerIndex, Func<IUnit, bool>? filter = null)
        => filter == null ? CountUnitsOwnedByPlayer(playerIndex) : GetUnitsOwnedByPlayer(playerIndex).Count(filter);



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
    event Action<IUnit, int>? OnUnitEnterZone;



    /// <summary>
    /// Associates an integer route-state value with the specified unit.
    /// Useful for tracking which waypoint branch a unit is following.
    /// </summary>
    /// <param name="unit">The unit to tag.</param>
    /// <param name="state">An arbitrary integer value meaningful to the caller.</param>
    void SetUnitRouteState(IUnit unit, int state);

    /// <summary>
    /// Retrieves the integer route-state value previously set on the unit,
    /// or 0 if none has been set.
    /// </summary>
    /// <param name="unit">The unit to query.</param>
    int GetUnitRouteState(IUnit unit);



    /// <summary>
    /// Sets the level of the specified hero unit.
    /// Has no effect on non-hero units.
    /// </summary>
    /// <param name="unit">The hero unit whose level should be changed.</param>
    /// <param name="level">The new level value.</param>
    void SetUnitLevel(IUnit unit, int level);



    /// <summary>
    /// Triggered when a player leaves or disconnects from the game.
    /// The integer argument is the zero-based player slot index.
    /// </summary>
    event Action<int>? OnPlayerLeft;



    /// <summary>
    /// Returns the number of enemy units killed by the specified player slot.
    /// </summary>
    /// <param name="playerIndex">Zero-based player slot index.</param>
    int GetPlayerKills(int playerIndex);

    /// <summary>
    /// Sets the kill count for the specified player slot directly.
    /// </summary>
    /// <param name="playerIndex">Zero-based player slot index.</param>
    /// <param name="kills">The new kill count.</param>
    void SetPlayerKills(int playerIndex, int kills);



    /// <summary>
    /// Orders the unit to move to the specified destination without attacking anything en route.
    /// Unlike <see cref="IssueAttackMoveOrder(IUnit, Vector3)"/>, this is a pure positional move command.
    /// </summary>
    /// <param name="unit">The unit to command.</param>
    /// <param name="destination">The destination in world coordinates.</param>
    void IssueMoveOrder(IUnit unit, Vector3 destination);



    /// <summary>
    /// Adds or removes a spell-immunity buff from the specified unit.
    /// Spell-immune units cannot be targeted by magic-type attacks or abilities.
    /// </summary>
    /// <param name="unit">The unit to modify.</param>
    /// <param name="immune">True to make the unit spell-immune; false to remove the immunity.</param>
    void SetUnitSpellImmune(IUnit unit, bool immune);

    /// <summary>
    /// Selects the specified unit programmatically, clearing any previously selected units.
    /// </summary>
    /// <param name="unit">The unit to select.</param>
    void SelectUnit(IUnit unit);

    /// <summary>
    /// Clears the player's unit selection programmatically.
    /// </summary>
    void ClearSelection();

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
    /// Spawns a new unit of the specified type at the center of the given coordinate region.
    /// </summary>
    /// <param name="unitTypeId">The type identifier of the unit to spawn.</param>
    /// <param name="coordinate">The coordinate region whose center will be used as spawn location.</param>
    /// <param name="isEnemy">True if the unit belongs to the enemy player.</param>
    /// <param name="bypassPopulation">If true, ignores population cap limits.</param>
    /// <returns>The spawned <see cref="IUnit"/>.</returns>
    IUnit SpawnUnit(string unitTypeId, Coordinate coordinate, bool isEnemy, bool bypassPopulation = false)
    {
        return SpawnUnit(unitTypeId, coordinate.Center, isEnemy, bypassPopulation);
    }

    /// <summary>
    /// Spawns a new unit for a specific player at the center of the given coordinate region.
    /// </summary>
    /// <param name="unitTypeId">The type identifier of the unit to spawn.</param>
    /// <param name="coordinate">The coordinate region whose center will be used as spawn location.</param>
    /// <param name="playerIndex">The player index who will own the unit.</param>
    /// <returns>The spawned <see cref="IUnit"/>.</returns>
    IUnit SpawnUnitForPlayer(string unitTypeId, Coordinate coordinate, int playerIndex)
    {
        return SpawnUnitForPlayer(unitTypeId, coordinate.Center, playerIndex);
    }

    /// <summary>
    /// Orders a unit to move to the center of the specified coordinate region.
    /// </summary>
    /// <param name="unit">The unit to order.</param>
    /// <param name="destination">The coordinate region to move to.</param>
    void IssueMoveOrder(IUnit unit, Coordinate destination)
    {
        IssueMoveOrder(unit, destination.Center);
    }

    /// <summary>
    /// Orders a unit to attack-move to the center of the specified coordinate region.
    /// </summary>
    /// <param name="unit">The unit to order.</param>
    /// <param name="destination">The coordinate region to attack-move towards.</param>
    void IssueAttackMoveOrder(IUnit unit, Coordinate destination)
    {
        IssueAttackMoveOrder(unit, destination.Center);
    }

    /// <summary>
    /// Adds an ability to the specified unit type definition.
    /// </summary>
    /// <param name="unitTypeId">The type identifier of the unit/structure.</param>
    /// <param name="abilityId">The identifier of the ability to add.</param>
    void AddUnitTypeAbility(string unitTypeId, string abilityId);

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
    event Action<IUnit, string>? OnItemSold { add { } remove { } }

    /// <summary>
    /// Triggered when a structure finishes construction.
    /// </summary>
    event Action<IUnit>? OnConstructionFinished { add { } remove { } }

    /// <summary>
    /// Triggered when a unit is issued an order.
    /// </summary>
    event Action<IUnit, string, Vector3>? OnUnitOrdered { add { } remove { } }

    /// <summary>
    /// Gets the research level of a technology upgrade for the specified player slot.
    /// </summary>
    int GetPlayerTechLevel(int playerIndex, string techId) => 0;

    /// <summary>
    /// Sets the research level of a technology upgrade for the specified player slot.
    /// </summary>
    void SetPlayerTechLevel(int playerIndex, string techId, int level) { }

    /// <summary>
    /// Adds to the research level of a technology upgrade for the specified player slot.
    /// </summary>
    void AddPlayerTechLevel(int playerIndex, string techId, int delta = 1) { }

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
    void SetItemTooltip(string itemId, string tooltip) { }

    /// <summary>
    /// Configures the grid position (X, Y) of an ability in the command card interface.
    /// </summary>
    void SetAbilityGridPosition(string abilityId, int x, int y);

    /// <summary>
    /// Sets whether an ability on a unit is disabled or hidden in the UI.
    /// </summary>
    void SetAbilityState(IUnit unit, string abilityId, bool disabled, bool hidden) { }

    /// <summary>
    /// Sets the mana cost of a unit's ability.
    /// </summary>
    void SetAbilityManaCost(IUnit unit, string abilityId, float manaCost) { }

    /// <summary>
    /// Plays a named visual animation on the unit's 3D model and equips any configured hand attachments for that animation.
    /// </summary>
    /// <param name="unit">The unit to animate.</param>
    /// <param name="animationName">The animation name (e.g., "Idle", "Labor", "Attack").</param>
    void SetUnitAnimation(IUnit unit, string animationName) { }

    /// <summary>
    /// Attaches or removes a 3D model object on the specified hand bone of the unit's rigged skeleton.
    /// </summary>
    /// <param name="unit">The target unit.</param>
    /// <param name="hand">The target hand ("RightHand" or "LeftHand").</param>
    /// <param name="attachmentId">The attachment identifier or model filename, or null/empty to clear the hand attachment.</param>
    void SetUnitHandAttachment(IUnit unit, string hand, string? attachmentId) { }

    /// <summary>
    /// Orders the unit to move to a world position without attacking along the way.
    /// Generic alias for <see cref="IssueMoveOrder(IUnit, Vector3)"/>.
    /// </summary>
    void MoveUnit(IUnit unit, Vector3 destination) => IssueMoveOrder(unit, destination);

    /// <summary>
    /// Returns living units within <paramref name="radius"/> of <paramref name="center"/>.
    /// Generic alias for <see cref="GetUnitsInRadius(Vector3, float)"/>.
    /// </summary>
    IEnumerable<IUnit> GetUnitsInRange(Vector3 center, float radius) =>
        GetUnitsInRadius(center, radius);

    /// <summary>
    /// Subtracts <paramref name="damage"/> from the target's health. Kills the target at 0 HP.
    /// Does not apply armor. Safe to call from any map script.
    /// </summary>
    void DealDamage(IUnit attacker, IUnit target, float damage)
    {
        if (target == null || target.IsDead || damage <= 0f)
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
    IUnit ReviveUnit(string unitTypeId, Vector3 position, int playerIndex) =>
        SpawnUnitForPlayer(unitTypeId, position, playerIndex);

    /// <summary>
    /// Destroys <paramref name="unit"/> and spawns a new copy of its type at <paramref name="position"/> with the same owner player index.
    /// </summary>
    /// <param name="unit">The existing unit to replace.</param>
    /// <param name="position">The spawn coordinates in 3D world space.</param>
    /// <returns>The revived unit instance.</returns>
    IUnit ReviveUnit(IUnit unit, Vector3 position)
    {
        string typeId = unit.UnitId;
        int playerIndex = unit.Player;
        DestroyUnit(unit);
        return SpawnUnitForPlayer(typeId, position, playerIndex);
    }

    /// <summary>
    /// Triggers a transient mesh deformation or hit impulse on a unit.
    /// </summary>
    /// <param name="unit">The target unit.</param>
    /// <param name="strength">The peak strength of the impulse deformation.</param>
    /// <param name="duration">The duration of the impulse oscillation in seconds.</param>
    /// <param name="frequency">The oscillation frequency in Hz.</param>
    void TriggerMeshImpulse(IUnit unit, float strength = 1.0f, float duration = 0.5f, float frequency = 12.0f);

    /// <summary>
    /// Triggers a transient mesh deformation or harvest shake impulse on a resource node.
    /// </summary>
    /// <param name="node">The target resource node.</param>
    /// <param name="strength">The peak strength of the impulse deformation.</param>
    /// <param name="duration">The duration of the impulse oscillation in seconds.</param>
    /// <param name="frequency">The oscillation frequency in Hz.</param>
    void TriggerResourceMeshImpulse(IResourceNode node, float strength = 1.0f, float duration = 0.5f, float frequency = 12.0f);

    /// <summary>
    /// Configures the AI decision profile for the specified player slot using a serialized JSON string.
    /// </summary>
    /// <param name="playerIndex">The zero-based player slot index.</param>
    /// <param name="profileJson">The serialized JSON string representing the bot profile weights and personality.</param>
    void SetPlayerBotProfile(int playerIndex, string profileJson) { }

    /// <summary>
    /// Retrieves the serialized JSON string of the active AI bot decision profile for the specified player slot.
    /// </summary>
    /// <param name="playerIndex">The zero-based player slot index.</param>
    /// <returns>The serialized JSON string representing the active bot profile, or an empty string if not configured.</returns>
    string GetPlayerBotProfile(int playerIndex) => string.Empty;

    /// <summary>
    /// Sets the AI sub-genre provider for the specified player slot (e.g., "rts", "tower_defense", "auto_battler", "custom").
    /// </summary>
    /// <param name="playerIndex">The zero-based player slot index.</param>
    /// <param name="genreName">The identifier of the AI genre provider.</param>
    /// <param name="configJson">Optional JSON configuration parameters for the genre provider.</param>
    void SetPlayerBotGenre(int playerIndex, string genreName, string? configJson = null) { }

    /// <summary>
    /// Retrieves the identifier of the active AI sub-genre provider for the specified player slot.
    /// </summary>
    /// <param name="playerIndex">The zero-based player slot index.</param>
    /// <returns>The genre identifier string (e.g., "rts", "tower_defense", "auto_battler").</returns>
    string GetPlayerBotGenre(int playerIndex) => "rts";

    /// <summary>
    /// Registers a map-defined custom candidate action for evaluation by the bot utility decision loop.
    /// </summary>
    /// <param name="playerIndex">The zero-based player slot index.</param>
    /// <param name="actionId">The unique action identifier.</param>
    /// <param name="intent">The action intent category (e.g., "Build", "Cast", "Transact", "Interact").</param>
    /// <param name="featureVector">The normalized feature vector scored against the AI weights.</param>
    /// <param name="position">The target world coordinate for the action.</param>
    /// <param name="payload">Optional metadata or argument string passed when the action executes.</param>
    void RegisterCustomBotDecision(int playerIndex, string actionId, string intent, float[] featureVector, Vector3 position, string payload = "") { }

    /// <summary>
    /// Clears all registered custom bot candidate actions for the specified player slot.
    /// </summary>
    /// <param name="playerIndex">The zero-based player slot index.</param>
    void ClearCustomBotDecisions(int playerIndex) { }

    /// <summary>
    /// Triggered when an autonomous AI bot executes a custom map-defined action.
    /// Parameters are: playerIndex, actionId, intent, targetPosition, payload.
    /// </summary>
    event Action<int, string, string, Vector3, string>? OnBotCustomActionExecuted { add { } remove { } }

    /// <summary>
    /// Executes an automated self-play genetic training simulation on CPU and returns the resulting serialized bot profile JSON.
    /// </summary>
    /// <param name="mapName">The identifier or name of the map being trained.</param>
    /// <param name="generations">The number of evolutionary generations to execute.</param>
    /// <param name="populationSize">The candidate population size per generation.</param>
    /// <param name="matchesPerEvaluation">The number of evaluation matches each candidate plays per generation.</param>
    /// <param name="genre">The AI sub-genre provider identifier (e.g. "rts", "tower_defense", "auto_battler").</param>
    /// <returns>The serialized JSON string of the best evolved bot profile.</returns>
    string TrainBotProfile(string mapName, int generations = 5, int populationSize = 8, int matchesPerEvaluation = 4, string genre = "rts") => string.Empty;

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
    /// Enumerates the zero-based indices of all active player slots below the given exclusive upper bound.
    /// </summary>
    /// <param name="exclusiveUpperBound">Exclusive upper bound on the slot index. Pass a negative value to scan every slot in <see cref="PlayerCount"/>.</param>
    /// <returns>A lazily-evaluated sequence of active player slot indices in ascending order.</returns>
    IEnumerable<int> GetActivePlayerIndices(int exclusiveUpperBound = -1)
    {
        int limit = exclusiveUpperBound < 0 ? PlayerCount : Math.Min(exclusiveUpperBound, PlayerCount);
        for (int playerIndex = 0; playerIndex < limit; playerIndex++)
        {
            if (IsPlayerActive(playerIndex))
                yield return playerIndex;
        }
    }

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
    /// <returns>A random matching unit, or null if none qualify.</returns>
    IUnit? GetRandomUnitInRadius(Vector3 center, float radius, Func<IUnit, bool>? filter = null)
    {
        var candidates = new List<IUnit>();
        foreach (IUnit unit in GetUnitsInRadius(center, radius))
        {
            if (unit.IsDead)
                continue;
            if (filter != null && !filter(unit))
                continue;
            candidates.Add(unit);
        }
        return PickRandom(candidates);
    }

    /// <summary>
    /// Restores health to the unit, clamped to its maximum health. Dead units are ignored.
    /// </summary>
    /// <param name="unit">The unit to heal.</param>
    /// <param name="amount">The amount of health to restore.</param>
    void HealUnit(IUnit unit, float amount)
    {
        if (unit == null || unit.IsDead || amount <= 0f)
            return;
        unit.Health = MathF.Min(unit.MaxHealth, unit.Health + amount);
    }

    /// <summary>
    /// Exchanges the world positions of two units.
    /// </summary>
    /// <param name="first">The first unit.</param>
    /// <param name="second">The second unit.</param>
    void SwapUnitPositions(IUnit first, IUnit second)
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
}
