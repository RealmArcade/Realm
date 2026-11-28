using System;

namespace Realm.MapGeneration;

public class MinimapTexture
{
	public int Width { get; }

	public int Height { get; }

	public float[] Red { get; }

	public float[] Green { get; }

	public float[] Blue { get; }

	public float AverageRed { get; }

	public float AverageGreen { get; }

	public float AverageBlue { get; }

	public MinimapTexture(int width, int height, float[] red, float[] green, float[] blue)
	{
		Width = width;
		Height = height;
		Red = red;
		Green = green;
		Blue = blue;

		double sumRed = 0;
		double sumGreen = 0;
		double sumBlue = 0;
		for (int index = 0; index < red.Length; index++)
		{
			sumRed += red[index];
			sumGreen += green[index];
			sumBlue += blue[index];
		}

		float denominator = Math.Max(1, red.Length);
		AverageRed = (float)(sumRed / denominator);
		AverageGreen = (float)(sumGreen / denominator);
		AverageBlue = (float)(sumBlue / denominator);
	}

	public static MinimapTexture Solid(float red, float green, float blue)
	{
		return new MinimapTexture(1, 1, new[] { red }, new[] { green }, new[] { blue });
	}

	public void SampleTiled(float u, float v, out float red, out float green, out float blue)
	{
		float wrappedU = u - MathF.Floor(u);
		float wrappedV = v - MathF.Floor(v);
		int x = Math.Clamp((int)(wrappedU * Width), 0, Width - 1);
		int y = Math.Clamp((int)(wrappedV * Height), 0, Height - 1);
		int index = y * Width + x;
		red = Red[index];
		green = Green[index];
		blue = Blue[index];
	}
}
