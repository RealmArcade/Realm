using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Realm.Shared.Textures;
using SkiaSharp;

namespace Realm.Shared;

/// <summary>rebuilding the binary chunk around the re-encoded images.</summary>
public static partial class GlbManifestUtils
	{
	private static byte[] RebuildBinaryChunk(
		byte[] bin, JsonArray images, JsonArray bufferViews,
		Dictionary<int, int> imageBvMap, HashSet<int> imageBufferViews,
		Dictionary<int, byte[]> newImageBytes, JsonObject root)
	{
		using var newBinStream = new MemoryStream();
		var newBvOffsets = new int[bufferViews.Count];
		var newBvLengths = new int[bufferViews.Count];

		RebuildNonImageBufferViews(bin, bufferViews, imageBufferViews, newBinStream, newBvOffsets, newBvLengths);
		RebuildImageBufferViews(images, imageBvMap, newImageBytes, newBinStream, newBvOffsets, newBvLengths);

		UpdateBufferViewsJson(bufferViews, newBvOffsets, newBvLengths);
		UpdateBuffersByteLength(root, (int)newBinStream.Position);

		return newBinStream.ToArray();
	}

	private static void RebuildNonImageBufferViews(
		byte[] bin, JsonArray bufferViews, HashSet<int> imageBufferViews,
		MemoryStream newBinStream, int[] newBvOffsets, int[] newBvLengths)
	{
		for (int bvIdx = 0; bvIdx < bufferViews.Count; bvIdx++)
		{
			if (imageBufferViews.Contains(bvIdx)) continue;
			if (bufferViews[bvIdx] is not JsonObject bv) continue;

			int origOffset = bv["byteOffset"]?.GetValue<int>() ?? 0;
			int origLength = bv["byteLength"]?.GetValue<int>() ?? 0;

			newBvOffsets[bvIdx] = (int)newBinStream.Position;
			newBvLengths[bvIdx] = origLength;

			if (origOffset + origLength <= bin.Length)
			{
				newBinStream.Write(bin, origOffset, origLength);
				int pad = (4 - (origLength % 4)) % 4;
				for (int p = 0; p < pad; p++) newBinStream.WriteByte(0);
			}
		}
	}

	private static void RebuildImageBufferViews(
		JsonArray images, Dictionary<int, int> imageBvMap, Dictionary<int, byte[]> newImageBytes,
		MemoryStream newBinStream, int[] newBvOffsets, int[] newBvLengths)
	{
		for (int i = 0; i < images.Count; i++)
		{
			if (!imageBvMap.TryGetValue(i, out int bvIdx)) continue;
			if (!newImageBytes.TryGetValue(i, out byte[]? imgData)) continue;

			newBvOffsets[bvIdx] = (int)newBinStream.Position;
			newBvLengths[bvIdx] = imgData.Length;

			newBinStream.Write(imgData, 0, imgData.Length);
			int pad = (4 - (imgData.Length % 4)) % 4;
			for (int p = 0; p < pad; p++) newBinStream.WriteByte(0);

			if (images[i] is JsonObject imgObj)
			{
				imgObj["mimeType"] = "image/webp";
			}
		}
	}

	private static void UpdateBufferViewsJson(JsonArray bufferViews, int[] newBvOffsets, int[] newBvLengths)
	{
		for (int bvIdx = 0; bvIdx < bufferViews.Count; bvIdx++)
		{
			if (bufferViews[bvIdx] is JsonObject bv)
			{
				bv["byteOffset"] = newBvOffsets[bvIdx];
				bv["byteLength"] = newBvLengths[bvIdx];
			}
		}
	}

	private static void UpdateBuffersByteLength(JsonObject root, int totalLength)
	{
		if (root["buffers"] is JsonArray buffers && buffers.Count > 0 && buffers[0] is JsonObject buf0)
		{
			buf0["byteLength"] = totalLength;
		}
	}
}
