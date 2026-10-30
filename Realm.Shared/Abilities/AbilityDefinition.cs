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
	public string? CastSound { get; set; }
}