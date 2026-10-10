namespace Realm.Ecs.Components.Core
{
	/// <summary>
	///     Represents the active countdown timer state.
	/// </summary>
	public struct CountdownState
	{
		public bool Active;
		public float Duration;
		public string Text;

		public CountdownState(bool active, float duration, string text)
		{
			Active = active;
			Duration = duration;
			Text = text;
		}
	}
}
