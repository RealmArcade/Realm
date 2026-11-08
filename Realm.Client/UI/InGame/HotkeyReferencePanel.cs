using Godot;
using Realm.Client.Utils;

namespace Realm.Client.UI.InGame;

/// <summary>
/// The in-match hotkey reference: the artwork frame, the four shortcut groups and their rows, and the plate's own
/// placement, which has to sit over the playfield rather than the console. The panel builds itself into the HUD it is
/// given and owns whether it is showing, so the HUD only toggles it and tells it how much room the playfield has.
/// </summary>
public sealed class HotkeyReferencePanel
{
	private const float PanelHalfWidth = 240f;
	private const float PanelHalfHeight = 336f;
	private const float PanelHeight = 672f;

	private static readonly (string GroupTitle, (string Key, string Desc)[] Items)[] Groups =
	{
		(
			"COMMANDS",
			new (string Key, string Desc)[]
			{
				("Right-Click", "Smart Command (Move/Attack/Rally)"),
				("Shift+R-Click", "Queue Action / Waypoint"),
				("A", "Attack / Attack-Move"),
				("M", "Move to Target"),
				("P", "Patrol Route"),
				("S", "Stop Current Action"),
				("H", "Hold Position"),
				("B", "Open Build Menu"),
			}
		),
		(
			"SELECTION & GROUPS",
			new (string Key, string Desc)[]
			{
				("Left-Click", "Select Unit (Drag to Box-Select)"),
				("Shift+Click", "Add / Remove from Selection"),
				("Double-Click", "Select All Same Type on Screen"),
				("Tab", "Cycle Selection Focus"),
				("F1", "Select All Idle Units"),
				("F2", "Select All Military Units"),
				("F3", "Select All Buildings"),
				("`", "Cycle Through Buildings"),
				("Ctrl+0..9", "Assign Control Group"),
				("0..9", "Recall Group (Double: Jump)"),
			}
		),
		(
			"CAMERA & MINIMAP",
			new (string Key, string Desc)[]
			{
				("WASD / Edge", "Pan Camera"),
				("Shift+WASD", "Fast Camera Pan"),
				("Space", "Center Camera on Base"),
				("Z", "Cycle Zoom (Near / Mid / Far)"),
				("F4", "Toggle Terrain Radar Mode"),
				("Alt+G", "Ping Map Alert"),
				("Ctrl+F5..F8", "Save Camera Location 1..4"),
				("F5..F8", "Jump to Saved Camera 1..4"),
			}
		),
		(
			"BUILDINGS & SYSTEM",
			new (string Key, string Desc)[]
			{
				("Y", "Set Building Rally Point"),
				("Enter", "Open Chat (Shift: All Chat)"),
				("Esc", "Clear Selection / Cancel Targeting"),
				("F12", "Settings"),
				("~ (Tilde)", "Developer Console"),
			}
		),
	};

	public PanelContainer? Root { get; private set; }

	public bool Visible => Root?.Visible ?? false;

	/// <summary>
	/// Builds the plate and parents it to the HUD. Nothing is shown until <see cref="Toggle"/> is called, because the
	/// reference is opened from the minimap's key button.
	/// </summary>
	public void Build(Node owner)
	{
		Root = new PanelContainer();
		Root.Name = "HotkeyPanel";
		Root.Visible = false;
		Root.MouseFilter = Control.MouseFilterEnum.Stop;
		Root.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());

		Root.SetAnchorsPreset(Control.LayoutPreset.Center);
		Root.GrowHorizontal = Control.GrowDirection.Both;
		Root.GrowVertical = Control.GrowDirection.Both;
		Root.OffsetLeft = -PanelHalfWidth;
		Root.OffsetRight = PanelHalfWidth;
		Root.OffsetTop = -372;
		Root.OffsetBottom = 300;
		Root.PivotOffset = new Vector2(PanelHalfWidth, PanelHalfHeight);

