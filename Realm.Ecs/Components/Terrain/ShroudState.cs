using System;
using System.Runtime.InteropServices;

namespace Realm.Ecs.Components.Terrain;

/// <summary>
/// Stores fog of war exploration and visibility grid state.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct ShroudState
{
	public const byte ExplorationShroud = 2;
	public const byte VisionShroud = 1;
	public const byte Visible = 0;

	public int GridHandle;
	public ShroudType Type;

	public ShroudState(byte[,] shroudGrid, ShroudType shroudType)
	{
		GridHandle = ColdPayloadRegistry<byte[,]>.Register(shroudGrid);
		Type = shroudType;
	}

	public ShroudState(byte[,] shroudGrid, string shroudType)
		: this(shroudGrid, ParseShroudType(shroudType))
	{
	}

	public byte[,] ShroudGrid
	{
		get => ColdPayloadRegistry<byte[,]>.Get(GridHandle);
		set => GridHandle = ColdPayloadRegistry<byte[,]>.Register(value);
	}

	public string ShroudType
	{
		get => Type switch
		{
			Terrain.ShroudType.VisionShroud => "VisionShroud",
			Terrain.ShroudType.ExplorationShroud => "ExplorationShroud",
			_ => "visible"
		};
		set => Type = ParseShroudType(value);
	}

	private static ShroudType ParseShroudType(string? type)
	{
		if (string.Equals(type, "ExplorationShroud", StringComparison.OrdinalIgnoreCase)) return Terrain.ShroudType.ExplorationShroud;
		if (string.Equals(type, "VisionShroud", StringComparison.OrdinalIgnoreCase)) return Terrain.ShroudType.VisionShroud;
		return Terrain.ShroudType.Visible;
	}
}
