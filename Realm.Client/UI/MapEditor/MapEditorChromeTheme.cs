using Godot;
using Realm.Client.Utils;
using static Godot.Control;

namespace Realm.Client.UI.MapEditor;

/// <summary>
/// The editor's frame rather than its cards: the two sidebar columns, the floating top bar and the labels on it. These
/// nodes are laid out in the scene, so the passes here take the nodes they style rather than building them, and the
/// editor keeps the single list of what it hands over.
/// </summary>
public static class MapEditorChromeTheme
{
	private const float ScrollSideMargin = 10f;
	private const float DefaultScrollbarWidth = 14f;
	private const float MaximumViewportFraction = 0.5f;

	/// <summary>
	/// Makes the sidebar columns click-through and blank. The columns are decoration behind the cards, so a click that
	/// lands on the column rather than a card must reach the 3D viewport underneath, and the column itself draws no plate
	/// of its own: leaving the theme's plate on it is what washes a grey background behind both card stacks. Both
	/// scrollers keep the default <c>Auto</c> bars, which stay hidden while the cards fit; the panel is widened to the
	/// cards by <see cref="MeasureColumnWidth"/> instead of any card being cropped, and
	/// <see cref="MapEditorHUD.TryScrollSidebarUnderMouse"/> scrolls them with the wheel because the cards swallow it.
	/// </summary>
	public static void SidebarColumns(Node owner, Panel? leftPanel, Panel? rightPanel)
	{
		ClearPlate(leftPanel);
		ClearPlate(rightPanel);

		IgnoreMouse(leftPanel);
		IgnoreMouse(rightPanel);

		MakeScrollable(owner.GetNodeOrNull<ScrollContainer>("LeftSlidePanel/LeftScroll"));
		MakeScrollable(owner.GetNodeOrNull<ScrollContainer>("RightSlidePanel/RightScroll"));

		var leftVBox = owner.GetNodeOrNull<VBoxContainer>("LeftSlidePanel/LeftScroll/LeftVBox");
		if (leftVBox != null)
		{
			leftVBox.MouseFilter = MouseFilterEnum.Ignore;
			leftVBox.AddThemeConstantOverride("separation", 14);
		}

		var rightVBox = owner.GetNodeOrNull<VBoxContainer>("RightSlidePanel/RightScroll/AccordionContainer");
		if (rightVBox != null)
		{
			rightVBox.MouseFilter = MouseFilterEnum.Ignore;
			rightVBox.AddThemeConstantOverride("separation", 14);
		}
	}

	private static void IgnoreMouse(Control? control)
	{
		if (control != null) control.MouseFilter = MouseFilterEnum.Ignore;
	}

	private static void MakeScrollable(ScrollContainer? scroller)
	{
		if (scroller == null) return;

		scroller.HorizontalScrollMode = ScrollContainer.ScrollMode.Auto;
		scroller.VerticalScrollMode = ScrollContainer.ScrollMode.Auto;
	}

	/// <summary>
	/// The width a sidebar panel needs so that its cards are drawn whole: the column's own minimum width plus the
	/// scroller's side margins, plus the vertical scrollbar the column would show when it is taller than the screen, so
	/// that the bar never covers the last few pixels of a card. The result is never narrower than the panel's design
	/// width and never wider than <see cref="MaximumViewportFraction"/> of the screen, which keeps one pathologically
	/// long label from covering the viewport.
	/// </summary>
	public static float MeasureColumnWidth(Control? column, ScrollContainer? scroller, float minimumWidth, float viewportWidth)
	{
		if (column == null) return minimumWidth;

		float scrollbarWidth = scroller?.GetVScrollBar()?.GetCombinedMinimumSize().X ?? 0f;
		if (scrollbarWidth < 1f) scrollbarWidth = DefaultScrollbarWidth;

		float width = Mathf.Max(minimumWidth, column.GetCombinedMinimumSize().X + ScrollSideMargin * 2f + scrollbarWidth);
		float maximumWidth = viewportWidth * MaximumViewportFraction;

		return maximumWidth > minimumWidth ? Mathf.Min(width, maximumWidth) : width;
	}

