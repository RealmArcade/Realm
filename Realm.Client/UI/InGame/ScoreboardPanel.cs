using System;
using System.Collections.Generic;
using Godot;
using Realm.Client.Core;
using Realm.Client.Services;
using Realm.Ecs.Common;

namespace Realm.Client.UI.InGame;

/// <summary>
///     Owns the one scoreboard panel a match shows. Its dropdown holds every visible leaderboard board and, when the
///     match publishes one, the summary table, so the combat statistics and the standings share a single collapsible
///     card instead of stacking two panels on the same corner of the screen.
/// </summary>
public class ScoreboardPanel
{
	private const string SummaryEntryId = "__summary";

	private readonly PanelContainer _panel;
	private readonly Label _titleLabel;
	private readonly Button _collapseButton;
	private readonly VBoxContainer _content;
	private readonly VBoxContainer _selectors;
	private readonly VBoxContainer _leaderboardContent;
	private readonly VBoxContainer _summaryContent;
	private readonly SummaryTablePanel _summaryTable;

	private readonly OptionButton _boardSelectButton;
	private readonly HBoxContainer _groupSelectRow;
	private readonly OptionButton _groupSelectButton;

	private readonly List<HBoxContainer> _rows = new();
	private readonly List<Label> _nameLabels = new();
	private readonly List<Label> _valueLabels = new();
	private readonly List<string> _entryIds = new();
	private readonly List<int> _groupPlayerIndices = new();

	private readonly List<(string Id, string Title)> _entries = new();

	private bool _collapsed = true;
	private string? _selectedEntryId;
	private int _selectedGroupPlayerIndex;
	private bool _hasGroupSelection;
	private int _lastRevision = -1;
	private int _lastBoardSignature = int.MinValue;
	private int _lastGroupSignature = int.MinValue;

	public ScoreboardPanel(
		PanelContainer panel,
		Label titleLabel,
		Button collapseButton,
		VBoxContainer content,
		VBoxContainer selectors,
		VBoxContainer leaderboardContent,
		VBoxContainer summaryContent)
	{
		_panel = panel;
		_titleLabel = titleLabel;
		_collapseButton = collapseButton;
		_content = content;
		_selectors = selectors;
		_leaderboardContent = leaderboardContent;
		_summaryContent = summaryContent;
		_summaryTable = new SummaryTablePanel(summaryContent);

		_boardSelectButton = CreateSelectorButton("BoardSelectButton");
		_boardSelectButton.ItemSelected += OnBoardSelected;
		_selectors.AddChild(_boardSelectButton);

		_groupSelectRow = new HBoxContainer();
		_groupSelectRow.Name = "GroupSelectRow";
		_groupSelectRow.AddThemeConstantOverride("separation", 6);
		_groupSelectRow.Visible = false;

		var groupLabel = new Label();
		groupLabel.Name = "GroupSelectLabel";
		groupLabel.Text = TranslationServer.Translate("Player");
		groupLabel.VerticalAlignment = VerticalAlignment.Center;
		groupLabel.AddThemeFontSizeOverride("font_size", 11);
		groupLabel.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		_groupSelectRow.AddChild(groupLabel);

		_groupSelectButton = CreateSelectorButton("GroupSelectButton");
		_groupSelectButton.ItemSelected += OnGroupSelected;
		_groupSelectRow.AddChild(_groupSelectButton);
		_selectors.AddChild(_groupSelectRow);

		if (_collapseButton != null)
		{
			_collapseButton.Pressed += ToggleCollapsed;
		}

		ApplyCollapsedState();
	}

