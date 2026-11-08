using Godot;
using static Godot.Control;

namespace Realm.Client.UI.MapEditor;

/// <summary>
/// The greenlight status modal: whether a map may be published, its review counts, and the explanation of what a
/// verified good review is. It is pure presentation over the numbers it is given, so it takes the node to parent
/// itself to and nothing else.
/// </summary>
internal static class GreenlightStatusDialog
{
	public static void Show(Node owner, string mapTitle, string mapVersion, int verifiedGoodReviews, int totalReviews, double averageRating, bool isGreenlit)
	{
		var overlay = CreateShell(owner);

		var vbox = BuildPanel(overlay, isGreenlit);

		BuildTitle(vbox, isGreenlit);

		BuildDescription(vbox, mapTitle, mapVersion, isGreenlit);

		BuildMetrics(vbox, verifiedGoodReviews, totalReviews, averageRating);

		BuildTip(vbox);

		BuildCloseRow(vbox, overlay);
	}

	private static ColorRect CreateShell(Node owner)
	{
		var overlay = new ColorRect();
		overlay.Name = "GreenlightStatusOverlay";
		overlay.Color = new Color(0, 0, 0, 0.75f);
		overlay.SetAnchorsPreset(LayoutPreset.FullRect);
		overlay.MouseFilter = MouseFilterEnum.Stop;
		overlay.ZIndex = 1000;
		owner.AddChild(overlay);
		return overlay;
	}

	private static VBoxContainer BuildPanel(ColorRect overlay, bool isGreenlit)
	{
		var center = UIStyle.CreateCenteredOverlay();
		overlay.AddChild(center);

		var panel = new PanelContainer();
		panel.Name = "Panel_PanelContainer";
		panel.CustomMinimumSize = new Vector2(640, 440);
		var style = new StyleBoxFlat();
		style.BgColor = new Color(0.12f, 0.14f, 0.20f, 0.98f);
		style.BorderWidthTop = 2;
		style.BorderWidthBottom = 2;
		style.BorderWidthLeft = 2;
		style.BorderWidthRight = 2;
		style.BorderColor = isGreenlit ? new Color(0.3f, 0.8f, 0.4f, 1f) : new Color(0.8f, 0.4f, 0.3f, 1f);
		style.SetCornerRadiusAll(8);
		panel.AddThemeStyleboxOverride("panel", style);
		center.AddChild(panel);

		var margin = new MarginContainer();
		margin.Name = "Margin_MarginContainer";
		margin.AddThemeConstantOverride("margin_top", 22);
		margin.AddThemeConstantOverride("margin_bottom", 22);
		margin.AddThemeConstantOverride("margin_left", 26);
		margin.AddThemeConstantOverride("margin_right", 26);
		panel.AddChild(margin);

		var vbox = new VBoxContainer();
		vbox.Name = "Vbox_VBoxContainer";
		vbox.AddThemeConstantOverride("separation", 14);
		margin.AddChild(vbox);
		return vbox;
	}

	private static void BuildTitle(VBoxContainer vbox, bool isGreenlit)
	{
		var lblTitle = new Label();
		lblTitle.Name = "LblTitle_Label";
		lblTitle.Text = isGreenlit ? TranslationServer.Translate("MAP GREENLIT FOR PUBLISHING") : TranslationServer.Translate("MAP NOT YET GREENLIT");
		lblTitle.HorizontalAlignment = HorizontalAlignment.Center;
		lblTitle.AddThemeFontSizeOverride("font_size", 20);
		lblTitle.AddThemeColorOverride("font_color", isGreenlit ? new Color(0.4f, 0.9f, 0.5f) : new Color(1f, 0.6f, 0.4f));
		vbox.AddChild(lblTitle);
	}

