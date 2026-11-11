using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;
using Realm.Client.Core;
using Realm.Client.Services;
using Realm.Client.Utils;
using Realm.Ecs;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Stats;
using Realm.Shared.Metadata;

namespace Realm.Client.UI.InGame;

/// <summary>
///     The stat breakdown a single selected unit shows: the six core attributes, its vitals, the derived combat
///     stats the authority computed for it, the status effects it currently carries and the upgrades its owner
///     has researched. Values are read from the replicated world where the authority published them, so the panel
///     describes the unit as it is at the moment it is drawn rather than the template it was authored from.
/// </summary>
public class UnitStatsPanel
{
	private const int ValueFontSize = 13;
	private const int IconFontSize = 12;
	private const int HeaderFontSize = 11;
	private const float IconColumnWidth = 18f;
	private const int SectionSpacing = 4;
	private const int SectionSeparation = 1;
	private const int RowSeparation = 4;
	private const int ColumnSeparation = 10;
	private const int RowsPerColumn = 6;
	private const float HeaderTitleMinimumWidth = 80f;
	private const float StripIconSize = 22f;
	private const ulong SignatureSeed = 1469598103934665603UL;
	private const ulong SignaturePrime = 1099511628211UL;
	private const int MaximumReadoutRows = 8;

	private static readonly string[] ReadoutNameKeys = BuildReadoutKeys("name");
	private static readonly string[] ReadoutValueKeys = BuildReadoutKeys("value");

	private static readonly StatId StrengthStat = new(AttributeType.Strength);
	private static readonly StatId AgilityStat = new(AttributeType.Agility);
	private static readonly StatId VitalityStat = new(AttributeType.Vitality);
	private static readonly StatId IntelligenceStat = new(AttributeType.Intelligence);
	private static readonly StatId WisdomStat = new(AttributeType.Wisdom);
	private static readonly StatId FortuneStat = new(AttributeType.Fortune);

	private static readonly StatId AttackStat = new(CombatStatType.Attack);
	private static readonly StatId ArmorStat = new(CombatStatType.Armor);
	private static readonly StatId MagicResistanceStat = new(CombatStatType.MagicResistance);
	private static readonly StatId MovementSpeedStat = new(CombatStatType.MovementSpeed);
	private static readonly StatId SpellPowerStat = new(CombatStatType.SpellPower);
	private static readonly StatId TenacityStat = new(CombatStatType.Tenacity);
	private static readonly StatId CooldownReductionStat = new(CombatStatType.CooldownReduction);
	private static readonly StatId LifeStealStat = new(CombatStatType.LifeSteal);
	private static readonly StatId EvasionStat = new(CombatStatType.Evasion);
	private static readonly StatId CritChanceStat = new(StatKeys.CritChance);
	private static readonly StatId CritMultiplierStat = new(StatKeys.CritMultiplier);
	private static readonly StatId FlatArmorPenetrationStat = new(CombatStatType.FlatArmorPenetration);
	private static readonly StatId PercentArmorPenetrationStat = new(CombatStatType.PercentArmorPenetration);
	private static readonly StatId FlatMagicPenetrationStat = new(CombatStatType.FlatMagicPenetration);
	private static readonly StatId PercentMagicPenetrationStat = new(CombatStatType.PercentMagicPenetration);
	private static readonly StatId AttackDelayStat = new(StatKeys.AttackDelay);

	private static string[] BuildReadoutKeys(string field)
	{
		var keys = new string[MaximumReadoutRows];
		for (int index = 0; index < keys.Length; index++)
		{
			keys[index] = $"readout.{index}.{field}";
		}

		return keys;
	}

	private readonly VBoxContainer _root;
	private readonly Label _messageLabel;
	private readonly VBoxContainer _content;
	private readonly HFlowContainer _columns;
	private readonly List<StatRow> _orderedRows = new();
	private readonly VBoxContainer _footer;
	private readonly Label _stateLabel;
	private readonly Label _descriptionLabel;
	private readonly StatRow _strengthRow;
	private readonly StatRow _agilityRow;
	private readonly StatRow _vitalityRow;
	private readonly StatRow _intelligenceRow;
	private readonly StatRow _wisdomRow;
	private readonly StatRow _fortuneRow;
	private readonly StatRow _experienceRow;
	private readonly StatRow _levelRow;
	private readonly StatRow _attackDamageRow;
	private readonly StatRow _damagePerSecondRow;
	private readonly StatRow _attackRangeRow;
	private readonly StatRow _attackSpeedRow;
	private readonly StatRow _moveSpeedRow;
	private readonly StatRow _armorRow;
	private readonly StatRow _magicResistanceRow;
	private readonly StatRow _armorPenetrationRow;
	private readonly StatRow _magicPenetrationRow;
	private readonly StatRow _criticalHitRow;
	private readonly StatRow _spellPowerRow;
	private readonly StatRow _evasionRow;
	private readonly StatRow _lifeStealRow;
	private readonly StatRow _cooldownReductionRow;
	private readonly StatRow _tenacityRow;
	private readonly StatRow _damageTypeRow;
	private readonly VBoxContainer _readingsSection;
	private readonly List<ReadingRowControl> _readingRows = new();
	private readonly HBoxContainer _statusEffectsStrip;
	private readonly VBoxContainer _statusEffectsSection;
	private readonly HBoxContainer _upgradesStrip;
	private readonly VBoxContainer _upgradesSection;

	private readonly List<string> _statusEffectIds = new();
	private int _lastColumnLayoutMask = -1;
	private ulong _lastSignature;
	private ulong _lastStatusEffectsSignature;
	private ulong _lastUpgradesSignature;
	private ulong _lastReadingsSignature;

