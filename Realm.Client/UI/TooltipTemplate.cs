using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Godot;
using Realm.Client.Core;
using Realm.Client.Services;
using Realm.Ecs;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Stats;
using Realm.Ecs.Services;

namespace Realm.Client.UI;

/// <summary>
/// Resolves <c>{{Scope.Key}}</c> tokens in an authored tooltip against live state.
///
/// A card stores the template and resolves it when the player hovers, so a number on a card is
/// always the number the game would use at that moment — no cache to invalidate, and no second
/// copy of the value to keep in step. Scopes: <c>Source</c>, <c>Target</c>, <c>Ability</c>,
/// <c>Item</c>, <c>Upgrade</c>, <c>Owner</c>. A bare key is <c>Source</c>.
///
/// Values come from two places: the canonical <c>StatKeys</c> snapshot the combat resolver reads,
/// and the map's published bag for anything the engine cannot know (see
/// <see cref="TooltipValueStore"/>). Engine keys win, so a map cannot redefine "Armor".
/// </summary>
public static class TooltipTemplate
{
	public const string MissingColor = "#ff6b6b";

	private static readonly Dictionary<string, Segment[]> CompiledTemplates = new(StringComparer.Ordinal);
	private static readonly HashSet<string> WarnedTokens = new(StringComparer.Ordinal);
	private static readonly object Gate = new();

	private readonly struct Segment
	{
		public readonly bool IsLiteral;
		public readonly string Literal;
		public readonly string Path;
		public readonly string Scope;
		public readonly string Key;
		public readonly string? Format;

		private Segment(bool isLiteral, string literal, string path, string scope, string key, string? format)
		{
			IsLiteral = isLiteral;
			Literal = literal;
			Path = path;
			Scope = scope;
			Key = key;
			Format = format;
		}

		public static Segment ForLiteral(string literal) => new(true, literal, string.Empty, string.Empty, string.Empty, null);

		public static Segment ForToken(string path, string scope, string key, string? format) =>
			new(false, string.Empty, path, scope, key, format);
	}

	/// <summary>
	/// Substitutes every token in <paramref name="template"/>. Returns the input unchanged when it
	/// holds no tokens, so a plain string costs one scan and no allocation.
	/// </summary>
	public static string Resolve(string? template, in TooltipContext context)
	{
		if (string.IsNullOrEmpty(template)) return string.Empty;
		if (template.IndexOf("{{", StringComparison.Ordinal) < 0) return template;

		Segment[] segments = Compile(template);
		var builder = new StringBuilder(template.Length + 16);

		foreach (Segment segment in segments)
		{
			if (segment.IsLiteral)
			{
				builder.Append(segment.Literal);
				continue;
			}

			if (TryResolveText(segment.Scope, segment.Key, context, out string text))
			{
				builder.Append(text);
				continue;
			}

			if (TryResolveNumber(segment.Scope, segment.Key, context, out float number))
			{
				builder.Append(FormatNumber(number, segment.Format));
				continue;
			}

			builder.Append(MissingToken(segment));
		}

		return builder.ToString();
	}

	/// <summary>
	/// Reports every token in a template that the given context cannot resolve, so an authoring
	/// check can fail a build instead of shipping a card with a hole in it.
	/// </summary>
	public static void CollectUnresolved(string? template, in TooltipContext context, List<string> into)
	{
		if (string.IsNullOrEmpty(template) || template.IndexOf("{{", StringComparison.Ordinal) < 0) return;

		foreach (Segment segment in Compile(template))
		{
			if (segment.IsLiteral) continue;
			if (TryResolveText(segment.Scope, segment.Key, context, out _)) continue;
			if (TryResolveNumber(segment.Scope, segment.Key, context, out _)) continue;
			into.Add(segment.Path);
		}
	}

