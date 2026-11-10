using Realm.Shared.Maps;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using DotRecast.Detour;
using Godot;
using Godot.Collections;
using Realm.Client.Core;
using Realm.Client.Services;
using Realm.Client.UI;
using Realm.Ecs;
using Realm.Ecs.Components.Terrain;
using Realm.Ecs.Services;
using Realm.Shared.Services;
using Realm.Shared.Textures;
using Array = Godot.Collections.Array;

namespace Realm.Client;
public partial class RuntimeTerrain
{
	private (int tex0, int tex1, int tex2, int tex3) GetQuadDominantTextures(ReadOnlySpan<TerrainSplatWeights> splats)
	{
		Span<int> indices = stackalloc int[16];
		Span<float> weights = stackalloc float[16];
		int count = AccumulateSplatWeights(splats, indices, weights);

		if (count == 0)
		{
			int defaultTex = splats.Length > 0 ? splats[0].Index0 : 0;
			return (defaultTex, defaultTex, defaultTex, defaultTex);
		}

		SortWeightsDescending(indices, weights, count);

		int res0 = indices[0];
		int res1 = count > 1 ? indices[1] : res0;
		int res2 = count > 2 ? indices[2] : res0;
		int res3 = count > 3 ? indices[3] : res0;

		return (res0, res1, res2, res3);
	}

	private int AccumulateSplatWeights(ReadOnlySpan<TerrainSplatWeights> splats, Span<int> indices, Span<float> weights)
	{
		int count = 0;

		for (int sIdx = 0; sIdx < splats.Length; sIdx++)
		{
			TerrainSplatWeights s = splats[sIdx];
			for (int k = 0; k < 4; k++)
			{
				int idx = k switch { 0 => s.Index0, 1 => s.Index1, 2 => s.Index2, _ => s.Index3 };
				float w = k switch { 0 => s.Weight0, 1 => s.Weight1, 2 => s.Weight2, _ => s.Weight3 };
				if (w <= 0.0001f) continue;

				count = AddOrUpdateWeight(idx, w, indices, weights, count);
			}
		}

		return count;
	}

	private int AddOrUpdateWeight(int idx, float w, Span<int> indices, Span<float> weights, int count)
	{
		for (int i = 0; i < count; i++)
		{
			if (indices[i] == idx)
			{
				weights[i] += w;
				return count;
			}
		}

		if (count < 16)
		{
			indices[count] = idx;
			weights[count] = w;
			return count + 1;
		}

		return count;
	}

	private void SortWeightsDescending(Span<int> indices, Span<float> weights, int count)
	{
		for (int i = 0; i < count - 1; i++)
		{
			for (int j = i + 1; j < count; j++)
			{
				if (weights[j] > weights[i])
				{
					float tempW = weights[i];
					weights[i] = weights[j];
					weights[j] = tempW;

					int tempIdx = indices[i];
					indices[i] = indices[j];
					indices[j] = tempIdx;
				}
			}
		}
	}

	private (int tex0, int tex1, int tex2, int tex3) GetQuadDominantTextures(TerrainSplatWeights s0, TerrainSplatWeights s1, TerrainSplatWeights s2, TerrainSplatWeights s3)
	{
		Span<TerrainSplatWeights> splats = stackalloc TerrainSplatWeights[4] { s0, s1, s2, s3 };
		return GetQuadDominantTextures(splats);
	}

	private (float w0, float w1, float w2, float w3) GetSplatWeightsForQuad(TerrainSplatWeights s, int tex0, int tex1, int tex2, int tex3)
	{
		float w0 = GetWeightForTexture(s, tex0);
		float w1 = (tex1 != tex0) ? GetWeightForTexture(s, tex1) : 0.0f;
		float w2 = (tex2 != tex0 && tex2 != tex1) ? GetWeightForTexture(s, tex2) : 0.0f;
		float w3 = (tex3 != tex0 && tex3 != tex1 && tex3 != tex2) ? GetWeightForTexture(s, tex3) : 0.0f;

		return NormalizeSplatWeights(w0, w1, w2, w3);
	}

	private float GetWeightForTexture(in TerrainSplatWeights splat, int targetTexIndex)
	{
		float w = 0.0f;
		if (splat.Index0 == targetTexIndex) w += splat.Weight0;
		if (splat.Index1 == targetTexIndex) w += splat.Weight1;
		if (splat.Index2 == targetTexIndex) w += splat.Weight2;
		if (splat.Index3 == targetTexIndex) w += splat.Weight3;
		return w;
	}

	private (float w0, float w1, float w2, float w3) NormalizeSplatWeights(float w0, float w1, float w2, float w3)
	{
		float sumW = w0 + w1 + w2 + w3;
		if (sumW > 0.0001f)
		{
			float invSum = 1.0f / sumW;
			return (w0 * invSum, w1 * invSum, w2 * invSum, w3 * invSum);
		}

		return (1.0f, 0.0f, 0.0f, 0.0f);
	}

