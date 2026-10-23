using System.Runtime.InteropServices;
using Realm.Ecs.Components.Terrain;
using Realm.MapAPI;

namespace Realm.Ecs.Components.Core
{
	/// <summary>
	///     Holds the state and configuration of the map editor, including block modes, camera boundary limits, and file status.
	/// </summary>
	[StructLayout(LayoutKind.Sequential, Pack = 4)]
	public struct EditorState
	{
		public byte BlockModeByte;

		public bool BlockMode
		{
			get => BlockModeByte != 0;
			set => BlockModeByte = value ? (byte)1 : (byte)0;
		}

		public float BlockLevelHeight;
		public float CameraBoundsLeft;
		public float CameraBoundsRight;
		public float CameraBoundsTop;
		public float CameraBoundsBottom;
		public int SkyboxStringId;
		public byte HasUnsavedChangesByte;

		public bool HasUnsavedChanges
		{
			get => HasUnsavedChangesByte != 0;
			set => HasUnsavedChangesByte = value ? (byte)1 : (byte)0;
		}

		public MirrorMode MirrorMode;
		public WaterType WaterMode;
		public byte WaterProfileIndex;

		public EditorState(
			bool blockMode,
			float blockLevelHeight,
			float cameraBoundsLeft,
			float cameraBoundsRight,
			float cameraBoundsTop,
			float cameraBoundsBottom,
			string skyboxPath,
			bool hasUnsavedChanges,
			MirrorMode mirrorMode = MirrorMode.None,
			WaterType waterMode = WaterType.None,
			byte waterProfileIndex = 0)
		{
			BlockModeByte = blockMode ? (byte)1 : (byte)0;
			BlockLevelHeight = blockLevelHeight;
			CameraBoundsLeft = cameraBoundsLeft;
			CameraBoundsRight = cameraBoundsRight;
			CameraBoundsTop = cameraBoundsTop;
			CameraBoundsBottom = cameraBoundsBottom;
			SkyboxStringId = StringRegistry.GetOrAdd(skyboxPath);
			HasUnsavedChangesByte = hasUnsavedChanges ? (byte)1 : (byte)0;
			MirrorMode = mirrorMode;
			WaterMode = waterMode;
			WaterProfileIndex = waterProfileIndex;
		}

		public string SkyboxPath
		{
			get => StringRegistry.Get(SkyboxStringId);
			set => SkyboxStringId = StringRegistry.GetOrAdd(value);
		}
	}
}