	private static string FormatNumber(float value, string? format)
	{
		string specifier = string.IsNullOrEmpty(format) ? "0.##" : format;
		try
		{
			return value.ToString(specifier, CultureInfo.InvariantCulture);
		}
		catch (FormatException)
		{
			return value.ToString("0.##", CultureInfo.InvariantCulture);
		}
	}

	private static string MissingToken(in Segment segment)
	{
		lock (Gate)
		{
			if (WarnedTokens.Add(segment.Path))
			{
				GD.PushWarning($"[Tooltip] '{segment.Path}' is not a known tooltip value. " +
					"Add it to the scope or publish it from the map with SetUnitTooltipValues/SetAbilityTooltipValues.");
			}
		}

		return $"<color={MissingColor}>{segment.Path}</color>";
	}

	private static Segment[] Compile(string template)
	{
		lock (Gate)
		{
			if (CompiledTemplates.TryGetValue(template, out Segment[]? cached)) return cached;
		}

		var segments = new List<Segment>(4);
		var literal = new StringBuilder(template.Length);
		int index = 0;

		while (index < template.Length)
		{
			char current = template[index];

			// "{{" escaped as the literal text it looks like.
			if (current == '\\' && index + 2 < template.Length && template[index + 1] == '{' && template[index + 2] == '{')
			{
				literal.Append("{{");
				index += 3;
				continue;
			}

			if (current == '{' && index + 1 < template.Length && template[index + 1] == '{')
			{
				int end = template.IndexOf("}}", index + 2, StringComparison.Ordinal);
				if (end > index + 2)
				{
					if (literal.Length > 0)
					{
						segments.Add(Segment.ForLiteral(literal.ToString()));
						literal.Clear();
					}

					segments.Add(ParseToken(template.Substring(index + 2, end - index - 2)));
					index = end + 2;
					continue;
				}
			}

			literal.Append(current);
			index++;
		}

		if (literal.Length > 0) segments.Add(Segment.ForLiteral(literal.ToString()));

		Segment[] result = segments.ToArray();
		lock (Gate)
		{
			CompiledTemplates[template] = result;
		}

		return result;
	}

	/// <summary>Token body is <c>Scope.Key</c> or <c>Key</c>, either optionally followed by <c>:format</c>.</summary>
	private static Segment ParseToken(string body)
	{
		string path = body;
		string? format = null;

		int colon = body.IndexOf(':');
		if (colon >= 0)
		{
			path = body[..colon];
			format = body[(colon + 1)..].Trim();
			if (format.Length == 0) format = null;
		}

		path = path.Trim();
		int dot = path.IndexOf('.');
		string scope = dot > 0 ? path[..dot].Trim().ToLowerInvariant() : "source";
		string key = dot > 0 ? path[(dot + 1)..].Trim() : path;

		return Segment.ForToken(path, scope, key, format);
	}

	private static bool TryResolveNumber(string scope, string key, in TooltipContext context, out float value)
	{
		value = 0f;

		switch (scope)
		{
			case "source":
				return TryResolveUnitNumber(context.Source, key, out value);
			case "target":
				return TryResolveUnitNumber(context.Target, key, out value);
			case "ability":
				return TryResolveAbilityNumber(context.AbilityId, key, out value);
			case "item":
				return TryResolveItemNumber(context.ItemId, key, out value);
			case "upgrade":
				return TryResolveUpgradeNumber(context.UpgradeId, key, out value);
			case "consumable":
				return TryResolveConsumableNumber(context.AbilityId, key, out value);
			case "owner":
				return TryResolveOwnerNumber(key, out value);
			default:
				return false;
		}
	}

	private static bool TryResolveText(string scope, string key, in TooltipContext context, out string value)
	{
		value = string.Empty;

		switch (scope)
		{
			case "source":
				return TryResolveUnitText(context.Source, key, out value);
			case "target":
				return TryResolveUnitText(context.Target, key, out value);
			case "ability":
				return TryResolveAbilityText(context.AbilityId, key, out value);
			case "item":
				return TryResolveItemText(context.ItemId, key, out value);
			case "upgrade":
				return TryResolveUpgradeText(context.UpgradeId, key, out value);
			case "consumable":
				return TryResolveConsumableText(context.AbilityId, key, out value);
			default:
				return false;
		}
	}

