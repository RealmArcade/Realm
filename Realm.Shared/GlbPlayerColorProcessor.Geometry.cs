using System.Numerics;
using System.Text.Json.Nodes;
using Assimp;
using Realm.Shared.Metadata;
using Realm.Shared.ModelOptimization;
using Realm.Shared.Textures;
using SkiaSharp;

namespace Realm.Shared;

/// <summary>point in triangle, edge keys and the bt601 conversions.</summary>
public static partial class GlbPlayerColorProcessor
	{
	private static bool PointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
	{
		float d1 = (p.X - b.X) * (a.Y - b.Y) - (a.X - b.X) * (p.Y - b.Y);
		float d2 = (p.X - c.X) * (b.Y - c.Y) - (b.X - c.X) * (p.Y - c.Y);
		float d3 = (p.X - a.X) * (c.Y - a.Y) - (c.X - a.X) * (p.Y - a.Y);

		bool hasNeg = d1 < 0 || d2 < 0 || d3 < 0;
		bool hasPos = d1 > 0 || d2 > 0 || d3 > 0;

		return !(hasNeg && hasPos);
	}

	private static void AddEdge(Dictionary<(long, long), List<int>> edgeToFaces, long kA, long kB, int faceIdx)
	{
		if (kA == kB) return;
		long minK = kA < kB ? kA : kB;
		long maxK = kA < kB ? kB : kA;
		var key = (minK, maxK);
		if (!edgeToFaces.TryGetValue(key, out var list))
		{
			list = new List<int>(2);
			edgeToFaces[key] = list;
		}

		list.Add(faceIdx);
	}

	private static long QuantizePosition(Vector3 pos)
	{
		const float precision = 10000f;
		long x = (long)(pos.X * precision + 0.5f) & 0xFFFFF;
		long y = (long)(pos.Y * precision + 0.5f) & 0xFFFFF;
		long z = (long)(pos.Z * precision + 0.5f) & 0xFFFFF;
		return x | (y << 20) | (z << 40);
	}

	private static (float Cb, float Cr) HexToBt601CbCr(string hex)
	{
		var (r, g, b) = GlbChromaKeyMatcher.HexToRgb(hex);
		return RgbToBt601CbCr(r, g, b);
	}

	private static (float Cb, float Cr) RgbToBt601CbCr(float r, float g, float b)
	{
		float cb = -0.16874f * r - 0.33126f * g + 0.50000f * b;
		float cr = 0.50000f * r - 0.41869f * g - 0.08131f * b;
		return (cb, cr);
	}
}
