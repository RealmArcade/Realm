using System.Numerics;
using System.Text.Json.Nodes;
using Assimp;
using Realm.Shared.Metadata;
using Realm.Shared.ModelOptimization;
using Realm.Shared.Textures;
using SkiaSharp;

namespace Realm.Shared;

/// <summary>the surface aware pass and the feathering it applies.</summary>
public static partial class GlbPlayerColorProcessor
	{
	private static HashSet<int> ProcessFacesSurfaceAware(
		List<(Vector3 Pos0, Vector3 Pos1, Vector3 Pos2)> facePositions,
		List<(Vector2 UV0, Vector2 UV1, Vector2 UV2)> faceUvCoords,
		bool[] seedMask,
		bool[] floodFillMask,
		int texW,
		int texH,
		float cosCreaseThreshold,
		int minClusterFaces)
	{
		int faceCount = facePositions.Count;
		if (faceCount == 0) return new HashSet<int>();

		BuildSmoothAdjacency(facePositions, faceCount, cosCreaseThreshold, out var smoothAdjacency);

		var isSeedFace = new bool[faceCount];
		var isFloodCandidateFace = new bool[faceCount];

		for (int faceIdx = 0; faceIdx < faceCount; faceIdx++)
		{
			var (uv0, uv1, uv2) = faceUvCoords[faceIdx];
			EvaluateFaceSeedStatus(faceIdx, uv0, uv1, uv2, seedMask, floodFillMask, texW, texH, isSeedFace, isFloodCandidateFace);
		}

		var validatedSeedFaces = FindSeedClusters(faceCount, isSeedFace, smoothAdjacency);
		var visited = RunFloodFill(faceCount, validatedSeedFaces, isFloodCandidateFace, smoothAdjacency);
		return ExtractFloodComponents(faceCount, visited, smoothAdjacency, minClusterFaces);
	}

	private static void ProcessFeatherNeighbor(
		int dx, int dy, int x, int y, int texW, int texH,
		float[] mask, bool[] isConfirmedPolygonTexel,
		ref int totalNeighborsInside, ref float sumMaskInside,
		ref bool hasInteriorUnmaskedNeighbor, ref bool hasInteriorMaskedNeighbor)
	{
		if (dx == 0 && dy == 0) return;
		int nx = x + dx;
		int ny = y + dy;
		if (nx < 0 || nx >= texW || ny < 0 || ny >= texH) return;

		int nIdx = ny * texW + nx;
		if (isConfirmedPolygonTexel[nIdx])
		{
			totalNeighborsInside++;
			sumMaskInside += mask[nIdx];
			if (mask[nIdx] < 0.5f) hasInteriorUnmaskedNeighbor = true;
			if (mask[nIdx] >= 0.5f) hasInteriorMaskedNeighbor = true;
		}
	}

	private static void CalculateFeatheredValue(
		int x, int y, int idx, float[] mask, float[] feathered,
		bool[] isConfirmedPolygonTexel, int texW, int texH)
	{
		int totalNeighborsInside = 0;
		float sumMaskInside = 0f;
		bool hasInteriorUnmaskedNeighbor = false;
		bool hasInteriorMaskedNeighbor = false;

		for (int dy = -1; dy <= 1; dy++)
		{
			for (int dx = -1; dx <= 1; dx++)
			{
				ProcessFeatherNeighbor(dx, dy, x, y, texW, texH, mask, isConfirmedPolygonTexel, ref totalNeighborsInside, ref sumMaskInside, ref hasInteriorUnmaskedNeighbor, ref hasInteriorMaskedNeighbor);
			}
		}

		if (mask[idx] >= 0.5f && hasInteriorUnmaskedNeighbor && totalNeighborsInside > 0)
		{
			float ratio = (sumMaskInside + 1.0f) / (totalNeighborsInside + 1.0f);
			feathered[idx] = Math.Clamp(0.5f + ratio * 0.5f, 0.4f, 1.0f);
		}
		else if (mask[idx] < 0.5f && hasInteriorMaskedNeighbor && totalNeighborsInside > 0)
		{
			float ratio = sumMaskInside / (float)totalNeighborsInside;
			if (ratio > 0.15f)
			{
				feathered[idx] = Math.Clamp(ratio * 0.5f, 0.0f, 0.45f);
			}
		}
	}
}
