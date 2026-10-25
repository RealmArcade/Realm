using System;
using Realm.Ecs.Components.Stats;

namespace Realm.Client.Core;

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

	/// <summary>A radius around the caster rather than around the cast point.</summary>
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

/// <summary>
///     One resolved effect of an ability: what it does, to whom, over what shape, and how its
///     magnitude follows the caster's attributes. Built once at registration; the cast path only
///     reads it.
/// </summary>
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

	/// <summary>Engine-recognised status id (stun, silence, ...) or a plain marker.</summary>
	public string? BuffId { get; init; }

	/// <summary>Registry key for this effect's inline modifiers, or null when it carries none.</summary>
	public string? BuffModifierKey { get; init; }

	public string? SummonUnitId { get; init; }
	public int SummonCount { get; init; }
	public int SummonCap { get; init; }

	public AbilityScalingShape ScalingShape { get; init; }

	/// <summary>Attribute the effect scales from, or null when it is a flat authored number.</summary>
	public AttributeType? ScalingAttribute { get; init; }
	public float ScalingCoefficient { get; init; }
	public float ScalingDivisor { get; init; }
	public float ScalingCap { get; init; }

	/// <summary>
	///     When false the scaling formula reaches the magnitude only, and the timer stays the
	///     authored Duration. Most kits want a shield whose size grows but whose lifetime does not.
	/// </summary>
	public bool ScalesDuration { get; init; } = true;

	/// <summary>Applies this effect's scaling to a duration, honouring <see cref="ScalesDuration" />.</summary>
	public float ScaleDuration(float duration, float attributeValue)
	{
		return ScalesDuration ? Scale(duration, attributeValue) : duration;
	}

	/// <summary>
	///     Applies this effect's scaling to a base magnitude. One attribute in, one magnitude out;
	///     the cap is the authoring guarantee that a stacking stat cannot make an effect unbounded.
	/// </summary>
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
