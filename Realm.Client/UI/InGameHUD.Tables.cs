// The leaderboard and summary-table API the simulation calls: set, clear and group boards, add rows,
// and the visibility flip the scoreboard panel watches.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Realm.Ecs;
using Godot;
using Realm.Client.Core;
using Realm.Client.Network;
using Realm.Client.ReplaySystem;
using Realm.Client.Services;
using Realm.Client.UI.InGame;
using Realm.Client.Utils;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Terrain;
using Realm.Ecs.Services;
using Vector3 = Godot.Vector3;
using static Godot.Control;
using static Godot.CanvasItem;


namespace Realm.Client.UI;

public partial class InGameHUD
{
	public void SetLeaderboardBoard(string boardId, string title, bool visible)
	{
		_viewModel.Leaderboards.SetBoard(boardId, title, visible);
	}

	public void ClearLeaderboardBoards()
	{
		_viewModel.Leaderboards.ClearAll();
	}

	public void ClearLeaderboardBoard(string boardId)
	{
		_viewModel.Leaderboards.ClearBoard(boardId);
	}

	public void SetLeaderboardBoardGroup(string boardId, int groupPlayerIndex, string groupTitle)
	{
		_viewModel.Leaderboards.SetGroup(boardId, groupPlayerIndex, groupTitle);
	}

	public void AddLeaderboardBoardRow(string boardId, int groupPlayerIndex, string groupTitle, string label, string value)
	{
		_viewModel.Leaderboards.AddRow(boardId, groupPlayerIndex, groupTitle, label, value);
	}

	private void UpdateScoreboardVisibility()
	{
		_scoreboardPanelController?.Update(_viewModel);
	}

	public void ShowSummaryTable(string title, bool visible)
	{
		_viewModel.SummaryTableTitle = title;
		_viewModel.SummaryTableVisible = visible;
	}

	public void ClearSummaryTable()
	{
		_viewModel.SummaryTableRows.Clear();
	}

	public void SetSummaryTableHeaders(string[] headers)
	{
		_viewModel.SummaryTableHeaders = headers;
	}

	public void SetSummaryTableRow(string rowKey, string[] cellValues)
	{
		_viewModel.SummaryTableRows[rowKey] = cellValues;
	}
}
