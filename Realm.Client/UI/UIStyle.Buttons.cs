// Extracted from UIStyle.cs (task 7 role split).
using System;
using System.Collections.Generic;
using Godot;
using SkiaSharp;
using Realm.Client.Utils;
using Realm.Client.Services;


namespace Realm.Client.UI;

public static partial class UIStyle
{
	public static StyleBox CreateButtonNormal()
	{
		return CreateArrowButtonStyle(new Color(1.0f, 1.0f, 1.0f));
	}

	public static StyleBox CreateButtonHover()
	{
		return CreateArrowButtonStyle(new Color(1.12f, 1.10f, 0.96f));
	}

	public static StyleBox CreateButtonPressed()
	{
		return CreateArrowButtonStyle(new Color(0.85f, 0.82f, 0.75f));
	}

	public static void ApplyButtonStyleBoxes(Control button)
	{
		button.AddThemeStyleboxOverride("normal", CreateButtonNormal());
		button.AddThemeStyleboxOverride("hover", CreateButtonHover());
		button.AddThemeStyleboxOverride("pressed", CreateButtonPressed());
	}

	public static void ApplyButtonStyleBoxesClearingFocus(Control button)
	{
		ApplyButtonStyleBoxes(button);
		button.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
	}

	public static void ApplyHudButtonStyleboxes(Control button)
	{
		button.AddThemeStyleboxOverride("normal", CreateHUDButtonStyle(false, false));
		button.AddThemeStyleboxOverride("hover", CreateHUDButtonStyle(true, false));
		button.AddThemeStyleboxOverride("pressed", CreateHUDButtonStyle(false, true));
	}

	public static Button CreateActionButton(string nodeName, string text, int fontSize)
	{
		var button = new Button();
		button.Name = nodeName;
		button.Flat = false;
		button.AddThemeConstantOverride("icon_max_width", 0);
		ApplyButtonStyleBoxesClearingFocus(button);
		ApplyButtonText(button, text, fontSize);
		return button;
	}

	public static Button CreateBareButton(string nodeName)
	{
		var button = new Button();
		button.Name = nodeName;
		button.Set("icon_max_width", 0);
		return button;
	}

	public static void ApplyGoldHoverColors(Control control)
	{
		control.AddThemeColorOverride("font_hover_color", ColorGold);
		control.AddThemeColorOverride("font_pressed_color", ColorGold);
	}

	public static void ApplyGoldHoverAndFocusColors(Control control)
	{
		ApplyGoldHoverColors(control);
		control.AddThemeColorOverride("font_focus_color", ColorGold);
	}

	private static StyleBox CreateArrowButtonStyle(Color modulateColor)
	{
		var style = new StyleBoxTexture();
		style.Texture = TrimmedTextureLoader.LoadTrimmedTexture(ButtonPlateTexturePath);

		style.TextureMarginLeft = ButtonPlateTipWidth;
		style.TextureMarginRight = ButtonPlateTipWidth;
		style.TextureMarginTop = ButtonPlateEdgeHeight;
		style.TextureMarginBottom = ButtonPlateEdgeHeight;

		style.ContentMarginLeft = 26;
		style.ContentMarginRight = 26;
		style.ContentMarginTop = 8;
		style.ContentMarginBottom = 8;

		style.ModulateColor = modulateColor;
		return style;
	}

	public static StyleBox CreateOptionButtonNormal()
	{
		var style = new StyleBoxFlat();
		style.BgColor = new Color(0.18f, 0.16f, 0.14f, 0.95f);
		style.BorderColor = new Color(0.42f, 0.36f, 0.26f, 0.85f);
		style.SetBorderWidthAll(1);
		style.SetCornerRadiusAll(4);
		style.ContentMarginLeft = 8;
		style.ContentMarginRight = 8;
		style.ContentMarginTop = 5;
		style.ContentMarginBottom = 5;
		return style;
	}

	public static StyleBox CreateOptionButtonHover()
	{
		var style = new StyleBoxFlat();
		style.BgColor = new Color(0.28f, 0.24f, 0.18f, 0.98f);
		style.BorderColor = ColorGold;
		style.SetBorderWidthAll(1);
		style.SetCornerRadiusAll(4);
		style.ContentMarginLeft = 8;
		style.ContentMarginRight = 8;
		style.ContentMarginTop = 5;
		style.ContentMarginBottom = 5;
		return style;
	}

