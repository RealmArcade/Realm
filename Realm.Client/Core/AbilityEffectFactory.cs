using System;
using System.Collections.Generic;
using Godot;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Stats;
using Realm.Shared.Metadata;

namespace Realm.Client.Core;

/// <summary>
///     Turns authored ability effects into the immutable definitions the cast path reads, and
///     registers any inline buff modifiers once so a cast never allocates a modifier list.
/// </summary>
public static class AbilityEffectFactory
{
	/// <summary>
	///     Parses the authored effects. Returns the definitions, plus the highest damage and heal
	///     magnitude before scaling, which the command card and autocast heuristics use to tell damage
	///     abilities from support ones.
	/// </summary>
	public static List<AbilityEffectDefinition> Create(AbilityMetadata meta, float defaultRadius, out float maxDamage, out float maxHealing)
	{
		var result = new List<AbilityEffectDefinition>();
		maxDamage = 0f;
		maxHealing = 0f;

		if (meta.Effects == null) return result;

		for (int index = 0; index < meta.Effects.Count; index++)
		{
			AbilityEffectMetadata effect = meta.Effects[index];
			if (effect == null) continue;

			if (!TryParseKind(effect.Kind, out AbilityEffectKind kind))
			{
				GD.PushWarning($"[Ability] '{meta.TemplateID}' effect {index} has unknown kind '{effect.Kind}' and was skipped.");
				continue;
			}

			float radius = effect.Radius > 0f ? effect.Radius : defaultRadius;
			AbilityEffectDelivery delivery = ParseDelivery(effect.Delivery, radius);
			AbilityTargetSide targets = ParseTargets(effect.Targets, kind);
			int maxTargets = effect.MaxTargets > 0 ? effect.MaxTargets : (delivery == AbilityEffectDelivery.Single ? 1 : 0);

			string? buffModifierKey = null;
			if ((kind == AbilityEffectKind.Buff || kind == AbilityEffectKind.Debuff) && effect.Modifiers is { Count: > 0 })
			{
				buffModifierKey = $"{meta.TemplateID}#{index}";
				RegisterBuffModifiers(buffModifierKey, effect.Modifiers, meta.TemplateID);
			}

			AbilityScalingMetadata? scaling = effect.Scaling;
			AbilityScalingShape shape = ParseShape(scaling?.Shape);
			AttributeType? attribute = ParseAttribute(scaling?.Attribute, meta.TemplateID, index);

			var definition = new AbilityEffectDefinition
			{
				Kind = kind,
				Delivery = delivery,
				Targets = targets,
				Radius = radius,
				Length = effect.Length > 0f ? effect.Length : radius,
				AngleDegrees = effect.AngleDegrees > 0f ? effect.AngleDegrees : 90f,
				MaxTargets = maxTargets,
				Amount = effect.Amount,
				Duration = effect.Duration,
				DamageType = string.IsNullOrWhiteSpace(effect.DamageType) ? "magic" : effect.DamageType.Trim(),
				BuffId = string.IsNullOrWhiteSpace(effect.BuffId) ? null : effect.BuffId.Trim(),
				BuffModifierKey = buffModifierKey,
				SummonUnitId = string.IsNullOrWhiteSpace(effect.SummonUnitId) ? null : effect.SummonUnitId.Trim(),
				SummonCount = Math.Max(1, effect.SummonCount),
				SummonCap = Math.Max(0, effect.SummonCap),
				ScalingShape = attribute == null ? AbilityScalingShape.None : shape,
				ScalingAttribute = attribute,
				ScalingCoefficient = scaling?.Coefficient ?? 0f,
				ScalingDivisor = scaling?.Divisor ?? 0f,
				ScalingCap = scaling?.Cap ?? 0f
			};

			if (kind == AbilityEffectKind.Damage) maxDamage = MathF.Max(maxDamage, effect.Amount);
			if (kind == AbilityEffectKind.Heal) maxHealing = MathF.Max(maxHealing, effect.Amount);

			if (kind == AbilityEffectKind.Summon && definition.SummonUnitId == null)
			{
				GD.PushWarning($"[Ability] '{meta.TemplateID}' effect {index} is a summon with no SummonUnitId and will do nothing.");
			}

			result.Add(definition);
		}

		return result;
	}

