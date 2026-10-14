using Godot;

namespace Realm.Client.Core
{
	public partial class GameHost
	{
		private void FocusCameraOnCoordinate(EditorCoordinate coord)
		{
			float centerX = (coord.MinX + coord.MaxX) / 2.0f;
			float centerZ = (coord.MinZ + coord.MaxZ) / 2.0f;
			float centerY = GetTerrainHeightAt(new Vector3(centerX, 0f, centerZ));

			var camera = MainCamera ?? GetViewport()?.GetCamera3D() as CameraControl;
			if (camera != null)
			{
				camera.FocusOnPosition(new Vector3(centerX, centerY, centerZ));
			}
		}
	}
}