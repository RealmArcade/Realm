using System.Runtime.InteropServices;

namespace Realm.Ecs.Components.Terrain;

/// <summary>
/// Points the world entity at its registry-owned shroud grid buffers and stores the map-authored shroud visibility mode.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct ShroudState
{
	public int GridHandle;
	public ShroudVisibilityMode VisibilityMode;
}
