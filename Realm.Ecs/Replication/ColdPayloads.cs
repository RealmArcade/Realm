using System;
using System.Collections.Generic;
using MemoryPack;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Core;
using Realm.MapAPI;

namespace Realm.Ecs.Replication;

public enum ColdPayloadKind : byte
{
	InternedPayload = 0,
	LeaderboardBoards = 1,
	SummaryTable = 2
}

public enum ColdPayloadRole : byte
{
	None = 0,
	ZoneList = 1,
	ProductionQueue = 2,
	AbilityList = 3
}

[MemoryPackable]
public partial struct ColdPayloadDelta
{
	public byte Scope;
	public int Key;
	public byte Kind;
	public byte Role;
	public byte InternedKind;
	public int ContentId;
	public byte Destroyed;
	public byte[] Bytes;
}

[MemoryPackable]
public partial class LeaderboardBoardsSnapshot
{
	public LeaderboardBoardSnapshot[] Boards = Array.Empty<LeaderboardBoardSnapshot>();
}

[MemoryPackable]
public partial class LeaderboardBoardSnapshot
{
	public string Id = string.Empty;
	public string Title = string.Empty;
	public bool Visible;
	public LeaderboardBoardGroupSnapshot[] Groups = Array.Empty<LeaderboardBoardGroupSnapshot>();
	public LeaderboardBoardRowSnapshot[] Rows = Array.Empty<LeaderboardBoardRowSnapshot>();
}

[MemoryPackable]
public partial class LeaderboardBoardGroupSnapshot
{
	public int PlayerIndex;
	public string Title = string.Empty;
}

[MemoryPackable]
public partial class LeaderboardBoardRowSnapshot
{
	public int GroupPlayerIndex;
	public string GroupTitle = string.Empty;
	public string Label = string.Empty;
	public string Value = string.Empty;
}

[MemoryPackable]
public partial class SummaryTableSnapshot
{
	public bool Visible;
	public int TitleStringId;
	public string[] Headers = Array.Empty<string>();
	public string[] RowKeys = Array.Empty<string>();
	public string[][] RowValues = Array.Empty<string[]>();
}

public static class ColdPayloadCodecs
{
	public static int ComputeContentId(ReadOnlySpan<byte> bytes)
	{
		uint hash = 2166136261u;
		for (int index = 0; index < bytes.Length; index++)
		{
			hash ^= bytes[index];
			hash *= 16777619u;
		}

		int contentId = unchecked((int)hash);
		return contentId == 0 ? 1 : contentId;
	}

	public static byte[] SerializeLeaderboards(LeaderboardBoards? boards)
	{
		var snapshot = new LeaderboardBoardsSnapshot();
		if (boards != null)
		{
			snapshot.Boards = new LeaderboardBoardSnapshot[boards.Boards.Count];
			for (int boardIndex = 0; boardIndex < boards.Boards.Count; boardIndex++)
			{
				LeaderboardBoard board = boards.Boards[boardIndex];
				var boardSnapshot = new LeaderboardBoardSnapshot
				{
					Id = board.Id,
					Title = board.Title,
					Visible = board.Visible,
					Groups = new LeaderboardBoardGroupSnapshot[board.Groups.Count],
					Rows = new LeaderboardBoardRowSnapshot[board.Rows.Count]
				};

				for (int groupIndex = 0; groupIndex < board.Groups.Count; groupIndex++)
				{
					LeaderboardBoardGroup group = board.Groups[groupIndex];
					boardSnapshot.Groups[groupIndex] = new LeaderboardBoardGroupSnapshot
					{
						PlayerIndex = group.PlayerIndex,
						Title = group.Title
					};
				}

				for (int rowIndex = 0; rowIndex < board.Rows.Count; rowIndex++)
				{
					LeaderboardBoardRow row = board.Rows[rowIndex];
					boardSnapshot.Rows[rowIndex] = new LeaderboardBoardRowSnapshot
					{
						GroupPlayerIndex = row.GroupPlayerIndex,
						GroupTitle = row.GroupTitle,
						Label = row.Label,
						Value = row.Value
					};
				}

				snapshot.Boards[boardIndex] = boardSnapshot;
			}
		}

		return MemoryPackSerializer.Serialize(snapshot);
	}

