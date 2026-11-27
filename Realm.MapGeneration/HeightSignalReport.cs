namespace Realm.MapGeneration;

public readonly record struct HeightSignalReport(
	float CellCount,
	float Luminance,
	float Saturation,
	float LocalContrast,
	float TextureDetail,
	float HorizonBrightness,
	float GreenOverRed);

public static class MapHeightSignal
{
	public static HeightSignalReport Analyse(MapFolderTerrain terrain, MinimapImage image)
	{
		var grid = new MinimapFeatureGrid(image, terrain.Width, terrain.Depth);
		int width = terrain.Width;
		int depth = terrain.Depth;
		int cellCount = width * depth;

		var heights = new float[cellCount];
		var luminance = new float[cellCount];
		var saturation = new float[cellCount];
		var contrast = new float[cellCount];
		var detail = new float[cellCount];
		var greenOverRed = new float[cellCount];
		var landMask = new bool[cellCount];
		int landCount = 0;
		for (int index = 0; index < cellCount; index++)
		{
			heights[index] = index < terrain.CellHeights.Length ? terrain.CellHeights[index] : 0f;
			luminance[index] = grid.Cells[index].Value;
			saturation[index] = grid.Cells[index].Saturation;
			contrast[index] = grid.Cells[index].LocalContrast;
			detail[index] = grid.Cells[index].TextureDetail;
			greenOverRed[index] = grid.Cells[index].Green - grid.Cells[index].Red;
			landMask[index] = !(index < terrain.WaterModes.Length && terrain.WaterModes[index] > 0);
			if (landMask[index])
			{
				landCount++;
			}
		}

		var horizon = new float[cellCount];
		for (int z = 0; z < depth; z++)
		{
			for (int x = 0; x < width; x++)
			{
				horizon[z * width + x] = luminance[z * width + x] - NeighbourhoodMean(luminance, width, depth, x, z, 6);
			}
		}

		return new HeightSignalReport(
			landCount,
			Correlate(heights, luminance, landMask),
			Correlate(heights, saturation, landMask),
			Correlate(heights, contrast, landMask),
			Correlate(heights, detail, landMask),
			Correlate(heights, horizon, landMask),
			Correlate(heights, greenOverRed, landMask));
	}

	private static float NeighbourhoodMean(float[] values, int width, int depth, int x, int z, int radius)
	{
		double sum = 0;
		int count = 0;
		for (int offsetZ = -radius; offsetZ <= radius; offsetZ++)
		{
			int sampleZ = z + offsetZ;
			if (sampleZ < 0 || sampleZ >= depth) continue;
			for (int offsetX = -radius; offsetX <= radius; offsetX++)
			{
				int sampleX = x + offsetX;
				if (sampleX < 0 || sampleX >= width) continue;
				sum += values[sampleZ * width + sampleX];
				count++;
			}
		}

		return count > 0 ? (float)(sum / count) : 0f;
	}

	private static float Correlate(float[] heights, float[] feature, bool[] mask)
	{
		double sumHeights = 0;
		double sumFeature = 0;
		int count = 0;
		for (int index = 0; index < heights.Length; index++)
		{
			if (!mask[index]) continue;
			sumHeights += heights[index];
			sumFeature += feature[index];
			count++;
		}

		if (count < 8)
		{
			return 0f;
		}

		double meanHeights = sumHeights / count;
		double meanFeature = sumFeature / count;
		double covariance = 0;
		double varianceHeights = 0;
		double varianceFeature = 0;
		for (int index = 0; index < heights.Length; index++)
		{
			if (!mask[index]) continue;
			double deltaHeights = heights[index] - meanHeights;
			double deltaFeature = feature[index] - meanFeature;
			covariance += deltaHeights * deltaFeature;
			varianceHeights += deltaHeights * deltaHeights;
			varianceFeature += deltaFeature * deltaFeature;
		}

		double denominator = System.Math.Sqrt(varianceHeights * varianceFeature);
		return denominator <= 0.000001 ? 0f : (float)(covariance / denominator);
	}
}
