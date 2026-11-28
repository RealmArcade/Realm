using System;
using System.Collections.Generic;

namespace Realm.MapGeneration;

public class ReconstructedTerrain
{
	public int Width { get; set; }

	public int Depth { get; set; }

	public float QuadSize { get; set; } = 2f;

	public float TierHeight { get; set; } = 3f;

	public float[] VertexHeights { get; set; } = Array.Empty<float>();

	public float[] CellHeights { get; set; } = Array.Empty<float>();

	public float[] WaterSurfaceHeights { get; set; } = Array.Empty<float>();

	public byte[] WaterModes { get; set; } = Array.Empty<byte>();

	public int[] SplatIndices { get; set; } = Array.Empty<int>();

	public int[] CliffSplatIndices { get; set; } = Array.Empty<int>();

	public byte[] PathingCodes { get; set; } = Array.Empty<byte>();

	public TerrainSurfaceKind[] CellKinds { get; set; } = Array.Empty<TerrainSurfaceKind>();

	public bool[] WaterMask { get; set; } = Array.Empty<bool>();

	public float[] RegionColours { get; set; } = Array.Empty<float>();

	public int[] SegmentLabels { get; set; } = Array.Empty<int>();

	public bool[] ForestMask { get; set; } = Array.Empty<bool>();

	public float[] FeatureColours { get; set; } = Array.Empty<float>();

	public List<GeneratedProp> Props { get; } = new();

	public int WaterProfileIndex { get; set; }

	public int VertexIndex(int x, int z)
	{
		return z * (Width + 1) + x;
	}

	public int CellIndex(int x, int z)
	{
		return z * Width + x;
	}

	public float HeightAtVertex(int x, int z)
	{
		return VertexHeights[VertexIndex(x, z)];
	}

	public float HeightAtCell(int x, int z)
	{
		return CellHeights[CellIndex(x, z)];
	}

	public bool IsWater(int x, int z)
	{
		return WaterMask.Length == Width * Depth && WaterMask[CellIndex(x, z)];
	}
}