	private static bool TryResolveUnitNumber(Entity entity, string key, out float value)
	{
		value = 0f;
		EcsWorld? world = ECSContext.World;
		if (world == null || entity == Entity.Null || !world.IsAlive(entity)) return false;

		if (string.Equals(key, "Health", StringComparison.OrdinalIgnoreCase))
		{
			if (!world.Has<Health>(entity)) return false;
			value = world.Get<Health>(entity).Current;
			return true;
		}

		if (string.Equals(key, "MaxHealth", StringComparison.OrdinalIgnoreCase) ||
			string.Equals(key, "HealthMax", StringComparison.OrdinalIgnoreCase))
		{
			if (!world.Has<Health>(entity)) return false;
			value = world.Get<Health>(entity).Max;
			return true;
		}

		if (string.Equals(key, "HealthPercent", StringComparison.OrdinalIgnoreCase))
		{
			if (!world.Has<Health>(entity)) return false;
			Health health = world.Get<Health>(entity);
			if (health.Max <= 0f) return false;
			value = health.Current / health.Max;
			return true;
		}

		if (string.Equals(key, "Mana", StringComparison.OrdinalIgnoreCase))
		{
			if (!world.Has<Mana>(entity)) return false;
			value = world.Get<Mana>(entity).Current;
			return true;
		}

		if (string.Equals(key, "MaxMana", StringComparison.OrdinalIgnoreCase))
		{
			if (!world.Has<Mana>(entity)) return false;
			value = world.Get<Mana>(entity).Max;
			return true;
		}

		if (string.Equals(key, "ManaPercent", StringComparison.OrdinalIgnoreCase))
		{
			if (!world.Has<Mana>(entity)) return false;
			Mana mana = world.Get<Mana>(entity);
			if (mana.Max <= 0f) return false;
			value = mana.Current / mana.Max;
			return true;
		}

		if (string.Equals(key, "Level", StringComparison.OrdinalIgnoreCase))
		{
			if (!world.Has<Level>(entity)) return false;
			value = world.Get<Level>(entity).Value;
			return true;
		}

		if (StatKeys.IsKnown(key) && ECSContext.StatService != null)
		{
			value = ECSContext.StatService.GetStatValue(entity, new StatId(key));
			return true;
		}

		TooltipValueStore? published = PublishedValues;
		return published != null && published.TryGetNumber(TooltipValueStore.OwnerKey("unit", entity.Id), key, out value);
	}

	private static bool TryResolveUnitText(Entity entity, string key, out string value)
	{
		value = string.Empty;
		if (entity == Entity.Null) return false;

		TooltipValueStore? published = PublishedValues;
		if (published != null &&
			published.TryGetText(TooltipValueStore.OwnerKey("unit", entity.Id), key, out value))
		{
			return true;
		}

		if (!string.Equals(key, "Name", StringComparison.OrdinalIgnoreCase)) return false;

		Unit_WasmRuntime? wrapper = GameContext.SimulationService.GetUnitWrapper(entity);
		if (wrapper == null) return false;

		string name = GameContext.RegistryService.GetUnitName(wrapper.UnitId);
		if (string.IsNullOrWhiteSpace(name)) return false;

		value = name;
		return true;
	}

