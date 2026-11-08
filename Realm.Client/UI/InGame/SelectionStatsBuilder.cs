using Godot;
using Realm.Client.Utils;

namespace Realm.Client.UI.InGame;

/// <summary>
/// The nodes the selection frame hosts: the scrolling stats column and the production box beside it. Both stay hidden
/// until a single selection has something to show, which is why they are built with Visible = false.
/// </summary>
public sealed record SelectionStatsNodes(
	HBoxContainer Container,
	UnitStatsPanel StatsPanel,
	VBoxContainer ProductionBox,
	Label ProductionTitle,
	ProgressBar ProductionProgress,
	Label ProductionQueueLabel,
	HBoxContainer QueueSlots);

/// <summary>
/// Builds the selection frame's stats and production column. The portrait panel drives every node this returns, so the
/// shape is the contract between the two: the HUD builds it once and hands the parts to that panel.
/// </summary>
public static class SelectionStatsBuilder
{
	public static SelectionStatsNodes Create(Control selectionFrame)
	{
		var container = new HBoxContainer();
		container.Name = "StatsContainer_HBoxContainer";
		container.LayoutMode = 2;
		container.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		container.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
		container.CustomMinimumSize = new Vector2(0, 0);
		container.ClipContents = true;
		container.AddThemeConstantOverride("separation", 20);
		selectionFrame.AddChild(container);
		container.Visible = false;

		var statsScroll = new ScrollContainer();
		statsScroll.Name = "StatsScroll";
		statsScroll.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		statsScroll.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
		statsScroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
		statsScroll.VerticalScrollMode = ScrollContainer.ScrollMode.Auto;
		container.AddChild(statsScroll);

		var statsVBox = new VBoxContainer();
		statsVBox.Name = "StatsVBox_VBoxContainer";
		statsVBox.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		statsVBox.SizeFlagsVertical = Control.SizeFlags.ShrinkBegin;
		statsScroll.AddChild(statsVBox);

		var statsPanel = new UnitStatsPanel();
		statsVBox.AddChild(statsPanel.Root);

		StyleScrollBar(statsScroll.GetVScrollBar());

		var productionBox = new VBoxContainer();
		productionBox.Name = "ProductionBox_VBoxContainer";
		productionBox.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		productionBox.Alignment = BoxContainer.AlignmentMode.Center;
		container.AddChild(productionBox);
		productionBox.Visible = false;

		var productionTitle = new Label();
		productionTitle.Name = "ProductionTitle_Label";
		productionTitle.Text = TranslationServer.Translate("PRODUCTION");
		UIStyle.ApplyTitle(productionTitle, TranslationServer.Translate("PRODUCTION"), 12);
		productionTitle.AddThemeColorOverride("font_color", UIStyle.ColorBronze);
		productionBox.AddChild(productionTitle);

		var productionProgress = new ProgressBar();
		productionProgress.Name = "ProductionProgress_ProgressBar";
		productionProgress.CustomMinimumSize = new Vector2(120, 16);
		productionProgress.ShowPercentage = true;
		productionProgress.AddThemeStyleboxOverride("background", UIStyle.CreateSliderTrack());
		productionProgress.AddThemeStyleboxOverride("fill", UIStyle.CreateSliderFill());
		productionBox.AddChild(productionProgress);

		var productionQueueLabel = new Label();
		productionQueueLabel.Name = "ProductionQueueLabel_Label";
		productionQueueLabel.Text = string.Format(TranslationServer.Translate("Queue: {0}"), 0);
		productionQueueLabel.HorizontalAlignment = HorizontalAlignment.Center;
		productionQueueLabel.AddThemeFontSizeOverride("font_size", 11);
		productionQueueLabel.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		productionBox.AddChild(productionQueueLabel);

		var queueSlots = new HBoxContainer();
		queueSlots.Name = "QueueSlotsContainer_HBoxContainer";
		queueSlots.Alignment = BoxContainer.AlignmentMode.Center;
		queueSlots.AddThemeConstantOverride("separation", 6);
		productionBox.AddChild(queueSlots);

		return new SelectionStatsNodes(container, statsPanel, productionBox, productionTitle, productionProgress,
			productionQueueLabel, queueSlots);
	}

	private static void StyleScrollBar(VScrollBar? vScroll)
	{
		if (vScroll == null) return;

		vScroll.CustomMinimumSize = new Vector2(6, 0);

		var grabberNormal = new StyleBoxFlat();
		grabberNormal.BgColor = UIStyle.ColorBronze;
		grabberNormal.SetCornerRadiusAll(3);
		vScroll.AddThemeStyleboxOverride("grabber", grabberNormal);

		var grabberHighlight = new StyleBoxFlat();
		grabberHighlight.BgColor = UIStyle.ColorGold;
		grabberHighlight.SetCornerRadiusAll(3);
		vScroll.AddThemeStyleboxOverride("grabber_highlight", grabberHighlight);

		var grabberPressed = new StyleBoxFlat();
		grabberPressed.BgColor = UIStyle.ColorCyanGlow;
		grabberPressed.SetCornerRadiusAll(3);
		vScroll.AddThemeStyleboxOverride("grabber_pressed", grabberPressed);

		var scrollTrack = new StyleBoxFlat();
		scrollTrack.BgColor = new Color(0.04f, 0.04f, 0.06f, 0.5f);
		scrollTrack.SetCornerRadiusAll(3);
		vScroll.AddThemeStyleboxOverride("scroll", scrollTrack);
	}
}