	public UnitStatsPanel()
	{
		_root = new VBoxContainer();
		_root.Name = "UnitStatsPanel_VBoxContainer";
		_root.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		_root.SizeFlagsVertical = Control.SizeFlags.ShrinkBegin;
		_root.AddThemeConstantOverride("separation", SectionSpacing);

		_messageLabel = new Label();
		_messageLabel.Name = "UnitStatsMessage_Label";
		_messageLabel.Visible = false;
		_messageLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		_messageLabel.CustomMinimumSize = new Vector2(280f, 0f);
		_messageLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		_messageLabel.AddThemeFontSizeOverride("font_size", ValueFontSize);
		_messageLabel.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		_root.AddChild(_messageLabel);

		_content = new VBoxContainer();
		_content.Name = "UnitStatsContent_VBoxContainer";
		_content.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		_content.SizeFlagsVertical = Control.SizeFlags.ShrinkBegin;
		_content.AddThemeConstantOverride("separation", SectionSpacing);
		_root.AddChild(_content);

		_columns = new HFlowContainer();
		_columns.Name = "UnitStatsColumns_HFlowContainer";
		_columns.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		_columns.SizeFlagsVertical = Control.SizeFlags.ShrinkBegin;
		_columns.AddThemeConstantOverride("h_separation", ColumnSeparation);
		_columns.AddThemeConstantOverride("v_separation", SectionSpacing);
		_content.AddChild(_columns);

		_strengthRow = AddStatRow(UnicodeIcons.HAND_FIST, "Strength", AttributeTooltip(AttributeType.Strength));
		_agilityRow = AddStatRow(UnicodeIcons.WIND, "Agility", AttributeTooltip(AttributeType.Agility));
		_vitalityRow = AddStatRow(UnicodeIcons.HAND_HEART, "Vitality", AttributeTooltip(AttributeType.Vitality));
		_intelligenceRow = AddStatRow(UnicodeIcons.BRAIN, "Intelligence", AttributeTooltip(AttributeType.Intelligence));
		_wisdomRow = AddStatRow(UnicodeIcons.LIGHTBULB, "Wisdom", AttributeTooltip(AttributeType.Wisdom));
		_fortuneRow = AddStatRow(UnicodeIcons.DICE, "Fortune", AttributeTooltip(AttributeType.Fortune));

		_experienceRow = AddStatRow(UnicodeIcons.STAR, "Experience", "Experience this unit has accumulated toward its next level.");
		_levelRow = AddStatRow(UnicodeIcons.STAIRS, "Level", "Level this unit has reached.");

		_attackDamageRow = AddStatRow(UnicodeIcons.BURST, "Attack Damage", "Damage this unit deals with one attack, including the bonus from its Strength.");
		_damagePerSecondRow = AddStatRow(UnicodeIcons.EXPLOSION, "Damage Per Second", "Damage this unit deals per second while attacking continuously.");
		_attackRangeRow = AddStatRow(UnicodeIcons.CROSSHAIRS, "Attack Range", "Distance at which this unit can attack.");
		_attackSpeedRow = AddStatRow(UnicodeIcons.GAUGE, "Attack Speed", "Attacks this unit makes per second, including the haste from its Agility.");
		_moveSpeedRow = AddStatRow(UnicodeIcons.SHOE_PRINTS, "Move Speed", "Distance this unit covers per second.");
		_criticalHitRow = AddStatRow(UnicodeIcons.STAR_HALF, "Critical Hit", "Chance for an attack to critically strike, and the damage a critical strike deals.");
		_damageTypeRow = AddStatRow(UnicodeIcons.FIRE, "Damage Type", "Damage type this unit's attacks are dealt as.");

		_armorPenetrationRow = AddStatRow(UnicodeIcons.BULLSEYE, "Armor Penetration", "Armor points this unit's physical attacks ignore, as a flat amount and a percentage.");
		_magicPenetrationRow = AddStatRow(UnicodeIcons.WAND_SPARKLES, "Magic Penetration", "Magic resistance points this unit's magic damage ignores, as a flat amount and a percentage.");
		_spellPowerRow = AddStatRow(UnicodeIcons.WAND_MAGIC, "Spell Power", "Ability damage multiplier.");
		_lifeStealRow = AddStatRow(UnicodeIcons.HAND_MEDICAL, "Life Steal", "Share of the damage dealt that is returned to this unit as health.");
		_cooldownReductionRow = AddStatRow(UnicodeIcons.HOURGLASS_HALF, "Cooldown Reduction", "Share of an ability's cooldown that is removed.");
		_armorRow = AddStatRow(UnicodeIcons.SHIELD, "Armor", "Armor points that reduce incoming physical damage.");
		_magicResistanceRow = AddStatRow(UnicodeIcons.WIZARD_HAT, "Magic Resistance", "Magic resistance points that reduce incoming magic damage.");
		_evasionRow = AddStatRow(UnicodeIcons.FEATHER_POINTED, "Evasion", "Chance to avoid an incoming attack entirely.");
		_tenacityRow = AddStatRow(UnicodeIcons.SHIELD_HEART, "Tenacity", "Share of an incoming crowd-control effect's duration that is resisted.");

		_footer = new VBoxContainer();
		_footer.Name = "UnitStatsFooter_VBoxContainer";
		_footer.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		_footer.SizeFlagsVertical = Control.SizeFlags.ShrinkBegin;
		_footer.AddThemeConstantOverride("separation", SectionSpacing);
		_content.AddChild(_footer);

		_readingsSection = new VBoxContainer();
		_readingsSection.Name = "UnitStatsReadings_VBoxContainer";
		_readingsSection.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		_readingsSection.AddThemeConstantOverride("separation", SectionSeparation);
		_readingsSection.Visible = false;
		_footer.AddChild(_readingsSection);
		AddSectionHeader(_readingsSection, UnicodeIcons.LIST, "Readings");

		_statusEffectsSection = new VBoxContainer();
		_statusEffectsSection.Name = "UnitStatsStatusEffects_VBoxContainer";
		_statusEffectsSection.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		_statusEffectsSection.AddThemeConstantOverride("separation", SectionSeparation);
		_footer.AddChild(_statusEffectsSection);
		AddSectionHeader(_statusEffectsSection, UnicodeIcons.CIRCLE_EXCLAMATION, "Status Effects");
		_statusEffectsStrip = CreateIconStrip("UnitStatsStatusEffects_HBoxContainer");
		_statusEffectsSection.AddChild(_statusEffectsStrip);

		_upgradesSection = new VBoxContainer();
		_upgradesSection.Name = "UnitStatsUpgrades_VBoxContainer";
		_upgradesSection.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		_upgradesSection.AddThemeConstantOverride("separation", SectionSeparation);
		_footer.AddChild(_upgradesSection);
		AddSectionHeader(_upgradesSection, UnicodeIcons.CIRCLE_ARROW_UP, "Upgrades");
		_upgradesStrip = CreateIconStrip("UnitStatsUpgrades_HBoxContainer");
		_upgradesSection.AddChild(_upgradesStrip);

		_stateLabel = new Label();
		_stateLabel.Name = "UnitStatsState_Label";
		_stateLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		_stateLabel.CustomMinimumSize = new Vector2(280f, 0f);
		_stateLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		_stateLabel.AddThemeFontSizeOverride("font_size", ValueFontSize);
		_stateLabel.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		_footer.AddChild(_stateLabel);

		_descriptionLabel = new Label();
		_descriptionLabel.Name = "UnitStatsDescription_Label";
		_descriptionLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		_descriptionLabel.CustomMinimumSize = new Vector2(280f, 0f);
		_descriptionLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		_descriptionLabel.AddThemeFontSizeOverride("font_size", ValueFontSize);
		_descriptionLabel.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		_footer.AddChild(_descriptionLabel);

		ShowMessage(string.Empty);
	}