	private static bool TryResolveAbilityNumber(string? abilityId, string key, out float value)
	{
		value = 0f;
		AbilityDefinition? definition = ResolveAbility(abilityId);

		if (definition != null)
		{
			if (TryResolveEffectNumber(definition, key, out value)) return true;
			if (string.Equals(key, "ManaCost", StringComparison.OrdinalIgnoreCase)) { value = definition.ManaCost; return true; }
			if (string.Equals(key, "Cooldown", StringComparison.OrdinalIgnoreCase)) { value = definition.Cooldown; return true; }
			if (string.Equals(key, "Range", StringComparison.OrdinalIgnoreCase)) { value = definition.TargetRange; return true; }
			if (string.Equals(key, "Radius", StringComparison.OrdinalIgnoreCase)) { value = definition.AreaOfEffectRadius; return true; }
			if (string.Equals(key, "Damage", StringComparison.OrdinalIgnoreCase)) { value = definition.Damage; return true; }
			if (string.Equals(key, "Healing", StringComparison.OrdinalIgnoreCase)) { value = definition.Healing; return true; }
		}

		TooltipValueStore? published = PublishedValues;
		return published != null &&
			published.TryGetNumber(TooltipValueStore.OwnerKey("ability", abilityId ?? string.Empty), key, out value);
	}

	private static bool TryResolveAbilityText(string? abilityId, string key, out string value)
	{
		value = string.Empty;
		if (string.IsNullOrEmpty(abilityId)) return false;

		TooltipValueStore? published = PublishedValues;
		if (published != null &&
			published.TryGetText(TooltipValueStore.OwnerKey("ability", abilityId), key, out value))
		{
			return true;
		}

		if (TryResolveEffectText(abilityId, key, out value)) return true;

		if (!string.Equals(key, "Name", StringComparison.OrdinalIgnoreCase)) return false;

		string name = GameContext.RegistryService.GetAbilityName(abilityId);
		if (string.IsNullOrWhiteSpace(name)) return false;

		value = name;
		return true;
	}

	private static bool TryResolveItemNumber(string? itemId, string key, out float value)
	{
		value = 0f;
		if (string.IsNullOrEmpty(itemId)) return false;

		if (GameHost.ItemRegistry.TryGetValue(itemId, out var item) && item != null)
		{
			if (string.Equals(key, "CostGold", StringComparison.OrdinalIgnoreCase)) { value = item.CostGold; return true; }
			if (string.Equals(key, "Charges", StringComparison.OrdinalIgnoreCase)) { value = item.ChargeCount; return true; }
			if (string.Equals(key, "ItemLevel", StringComparison.OrdinalIgnoreCase)) { value = item.ItemLevel; return true; }
		}

		TooltipValueStore? published = PublishedValues;
		return published != null &&
			published.TryGetNumber(TooltipValueStore.OwnerKey("item", itemId), key, out value);
	}

	private static bool TryResolveItemText(string? itemId, string key, out string value)
	{
		value = string.Empty;
		if (string.IsNullOrEmpty(itemId)) return false;

		TooltipValueStore? published = PublishedValues;
		if (published != null &&
			published.TryGetText(TooltipValueStore.OwnerKey("item", itemId), key, out value))
		{
			return true;
		}

		if (!string.Equals(key, "Name", StringComparison.OrdinalIgnoreCase)) return false;
		if (!GameHost.ItemRegistry.TryGetValue(itemId, out var item) || item == null) return false;
		if (string.IsNullOrWhiteSpace(item.Name)) return false;

		value = item.Name;
		return true;
	}

	private static bool TryResolveUpgradeNumber(string? upgradeId, string key, out float value)
	{
		value = 0f;
		if (string.IsNullOrEmpty(upgradeId)) return false;

		if (GameHost.UpgradeRegistry.TryGetValue(upgradeId, out var upgrade) && upgrade != null)
		{
			if (string.Equals(key, "CostGold", StringComparison.OrdinalIgnoreCase)) { value = upgrade.CostGold; return true; }
			if (string.Equals(key, "CostWood", StringComparison.OrdinalIgnoreCase)) { value = upgrade.CostWood; return true; }
			if (string.Equals(key, "CostStone", StringComparison.OrdinalIgnoreCase)) { value = upgrade.CostStone; return true; }
			if (string.Equals(key, "MaxLevel", StringComparison.OrdinalIgnoreCase)) { value = upgrade.MaxLevel; return true; }
			if (string.Equals(key, "ModifierCount", StringComparison.OrdinalIgnoreCase)) { value = upgrade.Modifiers?.Count ?? 0; return true; }
		}

		TooltipValueStore? published = PublishedValues;
		return published != null &&
			published.TryGetNumber(TooltipValueStore.OwnerKey("upgrade", upgradeId), key, out value);
	}

