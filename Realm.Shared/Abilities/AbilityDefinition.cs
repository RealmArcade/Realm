using System.Collections.Generic;
using Realm.Shared.Metadata;

namespace Realm.Shared.Abilities;

public class AbilityDefinition
{
	public List<AbilityEffectDefinition> Effects { get; set; } = new();


	public string Id { get; set; } = "";
	public string DisplayName { get; set; } = "";
	public string TooltipTemplate { get; set; } = "";
	public string IconPath { get; set; } = "";
	public bool IsInstant { get; set; }

	/// <summary>
	///     A passive ability is part of a unit's kit without being castable: its card shows the icon and tooltip a
	///     player reads, and pressing it does nothing because there is no cast to begin.
	/// </summary>
	public bool IsPassive { get; set; }
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
	public AutoCastTargetRule AutoCastTargetRule { get; set; } = AutoCastTargetRule.NearestEnemy;
	public float AutoCastAllyHealthPercentBelow { get; set; } = 1f;
	public float AutoCastManaReserve { get; set; }
	public float AutoCastMinManaPercent { get; set; }
	public string? VisualEffect { get; set; }

	/// <summary>
	///     The weapon template this ability flies as its visual, if any. A cast spawns it and its landing is reported
	///     through <c>OnSpellImpact</c>.
	/// </summary>
	public string? ProjectileId { get; set; }

	/// <summary>
	///     The template this card places with the standard construction placement, or null when the card is a cast.
	/// </summary>
	public string? PlacementTemplateId { get; set; }
	public string? CastSound { get; set; }
}