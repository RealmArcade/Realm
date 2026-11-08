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
	public static StyleBoxFlat CreateTextInput(bool hasFocus = false)
	{
		var style = new StyleBoxFlat();
		style.BgColor = new Color(0.05f, 0.06f, 0.07f, 0.9f);
		style.BorderColor = hasFocus ? ColorCyanGlow : new Color(0.34f, 0.3f, 0.24f);
		style.SetBorderWidthAll(hasFocus ? 2 : 1);
		style.SetCornerRadiusAll(4);
		style.ContentMarginLeft = 12;
		style.ContentMarginRight = 12;
		style.ContentMarginTop = 6;
		style.ContentMarginBottom = 6;
		return style;
	}

	public static void ApplyTextInput(LineEdit edit, int fontSize = 15)
	{
		edit.AddThemeStyleboxOverride("normal", CreateTextInput(false));
		edit.AddThemeStyleboxOverride("focus", CreateTextInput(true));
		edit.AddThemeStyleboxOverride("read_only", CreateTextInput(false));
		edit.AddThemeColorOverride("font_color", new Color(0.93f, 0.90f, 0.84f));
		edit.AddThemeColorOverride("font_uneditable_color", new Color(0.62f, 0.60f, 0.55f));
		edit.AddThemeColorOverride("font_placeholder_color", new Color(0.55f, 0.52f, 0.47f));
		edit.AddThemeColorOverride("caret_color", ColorGold);
		edit.AddThemeColorOverride("selection_color", new Color(0.15f, 0.65f, 1.0f, 0.35f));
		edit.AddThemeFontSizeOverride("font_size", fontSize);
		edit.FocusMode = Control.FocusModeEnum.All;
	}

	public static StyleBox CreateCustomLobbySearchInput(bool hasFocus = false)
	{
		var tex = LoadTextureWithFallback("res://Assets/UI/custom_lobby_search.webp");
		if (tex != null)
		{
			var style = new StyleBoxTexture();
			style.Texture = tex;
			style.TextureMarginLeft = 0;
			style.TextureMarginRight = 0;
			style.TextureMarginTop = 0;
			style.TextureMarginBottom = 0;
			style.ContentMarginLeft = 34;
			style.ContentMarginRight = 86;
			style.ContentMarginTop = 8;
			style.ContentMarginBottom = 8;
			style.AxisStretchHorizontal = StyleBoxTexture.AxisStretchMode.Stretch;
			style.AxisStretchVertical = StyleBoxTexture.AxisStretchMode.Stretch;
			style.ModulateColor = hasFocus
				? new Color(1.12f, 1.12f, 1.16f)
				: Colors.White;

			return style;
		}

		return CreateTextInput(hasFocus);
	}


	public static StyleBoxFlat CreateDropdownStyle(bool isHover = false, bool isPressed = false)
	{
		var style = new StyleBoxFlat();
		style.BgColor = new Color(0.08f, 0.09f, 0.11f, 0.95f);
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
			style.BorderColor = new Color(0.45f, 0.38f, 0.3f);
		}

		style.SetBorderWidthAll(1);
		style.ContentMarginLeft = 14;
		style.ContentMarginRight = 28;
		style.ContentMarginTop = 8;
		style.ContentMarginBottom = 8;
		style.SetCornerRadiusAll(4);
		return style;
	}

	public static void ApplyCheckboxStyle(CheckBox cb)
	{
		var checkedTex = TrimmedTextureLoader.LoadTrimmedTexture("res://Assets/UI/checked_box.webp");
		var uncheckedTex = TrimmedTextureLoader.LoadTrimmedTexture("res://Assets/UI/unchecked_box.webp");

		cb.AddThemeIconOverride("checked", checkedTex);
		cb.AddThemeIconOverride("unchecked", uncheckedTex);
		cb.AddThemeIconOverride("checked_disabled", checkedTex);
		cb.AddThemeIconOverride("unchecked_disabled", uncheckedTex);

		cb.AddThemeColorOverride("font_color", ColorGold);
		cb.AddThemeColorOverride("font_hover_color", ColorGold);
		cb.AddThemeColorOverride("font_pressed_color", ColorGold);
		cb.AddThemeColorOverride("font_hover_pressed_color", ColorGold);
		cb.AddThemeFontSizeOverride("font_size", 14);
		cb.AddThemeConstantOverride("icon_max_width", 20);
	}

	/// <summary>
	/// The plain framed text field, drawn with the focused plate while it holds focus. Both text controls Godot offers
	/// take the same pair under the same names, so either one can be passed.
	/// </summary>
	public static void ApplyTextInput(Control edit)
	{
		edit.AddThemeStyleboxOverride("normal", CreateTextInput(false));
		edit.AddThemeStyleboxOverride("focus", CreateTextInput(true));
	}

	/// <summary>
	/// A dropdown that is dressed like a text field rather than like a button: the pointed plate while closed, the
	/// focused one while open or hovered.
	/// </summary>
	public static void ApplyInputDropdown(Button option)
	{
		option.AddThemeStyleboxOverride("normal", CreateTextInput(false));
		option.AddThemeStyleboxOverride("hover", CreateTextInput(true));
		option.AddThemeStyleboxOverride("pressed", CreateTextInput(true));
	}

	/// <summary>
	/// A palette swatch. The plate is the colour itself, ringed gold while it is the chosen one, and every state draws
	/// the ring because a swatch is bigger than its pointer affordance.
	/// </summary>
	public static void ApplyPaletteSwatch(Button swatch, Color color, bool highlighted)
	{
		swatch.AddThemeStyleboxOverride("normal", CreatePaletteSwatchPlate(color, highlighted));
		swatch.AddThemeStyleboxOverride("hover", CreatePaletteSwatchPlate(color, true));
		swatch.AddThemeStyleboxOverride("pressed", CreatePaletteSwatchPlate(color, true));
		swatch.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
	}

	public static StyleBoxFlat CreatePaletteSwatchPlate(Color color, bool highlighted)
	{
		var style = new StyleBoxFlat();
		style.BgColor = color;
		style.BorderColor = highlighted ? ColorGold : new Color(0.35f, 0.35f, 0.4f);
		style.SetBorderWidthAll(highlighted ? 3 : 1);
		style.SetCornerRadiusAll(4);
		return style;
	}

	/// <summary>The gold-on-graphite bar a progress bar fills, matching the sliders beside it.</summary>
	public static void ApplyProgressBar(ProgressBar bar)
	{
		bar.AddThemeStyleboxOverride("background", CreateSliderTrack());
		bar.AddThemeStyleboxOverride("fill", CreateSliderFill());
	}

	public static StyleBoxFlat CreateSliderTrack()
	{
		var style = new StyleBoxFlat();
		style.BgColor = new Color(0.05f, 0.05f, 0.07f, 1.0f);
		style.BorderColor = new Color(0.3f, 0.3f, 0.35f, 1.0f);
		style.SetBorderWidthAll(1);
		style.ExpandMarginTop = 2;
		style.ExpandMarginBottom = 2;
		style.SetCornerRadiusAll(2);
		return style;
	}

	/// <summary>
	/// The gold-on-graphite bar a slider sits in: the track, the filled portion and the same fill while the grabber is
	/// held.
	/// </summary>
	public static void ApplySlider(HSlider slider)
	{
		slider.AddThemeStyleboxOverride("slider", CreateSliderTrack());
		slider.AddThemeStyleboxOverride("grabber_area", CreateSliderFill());
		slider.AddThemeStyleboxOverride("grabber_area_highlight", CreateSliderFill());
	}

	public static StyleBoxFlat CreateSliderFill()
	{
		var style = new StyleBoxFlat();
		style.BgColor = ColorGoldDull;
		style.ExpandMarginTop = 2;
		style.ExpandMarginBottom = 2;
		style.SetCornerRadiusAll(2);
		return style;
	}

		public static (HBoxContainer row, ColorPickerButton btn) CreateColorPickerRow(string labelText, Color initialColor, Action<Color> onColorChanged)
		{
			var row = new HBoxContainer();
			row.Name = "RowTint_HBoxContainer";
			row.AddThemeConstantOverride("separation", 8);

			var lbl = new Label();
			lbl.Name = "LblTint_Label";
			lbl.Text = TranslationServer.Translate(labelText);
			lbl.CustomMinimumSize = new Vector2(140, 0);
			lbl.AddThemeFontSizeOverride("font_size", 11);
			row.AddChild(lbl);

			var btn = new ColorPickerButton();
			btn.CustomMinimumSize = new Vector2(90, 24);
			btn.Color = initialColor;
			btn.ColorChanged += (newCol) => onColorChanged?.Invoke(newCol);
			row.AddChild(btn);

			return (row, btn);
		}

	public static StyleBox CreateLightDropdownNormal()
	{
		var style = new StyleBoxFlat();
		style.BgColor = new Color(0.20f, 0.19f, 0.18f, 1.0f);
		style.BorderColor = new Color(0.40f, 0.36f, 0.30f, 1.0f);
		style.SetBorderWidthAll(1);
		style.SetCornerRadiusAll(3);
		style.ContentMarginLeft = 10;
		style.ContentMarginRight = 10;
		style.ContentMarginTop = 5;
		style.ContentMarginBottom = 5;
		return style;
	}

	public static StyleBox CreateLightDropdownHover()
	{
		var style = new StyleBoxFlat();
		style.BgColor = new Color(0.26f, 0.25f, 0.23f, 1.0f);
		style.BorderColor = new Color(0.58f, 0.48f, 0.32f, 1.0f);
		style.SetBorderWidthAll(1);
		style.SetCornerRadiusAll(3);
		style.ContentMarginLeft = 10;
		style.ContentMarginRight = 10;
		style.ContentMarginTop = 5;
		style.ContentMarginBottom = 5;
		return style;
	}

	public static StyleBox CreateLightDropdownPressed()
	{
		var style = new StyleBoxFlat();
		style.BgColor = new Color(0.16f, 0.15f, 0.14f, 1.0f);
		style.BorderColor = new Color(0.15f, 0.65f, 1.0f, 1.0f);
		style.SetBorderWidthAll(1);
		style.SetCornerRadiusAll(3);
		style.ContentMarginLeft = 10;
		style.ContentMarginRight = 10;
		style.ContentMarginTop = 5;
		style.ContentMarginBottom = 5;
		return style;
	}

	/// <summary>The light dropdown the settings menu uses for its option rows, focus ring cleared.</summary>
	public static void ApplyLightDropdown(OptionButton option)
	{
		option.AddThemeStyleboxOverride("normal", CreateLightDropdownNormal());
		option.AddThemeStyleboxOverride("hover", CreateLightDropdownHover());
		option.AddThemeStyleboxOverride("pressed", CreateLightDropdownPressed());
		option.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
	}

	/// <summary>The stone-tracked slider and its fill, on both the resting and held states.</summary>
	public static void ApplyLightSlider(HSlider slider)
	{
		var track = CreateLightSliderTrack();
		var fill = CreateLightSliderFill();

		slider.AddThemeStyleboxOverride("slider", track);
		slider.AddThemeStyleboxOverride("grabber_area", fill);
		slider.AddThemeStyleboxOverride("grabber_area_highlight", fill);
	}

	public static StyleBox CreateLightSliderTrack()
	{
		var style = new StyleBoxFlat();
		style.BgColor = new Color(0.28f, 0.28f, 0.32f, 1.0f);
		style.BorderColor = new Color(0.20f, 0.20f, 0.22f, 1.0f);
		style.SetBorderWidthAll(1);
		style.ExpandMarginTop = 2;
		style.ExpandMarginBottom = 2;
		style.SetCornerRadiusAll(3);
		return style;
	}

	public static StyleBox CreateLightSliderFill()
	{
		var style = new StyleBoxFlat();
		style.BgColor = new Color(0.38f, 0.38f, 0.42f, 1.0f);
		style.ExpandMarginTop = 2;
		style.ExpandMarginBottom = 2;
		style.SetCornerRadiusAll(3);
		return style;
	}

	/// <summary>An option dropdown's three states, which every screen that owns one applies the same way.</summary>
	public static void ApplyDropdownButton(Control control)
	{
		control.AddThemeStyleboxOverride("normal", CreateDropdownStyle(false, false));
		control.AddThemeStyleboxOverride("hover", CreateDropdownStyle(true, false));
		control.AddThemeStyleboxOverride("pressed", CreateDropdownStyle(false, true));
	}
}
