using Godot;
using Godot.Collections;
using static Godot.Control;

namespace Realm.Client.UI.MapEditor;

/// <summary>
/// One editor card: the accordion header that names it, the content box beneath it, and how tall the content may grow
/// before it scrolls rather than overflow.
/// </summary>
internal readonly record struct MapEditorCard(Control? Accordion, Control? Content, float ScrollMaxHeight = 300f, bool AllowExpand = true);

/// <summary>
/// The card chrome shared by every panel in the editor sidebar: a header panel with a darker content box under it and
/// a scroll container inside that. The editor names its cards once, so a new card is styled by all of the passes
/// instead of being added to three of them.
/// </summary>
internal static class MapEditorCardTheme
{
	private const float CardWidth = 260f;
	private const float ContentWidth = 245f;

	/// <summary>Sizes the header and gives it the card background. The background is only created once.</summary>
	public static void StyleAccordion(Control? accordion)
	{
		if (accordion == null) return;

		accordion.CustomMinimumSize = new Vector2(CardWidth, 0);

		var cardStyle = UIStyle.CreateEditorCardHeaderPlate();

		Panel bgPanel = accordion.GetNodeOrNull<Panel>("CardBG");
		if (bgPanel == null)
		{
			bgPanel = new Panel();
			bgPanel.Name = "CardBG";
			bgPanel.ShowBehindParent = true;
			bgPanel.MouseFilter = MouseFilterEnum.Ignore;
			bgPanel.SetAnchorsPreset(LayoutPreset.FullRect);
			accordion.AddChild(bgPanel);
			accordion.MoveChild(bgPanel, 0);
		}

		bgPanel.AddThemeStyleboxOverride("panel", cardStyle);
	}

	/// <summary>
	/// Styles the box under a header. A panel container takes the stylebox directly; a plain box gets a background
	/// panel behind it, because it cannot draw one itself.
	/// </summary>
	public static void StyleContent(Control? content)
	{
		if (content == null) return;

		var contentStyle = UIStyle.CreateEditorCardBodyPlate();

		if (content is PanelContainer pc)
		{
			pc.AddThemeStyleboxOverride("panel", contentStyle);
		}
		else if (content is VBoxContainer vbox)
		{
			vbox.AddThemeConstantOverride("separation", 2);

			Panel bgPanel = vbox.GetNodeOrNull<Panel>("ContentBG");
			if (bgPanel == null)
			{
				bgPanel = new Panel();
				bgPanel.Name = "ContentBG";
				bgPanel.ShowBehindParent = true;
				bgPanel.MouseFilter = MouseFilterEnum.Ignore;
				bgPanel.SetAnchorsPreset(LayoutPreset.FullRect);
				vbox.AddChild(bgPanel);
				vbox.MoveChild(bgPanel, 0);
			}

			bgPanel.AddThemeStyleboxOverride("panel", contentStyle);
		}
	}

	/// <summary>
	/// Wraps the content in a scroll container the first time it runs, then sizes that container to the content's
	/// minimum height up to <paramref name="maxHeight"/>, showing the expand button only when the content overflows.
	/// </summary>
	public static void SetupScroll(Control? content, float maxHeight, bool allowExpand)
	{
		if (content == null) return;

		content.CustomMinimumSize = new Vector2(ContentWidth, 0);

		ScrollContainer scroll = content.GetNodeOrNull<ScrollContainer>("CardScroll");
		if (scroll == null)
		{
			scroll = CreateScroll(allowExpand);
			ReparentIntoInnerVBox(content, scroll);
			content.AddChild(scroll);
		}

		SizeScroll(content, scroll, maxHeight, allowExpand, resizeWhenOverflowing: true);
	}

	/// <summary>
	/// Re-applies the scroll sizing after the card's contents changed. It does not touch the overflowing height, because
	/// <see cref="SetupScroll"/> already set it; it only catches up with the content's new minimum height and with whether
	/// the expand button should be offered yet.
	/// </summary>
	public static void RefreshScroll(Control? content, float maxHeight, bool allowExpand)
	{
		if (content == null) return;

		ScrollContainer scroll = content.GetNodeOrNull<ScrollContainer>("CardScroll");
		if (scroll == null) return;

		SizeScroll(content, scroll, maxHeight, allowExpand, resizeWhenOverflowing: false);
	}

	private static void SizeScroll(Control content, ScrollContainer scroll, float maxHeight, bool allowExpand, bool resizeWhenOverflowing)
	{
		var targetInner = scroll.GetNodeOrNull<VBoxContainer>("InnerVBox");
		if (targetInner == null) return;

		targetInner.ForceUpdateTransform();
		float minH = targetInner.GetCombinedMinimumSize().Y;
		Button expandBtn = content.GetNodeOrNull<Button>("BtnExpandHeight");

		if (!allowExpand)
		{
			DisableExpansion(scroll, expandBtn);
			return;
		}

		if (minH > maxHeight)
		{
			if (resizeWhenOverflowing) scroll.CustomMinimumSize = new Vector2(ContentWidth, maxHeight);
			if (expandBtn != null) expandBtn.Visible = true;
		}
		else
		{
			scroll.CustomMinimumSize = new Vector2(ContentWidth, minH);
			if (expandBtn != null) expandBtn.Visible = false;
		}
	}

	private static ScrollContainer CreateScroll(bool allowExpand)
	{
		var scroll = new ScrollContainer();
		scroll.Name = "CardScroll";
		scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
		scroll.VerticalScrollMode = allowExpand ? ScrollContainer.ScrollMode.Auto : ScrollContainer.ScrollMode.Disabled;
		scroll.CustomMinimumSize = new Vector2(ContentWidth, 0);
		return scroll;
	}

	/// <summary>Moves the card's existing children into the scroll body, leaving the background panel behind.</summary>
	private static void ReparentIntoInnerVBox(Control content, ScrollContainer scroll)
	{
		var innerVBox = new VBoxContainer();
		innerVBox.Name = "InnerVBox";
		innerVBox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		innerVBox.AddThemeConstantOverride("separation", 2);

		var children = new Array<Node>(content.GetChildren());
		foreach (Node child in children)
		{
			if (child is Panel && child.Name == "ContentBG") continue;
			content.RemoveChild(child);
			innerVBox.AddChild(child);
		}

		scroll.AddChild(innerVBox);
	}

	private static void DisableExpansion(ScrollContainer scroll, Button expandBtn)
	{
		if (expandBtn != null) expandBtn.Visible = false;
		scroll.VerticalScrollMode = ScrollContainer.ScrollMode.Disabled;
		scroll.CustomMinimumSize = new Vector2(ContentWidth, 0);
	}
}