	private TerrainSplatWeights NormalizeAndCreateSplatWeights(int tex0, int tex1, int tex2, int tex3, float avg0, float avg1, float avg2, float avg3)
	{
		float sum = avg0 + avg1 + avg2 + avg3;
		if (sum > 0.0001f)
		{
			float invSum = 1.0f / sum;
			avg0 *= invSum;
			avg1 *= invSum;
			avg2 *= invSum;
			avg3 *= invSum;
		}
		else
		{
			avg0 = 1.0f;
			avg1 = 0f;
			avg2 = 0f;
			avg3 = 0f;
		}

		return new TerrainSplatWeights
		{
			Index0 = tex0,
			Index1 = tex1,
			Index2 = tex2,
			Index3 = tex3,
			Weight0 = avg0,
			Weight1 = avg1,
			Weight2 = avg2,
			Weight3 = avg3
		};
	}

	private TerrainSplatWeights BlendSplatWeights(TerrainSplatWeights s0, TerrainSplatWeights s1, TerrainSplatWeights s2, TerrainSplatWeights s3)
	{
		var (tex0, tex1, tex2, tex3) = GetQuadDominantTextures(s0, s1, s2, s3);
		var (w00, w01, w02, w03) = GetSplatWeightsForQuad(s0, tex0, tex1, tex2, tex3);
		var (w10, w11, w12, w13) = GetSplatWeightsForQuad(s1, tex0, tex1, tex2, tex3);
		var (w20, w21, w22, w23) = GetSplatWeightsForQuad(s2, tex0, tex1, tex2, tex3);
		var (w30, w31, w32, w33) = GetSplatWeightsForQuad(s3, tex0, tex1, tex2, tex3);

		float avg0 = (w00 + w10 + w20 + w30) * 0.25f;
		float avg1 = (w01 + w11 + w21 + w31) * 0.25f;
		float avg2 = (w02 + w12 + w22 + w32) * 0.25f;
		float avg3 = (w03 + w13 + w23 + w33) * 0.25f;

		return NormalizeAndCreateSplatWeights(tex0, tex1, tex2, tex3, avg0, avg1, avg2, avg3);
	}

	private TerrainSplatWeights BlendSplatWeights(TerrainSplatWeights s0, TerrainSplatWeights s1, TerrainSplatWeights s2)
	{
		var (tex0, tex1, tex2, tex3) = GetQuadDominantTextures(s0, s1, s2, s0);
		var (w00, w01, w02, w03) = GetSplatWeightsForQuad(s0, tex0, tex1, tex2, tex3);
		var (w10, w11, w12, w13) = GetSplatWeightsForQuad(s1, tex0, tex1, tex2, tex3);
		var (w20, w21, w22, w23) = GetSplatWeightsForQuad(s2, tex0, tex1, tex2, tex3);

		float avg0 = (w00 + w10 + w20) / 3.0f;
		float avg1 = (w01 + w11 + w21) / 3.0f;
		float avg2 = (w02 + w12 + w22) / 3.0f;
		float avg3 = (w03 + w13 + w23) / 3.0f;

		return NormalizeAndCreateSplatWeights(tex0, tex1, tex2, tex3, avg0, avg1, avg2, avg3);
	}

	public static float Smoothstep(float edge0, float edge1, float x)
	{
		float t = Mathf.Clamp((x - edge0) / (edge1 - edge0), 0.0f, 1.0f);
		return t * t * (3.0f - 2.0f * t);
	}

	public static float GetCliffFactor(Vector3 normal)
	{
		float ny = Mathf.Clamp(normal.Y, 0.0f, 1.0f);
		return 1.0f - Smoothstep(0.70f, 0.90f, ny);
	}

	public static float GetVertexCliffWeight(int gx, int gz, TerrainCell[,] cells, int w, int d, float quadSize)
	{
		if (cells == null || w <= 0 || d <= 0) return 0f;

		float maxDelta = CalculateMaxHeightDelta(gx, gz, cells, w, d);
		var (minTier, maxTier, cellCount) = CalculateTierBounds(gx, gz, cells, w, d);

		float tierCliffDeltaThreshold = TerrainCell.TIER_HEIGHT * 0.70f;
		bool hasTierDifference = cellCount > 1 && (maxTier - minTier) >= 1;
		bool hasStepHeightDelta = maxDelta >= tierCliffDeltaThreshold;

		if (!hasTierDifference && !hasStepHeightDelta) return 0.0f;

		float deltaCliff = Smoothstep(TerrainCell.TIER_HEIGHT * 0.50f, TerrainCell.TIER_HEIGHT * 0.85f, maxDelta);
		if (hasTierDifference)
		{
			deltaCliff = Math.Max(deltaCliff, 1.0f);
		}

		return Math.Clamp(deltaCliff, 0.0f, 1.0f);
	}

	private static float CalculateMaxHeightDelta(int gx, int gz, TerrainCell[,] cells, int w, int d)
	{
		float h = GetGridNodeHeight(gx, gz, cells, w, d);
		float maxDelta = 0f;

		if (gx > 0) maxDelta = Math.Max(maxDelta, Math.Abs(h - GetGridNodeHeight(gx - 1, gz, cells, w, d)));
		if (gx < w) maxDelta = Math.Max(maxDelta, Math.Abs(h - GetGridNodeHeight(gx + 1, gz, cells, w, d)));
		if (gz > 0) maxDelta = Math.Max(maxDelta, Math.Abs(h - GetGridNodeHeight(gx, gz - 1, cells, w, d)));
		if (gz < d) maxDelta = Math.Max(maxDelta, Math.Abs(h - GetGridNodeHeight(gx, gz + 1, cells, w, d)));

		return maxDelta;
	}

}