		var bgTexture = new TextureRect();
		bgTexture.Name = "Background";
		bgTexture.Texture = UIStyle.LoadTextureWithFallback("res://Assets/UI/In-Game_MatchView_HotkeyReference.webp");
		bgTexture.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
		bgTexture.StretchMode = TextureRect.StretchModeEnum.Scale;
		bgTexture.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		bgTexture.MouseFilter = Control.MouseFilterEnum.Ignore;
		Root.AddChild(bgTexture);

		var contentRoot = new Control();
		contentRoot.Name = "ContentRoot";
		contentRoot.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		contentRoot.MouseFilter = Control.MouseFilterEnum.Pass;
		Root.AddChild(contentRoot);

		contentRoot.AddChild(CreateTitle());
		contentRoot.AddChild(CreateCloseButton());
		contentRoot.AddChild(CreateRowList());

		owner.AddChild(Root);
	}

	public void Toggle()
	{
		if (Root == null) return;

		Root.Visible = !Root.Visible;
		if (Root.Visible)
		{
			Root.MoveToFront();
		}
	}

	public void Close()
	{
		if (Root != null) Root.Visible = false;
	}

	/// <summary>
	/// Centres the plate on the playfield, which is the area above the bottom console rather than the whole window. The
	/// offsets are relative to the centre anchor, so a taller console pushes the plate up.
	/// </summary>
	public void PlaceOverPlayfield(float playfieldCenterY, float viewportHeight)
	{
		if (Root == null) return;

		Root.OffsetLeft = -PanelHalfWidth;
		Root.OffsetRight = PanelHalfWidth;
		Root.OffsetTop = playfieldCenterY - (viewportHeight / 2f) - PanelHalfHeight;
		Root.OffsetBottom = Root.OffsetTop + PanelHeight;
	}

	/// <summary>
	/// HUD scaling pivots on the plate's own centre so it grows away from the playfield centre instead of from its
	/// corner.
	/// </summary>
	public void ApplyScale(float scale)
	{
		if (Root == null) return;

		Root.PivotOffset = new Vector2(PanelHalfWidth, PanelHalfHeight);
		Root.Scale = new Vector2(scale, scale);
	}

	private Label CreateTitle()
	{
		var titleLbl = new Label();
		titleLbl.Name = "TitleLabel";
		titleLbl.Text = TranslationServer.Translate("HOTKEY REFERENCE");
		titleLbl.SetAnchorsPreset(Control.LayoutPreset.TopWide);
		titleLbl.OffsetLeft = 65;
		titleLbl.OffsetRight = -105;
		titleLbl.OffsetTop = 22;
		titleLbl.OffsetBottom = 58;
		titleLbl.HorizontalAlignment = HorizontalAlignment.Center;
		titleLbl.VerticalAlignment = VerticalAlignment.Center;
		titleLbl.AddThemeFontOverride("font", UIStyle.FontNorseBold);
		titleLbl.AddThemeFontSizeOverride("font_size", 16);
		titleLbl.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		titleLbl.AddThemeConstantOverride("outline_size", 3);
		titleLbl.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.95f));
		return titleLbl;
	}

	private Button CreateCloseButton()
	{
		var btnClose = new Button();
		btnClose.Name = "BtnClose";
		btnClose.Text = "✕";
		btnClose.SetAnchorsPreset(Control.LayoutPreset.TopRight);
		btnClose.OffsetLeft = -98;
		btnClose.OffsetRight = -72;
		btnClose.OffsetTop = 27;
		btnClose.OffsetBottom = 53;
		btnClose.FocusMode = Control.FocusModeEnum.None;
		btnClose.TooltipText = TranslationServer.Translate("Close [Esc]");
		btnClose.AddThemeFontSizeOverride("font_size", 14);
		btnClose.AddThemeStyleboxOverride("normal", new StyleBoxEmpty());

		var closeHoverStyle = new StyleBoxFlat();
		closeHoverStyle.BgColor = new Color(0.15f, 0.65f, 1.0f, 0.25f);
		closeHoverStyle.SetCornerRadiusAll(4);
		btnClose.AddThemeStyleboxOverride("hover", closeHoverStyle);

		var closePressedStyle = new StyleBoxFlat();
		closePressedStyle.BgColor = new Color(0.15f, 0.65f, 1.0f, 0.45f);
		closePressedStyle.SetCornerRadiusAll(4);
		btnClose.AddThemeStyleboxOverride("pressed", closePressedStyle);

		btnClose.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
		btnClose.AddThemeColorOverride("font_color", Colors.White);
		btnClose.AddThemeColorOverride("font_hover_color", UIStyle.ColorGold);
		btnClose.AddThemeColorOverride("font_pressed_color", UIStyle.ColorCyanGlow);
		btnClose.AddThemeConstantOverride("outline_size", 2);
		btnClose.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.9f));
		btnClose.Pressed += Toggle;
		return btnClose;
	}

	private ScrollContainer CreateRowList()
	{
		var scroll = new ScrollContainer();
		scroll.Name = "HotkeyScroll";
		scroll.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		scroll.OffsetLeft = 90;
		scroll.OffsetRight = -90;
		scroll.OffsetTop = 88;
		scroll.OffsetBottom = -82;
		scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
		scroll.VerticalScrollMode = ScrollContainer.ScrollMode.Auto;

		StyleScrollBar(scroll.GetVScrollBar());

		var vbox = new VBoxContainer();
		vbox.Name = "Vbox_VBoxContainer";
		vbox.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		vbox.AddThemeConstantOverride("separation", 3);
		scroll.AddChild(vbox);

		for (int groupIndex = 0; groupIndex < Groups.Length; groupIndex++)
		{
			var (groupTitle, items) = Groups[groupIndex];

			if (groupIndex > 0)
			{
				var groupSpacer = new Control();
				groupSpacer.Name = "GroupSpacer_Control";
				groupSpacer.CustomMinimumSize = new Vector2(0, 6);
				vbox.AddChild(groupSpacer);
			}

			vbox.AddChild(CreateGroupHeader(groupTitle));

			for (int i = 0; i < items.Length; i++)
			{
				vbox.AddChild(CreateRow(items[i].Key, items[i].Desc, i));
			}
		}

		return scroll;
	}

	private static void StyleScrollBar(VScrollBar? vScroll)
	{
		if (vScroll == null) return;

		vScroll.CustomMinimumSize = new Vector2(4, 0);

		var grabberNormal = new StyleBoxFlat();
		grabberNormal.BgColor = new Color(0.6f, 0.5f, 0.35f, 0.7f);
		grabberNormal.SetCornerRadiusAll(2);
		vScroll.AddThemeStyleboxOverride("grabber", grabberNormal);

		var grabberHighlight = new StyleBoxFlat();
		grabberHighlight.BgColor = UIStyle.ColorGold;
		grabberHighlight.SetCornerRadiusAll(2);
		vScroll.AddThemeStyleboxOverride("grabber_highlight", grabberHighlight);

		var grabberPressed = new StyleBoxFlat();
		grabberPressed.BgColor = UIStyle.ColorCyanGlow;
		grabberPressed.SetCornerRadiusAll(2);
		vScroll.AddThemeStyleboxOverride("grabber_pressed", grabberPressed);

		var scrollTrack = new StyleBoxFlat();
		scrollTrack.BgColor = new Color(0.04f, 0.04f, 0.06f, 0.3f);
		scrollTrack.SetCornerRadiusAll(2);
		vScroll.AddThemeStyleboxOverride("scroll", scrollTrack);
	}

	private static PanelContainer CreateGroupHeader(string groupTitle)
	{
		var catHeader = new PanelContainer();
		catHeader.Name = "CatHeader_PanelContainer";

		var catHeaderStyle = new StyleBoxFlat();
		catHeaderStyle.BgColor = new Color(0.18f, 0.16f, 0.14f, 0.85f);
		catHeaderStyle.BorderColor = new Color(0.6f, 0.5f, 0.35f, 0.5f);
		catHeaderStyle.SetBorderWidthAll(1);
		catHeaderStyle.SetCornerRadiusAll(3);
		catHeaderStyle.ContentMarginLeft = 6;
		catHeaderStyle.ContentMarginRight = 6;
		catHeaderStyle.ContentMarginTop = 3;
		catHeaderStyle.ContentMarginBottom = 3;
		catHeader.AddThemeStyleboxOverride("panel", catHeaderStyle);

		var catTitleLbl = new Label();
		catTitleLbl.Name = "CatTitleLbl_Label";
		catTitleLbl.Text = TranslationServer.Translate(groupTitle);
		catTitleLbl.AddThemeFontOverride("font", UIStyle.FontNorseBold);
		catTitleLbl.AddThemeFontSizeOverride("font_size", 13);
		catTitleLbl.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		catTitleLbl.AddThemeConstantOverride("outline_size", 2);
		catTitleLbl.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.95f));
		catTitleLbl.HorizontalAlignment = HorizontalAlignment.Center;
		catTitleLbl.VerticalAlignment = VerticalAlignment.Center;
		catHeader.AddChild(catTitleLbl);

		return catHeader;
	}

	/// <summary>
	/// One shortcut: the key badge keeps a fixed width so the descriptions line up down the column, and the rows
	/// alternate their background so a long group stays readable.
	/// </summary>
	private static PanelContainer CreateRow(string key, string desc, int rowIndex)
	{
		var rowPanel = new PanelContainer();
		rowPanel.Name = "RowPanel_PanelContainer";

		var rowStyle = new StyleBoxFlat();
		rowStyle.BgColor = (rowIndex % 2 == 0)
			? new Color(0.12f, 0.13f, 0.15f, 0.35f)
			: new Color(0.06f, 0.07f, 0.08f, 0.15f);
		rowStyle.SetCornerRadiusAll(2);
		rowStyle.ContentMarginLeft = 4;
		rowStyle.ContentMarginRight = 4;
		rowStyle.ContentMarginTop = 2;
		rowStyle.ContentMarginBottom = 2;
		rowPanel.AddThemeStyleboxOverride("panel", rowStyle);

		var row = UIStyle.CreateRowContainer();
		row.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;

		var keyBadge = new PanelContainer();
		keyBadge.Name = "KeyBadge_PanelContainer";
		var badgeStyle = new StyleBoxFlat();
		badgeStyle.BgColor = new Color(0.08f, 0.10f, 0.14f, 0.85f);
		badgeStyle.BorderColor = new Color(0.35f, 0.45f, 0.55f, 0.5f);
		badgeStyle.SetBorderWidthAll(1);
		badgeStyle.SetCornerRadiusAll(3);
		badgeStyle.ContentMarginLeft = 4;
		badgeStyle.ContentMarginRight = 4;
		badgeStyle.ContentMarginTop = 1;
		badgeStyle.ContentMarginBottom = 1;
		keyBadge.AddThemeStyleboxOverride("panel", badgeStyle);
		keyBadge.CustomMinimumSize = new Vector2(86, 0);

		var keyLbl = new Label();
		keyLbl.Name = "KeyLbl_Label";
		keyLbl.Text = TranslationServer.Translate(key);
		keyLbl.AddThemeFontSizeOverride("font_size", 10);
		keyLbl.AddThemeColorOverride("font_color", UIStyle.ColorCyanGlow);
		keyLbl.AddThemeConstantOverride("outline_size", 1);
		keyLbl.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.9f));
		keyLbl.HorizontalAlignment = HorizontalAlignment.Center;
		keyLbl.VerticalAlignment = VerticalAlignment.Center;
		keyBadge.AddChild(keyLbl);
		row.AddChild(keyBadge);

		var descLbl = new Label();
		descLbl.Name = "DescLbl_Label";
		descLbl.Text = TranslationServer.Translate(desc);
		descLbl.AddThemeFontSizeOverride("font_size", 11);
		descLbl.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		descLbl.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		descLbl.CustomMinimumSize = new Vector2(170, 0);
		descLbl.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		descLbl.VerticalAlignment = VerticalAlignment.Center;
		row.AddChild(descLbl);

		rowPanel.AddChild(row);
		return rowPanel;
	}
}