	public static StyleBox CreateOptionButtonPressed()
	{
		var style = new StyleBoxFlat();
		style.BgColor = new Color(0.36f, 0.30f, 0.20f, 0.98f);
		style.BorderColor = ColorGold;
		style.SetBorderWidthAll(1);
		style.SetCornerRadiusAll(4);
		style.ContentMarginLeft = 8;
		style.ContentMarginRight = 8;
		style.ContentMarginTop = 5;
		style.ContentMarginBottom = 5;
		return style;
	}

	public static StyleBox CreateCustomLobbyButtonTexture(string path, int contentMarginX, int contentMarginY, bool isHover, bool isPressed)
	{
		var tex = TrimmedTextureLoader.LoadTrimmedTexture(path);
		if (tex != null)
		{
			var style = new StyleBoxTexture();
			style.Texture = tex;
			style.TextureMarginLeft = 0;
			style.TextureMarginRight = 0;
			style.TextureMarginTop = 0;
			style.TextureMarginBottom = 0;
			style.ContentMarginLeft = contentMarginX;
			style.ContentMarginRight = contentMarginX;
			style.ContentMarginTop = contentMarginY;
			style.ContentMarginBottom = contentMarginY;

			if (isPressed)
			{
				style.ModulateColor = new Color(0.85f, 0.85f, 0.9f);
			}
			else if (isHover)
			{
				style.ModulateColor = new Color(1.18f, 1.18f, 1.25f);
			}
			else
			{
				style.ModulateColor = Colors.White;
			}

			return style;
		}

		return isPressed ? CreateButtonPressed() : (isHover ? CreateButtonHover() : CreateButtonNormal());
	}

	public static StyleBox CreateCustomLobbyStartGameButton(bool isHover = false, bool isPressed = false)
	{
		return CreateCustomLobbyButtonTexture("res://Assets/UI/custom_lobby_start_game.webp", 20, 10, isHover, isPressed);
	}

	public static StyleBox CreateCustomLobbyBackButton(bool isHover = false, bool isPressed = false)
	{
		return CreateCustomLobbyButtonTexture("res://Assets/UI/custom_lobby_back_button.webp", 0, 0, isHover, isPressed);
	}

	public static StyleBox CreateCustomLobbyRechargeButton(bool isHover = false, bool isPressed = false)
	{
		return CreateCustomLobbyButtonTexture("res://Assets/UI/custom_lobby_recharge_button.webp", 0, 0, isHover, isPressed);
	}

	public static StyleBoxFlat CreateFlatButtonStyle(bool isHover = false, bool isPressed = false)
	{
		var style = new StyleBoxFlat();
		style.BgColor = isPressed ? new Color(0.12f, 0.14f, 0.18f, 0.75f) : (isHover ? new Color(0.1f, 0.12f, 0.15f, 0.55f) : new Color(0.06f, 0.08f, 0.1f, 0.35f));
		if (isPressed)
		{
			style.BorderColor = ColorCyanGlow;
		}
		else if (isHover)
		{
			style.BorderColor = ColorGold;
		}
		else
		{
			style.BorderColor = new Color(0.45f, 0.38f, 0.3f, 0.6f);
		}

		style.SetBorderWidthAll(1);
		style.ContentMarginLeft = 6;
		style.ContentMarginRight = 6;
		style.ContentMarginTop = 6;
		style.ContentMarginBottom = 6;
		style.SetCornerRadiusAll(4);
		return style;
	}

	public static StyleBoxFlat CreateHUDButtonStyle(bool isHover = false, bool isPressed = false)
	{
		var style = new StyleBoxFlat();
		style.BgColor = isPressed ? new Color(0.12f, 0.14f, 0.16f, 0.9f) : (isHover ? new Color(0.18f, 0.2f, 0.24f, 0.8f) : new Color(0.08f, 0.09f, 0.1f, 0.75f));
		style.BorderColor = isPressed ? ColorCyanGlow : (isHover ? ColorGold : new Color(0.35f, 0.3f, 0.25f, 0.8f));
		style.SetBorderWidthAll(2);

		style.ContentMarginLeft = 6;
		style.ContentMarginRight = 6;
		style.ContentMarginTop = 6;
		style.ContentMarginBottom = 6;

		style.SetCornerRadiusAll(4);
		return style;
	}

