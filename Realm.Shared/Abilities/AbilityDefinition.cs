using System.Collections.Generic;

namespace Realm.Shared.Abilities;

public class AbilityDefinition
{
	/// <summary>
	///     What the cast does, in authored order. Empty means the ability is scripted: the cast
	///     callback still fires, nothing is applied by the engine.
	/// </summary>
	public List<AbilityEffectDefinition> Effects { get; set; } = new();


	public string Id { get; set; } = "";
	public string DisplayName { get; set; } = "";
	/// <summary>Authored card text. May contain {{Scope.Key}} tokens, resolved when shown.</summary>
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

	/// <summary>Highest authored damage magnitude before scaling. Zero when the ability deals none.</summary>
	public float Damage { get; set; } = 0f;

	/// <summary>Highest authored heal magnitude before scaling. Zero when the ability heals none.</summary>
	public float Healing { get; set; } = 0f;
	public bool AutoCastEnabled { get; set; } = true;
	public bool AutoCastRequiresEnemy { get; set; } = true;
	public float AutoCastAllyHealthPercentBelow { get; set; } = 1f;
	public float AutoCastManaReserve { get; set; }
	public float AutoCastMinManaPercent { get; set; }
	public string? VisualEffect { get; set; }
	public string? CastSound { get; set; }
}