	/// <summary>
	/// Gives a sidebar panel the width <see cref="MeasureColumnWidth"/> asked for, by setting the offset the panel's
	/// anchor does not control, so the column grows away from the screen edge it is pinned to. A collapsed panel is
	/// left at its collapsed offsets and drops its minimum width, because a minimum larger than the collapsed offsets
	/// would stop the panel collapsing at all.
	/// </summary>
	public static void ApplyColumnWidth(Panel? panel, bool anchoredRight, bool expanded, float width)
	{
		if (panel == null) return;

		panel.CustomMinimumSize = expanded ? new Vector2(width, 0f) : Vector2.Zero;
		if (!expanded) return;

		if (anchoredRight)
		{
			panel.OffsetLeft = -width;
			panel.OffsetRight = 0f;
			return;
		}

		panel.OffsetLeft = 0f;
		panel.OffsetRight = width;
	}

	private static void ClearPlate(Panel? panel)
	{
		if (panel != null) panel.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
	}

	/// <summary>
	/// The floating top bar plate, which the measurement readout shares so the two read as one strip. The title label on
	/// it is hidden because the map name is drawn elsewhere.
	/// </summary>
	public static void TopBar(Node owner, PanelContainer? topBar, Control? measurementPanel)
	{
		var titleLbl = topBar?.GetNodeOrNull<Label>("HBox/TitleLabel") ?? owner.GetNodeOrNull<Label>("BottomCenterBar/TopBar/HBox/TitleLabel");
		if (titleLbl != null) titleLbl.Visible = false;

		StyleBox barStyle = CreateTopBarStylebox();

		if (topBar != null)
		{
			topBar.AddThemeStyleboxOverride("panel", barStyle);
		}

		if (measurementPanel != null)
		{
			measurementPanel.AddThemeStyleboxOverride("panel", barStyle);
		}

		var hBox = topBar?.GetNodeOrNull<HBoxContainer>("HBox") ?? owner.GetNodeOrNull<HBoxContainer>("BottomCenterBar/TopBar/HBox");
		if (hBox != null)
		{
			hBox.Alignment = BoxContainer.AlignmentMode.Center;
		}
	}

	/// <summary>
	/// The bar's textured plate, falling back to a flat panel of the same proportions when the artwork is missing so the
	/// bar does not change height between machines.
	/// </summary>
	private static StyleBox CreateTopBarStylebox()
	{
		var posTexture = TrimmedTextureLoader.LoadTrimmedTexture("res://Assets/UI/map_editor_pos.webp");
		if (posTexture != null)
		{
			var posStyle = new StyleBoxTexture();
			posStyle.Texture = posTexture;
			posStyle.TextureMarginLeft = 0;
			posStyle.TextureMarginRight = 0;
			posStyle.TextureMarginTop = 0;
			posStyle.TextureMarginBottom = 0;
			posStyle.ContentMarginLeft = 16;
			posStyle.ContentMarginRight = 16;
			posStyle.ContentMarginTop = 6;
			posStyle.ContentMarginBottom = 6;
			return posStyle;
		}

		return UIStyle.CreateCardPlate(new Color(0.12f, 0.14f, 0.18f, 0.75f), UIStyle.ColorGold, 1, 6, 16, 16, 6, 6);
	}

	/// <summary>
	/// The gold status and measurement readouts, plus the feedback line, which is a fading label laid out at title size
	/// so it can flash without shifting the bar. The FontAwesome font carries the glyphs these labels prefix.
	/// </summary>
	public static void StatusLabels(Label? status, Label? measurement, Label? feedback, Font? font)
	{
		ApplyReadoutStyle(status, font);
		ApplyReadoutStyle(measurement, font);

		if (feedback != null) UIStyle.ApplyTitle(feedback, "", 24);
	}

	private static void ApplyReadoutStyle(Label? label, Font? font)
	{
		if (label == null) return;

		label.HorizontalAlignment = HorizontalAlignment.Left;
		label.VerticalAlignment = VerticalAlignment.Center;
		label.AddThemeFontSizeOverride("font_size", 13);
		label.AddThemeColorOverride("font_color", UIStyle.ColorGold);

		if (font != null)
		{
			label.AddThemeFontOverride("font", font);
		}
	}
}
