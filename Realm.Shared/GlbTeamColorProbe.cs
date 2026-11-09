using System.Numerics;
using System.Text.Json.Nodes;
using Assimp;
using Realm.Shared.Metadata;
using Realm.Shared.ModelOptimization;
using Realm.Shared.Textures;
using SkiaSharp;

namespace Realm.Shared;

public static class GlbTeamColorProbe
{
	public static bool DetectSupportsTeamColor(string filePath)
	{
		if (!File.Exists(filePath)) return false;
		try
		{
			byte[] bytes = File.ReadAllBytes(filePath);
			return DetectSupportsTeamColor(bytes);
		}
		catch
		{
			return false;
		}
	}

	private static bool CheckOrmTeamColorMask(SKBitmap ormImg)
	{
		int maskCount = 0;
		int unmaskCount = 0;
		for (int y = 0; y < ormImg.Height; y += 2)
		{
			CountMaskPixelsInRow(ormImg, y, ref maskCount, ref unmaskCount);
		}

		return maskCount > 5 && unmaskCount > 5;
	}

	private static void CountMaskPixelsInRow(SKBitmap ormImg, int y, ref int maskCount, ref int unmaskCount)
	{
		for (int x = 0; x < ormImg.Width; x += 2)
		{
			if (ormImg.GetPixel(x, y).Red > 32)
			{
				maskCount++;
			}
			else
			{
				unmaskCount++;
			}
		}
	}

	public static bool DetectSupportsTeamColor(ReadOnlySpan<byte> glbOrRmeshBytes)
	{
		if (glbOrRmeshBytes.Length == 0) return false;
		try
		{
			byte[]? glbBytes = GlbChromaKeyDetector.GetGlbBytesFromSpan(glbOrRmeshBytes);
			if (glbBytes == null) return false;

			return DetectSupportsTeamColorFromGlb(glbBytes);
		}
		catch
		{
			return false;
		}
	}

	private static bool DetectSupportsTeamColorFromGlb(byte[] glbBytes)
	{
		var (jsonNode, binChunk, _) = GlbManifestUtils.ParseGlb(glbBytes);
		if (jsonNode is not JsonObject root || binChunk == null) return false;

		var textures = root["textures"] as JsonArray;
		var materials = root["materials"] as JsonArray;
		var images = root["images"] as JsonArray;
		var bufferViews = root["bufferViews"] as JsonArray;

		if (textures == null || materials == null || images == null || bufferViews == null) return false;

		int ormImageIndex = GlbTextureResolver.FindOrmImageIndex(textures, materials);
		if (ormImageIndex < 0) return false;

		byte[] ormRaw = GlbTextureResolver.ExtractImageBytes(ormImageIndex, images, bufferViews, binChunk);
		if (ormRaw.Length == 0) return false;

		using var ormImg = SKBitmap.Decode(ormRaw);
		return CheckOrmTeamColorMask(ormImg);
	}
}