	private static OptionButton CreateSelectorButton(string nodeName)
	{
		var button = new OptionButton();
		button.Name = nodeName;
		button.CustomMinimumSize = new Vector2(170, 26);
		button.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		button.FocusMode = Control.FocusModeEnum.All;
		UIStyle.ApplyButtonStyleboxes(button);
		button.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		button.AddThemeColorOverride("font_hover_color", UIStyle.ColorGold);
		button.AddThemeColorOverride("font_focus_color", UIStyle.ColorCyanGlow);
		button.AddThemeFontSizeOverride("font_size", 12);

		var focusStyle = new StyleBoxFlat();
		focusStyle.BgColor = new Color(0.16f, 0.16f, 0.18f, 0.9f);
		focusStyle.SetBorderWidthAll(1);
		focusStyle.BorderColor = UIStyle.ColorCyanGlow;
		button.AddThemeStyleboxOverride("focus", focusStyle);
		return button;
	}

	private void ToggleCollapsed()
	{
		GameContext.UserInterface?.PlayClickSound();
		_collapsed = !_collapsed;
		ApplyCollapsedState();
	}

	private void ApplyCollapsedState()
	{
		if (_content != null && GodotObject.IsInstanceValid(_content))
		{
			_content.Visible = !_collapsed;
		}

		if (_collapseButton == null || !GodotObject.IsInstanceValid(_collapseButton)) return;

		_collapseButton.Text = _collapsed ? UnicodeIcons.CARET_RIGHT : UnicodeIcons.CARET_DOWN;
		_collapseButton.TooltipText = _collapsed
			? TranslationServer.Translate("Expand scoreboard")
			: TranslationServer.Translate("Collapse scoreboard");
	}

	public void Update(InGameHUDViewModel viewModel)
	{
		bool allowLeaderboard = GameContext.InGameHud?.IsHudElementVisible(HudElement.Leaderboard, true) ?? true;
		bool allowSummary = GameContext.InGameHud?.IsHudElementVisible(HudElement.SummaryTable, true) ?? true;

		LeaderboardBoards boards = viewModel.Leaderboards;
		RebuildEntries(boards, allowLeaderboard, allowSummary, viewModel);
		ResolveSelection();

		bool visible = _entries.Count > 0;
		if (_panel.Visible != visible)
		{
			_panel.Visible = visible;
		}

		if (!visible)
		{
			_lastRevision = boards.Revision;
			return;
		}

		bool revisionChanged = boards.Revision != _lastRevision;
		_lastRevision = boards.Revision;

		int boardSignature = ComputeBoardSignature(_entries, boards);
		if (revisionChanged || boardSignature != _lastBoardSignature)
		{
			_lastBoardSignature = boardSignature;
			RebuildBoardOptions();
		}

		bool showingSummary = string.Equals(_selectedEntryId, SummaryEntryId, StringComparison.Ordinal);
		if (showingSummary)
		{
			ApplyBodyVisibility(leaderboard: false, summary: true);
			_groupSelectRow.Visible = false;
			EnsureTitle(viewModel.SummaryTableTitle);
			_summaryTable.Update(viewModel);
			return;
		}

		LeaderboardBoard? selectedBoard = FindBoard(boards, _selectedEntryId);
		if (selectedBoard == null)
		{
			return;
		}

		ApplyBodyVisibility(leaderboard: true, summary: false);
		EnsureTitle(selectedBoard.Title);

		int groupSignature = ComputeGroupSignature(selectedBoard);
		if (revisionChanged || groupSignature != _lastGroupSignature)
		{
			_lastGroupSignature = groupSignature;
			RebuildGroupOptions(selectedBoard);
		}

		RenderRows(selectedBoard);
	}

	private void ApplyBodyVisibility(bool leaderboard, bool summary)
	{
		if (_leaderboardContent != null && GodotObject.IsInstanceValid(_leaderboardContent) && _leaderboardContent.Visible != leaderboard)
		{
			_leaderboardContent.Visible = leaderboard;
		}

		if (_summaryContent != null && GodotObject.IsInstanceValid(_summaryContent) && _summaryContent.Visible != summary)
		{
			_summaryContent.Visible = summary;
		}
	}

	private void EnsureTitle(string? title)
	{
		string shown = string.IsNullOrWhiteSpace(title) ? TranslationServer.Translate("SCOREBOARD") : title;
		if (_titleLabel.Text != shown)
		{
			_titleLabel.Text = shown;
		}
	}

