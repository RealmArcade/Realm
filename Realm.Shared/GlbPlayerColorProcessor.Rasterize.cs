using System.Numerics;
using System.Text.Json.Nodes;
using Assimp;
using Realm.Shared.Metadata;
using Realm.Shared.ModelOptimization;
using Realm.Shared.Textures;
using SkiaSharp;

namespace Realm.Shared;

/// <summary>rasterising each face into the mask.</summary>
public static partial class GlbPlayerColorProcessor
	{
	private static (int minX, int maxX, int minY, int maxY) GetTriangleBounds(Vector2 uv0, Vector2 uv1, Vector2 uv2, int texW, int texH)
	{
		int minX = Math.Clamp((int)(Math.Min(Math.Min(uv0.X, uv1.X), uv2.X) * texW), 0, texW - 1);
		int maxX = Math.Clamp((int)(Math.Ceiling(Math.Max(Math.Max(uv0.X, uv1.X), uv2.X) * texW)), 0, texW - 1);
		int minY = Math.Clamp((int)(Math.Min(Math.Min(uv0.Y, uv1.Y), uv2.Y) * texH), 0, texH - 1);
		int maxY = Math.Clamp((int)(Math.Ceiling(Math.Max(Math.Max(uv0.Y, uv1.Y), uv2.Y) * texH)), 0, texH - 1);
		return (minX, maxX, minY, maxY);
	}

	private static void RasterizeFace(
		int faceIdx, Vector2 uv0, Vector2 uv1, Vector2 uv2,
		int texW, int texH, bool isConfirmed,
		bool[] floodFillMask, bool[] seedMask,
		bool[] isConfirmedPolygonTexel, float[] globalMask, bool[] isNonPlayerFaceTexel)
	{
		var (minX, maxX, minY, maxY) = GetTriangleBounds(uv0, uv1, uv2, texW, texH);

		for (int py = minY; py <= maxY; py++)
		{
			for (int px = minX; px <= maxX; px++)
			{
				ProcessRasterizedPixel(px, py, texW, texH, uv0, uv1, uv2, isConfirmed, floodFillMask, seedMask, isConfirmedPolygonTexel, globalMask, isNonPlayerFaceTexel);
			}
		}
	}

	private static void ProcessRasterizedPixel(
		int px, int py, int texW, int texH,
		Vector2 uv0, Vector2 uv1, Vector2 uv2,
		bool isConfirmed, bool[] floodFillMask, bool[] seedMask,
		bool[] isConfirmedPolygonTexel, float[] globalMask, bool[] isNonPlayerFaceTexel)
	{
		var p = new Vector2(px / (float)texW, py / (float)texH);
		if (!PointInTriangle(p, uv0, uv1, uv2)) return;

		int idx = py * texW + px;
		if (isConfirmed)
		{
			isConfirmedPolygonTexel[idx] = true;
			if (floodFillMask[idx] || seedMask[idx])
			{
				globalMask[idx] = 1.0f;
			}
		}
		else
		{
			isNonPlayerFaceTexel[idx] = true;
		}
	}

	private static void ProcessPrimitive(
		JsonObject primObj, JsonArray accessors, JsonArray bufferViews, byte[] binChunk,
		bool[] seedMask, bool[] floodFillMask, int texW, int texH, float cosCreaseThreshold,
		GlbPlayerColorOptions options, ref int totalFaces, ref int maskedFaces,
		bool[] isConfirmedPolygonTexel, float[] globalMask, bool[] isNonPlayerFaceTexel)
	{
		var faceUvCoords = ExtractFaceUvData(primObj, accessors, bufferViews, binChunk);
		if (faceUvCoords.Count == 0) return;

		var facePositions = ExtractFacePositionData(primObj, accessors, bufferViews, binChunk);
		if (facePositions.Count == 0) return;

		totalFaces += faceUvCoords.Count;

		var confirmedFaces = ProcessFacesSurfaceAware(
			facePositions, faceUvCoords, seedMask, floodFillMask, texW, texH, cosCreaseThreshold, options.MinClusterFaces);

		maskedFaces += confirmedFaces.Count;

		for (int faceIdx = 0; faceIdx < faceUvCoords.Count; faceIdx++)
		{
			var (uv0, uv1, uv2) = faceUvCoords[faceIdx];
			bool isConfirmed = confirmedFaces.Contains(faceIdx);
			RasterizeFace(faceIdx, uv0, uv1, uv2, texW, texH, isConfirmed, floodFillMask, seedMask, isConfirmedPolygonTexel, globalMask, isNonPlayerFaceTexel);
		}
	}

	private static void ProcessMeshes(
		JsonArray meshes, JsonArray accessors, JsonArray bufferViews, byte[] binChunk,
		bool[] seedMask, bool[] floodFillMask, int texW, int texH, float cosCreaseThreshold,
		GlbPlayerColorOptions options, ref int totalFaces, ref int maskedFaces,
		bool[] isConfirmedPolygonTexel, float[] globalMask, bool[] isNonPlayerFaceTexel)
	{
		foreach (var mesh in meshes)
		{
			if (mesh is not JsonObject meshObj) continue;
			if (meshObj["primitives"] is not JsonArray primitives) continue;

			foreach (var prim in primitives)
			{
				if (prim is not JsonObject primObj) continue;

				ProcessPrimitive(
					primObj, accessors, bufferViews, binChunk, seedMask, floodFillMask, texW, texH,
					cosCreaseThreshold, options, ref totalFaces, ref maskedFaces,
					isConfirmedPolygonTexel, globalMask, isNonPlayerFaceTexel);
			}
		}
	}
}
