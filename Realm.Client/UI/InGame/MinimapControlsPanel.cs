using System;
using Godot;
using Realm.Client.Services;
using Realm.Client.Utils;

namespace Realm.Client.UI.InGame;

/// <summary>
/// What the control column needs from the HUD: the node the column is anchored inside, the two minimap display toggles,
/// the feedback line for the ping hint, and the hotkey reference it opens.
/// </summary>
public sealed record MinimapControlsHooks(
	Node Owner,
	Action ToggleTerrain,
	Action ToggleColorScheme,
	Action<string, Color> Feedback,
	Action ToggleHotkeys);

/// <summary>
/// The button column beside the minimap. Each button is a FontAwesome glyph in the same plate, so the styling lives in one
/// setup method and the buttons are a table rather than eight near-identical blocks. The column is anchored to the console
/// artwork's minimap slot and scaled by the console layout, which is why the anchors are set here rather than there.
/// </summary>
public sealed class MinimapControlsPanel
{
	public VBoxContainer? Root { get; private set; }
	private float _pulseTimer;
	private int _lastIdleCount;


	/// <summary>The idle-unit button, which the HUD pulses and re-tooltips as the idle count changes.</summary>
	public Button? IdleButton { get; private set; }

	public void Build(MinimapControlsHooks hooks)
	{
		Root = new VBoxContainer();
		Root.Name = "MinimapControls_VBoxContainer";
		Root.Alignment = BoxContainer.AlignmentMode.Center;
		Root.AddThemeConstantOverride("separation", 3);
		Root.AnchorLeft = 0.1773f;
		Root.AnchorRight = 0.2660f;
		Root.AnchorTop = 0.18f;
		Root.AnchorBottom = 0.94f;
		Root.OffsetLeft = 0;
		Root.OffsetRight = 0;
		Root.OffsetTop = 0;
		Root.OffsetBottom = 0;
		Root.GrowHorizontal = Control.GrowDirection.Both;
		Root.GrowVertical = Control.GrowDirection.Both;

		var slotsContainer = hooks.Owner.GetNode<Control>("BottomConsole/HBox");
		slotsContainer.AddChild(Root);

		Add(hooks, "BtnZoom_Button", UnicodeIcons.ZOOM_IN, "Toggle Camera Zoom (Near / Mid / Far) [Z]",
			() => GameContext.Host?.CycleCameraZoom());

		Add(hooks, "BtnToggleTerrain_Button", UnicodeIcons.MAP_IMAGE, "Toggle Terrain Overlay (Radar Mode) [F4]",
			hooks.ToggleTerrain);

		Add(hooks, "BtnToggleColorScheme_Button", UnicodeIcons.PALETTE, "Toggle Minimap Color Scheme (Team / Player Colors)",
			hooks.ToggleColorScheme);

		Add(hooks, "BtnPing_Button", UnicodeIcons.CROSSHAIRS_TARGET, "Send Alert Ping on Map [Alt+G]", () =>
		{
			if (GameContext.Host == null) return;

			GameContext.Host.ActivePingMode = true;
			hooks.Feedback("Ping Mode: Click Minimap or Ground to ping", new Color(1.0f, 0.1f, 0.2f));
		});

		Add(hooks, "BtnCenter_Button", UnicodeIcons.CASTLE, "Center Camera on Base [Space]",
			() => GameContext.Host?.CenterCameraOnPrimaryBuilding());

		IdleButton = Add(hooks, "BtnSelectIdle_Button", UnicodeIcons.USER_CLOCK, "Select All Idle Units [F1]",
			() => GameContext.Host?.SelectAllIdleUnits());

		Add(hooks, "BtnSelectArmy_Button", UnicodeIcons.SHIELD, "Select All Army Units [F2]",
			() => GameContext.Host?.SelectAllMilitaryUnits());

		Add(hooks, "BtnHotkeys_Button", UnicodeIcons.KEYBOARD, "Hotkey Reference", hooks.ToggleHotkeys);
	}

	private Button Add(MinimapControlsHooks hooks, string name, string iconGlyph, string tooltip, Action onClick)
	{
		var button = new Button();
		button.Name = name;
		SetupButton(button, iconGlyph, tooltip, onClick);
		Root!.AddChild(button);
		return button;
	}

	/// <summary>
	/// The shared look for a glyph button: no icon texture, the FontAwesome font with an outline so the glyph reads over the
	/// artwork, and the minimap plate styleboxes for its three states.
	/// </summary>
	private static void SetupButton(Button btn, string iconGlyph, string tooltip, Action onClick)
	{
		btn.Flat = false;
		btn.Text = iconGlyph;
		btn.Icon = null;
		string trans = TranslationServer.Translate(tooltip);
		btn.TooltipText = string.IsNullOrEmpty(trans) ? tooltip : trans;
		btn.FocusMode = Control.FocusModeEnum.None;
		btn.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		btn.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
		btn.Alignment = HorizontalAlignment.Center;
		btn.AddThemeConstantOverride("icon_max_width", 0);

		var font = UIStyle.FontAwesome;
		if (font != null)
		{
			btn.AddThemeFontOverride("font", font);
		}

		btn.AddThemeFontSizeOverride("font_size", 14);
		btn.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		btn.AddThemeColorOverride("font_hover_color", UIStyle.ColorGold);
		btn.AddThemeColorOverride("font_pressed_color", UIStyle.ColorCyanGlow);
		btn.AddThemeColorOverride("font_focus_color", UIStyle.ColorGoldDull);
		btn.AddThemeConstantOverride("outline_size", 2);
		btn.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.9f));

		UIStyle.ApplyStyleBoxStates(btn,
			UIStyle.CreateMinimapButtonStyle(false, false),
			UIStyle.CreateMinimapButtonStyle(true, false),
			UIStyle.CreateMinimapButtonStyle(false, true));

		btn.Pressed += () =>
		{
			GameContext.UserInterface?.PlayClickSound();
			onClick?.Invoke();
		};
		btn.MouseEntered += () => GameContext.UserInterface?.PlayHoverSound();
	}

	/// <summary>
	/// The idle button's share of the per-frame work: while units are idle it pulses between gold and orange so the
	/// player notices, and it announces the first idle unit once rather than every frame. Returns nothing because the
	/// board it draws on is this panel's own button.
	/// </summary>
	public void UpdateIdlePulse(double delta, int idleCount, Action<int> onBecameIdle)
	{
		if (IdleButton is not { } idleButton) return;

		if (_lastIdleCount != idleCount)
		{
			idleButton.TooltipText = $"{TranslationServer.Translate("Select All Idle Units [F1]")} ({idleCount} {TranslationServer.Translate("Idle")})";
		}

		if (idleCount > 0)
		{
			_pulseTimer += (float)delta;
			if (_lastIdleCount == 0)
			{
				onBecameIdle(idleCount);
			}

			float pulse = Mathf.Sin(_pulseTimer * 4f) * 0.5f + 0.5f;
			idleButton.Modulate = new Color(1f, 0.8f + pulse * 0.2f, 0.2f + pulse * 0.8f, 1f);
		}
		else
		{
			_pulseTimer = 0f;
			idleButton.Modulate = Colors.White;
		}

		_lastIdleCount = idleCount;
	}

}