	private void RebuildEntries(LeaderboardBoards boards, bool allowLeaderboard, bool allowSummary, InGameHUDViewModel viewModel)
	{
		_entries.Clear();

		if (allowLeaderboard)
		{
			IReadOnlyList<LeaderboardBoard> allBoards = boards.Boards;
			for (int index = 0; index < allBoards.Count; index++)
			{
				LeaderboardBoard board = allBoards[index];
				if (!board.Visible) continue;

				_entries.Add((board.Id, board.Title ?? string.Empty));
			}
		}

		if (allowSummary && viewModel.SummaryTableVisible)
		{
			string summaryTitle = string.IsNullOrWhiteSpace(viewModel.SummaryTableTitle)
				? TranslationServer.Translate("COMBAT STATISTICS")
				: viewModel.SummaryTableTitle;
			_entries.Add((SummaryEntryId, summaryTitle));
		}
	}

	private void ResolveSelection()
	{
		for (int index = 0; index < _entries.Count; index++)
		{
			if (string.Equals(_entries[index].Id, _selectedEntryId, StringComparison.Ordinal))
			{
				return;
			}
		}

		_selectedEntryId = _entries.Count > 0 ? _entries[0].Id : null;
		_hasGroupSelection = false;
		_lastGroupSignature = int.MinValue;
	}

	private void RebuildBoardOptions()
	{
		_entryIds.Clear();
		_boardSelectButton.Clear();

		int selectedIndex = 0;
		for (int index = 0; index < _entries.Count; index++)
		{
			(string id, string title) = _entries[index];
			if (string.Equals(id, _selectedEntryId, StringComparison.Ordinal))
				selectedIndex = _entryIds.Count;

			_entryIds.Add(id);
			_boardSelectButton.AddItem(title);
		}

		_boardSelectButton.Visible = _entryIds.Count > 1;
		if (_entryIds.Count > 0)
			_boardSelectButton.Select(selectedIndex);
	}

	private static int ComputeBoardSignature(List<(string Id, string Title)> entries, LeaderboardBoards boards)
	{
		int signature = 17;
		for (int index = 0; index < entries.Count; index++)
		{
			signature = (signature * 31) + entries[index].Id.GetHashCode();
			signature = (signature * 31) + entries[index].Title.GetHashCode();
		}

		return signature;
	}

	private static LeaderboardBoard? FindBoard(LeaderboardBoards boards, string? boardId)
	{
		if (string.IsNullOrEmpty(boardId)) return null;

		LeaderboardBoard? board = boards.FindBoard(boardId);
		return board != null && board.Visible ? board : null;
	}

	private static int ComputeGroupSignature(LeaderboardBoard board)
	{
		int signature = 17;
		if (board.Groups.Count > 0)
		{
			for (int groupIndex = 0; groupIndex < board.Groups.Count; groupIndex++)
			{
				LeaderboardBoardGroup group = board.Groups[groupIndex];
				signature = (signature * 31) + group.PlayerIndex;
				signature = (signature * 31) + (group.Title?.GetHashCode() ?? 0);
			}

			return signature;
		}

		for (int rowIndex = 0; rowIndex < board.Rows.Count; rowIndex++)
		{
			LeaderboardBoardRow row = board.Rows[rowIndex];
			if (row.GroupPlayerIndex < 0)
				continue;

			signature = (signature * 31) + row.GroupPlayerIndex;
			signature = (signature * 31) + (row.GroupTitle?.GetHashCode() ?? 0);
		}

		return signature;
	}

