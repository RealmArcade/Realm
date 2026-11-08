using Godot;
using Realm.Client.Utils;

namespace Realm.Client.UI.InGame;

/// <summary>
/// The scoreboard's nodes. The leaderboard and summary sections arrive later as rows, so the panel is built once with
/// three empty containers and those two stay hidden until a board is registered.
/// </summary>
public sealed record ScoreboardNodes(
	PanelContainer Panel,
	Label TitleLabel,
	Button CollapseButton,
	VBoxContainer Content,
	VBoxContainer Selectors,
	VBoxContainer LeaderboardContent,
	VBoxContainer SummaryContent);

/// <summary>
/// Builds the in-match scoreboard plate: the title row with its collapse button, and the three content stacks that the
/// leaderboard, group selectors and summary table fill in.
/// </summary>
public static class ScoreboardBuilder
{
	public static ScoreboardNodes Create(Control parent)
	{
		var panel = new PanelContainer();
		panel.Name = "ScoreboardPanel";
		panel.Visible = false;
		panel.SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd;

		var panelStyle = UIStyle.CreateCardPlate(new Color(0.12f, 0.12f, 0.14f, 0.9f), UIStyle.ColorBronze, 1, 4, 10, 10, 8, 8);
		panel.AddThemeStyleboxOverride("panel", panelStyle);

		var vbox = new VBoxContainer();
		vbox.Name = "ScoreboardVBox_VBoxContainer";
		panel.AddChild(vbox);

		var header = new HBoxContainer();
		header.Name = "ScoreboardHeader_HBoxContainer";
		header.AddThemeConstantOverride("separation", 6);
		vbox.AddChild(header);

		// Keeps the title centred under the plate rather than under the collapse button.
		var headerInset = new Control();
		headerInset.Name = "ScoreboardHeaderInset";
		headerInset.CustomMinimumSize = new Vector2(26, 0);
		header.AddChild(headerInset);

		var titleLabel = new Label();
		titleLabel.Name = "ScoreboardTitle";
		titleLabel.Text = TranslationServer.Translate("SCOREBOARD");
		UIStyle.ApplyTitle(titleLabel, TranslationServer.Translate("SCOREBOARD"), 12);
		titleLabel.HorizontalAlignment = HorizontalAlignment.Center;
		titleLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		header.AddChild(titleLabel);

		var collapseButton = new Button();
		collapseButton.Name = "ScoreboardCollapse_Button";
		collapseButton.Flat = false;
		collapseButton.FocusMode = Control.FocusModeEnum.None;
		collapseButton.CustomMinimumSize = new Vector2(26, 22);
		collapseButton.AddThemeConstantOverride("icon_max_width", 0);
		Font collapseIconFont = UIStyle.FontAwesome;
		if (collapseIconFont != null)
		{
			collapseButton.AddThemeFontOverride("font", collapseIconFont);
		}

		collapseButton.AddThemeFontSizeOverride("font_size", 12);
		UIStyle.ApplyButtonStyleBoxesClearingFocus(collapseButton);
		collapseButton.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		collapseButton.AddThemeColorOverride("font_hover_color", UIStyle.ColorGold);
		header.AddChild(collapseButton);

		var content = new VBoxContainer();
		content.Name = "ScoreboardContent";
		content.AddThemeConstantOverride("separation", 6);
		vbox.AddChild(content);

		var selectors = new VBoxContainer();
		selectors.Name = "ScoreboardSelectors";
		selectors.AddThemeConstantOverride("separation", 4);
		content.AddChild(selectors);

		var leaderboardContent = new VBoxContainer();
		leaderboardContent.Name = "ScoreboardLeaderboardContent";
		content.AddChild(leaderboardContent);

		var summaryContent = new VBoxContainer();
		summaryContent.Name = "ScoreboardSummaryContent";
		summaryContent.Visible = false;
		content.AddChild(summaryContent);

		parent.AddChild(panel);

		return new ScoreboardNodes(panel, titleLabel, collapseButton, content, selectors, leaderboardContent, summaryContent);
	}
}
