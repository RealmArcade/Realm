using Realm.Ecs.Components.Terrain;
using Realm.Shared.Terrain;
using Realm.Shared.Textures;

namespace Realm.Shared.Maps;

/// <summary>
///     The terrain half of a map folder: the height, water, pathing and splat images, their canonical file names and
///     the packing that turns a <see cref="TerrainCell" /> grid into those images and back. The client editor and the
///     authoritative process both go through here, so a map decodes to the same terrain wherever it is loaded.
/// </summary>
public static class MapTerrainFiles
{
	public const string HeightsFileName = "terrain_heights.exr";
	public const string WaterFileName = "terrain_water.exr";
	public const string PathingFileName = "terrain_pathing.png";
	public const string SplatPrefix = "terrain_splat";
	public const string CliffSplatPrefix = "terrain_cliff_splat";
	public const int ChannelsPerTexel = ExrImageCodec.ChannelsPerPixel;

	public static string HeightsPath(string directory) => Path.Combine(directory, HeightsFileName);
	public static string WaterPath(string directory) => Path.Combine(directory, WaterFileName);
	public static string PathingPath(string directory) => Path.Combine(directory, PathingFileName);
	public static string SplatIndicesPath(string directory, string prefix = SplatPrefix) => Path.Combine(directory, $"{prefix}_indices.exr");
	public static string SplatWeightsPath(string directory, string prefix = SplatPrefix) => Path.Combine(directory, $"{prefix}_weights.exr");

	public static bool TryResolveSplatDimensions(int width, int depth, int colorCount, out int splatWidth, out int splatDepth)
	{
		if (colorCount == (width + 1) * (depth + 1))
		{
			splatWidth = width + 1;
			splatDepth = depth + 1;
			return true;
		}

		if (colorCount == width * depth)
		{
			splatWidth = width;
			splatDepth = depth;
			return true;
		}

		splatWidth = 0;
		splatDepth = 0;
		return false;
	}

	public static float[] PackHeights(int width, int depth, TerrainCell[,]? cells)
	{
		float[] rgba = new float[width * depth * ChannelsPerTexel];
		for (int z = 0; z < depth; z++)
		{
			for (int x = 0; x < width; x++)
			{
				TerrainCell cell = cells != null ? cells[x, z] : default;
				int baseIndex = (z * width + x) * ChannelsPerTexel;
				rgba[baseIndex + 0] = cell.Y_NW;
				rgba[baseIndex + 1] = cell.Y_NE;
				rgba[baseIndex + 2] = cell.Y_SE;
				rgba[baseIndex + 3] = cell.Y_SW;
			}
		}

		return rgba;
	}

	public static float[] PackWater(int width, int depth, TerrainCell[,]? cells)
	{
		float[] rgba = new float[width * depth * ChannelsPerTexel];
		for (int z = 0; z < depth; z++)
		{
			for (int x = 0; x < width; x++)
			{
				TerrainCell cell = cells != null ? cells[x, z] : default;
				int baseIndex = (z * width + x) * ChannelsPerTexel;
				rgba[baseIndex + 0] = (float)cell.WaterMode;
				rgba[baseIndex + 1] = cell.WaterProfileIndex;
				rgba[baseIndex + 2] = cell.WaterHeight;
				rgba[baseIndex + 3] = 1f;
			}
		}

		return rgba;
	}

	public static byte[] PackPathing(int width, int depth, int[,]? pathingCodes, WaterType defaultWaterMode)
	{
		byte[] rgba = new byte[width * depth * PngImageCodec.BytesPerPixel];
		int defaultCode = TerrainPathing.DefaultCode(defaultWaterMode);

		for (int z = 0; z < depth; z++)
		{
			for (int x = 0; x < width; x++)
			{
				int code = pathingCodes != null ? pathingCodes[x, z] : defaultCode;
				int baseIndex = (z * width + x) * PngImageCodec.BytesPerPixel;
				rgba[baseIndex + 0] = (byte)code;
				rgba[baseIndex + 1] = 0;
				rgba[baseIndex + 2] = 0;
				rgba[baseIndex + 3] = 255;
			}
		}

		return rgba;
	}

	public static void WriteHeights(string directory, int width, int depth, TerrainCell[,]? cells)
	{
		ExrImageCodec.EncodeRgbaFloat(HeightsPath(directory), PackHeights(width, depth, cells), width, depth);
	}