	public VBoxContainer Root => _root;

	public void ShowMessage(string message)
	{
		_messageLabel.Text = message;
		_messageLabel.Visible = !string.IsNullOrEmpty(message);
		_content.Visible = string.IsNullOrEmpty(message);
	}

	public void UpdateSingleSelection(SelectedUnitInfo info, bool forceRefresh)
	{
		ShowMessage(string.Empty);

		var world = GameContext.World;
		if (world == null || info == null || !world.IsAlive(info.Entity))
		{
			return;
		}

		Entity entity = info.Entity;
		bool hasExperience = world.Has<Experience>(entity);
		bool hasLevel = world.Has<Level>(entity);
		bool hasAttack = world.TryGet(entity, out Attack attack);
		world.TryGet(entity, out Stats stats);
		world.TryGet(entity, out UnitAttributes attributes);
		world.TryGet(entity, out Experience experience);
		world.TryGet(entity, out Level level);
		world.TryGet(entity, out Buffs buffs);

		UnitMetadata? metadata = null;
		GameHost.TryGetUnitOrBuildingMetadata(info.UnitId, out metadata);
		string damageType = metadata != null && !string.IsNullOrEmpty(metadata.DamageType) ? metadata.DamageType : string.Empty;
		float experiencePerLevel = metadata != null ? metadata.ExperiencePerLevel : 0f;

		_statusEffectIds.Clear();
		buffs.CopyKeys(_statusEffectIds);

		// A map's inspector rows live in the presentation store rather than the ECS, so they are refreshed before the
		// stat signature short-circuits: a change to a published row is not a change to the unit's stats, and a panel
		// that only redrew when the stats moved would leave its readouts frozen at whatever they were when it opened.
		UpdateReadings(entity, forceRefresh);

		ulong signature = ComputeSignature(entity, stats, attributes, experience, level, attack, hasExperience, hasLevel, hasAttack,
			damageType, experiencePerLevel, info.StateText, info.Description, info.Player, metadata);
		if (!forceRefresh && signature == _lastSignature)
		{
			return;
		}

		_lastSignature = signature;

		_strengthRow.SetValue(FormatAmount(ReadAttribute(stats, attributes, StrengthStat, AttributeType.Strength)));
		_agilityRow.SetValue(FormatAmount(ReadAttribute(stats, attributes, AgilityStat, AttributeType.Agility)));
		_vitalityRow.SetValue(FormatAmount(ReadAttribute(stats, attributes, VitalityStat, AttributeType.Vitality)));
		_intelligenceRow.SetValue(FormatAmount(ReadAttribute(stats, attributes, IntelligenceStat, AttributeType.Intelligence)));
		_wisdomRow.SetValue(FormatAmount(ReadAttribute(stats, attributes, WisdomStat, AttributeType.Wisdom)));
		_fortuneRow.SetValue(FormatAmount(ReadAttribute(stats, attributes, FortuneStat, AttributeType.Fortune)));

		_experienceRow.SetValue(FormatExperience(experience.Current, level.Value, experiencePerLevel, hasLevel));
		_levelRow.SetValue(level.Value.ToString(CultureInfo.InvariantCulture));

		float attackDamage = ReadStat(stats, AttackStat);
		_attackDamageRow.SetValue(FormatAmount(attackDamage));

		float attackDelay = ReadStat(stats, AttackDelayStat);
		float attacksPerSecond = attackDelay > 0f ? 1f / attackDelay : 0f;
		_attackSpeedRow.SetValue(FormatAmount(attacksPerSecond));
		_damagePerSecondRow.SetValue(FormatAmount(attackDamage * attacksPerSecond));
		_attackRangeRow.SetValue(FormatAmount(attack.Range));

		ApplyRowVisibility(hasExperience, hasLevel, hasAttack);

		_moveSpeedRow.SetValue(FormatAmount(ReadStat(stats, MovementSpeedStat)));
		_armorRow.SetValue(FormatAmount(ReadStat(stats, ArmorStat)));
		_magicResistanceRow.SetValue(FormatAmount(ReadStat(stats, MagicResistanceStat)));

		_armorPenetrationRow.SetValue($"+{FormatAmount(ReadStat(stats, FlatArmorPenetrationStat))}   {FormatPercent(ReadStat(stats, PercentArmorPenetrationStat))}");
		_magicPenetrationRow.SetValue($"+{FormatAmount(ReadStat(stats, FlatMagicPenetrationStat))}   {FormatPercent(ReadStat(stats, PercentMagicPenetrationStat))}");

		_criticalHitRow.SetValue($"{FormatPercent(ReadStat(stats, CritChanceStat))}   ×{FormatAmount(ReadStat(stats, CritMultiplierStat))}");
		_spellPowerRow.SetValue($"×{FormatAmount(ReadStat(stats, SpellPowerStat))}");
		_evasionRow.SetValue(FormatPercent(ReadStat(stats, EvasionStat)));
		_lifeStealRow.SetValue(FormatPercent(ReadStat(stats, LifeStealStat)));
		_cooldownReductionRow.SetValue(FormatPercent(ReadStat(stats, CooldownReductionStat)));
		_tenacityRow.SetValue(FormatPercent(ReadStat(stats, TenacityStat)));

		_damageTypeRow.SetValue(string.IsNullOrEmpty(damageType) ? "-" : TranslationServer.Translate(ToDisplayName(damageType)));

		ApplyText(_stateLabel, info.StateText);
		_stateLabel.Visible = !string.IsNullOrEmpty(_stateLabel.Text);
		string description = string.IsNullOrEmpty(info.Description)
			? string.Empty
			: TooltipTemplate.Resolve(TranslationServer.Translate(info.Description), TooltipContext.ForUnit(entity));
		ApplyText(_descriptionLabel, description);

		UpdateStatusEffectStrip();
		UpdateUpgradeStrip(info, metadata);
	}

