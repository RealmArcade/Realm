using System.Collections.Generic;

namespace Realm.Ecs.Common;

public class LeaderboardBoardRow
{
	public int GroupPlayerIndex = -1;
	public string GroupTitle = string.Empty;
	public string Label = string.Empty;
	public string Value = string.Empty;
}

public class LeaderboardBoardGroup
{
	public int PlayerIndex = -1;
	public string Title = string.Empty;
}

public class LeaderboardBoard
{
	public string Id = string.Empty;
	public string Title = string.Empty;
	public bool Visible;
	public readonly List<LeaderboardBoardGroup> Groups = new();
	public readonly List<LeaderboardBoardRow> Rows = new();
}

public class LeaderboardBoards
{
	private readonly List<LeaderboardBoard> _boards = new();

	public IReadOnlyList<LeaderboardBoard> Boards => _boards;

	public int Revision { get; private set; }

	public void ClearAll()
	{
		_boards.Clear();
		Revision++;
	}

	public LeaderboardBoard? FindBoard(string boardId)
	{
		for (int index = 0; index < _boards.Count; index++)
		{
			if (string.Equals(_boards[index].Id, boardId, System.StringComparison.Ordinal))
				return _boards[index];
		}

		return null;
	}

	public LeaderboardBoard GetOrCreateBoard(string boardId)
	{
		LeaderboardBoard? existing = FindBoard(boardId);
		if (existing != null)
			return existing;

		var board = new LeaderboardBoard { Id = boardId };
		_boards.Add(board);
		Revision++;
		return board;
	}

	public void SetBoard(string boardId, string title, bool visible)
	{
		LeaderboardBoard board = GetOrCreateBoard(boardId);
		board.Title = title;
		board.Visible = visible;
		Revision++;
	}

	public void ClearBoard(string boardId)
	{
		LeaderboardBoard? board = FindBoard(boardId);
		if (board == null)
			return;

		board.Rows.Clear();
		board.Groups.Clear();
		Revision++;
	}

	public void SetGroup(string boardId, int playerIndex, string title)
	{
		LeaderboardBoard board = GetOrCreateBoard(boardId);
		for (int index = 0; index < board.Groups.Count; index++)
		{
			LeaderboardBoardGroup existing = board.Groups[index];
			if (existing.PlayerIndex != playerIndex)
				continue;

			existing.Title = title;
			Revision++;
			return;
		}

		board.Groups.Add(new LeaderboardBoardGroup { PlayerIndex = playerIndex, Title = title });
		Revision++;
	}

	public void AddRow(string boardId, int groupPlayerIndex, string groupTitle, string label, string value)
	{
		LeaderboardBoard board = GetOrCreateBoard(boardId);
		for (int index = 0; index < board.Rows.Count; index++)
		{
			LeaderboardBoardRow existing = board.Rows[index];
			if (existing.GroupPlayerIndex != groupPlayerIndex || !string.Equals(existing.Label, label, System.StringComparison.Ordinal))
				continue;

			existing.Value = value;
			existing.GroupTitle = groupTitle;
			Revision++;
			return;
		}

		board.Rows.Add(new LeaderboardBoardRow
		{
			GroupPlayerIndex = groupPlayerIndex,
			GroupTitle = groupTitle,
			Label = label,
			Value = value
		});
		Revision++;
	}
}
