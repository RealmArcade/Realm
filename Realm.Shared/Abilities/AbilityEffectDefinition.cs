using System;
using Realm.Ecs.Components.Stats;

namespace Realm.Shared.Abilities;

public enum AbilityEffectKind
{
	Damage,
	Heal,
	Shield,
	Buff,
	Debuff,
	Summon,
	Teleport,
	Knockback
}

public enum AbilityEffectDelivery
{
	Single,
	Radius,
	Self,
	Cone,
	Line,

	Aura
}

public enum AbilityTargetSide
{
	Self,
	Allies,
	Enemies,
	All
}

public enum AbilityScalingShape
{
	None,
	Linear,
	Ratio,
	Saturating,
	Threshold,
	Inverse,
	Conversion
}

public sealed class AbilityEffectDefinition
{
	public AbilityEffectKind Kind { get; init; }
	public AbilityEffectDelivery Delivery { get; init; }
	public AbilityTargetSide Targets { get; init; }
	public float Radius { get; init; }
	public float Length { get; init; }
	public float AngleDegrees { get; init; } = 90f;
	public int MaxTargets { get; init; }
	public float Amount { get; init; }
	public float Duration { get; init; }
	public string DamageType { get; init; } = "magic";

	public string? BuffId { get; init; }

	public string? BuffModifierKey { get; init; }

	public string? SummonUnitId { get; init; }
	public int SummonCount { get; init; }
	public int SummonCap { get; init; }

	public AbilityScalingShape ScalingShape { get; init; }

	public AttributeType? ScalingAttribute { get; init; }
	public float ScalingCoefficient { get; init; }
	public float ScalingDivisor { get; init; }
	public float ScalingCap { get; init; }

	public bool ScalesDuration { get; init; } = true;

	public float ScaleDuration(float duration, float attributeValue)
	{
		return ScalesDuration ? Scale(duration, attributeValue) : duration;
	}

	public float Scale(float baseValue, float attributeValue)
	{
		float stat = MathF.Max(0f, attributeValue);
		float divisor = ScalingDivisor > 0.0001f ? ScalingDivisor : 1f;

		float scaled = ScalingShape switch
		{
			AbilityScalingShape.Linear => baseValue + (ScalingCoefficient * stat),
			AbilityScalingShape.Ratio => baseValue * (1f + (stat / divisor)),
			AbilityScalingShape.Saturating => (ScalingCap > 0f ? ScalingCap : baseValue) * (stat / (stat + divisor)),
			AbilityScalingShape.Threshold => baseValue + (ScalingCoefficient * MathF.Floor(stat / divisor)),
			AbilityScalingShape.Inverse => baseValue / (1f + (stat / divisor)),
			AbilityScalingShape.Conversion => stat * ScalingCoefficient,
			_ => baseValue
		};

		if (ScalingShape != AbilityScalingShape.Inverse && ScalingCap > 0f)
		{
			scaled = MathF.Min(scaled, ScalingCap);
		}

		return MathF.Max(0f, scaled);
	}
}