	public static StyleBox CreateMinimapButtonStyle(bool isHover = false, bool isPressed = false)
	{
		var style = new StyleBoxFlat();
		style.BgColor = isPressed
			? new Color(0.08f, 0.12f, 0.18f, 0.95f)
			: (isHover ? new Color(0.16f, 0.18f, 0.22f, 0.92f) : new Color(0.08f, 0.09f, 0.11f, 0.85f));
		style.BorderColor = isPressed
			? ColorCyanGlow
			: (isHover ? ColorGold : new Color(0.45f, 0.38f, 0.28f, 0.75f));
		style.SetBorderWidthAll(1);
		style.SetCornerRadiusAll(3);
		style.ContentMarginLeft = 4;
		style.ContentMarginRight = 4;
		style.ContentMarginTop = 2;
		style.ContentMarginBottom = 2;
		if (isHover)
		{
			style.ShadowColor = new Color(0.95f, 0.82f, 0.55f, 0.25f);
			style.ShadowSize = 2;
		}
		else if (isPressed)
		{
			style.ShadowColor = new Color(0.15f, 0.65f, 1.0f, 0.35f);
			style.ShadowSize = 2;
		}

		return style;
	}

		public static Button CreateCloseButton()
		{
			var closeBtn = new Button();
			closeBtn.Name = "CloseBtn_Button";
			closeBtn.Flat = false;
			closeBtn.AddThemeConstantOverride("icon_max_width", 0);
			ApplyButtonStyleBoxesClearingFocus(closeBtn);
			ApplyButtonText(closeBtn, Godot.TranslationServer.Translate("CLOSE"), 14);
			closeBtn.CustomMinimumSize = new Vector2(130, 36);
			closeBtn.Visible = false;
			return closeBtn;
		}

	/// <summary>
	/// The small ✕ that dismisses an artwork overlay, such as the hotkey reference: no plate of its own, a translucent
	/// blue wash on hover and a stronger one while held.
	/// </summary>
	public static Button CreateOverlayCloseButton()
	{
		var btnClose = new Button();
		btnClose.Name = "BtnClose";
		btnClose.Text = "✕";
		btnClose.FocusMode = Control.FocusModeEnum.None;
		btnClose.AddThemeFontSizeOverride("font_size", 14);
		btnClose.AddThemeStyleboxOverride("normal", new StyleBoxEmpty());
		btnClose.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());

		var hover = new StyleBoxFlat { BgColor = new Color(0.15f, 0.65f, 1.0f, 0.25f) };
		hover.SetCornerRadiusAll(4);
		btnClose.AddThemeStyleboxOverride("hover", hover);

		var pressed = new StyleBoxFlat { BgColor = new Color(0.15f, 0.65f, 1.0f, 0.45f) };
		pressed.SetCornerRadiusAll(4);
		btnClose.AddThemeStyleboxOverride("pressed", pressed);

