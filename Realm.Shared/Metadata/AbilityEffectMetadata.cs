using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Realm.Shared.Metadata;

/// <summary>
///     One composable effect inside an ability. An ability is a list of these rather than a single
///     damage-or-heal number, so a kit can damage and buff, control and shield, or summon and blink.
///     Every field is consumed by the runtime effect executor; a kind that has no implementation is
///     not declared here, so nothing can be authored and silently ignored.
/// </summary>
public class AbilityEffectMetadata
{
	/// <summary>
	///     One of: damage, heal, shield, buff, debuff, summon, teleport, knockback.
	///     Defaults to damage.
	/// </summary>
	[JsonPropertyName("Kind")] public string Kind { get; set; } = "damage";

	/// <summary>
	///     How targets are gathered: single, radius, self, cone or line. Absent means radius when a
	///     Radius is set, otherwise single.
	/// </summary>
	[JsonPropertyName("Delivery")] public string? Delivery { get; set; }

	/// <summary>
	///     Who the effect lands on: self, allies, enemies or all. Absent follows the kind — damage and
	///     debuffs hit enemies, heals, shields and buffs hit allies, teleport and knockback follow
	///     their own rules.
	/// </summary>
	[JsonPropertyName("Targets")] public string? Targets { get; set; }

	/// <summary>Radius for radius delivery, in world units. Falls back to the ability's AreaOfEffectRadius.</summary>
	[JsonPropertyName("Radius")] public float Radius { get; set; }

	/// <summary>Reach for cone and line delivery, in world units.</summary>
	[JsonPropertyName("Length")] public float Length { get; set; }

	/// <summary>Full opening angle of a cone delivery, in degrees.</summary>
	[JsonPropertyName("AngleDegrees")] public float AngleDegrees { get; set; } = 90f;

	/// <summary>Maximum number of targets. Zero means the delivery's own limit.</summary>
	[JsonPropertyName("MaxTargets")] public int MaxTargets { get; set; }

	/// <summary>
	///     Magnitude before scaling: damage, healing, shield size, knockback distance, or how far a
	///     teleport may move. Ignored by buff, debuff and summon.
	/// </summary>
	[JsonPropertyName("Amount")] public float Amount { get; set; }

	/// <summary>Duration for shields, buffs, debuffs and summon lifespan. Zero means permanent or instant.</summary>
	[JsonPropertyName("Duration")] public float Duration { get; set; }

	/// <summary>Damage type for damage effects. Defaults to magic.</summary>
	[JsonPropertyName("DamageType")] public string? DamageType { get; set; }

	/// <summary>
	///     Buff or status id applied by a buff/debuff effect. Engine-recognised ids such as "stun" and
	///     "silence" change behaviour; any other id is a named marker. Leaves a buff with no stat
	///     change when <see cref="Modifiers" /> is empty.
	/// </summary>
	[JsonPropertyName("BuffId")] public string? BuffId { get; set; }

	/// <summary>Stat modifiers the buff/debuff applies while it lasts.</summary>
	[JsonPropertyName("Modifiers")] public List<StatModifierDefinition>? Modifiers { get; set; }

	/// <summary>Unit template summoned by a summon effect.</summary>
	[JsonPropertyName("SummonUnitId")] public string? SummonUnitId { get; set; }

	/// <summary>How many units one cast of a summon effect spawns.</summary>
	[JsonPropertyName("SummonCount")] public int SummonCount { get; set; } = 1;

	/// <summary>
	///     How many summons of this template one caster may keep alive at once. Zero means no cap
	///     beyond the buff slot budget.
	/// </summary>
	[JsonPropertyName("SummonCap")] public int SummonCap { get; set; }

	/// <summary>Optional stat scaling applied to Amount and Duration.</summary>
	[JsonPropertyName("Scaling")] public AbilityScalingMetadata? Scaling { get; set; }
}

/// <summary>
///     Turns a raw effect magnitude into a value scaled by one attribute. One attribute, one shape,
///     one coefficient: enough for every beta kit without inventing a formula language.
/// </summary>
public class AbilityScalingMetadata
{
	/// <summary>Strength, Agility, Vitality, Intelligence, Wisdom or Fortune.</summary>
	[JsonPropertyName("Attribute")] public string? Attribute { get; set; }

	/// <summary>linear, ratio, saturating, threshold, inverse or conversion.</summary>
	[JsonPropertyName("Shape")] public string Shape { get; set; } = "linear";

	/// <summary>Per-point weight. For conversion this is the only term.</summary>
	[JsonPropertyName("Coefficient")] public float Coefficient { get; set; }

	/// <summary>Divisor, K, or step size depending on the shape.</summary>
	[JsonPropertyName("Divisor")] public float Divisor { get; set; }

	/// <summary>Upper bound on the scaled value. Zero means uncapped.</summary>
	[JsonPropertyName("Cap")] public float Cap { get; set; }
}
