using System;

namespace Realm.MapGeneration;

public static class MinimapViewport
{
	private const float MapLikeSaturation = 0.12f;
	private const float MapLikeValue = 0.08f;
	private const float RowCoverageThreshold = 0.35f;
	private const float MinimumKeptFraction = 0.30f;

	public static MinimapImage Crop(MinimapImage image)
	{
		float[] saturation = new float[image.Width * image.Height];
		float[] value = new float[image.Width * image.Height];
		for (int index = 0; index < saturation.Length; index++)
		{
			float maximum = MathF.Max(image.Red[index], MathF.Max(image.Green[index], image.Blue[index]));
			float minimum = MathF.Min(image.Red[index], MathF.Min(image.Green[index], image.Blue[index]));
			value[index] = maximum;
			saturation[index] = maximum <= 0.0001f ? 0f : (maximum - minimum) / maximum;
		}

		var rowCoverage = new float[image.Height];
		var columnCoverage = new float[image.Width];
		for (int y = 0; y < image.Height; y++)
		{
			int rowStart = y * image.Width;
			int count = 0;
			for (int x = 0; x < image.Width; x++)
			{
				int index = rowStart + x;
				if (saturation[index] >= MapLikeSaturation && value[index] >= MapLikeValue)
				{
					count++;
					columnCoverage[x]++;
				}
			}

			rowCoverage[y] = count / (float)image.Width;
		}

		for (int x = 0; x < image.Width; x++)
		{
			columnCoverage[x] /= image.Height;
		}

		(int startRow, int endRow) = LargestSpan(rowCoverage);
		(int startColumn, int endColumn) = LargestSpan(columnCoverage);
		if (endRow - startRow < image.Height || endColumn - startColumn < image.Width)
		{
			float keptArea = (endRow - startRow + 1) * (float)(endColumn - startColumn + 1);
			if (keptArea < image.Width * image.Height * MinimumKeptFraction)
			{
				return image;
			}

			int paddingX = Math.Max(1, image.Width / 100);
			int paddingY = Math.Max(1, image.Height / 100);
			startColumn = Math.Max(0, startColumn - paddingX);
			endColumn = Math.Min(image.Width - 1, endColumn + paddingX);
			startRow = Math.Max(0, startRow - paddingY);
			endRow = Math.Min(image.Height - 1, endRow + paddingY);
		}

		if (startColumn == 0 && startRow == 0 && endColumn == image.Width - 1 && endRow == image.Height - 1)
		{
			return image;
		}

		return Create(image, startColumn, startRow, endColumn - startColumn + 1, endRow - startRow + 1);
	}

	private static (int Start, int End) LargestSpan(float[] coverage)
	{
		int bestStart = 0;
		int bestEnd = coverage.Length - 1;
		int bestLength = 0;
		int start = -1;
		for (int index = 0; index <= coverage.Length; index++)
		{
			bool inside = index < coverage.Length && coverage[index] >= RowCoverageThreshold;
			if (inside && start < 0)
			{
				start = index;
			}
			else if (!inside && start >= 0)
			{
				int length = index - start;
				if (length > bestLength)
				{
					bestLength = length;
					bestStart = start;
					bestEnd = index - 1;
				}

				start = -1;
			}
		}

		return bestLength == 0 ? (0, coverage.Length - 1) : (bestStart, bestEnd);
	}

	private static MinimapImage Create(MinimapImage source, int originX, int originY, int width, int height)
	{
		var cropped = new MinimapImage(width, height);
		for (int y = 0; y < height; y++)
		{
			int sourceRow = (originY + y) * source.Width + originX;
			int targetRow = y * width;
			for (int x = 0; x < width; x++)
			{
				cropped.Red[targetRow + x] = source.Red[sourceRow + x];
				cropped.Green[targetRow + x] = source.Green[sourceRow + x];
				cropped.Blue[targetRow + x] = source.Blue[sourceRow + x];
			}
		}

		return cropped;
	}
}
