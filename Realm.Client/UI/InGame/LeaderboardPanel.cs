using System;
using System.Collections.Generic;
using Godot;
using Realm.Client.Core;
using Realm.Ecs.Common;
using Realm.Client.Services;

namespace Realm.Client.UI.InGame;

public class LeaderboardPanel
{
	private VBoxContainer _customUIPanel;
	private PanelContainer _leaderboardPanel;
	private Label _leaderboardTitleLabel;
	private VBoxContainer _leaderboardSelectors;
	private VBoxContainer _leaderboardContent;

	private OptionButton _boardSelectButton;
	private HBoxContainer _groupSelectRow;
	private OptionButton _groupSelectButton;

	private readonly List<HBoxContainer> _rows = new();
	private readonly List<Label> _nameLabels = new();
	private readonly List<Label> _valueLabels = new();
	private readonly List<string> _boardIds = new();
	private readonly List<int> _groupPlayerIndices = new();
	private string? _lastLeaderboardTitle;

	private string? _selectedBoardId;
	private int _selectedGroupPlayerIndex;
	private bool _hasGroupSelection;
	private int _lastRevision = -1;
	private int _lastBoardSignature = int.MinValue;
	private int _lastGroupSignature = int.MinValue;

	public LeaderboardPanel(VBoxContainer customUIPanel,
		PanelContainer leaderboardPanel, Label leaderboardTitleLabel, VBoxContainer leaderboardSelectors, VBoxContainer leaderboardContent)
	{
		_customUIPanel = customUIPanel;
		_leaderboardPanel = leaderboardPanel;
		_leaderboardTitleLabel = leaderboardTitleLabel;
		_leaderboardSelectors = leaderboardSelectors;
		_leaderboardContent = leaderboardContent;
		CreateSelectors();
	}

	private void CreateSelectors()
	{
		_boardSelectButton = CreateSelectorButton("BoardSelectButton");
		_boardSelectButton.ItemSelected += OnBoardSelected;
		_leaderboardSelectors.AddChild(_boardSelectButton);

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
		_leaderboardSelectors.AddChild(_groupSelectRow);
	}

	private static OptionButton CreateSelectorButton(string nodeName)
	{
		var button = new OptionButton();
		button.Name = nodeName;
		button.CustomMinimumSize = new Vector2(170, 26);
		button.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		button.FocusMode = Control.FocusModeEnum.All;
		button.AddThemeStyleboxOverride("normal", UIStyle.CreateButtonNormal());
		button.AddThemeStyleboxOverride("hover", UIStyle.CreateButtonHover());
		button.AddThemeStyleboxOverride("pressed", UIStyle.CreateButtonPressed());
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

	public void Update(InGameHUDViewModel viewModel)
	{
		bool allowLeaderboard = GameContext.InGameHud?.IsHudElementVisible(HudElement.Leaderboard, true) ?? true;
		LeaderboardBoards boards = viewModel.Leaderboards;
		LeaderboardBoard? selectedBoard = ResolveSelectedBoard(boards);
		bool leaderboardVisible = selectedBoard != null && allowLeaderboard;
		if (_leaderboardPanel.Visible != leaderboardVisible)
		{
			_leaderboardPanel.Visible = leaderboardVisible;
		}
		if (!leaderboardVisible || selectedBoard == null)
		{
			return;
		}

		bool revisionChanged = boards.Revision != _lastRevision;
		_lastRevision = boards.Revision;

		bool boardChanged = !string.Equals(_selectedBoardId, selectedBoard.Id, StringComparison.Ordinal);
		if (boardChanged)
		{
			_selectedBoardId = selectedBoard.Id;
			_hasGroupSelection = false;
		}

		int boardSignature = ComputeBoardSignature(boards);
		if (revisionChanged || boardChanged || boardSignature != _lastBoardSignature)
		{
			_lastBoardSignature = boardSignature;
			RebuildBoardOptions(boards);
		}

		int groupSignature = ComputeGroupSignature(selectedBoard);
		if (revisionChanged || boardChanged || groupSignature != _lastGroupSignature)
		{
			_lastGroupSignature = groupSignature;
			RebuildGroupOptions(selectedBoard);
		}

		string title = TranslationServer.Translate(selectedBoard.Title ?? string.Empty);
		if (_lastLeaderboardTitle != title)
		{
			_leaderboardTitleLabel.Text = title;
			_lastLeaderboardTitle = title;
		}

		RenderRows(selectedBoard);
	}

	private static int ComputeBoardSignature(LeaderboardBoards boards)
	{
		int signature = 17;
		IReadOnlyList<LeaderboardBoard> allBoards = boards.Boards;
		for (int index = 0; index < allBoards.Count; index++)
		{
			LeaderboardBoard board = allBoards[index];
			if (!board.Visible)
				continue;

			signature = (signature * 31) + board.Id.GetHashCode();
			signature = (signature * 31) + (board.Title?.GetHashCode() ?? 0);
		}

		return signature;
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

	private LeaderboardBoard? ResolveSelectedBoard(LeaderboardBoards boards)
	{
		LeaderboardBoard? selected = null;
		if (!string.IsNullOrEmpty(_selectedBoardId))
		{
			selected = boards.FindBoard(_selectedBoardId);
			if (selected != null && selected.Visible)
				return selected;
		}

		IReadOnlyList<LeaderboardBoard> allBoards = boards.Boards;
		for (int index = 0; index < allBoards.Count; index++)
		{
			if (allBoards[index].Visible)
			{
				selected = allBoards[index];
				_selectedBoardId = selected.Id;
				_hasGroupSelection = false;
				return selected;
			}
		}

		return null;
	}

	private void RebuildBoardOptions(LeaderboardBoards boards)
	{
		_boardIds.Clear();
		_boardSelectButton.Clear();

		int selectedIndex = 0;
		IReadOnlyList<LeaderboardBoard> allBoards = boards.Boards;
		for (int index = 0; index < allBoards.Count; index++)
		{
			LeaderboardBoard board = allBoards[index];
			if (!board.Visible)
				continue;

			if (string.Equals(board.Id, _selectedBoardId, StringComparison.Ordinal))
				selectedIndex = _boardIds.Count;

			_boardIds.Add(board.Id);
			_boardSelectButton.AddItem(TranslationServer.Translate(board.Title ?? string.Empty));
		}

		_boardSelectButton.Visible = _boardIds.Count > 1;
		if (_boardIds.Count > 0)
			_boardSelectButton.Select(selectedIndex);
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
		if (index < 0 || index >= _boardIds.Count)
			return;

		string boardId = _boardIds[(int)index];
		if (string.Equals(boardId, _selectedBoardId, StringComparison.Ordinal))
			return;

		GameContext.UserInterface?.PlayClickSound();
		_selectedBoardId = boardId;
		_hasGroupSelection = false;
		_lastRevision = -1;
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
