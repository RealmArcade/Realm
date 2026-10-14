namespace Realm.MapAPI;

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
	/// Alias for standard RTS melee tactical AI.
	/// </summary>
	public const string Melee = "melee";

	/// <summary>
	/// Tower defense building, upgrading, and pathing AI.
	/// </summary>
	public const string TowerDefense = "tower_defense";

	/// <summary>
	/// Auto-battler shop purchasing, bench management, and synergy AI.
	/// </summary>
	public const string AutoBattler = "auto_battler";

	/// <summary>
	/// Tug-of-war and auto-spawning wave combat AI (e.g., Castle Fight, Desert Strike, Nexus Wars).
	/// </summary>
	public const string TugOfWar = "tug_of_war";

	/// <summary>
	/// Hero arena and MOBA combat AI (e.g., DotA, Footman Frenzy, Hero Battles).
	/// </summary>
	public const string HeroArena = "hero_arena";

	/// <summary>
	/// Custom UGC map-defined AI driven by custom affordance registrations.
	/// </summary>
	public const string Custom = "custom";
}