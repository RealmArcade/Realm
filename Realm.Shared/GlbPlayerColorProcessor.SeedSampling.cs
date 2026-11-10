using System.Numerics;
using System.Text.Json.Nodes;
using Assimp;
using Realm.Shared.Metadata;
using Realm.Shared.ModelOptimization;
using Realm.Shared.Textures;
using SkiaSharp;

namespace Realm.Shared;

/// <summary>sampling triangles and finding the seed clusters.</summary>
public static partial class GlbPlayerColorProcessor
	{
	private static (int SeedHits, int FloodHits, int TotalSampled) SampleTriangleMasks(Vector2 uv0, Vector2 uv1, Vector2 uv2, bool[] seedMask, bool[] floodFillMask, int texW, int texH)
	{
		var (minX, maxX, minY, maxY) = GetTriangleBounds(uv0, uv1, uv2, texW, texH);

		int seedHits = 0;
		int floodHits = 0;
		int totalSampled = 0;

		for (int py = minY; py <= maxY; py++)
		{
			for (int px = minX; px <= maxX; px++)
			{
				var p = new Vector2(px / (float)texW, py / (float)texH);
				if (!PointInTriangle(p, uv0, uv1, uv2)) continue;

				totalSampled++;
				int idx = py * texW + px;
				if (seedMask[idx]) seedHits++;
				if (floodFillMask[idx]) floodHits++;
			}
		}

		return (seedHits, floodHits, totalSampled);
	}

	private static void EvaluateFaceSeedStatus(
		int faceIdx, Vector2 uv0, Vector2 uv1, Vector2 uv2,
		bool[] seedMask, bool[] floodFillMask, int texW, int texH,
		bool[] isSeedFace, bool[] isFloodCandidateFace)
	{
		var (seedHits, floodHits, totalSampled) = SampleTriangleMasks(uv0, uv1, uv2, seedMask, floodFillMask, texW, texH);

		if (totalSampled > 0)
		{
			float seedRatio = (float)seedHits / totalSampled;
			float floodRatio = (float)floodHits / totalSampled;

			isSeedFace[faceIdx] = (seedHits >= 3 && seedRatio >= 0.25f) ||
			                      (seedHits >= 2 && totalSampled <= 4 && seedRatio >= 0.40f);

			isFloodCandidateFace[faceIdx] = (floodHits >= 2 && floodRatio >= 0.20f) ||
			                                (floodHits >= 1 && totalSampled <= 4 && floodRatio >= 0.30f);
		}
		else
		{
			Vector2 centroid = (uv0 + uv1 + uv2) / 3f;
			int cx = Math.Clamp((int)(centroid.X * texW), 0, texW - 1);
			int cy = Math.Clamp((int)(centroid.Y * texH), 0, texH - 1);
			int idx = cy * texW + cx;

			isSeedFace[faceIdx] = seedMask[idx];
			isFloodCandidateFace[faceIdx] = floodFillMask[idx];
		}
	}

	private static void TraverseSeedCluster(int i, bool[] isSeedFace, bool[] seedClusterVisited, Dictionary<int, HashSet<int>> smoothAdjacency, HashSet<int> validatedSeedFaces)
	{
		var cluster = new List<int>();
		var queue = new Queue<int>();
		queue.Enqueue(i);
		seedClusterVisited[i] = true;

		while (queue.Count > 0)
		{
			int curr = queue.Dequeue();
			cluster.Add(curr);

			foreach (int neighbor in smoothAdjacency[curr])
			{
				if (!isSeedFace[neighbor] || seedClusterVisited[neighbor]) continue;

				seedClusterVisited[neighbor] = true;
				queue.Enqueue(neighbor);
			}
		}

		if (cluster.Count >= 3)
		{
			foreach (int f in cluster)
			{
				validatedSeedFaces.Add(f);
			}
		}
	}

	private static HashSet<int> FindSeedClusters(
		int faceCount, bool[] isSeedFace, Dictionary<int, HashSet<int>> smoothAdjacency)
	{
		var seedClusterVisited = new bool[faceCount];
		var validatedSeedFaces = new HashSet<int>();

		for (int i = 0; i < faceCount; i++)
		{
			if (!isSeedFace[i] || seedClusterVisited[i]) continue;

			TraverseSeedCluster(i, isSeedFace, seedClusterVisited, smoothAdjacency, validatedSeedFaces);
		}

		return validatedSeedFaces;
	}
}
