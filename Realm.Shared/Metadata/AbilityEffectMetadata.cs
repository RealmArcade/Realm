using System.Collections.Generic;
using System.Text.Json.Serialization;
using Realm.Shared.Serialization;

namespace Realm.Shared.Metadata;

[SchemaDescription("One thing an ability does when it lands: how much damage or healing it applies, in what shape, to which side, and what it summons or charges for it.")]
public class AbilityEffectMetadata
{
	[JsonPropertyName("Kind")]
	[SchemaDescription("What the effect does, such as damage, heal, buff, summon or displace.")]
	public string Kind { get; set; } = "damage";

	[JsonPropertyName("Delivery")]
	[SchemaDescription("How the effect reaches its targets, such as instantly at the aim point or in a travelling wave.")]
	public string? Delivery { get; set; }

	[JsonPropertyName("Targets")]
	[SchemaDescription("Which side of the fight the effect lands on, such as enemies, allies or the caster itself.")]
	public string? Targets { get; set; }

	[JsonPropertyName("Radius")]
	[SchemaDescription("How far from the aim point the effect reaches.")]
	[SchemaUnit("m")]
	public float Radius { get; set; }

	[JsonPropertyName("Length")]
	[SchemaDescription("How long the effect's shape is when it is a line rather than a circle.")]
	[SchemaUnit("m")]
	public float Length { get; set; }

	[JsonPropertyName("AngleDegrees")]
	[SchemaDescription("How wide the effect's cone opens when it is a cone rather than a circle.")]
	[SchemaUnit("°")]
	public float AngleDegrees { get; set; } = 90f;

	[JsonPropertyName("MaxTargets")]
	[SchemaDescription("How many units the effect may land on at once. Zero means as many as the shape covers.")]
	public int MaxTargets { get; set; }

	[JsonPropertyName("Amount")]
	[SchemaDescription("How much the effect applies: damage, healing or the base value its scaling starts from.")]
	public float Amount { get; set; }

	[JsonPropertyName("Duration")]
	[SchemaDescription("How long what the effect applies lasts, such as a buff's length or a summon's lifetime.")]
	[SchemaUnit("s")]
	public float Duration { get; set; }

	[JsonPropertyName("DamageType")]
	[SchemaDescription("Whether the effect's damage is mitigated as physical or as magical.")]
	public string? DamageType { get; set; }

	[JsonPropertyName("BuffId")]
	[SchemaDescription("The status effect the ability applies to the units it lands on.")]
	public string? BuffId { get; set; }

	[JsonPropertyName("Modifiers")]
	[SchemaDescription("The stat changes the ability applies for as long as its effect lasts.")]
	public List<StatModifierDefinition>? Modifiers { get; set; }

	[JsonPropertyName("SummonUnitId")]
	[SchemaDescription("The unit template the ability places on the battlefield.")]
	[SchemaReference("Units")]
	public string? SummonUnitId { get; set; }

	[JsonPropertyName("SummonCount")]
	[SchemaDescription("How many units one cast of the summoning ability places.")]
	public int SummonCount { get; set; } = 1;

	[JsonPropertyName("SummonCap")]
	[SchemaDescription("How many of the summoned units one caster may have alive at once. Zero means no limit.")]
	public int SummonCap { get; set; }

	[JsonPropertyName("Scaling")]
	[SchemaDescription("How the effect's amount grows with one of the caster's attributes.")]
	public AbilityScalingMetadata? Scaling { get; set; }
}

[SchemaDescription("How an ability effect's amount grows with one of the caster's attributes, such as its intelligence or its attack.")]
public class AbilityScalingMetadata
{
	[JsonPropertyName("Attribute")]
	[SchemaDescription("The caster attribute the effect scales with.")]
	public string? Attribute { get; set; }

	[JsonPropertyName("Shape")]
	[SchemaDescription("How the attribute is turned into extra effect, such as a straight line or a diminishing curve.")]
	public string Shape { get; set; } = "linear";

	[JsonPropertyName("Coefficient")]
	[SchemaDescription("How much extra effect each point of the attribute is worth.")]
	public float Coefficient { get; set; }

	[JsonPropertyName("Divisor")]
	[SchemaDescription("What the scaled bonus is divided by, which softens a large coefficient into a readable number.")]
	public float Divisor { get; set; }

	[JsonPropertyName("Cap")]
	[SchemaDescription("The most the attribute may add to the effect. Zero means the bonus is uncapped.")]
	public float Cap { get; set; }

	[JsonPropertyName("Offset")]
	[SchemaDescription("A flat amount added to the scaled bonus before it is applied.")]
	public float Offset { get; set; }

	[JsonPropertyName("ScalesDuration")]
	[SchemaDescription("Whether the attribute also stretches the effect's duration, not just its amount.")]
	public bool ScalesDuration { get; set; } = true;
}