	private ulong ComputeSignature(Entity entity, in Stats stats, in UnitAttributes attributes,
		in Experience experience, in Level level, in Attack attack, bool hasExperience, bool hasLevel, bool hasAttack,
		string damageType, float experiencePerLevel, string? stateText, string? description, int playerIndex, UnitMetadata? metadata)
	{
		ulong signature = SignatureSeed;
		signature = Mix(signature, entity.Id);
		signature = Mix(signature, hasExperience ? 1 : 0);
		signature = Mix(signature, hasLevel ? 1 : 0);
		signature = Mix(signature, hasAttack ? 1 : 0);
		signature = Mix(signature, attack.Range);
		signature = Mix(signature, ReadAttribute(stats, attributes, StrengthStat, AttributeType.Strength));
		signature = Mix(signature, ReadAttribute(stats, attributes, AgilityStat, AttributeType.Agility));
		signature = Mix(signature, ReadAttribute(stats, attributes, VitalityStat, AttributeType.Vitality));
		signature = Mix(signature, ReadAttribute(stats, attributes, IntelligenceStat, AttributeType.Intelligence));
		signature = Mix(signature, ReadAttribute(stats, attributes, WisdomStat, AttributeType.Wisdom));
		signature = Mix(signature, ReadAttribute(stats, attributes, FortuneStat, AttributeType.Fortune));
		signature = Mix(signature, experience.Current);
		signature = Mix(signature, level.Value);
		signature = Mix(signature, experiencePerLevel);
		signature = Mix(signature, ReadStat(stats, AttackStat));
		signature = Mix(signature, ReadStat(stats, AttackDelayStat));
		signature = Mix(signature, ReadStat(stats, MovementSpeedStat));
		signature = Mix(signature, ReadStat(stats, ArmorStat));
		signature = Mix(signature, ReadStat(stats, MagicResistanceStat));
		signature = Mix(signature, ReadStat(stats, FlatArmorPenetrationStat));
		signature = Mix(signature, ReadStat(stats, PercentArmorPenetrationStat));
		signature = Mix(signature, ReadStat(stats, FlatMagicPenetrationStat));
		signature = Mix(signature, ReadStat(stats, PercentMagicPenetrationStat));
		signature = Mix(signature, ReadStat(stats, CritChanceStat));
		signature = Mix(signature, ReadStat(stats, CritMultiplierStat));
		signature = Mix(signature, ReadStat(stats, SpellPowerStat));
		signature = Mix(signature, ReadStat(stats, EvasionStat));
		signature = Mix(signature, ReadStat(stats, LifeStealStat));
		signature = Mix(signature, ReadStat(stats, CooldownReductionStat));
		signature = Mix(signature, ReadStat(stats, TenacityStat));
		signature = Mix(signature, damageType);
		signature = Mix(signature, stateText);
		signature = Mix(signature, description);
		signature = Mix(signature, playerIndex);
		signature = Mix(signature, metadata?.TemplateID);
		signature = Mix(signature, metadata?.Upgrades == null ? 0 : metadata.Upgrades.Length);

		for (int index = 0; index < _statusEffectIds.Count; index++)
		{
			signature = Mix(signature, _statusEffectIds[index]);
		}

		if (metadata?.Upgrades != null)
		{
			for (int index = 0; index < metadata.Upgrades.Length; index++)
			{
				signature = Mix(signature, metadata.Upgrades[index]);
				signature = Mix(signature, ReadUpgradeLevel(playerIndex, metadata.Upgrades[index]));
			}
		}

		return signature;
	}

