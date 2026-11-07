using System.Numerics;
using System.Text.Json.Nodes;
using Assimp;
using Realm.Shared.Metadata;
using Realm.Shared.ModelOptimization;
using Realm.Shared.Textures;
using SkiaSharp;

namespace Realm.Shared;

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

		int ormImageIndex = FindOrmImageIndex(textures, materials);
		if (ormImageIndex < 0) return false;

		byte[] ormRaw = GlbTextureResolver.ExtractImageBytes(ormImageIndex, images, bufferViews, binChunk);
		if (ormRaw.Length == 0) return false;

		using var ormImg = SKBitmap.Decode(ormRaw);
		return CheckOrmTeamColorMask(ormImg);
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

	private static string ResolveChromaKey(GlbPlayerColorOptions options, SKBitmap albedoImg)
	{
		string effectiveChromaKey = options.ChromaKey;
		if (string.IsNullOrWhiteSpace(effectiveChromaKey) || string.Equals(effectiveChromaKey, "auto", StringComparison.OrdinalIgnoreCase))
		{
			string detectedKey = GlbChromaKeyDetector.AutoDetectChromaKey(albedoImg) ?? "#FF00FF";
			effectiveChromaKey = options.AutoCorrectChromaKey ? GlbChromaKeyMatcher.FindClosestMatchingChromaKey(albedoImg, detectedKey) ?? detectedKey : detectedKey;
			options.ChromaKey = effectiveChromaKey;
		}
		else if (options.AutoCorrectChromaKey)
		{
			effectiveChromaKey = GlbChromaKeyMatcher.FindClosestMatchingChromaKey(albedoImg, effectiveChromaKey) ?? effectiveChromaKey;
			options.ChromaKey = effectiveChromaKey;
		}

		return effectiveChromaKey;
	}

	private static void ProcessRasterizedPixel(
		int px, int py, int texW, int texH,
		Vector2 uv0, Vector2 uv1, Vector2 uv2,
		bool isConfirmed, bool[] floodFillMask, bool[] seedMask,
		bool[] isConfirmedPolygonTexel, float[] globalMask, bool[] isNonPlayerFaceTexel)
	{
		var p = new Vector2(px / (float)texW, py / (float)texH);
		if (!PointInTriangle(p, uv0, uv1, uv2)) return;

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

	private static (int minX, int maxX, int minY, int maxY) GetTriangleBounds(Vector2 uv0, Vector2 uv1, Vector2 uv2, int texW, int texH)
	{
		int minX = Math.Clamp((int)(Math.Min(Math.Min(uv0.X, uv1.X), uv2.X) * texW), 0, texW - 1);
		int maxX = Math.Clamp((int)(Math.Ceiling(Math.Max(Math.Max(uv0.X, uv1.X), uv2.X) * texW)), 0, texW - 1);
		int minY = Math.Clamp((int)(Math.Min(Math.Min(uv0.Y, uv1.Y), uv2.Y) * texH), 0, texH - 1);
		int maxY = Math.Clamp((int)(Math.Ceiling(Math.Max(Math.Max(uv0.Y, uv1.Y), uv2.Y) * texH)), 0, texH - 1);
		return (minX, maxX, minY, maxY);
	}

	private static void RasterizeFace(
		int faceIdx, Vector2 uv0, Vector2 uv1, Vector2 uv2,
		int texW, int texH, bool isConfirmed,
		bool[] floodFillMask, bool[] seedMask,
		bool[] isConfirmedPolygonTexel, float[] globalMask, bool[] isNonPlayerFaceTexel)
	{
		var (minX, maxX, minY, maxY) = GetTriangleBounds(uv0, uv1, uv2, texW, texH);

		for (int py = minY; py <= maxY; py++)
		{
			for (int px = minX; px <= maxX; px++)
			{
				ProcessRasterizedPixel(px, py, texW, texH, uv0, uv1, uv2, isConfirmed, floodFillMask, seedMask, isConfirmedPolygonTexel, globalMask, isNonPlayerFaceTexel);
			}
		}
	}

	private static void ProcessPrimitive(
		JsonObject primObj, JsonArray accessors, JsonArray bufferViews, byte[] binChunk,
		bool[] seedMask, bool[] floodFillMask, int texW, int texH, float cosCreaseThreshold,
		GlbPlayerColorOptions options, ref int totalFaces, ref int maskedFaces,
		bool[] isConfirmedPolygonTexel, float[] globalMask, bool[] isNonPlayerFaceTexel)
	{
		var faceUvCoords = ExtractFaceUvData(primObj, accessors, bufferViews, binChunk);
		if (faceUvCoords.Count == 0) return;

		var facePositions = ExtractFacePositionData(primObj, accessors, bufferViews, binChunk);
		if (facePositions.Count == 0) return;

		totalFaces += faceUvCoords.Count;

		var confirmedFaces = ProcessFacesSurfaceAware(
			facePositions, faceUvCoords, seedMask, floodFillMask, texW, texH, cosCreaseThreshold, options.MinClusterFaces);

		maskedFaces += confirmedFaces.Count;

		for (int faceIdx = 0; faceIdx < faceUvCoords.Count; faceIdx++)
		{
			var (uv0, uv1, uv2) = faceUvCoords[faceIdx];
			bool isConfirmed = confirmedFaces.Contains(faceIdx);
			RasterizeFace(faceIdx, uv0, uv1, uv2, texW, texH, isConfirmed, floodFillMask, seedMask, isConfirmedPolygonTexel, globalMask, isNonPlayerFaceTexel);
		}
	}

	private static void ProcessMeshes(
		JsonArray meshes, JsonArray accessors, JsonArray bufferViews, byte[] binChunk,
		bool[] seedMask, bool[] floodFillMask, int texW, int texH, float cosCreaseThreshold,
		GlbPlayerColorOptions options, ref int totalFaces, ref int maskedFaces,
		bool[] isConfirmedPolygonTexel, float[] globalMask, bool[] isNonPlayerFaceTexel)
	{
		foreach (var mesh in meshes)
		{
			if (mesh is not JsonObject meshObj) continue;
			if (meshObj["primitives"] is not JsonArray primitives) continue;

			foreach (var prim in primitives)
			{
				if (prim is not JsonObject primObj) continue;

				ProcessPrimitive(
					primObj, accessors, bufferViews, binChunk, seedMask, floodFillMask, texW, texH,
					cosCreaseThreshold, options, ref totalFaces, ref maskedFaces,
					isConfirmedPolygonTexel, globalMask, isNonPlayerFaceTexel);
			}
		}
	}

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

		int ormImageIndex = FindOrmImageIndex(textures, materials);

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

	private static void ScoreSingleTexel(
		SKColor pixel, int idx,
		float targetLightness, float targetA, float targetOklabB,
		bool[] seedMask, bool[] floodFillMask)
	{
		const float seedLightnessDeltaThreshold = 0.18f;
		const float seedChromaticityDistanceSquaredThreshold = 0.01f;
		const float floodLightnessDeltaThreshold = 0.25f;
		const float floodChromaticityDistanceSquaredThreshold = 0.0324f;
		const float minCandidateChromaSquared = 0.01f;

		float r = pixel.Red / 255f;
		float g = pixel.Green / 255f;
		float bVal = pixel.Blue / 255f;

		var (pL, pA, pB) = GlbChromaKeyDetector.ConvertRgbToOklab(r, g, bVal);
		float deltaLightness = MathF.Abs(pL - targetLightness);
		float deltaA = pA - targetA;
		float deltaB = pB - targetOklabB;
		float chromaticityDistanceSquared = deltaA * deltaA + deltaB * deltaB;
		float pixelChromaSquared = pA * pA + pB * pB;

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

	private static void ScoreTexels(
		SKBitmap albedoImg,
		float targetLightness, float targetA, float targetOklabB,
		bool[] seedMask, bool[] floodFillMask)
	{
		for (int y = 0; y < albedoImg.Height; y++)
		{
			for (int x = 0; x < albedoImg.Width; x++)
			{
				int idx = y * albedoImg.Width + x;
				ScoreSingleTexel(albedoImg.GetPixel(x, y), idx, targetLightness, targetA, targetOklabB, seedMask, floodFillMask);
			}
		}
	}

	private static void BuildSmoothAdjacency(
		List<(Vector3 Pos0, Vector3 Pos1, Vector3 Pos2)> facePositions,
		int faceCount, float cosCreaseThreshold,
		out Dictionary<int, HashSet<int>> smoothAdjacency)
	{
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

		smoothAdjacency = new Dictionary<int, HashSet<int>>();
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
	}

	private static (int SeedHits, int FloodHits, int TotalSampled) SampleTriangleMasks(Vector2 uv0, Vector2 uv1, Vector2 uv2, bool[] seedMask, bool[] floodFillMask, int texW, int texH)
	{
		var (minX, maxX, minY, maxY) = GetTriangleBounds(uv0, uv1, uv2, texW, texH);

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

		return (seedHits, floodHits, totalSampled);
	}

	private static void EvaluateFaceSeedStatus(
		int faceIdx, Vector2 uv0, Vector2 uv1, Vector2 uv2,
		bool[] seedMask, bool[] floodFillMask, int texW, int texH,
		bool[] isSeedFace, bool[] isFloodCandidateFace)
	{
		var (seedHits, floodHits, totalSampled) = SampleTriangleMasks(uv0, uv1, uv2, seedMask, floodFillMask, texW, texH);

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

	private static void TraverseSeedCluster(int i, bool[] isSeedFace, bool[] seedClusterVisited, Dictionary<int, HashSet<int>> smoothAdjacency, HashSet<int> validatedSeedFaces)
	{
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
				if (!isSeedFace[neighbor] || seedClusterVisited[neighbor]) continue;

				seedClusterVisited[neighbor] = true;
				queue.Enqueue(neighbor);
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

	private static HashSet<int> FindSeedClusters(
		int faceCount, bool[] isSeedFace, Dictionary<int, HashSet<int>> smoothAdjacency)
	{
		var seedClusterVisited = new bool[faceCount];
		var validatedSeedFaces = new HashSet<int>();

		for (int i = 0; i < faceCount; i++)
		{
			if (!isSeedFace[i] || seedClusterVisited[i]) continue;

			TraverseSeedCluster(i, isSeedFace, seedClusterVisited, smoothAdjacency, validatedSeedFaces);
		}

		return validatedSeedFaces;
	}

	private static bool[] RunFloodFill(
		int faceCount, HashSet<int> validatedSeedFaces,
		bool[] isFloodCandidateFace, Dictionary<int, HashSet<int>> smoothAdjacency)
	{
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

		return visited;
	}

	private static void TraverseFloodComponent(int i, bool[] visited, bool[] componentVisited, Dictionary<int, HashSet<int>> smoothAdjacency, HashSet<int> confirmedFaces, int minClusterFaces)
	{
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
				if (!visited[neighbor] || componentVisited[neighbor]) continue;

				componentVisited[neighbor] = true;
				queue.Enqueue(neighbor);
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

	private static HashSet<int> ExtractFloodComponents(
		int faceCount, bool[] visited, Dictionary<int, HashSet<int>> smoothAdjacency, int minClusterFaces)
	{
		var confirmedFaces = new HashSet<int>();
		var componentVisited = new bool[faceCount];

		for (int i = 0; i < faceCount; i++)
		{
			if (!visited[i] || componentVisited[i]) continue;

			TraverseFloodComponent(i, visited, componentVisited, smoothAdjacency, confirmedFaces, minClusterFaces);
		}

		return confirmedFaces;
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

		BuildSmoothAdjacency(facePositions, faceCount, cosCreaseThreshold, out var smoothAdjacency);

		var isSeedFace = new bool[faceCount];
		var isFloodCandidateFace = new bool[faceCount];

		for (int faceIdx = 0; faceIdx < faceCount; faceIdx++)
		{
			var (uv0, uv1, uv2) = faceUvCoords[faceIdx];
			EvaluateFaceSeedStatus(faceIdx, uv0, uv1, uv2, seedMask, floodFillMask, texW, texH, isSeedFace, isFloodCandidateFace);
		}

		var validatedSeedFaces = FindSeedClusters(faceCount, isSeedFace, smoothAdjacency);
		var visited = RunFloodFill(faceCount, validatedSeedFaces, isFloodCandidateFace, smoothAdjacency);
		return ExtractFloodComponents(faceCount, visited, smoothAdjacency, minClusterFaces);
	}

	private static void ProcessFeatherNeighbor(
		int dx, int dy, int x, int y, int texW, int texH,
		float[] mask, bool[] isConfirmedPolygonTexel,
		ref int totalNeighborsInside, ref float sumMaskInside,
		ref bool hasInteriorUnmaskedNeighbor, ref bool hasInteriorMaskedNeighbor)
	{
		if (dx == 0 && dy == 0) return;
		int nx = x + dx;
		int ny = y + dy;
		if (nx < 0 || nx >= texW || ny < 0 || ny >= texH) return;

		int nIdx = ny * texW + nx;
		if (isConfirmedPolygonTexel[nIdx])
		{
			totalNeighborsInside++;
			sumMaskInside += mask[nIdx];
			if (mask[nIdx] < 0.5f) hasInteriorUnmaskedNeighbor = true;
			if (mask[nIdx] >= 0.5f) hasInteriorMaskedNeighbor = true;
		}
	}

	private static void CalculateFeatheredValue(
		int x, int y, int idx, float[] mask, float[] feathered,
		bool[] isConfirmedPolygonTexel, int texW, int texH)
	{
		int totalNeighborsInside = 0;
		float sumMaskInside = 0f;
		bool hasInteriorUnmaskedNeighbor = false;
		bool hasInteriorMaskedNeighbor = false;

		for (int dy = -1; dy <= 1; dy++)
		{
			for (int dx = -1; dx <= 1; dx++)
			{
				ProcessFeatherNeighbor(dx, dy, x, y, texW, texH, mask, isConfirmedPolygonTexel, ref totalNeighborsInside, ref sumMaskInside, ref hasInteriorUnmaskedNeighbor, ref hasInteriorMaskedNeighbor);
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

				CalculateFeatheredValue(x, y, idx, mask, feathered, isConfirmedPolygonTexel, texW, texH);
			}
		}

		return feathered;
	}

	private static void ApplyDilationKernel(
		int x, int y, float sourceValue, float[] dilated, bool[] isNonPlayerFaceTexel,
		int texW, int texH, int radius)
	{
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

				ApplyDilationKernel(x, y, sourceValue, dilated, isNonPlayerFaceTexel, texW, texH, radius);
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

	private static (float Cb, float Cr) HexToBt601CbCr(string hex)
	{
		var (r, g, b) = GlbChromaKeyMatcher.HexToRgb(hex);
		return RgbToBt601CbCr(r, g, b);
	}

	private static (float Cb, float Cr) RgbToBt601CbCr(float r, float g, float b)
	{
		float cb = -0.16874f * r - 0.33126f * g + 0.50000f * b;
		float cr = 0.50000f * r - 0.41869f * g - 0.08131f * b;
		return (cb, cr);
	}

	private static void AppendIndexedFaces<T>(List<(T, T, T)> result, List<T> data, List<int> indices)
	{
		for (int i = 0; i + 2 < indices.Count; i += 3)
		{
			int i0 = indices[i], i1 = indices[i + 1], i2 = indices[i + 2];
			if (i0 < data.Count && i1 < data.Count && i2 < data.Count)
			{
				result.Add((data[i0], data[i1], data[i2]));
			}
		}
	}

	private static void AppendSequentialFaces<T>(List<(T, T, T)> result, List<T> data)
	{
		for (int i = 0; i + 2 < data.Count; i += 3)
		{
			result.Add((data[i], data[i + 1], data[i + 2]));
		}
	}

	private static List<(Vector2 UV0, Vector2 UV1, Vector2 UV2)> ExtractFaceUvData(
		JsonObject primObj,
		JsonArray accessors,
		JsonArray bufferViews,
		byte[] bin)
	{
		var result = new List<(Vector2, Vector2, Vector2)>();

		int uvAccessorIndex = GlbAccessorReader.GetAttributeIndex(primObj, "TEXCOORD_0");
		if (uvAccessorIndex < 0 || uvAccessorIndex >= accessors.Count) return result;

		var uvData = GlbAccessorReader.ReadAccessorVec2(accessors, bufferViews, bin, uvAccessorIndex);
		if (uvData.Count == 0) return result;

		int indexAccessorIndex = primObj["indices"]?.GetValue<int>() ?? -1;

		if (indexAccessorIndex >= 0 && indexAccessorIndex < accessors.Count)
		{
			var indices = GlbAccessorReader.ReadAccessorIndices(accessors, bufferViews, bin, indexAccessorIndex);
			AppendIndexedFaces(result, uvData, indices);
		}
		else
		{
			AppendSequentialFaces(result, uvData);
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

		int posAccessorIndex = GlbAccessorReader.GetAttributeIndex(primObj, "POSITION");
		if (posAccessorIndex < 0 || posAccessorIndex >= accessors.Count) return result;

		var posData = GlbAccessorReader.ReadAccessorVec3(accessors, bufferViews, bin, posAccessorIndex);
		if (posData.Count == 0) return result;

		int indexAccessorIndex = primObj["indices"]?.GetValue<int>() ?? -1;

		if (indexAccessorIndex >= 0 && indexAccessorIndex < accessors.Count)
		{
			var indices = GlbAccessorReader.ReadAccessorIndices(accessors, bufferViews, bin, indexAccessorIndex);
			AppendIndexedFaces(result, posData, indices);
		}
		else
		{
			AppendSequentialFaces(result, posData);
		}

		return result;
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

	public static int FindOrmImageIndex(JsonArray textures, JsonArray materials)
	{
		foreach (var mat in materials)
		{
			if (mat is not JsonObject matObj) continue;

			if (matObj["pbrMetallicRoughness"] is JsonObject pbr)
			{
				int texIdx = GlbTextureResolver.GetTextureRefIndex(pbr, "metallicRoughnessTexture");
				int imgIdx = GlbTextureResolver.ResolveTextureToImage(texIdx, textures);
				if (imgIdx >= 0) return imgIdx;
			}

			{
				int texIdx = GlbTextureResolver.GetTextureRefIndex(matObj, "occlusionTexture");
				int imgIdx = GlbTextureResolver.ResolveTextureToImage(texIdx, textures);
				if (imgIdx >= 0) return imgIdx;
			}
		}

		return -1;
	}

	private static SKBitmap CreateDefaultOrm(int width, int height)
	{
		var img = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
		img.Erase(new SKColor(0, 255, 0, 255));
		return img;
	}

}