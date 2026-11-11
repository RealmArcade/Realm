using System.Collections.Generic;
using System.Globalization;
using Godot;
using Realm.Client.Core;
using Realm.Client.Services;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Tags;

namespace Realm.Client.UI.InGame;

[GlobalClass]
public partial class UnitHealthBarOverlay : Control
{
	/// <summary>
	/// Adds the overlay to the HUD and puts it behind everything else, so the bars sit over the world but under every
	/// panel. It ignores the mouse throughout: health bars are read, never clicked.
	/// </summary>
	public static UnitHealthBarOverlay Create(Node host)
	{
		var overlay = new UnitHealthBarOverlay();
		overlay.Name = "UnitHealthBarOverlay";
		overlay.MouseFilter = Control.MouseFilterEnum.Ignore;
		host.AddChild(overlay);
		overlay.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		host.MoveChild(overlay, 0);
		return overlay;
	}

	private const float BaseBarWidth = 54f;
	private const float BaseBarHeight = 6f;
	private const float BaseBarGap = 2f;
	private const float BaseBadgeBoxWidth = 15f;
	private const float BaseBadgeGap = 2f;
	private const float BaseFrameThickness = 2f;
	private const float BaseBarInset = 1f;
	private const int BaseBadgeFontSize = 11;
	private const int MaximumCachedBadge = 999;

	private static readonly Color FrameColor = new(0.84f, 0.68f, 0.27f);
	private static readonly Color FrameShadowColor = new(0f, 0f, 0f, 0.6f);
	private static readonly Color BackplateColor = new(0.15f, 0.08f, 0.05f, 0.92f);
	private static readonly Color TrackColor = new(0.02f, 0.02f, 0.03f, 0.95f);
	private static readonly Color HealthFillColor = new(0.14f, 0.72f, 0.16f);
	private static readonly Color HealthHighlightColor = new(0.5f, 0.98f, 0.42f, 0.7f);
	private static readonly Color ManaFillColor = new(0.14f, 0.4f, 0.92f);
	private static readonly Color ManaHighlightColor = new(0.52f, 0.72f, 1f, 0.7f);
	private static readonly Color LevelTextColor = new(0.97f, 0.95f, 0.88f);
	private static readonly Color DormantFrameColor = new(0.42f, 0.24f, 0.2f);
	private static readonly Color DormantMarkColor = new(0.85f, 0.28f, 0.2f);

	private static readonly string[] BadgeText = BuildBadgeText();

	private readonly List<HealthBarEntry> _bars = new();
	private bool _showHealthBar = true;
	private bool _showManaBar;
	private bool _showBadge;

	private struct HealthBarEntry
	{
		public Realm.Ecs.Entity Entity;
		public Vector2 Position;
		public float Scale;
		public float HealthFraction;
		public float ManaFraction;
		public int BadgeValue;
		public Color BadgeColor;
		public bool HasHealth;
		public bool HasMana;
		public bool HasBadge;
		public bool IsDormant;
	}

	public int BadgesDrawn { get; private set; }

	/// <summary>
	///     Answers whether the plate drawn for a unit is the crossed-out plate of a dormant building, so a test can read
	///     what the screen drew rather than what it was asked to draw.
	/// </summary>
	/// <param name="entity">The unit whose plate is read.</param>
	/// <param name="dormant">True when the unit's plate is crossed out.</param>
	/// <returns>True when a plate was drawn for the unit this frame.</returns>
	public bool TryReadDormant(Realm.Ecs.Entity entity, out bool dormant)
	{
		for (int index = 0; index < _bars.Count; index++)
		{
			if (_bars[index].Entity.Id != entity.Id) continue;

			dormant = _bars[index].IsDormant;
			return true;
		}

		dormant = false;
		return false;
	}

	public bool TryReadBadge(Realm.Ecs.Entity entity, out int value)
	{
		for (int index = 0; index < _bars.Count; index++)
		{
			if (_bars[index].Entity.Id != entity.Id) continue;

			value = _bars[index].BadgeValue;
			return _bars[index].HasBadge;
		}

		value = 0;
		return false;
	}

