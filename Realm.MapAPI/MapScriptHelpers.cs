using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.Json;

namespace Realm.MapAPI;

/// <summary>
/// Configuration settings for tower defense combat behavior, including range, damage, cooldowns, and visual effects.
/// </summary>
public readonly struct TowerDefenseConfig
{
    /// <summary>
    /// Gets the attack range of the tower defense unit.
    /// </summary>
    public float Range { get; }

    /// <summary>
    /// Gets the base attack damage dealt by the tower defense unit.
    /// </summary>
    public float Damage { get; }

    /// <summary>
    /// Gets the cooldown interval between attacks in seconds.
    /// </summary>
    public float AttackCooldownSeconds { get; }

    /// <summary>
    /// Gets the identifier of the visual effect spawned when attacking.
    /// </summary>
    public string VisualEffectId { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="TowerDefenseConfig"/> struct.
    /// </summary>
    /// <param name="range">The attack range of the tower.</param>
    /// <param name="damage">The damage dealt per attack.</param>
    /// <param name="attackCooldownSeconds">The cooldown duration in seconds between attacks.</param>
    /// <param name="visualEffectId">The identifier of the visual effect to spawn when attacking, or an empty string for none.</param>
    public TowerDefenseConfig(float range, float damage, float attackCooldownSeconds, string visualEffectId)
    {
        Range = range;
        Damage = damage;
        AttackCooldownSeconds = attackCooldownSeconds;
        VisualEffectId = visualEffectId;
    }
}

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

/// <summary>
/// Provides helper methods for common map script gameplay mechanics, such as automated tower defense updates.
/// </summary>
public static class MapScriptHelpers
{
    /// <summary>
    /// Runs a single simulation tick of tower defense logic for a tower unit, acquiring the nearest valid enemy target within range and performing an attack if the cooldown has elapsed.
    /// </summary>
    /// <param name="api">The game API instance used to query targets and spawn visual effects.</param>
    /// <param name="tower">The defending tower unit.</param>
    /// <param name="config">The tower defense configuration specifying range, damage, cooldown, and visual effects.</param>
    /// <param name="cooldownRemaining">A reference to the remaining attack cooldown in seconds, which will be updated.</param>
    /// <param name="delta">The time elapsed since the previous simulation tick, in seconds.</param>
    /// <returns><see langword="true"/> if the tower attacked a target during this tick; otherwise, <see langword="false"/>.</returns>
    public static bool RunTowerDefenseTick(
        IGameAPI api,
        IUnit tower,
        TowerDefenseConfig config,
        ref float cooldownRemaining,
        float delta)
    {
        if (tower.IsDead)
            return false;

        cooldownRemaining = MathF.Max(0f, cooldownRemaining - delta);

        var range = tower.Range > 0 ? tower.Range : config.Range;

        var target = api.GetUnitsInRadius(tower.Position, range)
            .Where(unit => !unit.IsDead && unit.IsEnemy != tower.IsEnemy)
            .OrderBy(unit => Vector3.DistanceSquared(unit.Position, tower.Position))
            .FirstOrDefault();

        if (target == null || cooldownRemaining > 0)
            return false;

        tower.Attack(target);
        if (!string.IsNullOrEmpty(config.VisualEffectId))
            api.SpawnVisualEffect(config.VisualEffectId, target.Position, 0.35f);

        cooldownRemaining = config.AttackCooldownSeconds;
        return true;
    }
}

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

/// <summary>
/// Configuration settings for an autonomous AI bot profile including decision cadence and personality attributes.
/// </summary>
public readonly struct BotProfileConfig
{
    /// <summary>
    /// Gets the cadence in seconds between decision evaluation cycles.
    /// </summary>
    public float DecisionIntervalSeconds { get; }

    /// <summary>
    /// Gets the multiplier applied to aggressive/offensive tactical affordances.
    /// </summary>
    public float AggressionMultiplier { get; }

    /// <summary>
    /// Gets the softmax temperature parameter controlling exploration and decision randomness.
    /// </summary>
    public float ActionTemperature { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="BotProfileConfig"/> struct.
    /// </summary>
    /// <param name="decisionIntervalSeconds">The time in seconds between AI decision ticks.</param>
    /// <param name="aggressionMultiplier">The multiplier applied to aggressive utility calculations.</param>
    /// <param name="actionTemperature">The temperature factor for softmax action selection.</param>
    public BotProfileConfig(float decisionIntervalSeconds, float aggressionMultiplier, float actionTemperature)
    {
        DecisionIntervalSeconds = decisionIntervalSeconds;
        AggressionMultiplier = aggressionMultiplier;
        ActionTemperature = actionTemperature;
    }

    /// <summary>
    /// Gets the default AI bot profile configuration.
    /// </summary>
    public static BotProfileConfig Default => new(1.0f, 1.0f, 0.05f);
}

/// <summary>
/// Defines standard AI genre identifiers for arcade and RTS map scripts.
/// </summary>
public static class AiGenrePresets
{
    /// <summary>
    /// Standard RTS melee and skirmish tactical AI.
    /// </summary>
    public const string Rts = "rts";

    /// <summary>
    /// Tower defense building, upgrading, and pathing AI.
    /// </summary>
    public const string TowerDefense = "tower_defense";

    /// <summary>
    /// Auto-battler shop purchasing, bench management, and synergy AI.
    /// </summary>
    public const string AutoBattler = "auto_battler";

    /// <summary>
    /// Custom UGC map-defined AI driven by custom affordance registrations.
    /// </summary>
    public const string Custom = "custom";
}

/// <summary>
/// Configuration parameters for a Tower Defense AI bot.
/// </summary>
public class TowerDefenseBotConfig
{
    /// <summary>
    /// Gets or sets the collection of designated world coordinates where towers can be constructed.
    /// </summary>
    public List<Vector3> BuildSpots { get; set; } = new();

    /// <summary>
    /// Gets or sets whether the AI is permitted to sell obsolete towers during economic emergencies.
    /// </summary>
    public bool AllowSell { get; set; }

    /// <summary>
    /// Converts this configuration into a JSON dictionary string for injection into the AI bot provider.
    /// </summary>
    /// <returns>A JSON string representation of the configuration parameters.</returns>
    public string ToJson()
    {
        var dict = new Dictionary<string, string>
        {
            { "BuildSpotsJson", JsonSerializer.Serialize(BuildSpots) },
            { "AllowSell", AllowSell.ToString() }
        };
        return JsonSerializer.Serialize(dict);
    }
}

/// <summary>
/// Configuration parameters for an Auto-Battler AI bot.
/// </summary>
public class AutoBattlerBotConfig
{
    /// <summary>
    /// Gets or sets the collection of board tile coordinates available for deployed combat units.
    /// </summary>
    public List<Vector3> BoardSlots { get; set; } = new();

    /// <summary>
    /// Gets or sets the collection of bench tile coordinates available for reserve units.
    /// </summary>
    public List<Vector3> BenchSlots { get; set; } = new();

    /// <summary>
    /// Gets or sets the gold cost required to reroll the shop offerings.
    /// </summary>
    public int RerollCost { get; set; } = 2;

    /// <summary>
    /// Gets or sets the gold cost required to purchase shop experience / level up.
    /// </summary>
    public int LevelUpCost { get; set; } = 4;

    /// <summary>
    /// Converts this configuration into a JSON dictionary string for injection into the AI bot provider.
    /// </summary>
    /// <returns>A JSON string representation of the configuration parameters.</returns>
    public string ToJson()
    {
        var dict = new Dictionary<string, string>
        {
            { "BoardSlotsJson", JsonSerializer.Serialize(BoardSlots) },
            { "BenchSlotsJson", JsonSerializer.Serialize(BenchSlots) },
            { "RerollCost", RerollCost.ToString() },
            { "LevelUpCost", LevelUpCost.ToString() }
        };
        return JsonSerializer.Serialize(dict);
    }
}

/// <summary>
/// Represents a map-defined custom candidate action submitted to the AI utility decision loop.
/// </summary>
public class CustomBotDecision
{
    /// <summary>
    /// Gets or sets the unique action identifier.
    /// </summary>
    public string ActionId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the command intent name (e.g., "Build", "Cast", "Transact", "Interact").
    /// </summary>
    public string Intent { get; set; } = "Interact";

    /// <summary>
    /// Gets or sets the feature vector evaluated against the AI weights.
    /// </summary>
    public float[] FeatureVector { get; set; } = Array.Empty<float>();

    /// <summary>
    /// Gets or sets the target world position associated with this action.
    /// </summary>
    public Vector3 TargetPosition { get; set; } = Vector3.Zero;

    /// <summary>
    /// Gets or sets arbitrary payload data or arguments passed to the execution callback.
    /// </summary>
    public string Payload { get; set; } = string.Empty;
}
