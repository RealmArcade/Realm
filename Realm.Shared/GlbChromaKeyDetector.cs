using System.Numerics;
using System.Text.Json.Nodes;
using Assimp;
using Realm.Shared.Metadata;
using Realm.Shared.ModelOptimization;
using Realm.Shared.Textures;
using SkiaSharp;

namespace Realm.Shared;

internal delegate void PixelOklabAction(SKColor pixel, float pL, float pA, float pB);

public static class GlbChromaKeyDetector
{
	internal static byte[]? GetGlbBytesFromSpan(ReadOnlySpan<byte> bytes)
	{
		if (!RmeshFile.IsRmeshBytes(bytes)) return bytes.ToArray();

		byte[]? extractedGlb = RmeshFile.GetGlbBytes(bytes);
		return (extractedGlb == null || extractedGlb.Length == 0) ? null : extractedGlb;
	}

	internal static string? AutoDetectChromaKeyFromModel(string filePath)
	{
		using var importer = new AssimpContext();
		var scene = importer.ImportFile(filePath, PostProcessSteps.Triangulate | PostProcessSteps.GenerateNormals | PostProcessSteps.MakeLeftHanded | PostProcessSteps.FlipUVs);
		string tempGlb = Path.Combine(Path.GetTempPath(), $"realm_detect_{Guid.NewGuid():N}.glb");
		try
		{
			importer.ExportFile(scene, tempGlb, "glb2");
			byte[] bytes = File.ReadAllBytes(tempGlb);
			return AutoDetectChromaKey(bytes);
		}
		finally
		{
			if (File.Exists(tempGlb))
				try
				{
					File.Delete(tempGlb);
				}
				catch
				{
				}
		}
	}

	public static string? AutoDetectChromaKey(string filePath)
	{
		if (!File.Exists(filePath)) return null;
		try
		{
			string ext = Path.GetExtension(filePath).ToLowerInvariant();
			if (ext is ".obj" or ".fbx" or ".dae")
			{
				return AutoDetectChromaKeyFromModel(filePath);
			}

			return AutoDetectChromaKey(File.ReadAllBytes(filePath));
		}
		catch
		{
			return null;
		}
	}

	public static string? AutoDetectChromaKey(ReadOnlySpan<byte> glbOrRmeshBytes)
	{
		if (glbOrRmeshBytes.Length == 0) return null;
		try
		{
			byte[]? glbBytes = GetGlbBytesFromSpan(glbOrRmeshBytes);
			if (glbBytes == null) return null;

			return ExtractAlbedoAndDetectChroma(glbBytes);
		}
		catch
		{
			return null;
		}
	}

	internal static string? ExtractAlbedoAndDetectChroma(byte[] glbBytes)
	{
		var (jsonNode, binChunk, _) = GlbManifestUtils.ParseGlb(glbBytes);
		if (jsonNode is not JsonObject root || binChunk == null) return null;

		var textures = root["textures"] as JsonArray;
		var materials = root["materials"] as JsonArray;
		var images = root["images"] as JsonArray;
		var bufferViews = root["bufferViews"] as JsonArray;

		if (textures == null || materials == null || images == null || bufferViews == null) return null;

		int albedoImageIndex = GlbTextureResolver.FindAlbedoImageIndex(textures, materials);
		if (albedoImageIndex < 0) return null;

		byte[] albedoRaw = GlbTextureResolver.ExtractImageBytes(albedoImageIndex, images, bufferViews, binChunk);
		if (albedoRaw.Length == 0) return null;

		using var albedoImg = SKBitmap.Decode(albedoRaw);
		return AutoDetectChromaKey(albedoImg);
	}

	internal static void IterateOklabPixels(SkiaSharp.SKBitmap albedoImg, PixelOklabAction action)
	{
		for (int y = 0; y < albedoImg.Height; y++)
		{
			for (int x = 0; x < albedoImg.Width; x++)
			{
				var pixel = albedoImg.GetPixel(x, y);
				if (pixel.Alpha < 128) continue;

				float r = pixel.Red / 255f;
				float g = pixel.Green / 255f;
				float b = pixel.Blue / 255f;

				var (pL, pA, pB) = ConvertRgbToOklab(r, g, b);
				action(pixel, pL, pA, pB);
			}
		}
	}