	private static void RegisterBuffModifiers(string key, List<StatModifierDefinition> authored, string abilityId)
	{
		var modifiers = new List<StatModifier>(authored.Count);
		foreach (StatModifierDefinition authoredModifier in authored)
		{
			if (string.IsNullOrWhiteSpace(authoredModifier.StatKey)) continue;

			StatKeys.WarnIfUnknown($"ability '{abilityId}'", authoredModifier.StatKey);
			modifiers.Add(new StatModifier(
				new StatId(authoredModifier.StatKey.Trim()),
				ParseModifierType(authoredModifier.Type),
				authoredModifier.Value,
				-1f,
				authoredModifier.WASMFunctionName));
		}

		BuffRegistry.RegisterBuff(key, modifiers);
	}

	private static ModifierType ParseModifierType(string? type) => type?.Trim().ToLowerInvariant() switch
	{
		"percentage" or "percent" => ModifierType.Percentage,
		"percentadditive" or "additivepercent" => ModifierType.PercentAdditive,
		"percentmultiplicative" or "multiplicativepercent" => ModifierType.PercentMultiplicative,
		"customformula" or "formula" => ModifierType.CustomFormula,
		_ => ModifierType.Flat
	};

	private static bool TryParseKind(string? kind, out AbilityEffectKind parsed) => Enum.TryParse(kind?.Trim(), true, out parsed);

	private static AbilityEffectDelivery ParseDelivery(string? delivery, float radius)
	{
		switch (delivery?.Trim().ToLowerInvariant())
		{
			case "single": return AbilityEffectDelivery.Single;
			case "radius": return AbilityEffectDelivery.Radius;
			case "self": return AbilityEffectDelivery.Self;
			case "cone": return AbilityEffectDelivery.Cone;
			case "line": return AbilityEffectDelivery.Line;
			default: return radius > 0f ? AbilityEffectDelivery.Radius : AbilityEffectDelivery.Single;
		}
	}

	private static AbilityTargetSide ParseTargets(string? targets, AbilityEffectKind kind)
	{
		switch (targets?.Trim().ToLowerInvariant())
		{
			case "self": return AbilityTargetSide.Self;
			case "allies" or "ally": return AbilityTargetSide.Allies;
			case "enemies" or "enemy": return AbilityTargetSide.Enemies;
			case "all": return AbilityTargetSide.All;
		}

		return kind switch
		{
			AbilityEffectKind.Damage or AbilityEffectKind.Debuff or AbilityEffectKind.Knockback => AbilityTargetSide.Enemies,
			AbilityEffectKind.Heal or AbilityEffectKind.Shield or AbilityEffectKind.Buff => AbilityTargetSide.Allies,
			_ => AbilityTargetSide.Self
		};
	}

	private static AbilityScalingShape ParseShape(string? shape) => shape?.Trim().ToLowerInvariant() switch
	{
		"linear" => AbilityScalingShape.Linear,
		"ratio" => AbilityScalingShape.Ratio,
		"saturating" => AbilityScalingShape.Saturating,
		"threshold" => AbilityScalingShape.Threshold,
		"inverse" => AbilityScalingShape.Inverse,
		"conversion" => AbilityScalingShape.Conversion,
		_ => AbilityScalingShape.None
	};

	private static AttributeType? ParseAttribute(string? attribute, string abilityId, int index)
	{
		if (string.IsNullOrWhiteSpace(attribute)) return null;

		if (Enum.TryParse(attribute.Trim(), true, out AttributeType parsed)) return parsed;

		GD.PushWarning($"[Ability] '{abilityId}' effect {index} scales from unknown attribute '{attribute}'; effect left unscaled.");
		return null;
	}
}