	public static void WriteWater(string directory, int width, int depth, TerrainCell[,]? cells)
	{
		ExrImageCodec.EncodeRgbaFloat(WaterPath(directory), PackWater(width, depth, cells), width, depth);
	}

	public static void WriteHeightsAndWater(string directory, int width, int depth, TerrainCell[,]? cells)
	{
		WriteHeights(directory, width, depth, cells);
		WriteWater(directory, width, depth, cells);
	}

	public static void WritePathing(string directory, int width, int depth, int[,]? pathingCodes, WaterType defaultWaterMode)
	{
		PngImageCodec.EncodeRgba8(PathingPath(directory), PackPathing(width, depth, pathingCodes, defaultWaterMode), width, depth);
	}

	public static float[]? TryReadHeights(string directory, out int width, out int height)
	{
		return TryReadExr(HeightsPath(directory), out width, out height);
	}

	public static float[]? TryReadWater(string directory, out int width, out int height)
	{
		return TryReadExr(WaterPath(directory), out width, out height);
	}

	public static byte[]? TryReadPathing(string directory, out int width, out int height)
	{
		width = 0;
		height = 0;

		string path = PathingPath(directory);
		if (!File.Exists(path)) return null;

		return PngImageCodec.DecodeRgba8(path, out width, out height);
	}

	public static float[]? TryReadSplatIndices(string directory, string prefix, out int width, out int height)
	{
		return TryReadExr(SplatIndicesPath(directory, prefix), out width, out height);
	}

	public static float[]? TryReadSplatWeights(string directory, string prefix, out int width, out int height)
	{
		return TryReadExr(SplatWeightsPath(directory, prefix), out width, out height);
	}

	public static void WriteSplat(string directory, string prefix, int splatWidth, int splatDepth, string[] colors)
	{
		if (colors.Length != splatWidth * splatDepth)
		{
			throw new ArgumentException($"A {splatWidth}x{splatDepth} splat needs {splatWidth * splatDepth} cells but was handed {colors.Length}.");
		}

		float[] indices = new float[splatWidth * splatDepth * ChannelsPerTexel];
		float[] weights = new float[splatWidth * splatDepth * ChannelsPerTexel];

		for (int index = 0; index < colors.Length; index++)
		{
			TerrainSplatWeights splat = TerrainSplatWeights.Deserialize(colors[index]);
			int baseIndex = index * ChannelsPerTexel;

			indices[baseIndex + 0] = splat.Index0;
			indices[baseIndex + 1] = splat.Index1;
			indices[baseIndex + 2] = splat.Index2;
			indices[baseIndex + 3] = splat.Index3;

			weights[baseIndex + 0] = splat.Weight0;
			weights[baseIndex + 1] = splat.Weight1;
			weights[baseIndex + 2] = splat.Weight2;
			weights[baseIndex + 3] = splat.Weight3;
		}

		ExrImageCodec.EncodeRgbaFloat(SplatIndicesPath(directory, prefix), indices, splatWidth, splatDepth);
		ExrImageCodec.EncodeRgbaFloat(SplatWeightsPath(directory, prefix), weights, splatWidth, splatDepth);
	}

	public static string[]? ReadSplat(string directory, string prefix, int splatWidth, int splatDepth)
	{
		TerrainSplatWeights[,]? splatMap = ReadSplatMap(directory, prefix, splatWidth, splatDepth);
		if (splatMap == null) return null;

		string[] colors = new string[splatWidth * splatDepth];
		for (int z = 0; z < splatDepth; z++)
		{
			for (int x = 0; x < splatWidth; x++)
			{
				colors[z * splatWidth + x] = splatMap[x, z].Serialize();
			}
		}

		return colors;
	}

