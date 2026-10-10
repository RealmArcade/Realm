
namespace Realm.Ecs.Components.Core
{
	/// <summary>
	///     Represents the current leaderboard display state.
	/// </summary>
	public struct LeaderboardState
	{
		public bool Visible;
		public string Title;
		public Dictionary<string, string> Values;

		public LeaderboardState(bool visible, string title, Dictionary<string, string> values)
		{
			Visible = visible;
			Title = title;
			Values = values;
		}
	}
}
