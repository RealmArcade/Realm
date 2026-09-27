using System;
using System.IO;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using Realm.Shared.Textures;
using SkiaSharp;

namespace Realm.Shared;

// CRITICAL / IN-MEMORY ONLY:
// This preprocessor performs a 1x baked CPU in-memory analytical chroma despill pass for loaded GLB/RMESH models at runtime.
// It is intended EXCLUSIVELY for runtime model loading in memory (e.g. ModelCache, thumbnail rendering) and must NEVER
// be serialized to disk, written to .glb, or packaged into .rmesh files during asset conversion or export workflows.
// On-disk asset files must strictly preserve the pristine, raw albedo textures so that spatial shaders can retain
// original albedos when 'ignore_player_color' is active.
public static class GlbInMemoryColorPreprocessor
{
	private static readonly float[] SrgbToLinearTable = PrecomputeSrgbToLinear();

	private static readonly Vector3 RgbToLmsRow0 = new(0.4122214708f, 0.5363325363f, 0.0514459929f);
	private static readonly Vector3 RgbToLmsRow1 = new(0.2119034982f, 0.6806995451f, 0.1073969566f);
	private static readonly Vector3 RgbToLmsRow2 = new(0.0883024619f, 0.2817188376f, 0.6299787005f);

	private static readonly Vector3 LmsToOklabRow0 = new(0.2104542553f, 0.7936177850f, -0.0040720468f);
	private static readonly Vector3 LmsToOklabRow1 = new(1.9779984951f, -2.4285922050f, 0.4505937099f);
	private static readonly Vector3 LmsToOklabRow2 = new(0.0259040371f, 0.7827717662f, -0.8086757660f);

	private static readonly Vector3 OklabToLmsRootRow0 = new(1.0f, +0.3963377774f, +0.2158037573f);
	private static readonly Vector3 OklabToLmsRootRow1 = new(1.0f, -0.1055613458f, -0.0638541728f);
	private static readonly Vector3 OklabToLmsRootRow2 = new(1.0f, -0.0894841775f, -1.2914855480f);

	private static readonly Vector3 LmsToLinearRgbRow0 = new(+4.0767416621f, -3.3077115913f, +0.2309699292f);
	private static readonly Vector3 LmsToLinearRgbRow1 = new(-1.2684380046f, +2.6097574011f, -0.3413193965f);
	private static readonly Vector3 LmsToLinearRgbRow2 = new(-0.0041960863f, -0.7034186147f, +1.7076147010f);