	public void Refresh(Camera3D? camera)
	{
		_bars.Clear();
		BadgesDrawn = 0;

		ResolvePlateVisibility();

		var mode = GameSettings.ShowHealthBars;
		bool barsAllowed = mode != HealthBarMode.Hidden;
		if (!barsAllowed && !_showBadge || !_showHealthBar && !_showManaBar && !_showBadge)
		{
			QueueRedraw();
			return;
		}

		if (camera == null || !GodotObject.IsInstanceValid(camera) || GameContext.Host == null)
		{
			QueueRedraw();
			return;
		}

		var world = GameContext.World;
		if (world == null)
		{
			QueueRedraw();
			return;
		}

		Vector2 viewportSize = GetViewportRect().Size;
		var units = GameContext.Host.AllUnits;

		for (int i = 0; i < units.Count; i++)
		{
			Unit3D unit = units[i];
			if (unit == null || !GodotObject.IsInstanceValid(unit) || unit.IsPreview || !unit.Visible) continue;
			if (unit.Entity == Realm.Ecs.Entity.Null || !world.IsAlive(unit.Entity)) continue;
			if (world.Has<Dead>(unit.Entity)) continue;

			// A badge is a label a map states about a unit rather than a reading of its condition, so it draws even where
			// the map hid the bars: that is how a board square shows a rank without advertising hit points.
			Health health = default;
			Mana mana = default;
			OverheadBadge badge = default;

			bool hasBadge = _showBadge && world.TryGet(unit.Entity, out badge);
			bool barsHidden = world.Has<OverheadBarsHidden>(unit.Entity);

			bool hasHealth = !barsHidden && world.TryGet(unit.Entity, out health) && health.Max > 0f;
			bool hasMana = !barsHidden && world.TryGet(unit.Entity, out mana) && mana.Max > 0f;

			bool showHealth = barsAllowed && _showHealthBar && hasHealth;
			bool showMana = barsAllowed && _showManaBar && hasMana;
			if (!showHealth && !showMana && !hasBadge) continue;

			float healthFraction = showHealth ? Mathf.Clamp(health.Current / health.Max, 0f, 1f) : 0f;
			float manaFraction = showMana ? Mathf.Clamp(mana.Current / mana.Max, 0f, 1f) : 0f;

			if (mode == HealthBarMode.Damaged && !hasBadge)
			{
				bool healthFull = !showHealth || healthFraction >= 1f;
				bool manaFull = !showMana || manaFraction >= 1f;
				if (healthFull && manaFull) continue;
			}

			ShroudService shroud = GameContext.ShroudService;
			if (shroud != null && !shroud.IsUnitVisibleOnMinimap(unit)) continue;

			float anchorHeight = Mathf.Max(2.2f, unit.GetBaseObstacleRadius() * 2.2f);
			Vector3 anchor = unit.GlobalPosition + new Vector3(0f, anchorHeight, 0f);
			if (camera.IsPositionBehind(anchor)) continue;

			Vector2 screen = camera.UnprojectPosition(anchor);
			if (screen.X < 0f || screen.Y < 0f || screen.X > viewportSize.X || screen.Y > viewportSize.Y) continue;

			float distance = camera.GlobalPosition.DistanceTo(anchor);
			float scale = Mathf.Clamp(14f / Mathf.Max(distance, 0.001f), 0.4f, 1.4f);

			if (hasBadge) BadgesDrawn++;
			_bars.Add(new HealthBarEntry
			{
				Entity = unit.Entity,
				Position = screen,
				Scale = scale,
				HealthFraction = healthFraction,
				ManaFraction = manaFraction,
				BadgeValue = hasBadge ? badge.Value : 0,
				BadgeColor = hasBadge ? UnpackBadgeColor(badge.ColorRgb) : LevelTextColor,
				HasHealth = showHealth,
				HasMana = showMana,
				HasBadge = hasBadge,
				IsDormant = world.Has<Dormant>(unit.Entity)
			});
		}

		QueueRedraw();
	}

	public override void _Draw()
	{
		if (_bars.Count == 0) return;

		Font font = GetThemeDefaultFont();

		for (int i = 0; i < _bars.Count; i++)
		{
			HealthBarEntry bar = _bars[i];
			float scale = bar.Scale;

			float barHeight = BaseBarHeight * scale;
			float barGap = BaseBarGap * scale;
			float barWidth = BaseBarWidth * scale;
			float badgeBoxWidth = BaseBadgeBoxWidth * scale;
			float badgeGap = BaseBadgeGap * scale;
			float frameThickness = BaseFrameThickness * scale;
			float barInset = BaseBarInset * scale;

			bool hasBars = bar.HasHealth || bar.HasMana;
			bool hasStack = bar.HasHealth && bar.HasMana;
			float bodyHeight = hasStack ? barHeight * 2f + barGap : hasBars ? barHeight : barHeight * 2f + barGap;
			float bodyWidth = 0f;
			if (hasBars) bodyWidth += barWidth;
			if (bar.HasBadge) bodyWidth += badgeBoxWidth + (hasBars ? badgeGap : 0f);

			float totalWidth = bodyWidth + frameThickness * 2f;
			float totalHeight = bodyHeight + frameThickness * 2f;
			float left = bar.Position.X - totalWidth * 0.5f;
			float top = bar.Position.Y - totalHeight;
			float bodyLeft = left + frameThickness;
			float bodyTop = top + frameThickness;

			Color frameColor = bar.IsDormant ? DormantFrameColor : FrameColor;
			DrawRect(new Rect2(left + scale, top + scale, totalWidth, totalHeight), FrameShadowColor);
			DrawRect(new Rect2(left, top, totalWidth, totalHeight), frameColor);
			DrawRect(new Rect2(bodyLeft, bodyTop, bodyWidth, bodyHeight), BackplateColor);

			float cursor = bodyLeft;
			if (bar.HasBadge)
			{
				var badgeBox = new Rect2(cursor, bodyTop, badgeBoxWidth, bodyHeight);
				DrawBadge(font, bar.BadgeValue, bar.BadgeColor, badgeBox, barInset, scale);
				cursor += badgeBoxWidth + (hasBars ? badgeGap : 0f);
			}

			if (bar.HasHealth) DrawBar(new Rect2(cursor, bodyTop, barWidth, barHeight), barInset, bar.HealthFraction, HealthFillColor, HealthHighlightColor);
			if (bar.HasMana)
			{
				float manaTop = bar.HasHealth ? bodyTop + barHeight + barGap : bodyTop;
				DrawBar(new Rect2(cursor, manaTop, barWidth, barHeight), barInset, bar.ManaFraction, ManaFillColor, ManaHighlightColor);
			}

			if (bar.IsDormant) DrawDormantMark(new Rect2(bodyLeft, bodyTop, bodyWidth, bodyHeight), scale);
		}
	}