	private static bool TryResolveUpgradeText(string? upgradeId, string key, out string value)
	{
		value = string.Empty;
		if (string.IsNullOrEmpty(upgradeId)) return false;

		TooltipValueStore? published = PublishedValues;
		if (published != null &&
			published.TryGetText(TooltipValueStore.OwnerKey("upgrade", upgradeId), key, out value))
		{
			return true;
		}

		if (!string.Equals(key, "Name", StringComparison.OrdinalIgnoreCase)) return false;
		if (!GameHost.UpgradeRegistry.TryGetValue(upgradeId, out var upgrade) || upgrade == null) return false;
		if (string.IsNullOrWhiteSpace(upgrade.Name)) return false;

		value = upgrade.Name;
		return true;
	}

	private static bool TryResolveConsumableNumber(string? cardOrId, string key, out float value)
	{
		value = 0f;
		Realm.Shared.Metadata.ConsumableMetadata? consumable = ResolveConsumable(cardOrId);
		if (consumable == null) return false;

		switch (key.ToLowerInvariant())
		{
			case "cost":
			case "costgold": value = consumable.CostGold; return true;
			case "costwood": value = consumable.CostWood; return true;
			case "coststone": value = consumable.CostStone; return true;
			case "maxperunit": value = consumable.MaxPerUnit; return true;
			case "range": value = consumable.PurchaseRange; return true;
			default: return false;
		}
	}

	private static bool TryResolveConsumableText(string? cardOrId, string key, out string value)
	{
		value = string.Empty;
		Realm.Shared.Metadata.ConsumableMetadata? consumable = ResolveConsumable(cardOrId);
		if (consumable == null) return false;

		if (string.Equals(key, "SlotGroup", StringComparison.OrdinalIgnoreCase))
		{
			value = consumable.SlotGroup;
			return true;
		}

		if (!string.Equals(key, "TemplateID", StringComparison.OrdinalIgnoreCase)) return false;

		value = consumable.TemplateID;
		return true;
	}

	private static Realm.Shared.Metadata.ConsumableMetadata? ResolveConsumable(string? cardOrId)
	{
		if (string.IsNullOrEmpty(cardOrId)) return null;
		return GameContext.RegistryService?.GetConsumableForCard(cardOrId) ??
			GameContext.RegistryService?.GetConsumable(cardOrId);
	}

	private static bool TryResolveOwnerNumber(string key, out float value)
	{
		value = 0f;
		InGameHUD? hud = GameContext.InGameHud;
		if (hud == null || !GodotObject.IsInstanceValid(hud)) return false;

		if (string.Equals(key, "Gold", StringComparison.OrdinalIgnoreCase)) { value = hud.Gold; return true; }
		if (string.Equals(key, "Wood", StringComparison.OrdinalIgnoreCase)) { value = hud.Wood; return true; }
		if (string.Equals(key, "Stone", StringComparison.OrdinalIgnoreCase)) { value = hud.Stone; return true; }

		return false;
	}

