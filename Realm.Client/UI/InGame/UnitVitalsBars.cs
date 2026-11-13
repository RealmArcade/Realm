using System.Globalization;
using Godot;
using Realm.Client.Services;
using Realm.Ecs;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Resources;
using Realm.Ecs.Components.Stats;

namespace Realm.Client.UI.InGame;

/// <summary>
///     The health and mana readouts of the selected unit, drawn into the two bar slots the match view's bottom
///     console artwork reserves for them. Positions are the slot rectangles of the artwork, so the bars scale with
///     the console instead of floating over it.
/// </summary>
public class UnitVitalsBars
{
	private const float ArtworkHeight = 240f;
	private const float HealthSlotLeft = 375f;
	private const float HealthSlotTop = 202f;
	private const float HealthSlotRight = 512f;
	private const float HealthSlotBottom = 214f;
	private const float ManaSlotTop = 223f;
	private const float ManaSlotBottom = 237f;
	private const int LabelFontSize = 10;

	private static readonly StatId MaxHpStat = new(CombatStatType.MaxHp);
	private static readonly StatId MaxManaStat = new(CombatStatType.MaxMana);

	private static readonly Color HealthFillColor = new Color(0.72f, 0.16f, 0.14f);
	private static readonly Color ManaFillColor = new Color(0.18f, 0.42f, 0.86f);
	private static readonly Color TrackColor = new Color(0.02f, 0.02f, 0.03f, 0.85f);

	private readonly Control _root;
	private readonly VitalsBar _healthBar;
	private readonly VitalsBar _manaBar;

	public UnitVitalsBars()
	{
		_root = new Control();
		_root.Name = "UnitVitalsBars_Control";
		_root.AnchorLeft = 0f;
		_root.AnchorTop = 0f;
		_root.AnchorRight = 1f;
		_root.AnchorBottom = 1f;
		_root.OffsetLeft = 0f;
		_root.OffsetTop = 0f;
		_root.OffsetRight = 0f;
		_root.OffsetBottom = 0f;
		_root.MouseFilter = Control.MouseFilterEnum.Ignore;
		_root.Visible = false;

		string healthTooltip = TranslationServer.Translate("Health") + "\n" + TranslationServer.Translate("Current and maximum health points.");
		_healthBar = new VitalsBar("UnitVitalsHealth", HealthFillColor, healthTooltip);
		_root.AddChild(_healthBar.Root);

		string manaTooltip = TranslationServer.Translate("Mana") + "\n" + TranslationServer.Translate("Current and maximum mana points.");
		_manaBar = new VitalsBar("UnitVitalsMana", ManaFillColor, manaTooltip);
		_root.AddChild(_manaBar.Root);
	}

	public Control Root => _root;

	public void ApplyLayout(float consoleHeight)
	{
		float scale = consoleHeight > 0f ? consoleHeight / ArtworkHeight : 1f;
		float slotLeft = HealthSlotLeft * scale;
		float slotWidth = (HealthSlotRight - HealthSlotLeft) * scale;

		_healthBar.SetRect(slotLeft, HealthSlotTop * scale, slotWidth, (HealthSlotBottom - HealthSlotTop) * scale, scale);
		_manaBar.SetRect(slotLeft, ManaSlotTop * scale, slotWidth, (ManaSlotBottom - ManaSlotTop) * scale, scale);
	}

	public void UpdateSingleSelection(SelectedUnitInfo info)
	{
		var world = GameContext.World;
		if (world == null || info == null || !world.IsAlive(info.Entity))
		{
			_root.Visible = false;
			return;
		}

		Entity entity = info.Entity;
		world.TryGet(entity, out Stats stats);

		bool hasHealth = world.TryGet(entity, out Health health);
		_healthBar.Root.Visible = hasHealth;
		if (hasHealth)
		{
			float maxHealth = stats.TryGetValue(MaxHpStat, out float maxHp) ? maxHp : health.Max;
			_healthBar.SetValue(health.Current, maxHealth);
		}

		bool hasMana = world.TryGet(entity, out Mana mana);
		_manaBar.Root.Visible = hasMana;
		if (hasMana)
		{
			float maxMana = stats.TryGetValue(MaxManaStat, out float maxManaValue) ? maxManaValue : mana.Max;
			_manaBar.SetValue(mana.Current, maxMana);
		}

		_root.Visible = hasHealth || hasMana;
	}

