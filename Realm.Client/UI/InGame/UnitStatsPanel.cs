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
using Realm.Ecs.Components.Resources;
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
	private const int CategoryIconSize = 30;
	private const int CategoryIconFontSize = 18;
	private const int PopupWidth = 250;
	private const int PopupPadding = 12;
	private const float PopupCloseDelaySeconds = 0.18f;
	private const float PopupIconGap = 6f;
	private const float HeaderTitleMinimumWidth = 80f;
	private const float StripIconSize = 22f;
	private const ulong SignatureSeed = 1469598103934665603UL;
	private const ulong SignaturePrime = 1099511628211UL;

	private const string SoldierCapacityKey = "production.soldier_capacity";
	private const string WorkerCapacityKey = "production.worker_capacity";

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

	private readonly VBoxContainer _root;
	private readonly Label _messageLabel;
	private readonly VBoxContainer _content;
	private readonly HBoxContainer _categoryStrip;
	private readonly List<StatCategory> _categories = new();
	private readonly List<StatRow> _popupAttachedRows = new();
	private readonly PopupPanel _categoryPopup;
	private readonly Timer _popupCloseDelay;
	private readonly VBoxContainer _popupRows;
	private readonly Label _popupTitle;
	private readonly VBoxContainer _footer;
	private readonly Label _stateLabel;
	private readonly StatRow _strengthRow;
	private readonly StatRow _agilityRow;
	private readonly StatRow _vitalityRow;
	private readonly StatRow _intelligenceRow;
	private readonly StatRow _wisdomRow;
	private readonly StatRow _fortuneRow;
	private readonly StatRow _experienceRow;
	private readonly StatRow _levelRow;
	private readonly StatRow _attackDamageRow;
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
	private readonly HBoxContainer _buffsStrip;
	private readonly VBoxContainer _buffsSection;
	private readonly HBoxContainer _upgradesStrip;
	private readonly VBoxContainer _upgradesSection;
	private readonly VBoxContainer _capacitySection;
	private readonly StatRow _soldierCapacityRow;
	private readonly StatRow _workerCapacityRow;

	private readonly List<string> _buffIds = new();
	private StatCategory? _popupCategory;
	private ulong _lastSignature;
	private ulong _lastBuffsSignature;
	private ulong _lastUpgradesSignature;

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

		// The panel lists what a player checks at a glance as one icon per category rather than every stat at once: the
		// rows themselves live in the popup a category opens on hover, so the frame stays a short strip instead of a wall
		// of labels. Buffs and upgrades below keep their own sections because they are the map's own statements about the
		// unit rather than the engine's generic stat sheet.
		var statsSection = new VBoxContainer();
		statsSection.Name = "UnitStatsCategories_VBoxContainer";
		statsSection.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		statsSection.AddThemeConstantOverride("separation", SectionSeparation);
		_content.AddChild(statsSection);
		AddSectionHeader(statsSection, UnicodeIcons.TABLE, TranslationServer.Translate("Stats"));

		_categoryStrip = new HBoxContainer();
		_categoryStrip.Name = "UnitStatsCategories_HBoxContainer";
		_categoryStrip.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		_categoryStrip.SizeFlagsVertical = Control.SizeFlags.ShrinkBegin;
		_categoryStrip.AddThemeConstantOverride("separation", RowSeparation);
		statsSection.AddChild(_categoryStrip);

		_categoryPopup = new PopupPanel();
		_categoryPopup.Name = "UnitStatsCategoryPopup_PopupPanel";
		_categoryPopup.AddThemeStyleboxOverride("panel", CreatePopupStyle());
		_root.AddChild(_categoryPopup);
		_root.VisibilityChanged += HideCategoryPopupWhenHidden;

		// Moving from an icon into its popup crosses a small gap, so the close is delayed by a moment and cancelled when
		// the pointer arrives; a row's own tooltip stays readable instead of vanishing the instant the icon is left.
		_popupCloseDelay = new Timer();
		_popupCloseDelay.Name = "UnitStatsCategoryPopupCloseTimer";
		_popupCloseDelay.OneShot = true;
		_popupCloseDelay.WaitTime = PopupCloseDelaySeconds;
		_popupCloseDelay.Timeout += HideCategoryPopup;
		_root.AddChild(_popupCloseDelay);

		var popupMargin = new MarginContainer();
		popupMargin.Name = "UnitStatsCategoryPopupMargin_MarginContainer";
		popupMargin.AddThemeConstantOverride("margin_left", PopupPadding);
		popupMargin.AddThemeConstantOverride("margin_right", PopupPadding);
		popupMargin.AddThemeConstantOverride("margin_top", PopupPadding);
		popupMargin.AddThemeConstantOverride("margin_bottom", PopupPadding);
		popupMargin.MouseEntered += CancelCategoryPopupClose;
		popupMargin.MouseExited += ScheduleCategoryPopupClose;
		_categoryPopup.AddChild(popupMargin);

		_popupRows = new VBoxContainer();
		_popupRows.Name = "UnitStatsCategoryPopupRows_VBoxContainer";
		_popupRows.AddThemeConstantOverride("separation", SectionSeparation);
		popupMargin.AddChild(_popupRows);

		_popupTitle = new Label();
		_popupTitle.Name = "UnitStatsCategoryPopupTitle_Label";
		_popupTitle.CustomMinimumSize = new Vector2(PopupWidth - PopupPadding * 2, 0f);
		_popupTitle.AddThemeFontSizeOverride("font_size", HeaderFontSize + 1);
		_popupTitle.AddThemeColorOverride("font_color", UIStyle.ColorBronze);
		_popupRows.AddChild(_popupTitle);

		_strengthRow = AddStatRow(UnicodeIcons.HAND_FIST, "Strength", AttributeTooltip(AttributeType.Strength));
		_agilityRow = AddStatRow(UnicodeIcons.WIND, "Agility", AttributeTooltip(AttributeType.Agility));
		_vitalityRow = AddStatRow(UnicodeIcons.HAND_HEART, "Vitality", AttributeTooltip(AttributeType.Vitality));
		_intelligenceRow = AddStatRow(UnicodeIcons.BRAIN, "Intelligence", AttributeTooltip(AttributeType.Intelligence));
		_wisdomRow = AddStatRow(UnicodeIcons.LIGHTBULB, "Wisdom", AttributeTooltip(AttributeType.Wisdom));
		_fortuneRow = AddStatRow(UnicodeIcons.DICE, "Fortune", AttributeTooltip(AttributeType.Fortune));

		_experienceRow = AddStatRow(UnicodeIcons.STAR, "Experience", "Experience this unit has accumulated toward its next level.");
		_levelRow = AddStatRow(UnicodeIcons.STAIRS, "Level", "Level this unit has reached.");

		_attackDamageRow = AddStatRow(UnicodeIcons.BURST, "Attack Damage", "Damage this unit deals with one attack, including the bonus from its Strength.");
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

		RegisterCategory(UnicodeIcons.STAR, "General",
			_levelRow, _experienceRow, _lifeStealRow, _cooldownReductionRow, _moveSpeedRow, _damageTypeRow);
		RegisterCategory(UnicodeIcons.HAND_FIST, "Attributes",
			_strengthRow, _agilityRow, _vitalityRow, _intelligenceRow, _wisdomRow, _fortuneRow);
		RegisterCategory(UnicodeIcons.BURST, "Offense",
			_attackDamageRow, _attackRangeRow, _attackSpeedRow, _criticalHitRow, _armorPenetrationRow, _spellPowerRow, _magicPenetrationRow);
		RegisterCategory(UnicodeIcons.SHIELD, "Defense",
			_armorRow, _magicResistanceRow, _evasionRow, _tenacityRow);

		_footer = new VBoxContainer();
		_footer.Name = "UnitStatsFooter_VBoxContainer";
		_footer.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		_footer.SizeFlagsVertical = Control.SizeFlags.ShrinkBegin;
		_footer.AddThemeConstantOverride("separation", SectionSpacing);
		_content.AddChild(_footer);

		// A building that fields units from auto-cast abilities has no queue to show, so what it maintains is stated here
		// instead: the reading is the map's own count against the capacity that building contributes, and a building whose
		// map publishes neither reading draws no section at all.
		_capacitySection = new VBoxContainer();
		_capacitySection.Name = "UnitStatsCapacity_VBoxContainer";
		_capacitySection.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		_capacitySection.AddThemeConstantOverride("separation", SectionSeparation);
		_footer.AddChild(_capacitySection);
		AddSectionHeader(_capacitySection, UnicodeIcons.USERS, TranslationServer.Translate("Capacity"));

		_soldierCapacityRow = AddStatRow(UnicodeIcons.USERS, "Soldiers", "Soldiers this building maintains against the capacity it contributes.");
		_workerCapacityRow = AddStatRow(UnicodeIcons.WRENCH, "Workers", "Workers this building maintains against the worker capacity it contributes.");
		_soldierCapacityRow.Root.Visible = false;
		_workerCapacityRow.Root.Visible = false;
		_capacitySection.Visible = false;
		_capacitySection.AddChild(_soldierCapacityRow.Root);
		_capacitySection.AddChild(_workerCapacityRow.Root);

		_buffsSection = new VBoxContainer();
		_buffsSection.Name = "UnitStatsBuffs_VBoxContainer";
		_buffsSection.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		_buffsSection.AddThemeConstantOverride("separation", SectionSeparation);
		_footer.AddChild(_buffsSection);
		AddSectionHeader(_buffsSection, UnicodeIcons.HEART_PULSE, TranslationServer.Translate("Buffs"));
		_buffsStrip = CreateIconStrip("UnitStatsBuffs_HBoxContainer");
		_buffsSection.AddChild(_buffsStrip);

		_upgradesSection = new VBoxContainer();
		_upgradesSection.Name = "UnitStatsUpgrades_VBoxContainer";
		_upgradesSection.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		_upgradesSection.AddThemeConstantOverride("separation", SectionSeparation);
		_footer.AddChild(_upgradesSection);
		AddSectionHeader(_upgradesSection, UnicodeIcons.CIRCLE_ARROW_UP, TranslationServer.Translate("Upgrades"));
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

		ShowMessage(string.Empty);
	}

	public VBoxContainer Root => _root;

	public void DismissCategoryPopup()
	{
		HideCategoryPopup();
	}

	public void ShowMessage(string message)
	{
		_messageLabel.Text = message;
		_messageLabel.Visible = !string.IsNullOrEmpty(message);
		_content.Visible = string.IsNullOrEmpty(message);
		if (_content.Visible == false) HideCategoryPopup();
	}

	/// <summary>
	///     Writes the readings of a placed resource node: what it is worth, what is left of it, and the resistances that
	///     decide how much of a worker's hit it keeps. A node carries the same health and resistance components a unit
	///     does, so the panel reads it the same way rather than through a second sheet.
	/// </summary>
	/// <param name="entity">The placed prop entity of the node.</param>
	/// <param name="propId">The template the node is placed from.</param>
	public void UpdateResourceNode(Entity entity, string propId)
	{
		var world = GameContext.World;
		if (world == null || !world.IsAlive(entity) || !world.Has<ResourceNode>(entity))
		{
			ShowMessage(string.Empty);
			return;
		}

		world.TryGet(entity, out Health health);
		world.TryGet(entity, out Resistances resistances);

		string type = GameContext.RegistryService.GetResourceTypeForNode(propId) ?? string.Empty;
		string label = string.IsNullOrEmpty(type) ? propId : type.ToUpperInvariant();
		_messageLabel.Text = $"{label}\n"
		                     + $"{TranslationServer.Translate("Resources")}: {health.Current:F0} / {health.Max:F0}\n"
		                     + $"{TranslationServer.Translate("Armor")}: {resistances.Physical:F0}   "
		                     + $"{TranslationServer.Translate("Magic Resistance")}: {resistances.Magic:F0}";
		_messageLabel.Visible = true;
		_content.Visible = false;
		HideCategoryPopup();
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

		_buffIds.Clear();
		buffs.CopyKeys(_buffIds);

		// Buff text is published beside the ECS state rather than inside it, so a description that moves (a bulwark
		// counting a new soldier, for one) has to be refreshed before the stat signature short-circuits.
		UpdateBuffStrip(entity, buffs);

		// Capacity moves as the building fields units and loses them, and it is published rather than held in the ECS, so
		// it is read on every refresh for the same reason the buff text is.
		UpdateCapacityReadings(entity);

		ulong signature = ComputeSignature(entity, stats, attributes, experience, level, attack, hasExperience, hasLevel, hasAttack,
			damageType, experiencePerLevel, info.StateText, info.Player, metadata);
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

		UpdateUpgradeStrip(info, metadata);
	}

	private ulong ComputeSignature(Entity entity, in Stats stats, in UnitAttributes attributes,
		in Experience experience, in Level level, in Attack attack, bool hasExperience, bool hasLevel, bool hasAttack,
		string damageType, float experiencePerLevel, string? stateText, int playerIndex, UnitMetadata? metadata)
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
		signature = Mix(signature, playerIndex);
		signature = Mix(signature, metadata?.TemplateID);
		signature = Mix(signature, metadata?.Upgrades == null ? 0 : metadata.Upgrades.Length);

		for (int index = 0; index < _buffIds.Count; index++)
		{
			signature = Mix(signature, _buffIds[index]);
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

	/// <summary>
	///     Draws one icon per buff the unit carries. A map can publish what its buffs mean under the unit's own tooltip
	///     keys, so the strip shows the map's words when it has them and a readable fallback when it does not; a timed
	///     buff also states what is left of it.
	/// </summary>
	private void UpdateBuffStrip(Entity entity, in Buffs buffs)
	{
		TooltipValueStore? published = GameContext.RegistryService?.TooltipValues;
		string owner = TooltipValueStore.UnitOwnerKey(entity);

		ulong signature = SignatureSeed;
		for (int index = 0; index < _buffIds.Count; index++)
		{
			signature = Mix(signature, _buffIds[index]);
			signature = Mix(signature, ReadBuffName(published, owner, _buffIds[index]));
			signature = Mix(signature, ReadBuffDescription(published, owner, _buffIds[index]));
			signature = Mix(signature, ReadBuffIcon(published, owner, _buffIds[index]));
			signature = Mix(signature, buffs.GetDuration(index));
		}

		if (signature == _lastBuffsSignature && _buffsStrip.GetChildCount() > 0)
		{
			_buffsSection.Visible = true;
			return;
		}

		_lastBuffsSignature = signature;
		ClearStrip(_buffsStrip);

		for (int index = 0; index < _buffIds.Count; index++)
		{
			string buffId = _buffIds[index];
			if (string.IsNullOrEmpty(buffId)) continue;

			string name = ReadBuffName(published, owner, buffId);
			string description = ReadBuffDescription(published, owner, buffId);
			string tooltip = TranslationServer.Translate(name);
			if (!string.IsNullOrEmpty(description)) tooltip += "\n" + TranslationServer.Translate(description);

			float duration = buffs.GetDuration(index);
			if (duration > 0f && duration < 3600f)
			{
				tooltip += "\n" + string.Format(TranslationServer.Translate("{0} s remaining"), MathF.Ceiling(buffs.GetRemaining(index)).ToString(CultureInfo.InvariantCulture));
			}

			_buffsStrip.AddChild(CreateBuffIcon(ReadBuffIcon(published, owner, buffId), StatusEffectGlyph(buffId), tooltip));
		}

		_buffsSection.Visible = _buffsStrip.GetChildCount() > 0;
	}

	private static string ReadBuffName(TooltipValueStore? published, string owner, string buffId)
	{
		if (published != null && published.TryGetText(owner, BuffTooltipKey(buffId, "name"), out string name) && !string.IsNullOrWhiteSpace(name)) return name;

		return ToDisplayName(buffId);
	}

	private static string ReadBuffDescription(TooltipValueStore? published, string owner, string buffId)
	{
		return published != null && published.TryGetText(owner, BuffTooltipKey(buffId, "description"), out string description)
			? description
			: string.Empty;
	}

	/// <summary>
	///     The icon a map drew for one of its buffs. A status effect the map says nothing about falls back to the glyph the
	///     engine picks from the buff's name, so an undescribed buff is still a readable mark rather than a blank slot.
	/// </summary>
	private static string ReadBuffIcon(TooltipValueStore? published, string owner, string buffId)
	{
		return published != null && published.TryGetText(owner, BuffTooltipKey(buffId, "icon"), out string iconPath)
			? iconPath
			: string.Empty;
	}

	private static string BuffTooltipKey(string buffId, string field)
	{
		string core = buffId.StartsWith("buff/", StringComparison.OrdinalIgnoreCase)
			? buffId.Substring("buff/".Length)
			: buffId;
		return $"buff.{core}.{field}";
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
		string trimmed = identifier;
		string[] prefixes = { "upgrade/", "buff/", "ability/", "item/", "unit/", "building/" };
		for (int index = 0; index < prefixes.Length; index++)
		{
			if (!trimmed.StartsWith(prefixes[index], StringComparison.OrdinalIgnoreCase)) continue;

			trimmed = trimmed.Substring(prefixes[index].Length);
			break;
		}

		trimmed = trimmed.Replace('_', ' ');
		if (trimmed.Length == 0) return identifier;
		return char.ToUpperInvariant(trimmed[0]) + trimmed.Substring(1);
	}

	private static string StatusEffectGlyph(string statusEffectId)
	{
		if (statusEffectId.Contains("bulwark", StringComparison.OrdinalIgnoreCase)) return UnicodeIcons.SHIELD_HEART;
		if (statusEffectId.Contains("worker", StringComparison.OrdinalIgnoreCase)) return UnicodeIcons.WRENCH;
		if (statusEffectId.Contains("stun", StringComparison.OrdinalIgnoreCase)) return UnicodeIcons.CIRCLE_DOT;
		if (statusEffectId.Contains("silence", StringComparison.OrdinalIgnoreCase)) return UnicodeIcons.BAN;
		if (statusEffectId.Contains("slow", StringComparison.OrdinalIgnoreCase)) return UnicodeIcons.SNOWFLAKE;
		if (statusEffectId.Contains("root", StringComparison.OrdinalIgnoreCase)) return UnicodeIcons.LINK;
		if (statusEffectId.Contains("burn", StringComparison.OrdinalIgnoreCase)) return UnicodeIcons.FIRE;
		if (statusEffectId.Contains("haste", StringComparison.OrdinalIgnoreCase)) return UnicodeIcons.WIND;
		if (statusEffectId.Contains("run", StringComparison.OrdinalIgnoreCase)) return UnicodeIcons.PERSON_RUNNING;
		return UnicodeIcons.CIRCLE_EXCLAMATION;
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
		return new StatRow(icon, name, tooltip);
	}

	/// <summary>
	///     Registers one group of rows under an icon the player hovers to read them. The rows keep living in this panel
	///     so their values are written the same way whatever the popup is showing.
	/// </summary>
	private void RegisterCategory(string glyph, string title, params StatRow[] rows)
	{
		var category = new StatCategory(glyph, title, rows);

		var icon = new Label();
		icon.Name = $"UnitStatsCategory{title}_Label";
		icon.Text = glyph;
		icon.TooltipText = TranslationServer.Translate(title);
		icon.MouseFilter = Control.MouseFilterEnum.Stop;
		icon.CustomMinimumSize = new Vector2(CategoryIconSize, CategoryIconSize);
		icon.HorizontalAlignment = HorizontalAlignment.Center;
		icon.VerticalAlignment = VerticalAlignment.Center;
		ApplyIconFont(icon);
		icon.AddThemeFontSizeOverride("font_size", CategoryIconFontSize);
		icon.AddThemeColorOverride("font_color", UIStyle.ColorBronze);
		icon.MouseEntered += () =>
		{
			icon.AddThemeColorOverride("font_color", UIStyle.ColorGold);
			ShowCategoryPopup(category, icon);
		};
		icon.MouseExited += () =>
		{
			icon.AddThemeColorOverride("font_color", UIStyle.ColorBronze);
			ScheduleCategoryPopupClose();
		};

		category.Icon = icon;
		_categories.Add(category);
		_categoryStrip.AddChild(icon);
	}

	private void ShowCategoryPopup(StatCategory category, Control icon)
	{
		CancelCategoryPopupClose();
		if (_popupCategory == category && _categoryPopup.Visible) return;

		DetachPopupRows();
		_popupCategory = category;
		_popupTitle.Text = TranslationServer.Translate(category.Title);

		int rowCount = 0;
		for (int index = 0; index < category.Rows.Length; index++)
		{
			StatRow row = category.Rows[index];
			if (!row.Root.Visible) continue;

			_popupRows.AddChild(row.Root);
			_popupAttachedRows.Add(row);
			rowCount++;
		}

		if (rowCount == 0)
		{
			HideCategoryPopup();
			return;
		}

		Vector2 contentSize = _popupRows.GetCombinedMinimumSize();
		Vector2 iconScreen = icon.GetScreenPosition();
		Vector2 viewportSize = icon.GetViewportRect().Size;
		int width = Math.Max(PopupWidth, (int)contentSize.X + PopupPadding * 2);
		width = Math.Min(width, Math.Max(1, (int)viewportSize.X));
		int height = (int)contentSize.Y + PopupPadding * 2;
		int x = (int)(iconScreen.X + icon.Size.X + PopupIconGap);
		if (x + width > viewportSize.X) x = (int)(iconScreen.X - width - PopupIconGap);
		int y = (int)(iconScreen.Y + icon.Size.Y * 0.5f - height * 0.5f);
		x = Math.Clamp(x, 0, Math.Max(0, (int)viewportSize.X - width));
		y = Math.Clamp(y, 0, Math.Max(0, (int)viewportSize.Y - height));

		_categoryPopup.Popup(new Rect2I(x, y, width, height));
	}

	private void ScheduleCategoryPopupClose()
	{
		if (_categoryPopup.Visible) _popupCloseDelay.Start();
	}

	private void CancelCategoryPopupClose()
	{
		_popupCloseDelay.Stop();
	}

	private void HideCategoryPopup()
	{
		_popupCloseDelay.Stop();
		if (_categoryPopup.Visible) _categoryPopup.Hide();
		_popupCategory = null;
	}

	/// <summary>
	///     Closes the popup when the panel it belongs to is hidden. The popup is its own window, so it would otherwise stay
	///     on screen over a panel that no longer draws anything, such as when the selection is cleared.
	/// </summary>
	private void HideCategoryPopupWhenHidden()
	{
		if (!_root.IsVisibleInTree()) HideCategoryPopup();
	}

	private static StyleBoxFlat CreatePopupStyle()
	{
		var style = new StyleBoxFlat();
		style.BgColor = UIStyle.ColorPanelBg;
		style.BorderColor = UIStyle.ColorBronze;
		style.SetBorderWidthAll(1);
		style.SetCornerRadiusAll(4);
		return style;
	}

	/// <summary>
	///     Takes the rows back out of the popup without freeing them, so the next category can show its own. The title
	///     stays, because it is the popup's own label rather than a stat.
	/// </summary>
	private void DetachPopupRows()
	{
		for (int index = 0; index < _popupAttachedRows.Count; index++)
		{
			StatRow row = _popupAttachedRows[index];
			if (row.Root.GetParent() == _popupRows) _popupRows.RemoveChild(row.Root);
		}

		_popupAttachedRows.Clear();
	}

	private void ApplyRowVisibility(bool hasExperience, bool hasLevel, bool hasAttack)
	{
		_experienceRow.Root.Visible = hasExperience;
		_levelRow.Root.Visible = hasLevel;
		_attackRangeRow.Root.Visible = hasAttack;

		for (int index = 0; index < _categories.Count; index++)
		{
			StatCategory category = _categories[index];
			bool anyVisible = false;
			for (int rowIndex = 0; rowIndex < category.Rows.Length; rowIndex++)
			{
				if (!category.Rows[rowIndex].Root.Visible) continue;

				anyVisible = true;
				break;
			}

			category.Icon.Visible = anyVisible;
		}
	}

	/// <summary>
	///     Writes the two capacity readings a map publishes for a building: the soldiers it maintains against the capacity
	///     it contributes, and the workers it maintains against its worker capacity. A building that publishes neither is
	///     not a unit-raising building, so the whole section stays away rather than showing an empty header.
	/// </summary>
	/// <param name="entity">The selected building.</param>
	private void UpdateCapacityReadings(Entity entity)
	{
		TooltipValueStore? published = GameContext.RegistryService?.TooltipValues;
		string owner = TooltipValueStore.UnitOwnerKey(entity);

		string soldiers = string.Empty;
		string workers = string.Empty;
		bool hasSoldiers = published != null && published.TryGetText(owner, SoldierCapacityKey, out soldiers) && soldiers.Length > 0;
		bool hasWorkers = published != null && published.TryGetText(owner, WorkerCapacityKey, out workers) && workers.Length > 0;

		if (_soldierCapacityRow.Root.Visible != hasSoldiers) _soldierCapacityRow.Root.Visible = hasSoldiers;
		if (_workerCapacityRow.Root.Visible != hasWorkers) _workerCapacityRow.Root.Visible = hasWorkers;
		if (_capacitySection.Visible != (hasSoldiers || hasWorkers)) _capacitySection.Visible = hasSoldiers || hasWorkers;

		if (hasSoldiers) _soldierCapacityRow.SetValue(soldiers);
		if (hasWorkers) _workerCapacityRow.SetValue(workers);
	}

	private static HBoxContainer CreateIconStrip(string nodeName)
	{
		var strip = new HBoxContainer();
		strip.Name = nodeName;
		strip.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		strip.AddThemeConstantOverride("separation", 4);
		return strip;
	}

	private static Control CreateBuffIcon(string iconPath, string glyph, string tooltip)
	{
		Texture2D? texture = string.IsNullOrEmpty(iconPath) ? null : RtexIconLoader.Load(iconPath);
		if (texture == null) return CreateGlyphIcon(glyph, tooltip);

		var icon = new TextureRect();
		icon.Name = "UnitStatsBuff_TextureRect";
		icon.Texture = texture;
		icon.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
		icon.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
		icon.TooltipText = tooltip;
		icon.MouseFilter = Control.MouseFilterEnum.Pass;
		icon.CustomMinimumSize = new Vector2(StripIconSize, StripIconSize);
		return icon;
	}

	private static Control CreateGlyphIcon(string glyph, string tooltip)
	{
		var label = new Label();
		label.Name = "UnitStatsBuffIcon_Label";
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

	/// <summary>
	///     One group of stat rows the panel keeps behind an icon: the glyph that names the group, the translated title the
	///     popup heads itself with, and the rows the group shows in the order a player reads them.
	/// </summary>
	private class StatCategory
	{
		public StatCategory(string glyph, string title, StatRow[] rows)
		{
			Glyph = glyph;
			Title = title;
			Rows = rows;
		}

		public string Glyph { get; }

		public string Title { get; }

		public StatRow[] Rows { get; }

		public Label Icon { get; set; } = null!;
	}

	private class StatRow
	{
		private readonly Label _valueLabel;

		public StatRow(string icon, string name, string tooltip)
		{
			Root = new HBoxContainer();
			Root.Name = $"UnitStatsRow{name.Replace(" ", string.Empty, StringComparison.Ordinal)}_HBoxContainer";
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
