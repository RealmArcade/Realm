using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text.Json.Nodes;
using Realm.Shared.Metadata;
using Realm.Shared.ModelOptimization;
using Realm.Shared.Textures;
using SkiaSharp;

namespace Realm.Shared;

public class GlbPlayerColorResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public string? OutputFilePath { get; set; }
    public int MaskedFaceCount { get; set; }
    public int TotalFaceCount { get; set; }
    public string? DetectedChromaKey { get; set; }
}

public class GlbPlayerColorOptions
{
    public string ChromaKey { get; set; } = "#FF00FF";
    public string TargetHex { get => ChromaKey; set => ChromaKey = value; }
    public bool AutoCorrectChromaKey { get; set; } = true;
    public float CoreThreshold { get; set; } = 0.88f;
    public float FringeThreshold { get; set; } = 0.80f;
    public int MinClusterFaces { get; set; } = 10;
    public int DilationRadius { get; set; } = 3;
    public float CreaseAngleDegrees { get; set; } = GlbMeshSmoother.DefaultCreaseAngleDegrees;
}

public static class GlbPlayerColorProcessor
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

    public static bool DetectSupportsTeamColor(ReadOnlySpan<byte> glbOrRmeshBytes)
    {
        if (glbOrRmeshBytes.Length == 0) return false;
        try
        {
            byte[] glbBytes;
            if (RmeshFile.IsRmeshBytes(glbOrRmeshBytes))
            {
                byte[]? extractedGlb = RmeshFile.GetGlbBytes(glbOrRmeshBytes);
                if (extractedGlb == null || extractedGlb.Length == 0) return false;
                glbBytes = extractedGlb;
            }
            else
            {
                glbBytes = glbOrRmeshBytes.ToArray();
            }

            var (jsonNode, binChunk, _) = GlbManifestUtils.ParseGlb(glbBytes);
            if (jsonNode is not JsonObject root || binChunk == null) return false;

            var textures = root["textures"] as JsonArray;
            var materials = root["materials"] as JsonArray;
            var images = root["images"] as JsonArray;
            var bufferViews = root["bufferViews"] as JsonArray;

            if (textures == null || materials == null || images == null || bufferViews == null) return false;

            int ormImageIndex = FindOrmImageIndex(textures, materials);
            if (ormImageIndex < 0) return false;

            byte[] ormRaw = ExtractImageBytes(ormImageIndex, images, bufferViews, binChunk);
            if (ormRaw.Length == 0) return false;

            using var ormImg = SKBitmap.Decode(ormRaw);
            int maskCount = 0;
            int unmaskCount = 0;
            for (int y = 0; y < ormImg.Height; y += 2)
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

            return maskCount > 5 && unmaskCount > 5;
        }
        catch
        {
            return false;
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
                using var importer = new Assimp.AssimpContext();
                var scene = importer.ImportFile(filePath, Assimp.PostProcessSteps.Triangulate | Assimp.PostProcessSteps.GenerateNormals | Assimp.PostProcessSteps.MakeLeftHanded | Assimp.PostProcessSteps.FlipUVs);
                string tempGlb = Path.Combine(Path.GetTempPath(), $"realm_detect_{Guid.NewGuid():N}.glb");
                try
                {
                    importer.ExportFile(scene, tempGlb, "glb2");
                    byte[] bytes = File.ReadAllBytes(tempGlb);
                    return AutoDetectChromaKey(bytes);
                }
                finally
                {
                    if (File.Exists(tempGlb)) try { File.Delete(tempGlb); } catch { }
                }
            }
            byte[] glbBytes = File.ReadAllBytes(filePath);
            return AutoDetectChromaKey(glbBytes);
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
            byte[] glbBytes;
            if (RmeshFile.IsRmeshBytes(glbOrRmeshBytes))
            {
                byte[]? extractedGlb = RmeshFile.GetGlbBytes(glbOrRmeshBytes);
                if (extractedGlb == null || extractedGlb.Length == 0) return null;
                glbBytes = extractedGlb;
            }
            else
            {
                glbBytes = glbOrRmeshBytes.ToArray();
            }

            var (jsonNode, binChunk, _) = GlbManifestUtils.ParseGlb(glbBytes);
            if (jsonNode is not JsonObject root || binChunk == null) return null;

            var textures = root["textures"] as JsonArray;
            var materials = root["materials"] as JsonArray;
            var images = root["images"] as JsonArray;
            var bufferViews = root["bufferViews"] as JsonArray;

            if (textures == null || materials == null || images == null || bufferViews == null) return null;

            int albedoImageIndex = FindAlbedoImageIndex(textures, materials);
            if (albedoImageIndex < 0) return null;

            byte[] albedoRaw = ExtractImageBytes(albedoImageIndex, images, bufferViews, binChunk);
            if (albedoRaw.Length == 0) return null;

            using var albedoImg = SKBitmap.Decode(albedoRaw);
            return AutoDetectChromaKey(albedoImg);
        }
        catch
        {
            return null;
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

    public static string? FindClosestMatchingChromaKey(string filePath, string inputChromaKey)
    {
        if (!File.Exists(filePath)) return null;
        try
        {
            byte[] glbBytes = File.ReadAllBytes(filePath);
            return FindClosestMatchingChromaKey(glbBytes, inputChromaKey);
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
            byte[] glbBytes;
            if (RmeshFile.IsRmeshBytes(glbOrRmeshBytes))
            {
                byte[]? extractedGlb = RmeshFile.GetGlbBytes(glbOrRmeshBytes);
                if (extractedGlb == null || extractedGlb.Length == 0) return null;
                glbBytes = extractedGlb;
            }
            else
            {
                glbBytes = glbOrRmeshBytes.ToArray();
            }

            var (jsonNode, binChunk, _) = GlbManifestUtils.ParseGlb(glbBytes);
            if (jsonNode is not JsonObject root || binChunk == null) return null;

            var textures = root["textures"] as JsonArray;
            var materials = root["materials"] as JsonArray;
            var images = root["images"] as JsonArray;
            var bufferViews = root["bufferViews"] as JsonArray;

            if (textures == null || materials == null || images == null || bufferViews == null) return null;

            int albedoImageIndex = FindAlbedoImageIndex(textures, materials);
            if (albedoImageIndex < 0) return null;

            byte[] albedoRaw = ExtractImageBytes(albedoImageIndex, images, bufferViews, binChunk);
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
            return AutoDetectChromaKey(albedoImg);
        }

        string normalizedInput = inputChromaKey.Trim();
        if (!normalizedInput.StartsWith('#'))
        {
            normalizedInput = "#" + normalizedInput;
        }

        var (targetR, targetG, targetB) = HexToRgb(normalizedInput);
        var (targetL, targetA, targetBComponent) = ConvertRgbToOklab(targetR, targetG, targetB);
        float targetChroma = MathF.Sqrt(targetA * targetA + targetBComponent * targetBComponent);

        if (targetChroma < 0.01f)
        {
            return normalizedInput.ToUpperInvariant();
        }

        float targetHueUnitX = targetA / targetChroma;
        float targetHueUnitY = targetBComponent / targetChroma;

        var candidateColorCounts = new Dictionary<int, int>();
        int totalCandidatePixels = 0;

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
                float pChromaSquared = pA * pA + pB * pB;

                if (pChromaSquared < 0.0036f) continue;
                if (pL < 0.08f || pL > 0.98f) continue;

                float pChroma = MathF.Sqrt(pChromaSquared);
                float hueDot = (pA * targetHueUnitX + pB * targetHueUnitY) / pChroma;

                if (hueDot < 0.75f) continue;

                int rgbKey = (pixel.Red << 16) | (pixel.Green << 8) | pixel.Blue;
                candidateColorCounts[rgbKey] = candidateColorCounts.GetValueOrDefault(rgbKey) + 1;
                totalCandidatePixels++;
            }
        }

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

            byte rByte = (byte)((rgbKey >> 16) & 0xFF);
            byte gByte = (byte)((rgbKey >> 8) & 0xFF);
            byte bByte = (byte)(rgbKey & 0xFF);

            float r = rByte / 255f;
            float g = gByte / 255f;
            float b = bByte / 255f;

            var (pL, pA, pB) = ConvertRgbToOklab(r, g, b);
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

        if (bestRgbKey < 0)
        {
            foreach (var (rgbKey, count) in candidateColorCounts)
            {
                byte rByte = (byte)((rgbKey >> 16) & 0xFF);
                byte gByte = (byte)((rgbKey >> 8) & 0xFF);
                byte bByte = (byte)(rgbKey & 0xFF);

                float r = rByte / 255f;
                float g = gByte / 255f;
                float b = bByte / 255f;

                var (pL, pA, pB) = ConvertRgbToOklab(r, g, b);
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

        if (bestRgbKey < 0)
        {
            return normalizedInput.ToUpperInvariant();
        }

        byte bestR = (byte)((bestRgbKey >> 16) & 0xFF);
        byte bestG = (byte)((bestRgbKey >> 8) & 0xFF);
        byte bestB = (byte)(bestRgbKey & 0xFF);

        return $"#{bestR:X2}{bestG:X2}{bestB:X2}";
    }

    private static string? DetectDominantChromaKeyWithThreshold(SKBitmap albedoImg, float minChroma)
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

        float minChromaSquared = minChroma * minChroma;

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

                float chromaSquared = pA * pA + pB * pB;
                if (chromaSquared < minChromaSquared) continue;
                if (pL < 0.10f || pL > 0.95f) continue;

                int gridX = Math.Clamp((int)((pA - minGridCoord) / cellSize), 0, gridDimension - 1);
                int gridY = Math.Clamp((int)((pB - minGridCoord) / cellSize), 0, gridDimension - 1);
                int binIndex = gridY * gridDimension + gridX;

                float weight = chromaSquared * chromaSquared;

                binTotalWeight[binIndex] += weight;
                binSumLightness[binIndex] += pL * weight;
                binSumA[binIndex] += pA * weight;
                binSumB[binIndex] += pB * weight;
                binPixelCount[binIndex]++;
            }
        }

        float maximumClusterScore = 0f;
        int bestGridX = -1;
        int bestGridY = -1;

        for (int gy = 0; gy < gridDimension; gy++)
        {
            for (int gx = 0; gx < gridDimension; gx++)
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

                if (clusterScore > maximumClusterScore)
                {
                    maximumClusterScore = clusterScore;
                    bestGridX = gx;
                    bestGridY = gy;
                }
            }
        }

        if (maximumClusterScore > 0f && bestGridX >= 0 && bestGridY >= 0)
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
        }

        return null;
    }

    public static GlbPlayerColorResult ProcessFile(
        string inputPath,
        string outputPath,
        GlbPlayerColorOptions options)
    {
        if (!File.Exists(inputPath))
        {
            return new GlbPlayerColorResult
            {
                Success = false,
                ErrorMessage = $"Input file does not exist: {inputPath}"
            };
        }

        try
        {
            byte[] inputBytes = File.ReadAllBytes(inputPath);
            var (success, outputBytes, errorMessage, maskedFaces, totalFaces, detectedKey) = ProcessBytes(inputBytes, options);

            if (!success)
            {
                return new GlbPlayerColorResult { Success = false, ErrorMessage = errorMessage };
            }

            string? dir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllBytes(outputPath, outputBytes!);
            RealmMetadataHelper.SyncBlake3Metadata(outputPath);

            return new GlbPlayerColorResult
            {
                Success = true,
                OutputFilePath = outputPath,
                MaskedFaceCount = maskedFaces,
                TotalFaceCount = totalFaces,
                DetectedChromaKey = detectedKey ?? options.ChromaKey
            };
        }
        catch (Exception ex)
        {
            return new GlbPlayerColorResult { Success = false, ErrorMessage = ex.Message };
        }
    }

    public static (bool Success, byte[]? OutputBytes, string? ErrorMessage, int MaskedFaces, int TotalFaces, string? DetectedChromaKey) ProcessBytes(
        byte[] glbBytes,
        GlbPlayerColorOptions options)
    {
        var (jsonNode, binChunk, glbVersion) = GlbManifestUtils.ParseGlb(glbBytes);

        if (jsonNode is not JsonObject root || binChunk == null)
        {
            return (false, null, "Failed to parse GLB: missing JSON or BIN chunk.", 0, 0, null);
        }

        var meshes = root["meshes"] as JsonArray;
        var accessors = root["accessors"] as JsonArray;
        var bufferViews = root["bufferViews"] as JsonArray;
        var materials = root["materials"] as JsonArray;
        var images = root["images"] as JsonArray;
        var textures = root["textures"] as JsonArray;

        if (meshes == null || accessors == null || bufferViews == null)
        {
            return (false, null, "GLB lacks required mesh/accessor/bufferView data.", 0, 0, null);
        }

        if (images == null || textures == null || materials == null)
        {
            return (false, null, "GLB lacks required image/texture/material data.", 0, 0, null);
        }

        int albedoImageIndex = FindAlbedoImageIndex(textures, materials);
        if (albedoImageIndex < 0)
        {
            return (false, null, "No albedo/base-color texture found in the GLB.", 0, 0, null);
        }

        int ormImageIndex = FindOrmImageIndex(textures, materials);

        byte[] albedoRaw = ExtractImageBytes(albedoImageIndex, images, bufferViews, binChunk);
        if (albedoRaw.Length == 0)
        {
            return (false, null, "Albedo image data could not be extracted from GLB.", 0, 0, null);
        }

        byte[] ormRaw = ormImageIndex >= 0
            ? ExtractImageBytes(ormImageIndex, images, bufferViews, binChunk)
            : Array.Empty<byte>();

        using var albedoImg = SKBitmap.Decode(albedoRaw);
        int texW = albedoImg.Width;
        int texH = albedoImg.Height;

        string effectiveChromaKey = options.ChromaKey;
        if (string.IsNullOrWhiteSpace(effectiveChromaKey) || string.Equals(effectiveChromaKey, "auto", StringComparison.OrdinalIgnoreCase))
        {
            string detectedKey = AutoDetectChromaKey(albedoImg);
            if (options.AutoCorrectChromaKey)
            {
                effectiveChromaKey = FindClosestMatchingChromaKey(albedoImg, detectedKey);
            }
            else
            {
                effectiveChromaKey = detectedKey;
            }
            options.ChromaKey = effectiveChromaKey;
        }
        else if (options.AutoCorrectChromaKey)
        {
            effectiveChromaKey = FindClosestMatchingChromaKey(albedoImg, effectiveChromaKey);
            options.ChromaKey = effectiveChromaKey;
        }

        (float targetR, float targetG, float targetB) = HexToRgb(effectiveChromaKey);
        var (targetLightness, targetA, targetOklabB) = ConvertRgbToOklab(targetR, targetG, targetB);

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

        foreach (var mesh in meshes)
        {
            if (mesh is not JsonObject meshObj) continue;
            if (meshObj["primitives"] is not JsonArray primitives) continue;

            foreach (var prim in primitives)
            {
                if (prim is not JsonObject primObj) continue;

                var faceUvCoords = ExtractFaceUvData(primObj, accessors, bufferViews, binChunk);
                if (faceUvCoords.Count == 0) continue;

                var facePositions = ExtractFacePositionData(primObj, accessors, bufferViews, binChunk);
                if (facePositions.Count == 0) continue;

                totalFaces += faceUvCoords.Count;

                var confirmedFaces = ProcessFacesSurfaceAware(
                    facePositions,
                    faceUvCoords,
                    seedMask,
                    floodFillMask,
                    texW,
                    texH,
                    cosCreaseThreshold,
                    options.MinClusterFaces);

                maskedFaces += confirmedFaces.Count;

                for (int faceIdx = 0; faceIdx < faceUvCoords.Count; faceIdx++)
                {
                    var (uv0, uv1, uv2) = faceUvCoords[faceIdx];
                    int minX = Math.Clamp((int)(Math.Min(Math.Min(uv0.X, uv1.X), uv2.X) * texW), 0, texW - 1);
                    int maxX = Math.Clamp((int)(Math.Ceiling(Math.Max(Math.Max(uv0.X, uv1.X), uv2.X) * texW)), 0, texW - 1);
                    int minY = Math.Clamp((int)(Math.Min(Math.Min(uv0.Y, uv1.Y), uv2.Y) * texH), 0, texH - 1);
                    int maxY = Math.Clamp((int)(Math.Ceiling(Math.Max(Math.Max(uv0.Y, uv1.Y), uv2.Y) * texH)), 0, texH - 1);

                    bool isConfirmed = confirmedFaces.Contains(faceIdx);

                    for (int py = minY; py <= maxY; py++)
                    {
                        for (int px = minX; px <= maxX; px++)
                        {
                            var p = new Vector2(px / (float)texW, py / (float)texH);
                            if (!PointInTriangle(p, uv0, uv1, uv2)) continue;

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
                    }
                }
            }
        }

        globalMask = FeatherInteriorMaskEdges(globalMask, isConfirmedPolygonTexel, texW, texH);

        if (options.DilationRadius > 0)
        {
            globalMask = DilateFloat(globalMask, isNonPlayerFaceTexel, texW, texH, options.DilationRadius);
        }

        ApplyMaskToOrm(ormImg, globalMask, texW, texH);

        byte[] newOrmBytes = TextureConverter.EncodeWebp(ormImg, lossless: true);

        byte[] outputBytes = RebuildGlbWithUpdatedOrmTexture(
            root,
            binChunk,
            ormImageIndex,
            newOrmBytes,
            glbVersion);

        return (true, outputBytes, null, maskedFaces, totalFaces, effectiveChromaKey);
    }

    private static void ScoreTexels(
        SKBitmap albedoImg,
        float targetLightness, float targetA, float targetOklabB,
        bool[] seedMask, bool[] floodFillMask)
    {
        const float seedLightnessDeltaThreshold = 0.18f;
        const float seedChromaticityDistanceThreshold = 0.10f;
        const float seedChromaticityDistanceSquaredThreshold = seedChromaticityDistanceThreshold * seedChromaticityDistanceThreshold;

        const float floodLightnessDeltaThreshold = 0.25f;
        const float floodChromaticityDistanceThreshold = 0.18f;
        const float floodChromaticityDistanceSquaredThreshold = floodChromaticityDistanceThreshold * floodChromaticityDistanceThreshold;
        const float minCandidateChroma = 0.10f;
        const float minCandidateChromaSquared = minCandidateChroma * minCandidateChroma;

        for (int y = 0; y < albedoImg.Height; y++)
        {
            for (int x = 0; x < albedoImg.Width; x++)
            {
                var pixel = albedoImg.GetPixel(x, y);
                float r = pixel.Red / 255f;
                float g = pixel.Green / 255f;
                float bVal = pixel.Blue / 255f;

                var (pL, pA, pB) = ConvertRgbToOklab(r, g, bVal);
                float deltaLightness = MathF.Abs(pL - targetLightness);
                float deltaA = pA - targetA;
                float deltaB = pB - targetOklabB;
                float chromaticityDistanceSquared = deltaA * deltaA + deltaB * deltaB;
                float pixelChromaSquared = pA * pA + pB * pB;

                int idx = y * albedoImg.Width + x;

                bool isSeed = (deltaLightness <= seedLightnessDeltaThreshold) &&
                             (chromaticityDistanceSquared <= seedChromaticityDistanceSquaredThreshold);

                bool isFloodCandidate = (deltaLightness <= floodLightnessDeltaThreshold) &&
                                       (chromaticityDistanceSquared <= floodChromaticityDistanceSquaredThreshold) &&
                                       (pixelChromaSquared >= minCandidateChromaSquared);

                if (isSeed)
                {
                    seedMask[idx] = true;
                    floodFillMask[idx] = true;
                }
                else if (isFloodCandidate)
                {
                    floodFillMask[idx] = true;
                }
            }
        }
    }

    private static HashSet<int> ProcessFacesSurfaceAware(
        List<(Vector3 Pos0, Vector3 Pos1, Vector3 Pos2)> facePositions,
        List<(Vector2 UV0, Vector2 UV1, Vector2 UV2)> faceUvCoords,
        bool[] seedMask,
        bool[] floodFillMask,
        int texW,
        int texH,
        float cosCreaseThreshold,
        int minClusterFaces)
    {
        int faceCount = facePositions.Count;
        if (faceCount == 0) return new HashSet<int>();

        var faceNormals = new Vector3[faceCount];
        for (int i = 0; i < faceCount; i++)
        {
            var (p0, p1, p2) = facePositions[i];
            Vector3 e01 = p1 - p0;
            Vector3 e02 = p2 - p0;
            Vector3 cross = Vector3.Cross(e01, e02);
            float len = cross.Length();
            faceNormals[i] = len > 1e-7f ? (cross / len) : Vector3.UnitY;
        }

        var edgeToFaces = new Dictionary<(long, long), List<int>>();
        for (int faceIdx = 0; faceIdx < faceCount; faceIdx++)
        {
            var (p0, p1, p2) = facePositions[faceIdx];
            long k0 = QuantizePosition(p0);
            long k1 = QuantizePosition(p1);
            long k2 = QuantizePosition(p2);

            AddEdge(edgeToFaces, k0, k1, faceIdx);
            AddEdge(edgeToFaces, k1, k2, faceIdx);
            AddEdge(edgeToFaces, k2, k0, faceIdx);
        }

        var smoothAdjacency = new Dictionary<int, HashSet<int>>();
        for (int i = 0; i < faceCount; i++)
        {
            smoothAdjacency[i] = new HashSet<int>();
        }

        foreach (var faceList in edgeToFaces.Values)
        {
            for (int i = 0; i < faceList.Count; i++)
            {
                for (int j = i + 1; j < faceList.Count; j++)
                {
                    int fa = faceList[i];
                    int fb = faceList[j];
                    if (fa == fb) continue;

                    float dot = Vector3.Dot(faceNormals[fa], faceNormals[fb]);
                    if (dot >= cosCreaseThreshold)
                    {
                        smoothAdjacency[fa].Add(fb);
                        smoothAdjacency[fb].Add(fa);
                    }
                }
            }
        }

        var isSeedFace = new bool[faceCount];
        var isFloodCandidateFace = new bool[faceCount];

        for (int faceIdx = 0; faceIdx < faceCount; faceIdx++)
        {
            var (uv0, uv1, uv2) = faceUvCoords[faceIdx];
            int minX = Math.Clamp((int)(Math.Min(Math.Min(uv0.X, uv1.X), uv2.X) * texW), 0, texW - 1);
            int maxX = Math.Clamp((int)(Math.Ceiling(Math.Max(Math.Max(uv0.X, uv1.X), uv2.X) * texW)), 0, texW - 1);
            int minY = Math.Clamp((int)(Math.Min(Math.Min(uv0.Y, uv1.Y), uv2.Y) * texH), 0, texH - 1);
            int maxY = Math.Clamp((int)(Math.Ceiling(Math.Max(Math.Max(uv0.Y, uv1.Y), uv2.Y) * texH)), 0, texH - 1);

            int seedHits = 0;
            int floodHits = 0;
            int totalSampled = 0;

            for (int py = minY; py <= maxY; py++)
            {
                for (int px = minX; px <= maxX; px++)
                {
                    var p = new Vector2(px / (float)texW, py / (float)texH);
                    if (!PointInTriangle(p, uv0, uv1, uv2)) continue;

                    totalSampled++;
                    int idx = py * texW + px;
                    if (seedMask[idx]) seedHits++;
                    if (floodFillMask[idx]) floodHits++;
                }
            }

            if (totalSampled > 0)
            {
                float seedRatio = (float)seedHits / totalSampled;
                float floodRatio = (float)floodHits / totalSampled;

                isSeedFace[faceIdx] = (seedHits >= 3 && seedRatio >= 0.25f) ||
                                     (seedHits >= 2 && totalSampled <= 4 && seedRatio >= 0.40f);

                isFloodCandidateFace[faceIdx] = (floodHits >= 2 && floodRatio >= 0.20f) ||
                                               (floodHits >= 1 && totalSampled <= 4 && floodRatio >= 0.30f);
            }
            else
            {
                Vector2 centroid = (uv0 + uv1 + uv2) / 3f;
                int cx = Math.Clamp((int)(centroid.X * texW), 0, texW - 1);
                int cy = Math.Clamp((int)(centroid.Y * texH), 0, texH - 1);
                int idx = cy * texW + cx;

                isSeedFace[faceIdx] = seedMask[idx];
                isFloodCandidateFace[faceIdx] = floodFillMask[idx];
            }
        }

        var seedClusterVisited = new bool[faceCount];
        var validatedSeedFaces = new HashSet<int>();

        for (int i = 0; i < faceCount; i++)
        {
            if (!isSeedFace[i] || seedClusterVisited[i]) continue;

            var cluster = new List<int>();
            var queue = new Queue<int>();
            queue.Enqueue(i);
            seedClusterVisited[i] = true;

            while (queue.Count > 0)
            {
                int curr = queue.Dequeue();
                cluster.Add(curr);

                foreach (int neighbor in smoothAdjacency[curr])
                {
                    if (isSeedFace[neighbor] && !seedClusterVisited[neighbor])
                    {
                        seedClusterVisited[neighbor] = true;
                        queue.Enqueue(neighbor);
                    }
                }
            }

            if (cluster.Count >= 3)
            {
                foreach (int f in cluster)
                {
                    validatedSeedFaces.Add(f);
                }
            }
        }

        var visited = new bool[faceCount];
        var floodQueue = new Queue<int>();

        foreach (int f in validatedSeedFaces)
        {
            floodQueue.Enqueue(f);
            visited[f] = true;
        }

        while (floodQueue.Count > 0)
        {
            int current = floodQueue.Dequeue();

            foreach (int neighbor in smoothAdjacency[current])
            {
                if (visited[neighbor]) continue;

                if (isFloodCandidateFace[neighbor])
                {
                    visited[neighbor] = true;
                    floodQueue.Enqueue(neighbor);
                }
            }
        }

        var confirmedFaces = new HashSet<int>();
        var componentVisited = new bool[faceCount];

        for (int i = 0; i < faceCount; i++)
        {
            if (!visited[i] || componentVisited[i]) continue;

            var component = new List<int>();
            var queue = new Queue<int>();
            queue.Enqueue(i);
            componentVisited[i] = true;

            while (queue.Count > 0)
            {
                int curr = queue.Dequeue();
                component.Add(curr);

                foreach (int neighbor in smoothAdjacency[curr])
                {
                    if (visited[neighbor] && !componentVisited[neighbor])
                    {
                        componentVisited[neighbor] = true;
                        queue.Enqueue(neighbor);
                    }
                }
            }

            if (component.Count >= minClusterFaces)
            {
                foreach (int f in component)
                {
                    confirmedFaces.Add(f);
                }
            }
        }

        return confirmedFaces;
    }

    private static float[] FeatherInteriorMaskEdges(float[] mask, bool[] isConfirmedPolygonTexel, int texW, int texH)
    {
        var feathered = new float[mask.Length];
        Array.Copy(mask, feathered, mask.Length);

        for (int y = 0; y < texH; y++)
        {
            for (int x = 0; x < texW; x++)
            {
                int idx = y * texW + x;
                if (!isConfirmedPolygonTexel[idx]) continue;

                int totalNeighborsInside = 0;
                float sumMaskInside = 0f;
                bool hasInteriorUnmaskedNeighbor = false;
                bool hasInteriorMaskedNeighbor = false;

                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        int nx = x + dx;
                        int ny = y + dy;
                        if (nx < 0 || nx >= texW || ny < 0 || ny >= texH) continue;

                        int nIdx = ny * texW + nx;
                        if (isConfirmedPolygonTexel[nIdx])
                        {
                            totalNeighborsInside++;
                            sumMaskInside += mask[nIdx];
                            if (mask[nIdx] < 0.5f) hasInteriorUnmaskedNeighbor = true;
                            if (mask[nIdx] >= 0.5f) hasInteriorMaskedNeighbor = true;
                        }
                    }
                }

                if (mask[idx] >= 0.5f && hasInteriorUnmaskedNeighbor && totalNeighborsInside > 0)
                {
                    float ratio = (sumMaskInside + 1.0f) / (totalNeighborsInside + 1.0f);
                    feathered[idx] = Math.Clamp(0.5f + ratio * 0.5f, 0.4f, 1.0f);
                }
                else if (mask[idx] < 0.5f && hasInteriorMaskedNeighbor && totalNeighborsInside > 0)
                {
                    float ratio = sumMaskInside / (float)totalNeighborsInside;
                    if (ratio > 0.15f)
                    {
                        feathered[idx] = Math.Clamp(ratio * 0.5f, 0.0f, 0.45f);
                    }
                }
            }
        }

        return feathered;
    }

    private static float[] DilateFloat(float[] mask, bool[] isNonPlayerFaceTexel, int texW, int texH, int radius)
    {
        if (radius <= 0) return mask;

        var dilated = new float[mask.Length];
        Array.Copy(mask, dilated, mask.Length);

        for (int y = 0; y < texH; y++)
        {
            for (int x = 0; x < texW; x++)
            {
                float sourceValue = mask[y * texW + x];
                if (sourceValue <= 0f) continue;

                for (int dy = -radius; dy <= radius; dy++)
                {
                    for (int dx = -radius; dx <= radius; dx++)
                    {
                        if (dx * dx + dy * dy > radius * radius) continue;

                        int nx = x + dx;
                        int ny = y + dy;
                        if (nx < 0 || nx >= texW || ny < 0 || ny >= texH) continue;

                        int nIdx = ny * texW + nx;
                        if (isNonPlayerFaceTexel[nIdx]) continue;

                        dilated[nIdx] = Math.Max(dilated[nIdx], sourceValue);
                    }
                }
            }
        }

        return dilated;
    }

    private static bool PointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        float d1 = (p.X - b.X) * (a.Y - b.Y) - (a.X - b.X) * (p.Y - b.Y);
        float d2 = (p.X - c.X) * (b.Y - c.Y) - (b.X - c.X) * (p.Y - c.Y);
        float d3 = (p.X - a.X) * (c.Y - a.Y) - (c.X - a.X) * (p.Y - a.Y);

        bool hasNeg = d1 < 0 || d2 < 0 || d3 < 0;
        bool hasPos = d1 > 0 || d2 > 0 || d3 > 0;

        return !(hasNeg && hasPos);
    }

    private static void AddEdge(Dictionary<(long, long), List<int>> edgeToFaces, long kA, long kB, int faceIdx)
    {
        if (kA == kB) return;
        long minK = kA < kB ? kA : kB;
        long maxK = kA < kB ? kB : kA;
        var key = (minK, maxK);
        if (!edgeToFaces.TryGetValue(key, out var list))
        {
            list = new List<int>(2);
            edgeToFaces[key] = list;
        }
        list.Add(faceIdx);
    }

    private static long QuantizePosition(Vector3 pos)
    {
        const float precision = 10000f;
        long x = (long)(pos.X * precision + 0.5f) & 0xFFFFF;
        long y = (long)(pos.Y * precision + 0.5f) & 0xFFFFF;
        long z = (long)(pos.Z * precision + 0.5f) & 0xFFFFF;
        return x | (y << 20) | (z << 40);
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

    private static (float Lightness, float A, float B) ConvertRgbToOklab(float r, float g, float b)
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

    private static (float R, float G, float B) ConvertOklabToRgb(float lightness, float a, float b)
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

    private static (float Cb, float Cr) HexToBt601CbCr(string hex)
    {
        var (r, g, b) = HexToRgb(hex);
        return RgbToBt601CbCr(r, g, b);
    }

    private static (float Cb, float Cr) RgbToBt601CbCr(float r, float g, float b)
    {
        float cb = -0.16874f * r - 0.33126f * g + 0.50000f * b;
        float cr = 0.50000f * r - 0.41869f * g - 0.08131f * b;
        return (cb, cr);
    }

    private static List<(Vector2 UV0, Vector2 UV1, Vector2 UV2)> ExtractFaceUvData(
        JsonObject primObj,
        JsonArray accessors,
        JsonArray bufferViews,
        byte[] bin)
    {
        var result = new List<(Vector2, Vector2, Vector2)>();

        int uvAccessorIndex = GetAttributeIndex(primObj, "TEXCOORD_0");
        if (uvAccessorIndex < 0 || uvAccessorIndex >= accessors.Count) return result;

        var uvData = ReadAccessorVec2(accessors, bufferViews, bin, uvAccessorIndex);
        if (uvData.Count == 0) return result;

        int indexAccessorIndex = primObj["indices"]?.GetValue<int>() ?? -1;

        if (indexAccessorIndex >= 0 && indexAccessorIndex < accessors.Count)
        {
            var indices = ReadAccessorIndices(accessors, bufferViews, bin, indexAccessorIndex);
            for (int i = 0; i + 2 < indices.Count; i += 3)
            {
                int i0 = indices[i], i1 = indices[i + 1], i2 = indices[i + 2];
                if (i0 < uvData.Count && i1 < uvData.Count && i2 < uvData.Count)
                {
                    result.Add((uvData[i0], uvData[i1], uvData[i2]));
                }
            }
        }
        else
        {
            for (int i = 0; i + 2 < uvData.Count; i += 3)
            {
                result.Add((uvData[i], uvData[i + 1], uvData[i + 2]));
            }
        }

        return result;
    }

    private static List<(Vector3 Pos0, Vector3 Pos1, Vector3 Pos2)> ExtractFacePositionData(
        JsonObject primObj,
        JsonArray accessors,
        JsonArray bufferViews,
        byte[] bin)
    {
        var result = new List<(Vector3, Vector3, Vector3)>();

        int posAccessorIndex = GetAttributeIndex(primObj, "POSITION");
        if (posAccessorIndex < 0 || posAccessorIndex >= accessors.Count) return result;

        var posData = ReadAccessorVec3(accessors, bufferViews, bin, posAccessorIndex);
        if (posData.Count == 0) return result;

        int indexAccessorIndex = primObj["indices"]?.GetValue<int>() ?? -1;

        if (indexAccessorIndex >= 0 && indexAccessorIndex < accessors.Count)
        {
            var indices = ReadAccessorIndices(accessors, bufferViews, bin, indexAccessorIndex);
            for (int i = 0; i + 2 < indices.Count; i += 3)
            {
                int i0 = indices[i], i1 = indices[i + 1], i2 = indices[i + 2];
                if (i0 < posData.Count && i1 < posData.Count && i2 < posData.Count)
                {
                    result.Add((posData[i0], posData[i1], posData[i2]));
                }
            }
        }
        else
        {
            for (int i = 0; i + 2 < posData.Count; i += 3)
            {
                result.Add((posData[i], posData[i + 1], posData[i + 2]));
            }
        }

        return result;
    }

    private static int GetAttributeIndex(JsonObject primObj, string attributeName)
    {
        if (primObj["attributes"] is not JsonObject attributes) return -1;
        if (!attributes.TryGetPropertyValue(attributeName, out var val)) return -1;
        return val?.GetValue<int>() ?? -1;
    }

    private static void ApplyMaskToOrm(SKBitmap ormImg, float[] globalMask, int texW, int texH)
    {
        int ormW = ormImg.Width;
        int ormH = ormImg.Height;

        for (int y = 0; y < ormH; y++)
        {
            for (int x = 0; x < ormW; x++)
            {
                int srcX = Math.Clamp((int)((x / (float)ormW) * texW), 0, texW - 1);
                int srcY = Math.Clamp((int)((y / (float)ormH) * texH), 0, texH - 1);
                float maskValue = globalMask[srcY * texW + srcX];
                byte maskByte = (byte)Math.Clamp((int)(maskValue * 255f + 0.5f), 0, 255);
                var pixel = ormImg.GetPixel(x, y);
                ormImg.SetPixel(x, y, new SKColor(maskByte, pixel.Green, pixel.Blue, pixel.Alpha));
            }
        }
    }

    internal static int FindAlbedoImageIndex(JsonArray textures, JsonArray materials)
    {
        foreach (var mat in materials)
        {
            if (mat is not JsonObject matObj) continue;
            if (matObj["pbrMetallicRoughness"] is not JsonObject pbr) continue;

            int texIdx = GetTextureRefIndex(pbr, "baseColorTexture");
            int imgIdx = ResolveTextureToImage(texIdx, textures);
            if (imgIdx >= 0) return imgIdx;
        }

        return -1;
    }

    internal static int FindOrmImageIndex(JsonArray textures, JsonArray materials)
    {
        foreach (var mat in materials)
        {
            if (mat is not JsonObject matObj) continue;

            if (matObj["pbrMetallicRoughness"] is JsonObject pbr)
            {
                int texIdx = GetTextureRefIndex(pbr, "metallicRoughnessTexture");
                int imgIdx = ResolveTextureToImage(texIdx, textures);
                if (imgIdx >= 0) return imgIdx;
            }

            {
                int texIdx = GetTextureRefIndex(matObj, "occlusionTexture");
                int imgIdx = ResolveTextureToImage(texIdx, textures);
                if (imgIdx >= 0) return imgIdx;
            }
        }

        return -1;
    }

    internal static int GetTextureRefIndex(JsonObject container, string propertyName)
    {
        if (!container.TryGetPropertyValue(propertyName, out var texVal)) return -1;
        if (texVal is not JsonObject texObj) return -1;
        return texObj["index"]?.GetValue<int>() ?? -1;
    }

    internal static int ResolveTextureToImage(int textureIndex, JsonArray textures)
    {
        if (textureIndex < 0 || textureIndex >= textures.Count) return -1;
        if (textures[textureIndex] is not JsonObject texObj) return -1;

        if (texObj["extensions"] is JsonObject texExt)
        {
            if (texExt["EXT_texture_webp"] is JsonObject webpObj)
            {
                int src = webpObj["source"]?.GetValue<int>() ?? -1;
                if (src >= 0) return src;
            }
            if (texExt["KHR_texture_basisu"] is JsonObject basisObj)
            {
                int src = basisObj["source"]?.GetValue<int>() ?? -1;
                if (src >= 0) return src;
            }
        }

        return texObj["source"]?.GetValue<int>() ?? -1;
    }

    internal static int GetImageBufferViewIndex(JsonObject imgObj)
    {
        if (imgObj.TryGetPropertyValue("bufferView", out var bvVal) && bvVal != null)
        {
            return bvVal.GetValue<int>();
        }
        if (imgObj["extensions"] is JsonObject imgExt)
        {
            if (imgExt["EXT_texture_webp"] is JsonObject webp && webp.TryGetPropertyValue("bufferView", out var webpBv) && webpBv != null)
            {
                return webpBv.GetValue<int>();
            }
            if (imgExt["KHR_texture_basisu"] is JsonObject basis && basis.TryGetPropertyValue("bufferView", out var basisBv) && basisBv != null)
            {
                return basisBv.GetValue<int>();
            }
        }
        return -1;
    }

    internal static byte[] ExtractImageBytes(int imageIndex, JsonArray images, JsonArray bufferViews, byte[] bin)
    {
        if (imageIndex < 0 || imageIndex >= images.Count) return Array.Empty<byte>();
        if (images[imageIndex] is not JsonObject imgObj) return Array.Empty<byte>();

        int bvIdx = GetImageBufferViewIndex(imgObj);
        if (bvIdx < 0 || bvIdx >= bufferViews.Count) return Array.Empty<byte>();
        if (bufferViews[bvIdx] is not JsonObject bv) return Array.Empty<byte>();

        int byteOffset = bv["byteOffset"]?.GetValue<int>() ?? 0;
        int byteLength = bv["byteLength"]?.GetValue<int>() ?? 0;
        if (byteOffset + byteLength > bin.Length) return Array.Empty<byte>();

        byte[] result = new byte[byteLength];
        Array.Copy(bin, byteOffset, result, 0, byteLength);
        return result;
    }

    private static SKBitmap CreateDefaultOrm(int width, int height)
    {
        var img = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        img.Erase(new SKColor(0, 255, 0, 255));
        return img;
    }

    private static byte[] RebuildGlbWithUpdatedOrmTexture(
        JsonObject root,
        byte[] binChunk,
        int ormImageIndex,
        byte[] newOrmBytes,
        uint glbVersion)
    {
        var accessors = root["accessors"] as JsonArray ?? new JsonArray();
        var bufferViews = root["bufferViews"] as JsonArray ?? new JsonArray();
        var images = root["images"] as JsonArray ?? new JsonArray();
        var textures = root["textures"] as JsonArray ?? new JsonArray();
        var materials = root["materials"] as JsonArray ?? new JsonArray();

        var retainedBvIndices = new HashSet<int>();
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
            if (i == ormImageIndex)
            {
                continue;
            }

            if (images[i] is JsonObject imgObj)
            {
                int imgBv = GetImageBufferViewIndex(imgObj);
                if (imgBv >= 0 && imgBv < bufferViews.Count)
                {
                    retainedBvIndices.Add(imgBv);
                }
            }
        }

        using var newBinStream = new MemoryStream();
        var oldBvToNewBv = new Dictionary<int, int>();
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
        int ormOffset = (int)newBinStream.Position;
        newBinStream.Write(newOrmBytes, 0, newOrmBytes.Length);
        while ((newBinStream.Position % 4) != 0) newBinStream.WriteByte(0);

        newBufferViewsList.Add(new JsonObject
        {
            ["byteOffset"] = ormOffset,
            ["byteLength"] = newOrmBytes.Length,
            ["buffer"] = 0
        });
        int newOrmBvIdx = newBufferViewsList.Count - 1;

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
            if (i == ormImageIndex) continue;
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

        if (ormImageIndex >= 0 && ormImageIndex < images.Count && images[ormImageIndex] is JsonObject ormImgObj)
        {
            ormImgObj["bufferView"] = newOrmBvIdx;
            ormImgObj["mimeType"] = "image/webp";
            if (ormImgObj.ContainsKey("uri")) ormImgObj.Remove("uri");
            if (ormImgObj.ContainsKey("extensions")) ormImgObj.Remove("extensions");
        }
        else
        {
            int newOrmImageIdx = images.Count;
            images.Add(new JsonObject
            {
                ["mimeType"] = "image/webp",
                ["bufferView"] = newOrmBvIdx
            });

            int newOrmTextureIdx = textures.Count;
            textures.Add(new JsonObject
            {
                ["source"] = newOrmImageIdx,
                ["extensions"] = new JsonObject
                {
                    ["EXT_texture_webp"] = new JsonObject { ["source"] = newOrmImageIdx }
                }
            });

            if (materials.Count > 0 && materials[0] is JsonObject firstMat)
            {
                if (firstMat["pbrMetallicRoughness"] is not JsonObject pbr)
                {
                    pbr = new JsonObject();
                    firstMat["pbrMetallicRoughness"] = pbr;
                }

                if (!pbr.ContainsKey("metallicRoughnessTexture"))
                {
                    pbr["metallicRoughnessTexture"] = new JsonObject { ["index"] = newOrmTextureIdx };
                }
            }
        }

        for (int i = 0; i < textures.Count; i++)
        {
            if (textures[i] is not JsonObject texObj) continue;
            int src = texObj["source"]?.GetValue<int>() ?? -1;
            if (src < 0)
            {
                src = ResolveTextureToImage(i, textures);
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

    private static List<Vector2> ReadAccessorVec2(JsonArray accessors, JsonArray bufferViews, byte[] bin, int accessorIndex)
    {
        var result = new List<Vector2>();
        if (accessorIndex < 0 || accessorIndex >= accessors.Count) return result;
        if (accessors[accessorIndex] is not JsonObject acc) return result;

        int bvIdx = acc["bufferView"]?.GetValue<int>() ?? -1;
        int count = acc["count"]?.GetValue<int>() ?? 0;
        int accessorByteOffset = acc["byteOffset"]?.GetValue<int>() ?? 0;
        int componentType = acc["componentType"]?.GetValue<int>() ?? 5126;

        if (bvIdx < 0 || bvIdx >= bufferViews.Count) return result;
        if (bufferViews[bvIdx] is not JsonObject bv) return result;

        int bvByteOffset = bv["byteOffset"]?.GetValue<int>() ?? 0;
        int bvStride = bv["byteStride"]?.GetValue<int>() ?? 0;

        int elementSize = componentType == 5126 ? 8 : 4;
        int stride = bvStride > 0 ? bvStride : elementSize;
        int baseOffset = bvByteOffset + accessorByteOffset;

        for (int i = 0; i < count; i++)
        {
            int offset = baseOffset + i * stride;
            if (offset + elementSize > bin.Length) break;

            float u, v;
            if (componentType == 5126)
            {
                u = BitConverter.ToSingle(bin, offset);
                v = BitConverter.ToSingle(bin, offset + 4);
            }
            else
            {
                u = BitConverter.ToUInt16(bin, offset) / 65535f;
                v = BitConverter.ToUInt16(bin, offset + 2) / 65535f;
            }

            result.Add(new Vector2(Math.Clamp(u, 0f, 1f), Math.Clamp(v, 0f, 1f)));
        }

        return result;
    }

    private static List<Vector3> ReadAccessorVec3(JsonArray accessors, JsonArray bufferViews, byte[] bin, int accessorIndex)
    {
        var result = new List<Vector3>();
        if (accessorIndex < 0 || accessorIndex >= accessors.Count) return result;
        if (accessors[accessorIndex] is not JsonObject acc) return result;

        int bvIdx = acc["bufferView"]?.GetValue<int>() ?? -1;
        int count = acc["count"]?.GetValue<int>() ?? 0;
        int accessorByteOffset = acc["byteOffset"]?.GetValue<int>() ?? 0;

        if (bvIdx < 0 || bvIdx >= bufferViews.Count) return result;
        if (bufferViews[bvIdx] is not JsonObject bv) return result;

        int bvByteOffset = bv["byteOffset"]?.GetValue<int>() ?? 0;
        int bvStride = bv["byteStride"]?.GetValue<int>() ?? 0;
        int stride = bvStride > 0 ? bvStride : 12;
        int baseOffset = bvByteOffset + accessorByteOffset;

        for (int i = 0; i < count; i++)
        {
            int offset = baseOffset + i * stride;
            if (offset + 12 > bin.Length) break;

            float x = BitConverter.ToSingle(bin, offset);
            float y = BitConverter.ToSingle(bin, offset + 4);
            float z = BitConverter.ToSingle(bin, offset + 8);
            result.Add(new Vector3(x, y, z));
        }

        return result;
    }

    private static List<int> ReadAccessorIndices(JsonArray accessors, JsonArray bufferViews, byte[] bin, int accessorIndex)
    {
        var result = new List<int>();
        if (accessorIndex < 0 || accessorIndex >= accessors.Count) return result;
        if (accessors[accessorIndex] is not JsonObject acc) return result;

        int bvIdx = acc["bufferView"]?.GetValue<int>() ?? -1;
        int count = acc["count"]?.GetValue<int>() ?? 0;
        int accessorByteOffset = acc["byteOffset"]?.GetValue<int>() ?? 0;
        int componentType = acc["componentType"]?.GetValue<int>() ?? 5125;

        if (bvIdx < 0 || bvIdx >= bufferViews.Count) return result;
        if (bufferViews[bvIdx] is not JsonObject bv) return result;

        int bvByteOffset = bv["byteOffset"]?.GetValue<int>() ?? 0;
        int baseOffset = bvByteOffset + accessorByteOffset;

        int elementSize = componentType switch
        {
            5121 => 1,
            5123 => 2,
            _ => 4
        };

        for (int i = 0; i < count; i++)
        {
            int offset = baseOffset + i * elementSize;
            if (offset + elementSize > bin.Length) break;

            int index = componentType switch
            {
                5121 => bin[offset],
                5123 => BitConverter.ToUInt16(bin, offset),
                _ => (int)BitConverter.ToUInt32(bin, offset)
            };

            result.Add(index);
        }

        return result;
    }
}
