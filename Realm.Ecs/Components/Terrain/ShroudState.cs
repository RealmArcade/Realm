using System.Runtime.InteropServices;

namespace Realm.Ecs.Components.Terrain;

[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct ShroudState
{
	public const byte ExplorationShroud = 2;
	public const byte VisionShroud = 1;
	public const byte Visible = 0;

	public int GridHandle;
	public int TypeStringId;

	public ShroudState(byte[,] shroudGrid, string shroudType)
	{
		GridHandle = ColdPayloadRegistry<byte[,]>.Register(shroudGrid);
		TypeStringId = StringRegistry.GetOrAdd(shroudType);
	}

	public byte[,] ShroudGrid
	{
		get => ColdPayloadRegistry<byte[,]>.Get(GridHandle);
		set => GridHandle = ColdPayloadRegistry<byte[,]>.Register(value);
	}

	public string ShroudType
	{
		get => StringRegistry.Get(TypeStringId);
		set => TypeStringId = StringRegistry.GetOrAdd(value);
	}
}
