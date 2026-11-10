using System.Collections.Concurrent;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using Realm.Shared.Textures;
using SkiaSharp;

namespace Realm.Shared;

/// <summary>the glb walk that finds the albedo and orm images.</summary>
public static partial class GlbInMemoryColorPreprocessor
	{
	private static byte[] TryProcessGlbCore(byte[] glbBytes, string effectiveChromaKey, ulong cacheKey)
	{
		var (jsonNode, binChunk, glbVersion) = GlbManifestUtils.ParseGlb(glbBytes);
		if (jsonNode is not JsonObject root || binChunk == null) return glbBytes;

		if (!TryGetGlbArrays(root, out var textures, out var materials, out var images, out var bufferViews))
			return glbBytes;

		if (!TryExtractImageBytes(textures, materials, images, bufferViews, binChunk, out int albedoIdx, out byte[] albedoRaw, out byte[] ormRaw))
			return glbBytes;

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

		return ProcessDecodedImages(albedoImg, ormImg, effectiveChromaKey, root, binChunk, albedoIdx, glbVersion, cacheKey);
	}

	private static bool TryExtractImageBytes(
		JsonArray textures, JsonArray materials, JsonArray images, JsonArray bufferViews, byte[] binChunk,
		out int albedoImageIndex, out byte[] albedoRaw, out byte[] ormRaw)
	{
		albedoImageIndex = GlbTextureResolver.FindAlbedoImageIndex(textures, materials);
		int ormImageIndex = GlbTextureResolver.FindOrmImageIndex(textures, materials);

		if (albedoImageIndex < 0 || ormImageIndex < 0)
		{
			albedoRaw = [];
			ormRaw = [];
			return false;
		}

		albedoRaw = GlbTextureResolver.ExtractImageBytes(albedoImageIndex, images, bufferViews, binChunk);
		ormRaw = GlbTextureResolver.ExtractImageBytes(ormImageIndex, images, bufferViews, binChunk);

		return albedoRaw.Length > 0 && ormRaw.Length > 0;
	}
}
