using Godot;

namespace Realm.Client.UI;

/// <summary>
/// The plates the map editor draws its own chrome with: accordion headers, icon buttons, sub-panels, value badges,
/// popup menus and the list rows. They lived as literal styleboxes inside <see cref="MapEditor.MapEditorStyle"/>,
/// which is where the roles were decided; the values are the same ones, moved here so a panel and a dialog that
/// want the same plate can share it instead of copying the fields.
/// </summary>
public static partial class UIStyle
{
	/// <summary>
	/// The flat accordion header plate, used when the header art did not load. The textured version is
	/// <see cref="MapEditor.MapEditorStyle.AccordionHeader"/>'s own, because it needs the texture as well.
	/// </summary>
	public static StyleBoxFlat CreateEditorAccordionHeader()
	{
		var style = new StyleBoxFlat();
		style.BgColor = new Color(0.18f, 0.16f, 0.14f, 0.95f);
		style.BorderColor = new Color(0.40f, 0.34f, 0.24f, 0.9f);
		style.SetBorderWidthAll(1);
		style.CornerRadiusTopLeft = 4;
		style.CornerRadiusTopRight = 4;
		style.CornerRadiusBottomLeft = 2;
		style.CornerRadiusBottomRight = 2;
		style.ContentMarginLeft = 10;
		style.ContentMarginRight = 10;
		return style;
	}

	/// <summary>Square glyph button, at rest.</summary>
	public static StyleBoxFlat CreateEditorIconButtonNormal()
	{
		var style = new StyleBoxFlat();
		style.BgColor = new Color(0.16f, 0.15f, 0.13f, 0.85f);
		style.BorderColor = new Color(0.38f, 0.32f, 0.24f, 0.7f);
		style.SetBorderWidthAll(1);
		style.SetCornerRadiusAll(4);
		return style;
	}

	/// <summary>Square glyph button, hovered.</summary>
	public static StyleBoxFlat CreateEditorIconButtonHover()
	{
		var style = new StyleBoxFlat();
		style.BgColor = new Color(0.28f, 0.25f, 0.19f, 0.95f);
		style.BorderColor = ColorGold;
		style.SetBorderWidthAll(1);
		style.SetCornerRadiusAll(4);
		return style;
	}

	/// <summary>Square glyph button, held down. It carries no corner radius, as it always has.</summary>
	public static StyleBoxFlat CreateEditorIconButtonPressed()
	{
		var style = new StyleBoxFlat();
		style.BgColor = new Color(0.35f, 0.30f, 0.22f, 0.98f);
		style.BorderColor = ColorGold;
		style.SetBorderWidthAll(1);
		return style;
	}

	/// <summary>The inset panel behind a group of controls inside an editor card.</summary>
	public static StyleBoxFlat CreateEditorSubPanel()
	{
		var style = new StyleBoxFlat();
		style.BgColor = new Color(0.13f, 0.12f, 0.10f, 0.7f);
		style.BorderColor = new Color(0.32f, 0.27f, 0.20f, 0.6f);
		style.SetBorderWidthAll(1);
		style.SetCornerRadiusAll(4);
		style.ContentMarginLeft = 10;
		style.ContentMarginRight = 14;
		style.ContentMarginTop = 6;
		style.ContentMarginBottom = 6;
		return style;
	}

	/// <summary>The gold-edged plate behind a live value on a card.</summary>
	public static StyleBoxFlat CreateEditorValueBadge()
	{
		var style = new StyleBoxFlat();
		style.BgColor = new Color(0.18f, 0.16f, 0.13f, 0.95f);
		style.BorderColor = ColorGold;
		style.SetBorderWidthAll(1);
		style.SetCornerRadiusAll(3);
		style.ContentMarginLeft = 6;
		style.ContentMarginRight = 6;
		style.ContentMarginTop = 2;
		style.ContentMarginBottom = 2;
		return style;
	}

