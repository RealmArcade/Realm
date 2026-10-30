using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Realm.Shared.Metadata;

public class AbilityEffectMetadata
{
	[JsonPropertyName("Kind")] public string Kind { get; set; } = "damage";

	[JsonPropertyName("Delivery")] public string? Delivery { get; set; }

	[JsonPropertyName("Targets")] public string? Targets { get; set; }

	[JsonPropertyName("Radius")] public float Radius { get; set; }

	[JsonPropertyName("Length")] public float Length { get; set; }

	[JsonPropertyName("AngleDegrees")] public float AngleDegrees { get; set; } = 90f;

	[JsonPropertyName("MaxTargets")] public int MaxTargets { get; set; }

	[JsonPropertyName("Amount")] public float Amount { get; set; }

	[JsonPropertyName("Duration")] public float Duration { get; set; }

	[JsonPropertyName("DamageType")] public string? DamageType { get; set; }

	[JsonPropertyName("BuffId")] public string? BuffId { get; set; }

	[JsonPropertyName("Modifiers")] public List<StatModifierDefinition>? Modifiers { get; set; }

	[JsonPropertyName("SummonUnitId")] public string? SummonUnitId { get; set; }

	[JsonPropertyName("SummonCount")] public int SummonCount { get; set; } = 1;

	[JsonPropertyName("SummonCap")] public int SummonCap { get; set; }

	[JsonPropertyName("Scaling")] public AbilityScalingMetadata? Scaling { get; set; }
}

public class AbilityScalingMetadata
{
	[JsonPropertyName("Attribute")] public string? Attribute { get; set; }

	[JsonPropertyName("Shape")] public string Shape { get; set; } = "linear";

	[JsonPropertyName("Coefficient")] public float Coefficient { get; set; }

	[JsonPropertyName("Divisor")] public float Divisor { get; set; }

	[JsonPropertyName("Cap")] public float Cap { get; set; }

	[JsonPropertyName("ScalesDuration")] public bool ScalesDuration { get; set; } = true;
}