	private static void BuildDescription(VBoxContainer vbox, string mapTitle, string mapVersion, bool isGreenlit)
	{
		var lblDesc = new Label();
		lblDesc.Name = "LblDesc_Label";
		lblDesc.Text = isGreenlit
			? string.Format(TranslationServer.Translate("'{0}' v{1} has met the community greenlight requirement and can now be published to the public registry."), mapTitle, mapVersion)
			: string.Format(TranslationServer.Translate("'{0}' v{1} is currently in Beta-Testing. Maps must achieve 100 Verified Good Reviews before graduating to the public registry."), mapTitle, mapVersion);
		lblDesc.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		lblDesc.CustomMinimumSize = new Vector2(588, 0);
		lblDesc.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		lblDesc.HorizontalAlignment = HorizontalAlignment.Center;
		lblDesc.AddThemeFontSizeOverride("font_size", 13);
		lblDesc.AddThemeColorOverride("font_color", new Color(0.85f, 0.85f, 0.9f));
		vbox.AddChild(lblDesc);
	}

	private static void BuildMetrics(VBoxContainer vbox, int verifiedGoodReviews, int totalReviews, double averageRating)
	{
		var metricsBox = new VBoxContainer();
		metricsBox.Name = "MetricsBox_VBoxContainer";
		metricsBox.AddThemeConstantOverride("separation", 8);
		vbox.AddChild(metricsBox);

		bool reviewsMet = verifiedGoodReviews >= 100;
		var lblReviews = new Label();
		lblReviews.Name = "LblReviews_Label";
		lblReviews.Text = string.Format(TranslationServer.Translate("• Total Verified Good Reviews: {0} / 100 {1}"), verifiedGoodReviews, reviewsMet ? "✓" : "");
		lblReviews.AddThemeColorOverride("font_color", reviewsMet ? new Color(0.4f, 0.9f, 0.5f) : new Color(0.9f, 0.8f, 0.6f));
		lblReviews.AddThemeFontSizeOverride("font_size", 15);
		metricsBox.AddChild(lblReviews);

		// The aggregate row only means something once public reviews exist; with none it would read as 0.0 stars.
		if (totalReviews > 0)
		{
			var lblAggregate = new Label();
			lblAggregate.Name = "LblAggregate_Label";
			lblAggregate.Text = string.Format(TranslationServer.Translate("• Public Aggregate Reviews: {0} (Average Rating: {1:F1} ★)"), totalReviews, averageRating);
			lblAggregate.AddThemeColorOverride("font_color", new Color(0.7f, 0.75f, 0.85f));
			lblAggregate.AddThemeFontSizeOverride("font_size", 12);
			metricsBox.AddChild(lblAggregate);
		}
	}

	private static void BuildTip(VBoxContainer vbox)
	{
		var tip = new Label();
		tip.Name = "Tip_Label";
		tip.Text = TranslationServer.Translate("How Verified Good Reviews are determined:\nA review qualifies as Verified & Good when submitted by a player who:\n  1. Is signed in with Steam\n  2. Has played your map for at least 30 minutes across 3 or more completed games\n  3. Gives your map a rating of 3 stars or higher\n\nHost multiplayer lobbies or share your map peer-to-peer to build verified reviews from your community.");
		tip.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		tip.CustomMinimumSize = new Vector2(588, 0);
		tip.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		tip.AddThemeFontSizeOverride("font_size", 11);
		tip.AddThemeColorOverride("font_color", new Color(0.65f, 0.7f, 0.8f));
		vbox.AddChild(tip);
	}

	private static void BuildCloseRow(VBoxContainer vbox, ColorRect overlay)
	{
		var hbox = new HBoxContainer();
		hbox.Name = "Hbox_HBoxContainer";
		hbox.Alignment = BoxContainer.AlignmentMode.Center;
		hbox.AddThemeConstantOverride("separation", 20);
		vbox.AddChild(hbox);

		var btnClose = new Button();
		btnClose.Name = "BtnClose_Button";
		btnClose.Text = TranslationServer.Translate("OK");
		btnClose.CustomMinimumSize = new Vector2(100, 36);
		btnClose.Pressed += () => overlay.QueueFree();
		hbox.AddChild(btnClose);
	}
}
