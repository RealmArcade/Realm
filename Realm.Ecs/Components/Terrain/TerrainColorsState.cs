using System.Runtime.InteropServices;

namespace Realm.Ecs.Components.Terrain;

/// <summary>
/// Holds the terrain color palette configurations.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct TerrainColorsState
{
	public int Handle;

	public TerrainColorsState(string[] colors)
	{
		Handle = ColdPayloadRegistry<string[]>.Register(colors);
	}

	public string[] Colors
	{
		get => ColdPayloadRegistry<string[]>.Get(Handle);
		set => Handle = ColdPayloadRegistry<string[]>.Register(value);
	}
}
