using System;

namespace Realm.MapGeneration;

public readonly record struct MinimapCellFeatures(
	float Red,
	float Green,
	float Blue,
	float Hue,
	float Saturation,
	float Value,
	float LocalContrast,
	float TextureDetail)
{
	public static MinimapCellFeatures FromColour(float red, float green, float blue, float contrast, float detail)
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
