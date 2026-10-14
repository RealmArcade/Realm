namespace Realm.Ecs.Components.Meta
{
	/// <summary>
	/// Represents the current animation state for a unit during replay playback.
	/// </summary>
	public struct ReplayAnimationState
	{
		public string Animation;

		public ReplayAnimationState(string animation)
		{
			Animation = animation;
		}
	}
}