	private void UpdateStatusEffectStrip()
	{
		ulong signature = SignatureSeed;
		for (int index = 0; index < _statusEffectIds.Count; index++)
		{
			signature = Mix(signature, _statusEffectIds[index]);
		}

		if (signature == _lastStatusEffectsSignature && _statusEffectsStrip.GetChildCount() > 0)
		{
			return;
		}

		_lastStatusEffectsSignature = signature;
		ClearStrip(_statusEffectsStrip);

		for (int index = 0; index < _statusEffectIds.Count; index++)
		{
			string statusEffectId = _statusEffectIds[index];
			if (string.IsNullOrEmpty(statusEffectId)) continue;
			_statusEffectsStrip.AddChild(CreateGlyphIcon(StatusEffectGlyph(statusEffectId), TranslationServer.Translate(ToDisplayName(statusEffectId))));
		}

		_statusEffectsSection.Visible = _statusEffectsStrip.GetChildCount() > 0;
	}

	private void UpdateUpgradeStrip(SelectedUnitInfo info, UnitMetadata? metadata)
	{
		if (metadata?.Upgrades == null || metadata.Upgrades.Length == 0)
		{
			_upgradesSection.Visible = false;
			ClearStrip(_upgradesStrip);
			_lastUpgradesSignature = 0UL;
			return;
		}

		ulong signature = SignatureSeed;
		for (int index = 0; index < metadata.Upgrades.Length; index++)
		{
			signature = Mix(signature, metadata.Upgrades[index]);
			signature = Mix(signature, ReadUpgradeLevel(info.Player, metadata.Upgrades[index]));
		}

		if (signature == _lastUpgradesSignature && _upgradesStrip.GetChildCount() > 0)
		{
			_upgradesSection.Visible = true;
			return;
		}

		_lastUpgradesSignature = signature;
		ClearStrip(_upgradesStrip);

		for (int index = 0; index < metadata.Upgrades.Length; index++)
		{
			string upgradeId = metadata.Upgrades[index];
			if (string.IsNullOrEmpty(upgradeId)) continue;

			UpgradeMetadata? upgrade = ResolveUpgrade(upgradeId);
			int upgradeLevel = ReadUpgradeLevel(info.Player, upgradeId);
			string upgradeName = upgrade != null && !string.IsNullOrEmpty(upgrade.Name) ? upgrade.Name : ToDisplayName(upgradeId);
			string tooltip = TranslationServer.Translate(upgradeName);
			if (!string.IsNullOrEmpty(upgrade?.Description)) tooltip += "\n" + TranslationServer.Translate(upgrade.Description);
			if (upgradeLevel > 0) tooltip += $"\n{TranslationServer.Translate("Level")}: {upgradeLevel}";

			string iconPath = upgrade != null && !string.IsNullOrEmpty(upgrade.IconPath) ? upgrade.IconPath : "res://Assets/UI/magic_upgrade_arrow.webp";
			_upgradesStrip.AddChild(CreateTextureIcon(iconPath, tooltip, upgradeLevel > 0));
		}

		_upgradesSection.Visible = _upgradesStrip.GetChildCount() > 0;
	}

	private int ReadUpgradeLevel(int playerIndex, string upgradeId)
	{
		if (playerIndex <= 0 || GameContext.Host == null) return 0;

		UpgradeMetadata? upgrade = ResolveUpgrade(upgradeId);
		string canonicalId = upgrade != null && !string.IsNullOrEmpty(upgrade.TemplateID) ? upgrade.TemplateID : upgradeId;
		return GameContext.Host.GetPlayerTechLevel(playerIndex, canonicalId);
	}

	private static UpgradeMetadata? ResolveUpgrade(string upgradeId)
	{
		if (GameHost.UpgradeRegistry.TryGetValue(upgradeId, out UpgradeMetadata? upgrade)) return upgrade;

		string alternateId = upgradeId.StartsWith("upgrade/", StringComparison.OrdinalIgnoreCase)
			? upgradeId.Substring("upgrade/".Length)
			: "upgrade/" + upgradeId;
		return GameHost.UpgradeRegistry.TryGetValue(alternateId, out upgrade) ? upgrade : null;
	}

	private static float ReadAttribute(in Stats stats, in UnitAttributes attributes, StatId statId, AttributeType attributeType)
	{
		return stats.TryGetValue(statId, out float value) ? value : attributes.Get(attributeType);
	}

	private static float ReadStat(in Stats stats, StatId statId)
	{
		return stats.TryGetValue(statId, out float value) ? value : 0f;
	}

	private static string FormatAmount(float value)
	{
		return value.ToString("0.##", CultureInfo.InvariantCulture);
	}

	private static string FormatPercent(float fraction)
	{
		return (fraction * 100f).ToString("0.#", CultureInfo.InvariantCulture) + "%";
	}