	/// <summary>
	///     Writes a placed resource node into the console: what it still holds against what it held, which is the capacity
	///     its health component states. A node stores no mana, so the second slot stays hidden for it.
	/// </summary>
	/// <param name="entity">The placed node the local player selected.</param>
	public void UpdateResourceNode(Entity entity)
	{
		var world = GameContext.World;
		if (world == null || !world.IsAlive(entity) || !world.Has<ResourceNode>(entity))
		{
			_root.Visible = false;
			return;
		}

		float current = 0f;
		float maximum = 0f;

		if (world.TryGet(entity, out Health capacity) && capacity.Max > 0f)
		{
			current = capacity.Current;
			maximum = capacity.Max;
		}
		else if (world.TryGet(entity, out ResourceNode resourceNode) && resourceNode.MaxCapacity > 0f)
		{
			current = resourceNode.Amount;
			maximum = resourceNode.MaxCapacity;
		}

		if (maximum <= 0f)
		{
			_root.Visible = false;
			return;
		}

		_healthBar.Root.Visible = true;
		_healthBar.SetValue(current, maximum);
		_manaBar.Root.Visible = false;
		_root.Visible = true;
	}

	public void Hide()
	{
		_root.Visible = false;
	}
	private class VitalsBar
	{
		private readonly ColorRect _fill;
		private readonly Label _valueLabel;
		private string _lastText = null!;
		private float _ratio;
		private float _barWidth;

		public VitalsBar(string nodeName, Color fillColor, string tooltip)
		{
			Root = new Control();
			Root.Name = nodeName + "_Control";
			Root.AnchorLeft = 0f;
			Root.AnchorTop = 0f;
			Root.AnchorRight = 0f;
			Root.AnchorBottom = 0f;
			Root.ClipContents = true;
			Root.MouseFilter = Control.MouseFilterEnum.Pass;
			Root.TooltipText = tooltip;

			var track = new ColorRect();
			track.Name = nodeName + "Track_ColorRect";
			track.Color = TrackColor;
			track.MouseFilter = Control.MouseFilterEnum.Ignore;
			track.SetAnchorsPreset(Control.LayoutPreset.FullRect);
			Root.AddChild(track);

			_fill = new ColorRect();
			_fill.Name = nodeName + "Fill_ColorRect";
			_fill.Color = fillColor;
			_fill.MouseFilter = Control.MouseFilterEnum.Ignore;
			_fill.AnchorLeft = 0f;
			_fill.AnchorTop = 0f;
			_fill.AnchorRight = 0f;
			_fill.AnchorBottom = 1f;
			_fill.OffsetLeft = 1f;
			_fill.OffsetTop = 1f;
			_fill.OffsetRight = 1f;
			_fill.OffsetBottom = -1f;
			Root.AddChild(_fill);

			_valueLabel = new Label();
			_valueLabel.Name = nodeName + "Value_Label";
			_valueLabel.HorizontalAlignment = HorizontalAlignment.Center;
			_valueLabel.VerticalAlignment = VerticalAlignment.Center;
			_valueLabel.MouseFilter = Control.MouseFilterEnum.Ignore;
			_valueLabel.SetAnchorsPreset(Control.LayoutPreset.FullRect);
			_valueLabel.AddThemeColorOverride("font_color", UIStyle.ColorGold);
			Root.AddChild(_valueLabel);
		}

		public Control Root { get; }

		public void SetRect(float left, float top, float width, float height, float consoleScale)
		{
			Root.OffsetLeft = left;
			Root.OffsetTop = top;
			Root.OffsetRight = left + width;
			Root.OffsetBottom = top + height;

			_barWidth = width;
			_valueLabel.AddThemeFontSizeOverride("font_size", Mathf.Max(7, Mathf.RoundToInt(LabelFontSize * consoleScale)));
			ApplyFill();
		}

		public void SetValue(float current, float max)
		{
			_ratio = max > 0f ? Mathf.Clamp(current / max, 0f, 1f) : 0f;
			ApplyFill();

			string text = $"{FormatAmount(current)} / {FormatAmount(max)}";
			if (text == _lastText) return;

			_lastText = text;
			_valueLabel.Text = text;
		}

		private void ApplyFill()
		{
			float fillRight = 1f + _ratio * Mathf.Max(_barWidth - 2f, 0f);
			if (!Mathf.IsEqualApprox(_fill.OffsetRight, fillRight)) _fill.OffsetRight = fillRight;
		}

		private static string FormatAmount(float value)
		{
			return value.ToString("0", CultureInfo.InvariantCulture);
		}
	}
}
