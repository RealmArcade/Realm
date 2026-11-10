using System.Numerics;
using System.Text.Json.Nodes;
using Assimp;
using Realm.Shared.Metadata;
using Realm.Shared.ModelOptimization;
using Realm.Shared.Textures;
using SkiaSharp;

namespace Realm.Shared;

/// <summary>scoring texels against the chroma key.</summary>
public static partial class GlbPlayerColorProcessor
	{
	private static void ScoreSingleTexel(
		SKColor pixel, int idx,
		float targetLightness, float targetA, float targetOklabB,
		bool[] seedMask, bool[] floodFillMask)
	{
		const float seedLightnessDeltaThreshold = 0.18f;
		const float seedChromaticityDistanceSquaredThreshold = 0.01f;
		const float floodLightnessDeltaThreshold = 0.25f;
		const float floodChromaticityDistanceSquaredThreshold = 0.0324f;
		const float minCandidateChromaSquared = 0.01f;

		float r = pixel.Red / 255f;
		float g = pixel.Green / 255f;
		float bVal = pixel.Blue / 255f;

		var (pL, pA, pB) = GlbChromaKeyDetector.ConvertRgbToOklab(r, g, bVal);
		float deltaLightness = MathF.Abs(pL - targetLightness);
		float deltaA = pA - targetA;
		float deltaB = pB - targetOklabB;
		float chromaticityDistanceSquared = deltaA * deltaA + deltaB * deltaB;
		float pixelChromaSquared = pA * pA + pB * pB;

		bool isSeed = (deltaLightness <= seedLightnessDeltaThreshold) &&
		              (chromaticityDistanceSquared <= seedChromaticityDistanceSquaredThreshold);

		bool isFloodCandidate = (deltaLightness <= floodLightnessDeltaThreshold) &&
		                        (chromaticityDistanceSquared <= floodChromaticityDistanceSquaredThreshold) &&
		                        (pixelChromaSquared >= minCandidateChromaSquared);

		if (isSeed)
		{
			seedMask[idx] = true;
			floodFillMask[idx] = true;
		}
		else if (isFloodCandidate)
		{
			floodFillMask[idx] = true;
		}
	}

	private static void ScoreTexels(
		SKBitmap albedoImg,
		float targetLightness, float targetA, float targetOklabB,
		bool[] seedMask, bool[] floodFillMask)
	{
		for (int y = 0; y < albedoImg.Height; y++)
		{
			for (int x = 0; x < albedoImg.Width; x++)
			{
				int idx = y * albedoImg.Width + x;
				ScoreSingleTexel(albedoImg.GetPixel(x, y), idx, targetLightness, targetA, targetOklabB, seedMask, floodFillMask);
			}
		}
	}
}
