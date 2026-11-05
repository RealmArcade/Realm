using System.Collections.Generic;

namespace Realm.Shared.Abilities;

public class AbilityDefinition
{
	public List<AbilityEffectDefinition> Effects { get; set; } = new();


	public string Id { get; set; } = "";
	public string DisplayName { get; set; } = "";
	public string TooltipTemplate { get; set; } = "";
	public string IconPath { get; set; } = "";
	public bool IsInstant { get; set; }
	public string Hotkey { get; set; } = "";
	public int GridX { get; set; } = -1;
	public int GridY { get; set; } = -1;
	public float ManaCost { get; set; } = 0f;
	public float CostGold { get; set; } = 0f;
	public float CostWood { get; set; } = 0f;
	public float CostStone { get; set; } = 0f;
	public float Cooldown { get; set; } = 0f;
	public float TargetRange { get; set; } = 0f;
	public float AreaOfEffectRadius { get; set; } = 0f;

	public float Damage { get; set; } = 0f;

	public float Healing { get; set; } = 0f;
	public bool AutoCastEnabled { get; set; } = true;
	public bool AutoCastRequiresEnemy { get; set; } = true;
	public float AutoCastAllyHealthPercentBelow { get; set; } = 1f;
	public float AutoCastManaReserve { get; set; }
	public float AutoCastMinManaPercent { get; set; }
	public string? VisualEffect { get; set; }

	/// <summary>
	///     The weapon template this ability flies as its visual, if any. A cast spawns it and its landing is reported
	///     through <c>OnSpellImpact</c>.
	/// </summary>
	public string? ProjectileId { get; set; }
	public string? CastSound { get; set; }
}