	private static string FormatExperience(float current, int level, float experiencePerLevel, bool hasLevel)
	{
		if (!hasLevel || experiencePerLevel <= 0f)
		{
			return FormatAmount(current);
		}

		int nextLevelStep = level > 0 ? level : 1;
		return $"{FormatAmount(current)} / {FormatAmount(nextLevelStep * experiencePerLevel)}";
	}

	private static string ToDisplayName(string identifier)
	{
		string trimmed = identifier.Contains("upgrade/", StringComparison.Ordinal)
			? identifier.Replace("upgrade/", string.Empty, StringComparison.Ordinal)
			: identifier;
		trimmed = trimmed.Replace('_', ' ');
		if (trimmed.Length == 0) return identifier;
		return char.ToUpperInvariant(trimmed[0]) + trimmed.Substring(1);
	}

	private static string StatusEffectGlyph(string statusEffectId)
	{
		if (statusEffectId.Contains("stun", StringComparison.OrdinalIgnoreCase)) return UnicodeIcons.CIRCLE_DOT;
		if (statusEffectId.Contains("silence", StringComparison.OrdinalIgnoreCase)) return UnicodeIcons.BAN;
		if (statusEffectId.Contains("slow", StringComparison.OrdinalIgnoreCase)) return UnicodeIcons.SNOWFLAKE;
		if (statusEffectId.Contains("root", StringComparison.OrdinalIgnoreCase)) return UnicodeIcons.LINK;
		if (statusEffectId.Contains("burn", StringComparison.OrdinalIgnoreCase)) return UnicodeIcons.FIRE;
		if (statusEffectId.Contains("haste", StringComparison.OrdinalIgnoreCase)) return UnicodeIcons.WIND;
		if (statusEffectId.Contains("run", StringComparison.OrdinalIgnoreCase)) return UnicodeIcons.PERSON_RUNNING;
		return UnicodeIcons.CIRCLE_EXCLAMATION;
	}

	private void UpdateReadings(Entity entity, bool forceRefresh)
	{
		var world = GameContext.World;
		UnitCharges charges = default;
		bool hasCharges = world != null && world.TryGet(entity, out charges) && charges.MaximumCharges > 0;

		float remainingSeconds = hasCharges ? charges.RemainingSecondsToNextCharge() : 0f;
		int wholeRemainingSeconds = (int)MathF.Ceiling(remainingSeconds);

		TooltipValueStore? published = GameContext.RegistryService?.TooltipValues;
		string owner = TooltipValueStore.OwnerKey("unit", entity.Id);
		int publishedRows = 0;

		ulong signature = SignatureSeed;
		signature = Mix(signature, hasCharges ? 1 : 0);
		if (hasCharges)
		{
			signature = Mix(signature, charges.Charges);
			signature = Mix(signature, charges.MaximumCharges);
			signature = Mix(signature, wholeRemainingSeconds);
		}

		for (int index = 0; index < MaximumReadoutRows; index++)
		{
			if (published == null || !published.TryGetText(owner, ReadoutNameKeys[index], out string name) || string.IsNullOrWhiteSpace(name))
			{
				signature = Mix(signature, 0);
				continue;
			}

			published.TryGetText(owner, ReadoutValueKeys[index], out string value);
			signature = Mix(signature, name);
			signature = Mix(signature, value);
			publishedRows++;
		}

		signature = Mix(signature, publishedRows);

		if (!forceRefresh && signature == _lastReadingsSignature) return;
		_lastReadingsSignature = signature;

		int visibleRows = publishedRows + (hasCharges ? 2 : 0);
		EnsureReadingRows(visibleRows);

		int rowIndex = 0;
		for (int index = 0; index < MaximumReadoutRows; index++)
		{
			if (published == null || !published.TryGetText(owner, ReadoutNameKeys[index], out string name) || string.IsNullOrWhiteSpace(name)) continue;

			published.TryGetText(owner, ReadoutValueKeys[index], out string value);
			_readingRows[rowIndex].Root.Visible = true;
			_readingRows[rowIndex].Set(TranslationServer.Translate(name), TranslationServer.Translate(value));
			rowIndex++;
		}

		if (hasCharges)
		{
			_readingRows[rowIndex].Root.Visible = true;
			_readingRows[rowIndex].Set(TranslationServer.Translate("Charges"), $"{charges.Charges} / {charges.MaximumCharges}");
			rowIndex++;

			_readingRows[rowIndex].Root.Visible = true;
			_readingRows[rowIndex].Set(
				TranslationServer.Translate("Next charge"),
				wholeRemainingSeconds <= 0
					? TranslationServer.Translate("Ready")
					: string.Format(TranslationServer.Translate("{0} s"), wholeRemainingSeconds.ToString(CultureInfo.InvariantCulture)));
			rowIndex++;
		}

		for (int index = rowIndex; index < _readingRows.Count; index++)
		{
			_readingRows[index].Root.Visible = false;
		}

		_readingsSection.Visible = visibleRows > 0;
	}

	private void EnsureReadingRows(int count)
	{
		while (_readingRows.Count < count)
		{
			var row = new ReadingRowControl();
			_readingsSection.AddChild(row.Root);
			_readingRows.Add(row);
		}
	}

	private static ulong Mix(ulong signature, float value)
	{
		return (signature ^ BitConverter.SingleToUInt32Bits(value)) * SignaturePrime;
	}

	private static ulong Mix(ulong signature, int value)
	{
		return (signature ^ (uint)value) * SignaturePrime;
	}

	private static ulong Mix(ulong signature, string? text)
	{
		return (signature ^ (uint)(text?.GetHashCode() ?? 0)) * SignaturePrime;
	}

