using System.Runtime.InteropServices;
namespace Realm.Ecs.Components.Meta
{
	/// <summary>
	/// Represents the current animation state for a unit during replay playback.
	/// </summary>
	[StructLayout(LayoutKind.Sequential, Pack = 4)]
	public struct ReplayAnimationState
	{
		public int StringId;

		public ReplayAnimationState(string animation)
		{
			StringId = StringRegistry.GetOrAdd(animation);
		}

		public string Animation
		{
			get => StringRegistry.Get(StringId);
			set => StringId = StringRegistry.GetOrAdd(value);
		}
	}
}
