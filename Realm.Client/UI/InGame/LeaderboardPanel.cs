using System;
using System.Collections.Generic;
using Godot;
using Realm.MapAPI;

namespace Realm.Client.UI.InGame;

public class LeaderboardPanel
{
	private VBoxContainer _customUIPanel;
	private PanelContainer _countdownPanel;
	private Label _countdownLabel;
	private PanelContainer _leaderboardPanel;
	private Label _leaderboardTitleLabel;
	private VBoxContainer _leaderboardContent;

	private readonly List<HBoxContainer> _rows = new();
	private readonly List<Label> _nameLabels = new();
	private readonly List<Label> _valueLabels = new();
	private string? _lastCountdownText;
	private int _lastCountdownSeconds = int.MinValue;
	private string? _lastLeaderboardTitle;

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
		bool allowCountdown = InGameHUD.Instance?.IsHudElementVisible(HudElement.CountdownTimer, true) ?? true;
		bool countdownVisible = viewModel.CountdownActive && allowCountdown;
		if (_countdownPanel.Visible != countdownVisible)
		{
			_countdownPanel.Visible = countdownVisible;
		}
		if (countdownVisible)
		{
			int secs = (int)Math.Ceiling(viewModel.CountdownDuration);
			string text = viewModel.CountdownText ?? string.Empty;
			if (secs != _lastCountdownSeconds || text != _lastCountdownText)
			{
				_countdownLabel.Text = $"{text}: {secs}s";
				_lastCountdownSeconds = secs;
				_lastCountdownText = text;
			}
		}

		bool allowLeaderboard = InGameHUD.Instance?.IsHudElementVisible(HudElement.Leaderboard, true) ?? true;
		bool leaderboardVisible = viewModel.LeaderboardVisible && allowLeaderboard;
		if (_leaderboardPanel.Visible != leaderboardVisible)
		{
			_leaderboardPanel.Visible = leaderboardVisible;
		}
		if (!leaderboardVisible)
		{
			return;
		}

		string title = viewModel.LeaderboardTitle ?? string.Empty;
		if (_lastLeaderboardTitle != title)
		{
			_leaderboardTitleLabel.Text = title;
			_lastLeaderboardTitle = title;
		}

		int rowIdx = 0;
		foreach (var kvp in viewModel.LeaderboardValues)
		{
			if (rowIdx >= _rows.Count)
			{
				var row = new HBoxContainer();
				row.AddThemeConstantOverride("separation", 20);

				var nameLbl = new Label();
				nameLbl.AddThemeFontSizeOverride("font_size", 12);
				nameLbl.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
				row.AddChild(nameLbl);

				var valLbl = new Label();
				valLbl.AddThemeFontSizeOverride("font_size", 12);
				valLbl.AddThemeColorOverride("font_color", Colors.White);
				valLbl.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
				valLbl.HorizontalAlignment = HorizontalAlignment.Right;
				row.AddChild(valLbl);

				_leaderboardContent.AddChild(row);
				_rows.Add(row);
				_nameLabels.Add(nameLbl);
				_valueLabels.Add(valLbl);
			}

			var curRow = _rows[rowIdx];
			if (!curRow.Visible)
			{
				curRow.Visible = true;
			}

			var curName = _nameLabels[rowIdx];
			string keyText = kvp.Key ?? string.Empty;
			if (curName.Text != keyText)
			{
				curName.Text = keyText;
			}

			var curVal = _valueLabels[rowIdx];
			string valText = kvp.Value ?? string.Empty;
			if (curVal.Text != valText)
			{
				curVal.Text = valText;
			}

			rowIdx++;
		}

		for (int i = rowIdx; i < _rows.Count; i++)
		{
			if (_rows[i].Visible)
			{
				_rows[i].Visible = false;
			}
		}
	}
}