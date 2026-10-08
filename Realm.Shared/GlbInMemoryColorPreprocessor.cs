using System;
using System.IO;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
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
	private static readonly System.Collections.Concurrent.ConcurrentDictionary<ulong, byte[]> DespilledGlbCache = new();
	private static readonly float[] SrgbToLinearTable = PrecomputeSrgbToLinear();
	private static readonly byte[] LinearToSrgbLut = PrecomputeLinearToSrgbLut();
	private static readonly float[] CbrtLut = PrecomputeCbrtLut();

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

	private static byte[] PrecomputeLinearToSrgbLut()
	{
		byte[] table = new byte[65536];
		for (int i = 0; i < 65536; i++)
		{
			float linear = i / 65535.0f;
			float srgb = linear <= 0.0031308f
				? 12.92f * linear
				: 1.055f * MathF.Pow(linear, 1.0f / 2.4f) - 0.055f;
			table[i] = (byte)Math.Clamp((int)(srgb * 255.0f + 0.5f), 0, 255);
		}
		return table;
	}

	private static float[] PrecomputeCbrtLut()
	{
		float[] table = new float[65536];
		for (int i = 0; i < 65536; i++)
		{
			table[i] = MathF.Cbrt(i / 65535.0f);
		}
		return table;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static float FastCbrt(float val)
	{
		int idx = (int)(val * 65535.0f);
		if ((uint)idx >= 65536)
		{
			return val <= 0.0f ? 0.0f : MathF.Cbrt(val);
		}
		return CbrtLut[idx];
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static byte LinearToSrgbByte(float linear)
	{
		int idx = (int)(linear * 65535.0f);
		if ((uint)idx >= 65536)
		{
			return idx < 0 ? (byte)0 : (byte)255;
		}
		return LinearToSrgbLut[idx];
	}

	private static void StoreInCache(ulong cacheKey, byte[] value)
	{
		DespilledGlbCache[cacheKey] = value;
	}

	private static ulong ComputeFnv1a64(byte[] bytes, string chromaKey)
	{
		ulong hash = 14695981039346656037UL;
		hash ^= (ulong)bytes.Length;
		hash *= 1099511628211UL;

		int sampleSize = Math.Min(bytes.Length, 256);
		for (int i = 0; i < sampleSize; i++)
		{
			hash ^= bytes[i];
			hash *= 1099511628211UL;
		}

		if (bytes.Length > 512)
		{
			int midStart = (bytes.Length / 2) - 128;
			int midEnd = midStart + 256;
			for (int i = midStart; i < midEnd; i++)
			{
				hash ^= bytes[i];
				hash *= 1099511628211UL;
			}

			int tailStart = bytes.Length - 256;
			for (int i = tailStart; i < bytes.Length; i++)
			{
				hash ^= bytes[i];
				hash *= 1099511628211UL;
			}
		}

		for (int i = 0; i < chromaKey.Length; i++)
		{
			hash ^= (byte)chromaKey[i];
			hash *= 1099511628211UL;
		}
		return hash;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static Vector3 ConvertLinearRgbToOklab(Vector3 linearRgb)
	{
		float l = linearRgb.X * 0.4122214708f + linearRgb.Y * 0.5363325363f + linearRgb.Z * 0.0514459929f;
		float m = linearRgb.X * 0.2119034982f + linearRgb.Y * 0.6806995451f + linearRgb.Z * 0.1073969566f;
		float s = linearRgb.X * 0.0883024619f + linearRgb.Y * 0.2817188376f + linearRgb.Z * 0.6299787005f;

		float lRoot = FastCbrt(MathF.Max(0.0f, l));
		float mRoot = FastCbrt(MathF.Max(0.0f, m));
		float sRoot = FastCbrt(MathF.Max(0.0f, s));

		return new Vector3(
			lRoot * 0.2104542553f + mRoot * 0.7936177850f + sRoot * -0.0040720468f,
			lRoot * 1.9779984951f + mRoot * -2.4285922050f + sRoot * 0.4505937099f,
			lRoot * 0.0259040371f + mRoot * 0.7827717662f + sRoot * -0.8086757660f);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static Vector3 ConvertOklabToLinearRgb(Vector3 oklab)
	{
		float lRoot = oklab.X + oklab.Y * 0.3963377774f + oklab.Z * 0.2158037573f;
		float mRoot = oklab.X + oklab.Y * -0.1055613458f + oklab.Z * -0.0638541728f;
		float sRoot = oklab.X + oklab.Y * -0.0894841775f + oklab.Z * -1.2914855480f;

		float l = lRoot * lRoot * lRoot;
		float m = mRoot * mRoot * mRoot;
		float s = sRoot * sRoot * sRoot;

		return new Vector3(
			l * 4.0767416621f + m * -3.3077115913f + s * 0.2309699292f,
			l * -1.2684380046f + m * 2.6097574011f + s * -0.3413193965f,
			l * -0.0041960863f + m * -0.7034186147f + s * 1.7076147010f);
	}

	public static byte[] PreprocessGlbInMemory(byte[] glbBytes, string? chromaKeyHex = null)
	{
		if (glbBytes == null || glbBytes.Length == 0)
		{
			return glbBytes ?? Array.Empty<byte>();
		}

		string effectiveChromaKey = NormalizeChromaKey(chromaKeyHex);

		ulong cacheKey = ComputeFnv1a64(glbBytes, effectiveChromaKey);
		if (DespilledGlbCache.TryGetValue(cacheKey, out var cachedBytes))
		{
			return cachedBytes;
		}

		try
		{
			return TryProcessGlbCore(glbBytes, effectiveChromaKey, cacheKey);
		}
		catch
		{
			StoreInCache(cacheKey, glbBytes);
			return glbBytes;
		}
	}

	private static string NormalizeChromaKey(string? chromaKeyHex)
	{
		string key = chromaKeyHex ?? string.Empty;
		if (string.IsNullOrWhiteSpace(key) || string.Equals(key, "auto", StringComparison.OrdinalIgnoreCase))
			return "#FF00FF";

		key = key.Trim();
		return key.StartsWith('#') ? key : "#" + key;
	}

	private static byte[] TryProcessGlbCore(byte[] glbBytes, string effectiveChromaKey, ulong cacheKey)
	{
		var (jsonNode, binChunk, glbVersion) = GlbManifestUtils.ParseGlb(glbBytes);
		if (jsonNode is not JsonObject root || binChunk == null) return glbBytes;

		if (!TryGetGlbArrays(root, out var textures, out var materials, out var images, out var bufferViews))
			return glbBytes;

		int albedoImageIndex = GlbPlayerColorProcessor.FindAlbedoImageIndex(textures, materials);
		int ormImageIndex = GlbPlayerColorProcessor.FindOrmImageIndex(textures, materials);
		if (albedoImageIndex < 0 || ormImageIndex < 0) return glbBytes;

		byte[] albedoRaw = GlbPlayerColorProcessor.ExtractImageBytes(albedoImageIndex, images, bufferViews, binChunk);
		byte[] ormRaw = GlbPlayerColorProcessor.ExtractImageBytes(ormImageIndex, images, bufferViews, binChunk);
		if (albedoRaw.Length == 0 || ormRaw.Length == 0) return glbBytes;

		using var ormImg = SKBitmap.Decode(ormRaw);
		if (ormImg == null || !HasMaskInOrm(ormImg))
		{
			StoreInCache(cacheKey, glbBytes);
			return glbBytes;
		}

		using var albedoImg = SKBitmap.Decode(albedoRaw);
		if (albedoImg == null)
		{
			StoreInCache(cacheKey, glbBytes);
			return glbBytes;
		}

		return ProcessDecodedImages(albedoImg, ormImg, effectiveChromaKey, root, binChunk, albedoImageIndex, glbVersion, cacheKey);
	}

	private static bool TryGetGlbArrays(JsonObject root, out JsonArray textures, out JsonArray materials, out JsonArray images, out JsonArray bufferViews)
	{
		textures = (root["textures"] as JsonArray)!;
		materials = (root["materials"] as JsonArray)!;
		images = (root["images"] as JsonArray)!;
		bufferViews = (root["bufferViews"] as JsonArray)!;

		return textures != null && materials != null && images != null && bufferViews != null;
	}

	private static byte[] ProcessDecodedImages(SKBitmap albedoImg, SKBitmap ormImg, string chromaKey, JsonObject root, byte[] binChunk, int albedoImageIndex, uint glbVersion, ulong cacheKey)
	{
		ApplyAnalyticalChromaDespill(albedoImg, ormImg, chromaKey);
		byte[] newAlbedoBytes = TextureConverter.EncodeWebp(albedoImg, lossless: false, quality: 90, method: 1, sharpYuv: false);
		byte[] resultGlb = RebuildGlbWithUpdatedAlbedoTexture(root, binChunk, albedoImageIndex, newAlbedoBytes, glbVersion);
		StoreInCache(cacheKey, resultGlb);
		return resultGlb;
	}

	private static bool HasMaskInOrm(SKBitmap ormImg)
	{
		SKBitmap workBitmap = ormImg;
		bool disposeWork = false;
		if (ormImg.ColorType != SKColorType.Rgba8888 && ormImg.ColorType != SKColorType.Bgra8888)
		{
			workBitmap = ormImg.Copy(SKColorType.Rgba8888);
			disposeWork = workBitmap != null && workBitmap != ormImg;
			if (workBitmap == null) workBitmap = ormImg;
		}

		try
		{
			IntPtr pixelsPtr = workBitmap.GetPixels();
			if (pixelsPtr != IntPtr.Zero)
			{
				return HasMaskUnsafe(workBitmap, pixelsPtr);
			}

			return HasMaskSafe(workBitmap);
		}
		finally
		{
			if (disposeWork) workBitmap.Dispose();
		}
	}

	private static unsafe bool HasMaskUnsafe(SKBitmap workBitmap, IntPtr pixelsPtr)
	{
		byte* basePtr = (byte*)pixelsPtr;
		int rowBytes = workBitmap.RowBytes;
		int rOffset = workBitmap.ColorType == SKColorType.Bgra8888 ? 2 : 0;
		int width = workBitmap.Width;
		int height = workBitmap.Height;

		for (int y = 0; y < height; y++)
		{
			byte* row = basePtr + (y * rowBytes);
			if (HasMaskInRowUnsafe(row, width, rOffset))
			{
				return true;
			}
		}
		return false;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static unsafe bool HasMaskInRowUnsafe(byte* row, int width, int rOffset)
	{
		for (int x = 0; x < width; x++)
		{
			if (row[x * 4 + rOffset] > 0)
			{
				return true;
			}
		}
		return false;
	}

	private static bool HasMaskSafe(SKBitmap workBitmap)
	{
		int h = workBitmap.Height;
		int w = workBitmap.Width;
		for (int y = 0; y < h; y++)
		{
			if (HasMaskInRowSafe(workBitmap, y, w))
			{
				return true;
			}
		}
		return false;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static bool HasMaskInRowSafe(SKBitmap workBitmap, int y, int w)
	{
		for (int x = 0; x < w; x++)
		{
			if (workBitmap.GetPixel(x, y).Red > 0)
			{
				return true;
			}
		}
		return false;
	}

	public static void ApplyAnalyticalChromaDespill(
		SKBitmap albedoImg,
		SKBitmap ormImg,
		string chromaKeyHex)
	{
		if (!TryCalculateKeyUnitVector(chromaKeyHex, out Vector2 keyUnitVector))
			return;

		bool sameDimensions = (albedoImg.Width == ormImg.Width && albedoImg.Height == ormImg.Height);

		var (workAlbedo, disposeWorkAlbedo) = PrepareBitmap(albedoImg);
		var (workOrm, disposeWorkOrm) = PrepareBitmap(ormImg);

		try
		{
			ExecuteDespill(albedoImg, workAlbedo, workOrm, keyUnitVector, sameDimensions, disposeWorkAlbedo);
		}
		finally
		{
			if (disposeWorkAlbedo) workAlbedo.Dispose();
			if (disposeWorkOrm) workOrm.Dispose();
		}
	}

	private static bool TryCalculateKeyUnitVector(string chromaKeyHex, out Vector2 keyUnitVector)
	{
		keyUnitVector = default;
		(float targetR, float targetG, float targetB) = GlbPlayerColorProcessor.HexToRgb(chromaKeyHex);
		Vector3 targetLinear = new(
			SrgbToLinearTable[(byte)Math.Clamp((int)(targetR * 255.0f + 0.5f), 0, 255)],
			SrgbToLinearTable[(byte)Math.Clamp((int)(targetG * 255.0f + 0.5f), 0, 255)],
			SrgbToLinearTable[(byte)Math.Clamp((int)(targetB * 255.0f + 0.5f), 0, 255)]);

		Vector3 targetOklab = ConvertLinearRgbToOklab(targetLinear);
		Vector2 keyVector = new(targetOklab.Y, targetOklab.Z);
		float keyChroma = keyVector.Length();
		if (keyChroma < 1e-5f) return false;

		keyUnitVector = keyVector / keyChroma;
		return true;
	}

	private static (SKBitmap workBitmap, bool disposeWork) PrepareBitmap(SKBitmap original)
	{
		if (original.ColorType == SKColorType.Rgba8888 || original.ColorType == SKColorType.Bgra8888)
		{
			return (original, false);
		}

		SKBitmap workBitmap = original.Copy(SKColorType.Rgba8888) ?? original;
		bool disposeWork = workBitmap != original;
		return (workBitmap, disposeWork);
	}

	private static void ExecuteDespill(SKBitmap originalAlbedo, SKBitmap workAlbedo, SKBitmap workOrm, Vector2 keyUnitVector, bool sameDimensions, bool copyBack)
	{
		IntPtr albedoPtr = workAlbedo.GetPixels();
		IntPtr ormPtr = workOrm.GetPixels();

		if (albedoPtr != IntPtr.Zero && ormPtr != IntPtr.Zero)
		{
			ApplyAnalyticalChromaDespillUnsafe(workAlbedo, workOrm, keyUnitVector, sameDimensions);
		}
		else
		{
			ApplyAnalyticalChromaDespillSafe(workAlbedo, workOrm, keyUnitVector, sameDimensions);
		}

		if (copyBack)
		{
			using var skImg = SKImage.FromBitmap(workAlbedo);
			using var canvas = new SKCanvas(originalAlbedo);
			canvas.Clear();
			canvas.DrawImage(skImg, 0, 0);
		}
	}

	private static unsafe void ApplyAnalyticalChromaDespillUnsafe(SKBitmap workAlbedo, SKBitmap workOrm, Vector2 keyUnitVector, bool sameDimensions)
	{
		byte* albBase = (byte*)workAlbedo.GetPixels();
		byte* ormBase = (byte*)workOrm.GetPixels();
		int albRowBytes = workAlbedo.RowBytes;
		int ormRowBytes = workOrm.RowBytes;

		int albROff = workAlbedo.ColorType == SKColorType.Bgra8888 ? 2 : 0;
		int albGOff = 1;
		int albBOff = workAlbedo.ColorType == SKColorType.Bgra8888 ? 0 : 2;

		int ormROff = workOrm.ColorType == SKColorType.Bgra8888 ? 2 : 0;

		int width = workAlbedo.Width;
		int height = workAlbedo.Height;
		int ormWidth = workOrm.Width;
		int ormHeight = workOrm.Height;

		Parallel.For(0, height, y =>
		{
			int ormY = sameDimensions ? y : Math.Clamp((int)(((y + 0.5f) / height) * ormHeight), 0, ormHeight - 1);
			byte* albRow = albBase + y * albRowBytes;
			byte* ormRow = ormBase + ormY * ormRowBytes;

			ProcessDespillRowUnsafe(albRow, ormRow, width, ormWidth, albROff, albGOff, albBOff, ormROff, sameDimensions, keyUnitVector);
		});
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static unsafe void ProcessDespillRowUnsafe(
		byte* albRow, byte* ormRow,
		int width, int ormWidth,
		int albROff, int albGOff, int albBOff, int ormROff,
		bool sameDimensions, Vector2 keyUnitVector)
	{
		for (int x = 0; x < width; x++)
		{
			int ormX = sameDimensions ? x : Math.Clamp((int)(((x + 0.5f) / width) * ormWidth), 0, ormWidth - 1);
			float mask = ormRow[ormX * 4 + ormROff] / 255.0f;
			if (mask >= 0.999f) continue;

			int albIdx = x * 4;
			byte r = albRow[albIdx + albROff];
			byte g = albRow[albIdx + albGOff];
			byte b = albRow[albIdx + albBOff];

			if (TryDespillPixel(r, g, b, mask, keyUnitVector, out byte newR, out byte newG, out byte newB))
			{
				albRow[albIdx + albROff] = newR;
				albRow[albIdx + albGOff] = newG;
				albRow[albIdx + albBOff] = newB;
			}
		}
	}

	private static void ApplyAnalyticalChromaDespillSafe(SKBitmap workAlbedo, SKBitmap workOrm, Vector2 keyUnitVector, bool sameDimensions)
	{
		int width = workAlbedo.Width;
		int height = workAlbedo.Height;
		int ormWidth = workOrm.Width;
		int ormHeight = workOrm.Height;

		float[] maskValues = new float[width * height];
		for (int y = 0; y < height; y++)
		{
			PopulateMaskRowSafe(workOrm, maskValues, y, width, height, ormWidth, ormHeight, sameDimensions);
		}

		for (int y = 0; y < height; y++)
		{
			ProcessDespillRowSafe(workAlbedo, maskValues, y, width, keyUnitVector);
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static void PopulateMaskRowSafe(
		SKBitmap workOrm, float[] maskValues,
		int y, int width, int height, int ormWidth, int ormHeight,
		bool sameDimensions)
	{
		int ormY = sameDimensions ? y : Math.Clamp((int)(((y + 0.5f) / height) * ormHeight), 0, ormHeight - 1);
		int rowOffset = y * width;

		for (int x = 0; x < width; x++)
		{
			int ormX = sameDimensions ? x : Math.Clamp((int)(((x + 0.5f) / width) * ormWidth), 0, ormWidth - 1);
			maskValues[rowOffset + x] = workOrm.GetPixel(ormX, ormY).Red / 255.0f;
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static void ProcessDespillRowSafe(
		SKBitmap workAlbedo, float[] maskValues,
		int y, int width, Vector2 keyUnitVector)
	{
		int rowOffset = y * width;

		for (int x = 0; x < width; x++)
		{
			float mask = maskValues[rowOffset + x];
			if (mask >= 0.999f) continue;

			var pixel = workAlbedo.GetPixel(x, y);
			if (TryDespillPixel(pixel.Red, pixel.Green, pixel.Blue, mask, keyUnitVector, out byte newR, out byte newG, out byte newB))
			{
				workAlbedo.SetPixel(x, y, new SKColor(newR, newG, newB, pixel.Alpha));
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

		if (maskFactor >= 0.999f || (rByte == gByte && gByte == bByte))
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

		var retainedBvIndices = CollectRetainedBufferViews(accessors, images, bufferViews.Count, albedoImageIndex);

		using var newBinStream = new MemoryStream();
		var oldBvToNewBv = new System.Collections.Generic.Dictionary<int, int>();
		var newBufferViewsList = new JsonArray();

		for (int oldBvIdx = 0; oldBvIdx < bufferViews.Count; oldBvIdx++)
		{
			ProcessRetainedBufferView(oldBvIdx, bufferViews, retainedBvIndices, binChunk, newBinStream, newBufferViewsList, oldBvToNewBv);
		}

		int newAlbedoBvIdx = AppendNewAlbedoBufferView(newBinStream, newAlbedoBytes, newBufferViewsList);

		UpdateAccessorBufferViews(accessors, oldBvToNewBv);
		UpdateImageBufferViews(images, albedoImageIndex, oldBvToNewBv);
		SetAlbedoImageProperties(images, albedoImageIndex, newAlbedoBvIdx);
		EnsureTexturesUseWebp(textures, images.Count);
		EnsureExtensionsUsedContainsWebp(root);

		root["bufferViews"] = newBufferViewsList;

		if (root["buffers"] is JsonArray buffers && buffers.Count > 0 && buffers[0] is JsonObject buf0)
		{
			buf0["byteLength"] = (int)newBinStream.Position;
		}

		byte[] newBin = newBinStream.ToArray();
		return GlbManifestUtils.BuildGlb(root, newBin, glbVersion);
	}

	private static System.Collections.Generic.HashSet<int> CollectRetainedBufferViews(JsonArray accessors, JsonArray images, int bufferViewsCount, int albedoImageIndex)
	{
		var retained = new System.Collections.Generic.HashSet<int>();
		CollectAccessorBufferViews(accessors, bufferViewsCount, retained);
		CollectImageBufferViews(images, bufferViewsCount, albedoImageIndex, retained);
		return retained;
	}

	private static void CollectAccessorBufferViews(JsonArray accessors, int bufferViewsCount, System.Collections.Generic.HashSet<int> retained)
	{
		foreach (var acc in accessors)
		{
			if (acc is JsonObject accObj && accObj.TryGetPropertyValue("bufferView", out var bvVal) && bvVal != null)
			{
				int bvIdx = bvVal.GetValue<int>();
				if (bvIdx >= 0 && bvIdx < bufferViewsCount)
				{
					retained.Add(bvIdx);
				}
			}
		}
	}

	private static void CollectImageBufferViews(JsonArray images, int bufferViewsCount, int albedoImageIndex, System.Collections.Generic.HashSet<int> retained)
	{
		for (int i = 0; i < images.Count; i++)
		{
			if (i == albedoImageIndex) continue;
			if (images[i] is JsonObject imgObj)
			{
				int imgBv = GlbPlayerColorProcessor.GetImageBufferViewIndex(imgObj);
				if (imgBv >= 0 && imgBv < bufferViewsCount)
				{
					retained.Add(imgBv);
				}
			}
		}
	}

	private static void ProcessRetainedBufferView(
		int oldBvIdx, JsonArray bufferViews, System.Collections.Generic.HashSet<int> retainedBvIndices,
		byte[] binChunk, MemoryStream newBinStream, JsonArray newBufferViewsList,
		System.Collections.Generic.Dictionary<int, int> oldBvToNewBv)
	{
		if (!retainedBvIndices.Contains(oldBvIdx) || bufferViews[oldBvIdx] is not JsonObject oldBv)
			return;

		int origOffset = GetIntValueOrDefault(oldBv, "byteOffset");
		int origLength = GetIntValueOrDefault(oldBv, "byteLength");

		AlignStreamTo4Bytes(newBinStream);
		int newOffset = (int)newBinStream.Position;

		CopyBufferViewData(binChunk, origOffset, origLength, newBinStream);

		var clonedBv = (JsonObject)oldBv.DeepClone();
		clonedBv["byteOffset"] = newOffset;
		clonedBv["buffer"] = 0;

		newBufferViewsList.Add(clonedBv);
		oldBvToNewBv[oldBvIdx] = newBufferViewsList.Count - 1;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static int GetIntValueOrDefault(JsonObject obj, string propertyName)
	{
		return obj[propertyName]?.GetValue<int>() ?? 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static void AlignStreamTo4Bytes(MemoryStream stream)
	{
		while ((stream.Position % 4) != 0) stream.WriteByte(0);
	}

	private static void CopyBufferViewData(byte[] binChunk, int origOffset, int origLength, MemoryStream newBinStream)
	{
		if (origOffset + origLength <= binChunk.Length && origLength > 0)
		{
			newBinStream.Write(binChunk, origOffset, origLength);
			AlignStreamTo4Bytes(newBinStream);
		}
	}

	private static int AppendNewAlbedoBufferView(MemoryStream newBinStream, byte[] newAlbedoBytes, JsonArray newBufferViewsList)
	{
		AlignStreamTo4Bytes(newBinStream);
		int albedoOffset = (int)newBinStream.Position;
		
		newBinStream.Write(newAlbedoBytes, 0, newAlbedoBytes.Length);
		AlignStreamTo4Bytes(newBinStream);

		newBufferViewsList.Add(new JsonObject
		{
			["byteOffset"] = albedoOffset,
			["byteLength"] = newAlbedoBytes.Length,
			["buffer"] = 0
		});
		return newBufferViewsList.Count - 1;
	}

	private static void UpdateAccessorBufferViews(JsonArray accessors, System.Collections.Generic.Dictionary<int, int> oldBvToNewBv)
	{
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
	}

	private static void UpdateImageBufferViews(JsonArray images, int albedoImageIndex, System.Collections.Generic.Dictionary<int, int> oldBvToNewBv)
	{
		for (int i = 0; i < images.Count; i++)
		{
			if (i == albedoImageIndex) continue;
			if (images[i] is JsonObject imgObj)
			{
				UpdateSingleImageBufferView(imgObj, oldBvToNewBv);
				UpdateImageWebpExtensionBufferView(imgObj, oldBvToNewBv);
			}
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static void UpdateSingleImageBufferView(JsonObject imgObj, System.Collections.Generic.Dictionary<int, int> oldBvToNewBv)
	{
		if (!imgObj.TryGetPropertyValue("bufferView", out var bvVal) || bvVal == null) return;
		int oldBv = bvVal.GetValue<int>();
		if (oldBvToNewBv.TryGetValue(oldBv, out int newBv))
		{
			imgObj["bufferView"] = newBv;
		}
	}

	private static void UpdateImageWebpExtensionBufferView(JsonObject imgObj, System.Collections.Generic.Dictionary<int, int> oldBvToNewBv)
	{
		if (imgObj["extensions"] is JsonObject imgExt && imgExt["EXT_texture_webp"] is JsonObject webp)
		{
			if (webp.TryGetPropertyValue("bufferView", out var wbVal) && wbVal != null)
			{
				int oldBv = wbVal.GetValue<int>();
				if (oldBvToNewBv.TryGetValue(oldBv, out int newBv))
				{
					webp["bufferView"] = newBv;
				}
			}
		}
	}

	private static void SetAlbedoImageProperties(JsonArray images, int albedoImageIndex, int newAlbedoBvIdx)
	{
		if (albedoImageIndex >= 0 && albedoImageIndex < images.Count && images[albedoImageIndex] is JsonObject albedoImgObj)
		{
			albedoImgObj["bufferView"] = newAlbedoBvIdx;
			albedoImgObj["mimeType"] = "image/webp";
			if (albedoImgObj.ContainsKey("uri")) albedoImgObj.Remove("uri");
			if (albedoImgObj.ContainsKey("extensions")) albedoImgObj.Remove("extensions");
		}
	}

	private static void EnsureTexturesUseWebp(JsonArray textures, int imagesCount)
	{
		for (int i = 0; i < textures.Count; i++)
		{
			if (textures[i] is not JsonObject texObj) continue;
			int src = texObj["source"]?.GetValue<int>() ?? -1;
			if (src < 0)
			{
				src = ResolveMissingTextureSource(i, textures, imagesCount);
				texObj["source"] = src;
			}
			if (src >= 0)
			{
				UpdateTextureWebpExtension(texObj, src);
			}
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static int ResolveMissingTextureSource(int textureIndex, JsonArray textures, int imagesCount)
	{
		int src = GlbPlayerColorProcessor.ResolveTextureToImage(textureIndex, textures);
		if (src >= 0 && src < imagesCount) return src;
		return imagesCount > 0 ? 0 : -1;
	}

	private static void UpdateTextureWebpExtension(JsonObject texObj, int src)
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

	private static void EnsureExtensionsUsedContainsWebp(JsonObject root)
	{
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
	}
}