	private void RebuildGroupOptions(LeaderboardBoard board)
	{
		_groupPlayerIndices.Clear();
		_groupSelectButton.Clear();

		for (int groupIndex = 0; groupIndex < board.Groups.Count; groupIndex++)
		{
			LeaderboardBoardGroup group = board.Groups[groupIndex];
			if (group.PlayerIndex < 0 || _groupPlayerIndices.Contains(group.PlayerIndex))
				continue;

			_groupPlayerIndices.Add(group.PlayerIndex);
			_groupSelectButton.AddItem(group.Title ?? string.Empty);
		}

		for (int rowIndex = 0; rowIndex < board.Rows.Count; rowIndex++)
		{
			LeaderboardBoardRow row = board.Rows[rowIndex];
			if (row.GroupPlayerIndex < 0)
				continue;

			if (_groupPlayerIndices.Contains(row.GroupPlayerIndex))
				continue;

			_groupPlayerIndices.Add(row.GroupPlayerIndex);
			_groupSelectButton.AddItem(row.GroupTitle ?? string.Empty);
		}

		if (_groupPlayerIndices.Count == 0)
		{
			_hasGroupSelection = false;
			_groupSelectRow.Visible = false;
			return;
		}

		if (!_hasGroupSelection)
		{
			_selectedGroupPlayerIndex = ResolveDefaultGroupPlayerIndex();
			_hasGroupSelection = true;
		}

		int selectedIndex = _groupPlayerIndices.IndexOf(_selectedGroupPlayerIndex);
		if (selectedIndex < 0)
		{
			_selectedGroupPlayerIndex = _groupPlayerIndices[0];
			selectedIndex = 0;
		}

		_groupSelectRow.Visible = true;
		_groupSelectButton.Select(selectedIndex);
	}

	private int ResolveDefaultGroupPlayerIndex()
	{
		int localPlayerIndex = GameContext.Host?.LocalPlayerIndex ?? -1;
		if (localPlayerIndex >= 0 && _groupPlayerIndices.Contains(localPlayerIndex))
			return localPlayerIndex;

		return _groupPlayerIndices[0];
	}

	private void OnBoardSelected(long index)
	{
		if (index < 0 || index >= _entryIds.Count)
			return;

		string boardId = _entryIds[(int)index];
		if (string.Equals(boardId, _selectedEntryId, StringComparison.Ordinal))
			return;

		GameContext.UserInterface?.PlayClickSound();
		_selectedEntryId = boardId;
		_hasGroupSelection = false;
		_lastRevision = -1;
		_lastGroupSignature = int.MinValue;
	}

	private void OnGroupSelected(long index)
	{
		if (index < 0 || index >= _groupPlayerIndices.Count)
			return;

		int playerIndex = _groupPlayerIndices[(int)index];
		if (playerIndex == _selectedGroupPlayerIndex)
			return;

		GameContext.UserInterface?.PlayClickSound();
		_selectedGroupPlayerIndex = playerIndex;
		_hasGroupSelection = true;
	}

	private void RenderRows(LeaderboardBoard board)
	{
		int rowIdx = 0;
		for (int rowIndex = 0; rowIndex < board.Rows.Count; rowIndex++)
		{
			LeaderboardBoardRow data = board.Rows[rowIndex];
			if (data.GroupPlayerIndex >= 0 && _hasGroupSelection && data.GroupPlayerIndex != _selectedGroupPlayerIndex)
				continue;

			EnsureRowWidgets(rowIdx);

			var curRow = _rows[rowIdx];
			if (!curRow.Visible)
			{
				curRow.Visible = true;
			}

			var curName = _nameLabels[rowIdx];
			string keyText = data.Label ?? string.Empty;
			if (curName.Text != keyText)
			{
				curName.Text = keyText;
			}

			var curVal = _valueLabels[rowIdx];
			string valText = data.Value ?? string.Empty;
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

	private void EnsureRowWidgets(int rowIdx)
	{
		while (rowIdx >= _rows.Count)
		{
			var row = new HBoxContainer();
			row.Name = $"LeaderboardRow{_rows.Count}_HBoxContainer";
			row.AddThemeConstantOverride("separation", 20);

			var nameLbl = new Label();
			nameLbl.Name = "NameLbl_Label";
			nameLbl.AddThemeFontSizeOverride("font_size", 12);
			nameLbl.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
			row.AddChild(nameLbl);

			var valLbl = new Label();
			valLbl.Name = "ValLbl_Label";
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
	}
}