	private static void ApplyIconFont(Label label)
	{
		if (UIStyle.FontAwesome != null) label.AddThemeFontOverride("font", UIStyle.FontAwesome);
	}

	private static void ApplyText(Label label, string? text)
	{
		string resolved = text ?? string.Empty;
		if (label.Text != resolved) label.Text = resolved;
	}

	private static void ClearStrip(HBoxContainer strip)
	{
		foreach (Node child in strip.GetChildren())
		{
			strip.RemoveChild(child);
			child.QueueFree();
		}
	}

	private static string AttributeTooltip(AttributeType attributeType)
	{
		return attributeType switch
		{
			AttributeType.Strength => TranslationServer.Translate("Attack damage, Tenacity (Crowd-control resistance), Armor (physical damage reduction)."),
			AttributeType.Agility => TranslationServer.Translate("Attack Speed, Movement Speed, Cooldown reduction."),
			AttributeType.Vitality => TranslationServer.Translate("Max Health, Max Mana, Life steal."),
			AttributeType.Intelligence => TranslationServer.Translate("Ability damage, Mana regeneration, Magic resistance."),
			AttributeType.Wisdom => TranslationServer.Translate("Magic penetration, Healing power / HP regen rate, Critical multiplier."),
			AttributeType.Fortune => TranslationServer.Translate("Critical chance, Evasion, Proc rates (on-hit effects), Bounty yield (XP gain ratio)."),
			_ => string.Empty
		};
	}

	private static VBoxContainer CreateColumn(string nodeName)
	{
		var column = new VBoxContainer();
		column.Name = nodeName;
		column.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
		column.SizeFlagsVertical = Control.SizeFlags.ShrinkBegin;
		column.AddThemeConstantOverride("separation", SectionSeparation);
		return column;
	}

	private static void AddSectionHeader(VBoxContainer column, string icon, string title)
	{
		var header = new HBoxContainer();
		header.Name = "UnitStatsHeader_HBoxContainer";
		header.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		header.AddThemeConstantOverride("separation", RowSeparation);

		var iconLabel = new Label();
		iconLabel.Name = "UnitStatsHeaderIcon_Label";
		iconLabel.Text = icon;
		iconLabel.CustomMinimumSize = new Vector2(IconColumnWidth, 0f);
		iconLabel.VerticalAlignment = VerticalAlignment.Center;
		iconLabel.MouseFilter = Control.MouseFilterEnum.Pass;
		ApplyIconFont(iconLabel);
		iconLabel.AddThemeFontSizeOverride("font_size", HeaderFontSize);
		iconLabel.AddThemeColorOverride("font_color", UIStyle.ColorBronze);
		header.AddChild(iconLabel);

		var titleLabel = new Label();
		titleLabel.Name = "UnitStatsHeaderTitle_Label";
		titleLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		titleLabel.CustomMinimumSize = new Vector2(HeaderTitleMinimumWidth, 0f);
		titleLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		titleLabel.VerticalAlignment = VerticalAlignment.Center;
		UIStyle.ApplyTitle(titleLabel, title, HeaderFontSize);
		titleLabel.HorizontalAlignment = HorizontalAlignment.Left;
		header.AddChild(titleLabel);

		column.AddChild(header);
	}

	private StatRow AddStatRow(string icon, string name, string tooltip)
	{
		var row = new StatRow(icon, name, tooltip);
		_orderedRows.Add(row);
		return row;
	}

	private void ApplyRowVisibility(bool hasExperience, bool hasLevel, bool hasAttack)
	{
		_experienceRow.Root.Visible = hasExperience;
		_levelRow.Root.Visible = hasLevel;
		_damagePerSecondRow.Root.Visible = hasAttack;
		_attackRangeRow.Root.Visible = hasAttack;

		int layoutMask = (hasExperience ? 1 : 0) | (hasLevel ? 2 : 0) | (hasAttack ? 4 : 0);
		EnsureColumnLayout(layoutMask);
	}

	private void EnsureColumnLayout(int layoutMask)
	{
		if (layoutMask == _lastColumnLayoutMask && _columns.GetChildCount() > 0) return;

		_lastColumnLayoutMask = layoutMask;

		for (int index = 0; index < _orderedRows.Count; index++)
		{
			StatRow row = _orderedRows[index];
			row.Root.GetParent()?.RemoveChild(row.Root);
		}

		for (int index = _columns.GetChildCount() - 1; index >= 0; index--)
		{
			Node column = _columns.GetChild(index);
			_columns.RemoveChild(column);
			column.QueueFree();
		}

		VBoxContainer? currentColumn = null;
		int rowsInColumn = RowsPerColumn;
		for (int index = 0; index < _orderedRows.Count; index++)
		{
			StatRow row = _orderedRows[index];
			if (!row.Root.Visible) continue;

			if (currentColumn == null || rowsInColumn >= RowsPerColumn)
			{
				currentColumn = CreateColumn("UnitStatsColumn_VBoxContainer");
				_columns.AddChild(currentColumn);
				rowsInColumn = 0;
			}

			currentColumn.AddChild(row.Root);
			rowsInColumn++;
		}
	}

	private static HBoxContainer CreateIconStrip(string nodeName)
	{
		var strip = new HBoxContainer();
		strip.Name = nodeName;
		strip.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		strip.AddThemeConstantOverride("separation", 4);
		return strip;
	}

