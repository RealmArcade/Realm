namespace Realm.Ecs.Components.Core
{
	/// <summary>
	///     Represents the current multiplayer summary statistics table display state.
	/// </summary>
	public struct SummaryTableState
	{
		public bool Visible;
		public string Title;
		public string[] Headers;
		public Dictionary<string, string[]> Rows;

		public SummaryTableState(bool visible, string title, string[] headers, Dictionary<string, string[]> rows)
		{
			Visible = visible;
			Title = title;
			Headers = headers;
			Rows = rows;
		}
	}
}