	/// <summary>
	/// Reads one numeric field of one authored effect, e.g. <c>Effect0.Amount</c>,
	/// <c>Effect1.Duration</c>, <c>Effect0.Modifier1</c>. Indexes are the authored order, so a card
	/// describes the effect list the cast reads rather than restating it.
	/// </summary>
	private static bool TryResolveEffectNumber(AbilityDefinition definition, string key, out float value)
	{
		value = 0f;

		int dot = key.IndexOf('.');
		if (dot <= 0) return false;

		string head = key[..dot];
		string field = key[(dot + 1)..];

		if (!head.StartsWith("effect", StringComparison.OrdinalIgnoreCase)) return false;
		if (!int.TryParse(head[6..], NumberStyles.None, CultureInfo.InvariantCulture, out int index)) return false;
		if (definition.Effects == null || index < 0 || index >= definition.Effects.Count) return false;

		AbilityEffectDefinition effect = definition.Effects[index];
		if (effect == null) return false;

		int fieldDot = field.IndexOf('.');
		if (fieldDot > 0 && field.StartsWith("modifier", StringComparison.OrdinalIgnoreCase))
		{
			return TryResolveEffectModifier(effect, field[(fieldDot + 1)..], out value);
		}

		switch (field.ToLowerInvariant())
		{
			case "amount": value = effect.Amount; return true;
			case "duration": value = effect.Duration; return true;
			case "radius": value = effect.Radius; return true;
			case "length": value = effect.Length; return true;
			case "angle": value = effect.AngleDegrees; return true;
			case "maxtargets": value = effect.MaxTargets; return true;
			case "summoncount": value = effect.SummonCount; return true;
			case "summoncap": value = effect.SummonCap; return true;
			case "coefficient": value = effect.ScalingCoefficient; return true;
			case "divisor": value = effect.ScalingDivisor; return true;
			case "cap": value = effect.ScalingCap; return true;
			case "modifiercount": value = ModifiersFor(effect)?.Count ?? 0; return true;
			default: return false;
		}
	}

	private static bool TryResolveEffectModifier(AbilityEffectDefinition effect, string indexText, out float value)
	{
		value = 0f;

		if (!int.TryParse(indexText, NumberStyles.None, CultureInfo.InvariantCulture, out int modifierIndex)) return false;

		List<Realm.Ecs.Components.Stats.StatModifier>? modifiers = ModifiersFor(effect);
		if (modifiers == null || modifierIndex < 0 || modifierIndex >= modifiers.Count) return false;

		value = modifiers[modifierIndex].Value;
		return true;
	}

	private static List<Realm.Ecs.Components.Stats.StatModifier>? ModifiersFor(AbilityEffectDefinition effect)
	{
		if (string.IsNullOrEmpty(effect.BuffModifierKey)) return null;
		return BuffRegistry.BuffModifiers.TryGetValue(effect.BuffModifierKey, out var modifiers) ? modifiers : null;
	}

	/// <summary>Text fields of an effect, currently just the scaling attribute's name.</summary>
	private static bool TryResolveEffectText(string? abilityId, string key, out string value)
	{
		value = string.Empty;

		int dot = key.IndexOf('.');
		if (dot <= 0) return false;

		string head = key[..dot];
		string field = key[(dot + 1)..];

		if (!head.StartsWith("effect", StringComparison.OrdinalIgnoreCase)) return false;
		if (!string.Equals(field, "Attribute", StringComparison.OrdinalIgnoreCase)) return false;

		AbilityDefinition? definition = ResolveAbility(abilityId);
		if (definition?.Effects == null) return false;
		if (!int.TryParse(head[6..], NumberStyles.None, CultureInfo.InvariantCulture, out int index)) return false;
		if (index < 0 || index >= definition.Effects.Count) return false;

		AttributeType? attribute = definition.Effects[index]?.ScalingAttribute;
		if (attribute == null) return false;

		value = attribute.Value.ToString();
		return true;
	}

	private static AbilityDefinition? ResolveAbility(string? abilityId)
	{
		if (string.IsNullOrEmpty(abilityId)) return null;
		return GameContext.Host?.GetAbilityDefinition(abilityId) ??
			GameContext.RegistryService?.GetAbilityDefinition(abilityId);
	}

	private static TooltipValueStore? PublishedValues => GameContext.RegistryService?.TooltipValues;
}