	public static string AutoDetectChromaKey(SKBitmap albedoImg)
	{
		string? result = DetectDominantChromaKeyWithThreshold(albedoImg, 0.15f);
		if (result != null) return result;

		result = DetectDominantChromaKeyWithThreshold(albedoImg, 0.10f);
		if (result != null) return result;

		return "#FF00FF";
	}

	internal static void PopulateChromaBins(
		SKBitmap albedoImg, float minChroma,
		float[] binTotalWeight, float[] binSumLightness,
		float[] binSumA, float[] binSumB, int[] binPixelCount,
		float minGridCoord, float cellSize, int gridDimension)
	{
		float minChromaSquared = minChroma * minChroma;
		IterateOklabPixels(albedoImg, (pixel, pL, pA, pB) =>
		{
			float chromaSquared = pA * pA + pB * pB;
			if (chromaSquared < minChromaSquared) return;
			if (pL < 0.10f || pL > 0.95f) return;

			int gridX = Math.Clamp((int)((pA - minGridCoord) / cellSize), 0, gridDimension - 1);
			int gridY = Math.Clamp((int)((pB - minGridCoord) / cellSize), 0, gridDimension - 1);
			int binIndex = gridY * gridDimension + gridX;

			float weight = chromaSquared * chromaSquared;

			binTotalWeight[binIndex] += weight;
			binSumLightness[binIndex] += pL * weight;
			binSumA[binIndex] += pA * weight;
			binSumB[binIndex] += pB * weight;
			binPixelCount[binIndex]++;
		});
	}

	internal static (int bestGridX, int bestGridY) FindBestChromaCluster(float[] binTotalWeight, int gridDimension)
	{
		float maximumClusterScore = 0f;
		int bestGridX = -1;
		int bestGridY = -1;

		for (int gy = 0; gy < gridDimension; gy++)
		{
			for (int gx = 0; gx < gridDimension; gx++)
			{
				float clusterScore = CalculateClusterScore(gx, gy, binTotalWeight, gridDimension);

				if (clusterScore > maximumClusterScore)
				{
					maximumClusterScore = clusterScore;
					bestGridX = gx;
					bestGridY = gy;
				}
			}
		}

		return (bestGridX, bestGridY);
	}

	internal static float CalculateClusterScore(int gx, int gy, float[] binTotalWeight, int gridDimension)
	{
		float clusterScore = 0f;
		for (int dy = -1; dy <= 1; dy++)
		{
			int ny = gy + dy;
			if (ny < 0 || ny >= gridDimension) continue;

			for (int dx = -1; dx <= 1; dx++)
			{
				int nx = gx + dx;
				if (nx < 0 || nx >= gridDimension) continue;

				clusterScore += binTotalWeight[ny * gridDimension + nx];
			}
		}

		return clusterScore;
	}

	internal static string? ComputeClusterAverageHex(
		int bestGridX, int bestGridY, int gridDimension,
		float[] binTotalWeight, float[] binSumLightness,
		float[] binSumA, float[] binSumB, int[] binPixelCount)
	{
		float totalWindowWeight = 0f;
		float totalWindowLightness = 0f;
		float totalWindowA = 0f;
		float totalWindowB = 0f;
		int totalWindowPixels = 0;

		for (int dy = -1; dy <= 1; dy++)
		{
			int ny = bestGridY + dy;
			if (ny < 0 || ny >= gridDimension) continue;

			for (int dx = -1; dx <= 1; dx++)
			{
				int nx = bestGridX + dx;
				if (nx < 0 || nx >= gridDimension) continue;

				int binIndex = ny * gridDimension + nx;
				float weight = binTotalWeight[binIndex];
				totalWindowWeight += weight;
				totalWindowLightness += binSumLightness[binIndex];
				totalWindowA += binSumA[binIndex];
				totalWindowB += binSumB[binIndex];
				totalWindowPixels += binPixelCount[binIndex];
			}
		}

		if (totalWindowPixels >= 8 && totalWindowWeight > 0f)
		{
			float averageLightness = totalWindowLightness / totalWindowWeight;
			float averageA = totalWindowA / totalWindowWeight;
			float averageB = totalWindowB / totalWindowWeight;

			var (r, g, b) = ConvertOklabToRgb(averageLightness, averageA, averageB);
			byte byteR = (byte)Math.Clamp((int)(r * 255f + 0.5f), 0, 255);
			byte byteG = (byte)Math.Clamp((int)(g * 255f + 0.5f), 0, 255);
			byte byteB = (byte)Math.Clamp((int)(b * 255f + 0.5f), 0, 255);

			return $"#{byteR:X2}{byteG:X2}{byteB:X2}";
		}

		return null;
	}

