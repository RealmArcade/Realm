using Realm.Ecs.Components.Terrain;

namespace Realm.Ecs.Components.Core
{
	public enum MirrorMode
	{
		None,
		Horizontal,
		Vertical,
		Both
	}

	/// <summary>
	///     Holds the state and configuration of the map editor, including block modes, camera boundary limits, and file status.
	/// </summary>
	internal struct EditorState
	{
		public bool BlockMode;
		public float BlockLevelHeight;
		public float CameraBoundsLeft;
		public float CameraBoundsRight;
		public float CameraBoundsTop;
		public float CameraBoundsBottom;
		public string SkyboxPath;
		public bool HasUnsavedChanges;
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
			BlockMode = blockMode;
			BlockLevelHeight = blockLevelHeight;
			CameraBoundsLeft = cameraBoundsLeft;
			CameraBoundsRight = cameraBoundsRight;
			CameraBoundsTop = cameraBoundsTop;
			CameraBoundsBottom = cameraBoundsBottom;
			SkyboxPath = skyboxPath;
			HasUnsavedChanges = hasUnsavedChanges;
			MirrorMode = mirrorMode;
			WaterMode = waterMode;
			WaterProfileIndex = waterProfileIndex;
		}
	}
}