	/// <summary>
	/// One row of an editor dropdown's suggestion list: invisible until hovered or held, and sharing the resting plate
	/// while disabled.
	/// </summary>
	public static void ApplyEditorSuggestionRow(Button row)
	{
		row.AddThemeStyleboxOverride("normal", CreateCardPlate(new Color(0, 0, 0, 0), new Color(0, 0, 0, 0), 0, 3, 5, 5, 1, 1));
		row.AddThemeStyleboxOverride("hover", CreateEditorSuggestionRowHover());
		row.AddThemeStyleboxOverride("pressed", CreateEditorSuggestionRowHover());
		row.AddThemeStyleboxOverride("disabled", CreateCardPlate(new Color(0, 0, 0, 0), new Color(0, 0, 0, 0), 0, 3, 5, 5, 1, 1));
		row.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
	}

	/// <summary>The gold-edged plate a suggestion row takes while the pointer is over it or it is held down.</summary>
	private static StyleBoxFlat CreateEditorSuggestionRowHover()
	{
		return CreateCardPlate(new Color(0.16f, 0.18f, 0.22f, 0.95f), ColorGold, 1, 3, 5, 5, 1, 1);
	}

	/// <summary>The lit plate under the entity palette's selected category button.</summary>
	public static StyleBoxFlat CreateEditorPaletteCategoryActive()
	{
		var style = new StyleBoxFlat();
		style.BgColor = new Color(0.15f, 0.45f, 0.7f, 0.8f);
		style.BorderColor = ColorCyanGlow;
		style.SetBorderWidthAll(2);
		style.SetCornerRadiusAll(4);
		return style;
	}

	/// <summary>The panel behind an editor option button's popup list.</summary>
	public static StyleBoxFlat CreateEditorPopupPanel()
	{
		var style = new StyleBoxFlat();
		style.BgColor = new Color(0.14f, 0.13f, 0.11f, 0.98f);
		style.BorderColor = ColorGold;
		style.SetBorderWidthAll(1);
		style.SetCornerRadiusAll(4);
		style.ContentMarginLeft = 8;
		style.ContentMarginRight = 8;
		style.ContentMarginTop = 6;
		style.ContentMarginBottom = 6;
		return style;
	}

	/// <summary>List row in an editor panel, at rest: a bottom rule only, no box.</summary>
	public static StyleBoxFlat CreateEditorRowButtonNormal()
	{
		var style = new StyleBoxFlat();
		style.BgColor = new Color(0.14f, 0.13f, 0.11f, 0.4f);
		style.BorderColor = new Color(0.24f, 0.21f, 0.17f, 0.5f);
		style.BorderWidthBottom = 1;
		style.ContentMarginLeft = 10;
		style.ContentMarginRight = 10;
		return style;
	}

	/// <summary>List row, hovered: the bottom rule turns gold.</summary>
	public static StyleBoxFlat CreateEditorRowButtonHover()
	{
		var style = new StyleBoxFlat();
		style.BgColor = new Color(0.25f, 0.22f, 0.18f, 0.9f);
		style.BorderColor = ColorGold;
		style.BorderWidthBottom = 1;
		style.ContentMarginLeft = 10;
		style.ContentMarginRight = 10;
		return style;
	}

	/// <summary>List row, held down: the border closes around all four sides.</summary>
	public static StyleBoxFlat CreateEditorRowButtonPressed()
	{
		var style = new StyleBoxFlat();
		style.BgColor = new Color(0.30f, 0.26f, 0.20f, 0.95f);
		style.BorderColor = ColorGold;
		style.SetBorderWidthAll(1);
		style.ContentMarginLeft = 10;
		style.ContentMarginRight = 10;
		return style;
	}

	/// <summary>
	/// The tinted plate behind a palette row. The two terrain rows want the same plate in different colours, which is
	/// why the colours are the arguments and everything else is fixed.
	/// </summary>
	public static StyleBoxFlat CreateEditorRowPlate(Color bgColor, Color borderColor)
	{
		var style = new StyleBoxFlat();
		style.BgColor = bgColor;
		style.BorderColor = borderColor;
		style.SetBorderWidthAll(1);
		style.SetCornerRadiusAll(4);
		style.ContentMarginLeft = 8;
		style.ContentMarginRight = 8;
		style.ContentMarginTop = 4;
		style.ContentMarginBottom = 4;
		return style;
	}
}