	internal static string? DetectDominantChromaKeyWithThreshold(SKBitmap albedoImg, float minChroma)
	{
		const int gridDimension = 32;
		const float minGridCoord = -0.40f;
		const float maxGridCoord = +0.40f;
		const float gridRange = maxGridCoord - minGridCoord;
		const float cellSize = gridRange / gridDimension;

		float[] binTotalWeight = new float[gridDimension * gridDimension];
		float[] binSumLightness = new float[gridDimension * gridDimension];
		float[] binSumA = new float[gridDimension * gridDimension];
		float[] binSumB = new float[gridDimension * gridDimension];
		int[] binPixelCount = new int[gridDimension * gridDimension];

		PopulateChromaBins(
			albedoImg, minChroma, binTotalWeight, binSumLightness, binSumA, binSumB, binPixelCount,
			minGridCoord, cellSize, gridDimension);

		var (bestGridX, bestGridY) = FindBestChromaCluster(binTotalWeight, gridDimension);

		if (bestGridX >= 0 && bestGridY >= 0)
		{
			return ComputeClusterAverageHex(
				bestGridX, bestGridY, gridDimension,
				binTotalWeight, binSumLightness, binSumA, binSumB, binPixelCount);
		}

		return null;
	}

	internal static (float Lightness, float A, float B) ConvertRgbToOklab(float r, float g, float b)
	{
		float rLinear = r <= 0.04045f ? r / 12.92f : MathF.Pow((r + 0.055f) / 1.055f, 2.4f);
		float gLinear = g <= 0.04045f ? g / 12.92f : MathF.Pow((g + 0.055f) / 1.055f, 2.4f);
		float bLinear = b <= 0.04045f ? b / 12.92f : MathF.Pow((b + 0.055f) / 1.055f, 2.4f);

		float l = 0.4122214708f * rLinear + 0.5363325363f * gLinear + 0.0514459929f * bLinear;
		float m = 0.2119034982f * rLinear + 0.6806995451f * gLinear + 0.1073969566f * bLinear;
		float s = 0.0883024619f * rLinear + 0.2817188376f * gLinear + 0.6299787005f * bLinear;

		float lRoot = MathF.Cbrt(l);
		float mRoot = MathF.Cbrt(m);
		float sRoot = MathF.Cbrt(s);

		float lightness = 0.2104542553f * lRoot + 0.7936177850f * mRoot - 0.0040720468f * sRoot;
		float a = 1.9779984951f * lRoot - 2.4285922050f * mRoot + 0.4505937099f * sRoot;
		float bComponent = 0.0259040371f * lRoot + 0.7827717662f * mRoot - 0.8086757660f * sRoot;

		return (lightness, a, bComponent);
	}

	internal static (float R, float G, float B) ConvertOklabToRgb(float lightness, float a, float b)
	{
		float lRoot = lightness + 0.3963377774f * a + 0.2158037573f * b;
		float mRoot = lightness - 0.1055613458f * a - 0.0638541728f * b;
		float sRoot = lightness - 0.0894841775f * a - 1.2914855480f * b;

		float l = lRoot * lRoot * lRoot;
		float m = mRoot * mRoot * mRoot;
		float s = sRoot * sRoot * sRoot;

		float rLinear = +4.0767416621f * l - 3.3077115913f * m + 0.2309699292f * s;
		float gLinear = -1.2684380046f * l + 2.6097574011f * m - 0.3413193965f * s;
		float bLinear = -0.0041960863f * l - 0.7034186147f * m + 1.7076147010f * s;

		float r = rLinear <= 0.0031308f ? 12.92f * rLinear : 1.055f * MathF.Pow(Math.Max(0.0f, rLinear), 1.0f / 2.4f) - 0.055f;
		float g = gLinear <= 0.0031308f ? 12.92f * gLinear : 1.055f * MathF.Pow(Math.Max(0.0f, gLinear), 1.0f / 2.4f) - 0.055f;
		float bComponent = bLinear <= 0.0031308f ? 12.92f * bLinear : 1.055f * MathF.Pow(Math.Max(0.0f, bLinear), 1.0f / 2.4f) - 0.055f;

		return (Math.Clamp(r, 0.0f, 1.0f), Math.Clamp(g, 0.0f, 1.0f), Math.Clamp(bComponent, 0.0f, 1.0f));
	}

}
