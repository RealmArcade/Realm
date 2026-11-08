using System.Numerics;
using System.Text.Json.Nodes;
using Assimp;
using Realm.Shared.Metadata;
using Realm.Shared.ModelOptimization;
using Realm.Shared.Textures;
using SkiaSharp;

namespace Realm.Shared;

/// <summary>the in-memory entry point and the mask it builds.</summary>
public static partial class GlbPlayerColorProcessor
	{
	public static (bool Success, byte[]? OutputBytes, string? ErrorMessage, int MaskedFaces, int TotalFaces, string? DetectedChromaKey) ProcessBytes(
		byte[] glbBytes,
		GlbPlayerColorOptions options)
	{
		var (jsonNode, binChunk, glbVersion) = GlbManifestUtils.ParseGlb(glbBytes);
		var root = jsonNode as JsonObject;

		if (root == null || binChunk == null || !GlbAccessorReader.TryGetBaseGlbArrays(root, out var textures, out var materials, out var images, out var bufferViews))
		{
			return (false, null, "Failed to parse GLB or missing basic arrays.", 0, 0, null);
		}

		if (!GlbAccessorReader.TryGetMeshArrays(root, out var meshes, out var accessors))
		{
			return (false, null, "GLB lacks required mesh/accessor data.", 0, 0, null);
		}

		int albedoImageIndex = GlbTextureResolver.FindAlbedoImageIndex(textures, materials);
		if (albedoImageIndex < 0)
		{
			return (false, null, "No albedo/base-color texture found in the GLB.", 0, 0, null);
		}

		int ormImageIndex = GlbTextureResolver.FindOrmImageIndex(textures, materials);

		byte[] albedoRaw = GlbTextureResolver.ExtractImageBytes(albedoImageIndex, images, bufferViews, binChunk);
		if (albedoRaw.Length == 0)
		{
			return (false, null, "Albedo image data could not be extracted from GLB.", 0, 0, null);
		}

		byte[] ormRaw = ormImageIndex >= 0
			? GlbTextureResolver.ExtractImageBytes(ormImageIndex, images, bufferViews, binChunk)
			: Array.Empty<byte>();

		using var albedoImg = SKBitmap.Decode(albedoRaw);
		int texW = albedoImg.Width;
		int texH = albedoImg.Height;

		string effectiveChromaKey = ResolveChromaKey(options, albedoImg);

		(float targetR, float targetG, float targetB) = GlbChromaKeyMatcher.HexToRgb(effectiveChromaKey);
		var (targetLightness, targetA, targetOklabB) = GlbChromaKeyDetector.ConvertRgbToOklab(targetR, targetG, targetB);

		using var ormImg = ormRaw.Length > 0
			? SKBitmap.Decode(ormRaw)
			: CreateDefaultOrm(texW, texH);

		var seedMask = new bool[texW * texH];
		var floodFillMask = new bool[texW * texH];
		var globalMask = new float[texW * texH];

		ScoreTexels(
			albedoImg,
			targetLightness, targetA, targetOklabB,
			seedMask, floodFillMask);

		float cosCreaseThreshold = MathF.Cos(options.CreaseAngleDegrees * (MathF.PI / 180.0f));

		int totalFaces = 0;
		int maskedFaces = 0;

		var isConfirmedPolygonTexel = new bool[texW * texH];
		var isNonPlayerFaceTexel = new bool[texW * texH];

		ProcessMeshes(
			meshes, accessors, bufferViews, binChunk, seedMask, floodFillMask, texW, texH, cosCreaseThreshold,
			options, ref totalFaces, ref maskedFaces, isConfirmedPolygonTexel, globalMask, isNonPlayerFaceTexel);

		globalMask = FeatherInteriorMaskEdges(globalMask, isConfirmedPolygonTexel, texW, texH);

		if (options.DilationRadius > 0)
		{
			globalMask = DilateFloat(globalMask, isNonPlayerFaceTexel, texW, texH, options.DilationRadius);
		}

		ApplyMaskToOrm(ormImg, globalMask, texW, texH);

		byte[] newOrmBytes = TextureConverter.EncodeWebp(ormImg, lossless: true);

		byte[] outputBytes = GlbOrmRewriter.RebuildGlbWithUpdatedOrmTexture(
			root,
			binChunk,
			ormImageIndex,
			newOrmBytes,
			glbVersion);

		return (true, outputBytes, null, maskedFaces, totalFaces, effectiveChromaKey);
	}
}
