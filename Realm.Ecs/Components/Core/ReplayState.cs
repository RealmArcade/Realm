using System.Runtime.InteropServices;
namespace Realm.Ecs.Components.Core
{
	/// <summary>
	///     Holds replay recording and playback metadata, tracking tick progress and resource fallbacks.
	/// </summary>
	[StructLayout(LayoutKind.Sequential, Pack = 4)]
	public struct ReplayState
	{
		public int ReplayTickCounter;
		public float GoldBackup;
		public float WoodBackup;
		public float StoneBackup;

		public ReplayState(int replayTickCounter, float goldBackup, float woodBackup, float stoneBackup)
		{
			ReplayTickCounter = replayTickCounter;
			GoldBackup = goldBackup;
			WoodBackup = woodBackup;
			StoneBackup = stoneBackup;
		}
	}
}