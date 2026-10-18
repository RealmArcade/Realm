using System.Runtime.InteropServices;

namespace Realm.Ecs.Components.Core
{
	/// <summary>
	///     Represents the active countdown timer state.
	/// </summary>
	[StructLayout(LayoutKind.Sequential, Pack = 4)]
	public struct CountdownState
	{
		public bool Active;
		public float Duration;
		public int TextStringId;

		public CountdownState(bool active, float duration, string text)
		{
			Active = active;
			Duration = duration;
			TextStringId = StringRegistry.GetOrAdd(text);
		}

		public string Text
		{
			get => StringRegistry.Get(TextStringId);
			set => TextStringId = StringRegistry.GetOrAdd(value);
		}
	}
}