	private void ResolvePlateVisibility()
	{
		var rules = GameContext.Simulation?.MapRules;
		_showHealthBar = rules == null || rules.UnitHealthBarEnabled;
		_showManaBar = rules != null && rules.UnitManaBarEnabled;
		_showBadge = rules != null && rules.UnitLevelBadgeEnabled;
	}

	/// <summary>
	///     Crosses a dormant building's plate out, so a building reduced to zero health reads as ruined at a glance instead
	///     of as a unit that happens to be at no health.
	/// </summary>
	private void DrawDormantMark(Rect2 body, float scale)
	{
		if (body.Size.X <= 0f || body.Size.Y <= 0f) return;

		float thickness = Mathf.Max(1f, scale);
		DrawLine(new Vector2(body.Position.X, body.Position.Y), new Vector2(body.Position.X + body.Size.X, body.Position.Y + body.Size.Y), DormantMarkColor, thickness);
		DrawLine(new Vector2(body.Position.X + body.Size.X, body.Position.Y), new Vector2(body.Position.X, body.Position.Y + body.Size.Y), DormantMarkColor, thickness);
	}

	private void DrawBar(Rect2 bar, float inset, float fraction, Color fillColor, Color highlightColor)
	{
		DrawRect(bar, TrackColor);
		if (fraction <= 0f) return;

		float innerWidth = Mathf.Max(0f, (bar.Size.X - inset * 2f) * fraction);
		float innerHeight = Mathf.Max(1f, bar.Size.Y - inset * 2f);
		if (innerWidth <= 0f) return;

		var fill = new Rect2(bar.Position.X + inset, bar.Position.Y + inset, innerWidth, innerHeight);
		DrawRect(fill, fillColor);
		DrawRect(new Rect2(fill.Position.X, fill.Position.Y, fill.Size.X, Mathf.Max(1f, fill.Size.Y * 0.35f)), highlightColor);
	}

	private void DrawBadge(Font font, int value, Color color, Rect2 box, float inset, float scale)
	{
		DrawRect(box, TrackColor);

		var fill = new Rect2(box.Position.X + inset, box.Position.Y + inset, Mathf.Max(1f, box.Size.X - inset * 2f), Mathf.Max(1f, box.Size.Y - inset * 2f));
		DrawRect(fill, new Color(color.R * 0.42f, color.G * 0.42f, color.B * 0.42f));

		if (font == null) return;

		int fontSize = Mathf.Max(7, Mathf.RoundToInt(BaseBadgeFontSize * scale));
		float ascent = font.GetAscent(fontSize);
		float descent = font.GetDescent(fontSize);
		float baseline = box.Position.Y + (box.Size.Y + ascent - descent) * 0.5f;
		int clampedValue = Mathf.Clamp(value, 0, MaximumCachedBadge);

		DrawString(font, new Vector2(box.Position.X, baseline), BadgeText[clampedValue], HorizontalAlignment.Center, box.Size.X, fontSize, LevelTextColor);
	}

	private static Color UnpackBadgeColor(uint colorRgb)
	{
		return new Color(((colorRgb >> 16) & 0xFF) / 255f, ((colorRgb >> 8) & 0xFF) / 255f, (colorRgb & 0xFF) / 255f);
	}

	private static string[] BuildBadgeText()
	{
		var text = new string[MaximumCachedBadge + 1];
		for (int index = 0; index < text.Length; index++) text[index] = index.ToString(CultureInfo.InvariantCulture);
		return text;
	}
}
