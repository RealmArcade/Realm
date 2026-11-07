using System.Numerics;
using System.Text.Json.Nodes;
using Assimp;
using Realm.Shared.Metadata;
using Realm.Shared.ModelOptimization;
using Realm.Shared.Textures;
using SkiaSharp;

namespace Realm.Shared;

public static class GlbChromaKeyMatcher
{
	internal static int ProcessCandidatePixelsForChromaKey(SKBitmap albedoImg, float targetHueUnitX, float targetHueUnitY, Dictionary<int, int> candidateColorCounts)
	{
		int totalCandidatePixels = 0;
		GlbChromaKeyDetector.IterateOklabPixels(albedoImg, (pixel, pL, pA, pB) =>
		{
			float pChromaSquared = pA * pA + pB * pB;

			if (pChromaSquared < 0.0036f || pL < 0.08f || pL > 0.98f) return;

			float pChroma = MathF.Sqrt(pChromaSquared);
			float hueDot = (pA * targetHueUnitX + pB * targetHueUnitY) / pChroma;

			if (hueDot < 0.75f) return;

			int rgbKey = (pixel.Red << 16) | (pixel.Green << 8) | pixel.Blue;
			candidateColorCounts[rgbKey] = candidateColorCounts.GetValueOrDefault(rgbKey) + 1;
			totalCandidatePixels++;
		});

		return totalCandidatePixels;
	}

	public static string? FindClosestMatchingChromaKey(string filePath, string inputChromaKey)
	{
		if (!File.Exists(filePath)) return null;
		try
		{
			return FindClosestMatchingChromaKey(File.ReadAllBytes(filePath), inputChromaKey);
		}
		catch
		{
			return null;
		}
	}

	public static string? FindClosestMatchingChromaKey(ReadOnlySpan<byte> glbOrRmeshBytes, string inputChromaKey)
	{
		if (glbOrRmeshBytes.Length == 0) return null;
		try
		{
			byte[]? glbBytes = GlbChromaKeyDetector.GetGlbBytesFromSpan(glbOrRmeshBytes);
			if (glbBytes == null) return null;

			var (jsonNode, binChunk, _) = GlbManifestUtils.ParseGlb(glbBytes);
			if (jsonNode is not JsonObject root || binChunk == null) return null;

			if (!GlbAccessorReader.TryGetBaseGlbArrays(root, out var textures, out var materials, out var images, out var bufferViews))
			{
				return null;
			}

			int albedoImageIndex = GlbTextureResolver.FindAlbedoImageIndex(textures, materials);
			if (albedoImageIndex < 0) return null;

			byte[] albedoRaw = GlbTextureResolver.ExtractImageBytes(albedoImageIndex, images, bufferViews, binChunk);
			if (albedoRaw.Length == 0) return null;

			using var albedoImg = SKBitmap.Decode(albedoRaw);
			return FindClosestMatchingChromaKey(albedoImg, inputChromaKey);
		}
		catch
		{
			return null;
		}
	}

	public static string FindClosestMatchingChromaKey(SKBitmap albedoImg, string inputChromaKey)
	{
		if (string.IsNullOrWhiteSpace(inputChromaKey) || string.Equals(inputChromaKey, "auto", StringComparison.OrdinalIgnoreCase))
		{
			return GlbChromaKeyDetector.AutoDetectChromaKey(albedoImg);
		}

		string normalizedInput = inputChromaKey.Trim();
		if (!normalizedInput.StartsWith('#'))
		{
			normalizedInput = "#" + normalizedInput;
		}

		var (targetR, targetG, targetB) = HexToRgb(normalizedInput);
		var (targetL, targetA, targetBComponent) = GlbChromaKeyDetector.ConvertRgbToOklab(targetR, targetG, targetB);
		float targetChroma = MathF.Sqrt(targetA * targetA + targetBComponent * targetBComponent);

		if (targetChroma < 0.01f)
		{
			return normalizedInput.ToUpperInvariant();
		}

		float targetHueUnitX = targetA / targetChroma;
		float targetHueUnitY = targetBComponent / targetChroma;

		var candidateColorCounts = new Dictionary<int, int>();
		int totalCandidatePixels = ProcessCandidatePixelsForChromaKey(albedoImg, targetHueUnitX, targetHueUnitY, candidateColorCounts);

		if (candidateColorCounts.Count == 0)
		{
			return normalizedInput.ToUpperInvariant();
		}

		int minPixelCount = Math.Max(1, Math.Min(4, totalCandidatePixels / 500));

		float bestScore = -1f;
		int bestRgbKey = -1;

		foreach (var (rgbKey, count) in candidateColorCounts)
		{
			if (count < minPixelCount && candidateColorCounts.Count > 1)
			{
				continue;
			}

			CalculateAndCheckScore(rgbKey, count, targetHueUnitX, targetHueUnitY, ref bestScore, ref bestRgbKey);
		}

		if (bestRgbKey < 0)
		{
			foreach (var (rgbKey, count) in candidateColorCounts)
			{
				CalculateAndCheckScore(rgbKey, count, targetHueUnitX, targetHueUnitY, ref bestScore, ref bestRgbKey);
			}
		}

		if (bestRgbKey < 0)
		{
			return normalizedInput.ToUpperInvariant();
		}

		byte bestR = (byte)((bestRgbKey >> 16) & 0xFF);
		byte bestG = (byte)((bestRgbKey >> 8) & 0xFF);
		byte bestB = (byte)(bestRgbKey & 0xFF);

		return $"#{bestR:X2}{bestG:X2}{bestB:X2}";
	}

	public static (float R, float G, float B) HexToRgb(string hex)
	{
		hex = hex.TrimStart('#');
		if (hex.Length == 3)
		{
			hex = $"{hex[0]}{hex[0]}{hex[1]}{hex[1]}{hex[2]}{hex[2]}";
		}

		if (hex.Length < 6)
		{
			return (1f, 0f, 1f);
		}

		try
		{
			byte r = Convert.ToByte(hex[..2], 16);
			byte g = Convert.ToByte(hex[2..4], 16);
			byte b = Convert.ToByte(hex[4..6], 16);
			return (r / 255f, g / 255f, b / 255f);
		}
		catch
		{
			return (1f, 0f, 1f);
		}
	}

	internal static void CalculateAndCheckScore(int rgbKey, int count, float targetHueUnitX, float targetHueUnitY, ref float bestScore, ref int bestRgbKey)
	{
		byte rByte = (byte)((rgbKey >> 16) & 0xFF);
		byte gByte = (byte)((rgbKey >> 8) & 0xFF);
		byte bByte = (byte)(rgbKey & 0xFF);

		float r = rByte / 255f;
		float g = gByte / 255f;
		float b = bByte / 255f;

		var (pL, pA, pB) = GlbChromaKeyDetector.ConvertRgbToOklab(r, g, b);
		float pChroma = MathF.Sqrt(pA * pA + pB * pB);
		float hueDot = (pA * targetHueUnitX + pB * targetHueUnitY) / pChroma;

		float hueFactor = MathF.Pow(MathF.Max(0f, hueDot), 2f);
		float score = (pL * pL) * pChroma * hueFactor * MathF.Log2(1 + count);

		if (score > bestScore)
		{
			bestScore = score;
			bestRgbKey = rgbKey;
		}
	}
}
