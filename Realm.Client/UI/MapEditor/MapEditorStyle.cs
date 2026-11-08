using Godot;
using Realm.Client.Utils;

namespace Realm.Client.UI.MapEditor;

/// <summary>
/// Theme overrides shared by the map editor's chrome. These were instance methods on
/// <see cref="MapEditorHUD"/> even though they only read their arguments and the FontAwesome
/// fallback font, which is why they take that font as a parameter here rather than reaching
/// for the HUD through a back-reference.
/// </summary>
internal static class MapEditorStyle
{
	/// <summary>
	/// Accordion headers: a textured plate when the map editor header art is available, and a
	/// flat panel of the same proportions when it is not, so an accordion that fails to load
	/// its texture still lines up with the ones that did.
	/// </summary>
	public static void AccordionHeader(Button btn, Font? font)
	{
		if (btn == null) return;
		btn.Flat = false;
		btn.CustomMinimumSize = new Vector2(0, 36);

		if (font != null)
		{
			btn.AddThemeFontOverride("font", font);
		}

		var headerTex = TrimmedTextureLoader.LoadTrimmedTexture("res://Assets/UI/map_editor_options_header.webp");
		if (headerTex != null)
		{
			var headerNormal = new StyleBoxTexture();
			headerNormal.Texture = headerTex;
			headerNormal.TextureMarginLeft = 0;
			headerNormal.TextureMarginRight = 0;
			headerNormal.TextureMarginTop = 0;
			headerNormal.TextureMarginBottom = 0;
			headerNormal.ContentMarginLeft = 12;
			headerNormal.ContentMarginRight = 12;
			headerNormal.ContentMarginTop = 6;
			headerNormal.ContentMarginBottom = 6;

			var headerHover = new StyleBoxTexture();
			headerHover.Texture = headerTex;
			headerHover.ModulateColor = new Color(1.2f, 1.15f, 0.9f, 1.0f);
			headerHover.TextureMarginLeft = 0;
			headerHover.TextureMarginRight = 0;
			headerHover.TextureMarginTop = 0;
			headerHover.TextureMarginBottom = 0;
			headerHover.ContentMarginLeft = 12;
			headerHover.ContentMarginRight = 12;
			headerHover.ContentMarginTop = 6;
			headerHover.ContentMarginBottom = 6;

			btn.AddThemeStyleboxOverride("normal", headerNormal);
			btn.AddThemeStyleboxOverride("hover", headerHover);
			btn.AddThemeStyleboxOverride("pressed", headerHover);
		}
		else
		{
			var headerNormal = new StyleBoxFlat();
			headerNormal.BgColor = new Color(0.18f, 0.16f, 0.14f, 0.95f);
			headerNormal.BorderColor = new Color(0.40f, 0.34f, 0.24f, 0.9f);
			headerNormal.SetBorderWidthAll(1);
			headerNormal.CornerRadiusTopLeft = 4;
			headerNormal.CornerRadiusTopRight = 4;
			headerNormal.CornerRadiusBottomLeft = 2;
			headerNormal.CornerRadiusBottomRight = 2;
			headerNormal.ContentMarginLeft = 10;
			headerNormal.ContentMarginRight = 10;

			btn.AddThemeStyleboxOverride("normal", headerNormal);
			btn.AddThemeStyleboxOverride("hover", headerNormal);
			btn.AddThemeStyleboxOverride("pressed", headerNormal);
		}

		btn.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
		btn.AddThemeFontSizeOverride("font_size", 12);
		btn.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		btn.AddThemeColorOverride("font_hover_color", new Color(1f, 0.95f, 0.8f));
		btn.Alignment = HorizontalAlignment.Left;
	}

	/// <summary>
	/// Square icon buttons: takes the glyph, not the button's existing text, because the caller
	/// owns what the icon means and the font is the same FontAwesome set for all of them.
	/// </summary>
	public static void IconButton(Button btn, string iconText, string tooltipText, Font? font)
	{
		if (btn == null) return;
		btn.Text = iconText;
		btn.TooltipText = tooltipText;
		btn.Flat = false;
		btn.CustomMinimumSize = new Vector2(30, 30);
		btn.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;

		if (font != null)
		{
			btn.AddThemeFontOverride("font", font);
		}

		var btnNormal = new StyleBoxFlat();
		btnNormal.BgColor = new Color(0.16f, 0.15f, 0.13f, 0.85f);
		btnNormal.BorderColor = new Color(0.38f, 0.32f, 0.24f, 0.7f);
		btnNormal.SetBorderWidthAll(1);
		btnNormal.SetCornerRadiusAll(4);

		var btnHover = new StyleBoxFlat();
		btnHover.BgColor = new Color(0.28f, 0.25f, 0.19f, 0.95f);
		btnHover.BorderColor = UIStyle.ColorGold;
		btnHover.SetBorderWidthAll(1);
		btnHover.SetCornerRadiusAll(4);

		var btnPressed = new StyleBoxFlat();
		btnPressed.BgColor = new Color(0.35f, 0.30f, 0.22f, 0.98f);
		btnPressed.BorderColor = UIStyle.ColorGold;
		btnPressed.SetBorderWidthAll(1);

		btn.AddThemeStyleboxOverride("normal", btnNormal);
		btn.AddThemeStyleboxOverride("hover", btnHover);
		btn.AddThemeStyleboxOverride("pressed", btnPressed);
		btn.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
		btn.AddThemeFontSizeOverride("font_size", 14);
		btn.AddThemeColorOverride("font_color", new Color(0.95f, 0.90f, 0.82f));
		btn.AddThemeColorOverride("font_hover_color", UIStyle.ColorGold);
		btn.Alignment = HorizontalAlignment.Center;
	}
}
