using System.Runtime.InteropServices;
using Realm.Ecs.Common;

namespace Realm.Ecs.Components.Core
{
	/// <summary>
	///     Holds the leaderboard boards published by the active map script.
	/// </summary>
	[StructLayout(LayoutKind.Sequential, Pack = 4)]
	public struct LeaderboardState
	{
		public int BoardsHandle;

		public LeaderboardState(LeaderboardBoards boards)
		{
			BoardsHandle = ColdPayloadRegistry<LeaderboardBoards>.Register(boards);
		}

		public LeaderboardBoards Boards
		{
			get => ColdPayloadRegistry<LeaderboardBoards>.Get(BoardsHandle)!;
			set => BoardsHandle = ColdPayloadRegistry<LeaderboardBoards>.Register(value);
		}
	}
}