	private static Control CreateGlyphIcon(string glyph, string tooltip)
	{
		var label = new Label();
		label.Name = "UnitStatsStatusEffect_Label";
		label.Text = glyph;
		label.TooltipText = tooltip;
		label.MouseFilter = Control.MouseFilterEnum.Pass;
		label.CustomMinimumSize = new Vector2(StripIconSize, StripIconSize);
		label.HorizontalAlignment = HorizontalAlignment.Center;
		label.VerticalAlignment = VerticalAlignment.Center;
		ApplyIconFont(label);
		label.AddThemeFontSizeOverride("font_size", IconFontSize);
		label.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		return label;
	}

	private static Control CreateTextureIcon(string iconPath, string tooltip, bool researched)
	{
		var icon = new TextureRect();
		icon.Name = "UnitStatsUpgrade_TextureRect";
		icon.Texture = RtexIconLoader.Load(iconPath);
		icon.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
		icon.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
		icon.TooltipText = tooltip;
		icon.MouseFilter = Control.MouseFilterEnum.Pass;
		icon.CustomMinimumSize = new Vector2(StripIconSize, StripIconSize);
		icon.Modulate = researched ? Colors.White : new Color(1f, 1f, 1f, 0.55f);
		return icon;
	}

	private class ReadingRowControl
	{
		private readonly Label _valueLabel;
		private readonly Label _nameLabel;

		public ReadingRowControl()
		{
			Root = new HBoxContainer();
			Root.Name = "UnitStatsReadingRow_HBoxContainer";
			Root.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
			Root.AddThemeConstantOverride("separation", RowSeparation);
			Root.MouseFilter = Control.MouseFilterEnum.Pass;

			var iconLabel = new Label();
			iconLabel.Name = "UnitStatsReadingRowIcon_Label";
			iconLabel.Text = UnicodeIcons.CIRCLE;
			iconLabel.CustomMinimumSize = new Vector2(IconColumnWidth, 0f);
			iconLabel.VerticalAlignment = VerticalAlignment.Center;
			iconLabel.MouseFilter = Control.MouseFilterEnum.Pass;
			ApplyIconFont(iconLabel);
			iconLabel.AddThemeFontSizeOverride("font_size", IconFontSize);
			iconLabel.AddThemeColorOverride("font_color", UIStyle.ColorBronze);
			Root.AddChild(iconLabel);

			_nameLabel = new Label();
			_nameLabel.Name = "UnitStatsReadingRowName_Label";
			_nameLabel.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
			_nameLabel.VerticalAlignment = VerticalAlignment.Center;
			_nameLabel.MouseFilter = Control.MouseFilterEnum.Pass;
			_nameLabel.AddThemeFontSizeOverride("font_size", ValueFontSize);
			_nameLabel.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
			Root.AddChild(_nameLabel);

			_valueLabel = new Label();
			_valueLabel.Name = "UnitStatsReadingRowValue_Label";
			_valueLabel.VerticalAlignment = VerticalAlignment.Center;
			_valueLabel.MouseFilter = Control.MouseFilterEnum.Pass;
			_valueLabel.AddThemeFontSizeOverride("font_size", ValueFontSize);
			_valueLabel.AddThemeColorOverride("font_color", UIStyle.ColorGold);
			Root.AddChild(_valueLabel);
		}

		public HBoxContainer Root { get; }

		public void Set(string label, string value)
		{
			ApplyText(_nameLabel, label);
			ApplyText(_valueLabel, value);
			_valueLabel.Visible = !string.IsNullOrEmpty(value);
		}
	}

	private class StatRow
	{
		private readonly Label _valueLabel;

		public StatRow(string icon, string name, string tooltip)
		{
			Root = new HBoxContainer();
			Root.Name = "UnitStatsRow_HBoxContainer";
			Root.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
			Root.AddThemeConstantOverride("separation", RowSeparation);
			Root.MouseFilter = Control.MouseFilterEnum.Pass;
			Root.TooltipText = tooltip;

			var iconLabel = new Label();
			iconLabel.Name = "UnitStatsRowIcon_Label";
			iconLabel.Text = icon;
			iconLabel.CustomMinimumSize = new Vector2(IconColumnWidth, 0f);
			iconLabel.VerticalAlignment = VerticalAlignment.Center;
			iconLabel.MouseFilter = Control.MouseFilterEnum.Pass;
			ApplyIconFont(iconLabel);
			iconLabel.AddThemeFontSizeOverride("font_size", IconFontSize);
			iconLabel.AddThemeColorOverride("font_color", UIStyle.ColorBronze);
			Root.AddChild(iconLabel);

			var nameLabel = new Label();
			nameLabel.Name = "UnitStatsRowName_Label";
			nameLabel.Text = TranslationServer.Translate(name);
			nameLabel.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
			nameLabel.VerticalAlignment = VerticalAlignment.Center;
			nameLabel.MouseFilter = Control.MouseFilterEnum.Pass;
			nameLabel.AddThemeFontSizeOverride("font_size", ValueFontSize);
			nameLabel.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
			Root.AddChild(nameLabel);

			_valueLabel = new Label();
			_valueLabel.Name = "UnitStatsRowValue_Label";
			_valueLabel.VerticalAlignment = VerticalAlignment.Center;
			_valueLabel.MouseFilter = Control.MouseFilterEnum.Pass;
			_valueLabel.AddThemeFontSizeOverride("font_size", ValueFontSize);
			_valueLabel.AddThemeColorOverride("font_color", UIStyle.ColorGold);
			Root.AddChild(_valueLabel);
		}

		public HBoxContainer Root { get; }

		public void SetValue(string text)
		{
			if (_valueLabel.Text != text) _valueLabel.Text = text;
		}
	}
}