	public static void ApplyLeaderboards(EcsWorld world, Entity worldEntity, ReadOnlySpan<byte> bytes)
	{
		if (world == null || worldEntity == Entity.Null || !world.IsAlive(worldEntity)) return;

		var snapshot = MemoryPackSerializer.Deserialize<LeaderboardBoardsSnapshot>(bytes.ToArray());
		var boards = new LeaderboardBoards();
		for (int boardIndex = 0; boardIndex < snapshot.Boards.Length; boardIndex++)
		{
			LeaderboardBoardSnapshot boardSnapshot = snapshot.Boards[boardIndex];
			boards.SetBoard(boardSnapshot.Id, boardSnapshot.Title, boardSnapshot.Visible);
			for (int groupIndex = 0; groupIndex < boardSnapshot.Groups.Length; groupIndex++)
			{
				LeaderboardBoardGroupSnapshot group = boardSnapshot.Groups[groupIndex];
				boards.SetGroup(boardSnapshot.Id, group.PlayerIndex, group.Title);
			}

			for (int rowIndex = 0; rowIndex < boardSnapshot.Rows.Length; rowIndex++)
			{
				LeaderboardBoardRowSnapshot row = boardSnapshot.Rows[rowIndex];
				boards.AddRow(boardSnapshot.Id, row.GroupPlayerIndex, row.GroupTitle, row.Label, row.Value);
			}
		}

		var state = new LeaderboardState(boards);
		world.SetOrAdd(worldEntity, in state);
	}

	public static byte[] SerializeSummaryTable(in SummaryTableState state)
	{
		string[] headers = state.Headers ?? Array.Empty<string>();
		Dictionary<string, string[]> rows = state.Rows;
		int rowCount = rows?.Count ?? 0;
		var snapshot = new SummaryTableSnapshot
		{
			Visible = state.Visible,
			TitleStringId = state.TitleStringId,
			Headers = headers,
			RowKeys = new string[rowCount],
			RowValues = new string[rowCount][]
		};

		if (rows != null)
		{
			int rowIndex = 0;
			foreach (var pair in rows)
			{
				snapshot.RowKeys[rowIndex] = pair.Key;
				snapshot.RowValues[rowIndex] = pair.Value ?? Array.Empty<string>();
				rowIndex++;
			}
		}

		return MemoryPackSerializer.Serialize(snapshot);
	}

	public static void ApplySummaryTable(EcsWorld world, Entity worldEntity, ReadOnlySpan<byte> bytes)
	{
		if (world == null || worldEntity == Entity.Null || !world.IsAlive(worldEntity)) return;

		var snapshot = MemoryPackSerializer.Deserialize<SummaryTableSnapshot>(bytes.ToArray());
		var rows = new Dictionary<string, string[]>(snapshot.RowKeys.Length, StringComparer.Ordinal);
		for (int index = 0; index < snapshot.RowKeys.Length; index++)
		{
			rows[snapshot.RowKeys[index]] = snapshot.RowValues[index] ?? Array.Empty<string>();
		}

		if (world.Has<SummaryTableState>(worldEntity))
		{
			ref var state = ref world.Get<SummaryTableState>(worldEntity);
			state.Visible = snapshot.Visible;
			state.TitleStringId = snapshot.TitleStringId;
			state.Headers = snapshot.Headers;
			state.Rows = rows;
			return;
		}

		world.Add(worldEntity, new SummaryTableState(snapshot.Visible, string.Empty, snapshot.Headers, rows)
		{
			TitleStringId = snapshot.TitleStringId
		});
	}
}
