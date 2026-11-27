using System;
using System.Collections.Generic;

namespace Realm.MapGeneration;

public class MapFolderTerrain
{
	public int Width { get; set; }

	public int Depth { get; set; }

	public float[] VertexHeights { get; set; } = Array.Empty<float>();

	public float[] CellHeights { get; set; } = Array.Empty<float>();

	public float[] WaterSurfaceHeights { get; set; } = Array.Empty<float>();

	public byte[] WaterModes { get; set; } = Array.Empty<byte>();

	public int[] SplatIndices { get; set; } = Array.Empty<int>();

	public byte[] PathingCodes { get; set; } = Array.Empty<byte>();

	public List<MapPlacedProp> Props { get; } = new();

	public List<MapTextureSource> Textures { get; } = new();

	public string FolderPath { get; set; } = string.Empty;

	public string? ThumbnailPath { get; set; }

	public MapGenerationPalette BuildPalette(MinimapTextureLibrary? textureLibrary = null)
	{
		var palette = new MapGenerationPalette();
		for (int index = 0; index < Textures.Count; index++)
		{
			MapTextureSource texture = Textures[index];
			if (textureLibrary != null)
			{
				MinimapTexture sampled = textureLibrary.Get(texture);
				palette.Add(
					texture.SwatchIndex,
					texture.Name,
					texture.DefaultPathingCode,
					Math.Clamp(sampled.AverageRed * texture.Brightness * texture.TintRed, 0f, 1f),
					Math.Clamp(sampled.AverageGreen * texture.Brightness * texture.TintGreen, 0f, 1f),
					Math.Clamp(sampled.AverageBlue * texture.Brightness * texture.TintBlue, 0f, 1f));
				continue;
			}

			palette.Add(texture.SwatchIndex, texture.Name, texture.DefaultPathingCode);
		}

		return palette;
	}

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

	public bool HasWater(int x, int z)
	{
		if (WaterModes.Length != Width * Depth)
		{
			return false;
		}

		return WaterModes[CellIndex(x, z)] > 0;
	}

	public float WaterSurfaceAtCell(int x, int z)
	{
		if (WaterSurfaceHeights.Length != Width * Depth)
		{
			return 0f;
		}

		return WaterSurfaceHeights[CellIndex(x, z)];
	}
}
