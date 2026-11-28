using System;

namespace Realm.MapGeneration;

/// <summary>
/// Peels a thin decorative frame off the outer edge of a minimap image. Frames are a flat, desaturated
/// colour for the full span of the line, so a coastline or a lake can never be mistaken for one.
/// </summary>
public static class MinimapFrame
{
	private const float ColourTolerance = 0.055f;
	private const float LineMatchFraction = 0.92f;
	private const float MaximumPeelFraction = 0.08f;

	// Frames are neutral greys; a tinted border is a shoreline seen edge-on and is left to the map.
	private const float MaximumFrameSaturation = 0.20f;

	public static MinimapImage Trim(MinimapImage image)
	{
		float[]? borderColour = MinimapBackgroundMask.BorderColour(image, MaximumFrameSaturation);
		if (borderColour == null)
		{
			return image;
		}

		int maximumX = Math.Max(1, (int)(image.Width * MaximumPeelFraction));
		int maximumY = Math.Max(1, (int)(image.Height * MaximumPeelFraction));

		int left = 0;
		while (left < maximumX && ColumnMatches(image, left, borderColour))
		{
			left++;
		}

		int right = image.Width - 1;
		while (right > image.Width - 1 - maximumX && ColumnMatches(image, right, borderColour))
		{
			right--;
		}

		int top = 0;
		while (top < maximumY && RowMatches(image, top, borderColour))
		{
			top++;
		}

		int bottom = image.Height - 1;
		while (bottom > image.Height - 1 - maximumY && RowMatches(image, bottom, borderColour))
		{
			bottom--;
		}

		if (left == 0 && top == 0 && right == image.Width - 1 && bottom == image.Height - 1)
		{
			return image;
		}

		int width = right - left + 1;
		int height = bottom - top + 1;
		if (width * 2 < image.Width || height * 2 < image.Height)
		{
			return image;
		}

		return MinimapViewport.Create(image, left, top, width, height);
	}

	private static bool RowMatches(MinimapImage image, int y, float[] colour)
	{
		int matched = 0;
		int rowStart = y * image.Width;
		for (int x = 0; x < image.Width; x++)
		{
			if (Matches(image, rowStart + x, colour))
			{
				matched++;
			}
		}

		return matched >= image.Width * LineMatchFraction;
	}

	private static bool ColumnMatches(MinimapImage image, int x, float[] colour)
	{
		int matched = 0;
		for (int y = 0; y < image.Height; y++)
		{
			if (Matches(image, y * image.Width + x, colour))
			{
				matched++;
			}
		}

		return matched >= image.Height * LineMatchFraction;
	}

	private static bool Matches(MinimapImage image, int index, float[] colour)
	{
		return MathF.Abs(image.Red[index] - colour[0]) <= ColourTolerance &&
		       MathF.Abs(image.Green[index] - colour[1]) <= ColourTolerance &&
		       MathF.Abs(image.Blue[index] - colour[2]) <= ColourTolerance;
	}
}
