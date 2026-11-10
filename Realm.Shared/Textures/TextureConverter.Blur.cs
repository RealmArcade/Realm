using System.Globalization;
using System.Text.Json.Nodes;
using Imazen.WebP;
using Realm.Shared.Metadata;
using SkiaSharp;

namespace Realm.Shared.Textures;

/// <summary>the separable box blur the terrain height derivation uses.</summary>
public static partial class TextureConverter
	{
	private static float[,] ComputeSeparableBoxBlur(float[,] input, int w, int h, int radius, bool isDecal = false)
	{
		float[,] temp = new float[w, h];
		float[,] result = new float[w, h];
		int windowSize = 2 * radius + 1;
		float invWindow = 1.0f / windowSize;

		ComputeHorizontalBoxBlur(input, temp, w, h, radius, invWindow, isDecal);
		ComputeVerticalBoxBlur(temp, result, w, h, radius, invWindow, isDecal);

		return result;
	}

	private static void ComputeHorizontalBoxBlur(float[,] input, float[,] temp, int w, int h, int radius, float invWindow, bool isDecal)
	{
		for (int y = 0; y < h; y++)
		{
			float sum = 0.0f;
			for (int k = -radius; k <= radius; k++)
			{
				int px = isDecal ? Math.Clamp(k, 0, w - 1) : (k % w + w) % w;
				sum += input[px, y];
			}

			temp[0, y] = sum * invWindow;

			for (int x = 1; x < w; x++)
			{
				int removeX = isDecal ? Math.Clamp(x - 1 - radius, 0, w - 1) : ((x - 1 - radius) % w + w) % w;
				int addX = isDecal ? Math.Clamp(x + radius, 0, w - 1) : ((x + radius) % w + w) % w;
				sum += input[addX, y] - input[removeX, y];
				temp[x, y] = sum * invWindow;
			}
		}
	}

	private static void ComputeVerticalBoxBlur(float[,] temp, float[,] result, int w, int h, int radius, float invWindow, bool isDecal)
	{
		for (int x = 0; x < w; x++)
		{
			float sum = 0.0f;
			for (int k = -radius; k <= radius; k++)
			{
				int py = isDecal ? Math.Clamp(k, 0, h - 1) : (k % h + h) % h;
				sum += temp[x, py];
			}

			result[x, 0] = sum * invWindow;

			for (int y = 1; y < h; y++)
			{
				int removeY = isDecal ? Math.Clamp(y - 1 - radius, 0, h - 1) : ((y - 1 - radius) % h + h) % h;
				int addY = isDecal ? Math.Clamp(y + radius, 0, h - 1) : ((y + radius) % h + h) % h;
				sum += temp[x, addY] - temp[x, removeY];
				result[x, y] = sum * invWindow;
			}
		}
	}
}