	public static TerrainSplatWeights[,]? ReadSplatMap(string directory, string prefix, int splatWidth, int splatDepth)
	{
		float[]? indices = TryReadSplatIndices(directory, prefix, out int indicesWidth, out int indicesHeight);
		float[]? weights = TryReadSplatWeights(directory, prefix, out int weightsWidth, out int weightsHeight);
		if (indices == null || weights == null) return null;

		var splatMap = new TerrainSplatWeights[splatWidth, splatDepth];
		int lastIndicesX = Math.Max(0, indicesWidth - 1);
		int lastIndicesZ = Math.Max(0, indicesHeight - 1);
		int lastWeightsX = Math.Max(0, weightsWidth - 1);
		int lastWeightsZ = Math.Max(0, weightsHeight - 1);
		int splatSpanX = Math.Max(1, splatWidth - 1);
		int splatSpanZ = Math.Max(1, splatDepth - 1);

		for (int z = 0; z < splatDepth; z++)
		{
			for (int x = 0; x < splatWidth; x++)
			{
				int indicesSourceX = indicesWidth == splatWidth ? x : Math.Clamp(x * lastIndicesX / splatSpanX, 0, lastIndicesX);
				int indicesSourceZ = indicesHeight == splatDepth ? z : Math.Clamp(z * lastIndicesZ / splatSpanZ, 0, lastIndicesZ);
				int weightsSourceX = weightsWidth == splatWidth ? x : Math.Clamp(x * lastWeightsX / splatSpanX, 0, lastWeightsX);
				int weightsSourceZ = weightsHeight == splatDepth ? z : Math.Clamp(z * lastWeightsZ / splatSpanZ, 0, lastWeightsZ);

				int indicesBase = (indicesSourceZ * indicesWidth + indicesSourceX) * ChannelsPerTexel;
				int weightsBase = (weightsSourceZ * weightsWidth + weightsSourceX) * ChannelsPerTexel;

				splatMap[x, z] = new TerrainSplatWeights
				{
					Index0 = (int)MathF.Round(indices[indicesBase + 0]),
					Index1 = (int)MathF.Round(indices[indicesBase + 1]),
					Index2 = (int)MathF.Round(indices[indicesBase + 2]),
					Index3 = (int)MathF.Round(indices[indicesBase + 3]),
					Weight0 = weights[weightsBase + 0],
					Weight1 = weights[weightsBase + 1],
					Weight2 = weights[weightsBase + 2],
					Weight3 = weights[weightsBase + 3]
				};
			}
		}

		return splatMap;
	}

	public static TerrainCell[,] CellsFromHeights(ReadOnlySpan<float> heights, int width, int height)
	{
		var cells = new TerrainCell[width, height];
		for (int z = 0; z < height; z++)
		{
			for (int x = 0; x < width; x++)
			{
				int baseIndex = (z * width + x) * ChannelsPerTexel;
				cells[x, z] = new TerrainCell(heights[baseIndex], heights[baseIndex + 1], heights[baseIndex + 2], heights[baseIndex + 3]);
			}
		}

		return cells;
	}

	public static void ApplyWater(ReadOnlySpan<float> water, int width, int height, TerrainCell[,] cells)
	{
		if (cells.GetLength(0) != width || cells.GetLength(1) != height)
		{
			throw new ArgumentException($"A {width}x{height} water image describes a grid of the same size, but the grid is {cells.GetLength(0)}x{cells.GetLength(1)}.");
		}

		for (int z = 0; z < height; z++)
		{
			for (int x = 0; x < width; x++)
			{
				int baseIndex = (z * width + x) * ChannelsPerTexel;
				cells[x, z].WaterMode = (WaterType)Math.Clamp((int)MathF.Round(water[baseIndex + 0]), 0, 2);
				cells[x, z].WaterProfileIndex = (byte)Math.Clamp((int)MathF.Round(water[baseIndex + 1]), 0, 255);
				cells[x, z].WaterHeight = water[baseIndex + 2];
			}
		}
	}

	public static void ApplyPathing(ReadOnlySpan<byte> pathing, int imageWidth, int imageHeight, int[,] pathingCodes, int targetWidth, int targetDepth)
	{
		for (int z = 0; z < targetDepth; z++)
		{
			int sourceZ = imageHeight == targetDepth ? z : Math.Clamp(z * imageHeight / targetDepth, 0, imageHeight - 1);
			for (int x = 0; x < targetWidth; x++)
			{
				int sourceX = imageWidth == targetWidth ? x : Math.Clamp(x * imageWidth / targetWidth, 0, imageWidth - 1);
				pathingCodes[x, z] = pathing[(sourceZ * imageWidth + sourceX) * PngImageCodec.BytesPerPixel];
			}
		}
	}

	private static float[]? TryReadExr(string path, out int width, out int height)
	{
		width = 0;
		height = 0;
		if (!File.Exists(path)) return null;

		return ExrImageCodec.DecodeRgbaFloat(path, out width, out height);
	}
}
