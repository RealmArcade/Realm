using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Realm.Client.Core;
using Realm.Shared.Metadata;

namespace Realm.Client.UI;

public static class TooltipStatBlock
{
	public static string ForUpgrade(UpgradeMetadata? upgrade)
	{
		if (upgrade?.Modifiers == null || upgrade.Modifiers.Count == 0) return string.Empty;

		var builder = new StringBuilder(64);
		Append(builder, upgrade.Modifiers);

		if (upgrade.MaxLevel > 1)
		{
			builder.Append('\n');
			builder.Append($"Up to {upgrade.MaxLevel} times.");
		}

		return builder.ToString();
	}

	public static string ForItem(ItemMetadata? item, int stacks = 1)
	{
		if (item?.GrantedUpgrades == null || item.GrantedUpgrades.Length == 0) return string.Empty;
		if (stacks < 1) stacks = 1;

		var builder = new StringBuilder(64);
		int written = 0;

		foreach (string upgradeId in item.GrantedUpgrades)
		{
			if (string.IsNullOrWhiteSpace(upgradeId)) continue;
			if (!TryResolveUpgrade(upgradeId, out UpgradeMetadata? upgrade)) continue;
			if (upgrade.Modifiers == null || upgrade.Modifiers.Count == 0) continue;

			if (written > 0) builder.Append('\n');
			Append(builder, upgrade.Modifiers, stacks);
			written++;
		}

		if (written == 0) return string.Empty;
		if (stacks > 1)
		{
			builder.Append('\n');
			builder.Append($"At {stacks} copies.");
		}

		return builder.ToString();
	}

	private static void Append(StringBuilder builder, IReadOnlyList<StatModifierDefinition> modifiers, int multiplier = 1)
	{
		bool first = true;

		foreach (StatModifierDefinition modifier in modifiers)
		{
			if (modifier == null || string.IsNullOrWhiteSpace(modifier.StatKey)) continue;

			float value = modifier.Value * multiplier;
			bool isPercent = IsPercentage(modifier.Type);
			float shown = isPercent ? value * 100f : value;

			if (!first) builder.Append('\n');
			first = false;

			builder.Append(shown >= 0f ? "+" : string.Empty);
			builder.Append(shown.ToString("0.##", CultureInfo.InvariantCulture));
			if (isPercent) builder.Append('%');
			builder.Append(' ');
			builder.Append(Label(modifier.StatKey));
		}
	}

	private static bool IsPercentage(string? type)
	{
		return !string.IsNullOrEmpty(type) &&
			(type.Equals("Percentage", System.StringComparison.OrdinalIgnoreCase) ||
			 type.Equals("Percent", System.StringComparison.OrdinalIgnoreCase) ||
			 type.Equals("AdditivePercent", System.StringComparison.OrdinalIgnoreCase));
	}

	public static string Label(string statKey)
	{
		switch (statKey)
		{
			case "MaxHp": return "Health";
			case "HpRegen": return "Health Regen";
			case "MaxMana": return "Mana";
			case "ManaRegen": return "Mana Regen";
			case "Attack": return "Attack Damage";
			case "AttackSpeed": return "Attack Speed";
			case "AttackDelay": return "Attack Interval";
			case "MovementSpeed": return "Move Speed";
			case "MagicResistance": return "Magic Resist";
			case "CooldownReduction": return "Cooldown Reduction";
			case "LifeSteal": return "Life Steal";
			case "ProcRateMultiplier": return "Proc Rate";
			case "BountyMultiplier": return "Bounty";
			case "CritChance": return "Crit Chance";
			case "CritMultiplier": return "Crit Damage";
			case "SpellPower": return "Spell Power";
			case "HealingMultiplier": return "Healing";
			case "FlatArmorPenetration": return "Armor Penetration";
			case "PercentArmorPenetration": return "Armor Penetration";
			case "FlatMagicPenetration": return "Magic Penetration";
			case "PercentMagicPenetration": return "Magic Penetration";
			default: return statKey;
		}
	}

	private static bool TryResolveUpgrade(string upgradeId, out UpgradeMetadata upgrade)
	{
		upgrade = null!;

		if (GameHost.UpgradeRegistry.TryGetValue(upgradeId, out var direct) && direct != null)
		{
			upgrade = direct;
			return true;
		}

		string alternate = upgradeId.StartsWith("upgrade/", System.StringComparison.OrdinalIgnoreCase)
			? upgradeId["upgrade/".Length..]
			: "upgrade/" + upgradeId;

		if (GameHost.UpgradeRegistry.TryGetValue(alternate, out var indirect) && indirect != null)
		{
			upgrade = indirect;
			return true;
		}

		return false;
	}
}
