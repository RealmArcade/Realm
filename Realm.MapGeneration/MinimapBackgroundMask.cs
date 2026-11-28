using System;
using System.Collections.Generic;

namespace Realm.MapGeneration;

public static class MinimapBackgroundMask
{
	private const float ColourTolerance = 0.055f;

	public static bool[] BuildCells(MinimapImage image, int width, int depth)
	{
		bool[] pixels = BuildPixels(image);
		var cells = new bool[width * depth];
		int samplesPerCellX = Math.Max(1, image.Width / width);
		int samplesPerCellY = Math.Max(1, image.Height / depth);
		for (int cellZ = 0; cellZ < depth; cellZ++)
		{
			int startY = Math.Min(image.Height - 1, cellZ * image.Height / depth);
			int endY = Math.Max(startY + 1, Math.Min(image.Height, (cellZ + 1) * image.Height / depth));
			for (int cellX = 0; cellX < width; cellX++)
			{
				int startX = Math.Min(image.Width - 1, cellX * image.Width / width);
				int endX = Math.Max(startX + 1, Math.Min(image.Width, (cellX + 1) * image.Width / width));
				int background = 0;
				int total = 0;
				for (int y = startY; y < endY; y += samplesPerCellY)
				{
					int rowStart = y * image.Width;
					for (int x = startX; x < endX; x += samplesPerCellX)
					{
						if (pixels[rowStart + x])
						{
							background++;
						}

						total++;
					}
				}

				cells[cellZ * width + cellX] = total > 0 && background * 5 >= total * 3;
			}
		}

		return cells;
	}

	public static bool[] BuildPixels(MinimapImage image)
	{
		var backgroundColour = BorderColour(image);
		var pixels = new bool[image.Width * image.Height];
		var queue = new Queue<int>();
		for (int x = 0; x < image.Width; x++)
		{
			Enqueue(x);
			Enqueue((image.Height - 1) * image.Width + x);
		}

		for (int y = 0; y < image.Height; y++)
		{
			Enqueue(y * image.Width);
			Enqueue(y * image.Width + image.Width - 1);
		}

		while (queue.Count > 0)
		{
			int index = queue.Dequeue();
			int pixelX = index % image.Width;
			int pixelY = index / image.Width;
			Visit(pixelX - 1, pixelY);
			Visit(pixelX + 1, pixelY);
			Visit(pixelX, pixelY - 1);
			Visit(pixelX, pixelY + 1);
		}

		if (!IsFlatPlate(image, pixels))
		{
			Array.Clear(pixels);
		}

		return pixels;

		void Enqueue(int index)
		{
			if (backgroundColour == null || pixels[index] || !Matches(image, index, backgroundColour))
			{
				return;
			}

			pixels[index] = true;
			queue.Enqueue(index);
		}

		void Visit(int x, int y)
		{
			if (x < 0 || x >= image.Width || y < 0 || y >= image.Height) return;
			Enqueue(y * image.Width + x);
		}
	}

	private static bool IsFlatPlate(MinimapImage image, bool[] pixels)
	{
		int covered = 0;
		for (int index = 0; index < pixels.Length; index++)
		{
			if (pixels[index])
			{
				covered++;
			}
		}

		if (covered < pixels.Length / 10)
		{
			return false;
		}

		double roughness = 0;
		int samples = 0;
		for (int y = 1; y < image.Height - 1; y++)
		{
			for (int x = 1; x < image.Width - 1; x++)
			{
				int index = y * image.Width + x;
				if (!pixels[index])
				{
					continue;
				}

				float luminance = image.Red[index] * 0.2126f + image.Green[index] * 0.7152f + image.Blue[index] * 0.0722f;
				float neighbourAverage = 0f;
				for (int offsetY = -1; offsetY <= 1; offsetY++)
				{
					for (int offsetX = -1; offsetX <= 1; offsetX++)
					{
						int sample = (y + offsetY) * image.Width + x + offsetX;
						neighbourAverage += image.Red[sample] * 0.2126f + image.Green[sample] * 0.7152f + image.Blue[sample] * 0.0722f;
					}
				}

				roughness += Math.Abs(luminance - neighbourAverage / 9f);
				samples++;
			}
		}

		return samples > 0 && roughness / samples < 0.012;
	}

	public static float[]? BorderColour(MinimapImage image, float maximumSaturation = 0.30f)
	{
		var reds = new List<float>();
		var greens = new List<float>();
		var blues = new List<float>();
		for (int x = 0; x < image.Width; x++)
		{
			Collect(image, x, reds, greens, blues);
			Collect(image, (image.Height - 1) * image.Width + x, reds, greens, blues);
		}

		for (int y = 0; y < image.Height; y++)
		{
			Collect(image, y * image.Width, reds, greens, blues);
			Collect(image, y * image.Width + image.Width - 1, reds, greens, blues);
		}

		if (reds.Count == 0)
		{
			return null;
		}

		reds.Sort();
		greens.Sort();
		blues.Sort();
		float red = reds[reds.Count / 2];
		float green = greens[greens.Count / 2];
		float blue = blues[blues.Count / 2];
		float maximum = MathF.Max(red, MathF.Max(green, blue));
		float minimum = MathF.Min(red, MathF.Min(green, blue));
		float saturation = maximum <= 0.0001f ? 0f : (maximum - minimum) / maximum;
		if (saturation > maximumSaturation)
		{
			return null;
		}

		return new[] { red, green, blue };
	}

	private static void Collect(MinimapImage image, int index, List<float> reds, List<float> greens, List<float> blues)
	{
		reds.Add(image.Red[index]);
		greens.Add(image.Green[index]);
		blues.Add(image.Blue[index]);
	}

	private static bool Matches(MinimapImage image, int index, float[] colour)
	{
		return MathF.Abs(image.Red[index] - colour[0]) <= ColourTolerance &&
		       MathF.Abs(image.Green[index] - colour[1]) <= ColourTolerance &&
		       MathF.Abs(image.Blue[index] - colour[2]) <= ColourTolerance;
	}
}
