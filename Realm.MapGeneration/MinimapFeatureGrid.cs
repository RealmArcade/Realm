using System;

namespace Realm.MapGeneration;

public class MinimapFeatureGrid
{
	public int Width { get; }

	public int Depth { get; }

	public MinimapCellFeatures[] Cells { get; }

	public MinimapFeatureGrid(MinimapImage image, int width, int depth)
	{
		Width = width;
		Depth = depth;
		Cells = new MinimapCellFeatures[width * depth];

		var luminance = new float[width * depth];
		var reds = new float[width * depth];
		var greens = new float[width * depth];
		var blues = new float[width * depth];
		var details = new float[width * depth];

		for (int z = 0; z < depth; z++)
		{
			float minV = z / (float)depth;
			float maxV = (z + 1) / (float)depth;
			for (int x = 0; x < width; x++)
			{
				float minU = x / (float)width;
				float maxU = (x + 1) / (float)width;
				image.SampleBox(minU, minV, maxU, maxV, out float red, out float green, out float blue);
				int index = z * width + x;
				reds[index] = red;
				greens[index] = green;
				blues[index] = blue;
				luminance[index] = red * 0.2126f + green * 0.7152f + blue * 0.0722f;
				details[index] = MeasureDetail(image, minU, minV, maxU, maxV);
			}
		}

		for (int z = 0; z < depth; z++)
		{
			for (int x = 0; x < width; x++)
			{
				int index = z * width + x;
				float mean = 0f;
				int count = 0;
				for (int offsetZ = -1; offsetZ <= 1; offsetZ++)
				{
					int sampleZ = z + offsetZ;
					if (sampleZ < 0 || sampleZ >= depth) continue;
					for (int offsetX = -1; offsetX <= 1; offsetX++)
					{
						int sampleX = x + offsetX;
						if (sampleX < 0 || sampleX >= width) continue;
						mean += luminance[sampleZ * width + sampleX];
						count++;
					}
				}

				mean /= Math.Max(1, count);
				float squared = 0f;
				for (int offsetZ = -1; offsetZ <= 1; offsetZ++)
				{
					int sampleZ = z + offsetZ;
					if (sampleZ < 0 || sampleZ >= depth) continue;
					for (int offsetX = -1; offsetX <= 1; offsetX++)
					{
						int sampleX = x + offsetX;
						if (sampleX < 0 || sampleX >= width) continue;
						float delta = luminance[sampleZ * width + sampleX] - mean;
						squared += delta * delta;
					}
				}

				float contrast = MathF.Sqrt(squared / Math.Max(1, count));
				Cells[index] = Create(reds[index], greens[index], blues[index], contrast, details[index]);
			}
		}
	}

	private static float MeasureDetail(MinimapImage image, float minimumU, float minimumV, float maximumU, float maximumV)
	{
		int startX = Math.Clamp((int)(minimumU * image.Width), 0, image.Width - 1);
		int endX = Math.Clamp((int)MathF.Ceiling(maximumU * image.Width), startX + 1, image.Width);
		int startY = Math.Clamp((int)(minimumV * image.Height), 0, image.Height - 1);
		int endY = Math.Clamp((int)MathF.Ceiling(maximumV * image.Height), startY + 1, image.Height);

		float sum = 0f;
		int count = 0;
		for (int y = startY; y < endY; y++)
		{
			int rowStart = y * image.Width;
			for (int x = startX; x < endX; x++)
			{
				int index = rowStart + x;
				sum += image.Red[index] * 0.2126f + image.Green[index] * 0.7152f + image.Blue[index] * 0.0722f;
				count++;
			}
		}

		if (count <= 1)
		{
			return 0f;
		}

		float mean = sum / count;
		float squared = 0f;
		for (int y = startY; y < endY; y++)
		{
			int rowStart = y * image.Width;
			for (int x = startX; x < endX; x++)
			{
				int index = rowStart + x;
				float luminance = image.Red[index] * 0.2126f + image.Green[index] * 0.7152f + image.Blue[index] * 0.0722f;
				float delta = luminance - mean;
				squared += delta * delta;
			}
		}

		return MathF.Sqrt(squared / count);
	}

	private static MinimapCellFeatures Create(float red, float green, float blue, float contrast, float detail)
	{
		float maximum = MathF.Max(red, MathF.Max(green, blue));
		float minimum = MathF.Min(red, MathF.Min(green, blue));
		float delta = maximum - minimum;
		float hue;
		if (delta <= 0.0001f)
		{
			hue = 0f;
		}
		else if (maximum == red)
		{
			hue = 60f * (((green - blue) / delta) % 6f);
		}
		else if (maximum == green)
		{
			hue = 60f * ((blue - red) / delta + 2f);
		}
		else
		{
			hue = 60f * ((red - green) / delta + 4f);
		}

		if (hue < 0f)
		{
			hue += 360f;
		}

		float saturation = maximum <= 0.0001f ? 0f : delta / maximum;
		return new MinimapCellFeatures(red, green, blue, hue, saturation, maximum, contrast, detail);
	}
}