	private static float[] PrecomputeSrgbToLinear()
	{
		float[] table = new float[256];
		for (int i = 0; i < 256; i++)
		{
			float c = i / 255.0f;
			table[i] = c <= 0.04045f ? c / 12.92f : MathF.Pow((c + 0.055f) / 1.055f, 2.4f);
		}
		return table;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static byte LinearToSrgbByte(float linear)
	{
		if (linear <= 0.0f) return 0;
		if (linear >= 1.0f) return 255;
		float srgb = linear <= 0.0031308f
			? 12.92f * linear
			: 1.055f * MathF.Pow(linear, 1.0f / 2.4f) - 0.055f;
		return (byte)Math.Clamp((int)(srgb * 255.0f + 0.5f), 0, 255);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static Vector3 ConvertLinearRgbToOklab(Vector3 linearRgb)
	{
		float l = Vector3.Dot(linearRgb, RgbToLmsRow0);
		float m = Vector3.Dot(linearRgb, RgbToLmsRow1);
		float s = Vector3.Dot(linearRgb, RgbToLmsRow2);

		Vector3 lmsRoot = new(
			MathF.Cbrt(MathF.Max(0.0f, l)),
			MathF.Cbrt(MathF.Max(0.0f, m)),
			MathF.Cbrt(MathF.Max(0.0f, s)));

		return new Vector3(
			Vector3.Dot(lmsRoot, LmsToOklabRow0),
			Vector3.Dot(lmsRoot, LmsToOklabRow1),
			Vector3.Dot(lmsRoot, LmsToOklabRow2));
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static Vector3 ConvertOklabToLinearRgb(Vector3 oklab)
	{
		float lRoot = Vector3.Dot(oklab, OklabToLmsRootRow0);
		float mRoot = Vector3.Dot(oklab, OklabToLmsRootRow1);
		float sRoot = Vector3.Dot(oklab, OklabToLmsRootRow2);

		Vector3 lms = new(
			lRoot * lRoot * lRoot,
			mRoot * mRoot * mRoot,
			sRoot * sRoot * sRoot);

		return new Vector3(
			Vector3.Dot(lms, LmsToLinearRgbRow0),
			Vector3.Dot(lms, LmsToLinearRgbRow1),
			Vector3.Dot(lms, LmsToLinearRgbRow2));
	}

	public static byte[] PreprocessGlbInMemory(byte[] glbBytes, string? chromaKeyHex = null)
	{
		if (glbBytes == null || glbBytes.Length == 0)
		{
			return glbBytes ?? Array.Empty<byte>();
		}

		try
		{
			var (jsonNode, binChunk, glbVersion) = GlbManifestUtils.ParseGlb(glbBytes);
			if (jsonNode is not JsonObject root || binChunk == null)
			{
				return glbBytes;
			}

			var textures = root["textures"] as JsonArray;
			var materials = root["materials"] as JsonArray;
			var images = root["images"] as JsonArray;
			var bufferViews = root["bufferViews"] as JsonArray;

			if (textures == null || materials == null || images == null || bufferViews == null)
			{
				return glbBytes;
			}

			int albedoImageIndex = GlbPlayerColorProcessor.FindAlbedoImageIndex(textures, materials);
			if (albedoImageIndex < 0)
			{
				return glbBytes;
			}

			int ormImageIndex = GlbPlayerColorProcessor.FindOrmImageIndex(textures, materials);
			if (ormImageIndex < 0)
			{
				return glbBytes;
			}

			byte[] albedoRaw = GlbPlayerColorProcessor.ExtractImageBytes(albedoImageIndex, images, bufferViews, binChunk);
			if (albedoRaw.Length == 0)
			{
				return glbBytes;
			}

			byte[] ormRaw = GlbPlayerColorProcessor.ExtractImageBytes(ormImageIndex, images, bufferViews, binChunk);
			if (ormRaw.Length == 0)
			{
				return glbBytes;
			}

			using var ormImg = SKBitmap.Decode(ormRaw);
			if (ormImg == null) return glbBytes;

			bool hasMask = false;
			for (int y = 0; y < ormImg.Height; y++)
			{
				for (int x = 0; x < ormImg.Width; x++)
				{
					if (ormImg.GetPixel(x, y).Red > 0)
					{
						hasMask = true;
						break;
					}
				}
				if (hasMask) break;
			}

			if (!hasMask)
			{
				return glbBytes;
			}

			using var albedoImg = SKBitmap.Decode(albedoRaw);
			if (albedoImg == null) return glbBytes;

			string effectiveChromaKey = chromaKeyHex ?? string.Empty;
			if (string.IsNullOrWhiteSpace(effectiveChromaKey) || string.Equals(effectiveChromaKey, "auto", StringComparison.OrdinalIgnoreCase))
			{
				effectiveChromaKey = "#FF00FF";
			}
			else
			{
				effectiveChromaKey = effectiveChromaKey.Trim();
				if (!effectiveChromaKey.StartsWith('#'))
				{
					effectiveChromaKey = "#" + effectiveChromaKey;
				}
			}

			ApplyAnalyticalChromaDespill(albedoImg, ormImg, effectiveChromaKey);

			byte[] newAlbedoBytes = TextureConverter.EncodeWebp(albedoImg, lossless: false, quality: 90);
			return RebuildGlbWithUpdatedAlbedoTexture(root, binChunk, albedoImageIndex, newAlbedoBytes, glbVersion);
		}
		catch
		{
			return glbBytes;
		}
	}

	public static void ApplyAnalyticalChromaDespill(
		SKBitmap albedoImg,
		SKBitmap ormImg,
		string chromaKeyHex)
	{
		(float targetR, float targetG, float targetB) = GlbPlayerColorProcessor.HexToRgb(chromaKeyHex);
		Vector3 targetLinear = new(
			SrgbToLinearTable[(byte)Math.Clamp((int)(targetR * 255.0f + 0.5f), 0, 255)],
			SrgbToLinearTable[(byte)Math.Clamp((int)(targetG * 255.0f + 0.5f), 0, 255)],
			SrgbToLinearTable[(byte)Math.Clamp((int)(targetB * 255.0f + 0.5f), 0, 255)]);

		Vector3 targetOklab = ConvertLinearRgbToOklab(targetLinear);
		Vector2 keyVector = new(targetOklab.Y, targetOklab.Z);
		float keyChroma = keyVector.Length();
		if (keyChroma < 1e-5f)
		{
			return;
		}
		Vector2 keyUnitVector = keyVector / keyChroma;

		int width = albedoImg.Width;
		int height = albedoImg.Height;
		int ormWidth = ormImg.Width;
		int ormHeight = ormImg.Height;
		bool sameDimensions = (width == ormWidth && height == ormHeight);

		float[] maskValues = new float[width * height];
		for (int y = 0; y < height; y++)
		{
			int ormY = sameDimensions ? y : Math.Clamp((int)(((y + 0.5f) / height) * ormHeight), 0, ormHeight - 1);
			int rowOffset = y * width;

			for (int x = 0; x < width; x++)
			{
				int ormX = sameDimensions ? x : Math.Clamp((int)(((x + 0.5f) / width) * ormWidth), 0, ormWidth - 1);
				maskValues[rowOffset + x] = ormImg.GetPixel(ormX, ormY).Red / 255.0f;
			}
		}

		for (int y = 0; y < height; y++)
		{
			int rowOffset = y * width;

			for (int x = 0; x < width; x++)
			{
				float mask = maskValues[rowOffset + x];
				if (mask >= 0.999f) continue;

				var pixel = albedoImg.GetPixel(x, y);
				if (TryDespillPixel(pixel.Red, pixel.Green, pixel.Blue, mask, keyUnitVector, out byte newR, out byte newG, out byte newB))
				{
					albedoImg.SetPixel(x, y, new SKColor(newR, newG, newB, pixel.Alpha));
				}
			}
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static bool TryDespillPixel(
		byte rByte, byte gByte, byte bByte,
		float maskFactor,
		Vector2 keyUnitVector,
		out byte outR, out byte outG, out byte outB)
	{
		outR = rByte;
		outG = gByte;
		outB = bByte;

		if (maskFactor >= 0.999f)
		{
			return false;
		}

		float nonMaskWeight = 1.0f - maskFactor;

		Vector3 linearRgb = new(
			SrgbToLinearTable[rByte],
			SrgbToLinearTable[gByte],
			SrgbToLinearTable[bByte]);

		Vector3 oklab = ConvertLinearRgbToOklab(linearRgb);
		Vector2 chrominance = new(oklab.Y, oklab.Z);

		float parallelComponent = Vector2.Dot(chrominance, keyUnitVector);
		if (parallelComponent <= 0.0f)
		{
			return false;
		}

		Vector2 perpendicular = chrominance - parallelComponent * keyUnitVector;
		float orthogonalChroma = perpendicular.Length();

		float maxAllowedKeyChroma = 0.35f * orthogonalChroma;
		float excessChrominance = MathF.Max(0.0f, parallelComponent - maxAllowedKeyChroma) * nonMaskWeight;

		if (excessChrominance <= 1e-6f)
		{
			return false;
		}

		Vector2 despilledChrominance = chrominance - excessChrominance * keyUnitVector;
		Vector3 despilledOklab = new(oklab.X, despilledChrominance.X, despilledChrominance.Y);

		Vector3 despilledLinearRgb = ConvertOklabToLinearRgb(despilledOklab);
		outR = LinearToSrgbByte(despilledLinearRgb.X);
		outG = LinearToSrgbByte(despilledLinearRgb.Y);
		outB = LinearToSrgbByte(despilledLinearRgb.Z);
		return true;
	}

	private static byte[] RebuildGlbWithUpdatedAlbedoTexture(
		JsonObject root,
		byte[] binChunk,
		int albedoImageIndex,
		byte[] newAlbedoBytes,
		uint glbVersion)
	{
		var accessors = root["accessors"] as JsonArray ?? new JsonArray();
		var bufferViews = root["bufferViews"] as JsonArray ?? new JsonArray();
		var images = root["images"] as JsonArray ?? new JsonArray();
		var textures = root["textures"] as JsonArray ?? new JsonArray();

		var retainedBvIndices = new System.Collections.Generic.HashSet<int>();
		foreach (var acc in accessors)
		{
			if (acc is JsonObject accObj && accObj.TryGetPropertyValue("bufferView", out var bvVal) && bvVal != null)
			{
				int bvIdx = bvVal.GetValue<int>();
				if (bvIdx >= 0 && bvIdx < bufferViews.Count)
				{
					retainedBvIndices.Add(bvIdx);
				}
			}
		}

		for (int i = 0; i < images.Count; i++)
		{
			if (i == albedoImageIndex)
			{
				continue;
			}

			if (images[i] is JsonObject imgObj)
			{
				int imgBv = GlbPlayerColorProcessor.GetImageBufferViewIndex(imgObj);
				if (imgBv >= 0 && imgBv < bufferViews.Count)
				{
					retainedBvIndices.Add(imgBv);
				}
			}
		}

		using var newBinStream = new MemoryStream();
		var oldBvToNewBv = new System.Collections.Generic.Dictionary<int, int>();
		var newBufferViewsList = new JsonArray();

		for (int oldBvIdx = 0; oldBvIdx < bufferViews.Count; oldBvIdx++)
		{
			if (!retainedBvIndices.Contains(oldBvIdx))
			{
				continue;
			}

			if (bufferViews[oldBvIdx] is not JsonObject oldBv)
			{
				continue;
			}

			int origOffset = oldBv["byteOffset"]?.GetValue<int>() ?? 0;
			int origLength = oldBv["byteLength"]?.GetValue<int>() ?? 0;

			while ((newBinStream.Position % 4) != 0) newBinStream.WriteByte(0);
			int newOffset = (int)newBinStream.Position;

			if (origOffset + origLength <= binChunk.Length && origLength > 0)
			{
				newBinStream.Write(binChunk, origOffset, origLength);
				while ((newBinStream.Position % 4) != 0) newBinStream.WriteByte(0);
			}

			var clonedBv = (JsonObject)oldBv.DeepClone();
			clonedBv["byteOffset"] = newOffset;
			clonedBv["buffer"] = 0;

			newBufferViewsList.Add(clonedBv);
			oldBvToNewBv[oldBvIdx] = newBufferViewsList.Count - 1;
		}

		while ((newBinStream.Position % 4) != 0) newBinStream.WriteByte(0);
		int albedoOffset = (int)newBinStream.Position;
		newBinStream.Write(newAlbedoBytes, 0, newAlbedoBytes.Length);
		while ((newBinStream.Position % 4) != 0) newBinStream.WriteByte(0);

		newBufferViewsList.Add(new JsonObject
		{
			["byteOffset"] = albedoOffset,
			["byteLength"] = newAlbedoBytes.Length,
			["buffer"] = 0
		});
		int newAlbedoBvIdx = newBufferViewsList.Count - 1;

		foreach (var acc in accessors)
		{
			if (acc is JsonObject accObj && accObj.TryGetPropertyValue("bufferView", out var bvVal) && bvVal != null)
			{
				int oldBv = bvVal.GetValue<int>();
				if (oldBvToNewBv.TryGetValue(oldBv, out int newBv))
				{
					accObj["bufferView"] = newBv;
				}
			}
		}

		for (int i = 0; i < images.Count; i++)
		{
			if (i == albedoImageIndex) continue;
			if (images[i] is JsonObject imgObj)
			{
				if (imgObj.TryGetPropertyValue("bufferView", out var bvVal) && bvVal != null)
				{
					int oldBv = bvVal.GetValue<int>();
					if (oldBvToNewBv.TryGetValue(oldBv, out int newBv))
					{
						imgObj["bufferView"] = newBv;
					}
				}
				if (imgObj["extensions"] is JsonObject imgExt)
				{
					if (imgExt["EXT_texture_webp"] is JsonObject webp && webp.TryGetPropertyValue("bufferView", out var wbVal) && wbVal != null)
					{
						int oldBv = wbVal.GetValue<int>();
						if (oldBvToNewBv.TryGetValue(oldBv, out int newBv))
						{
							webp["bufferView"] = newBv;
						}
					}
				}
			}
		}

		if (albedoImageIndex >= 0 && albedoImageIndex < images.Count && images[albedoImageIndex] is JsonObject albedoImgObj)
		{
			albedoImgObj["bufferView"] = newAlbedoBvIdx;
			albedoImgObj["mimeType"] = "image/webp";
			if (albedoImgObj.ContainsKey("uri")) albedoImgObj.Remove("uri");
			if (albedoImgObj.ContainsKey("extensions")) albedoImgObj.Remove("extensions");
		}

		for (int i = 0; i < textures.Count; i++)
		{
			if (textures[i] is not JsonObject texObj) continue;
			int src = texObj["source"]?.GetValue<int>() ?? -1;
			if (src < 0)
			{
				src = GlbPlayerColorProcessor.ResolveTextureToImage(i, textures);
				if (src >= 0 && src < images.Count)
				{
					texObj["source"] = src;
				}
				else if (images.Count > 0)
				{
					texObj["source"] = 0;
					src = 0;
				}
			}
			if (src >= 0)
			{
				if (texObj["extensions"] is JsonObject texExt)
				{
					if (texExt.ContainsKey("KHR_texture_basisu")) texExt.Remove("KHR_texture_basisu");
					texExt["EXT_texture_webp"] = new JsonObject { ["source"] = src };
				}
				else
				{
					texObj["extensions"] = new JsonObject
					{
						["EXT_texture_webp"] = new JsonObject { ["source"] = src }
					};
				}
			}
		}

		if (root.TryGetPropertyValue("extensionsUsed", out var extNode) && extNode is JsonArray extArray)
		{
			bool exists = false;
			foreach (var item in extArray)
			{
				if (item?.GetValue<string>() == "EXT_texture_webp")
				{
					exists = true;
					break;
				}
			}
			if (!exists) extArray.Add("EXT_texture_webp");
		}
		else
		{
			root["extensionsUsed"] = new JsonArray("EXT_texture_webp");
		}

		root["bufferViews"] = newBufferViewsList;

		if (root["buffers"] is JsonArray buffers && buffers.Count > 0 && buffers[0] is JsonObject buf0)
		{
			buf0["byteLength"] = (int)newBinStream.Position;
		}

		byte[] newBin = newBinStream.ToArray();
		return GlbManifestUtils.BuildGlb(root, newBin, glbVersion);
	}
}
