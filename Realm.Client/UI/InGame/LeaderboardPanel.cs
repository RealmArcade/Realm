using Godot;
using System;

namespace Realm.Client.UI.InGame;

public class LeaderboardPanel
{
	private VBoxContainer _customUIPanel;
	private PanelContainer _countdownPanel;
	private Label _countdownLabel;
	private PanelContainer _leaderboardPanel;
	private Label _leaderboardTitleLabel;
	private VBoxContainer _leaderboardContent;

	public LeaderboardPanel(VBoxContainer customUIPanel, PanelContainer countdownPanel, Label countdownLabel, 
		PanelContainer leaderboardPanel, Label leaderboardTitleLabel, VBoxContainer leaderboardContent)
	{
		_customUIPanel = customUIPanel;
		_countdownPanel = countdownPanel;
		_countdownLabel = countdownLabel;
		_leaderboardPanel = leaderboardPanel;
		_leaderboardTitleLabel = leaderboardTitleLabel;
		_leaderboardContent = leaderboardContent;
	}

	public void Update(InGameHUDViewModel viewModel)
	{
		if (viewModel.CountdownActive)
		{
			_countdownPanel.Visible = true;
			_countdownLabel.Text = $"{viewModel.CountdownText}: {(int)Math.Ceiling(viewModel.CountdownDuration)}s";
		}
		else
		{
			_countdownPanel.Visible = false;
		}

		_leaderboardPanel.Visible = viewModel.LeaderboardVisible;
		if (viewModel.LeaderboardVisible)
		{
			_leaderboardTitleLabel.Text = viewModel.LeaderboardTitle;
			
			foreach (Node child in _leaderboardContent.GetChildren())
			{
				_leaderboardContent.RemoveChild(child);
				child.QueueFree();
			}

			foreach (var kvp in viewModel.LeaderboardValues)
			{
				var row = new HBoxContainer();
				row.AddThemeConstantOverride("separation", 20);

				var nameLbl = new Label();
				nameLbl.Text = kvp.Key;
				nameLbl.AddThemeFontSizeOverride("font_size", 12);
				nameLbl.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
				row.AddChild(nameLbl);

				var valLbl = new Label();
				valLbl.Text = kvp.Value;
				valLbl.AddThemeFontSizeOverride("font_size", 12);
				valLbl.AddThemeColorOverride("font_color", Colors.White);
				valLbl.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
				valLbl.HorizontalAlignment = HorizontalAlignment.Right;
				row.AddChild(valLbl);

				_leaderboardContent.AddChild(row);
			}
		}
	}
}