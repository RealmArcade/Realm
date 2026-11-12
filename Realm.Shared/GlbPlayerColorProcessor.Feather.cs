using System.Numerics;
using System.Text.Json.Nodes;
using Assimp;
using Realm.Shared.Metadata;
using Realm.Shared.ModelOptimization;
using Realm.Shared.Textures;
using SkiaSharp;

namespace Realm.Shared;

/// <summary>the dilation and feathering kernels.</summary>
public static partial class GlbPlayerColorProcessor
	{
	private static float[] FeatherInteriorMaskEdges(float[] mask, bool[] isConfirmedPolygonTexel, int texW, int texH)
	{
		var feathered = new float[mask.Length];
		Array.Copy(mask, feathered, mask.Length);

		for (int y = 0; y < texH; y++)
		{
			for (int x = 0; x < texW; x++)
			{
				int idx = y * texW + x;
				if (!isConfirmedPolygonTexel[idx]) continue;

				CalculateFeatheredValue(x, y, idx, mask, feathered, isConfirmedPolygonTexel, texW, texH);
			}
		}

		return feathered;
	}

	private static void ApplyDilationKernel(
		int x, int y, float sourceValue, float[] dilated, bool[] isNonPlayerFaceTexel,
		int texW, int texH, int radius)
	{
		for (int dy = -radius; dy <= radius; dy++)
		{
			for (int dx = -radius; dx <= radius; dx++)
			{
				if (dx * dx + dy * dy > radius * radius) continue;

				int nx = x + dx;
				int ny = y + dy;
				if (nx < 0 || nx >= texW || ny < 0 || ny >= texH) continue;

				int nIdx = ny * texW + nx;
				if (isNonPlayerFaceTexel[nIdx]) continue;

				dilated[nIdx] = Math.Max(dilated[nIdx], sourceValue);
			}
		}
	}

	private static float[] DilateFloat(float[] mask, bool[] isNonPlayerFaceTexel, int texW, int texH, int radius)
	{
		if (radius <= 0) return mask;

		var dilated = new float[mask.Length];
		Array.Copy(mask, dilated, mask.Length);

		for (int y = 0; y < texH; y++)
		{
			for (int x = 0; x < texW; x++)
			{
				float sourceValue = mask[y * texW + x];
				if (sourceValue <= 0f) continue;

				ApplyDilationKernel(x, y, sourceValue, dilated, isNonPlayerFaceTexel, texW, texH, radius);
			}
		}

		return dilated;
	}
}
