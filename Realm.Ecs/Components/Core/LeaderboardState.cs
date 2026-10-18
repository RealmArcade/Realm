using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Realm.Ecs.Components.Core
{
	/// <summary>
	///     Represents the current leaderboard display state.
	/// </summary>
	[StructLayout(LayoutKind.Sequential, Pack = 4)]
	public struct LeaderboardState
	{
		public byte VisibleByte;

		public bool Visible
		{
			get => VisibleByte != 0;
			set => VisibleByte = value ? (byte)1 : (byte)0;
		}

		public int TitleStringId;
		public int ValuesHandle;

		public LeaderboardState(bool visible, string title, Dictionary<string, string> values)
		{
			VisibleByte = visible ? (byte)1 : (byte)0;
			TitleStringId = StringRegistry.GetOrAdd(title);
			ValuesHandle = ColdPayloadRegistry<Dictionary<string, string>>.Register(values);
		}

		public string Title
		{
			get => StringRegistry.Get(TitleStringId);
			set => TitleStringId = StringRegistry.GetOrAdd(value);
		}

		public Dictionary<string, string> Values
		{
			get => ColdPayloadRegistry<Dictionary<string, string>>.Get(ValuesHandle);
			set => ValuesHandle = ColdPayloadRegistry<Dictionary<string, string>>.Register(value);
		}
	}
}
