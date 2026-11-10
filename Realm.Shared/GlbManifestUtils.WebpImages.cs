using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Realm.Shared.Textures;
using SkiaSharp;

namespace Realm.Shared;

/// <summary>identifying the image buffer views and encoding each image.</summary>
public static partial class GlbManifestUtils
	{
	private static (Dictionary<int, int>, HashSet<int>) IdentifyImageBufferViews(JsonArray images, JsonArray bufferViews)
	{
		var imageBvMap = new Dictionary<int, int>();
		var imageBufferViews = new HashSet<int>();
		for (int i = 0; i < images.Count; i++)
		{
			if (images[i] is JsonObject imgObj && imgObj.TryGetPropertyValue("bufferView", out var bvVal))
			{
				int bvIdx = bvVal?.GetValue<int>() ?? -1;
				if (bvIdx >= 0 && bvIdx < bufferViews.Count)
				{
					imageBvMap[i] = bvIdx;
					imageBufferViews.Add(bvIdx);
				}
			}
		}

		return (imageBvMap, imageBufferViews);
	}

	private static Dictionary<int, byte[]> EncodeImagesToWebp(
		JsonArray images, JsonArray bufferViews, byte[] bin,
		HashSet<int> pbrImageIndices, Dictionary<int, int> imageBvMap, int maxResolution)
	{
		var newImageBytes = new Dictionary<int, byte[]>();
		for (int i = 0; i < images.Count; i++)
		{
			if (!imageBvMap.TryGetValue(i, out int bvIdx)) continue;
			if (bufferViews[bvIdx] is not JsonObject bv) continue;

			int byteOffset = bv["byteOffset"]?.GetValue<int>() ?? 0;
			int byteLength = bv["byteLength"]?.GetValue<int>() ?? 0;
			if (byteOffset + byteLength > bin.Length) continue;

			byte[] raw = new byte[byteLength];
			Array.Copy(bin, byteOffset, raw, 0, byteLength);

			newImageBytes[i] = ProcessSingleImage(raw, pbrImageIndices.Contains(i), maxResolution);
		}

		return newImageBytes;
	}

	private static byte[] ProcessSingleImage(byte[] raw, bool isPbr, int maxResolution)
	{
		try
		{
			using var img = SKBitmap.Decode(raw);
			if (img == null) return raw;

			SKBitmap workingImg = img;
			bool ownsResized = false;
			if (img.Width > maxResolution || img.Height > maxResolution)
			{
				float scale = Math.Min((float)maxResolution / img.Width, (float)maxResolution / img.Height);
				int targetW = Math.Max(1, (int)(img.Width * scale));
				int targetH = Math.Max(1, (int)(img.Height * scale));
				var resized = img.Resize(new SKImageInfo(targetW, targetH), new SKSamplingOptions(SKCubicResampler.Mitchell));
				if (resized != null)
				{
					workingImg = resized;
					ownsResized = true;
				}
			}

			try
			{
				return TextureConverter.EncodeWebp(
					workingImg,
					lossless: isPbr,
					quality: isPbr ? 100 : 80
				);
			}
			finally
			{
				if (ownsResized)
				{
					workingImg.Dispose();
				}
			}
		}
		catch
		{
			return raw;
		}
	}
}