		btnClose.AddThemeColorOverride("font_color", Colors.White);
		btnClose.AddThemeColorOverride("font_hover_color", ColorGold);
		btnClose.AddThemeColorOverride("font_pressed_color", ColorCyanGlow);
		btnClose.AddThemeConstantOverride("outline_size", 2);
		btnClose.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.9f));
		return btnClose;
	}

	public static void ApplyBackButton(Button button, Action onPressed)
	{
		button.AddThemeConstantOverride("icon_max_width", 24);
		button.CustomMinimumSize = new Vector2(130, 42);
		button.Size = new Vector2(130, 42);
		ApplyButtonText(button, "◀ " + TranslationServer.Translate("BACK"), 16);
		ApplyButtonStyleBoxes(button);
		button.FocusMode = Control.FocusModeEnum.None;
		button.MouseFilter = Control.MouseFilterEnum.Stop;
		button.MouseEntered += () => GameContext.UserInterface?.PlayHoverSound();
		button.Pressed += () =>
		{
			GameContext.UserInterface?.PlayClickSound();
			onPressed?.Invoke();
		};
	}

	public static void ApplyButtonText(Button button, string text, int fontSize = 18)
	{
		button.Text = TranslationServer.Translate(text);
		var font = FontNorseBold;
		if (font != null)
		{
			button.AddThemeFontOverride("font", font);
		}

		button.AddThemeColorOverride("font_color", ColorGoldDull);
		button.AddThemeColorOverride("font_hover_color", ColorGold);
		button.AddThemeColorOverride("font_pressed_color", ColorGold);
		button.AddThemeColorOverride("font_focus_color", ColorGold);
		button.AddThemeFontSizeOverride("font_size", fontSize);
	}

	public static StyleBox CreateLightButtonNormal()
	{
		var style = new StyleBoxFlat();
		style.BgColor = new Color(0.13f, 0.15f, 0.18f, 1.0f);
		style.BorderColor = new Color(0.36f, 0.39f, 0.44f, 1.0f);
		style.SetBorderWidthAll(3);
		style.SetCornerRadiusAll(24);
		style.CornerDetail = 1;
		style.ContentMarginLeft = 32;
		style.ContentMarginRight = 32;
		style.ContentMarginTop = 8;
		style.ContentMarginBottom = 8;
		style.ShadowColor = new Color(0, 0, 0, 0.75f);
		style.ShadowSize = 6;
		style.ShadowOffset = new Vector2(0, 4);
		return style;
	}

	public static StyleBox CreateLightButtonHover()
	{
		var style = new StyleBoxFlat();
		style.BgColor = new Color(0.18f, 0.21f, 0.26f, 1.0f);
		style.BorderColor = new Color(0.50f, 0.65f, 0.85f, 1.0f);
		style.SetBorderWidthAll(3);
		style.SetCornerRadiusAll(24);
		style.CornerDetail = 1;
		style.ContentMarginLeft = 32;
		style.ContentMarginRight = 32;
		style.ContentMarginTop = 8;
		style.ContentMarginBottom = 8;
		style.ShadowColor = new Color(0, 0, 0, 0.85f);
		style.ShadowSize = 8;
		style.ShadowOffset = new Vector2(0, 5);
		return style;
	}

	public static StyleBox CreateLightButtonPressed()
	{
		var style = new StyleBoxFlat();
		style.BgColor = new Color(0.10f, 0.12f, 0.14f, 1.0f);
		style.BorderColor = new Color(0.20f, 0.75f, 1.0f, 1.0f);
		style.SetBorderWidthAll(3);
		style.SetCornerRadiusAll(24);
		style.CornerDetail = 1;
		style.ContentMarginLeft = 32;
		style.ContentMarginRight = 32;
		style.ContentMarginTop = 8;
		style.ContentMarginBottom = 8;
		style.ShadowColor = new Color(0, 0, 0, 0.6f);
		style.ShadowSize = 3;
		style.ShadowOffset = new Vector2(0, 2);
		return style;
	}

	public static void SetupHUDButton(Button btn, string tooltip, bool showHotkey = true)
	{
		btn.CustomMinimumSize = new Vector2(48, 48);
		btn.ClipContents = true;
		btn.AddThemeConstantOverride("icon_max_width", 42);

		if (showHotkey && tooltip.StartsWith('[') && tooltip.Contains(']'))
		{
			int end = tooltip.IndexOf(']');
			string hotkeyText = tooltip.Substring(1, end - 1);
			var hotkeyLabel = new Label();
			hotkeyLabel.Name = "HotkeyLabel";
			hotkeyLabel.Text = hotkeyText;
			hotkeyLabel.AddThemeFontSizeOverride("font_size", 12);
			hotkeyLabel.AddThemeColorOverride("font_color", ColorGold);
			hotkeyLabel.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.9f));
			hotkeyLabel.AddThemeConstantOverride("outline_size", 4);
			hotkeyLabel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopLeft);
			hotkeyLabel.OffsetLeft = 4;
			hotkeyLabel.OffsetTop = 3;
			hotkeyLabel.MouseFilter = Control.MouseFilterEnum.Ignore;
			btn.AddChild(hotkeyLabel);
		}
	}

	public static void ApplyMapEditorRandomizeButtonStyle(Button btn)
	{
		var mapEdBtnTex = Utils.TrimmedTextureLoader.LoadTrimmedTexture("res://Assets/UI/map_editor_button.webp");
		if (mapEdBtnTex != null)
		{
			var normalSb = new StyleBoxTexture { Texture = mapEdBtnTex, ContentMarginLeft = 12, ContentMarginRight = 12, ContentMarginTop = 4, ContentMarginBottom = 4 };
			var hoverSb = new StyleBoxTexture { Texture = mapEdBtnTex, ModulateColor = new Color(1.25f, 1.2f, 1.0f, 1.0f), ContentMarginLeft = 12, ContentMarginRight = 12, ContentMarginTop = 4, ContentMarginBottom = 4 };
			var pressedSb = new StyleBoxTexture { Texture = mapEdBtnTex, ModulateColor = new Color(0.85f, 0.8f, 0.7f, 1.0f), ContentMarginLeft = 12, ContentMarginRight = 12, ContentMarginTop = 4, ContentMarginBottom = 4 };
			btn.AddThemeStyleboxOverride("normal", normalSb);
			btn.AddThemeStyleboxOverride("hover", hoverSb);
			btn.AddThemeStyleboxOverride("pressed", pressedSb);
			btn.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
		}
		else
		{
			ApplyButtonStyleBoxes(btn);
		}
	}

	public static void ClearCache()
	{
		_unicodeIconTextureCache.Clear();
		_emptyBlackTexture = null;
	}

	public static Button CreateWebAiButton()
	{
		var btnWebAi = new Button();
		btnWebAi.Name = "BtnWebAi_Button";
		btnWebAi.Set("icon_max_width", 16);
		btnWebAi.AddThemeConstantOverride("icon_max_width", 16);
		btnWebAi.ExpandIcon = false;
		btnWebAi.IconAlignment = HorizontalAlignment.Center;
		btnWebAi.VerticalIconAlignment = VerticalAlignment.Center;
		if (ResourceLoader.Exists("res://Assets/UI/globe_icon.webp"))
		{
			btnWebAi.Icon = TrimmedTextureLoader.LoadTrimmedTexture("res://Assets/UI/globe_icon.webp");
		}
		else
		{
			btnWebAi.Text = "🌐";
		}

		btnWebAi.TooltipText = TranslationServer.Translate("Generate 3D Model with AI Online");
		btnWebAi.CustomMinimumSize = new Vector2(24, 24);
		btnWebAi.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
		btnWebAi.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
		btnWebAi.FocusMode = Control.FocusModeEnum.None;
		btnWebAi.Pressed += () => OS.ShellOpen("https://3d.hunyuanglobal.com");
		
		return btnWebAi;
	}

	/// <summary>One of the discovery screen's two tabs, drawn active or inactive.</summary>
	public static void ApplyCreatorDiscoveryTabButton(Button button, bool isActive)
	{
		button.AddThemeStyleboxOverride("normal", CreateCreatorDiscoveryTabButtonStyle(isActive, false, false));
		button.AddThemeStyleboxOverride("hover", CreateCreatorDiscoveryTabButtonStyle(isActive, true, false));
		button.AddThemeStyleboxOverride("pressed", CreateCreatorDiscoveryTabButtonStyle(isActive, false, true));
	}

	/// <summary>
	/// A button that draws no plate in any state because its artwork does the talking, such as a carousel arrow drawn as
	/// a texture child.
	/// </summary>
	public static void ApplyPlainButtonStates(Button button)
	{
		button.AddThemeStyleboxOverride("normal", new StyleBoxEmpty());
		button.AddThemeStyleboxOverride("hover", new StyleBoxEmpty());
		button.AddThemeStyleboxOverride("pressed", new StyleBoxEmpty());
		button.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
	}

	/// <summary>
	/// A button plate that sets only the fill, the border and the rounding. Its content inset is left to the border
	/// width, which is the default every small square button relies on.
	/// </summary>
	public static StyleBoxFlat CreateButtonPlate(Color bgColor, Color borderColor, int cornerRadius = 4)
	{
		var style = new StyleBoxFlat();
		style.BgColor = bgColor;
		style.BorderColor = borderColor;
		style.SetBorderWidthAll(1);
		style.SetCornerRadiusAll(cornerRadius);
		return style;
	}

	/// <summary>The light stone button the settings menu dresses its option rows with.</summary>
	public static void ApplyLightButton(Button button)
	{
		button.AddThemeStyleboxOverride("normal", CreateLightButtonNormal());
		button.AddThemeStyleboxOverride("hover", CreateLightButtonHover());
		button.AddThemeStyleboxOverride("pressed", CreateLightButtonPressed());
		button.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
	}

	/// <summary>The flat text button the discovery and lobby screens use for secondary actions.</summary>
	public static void ApplyFlatButton(Button button)
	{
		button.AddThemeStyleboxOverride("normal", CreateFlatButtonStyle(false, false));
		button.AddThemeStyleboxOverride("hover", CreateFlatButtonStyle(true, false));
		button.AddThemeStyleboxOverride("pressed", CreateFlatButtonStyle(false, true));
	}
}
