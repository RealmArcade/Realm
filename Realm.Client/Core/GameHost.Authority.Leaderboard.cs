using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Godot;
using Realm.Client.Services;
using Realm.Client.UI;
using Realm.Client.VFX;
using Realm.Ecs;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Replication;
using Realm.Ecs.Services;
using Realm.Shared.Metadata;
using Realm.Shared.Network;

namespace Realm.Client.Core;


public partial class GameHost
{
	private void PresentLeaderboard(PresentationMessage message)
	{
		switch ((PresentationKind)message.Kind)
		{
			case PresentationKind.LeaderboardVisible:
				QueueLeaderboardOperation(new LeaderboardOperation
				{
					Kind = LeaderboardOperationKind.SetBoard,
					BoardId = DefaultLeaderboardBoardId,
					Title = message.Key,
					Visible = message.Flag
				});
				break;

			case PresentationKind.LeaderboardValue:
				QueueLeaderboardOperation(new LeaderboardOperation
				{
					Kind = LeaderboardOperationKind.AddRow,
					BoardId = DefaultLeaderboardBoardId,
					GroupPlayerIndex = -1,
					Label = message.Key,
					Value = message.Text
				});
				break;

			case PresentationKind.LeaderboardCleared:
				QueueLeaderboardOperation(new LeaderboardOperation { Kind = LeaderboardOperationKind.ClearAll });
				break;

			case PresentationKind.LeaderboardBoard:
				QueueLeaderboardOperation(new LeaderboardOperation
				{
					Kind = LeaderboardOperationKind.SetBoard,
					BoardId = message.Key,
					Title = message.Text,
					Visible = message.Flag
				});
				break;

			case PresentationKind.LeaderboardBoardCleared:
				QueueLeaderboardOperation(new LeaderboardOperation
				{
					Kind = LeaderboardOperationKind.ClearBoard,
					BoardId = message.Key
				});
				break;

			case PresentationKind.LeaderboardBoardGroup:
				QueueLeaderboardOperation(new LeaderboardOperation
				{
					Kind = LeaderboardOperationKind.SetGroup,
					BoardId = message.Key,
					GroupPlayerIndex = message.Count,
					GroupTitle = message.Text
				});
				break;

			case PresentationKind.LeaderboardBoardRow:
				QueueLeaderboardOperation(new LeaderboardOperation
				{
					Kind = LeaderboardOperationKind.AddRow,
					BoardId = message.Key,
					GroupPlayerIndex = message.Count,
					GroupTitle = message.Text,
					Label = message.Text2,
					Value = message.Text3
				});
				break;

			case PresentationKind.LeaderboardRow:
				QueueLeaderboardOperation(new LeaderboardOperation
				{
					Kind = LeaderboardOperationKind.AddRow,
					BoardId = string.IsNullOrWhiteSpace(message.Key) ? DefaultLeaderboardBoardId : message.Key,
					GroupPlayerIndex = -1,
					Label = message.Text,
					Value = message.Text2
				});
				break;
		}
	}

}
