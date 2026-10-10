using System.Numerics;

namespace Realm.MapAPI;

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

    /// <summary>
    /// Configures an AI bot player for standard Melee or RTS skirmish tactics with customizable retreat and aggression parameters.
    /// </summary>
    /// <param name="api">The game API instance.</param>
    /// <param name="playerIndex">The zero-based player slot index.</param>
    /// <param name="config">The melee bot configuration parameters.</param>
    public static void ConfigureMeleeBot(this IGameAPI api, int playerIndex, MeleeBotConfig config)
    {
        api.SetPlayerBotGenre(playerIndex, AiGenrePresets.Melee, config?.ToJson());
    }

    /// <summary>
    /// Configures an AI bot player for Tower Defense mode with customizable build spot locations and sell policies.
    /// </summary>
    /// <param name="api">The game API instance.</param>
    /// <param name="playerIndex">The zero-based player slot index.</param>
    /// <param name="config">The tower defense bot configuration parameters.</param>
    public static void ConfigureTowerDefenseBot(this IGameAPI api, int playerIndex, TowerDefenseBotConfig config)
    {
        api.SetPlayerBotGenre(playerIndex, AiGenrePresets.TowerDefense, config?.ToJson());
    }

    /// <summary>
    /// Configures an AI bot player for Auto-Battler mode with customizable board slots, bench slots, and economic thresholds.
    /// </summary>
    /// <param name="api">The game API instance.</param>
    /// <param name="playerIndex">The zero-based player slot index.</param>
    /// <param name="config">The auto-battler bot configuration parameters.</param>
    public static void ConfigureAutoBattlerBot(this IGameAPI api, int playerIndex, AutoBattlerBotConfig config)
    {
        api.SetPlayerBotGenre(playerIndex, AiGenrePresets.AutoBattler, config?.ToJson());
    }

    /// <summary>
    /// Configures an AI bot player for Tug-of-War / Auto-Spawn mode (e.g., Castle Fight, Desert Strike, Nexus Wars) with build spots, spawner options, and income rules.
    /// </summary>
    /// <param name="api">The game API instance.</param>
    /// <param name="playerIndex">The zero-based player slot index.</param>
    /// <param name="config">The tug-of-war bot configuration parameters.</param>
    public static void ConfigureTugOfWarBot(this IGameAPI api, int playerIndex, TugOfWarBotConfig config)
    {
        api.SetPlayerBotGenre(playerIndex, AiGenrePresets.TugOfWar, config?.ToJson());
    }

    /// <summary>
    /// Configures an AI bot player for Hero Arena / MOBA mode (e.g., DotA, Footman Frenzy) with fountain coordinates, retreat thresholds, and item build paths.
    /// </summary>
    /// <param name="api">The game API instance.</param>
    /// <param name="playerIndex">The zero-based player slot index.</param>
    /// <param name="config">The hero arena bot configuration parameters.</param>
    public static void ConfigureHeroArenaBot(this IGameAPI api, int playerIndex, HeroArenaBotConfig config)
    {
        api.SetPlayerBotGenre(playerIndex, AiGenrePresets.HeroArena, config?.ToJson());
    }

    /// <summary>
    /// Registers a map-defined custom candidate decision for evaluation by an AI bot.
    /// </summary>
    /// <param name="api">The game API instance.</param>
    /// <param name="playerIndex">The zero-based player slot index.</param>
    /// <param name="decision">The candidate decision descriptor.</param>
    public static void RegisterBotCustomDecision(this IGameAPI api, int playerIndex, CustomBotDecision decision)
    {
        if (decision == null) return;
        api.RegisterCustomBotDecision(playerIndex, decision.ActionId, decision.Intent, decision.FeatureVector, decision.TargetPosition, decision.Payload);
    }
}