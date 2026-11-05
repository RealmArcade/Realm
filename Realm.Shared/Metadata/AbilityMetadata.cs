using System.Collections.Generic;

namespace Realm.Shared.Metadata;

public class AbilityMetadata
{
	public string TemplateID { get; set; } = string.Empty;
	public string Name { get; set; } = string.Empty;
	public string Description { get; set; } = string.Empty;

	public string? Role { get; set; }

	public string AbilityType { get; set; } = "target_spell";
	public string? IconPath { get; set; }
	public float ManaCost { get; set; }

	// A script-defined spell can charge resources of its own. They are shown in the command card tooltip header.
	public float CostGold { get; set; }
	public float CostWood { get; set; }
	public float CostStone { get; set; }
	public float Cooldown { get; set; }
	public float TargetRange { get; set; }
	public string? VisualEffect { get; set; }

	/// <summary>
	///     A weapon template this ability fires as its own visual, from the caster towards the point it was cast at. The
	///     engine flies it at the weapon's own speed and reports the landing through OnSpellImpact, so a map neither
	///     repeats a speed its weapon template already carries nor guesses how long the flight takes.
	/// </summary>
	public string? ProjectileId { get; set; }

	public string? CastSound { get; set; }

	public AutoCastMetadata? AutoCast { get; set; }

	public float AreaOfEffectRadius { get; set; }

	public List<AbilityEffectMetadata>? Effects { get; set; }

	public int GridX { get; set; } = -1;
	public int GridY { get; set; } = -1;
	public string? Hotkey { get; set; }

	public string? Attribution_ImportedFromMap { get; set; }
}
