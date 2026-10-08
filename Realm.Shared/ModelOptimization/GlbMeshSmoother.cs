using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text.Json.Nodes;
using Realm.Shared;

namespace Realm.Shared.ModelOptimization;

public readonly struct SpatialPositionKey : IEquatable<SpatialPositionKey>
{
	public readonly int X;
	public readonly int Y;
	public readonly int Z;

	public SpatialPositionKey(Vector3 position)
	{
		X = (int)MathF.Round(position.X * 10000.0f);
		Y = (int)MathF.Round(position.Y * 10000.0f);
		Z = (int)MathF.Round(position.Z * 10000.0f);
	}

	public bool Equals(SpatialPositionKey other)
	{
		return X == other.X && Y == other.Y && Z == other.Z;
	}

	public override bool Equals(object? obj)
	{
		return obj is SpatialPositionKey other && Equals(other);
	}

	public override int GetHashCode()
	{
		return HashCode.Combine(X, Y, Z);
	}
}

public readonly struct VertexWeldKey : IEquatable<VertexWeldKey>
{
	public readonly int PosX;
	public readonly int PosY;
	public readonly int PosZ;
	public readonly int NormX;
	public readonly int NormY;
	public readonly int NormZ;
	public readonly int UvX;
	public readonly int UvY;
	public readonly int ExtraHash;

	public VertexWeldKey(
		Vector3 position,
		Vector3 normal,
		Vector2 uv0,
		Vector2 uv1,
		Vector4 joints0,
		Vector4 weights0,
		Vector4 color0)
	{
		PosX = (int)MathF.Round(position.X * 10000.0f);
		PosY = (int)MathF.Round(position.Y * 10000.0f);
		PosZ = (int)MathF.Round(position.Z * 10000.0f);

		NormX = (int)MathF.Round(normal.X * 1000.0f);
		NormY = (int)MathF.Round(normal.Y * 1000.0f);
		NormZ = (int)MathF.Round(normal.Z * 1000.0f);

		UvX = (int)MathF.Round(uv0.X * 10000.0f);
		UvY = (int)MathF.Round(uv0.Y * 10000.0f);

		int u1X = (int)MathF.Round(uv1.X * 10000.0f);
		int u1Y = (int)MathF.Round(uv1.Y * 10000.0f);

		int j0 = (int)joints0.X;
		int j1 = (int)joints0.Y;
		int j2 = (int)joints0.Z;
		int j3 = (int)joints0.W;

		int w0 = (int)MathF.Round(weights0.X * 1000.0f);
		int w1 = (int)MathF.Round(weights0.Y * 1000.0f);
		int w2 = (int)MathF.Round(weights0.Z * 1000.0f);
		int w3 = (int)MathF.Round(weights0.W * 1000.0f);

		int cR = (int)MathF.Round(color0.X * 255.0f);
		int cG = (int)MathF.Round(color0.Y * 255.0f);
		int cB = (int)MathF.Round(color0.Z * 255.0f);
		int cA = (int)MathF.Round(color0.W * 255.0f);

		int skinHash = HashCode.Combine(j0, j1, j2, j3, w0, w1, w2, w3);
		int colHash = HashCode.Combine(cR, cG, cB, cA, u1X, u1Y);
		ExtraHash = HashCode.Combine(skinHash, colHash);
	}

	public bool Equals(VertexWeldKey other)
	{
		return PosX == other.PosX && PosY == other.PosY && PosZ == other.PosZ &&
		       NormX == other.NormX && NormY == other.NormY && NormZ == other.NormZ &&
		       UvX == other.UvX && UvY == other.UvY &&
		       ExtraHash == other.ExtraHash;
	}

	public override bool Equals(object? obj)
	{
		return obj is VertexWeldKey other && Equals(other);
	}

	public override int GetHashCode()
	{
		return HashCode.Combine(PosX, PosY, PosZ, NormX, NormY, NormZ, UvX, HashCode.Combine(UvY, ExtraHash));
	}
}

public struct SmoothedVertexData
{
	public Vector3 Position;
	public Vector3 Normal;
	public Vector2 UV0;
	public Vector2 UV1;
	public Vector4 Joints0;
	public Vector4 Weights0;
	public Vector4 Color0;
	public Vector4 Tangent;
}

public static unsafe class GlbMeshSmoother
{
	public const float DefaultCreaseAngleDegrees = 60.0f;

	public static byte[] SmoothMesh(byte[] inputBytes, float creaseAngleDegrees = DefaultCreaseAngleDegrees)
	{
		if (inputBytes == null || inputBytes.Length < 20)
		{
			return inputBytes ?? Array.Empty<byte>();
		}

		if (RmeshFile.IsRmeshBytes(inputBytes))
		{
			var (meta, glbPayload, _) = RmeshFile.Parse(inputBytes);
			byte[] smoothedGlb = SmoothGlbBytes(glbPayload, creaseAngleDegrees);
			return RmeshFile.Build(meta, smoothedGlb);
		}

		return SmoothGlbBytes(inputBytes, creaseAngleDegrees);
	}

	public static bool SmoothMeshFile(string inputPath, string? outputPath = null, float creaseAngleDegrees = DefaultCreaseAngleDegrees)
	{
		if (!File.Exists(inputPath)) return false;

		byte[] inputBytes = File.ReadAllBytes(inputPath);
		byte[] smoothedBytes = SmoothMesh(inputBytes, creaseAngleDegrees);

		string targetPath = string.IsNullOrEmpty(outputPath) ? inputPath : outputPath;
		string? dir = Path.GetDirectoryName(targetPath);
		if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
		{
			Directory.CreateDirectory(dir);
		}

		File.WriteAllBytes(targetPath, smoothedBytes);
		return true;
	}

	private static byte[] SmoothGlbBytes(byte[] glbBytes, float creaseAngleDegrees)
	{
		var (jsonNode, binChunk, glbVersion) = GlbManifestUtils.ParseGlb(glbBytes);
		if (jsonNode is not JsonObject root || binChunk == null) return glbBytes;

		if (root["meshes"] is not JsonArray meshes || meshes.Count == 0 ||
		    root["accessors"] is not JsonArray accessors ||
		    root["bufferViews"] is not JsonArray bufferViews ||
		    root["buffers"] is not JsonArray buffers)
		{
			return glbBytes;
		}

		RemoveMsftLodExtension(root, "extensionsUsed");
		RemoveMsftLodExtension(root, "extensionsRequired");
		CleanNodes(root);

		float cosThreshold = MathF.Cos(creaseAngleDegrees * (MathF.PI / 180.0f));
		var retainedBufferViews = GetRetainedBufferViews(root, bufferViews.Count, accessors);

		using var newBinStream = new MemoryStream();
		var oldBvToNewBvOffset = new Dictionary<int, int>();

		for (int bvIdx = 0; bvIdx < bufferViews.Count; bvIdx++)
		{
			if (!retainedBufferViews.Contains(bvIdx)) continue;
			if (bufferViews[bvIdx] is not JsonObject bv) continue;

			int origOffset = bv["byteOffset"]?.GetValue<int>() ?? 0;
			int origLength = bv["byteLength"]?.GetValue<int>() ?? 0;

			while ((newBinStream.Position % 4) != 0) newBinStream.WriteByte(0);
			int newOffset = (int)newBinStream.Position;
			oldBvToNewBvOffset[bvIdx] = newOffset;

			if (origOffset + origLength <= binChunk.Length)
			{
				newBinStream.Write(binChunk, origOffset, origLength);
				while ((newBinStream.Position % 4) != 0) newBinStream.WriteByte(0);
			}

			bv["byteOffset"] = newOffset;
		}

		for (int m = 0; m < meshes.Count; m++)
		{
			ProcessMesh(meshes[m], accessors, bufferViews, binChunk, cosThreshold, newBinStream);
		}

		while ((newBinStream.Position % 4) != 0) newBinStream.WriteByte(0);

		if (buffers[0] is JsonObject buf0)
		{
			buf0["byteLength"] = (int)newBinStream.Position;
		}

		byte[] newBin = newBinStream.ToArray();
		return GlbManifestUtils.BuildGlb(root, newBin, glbVersion);
	}

	private static void RemoveMsftLodExtension(JsonObject root, string arrayName)
	{
		if (root[arrayName] is not JsonArray extArray) return;
		for (int i = extArray.Count - 1; i >= 0; i--)
		{
			if (extArray[i]?.GetValue<string>() == "MSFT_lod")
			{
				extArray.RemoveAt(i);
			}
		}
	}

	private static void CleanNodes(JsonObject root)
	{
		if (root["nodes"] is not JsonArray nodes) return;
		for (int i = nodes.Count - 1; i >= 0; i--)
		{
			if (nodes[i] is not JsonObject nodeObj) continue;
			string nodeName = nodeObj["name"]?.GetValue<string>() ?? string.Empty;
			
			if (nodeName.EndsWith("_LOD1", StringComparison.OrdinalIgnoreCase) ||
				nodeName.EndsWith("_LOD2", StringComparison.OrdinalIgnoreCase) ||
				nodeName.EndsWith("_LOD3", StringComparison.OrdinalIgnoreCase))
			{
				nodes.RemoveAt(i);
				continue;
			}
			
			if (nodeName.EndsWith("_LOD0", StringComparison.OrdinalIgnoreCase))
			{
				nodeObj["name"] = nodeName.Substring(0, nodeName.Length - 5);
			}
			
			if (nodeObj.TryGetPropertyValue("extensions", out var extNode) && extNode is JsonObject nodeExts)
			{
				nodeExts.Remove("MSFT_lod");
			}
		}
	}

	private static HashSet<int> GetRetainedBufferViews(JsonObject root, int bufferViewCount, JsonArray accessors)
	{
		var retained = new HashSet<int>();

		if (root["images"] is JsonArray images)
		{
			foreach (var img in images)
			{
				if (img is JsonObject imgObj && imgObj.TryGetPropertyValue("bufferView", out var bvVal))
				{
					int bvIdx = bvVal?.GetValue<int>() ?? -1;
					if (bvIdx >= 0 && bvIdx < bufferViewCount) retained.Add(bvIdx);
				}
			}
		}

		if (root["animations"] is JsonArray animations)
		{
			foreach (var anim in animations)
			{
				if (anim is not JsonObject animObj || animObj["samplers"] is not JsonArray samplers) continue;
				foreach (var s in samplers)
				{
					if (s is not JsonObject sampObj) continue;
					AddRetainedAccessor(sampObj, "input", accessors, retained, bufferViewCount);
					AddRetainedAccessor(sampObj, "output", accessors, retained, bufferViewCount);
				}
			}
		}

		if (root["skins"] is JsonArray skins)
		{
			foreach (var skin in skins)
			{
				if (skin is JsonObject skinObj && skinObj.TryGetPropertyValue("inverseBindMatrices", out var ibmVal))
				{
					int aIdx = ibmVal?.GetValue<int>() ?? -1;
					if (aIdx >= 0 && aIdx < accessors.Count && accessors[aIdx] is JsonObject aObj)
					{
						int bv = aObj["bufferView"]?.GetValue<int>() ?? -1;
						if (bv >= 0 && bv < bufferViewCount) retained.Add(bv);
					}
				}
			}
		}

		if (root["meshes"] is JsonArray meshes)
		{
			foreach (var m in meshes)
			{
				if (m is not JsonObject meshObj || meshObj["primitives"] is not JsonArray primitives) continue;
				foreach (var p in primitives)
				{
					if (p is not JsonObject primObj) continue;
					
					if (primObj["attributes"] is JsonObject attrs)
					{
						foreach (var kvp in attrs)
						{
							if (kvp.Value != null)
							{
								int aIdx = kvp.Value.GetValue<int>();
								if (aIdx >= 0 && aIdx < accessors.Count && accessors[aIdx] is JsonObject aObj)
								{
									int bv = aObj["bufferView"]?.GetValue<int>() ?? -1;
									if (bv >= 0 && bv < bufferViewCount) retained.Add(bv);
								}
							}
						}
					}

					if (primObj.TryGetPropertyValue("indices", out var indVal) && indVal != null)
					{
						int aIdx = indVal.GetValue<int>();
						if (aIdx >= 0 && aIdx < accessors.Count && accessors[aIdx] is JsonObject aObj)
						{
							int bv = aObj["bufferView"]?.GetValue<int>() ?? -1;
							if (bv >= 0 && bv < bufferViewCount) retained.Add(bv);
						}
					}
				}
			}
		}

		return retained;
	}

	private static void AddRetainedAccessor(JsonObject obj, string prop, JsonArray accessors, HashSet<int> retained, int bufferViewCount)
	{
		if (!obj.TryGetPropertyValue(prop, out var aVal)) return;
		int aIdx = aVal?.GetValue<int>() ?? -1;
		if (aIdx < 0 || aIdx >= accessors.Count || accessors[aIdx] is not JsonObject aObj) return;
		
		int bv = aObj["bufferView"]?.GetValue<int>() ?? -1;
		if (bv >= 0 && bv < bufferViewCount) retained.Add(bv);
	}

	private static void ProcessMesh(JsonNode? meshNode, JsonArray accessors, JsonArray bufferViews, byte[] binChunk, float cosThreshold, MemoryStream newBinStream)
	{
		if (meshNode is not JsonObject meshObj) return;
		
		string mName = meshObj["name"]?.GetValue<string>() ?? string.Empty;
		if (mName.EndsWith("_LOD0", StringComparison.OrdinalIgnoreCase))
		{
			meshObj["name"] = mName.Substring(0, mName.Length - 5);
		}
		else if (mName.EndsWith("LOD0", StringComparison.OrdinalIgnoreCase))
		{
			meshObj["name"] = mName.Substring(0, mName.Length - 4);
		}
		
		if (meshObj["primitives"] is not JsonArray primitives || primitives.Count == 0) return;

		for (int p = 0; p < primitives.Count; p++)
		{
			ProcessPrimitive(primitives[p], accessors, bufferViews, binChunk, cosThreshold, newBinStream);
		}
	}

	private static void ProcessPrimitive(JsonNode? primNode, JsonArray accessors, JsonArray bufferViews, byte[] binChunk, float cosThreshold, MemoryStream newBinStream)
	{
		if (primNode is not JsonObject primObj) return;

		if (primObj.TryGetPropertyValue("mode", out var modeVal) && modeVal != null && modeVal.GetValue<int>() != 4) return;
		if (primObj.ContainsKey("extensions") && primObj["extensions"] != null) return;
		if (primObj.ContainsKey("targets") && primObj["targets"] != null) return;

		if (primObj["attributes"] is not JsonObject attributes) return;
		if (!attributes.ContainsKey("POSITION")) return;

		int posAccIdx = attributes["POSITION"]!.GetValue<int>();
		var positions = ExtractVector3Array(posAccIdx, accessors, bufferViews, binChunk);
		if (positions == null || positions.Length < 3) return;

		var indices = ExtractIndices(primObj, accessors, bufferViews, binChunk, positions.Length);
		if (indices == null || indices.Length < 3 || indices.Length % 3 != 0) return;

		bool hasUv0 = attributes.ContainsKey("TEXCOORD_0");
		var uvs0 = hasUv0 ? ExtractVector2Array(attributes["TEXCOORD_0"]!.GetValue<int>(), accessors, bufferViews, binChunk) : null;
		hasUv0 = uvs0 != null && uvs0.Length == positions.Length;

		bool hasUv1 = attributes.ContainsKey("TEXCOORD_1");
		var uvs1 = hasUv1 ? ExtractVector2Array(attributes["TEXCOORD_1"]!.GetValue<int>(), accessors, bufferViews, binChunk) : null;
		hasUv1 = uvs1 != null && uvs1.Length == positions.Length;

		bool hasJoints0 = attributes.ContainsKey("JOINTS_0");
		var joints0 = hasJoints0 ? ExtractVector4Array(attributes["JOINTS_0"]!.GetValue<int>(), accessors, bufferViews, binChunk, false) : null;
		hasJoints0 = joints0 != null && joints0.Length == positions.Length;

		bool hasWeights0 = attributes.ContainsKey("WEIGHTS_0");
		var weights0 = hasWeights0 ? ExtractVector4Array(attributes["WEIGHTS_0"]!.GetValue<int>(), accessors, bufferViews, binChunk, true) : null;
		hasWeights0 = weights0 != null && weights0.Length == positions.Length;

		bool hasColor0 = attributes.ContainsKey("COLOR_0");
		var colors0 = hasColor0 ? ExtractVector4Array(attributes["COLOR_0"]!.GetValue<int>(), accessors, bufferViews, binChunk, true) : null;
		hasColor0 = colors0 != null && colors0.Length == positions.Length;

		bool hasTangents = attributes.ContainsKey("TANGENT");

		int triangleCount = indices.Length / 3;
		var faceNormals = new Vector3[triangleCount];
		var cornerWeights = new float[triangleCount, 3];

		ComputeFaceNormalsAndWeights(triangleCount, indices, positions, faceNormals, cornerWeights);

		var spatialPosMap = BuildSpatialPosMap(triangleCount, indices, positions);

		var cornerNormals = ComputeCornerNormals(triangleCount, spatialPosMap, faceNormals, cornerWeights, cosThreshold);

		var uniqueVertexMap = new Dictionary<VertexWeldKey, uint>(positions.Length);
		var weldedVertices = new List<SmoothedVertexData>(positions.Length);
		var weldedIndices = new List<uint>(indices.Length);

		for (int t = 0; t < triangleCount; t++)
		{
			uint orig0 = indices[t * 3];
			uint orig1 = indices[t * 3 + 1];
			uint orig2 = indices[t * 3 + 2];

			if (orig0 >= positions.Length || orig1 >= positions.Length || orig2 >= positions.Length) continue;

			uint c0 = ProcessWeldCorner(
				positions[orig0], cornerNormals[t, 0],
				hasUv0 ? uvs0![orig0] : Vector2.Zero, hasUv1 ? uvs1![orig1] : Vector2.Zero,
				hasJoints0 ? joints0![orig0] : Vector4.Zero, hasWeights0 ? weights0![orig0] : Vector4.Zero,
				hasColor0 ? colors0![orig0] : Vector4.One,
				uniqueVertexMap, weldedVertices);

			uint c1 = ProcessWeldCorner(
				positions[orig1], cornerNormals[t, 1],
				hasUv0 ? uvs0![orig1] : Vector2.Zero, hasUv1 ? uvs1![orig1] : Vector2.Zero,
				hasJoints0 ? joints0![orig1] : Vector4.Zero, hasWeights0 ? weights0![orig1] : Vector4.Zero,
				hasColor0 ? colors0![orig1] : Vector4.One,
				uniqueVertexMap, weldedVertices);

			uint c2 = ProcessWeldCorner(
				positions[orig2], cornerNormals[t, 2],
				hasUv0 ? uvs0![orig2] : Vector2.Zero, hasUv1 ? uvs1![orig2] : Vector2.Zero,
				hasJoints0 ? joints0![orig2] : Vector4.Zero, hasWeights0 ? weights0![orig2] : Vector4.Zero,
				hasColor0 ? colors0![orig2] : Vector4.One,
				uniqueVertexMap, weldedVertices);

			if (c0 != c1 && c1 != c2 && c0 != c2)
			{
				weldedIndices.Add(c0);
				weldedIndices.Add(c1);
				weldedIndices.Add(c2);
			}
		}

		if (weldedVertices.Count == 0 || weldedIndices.Count < 3) return;

		if (hasTangents && hasUv0)
		{
			ComputeWeldedTangents(weldedVertices, weldedIndices);
		}

		OptimizeMeshLayout(weldedVertices, weldedIndices);

		WritePrimitiveToBin(primObj, weldedVertices, weldedIndices, hasUv0, hasUv1, hasJoints0, hasWeights0, hasColor0, hasTangents, newBinStream, bufferViews, accessors);
	}

	private static void ComputeFaceNormalsAndWeights(int triangleCount, uint[] indices, Vector3[] positions, Vector3[] faceNormals, float[,] cornerWeights)
	{
		for (int t = 0; t < triangleCount; t++)
		{
			uint i0 = indices[t * 3];
			uint i1 = indices[t * 3 + 1];
			uint i2 = indices[t * 3 + 2];

			if (i0 >= positions.Length || i1 >= positions.Length || i2 >= positions.Length)
			{
				faceNormals[t] = Vector3.UnitY;
				cornerWeights[t, 0] = 1.0f;
				cornerWeights[t, 1] = 1.0f;
				cornerWeights[t, 2] = 1.0f;
				continue;
			}

			Vector3 p0 = positions[i0];
			Vector3 p1 = positions[i1];
			Vector3 p2 = positions[i2];

			Vector3 e01 = p1 - p0;
			Vector3 e02 = p2 - p0;
			Vector3 e12 = p2 - p1;

			Vector3 cross = Vector3.Cross(e01, e02);
			float crossLen = cross.Length();
			faceNormals[t] = crossLen > 1e-7f ? (cross / crossLen) : Vector3.UnitY;

			float l01 = e01.Length();
			float l02 = e02.Length();
			float l12 = e12.Length();

			float w0 = 1.0f, w1 = 1.0f, w2 = 1.0f;

			if (l01 > 1e-6f && l02 > 1e-6f)
			{
				float dot = Math.Clamp(Vector3.Dot(e01, e02) / (l01 * l02), -1.0f, 1.0f);
				float a = MathF.Acos(dot);
				if (!float.IsNaN(a) && a > 1e-4f) w0 = a;
			}

			if (l01 > 1e-6f && l12 > 1e-6f)
			{
				float dot = Math.Clamp(Vector3.Dot(-e01, e12) / (l01 * l12), -1.0f, 1.0f);
				float a = MathF.Acos(dot);
				if (!float.IsNaN(a) && a > 1e-4f) w1 = a;
			}

			if (l02 > 1e-6f && l12 > 1e-6f)
			{
				float dot = Math.Clamp(Vector3.Dot(-e02, -e12) / (l02 * l12), -1.0f, 1.0f);
				float a = MathF.Acos(dot);
				if (!float.IsNaN(a) && a > 1e-4f) w2 = a;
			}

			cornerWeights[t, 0] = w0;
			cornerWeights[t, 1] = w1;
			cornerWeights[t, 2] = w2;
		}
	}

	private static Dictionary<SpatialPositionKey, List<(int TriIdx, int CornerIdx)>> BuildSpatialPosMap(int triangleCount, uint[] indices, Vector3[] positions)
	{
		var spatialPosMap = new Dictionary<SpatialPositionKey, List<(int TriIdx, int CornerIdx)>>(positions.Length);
		for (int t = 0; t < triangleCount; t++)
		{
			for (int c = 0; c < 3; c++)
			{
				uint origIdx = indices[t * 3 + c];
				if (origIdx >= positions.Length) continue;

				Vector3 pos = positions[origIdx];
				var key = new SpatialPositionKey(pos);
				if (!spatialPosMap.TryGetValue(key, out var list))
				{
					list = new List<(int TriIdx, int CornerIdx)>(4);
					spatialPosMap[key] = list;
				}
				list.Add((t, c));
			}
		}
		return spatialPosMap;
	}

	private static Vector3[,] ComputeCornerNormals(int triangleCount, Dictionary<SpatialPositionKey, List<(int TriIdx, int CornerIdx)>> spatialPosMap, Vector3[] faceNormals, float[,] cornerWeights, float cosThreshold)
	{
		var cornerNormals = new Vector3[triangleCount, 3];
		foreach (var kvp in spatialPosMap)
		{
			var corners = kvp.Value;
			int cornerCount = corners.Count;

			if (cornerCount == 1)
			{
				cornerNormals[corners[0].TriIdx, corners[0].CornerIdx] = faceNormals[corners[0].TriIdx];
				continue;
			}

			for (int i = 0; i < cornerCount; i++)
			{
				var (triA, cornerA) = corners[i];
				Vector3 normA = faceNormals[triA];

				Vector3 accum = Vector3.Zero;
				for (int j = 0; j < cornerCount; j++)
				{
					var (triB, cornerB) = corners[j];
					Vector3 normB = faceNormals[triB];

					float dot = Vector3.Dot(normA, normB);
					if (dot >= cosThreshold)
					{
						float weight = cornerWeights[triB, cornerB];
						accum += normB * weight;
					}
				}

				float len = accum.Length();
				cornerNormals[triA, cornerA] = len > 1e-6f ? (accum / len) : normA;
			}
		}
		return cornerNormals;
	}

	private static uint ProcessWeldCorner(
		Vector3 position,
		Vector3 normal,
		Vector2 uv0,
		Vector2 uv1,
		Vector4 joints0,
		Vector4 weights0,
		Vector4 color0,
		Dictionary<VertexWeldKey, uint> uniqueVertexMap,
		List<SmoothedVertexData> weldedVertices)
	{
		var key = new VertexWeldKey(position, normal, uv0, uv1, joints0, weights0, color0);
		if (!uniqueVertexMap.TryGetValue(key, out uint newIdx))
		{
			newIdx = (uint)weldedVertices.Count;
			weldedVertices.Add(new SmoothedVertexData
			{
				Position = position,
				Normal = normal,
				UV0 = uv0,
				UV1 = uv1,
				Joints0 = joints0,
				Weights0 = weights0,
				Color0 = color0,
				Tangent = Vector4.UnitX
			});
			uniqueVertexMap[key] = newIdx;
		}
		return newIdx;
	}

	private static void ComputeWeldedTangents(List<SmoothedVertexData> vertices, List<uint> indices)
	{
		int count = vertices.Count;
		var tan1 = new Vector3[count];
		var tan2 = new Vector3[count];

		for (int t = 0; t + 2 < indices.Count; t += 3)
		{
			uint i0 = indices[t];
			uint i1 = indices[t + 1];
			uint i2 = indices[t + 2];

			if (i0 >= count || i1 >= count || i2 >= count) continue;

			Vector3 v0 = vertices[(int)i0].Position;
			Vector3 v1 = vertices[(int)i1].Position;
			Vector3 v2 = vertices[(int)i2].Position;

			Vector2 w0 = vertices[(int)i0].UV0;
			Vector2 w1 = vertices[(int)i1].UV0;
			Vector2 w2 = vertices[(int)i2].UV0;

			float x1 = v1.X - v0.X;
			float x2 = v2.X - v0.X;
			float y1 = v1.Y - v0.Y;
			float y2 = v2.Y - v0.Y;
			float z1 = v1.Z - v0.Z;
			float z2 = v2.Z - v0.Z;

			float s1 = w1.X - w0.X;
			float s2 = w2.X - w0.X;
			float t1 = w1.Y - w0.Y;
			float t2 = w2.Y - w0.Y;

			float r = (s1 * t2 - s2 * t1);
			float invR = MathF.Abs(r) > 1e-7f ? 1.0f / r : 0.0f;

			Vector3 sdir = new Vector3((t2 * x1 - t1 * x2) * invR, (t2 * y1 - t1 * y2) * invR, (t2 * z1 - t1 * z2) * invR);
			Vector3 tdir = new Vector3((s1 * x2 - s2 * x1) * invR, (s1 * y2 - s2 * y1) * invR, (s1 * z2 - s2 * z1) * invR);

			tan1[i0] += sdir;
			tan1[i1] += sdir;
			tan1[i2] += sdir;

			tan2[i0] += tdir;
			tan2[i1] += tdir;
			tan2[i2] += tdir;
		}

		for (int a = 0; a < count; a++)
		{
			var v = vertices[a];
			Vector3 n = v.Normal;
			Vector3 t = tan1[a];

			Vector3 tangentVec = t - n * Vector3.Dot(n, t);
			float tanLen = tangentVec.Length();
			if (tanLen > 1e-6f)
			{
				tangentVec /= tanLen;
			}
			else
			{
				tangentVec = Vector3.UnitX;
			}

			float sign = (Vector3.Dot(Vector3.Cross(n, t), tan2[a]) < 0.0f) ? -1.0f : 1.0f;
			v.Tangent = new Vector4(tangentVec.X, tangentVec.Y, tangentVec.Z, sign);
			vertices[a] = v;
		}
	}

	private static void OptimizeMeshLayout(List<SmoothedVertexData> vertices, List<uint> indices)
	{
		int vertexCount = vertices.Count;
		int indexCount = indices.Count;
		if (vertexCount == 0 || indexCount < 3) return;

		var optIndices = indices.ToArray();
		var optPositions = new float[vertexCount * 3];
		for (int i = 0; i < vertexCount; i++)
		{
			optPositions[i * 3] = vertices[i].Position.X;
			optPositions[i * 3 + 1] = vertices[i].Position.Y;
			optPositions[i * 3 + 2] = vertices[i].Position.Z;
		}

		fixed (uint* pIndices = optIndices)
		fixed (float* pPositions = optPositions)
		{
			MeshOptimizerNative.meshopt_optimizeVertexCache(
				pIndices,
				pIndices,
				(nuint)indexCount,
				(nuint)vertexCount);

			MeshOptimizerNative.meshopt_optimizeOverdraw(
				pIndices,
				pIndices,
				(nuint)indexCount,
				pPositions,
				(nuint)vertexCount,
				(nuint)(3 * sizeof(float)),
				1.05f);

			var remap = new uint[vertexCount];
			fixed (uint* pRemap = remap)
			{
				nuint uniqueCount = MeshOptimizerNative.meshopt_optimizeVertexFetchRemap(
					pRemap,
					pIndices,
					(nuint)indexCount,
					(nuint)vertexCount);

				MeshOptimizerNative.meshopt_remapIndexBuffer(
					pIndices,
					pIndices,
					(nuint)indexCount,
					pRemap);

				var remappedVertices = new SmoothedVertexData[uniqueCount];
				for (int i = 0; i < vertexCount; i++)
				{
					uint newIdx = remap[i];
					if (newIdx != 0xFFFFFFFF && newIdx < uniqueCount)
					{
						remappedVertices[newIdx] = vertices[i];
					}
				}

				vertices.Clear();
				vertices.AddRange(remappedVertices);
				indices.Clear();
				indices.AddRange(optIndices);
			}
		}
	}

	private static void WritePrimitiveToBin(
		JsonObject primObj,
		List<SmoothedVertexData> vertices,
		List<uint> indices,
		bool hasUv0,
		bool hasUv1,
		bool hasJoints0,
		bool hasWeights0,
		bool hasColor0,
		bool hasTangents,
		MemoryStream binStream,
		JsonArray bufferViews,
		JsonArray accessors)
	{
		int vertexCount = vertices.Count;
		var attributes = new JsonObject();

		float minX = float.MaxValue, minY = float.MaxValue, minZ = float.MaxValue;
		float maxX = float.MinValue, maxY = float.MinValue, maxZ = float.MinValue;
		byte[] posBytes = new byte[vertexCount * 12];
		for (int i = 0; i < vertexCount; i++)
		{
			var p = vertices[i].Position;
			minX = MathF.Min(minX, p.X);
			minY = MathF.Min(minY, p.Y);
			minZ = MathF.Min(minZ, p.Z);
			maxX = MathF.Max(maxX, p.X);
			maxY = MathF.Max(maxY, p.Y);
			maxZ = MathF.Max(maxZ, p.Z);

			BitConverter.TryWriteBytes(posBytes.AsSpan(i * 12, 4), p.X);
			BitConverter.TryWriteBytes(posBytes.AsSpan(i * 12 + 4, 4), p.Y);
			BitConverter.TryWriteBytes(posBytes.AsSpan(i * 12 + 8, 4), p.Z);
		}
		int posBvIdx = AppendBufferView(binStream, posBytes, 34962, bufferViews);
		int posAccIdx = AppendAccessor(accessors, posBvIdx, 5126, vertexCount, "VEC3",
			new JsonArray(minX, minY, minZ),
			new JsonArray(maxX, maxY, maxZ));
		attributes["POSITION"] = posAccIdx;

		byte[] normBytes = new byte[vertexCount * 12];
		for (int i = 0; i < vertexCount; i++)
		{
			var n = vertices[i].Normal;
			BitConverter.TryWriteBytes(normBytes.AsSpan(i * 12, 4), n.X);
			BitConverter.TryWriteBytes(normBytes.AsSpan(i * 12 + 4, 4), n.Y);
			BitConverter.TryWriteBytes(normBytes.AsSpan(i * 12 + 8, 4), n.Z);
		}
		int normBvIdx = AppendBufferView(binStream, normBytes, 34962, bufferViews);
		int normAccIdx = AppendAccessor(accessors, normBvIdx, 5126, vertexCount, "VEC3");
		attributes["NORMAL"] = normAccIdx;

		if (hasUv0)
		{
			byte[] uv0Bytes = new byte[vertexCount * 8];
			for (int i = 0; i < vertexCount; i++)
			{
				var u = vertices[i].UV0;
				BitConverter.TryWriteBytes(uv0Bytes.AsSpan(i * 8, 4), u.X);
				BitConverter.TryWriteBytes(uv0Bytes.AsSpan(i * 8 + 4, 4), u.Y);
			}
			int uv0BvIdx = AppendBufferView(binStream, uv0Bytes, 34962, bufferViews);
			int uv0AccIdx = AppendAccessor(accessors, uv0BvIdx, 5126, vertexCount, "VEC2");
			attributes["TEXCOORD_0"] = uv0AccIdx;
		}

		if (hasUv1)
		{
			byte[] uv1Bytes = new byte[vertexCount * 8];
			for (int i = 0; i < vertexCount; i++)
			{
				var u = vertices[i].UV1;
				BitConverter.TryWriteBytes(uv1Bytes.AsSpan(i * 8, 4), u.X);
				BitConverter.TryWriteBytes(uv1Bytes.AsSpan(i * 8 + 4, 4), u.Y);
			}
			int uv1BvIdx = AppendBufferView(binStream, uv1Bytes, 34962, bufferViews);
			int uv1AccIdx = AppendAccessor(accessors, uv1BvIdx, 5126, vertexCount, "VEC2");
			attributes["TEXCOORD_1"] = uv1AccIdx;
		}

		if (hasJoints0)
		{
			byte[] jointsBytes = new byte[vertexCount * 8];
			for (int i = 0; i < vertexCount; i++)
			{
				var j = vertices[i].Joints0;
				BitConverter.TryWriteBytes(jointsBytes.AsSpan(i * 8, 2), (ushort)Math.Clamp((int)j.X, 0, 65535));
				BitConverter.TryWriteBytes(jointsBytes.AsSpan(i * 8 + 2, 2), (ushort)Math.Clamp((int)j.Y, 0, 65535));
				BitConverter.TryWriteBytes(jointsBytes.AsSpan(i * 8 + 4, 2), (ushort)Math.Clamp((int)j.Z, 0, 65535));
				BitConverter.TryWriteBytes(jointsBytes.AsSpan(i * 8 + 6, 2), (ushort)Math.Clamp((int)j.W, 0, 65535));
			}
			int jBvIdx = AppendBufferView(binStream, jointsBytes, 34962, bufferViews);
			int jAccIdx = AppendAccessor(accessors, jBvIdx, 5123, vertexCount, "VEC4");
			attributes["JOINTS_0"] = jAccIdx;
		}

		if (hasWeights0)
		{
			byte[] wBytes = new byte[vertexCount * 16];
			for (int i = 0; i < vertexCount; i++)
			{
				var w = vertices[i].Weights0;
				BitConverter.TryWriteBytes(wBytes.AsSpan(i * 16, 4), w.X);
				BitConverter.TryWriteBytes(wBytes.AsSpan(i * 16 + 4, 4), w.Y);
				BitConverter.TryWriteBytes(wBytes.AsSpan(i * 16 + 8, 4), w.Z);
				BitConverter.TryWriteBytes(wBytes.AsSpan(i * 16 + 12, 4), w.W);
			}
			int wBvIdx = AppendBufferView(binStream, wBytes, 34962, bufferViews);
			int wAccIdx = AppendAccessor(accessors, wBvIdx, 5126, vertexCount, "VEC4");
			attributes["WEIGHTS_0"] = wAccIdx;
		}

		if (hasColor0)
		{
			byte[] cBytes = new byte[vertexCount * 16];
			for (int i = 0; i < vertexCount; i++)
			{
				var c = vertices[i].Color0;
				BitConverter.TryWriteBytes(cBytes.AsSpan(i * 16, 4), c.X);
				BitConverter.TryWriteBytes(cBytes.AsSpan(i * 16 + 4, 4), c.Y);
				BitConverter.TryWriteBytes(cBytes.AsSpan(i * 16 + 8, 4), c.Z);
				BitConverter.TryWriteBytes(cBytes.AsSpan(i * 16 + 12, 4), c.W);
			}
			int cBvIdx = AppendBufferView(binStream, cBytes, 34962, bufferViews);
			int cAccIdx = AppendAccessor(accessors, cBvIdx, 5126, vertexCount, "VEC4");
			attributes["COLOR_0"] = cAccIdx;
		}

		if (hasTangents)
		{
			byte[] tanBytes = new byte[vertexCount * 16];
			for (int i = 0; i < vertexCount; i++)
			{
				var t = vertices[i].Tangent;
				BitConverter.TryWriteBytes(tanBytes.AsSpan(i * 16, 4), t.X);
				BitConverter.TryWriteBytes(tanBytes.AsSpan(i * 16 + 4, 4), t.Y);
				BitConverter.TryWriteBytes(tanBytes.AsSpan(i * 16 + 8, 4), t.Z);
				BitConverter.TryWriteBytes(tanBytes.AsSpan(i * 16 + 12, 4), t.W);
			}
			int tanBvIdx = AppendBufferView(binStream, tanBytes, 34962, bufferViews);
			int tanAccIdx = AppendAccessor(accessors, tanBvIdx, 5126, vertexCount, "VEC4");
			attributes["TANGENT"] = tanAccIdx;
		}

		primObj["attributes"] = attributes;

		uint maxIdx = 0;
		for (int i = 0; i < indices.Count; i++)
		{
			if (indices[i] > maxIdx) maxIdx = indices[i];
		}

		bool useShort = maxIdx <= 65535;
		byte[] indBytes = new byte[indices.Count * (useShort ? 2 : 4)];
		for (int i = 0; i < indices.Count; i++)
		{
			if (useShort)
			{
				BitConverter.TryWriteBytes(indBytes.AsSpan(i * 2, 2), (ushort)indices[i]);
			}
			else
			{
				BitConverter.TryWriteBytes(indBytes.AsSpan(i * 4, 4), indices[i]);
			}
		}

		int indBvIdx = AppendBufferView(binStream, indBytes, 34963, bufferViews);
		int indAccIdx = AppendAccessor(accessors, indBvIdx, useShort ? 5123 : 5125, indices.Count, "SCALAR");
		primObj["indices"] = indAccIdx;
	}

	private static int AppendBufferView(MemoryStream stream, byte[] data, int target, JsonArray bufferViews)
	{
		while ((stream.Position % 4) != 0) stream.WriteByte(0);
		int offset = (int)stream.Position;
		stream.Write(data, 0, data.Length);
		while ((stream.Position % 4) != 0) stream.WriteByte(0);

		int bvIdx = bufferViews.Count;
		var bvObj = new JsonObject
		{
			["buffer"] = 0,
			["byteOffset"] = offset,
			["byteLength"] = data.Length,
			["target"] = target
		};
		bufferViews.Add(bvObj);
		return bvIdx;
	}

	private static int AppendAccessor(
		JsonArray accessors,
		int bufferViewIndex,
		int componentType,
		int count,
		string type,
		JsonArray? min = null,
		JsonArray? max = null)
	{
		int accIdx = accessors.Count;
		var accObj = new JsonObject
		{
			["bufferView"] = bufferViewIndex,
			["byteOffset"] = 0,
			["componentType"] = componentType,
			["count"] = count,
			["type"] = type
		};

		if (min != null) accObj["min"] = min;
		if (max != null) accObj["max"] = max;

		accessors.Add(accObj);
		return accIdx;
	}

	private static Vector3[]? ExtractVector3Array(int accessorIndex, JsonArray accessors, JsonArray bufferViews, byte[] binChunk)
	{
		if (accessorIndex < 0 || accessorIndex >= accessors.Count) return null;
		if (accessors[accessorIndex] is not JsonObject acc) return null;
		if (acc.ContainsKey("sparse")) return null;

		string type = acc["type"]?.GetValue<string>() ?? string.Empty;
		int compType = acc["componentType"]?.GetValue<int>() ?? 0;
		if (type != "VEC3" || compType != 5126) return null;

		int count = acc["count"]?.GetValue<int>() ?? 0;
		if (count <= 0) return null;

		int bvIdx = acc["bufferView"]?.GetValue<int>() ?? -1;
		if (bvIdx < 0 || bvIdx >= bufferViews.Count) return null;
		if (bufferViews[bvIdx] is not JsonObject bv) return null;

		int byteOffset = (acc["byteOffset"]?.GetValue<int>() ?? 0) + (bv["byteOffset"]?.GetValue<int>() ?? 0);
		int stride = bv["byteStride"]?.GetValue<int>() ?? 12;
		if (stride < 12) stride = 12;

		if ((long)byteOffset + ((long)count * stride) - (stride - 12) > binChunk.Length) return null;

		var result = new Vector3[count];
		for (int i = 0; i < count; i++)
		{
			int offset = byteOffset + (i * stride);
			result[i] = new Vector3(
				BitConverter.ToSingle(binChunk, offset),
				BitConverter.ToSingle(binChunk, offset + 4),
				BitConverter.ToSingle(binChunk, offset + 8));
		}
		return result;
	}

	private static Vector2[]? ExtractVector2Array(int accessorIndex, JsonArray accessors, JsonArray bufferViews, byte[] binChunk)
	{
		if (accessorIndex < 0 || accessorIndex >= accessors.Count) return null;
		if (accessors[accessorIndex] is not JsonObject acc) return null;
		if (acc.ContainsKey("sparse")) return null;

		string type = acc["type"]?.GetValue<string>() ?? string.Empty;
		int compType = acc["componentType"]?.GetValue<int>() ?? 0;
		if (type != "VEC2") return null;

		int count = acc["count"]?.GetValue<int>() ?? 0;
		if (count <= 0) return null;

		int bvIdx = acc["bufferView"]?.GetValue<int>() ?? -1;
		if (bvIdx < 0 || bvIdx >= bufferViews.Count) return null;
		if (bufferViews[bvIdx] is not JsonObject bv) return null;

		int byteOffset = (acc["byteOffset"]?.GetValue<int>() ?? 0) + (bv["byteOffset"]?.GetValue<int>() ?? 0);
		int elemSize = compType switch
		{
			5126 => 8,
			5123 => 4,
			5121 => 2,
			_ => 0
		};
		if (elemSize == 0) return null;

		int stride = bv["byteStride"]?.GetValue<int>() ?? elemSize;
		if (stride < elemSize) stride = elemSize;

		if ((long)byteOffset + ((long)count * stride) - (stride - elemSize) > binChunk.Length) return null;

		var result = new Vector2[count];
		for (int i = 0; i < count; i++)
		{
			int offset = byteOffset + (i * stride);
			result[i] = ParseVector2(binChunk, offset, compType);
		}
		return result;
	}

	private static Vector2 ParseVector2(byte[] binChunk, int offset, int compType)
	{
		return compType switch
		{
			5126 => new Vector2(
				BitConverter.ToSingle(binChunk, offset),
				BitConverter.ToSingle(binChunk, offset + 4)),
			5123 => new Vector2(
				BitConverter.ToUInt16(binChunk, offset) / 65535.0f,
				BitConverter.ToUInt16(binChunk, offset + 2) / 65535.0f),
			5121 => new Vector2(
				binChunk[offset] / 255.0f,
				binChunk[offset + 1] / 255.0f),
			_ => Vector2.Zero
		};
	}

	private static Vector4[]? ExtractVector4Array(int accessorIndex, JsonArray accessors, JsonArray bufferViews, byte[] binChunk, bool isNormalized)
	{
		if (accessorIndex < 0 || accessorIndex >= accessors.Count) return null;
		if (accessors[accessorIndex] is not JsonObject acc) return null;
		if (acc.ContainsKey("sparse")) return null;

		string type = acc["type"]?.GetValue<string>() ?? string.Empty;
		int compType = acc["componentType"]?.GetValue<int>() ?? 0;
		if (type != "VEC4" && type != "VEC3") return null;

		int count = acc["count"]?.GetValue<int>() ?? 0;
		if (count <= 0) return null;

		int bvIdx = acc["bufferView"]?.GetValue<int>() ?? -1;
		if (bvIdx < 0 || bvIdx >= bufferViews.Count) return null;
		if (bufferViews[bvIdx] is not JsonObject bv) return null;

		int byteOffset = (acc["byteOffset"]?.GetValue<int>() ?? 0) + (bv["byteOffset"]?.GetValue<int>() ?? 0);
		int numComponents = type == "VEC4" ? 4 : 3;
		int bytesPerComp = compType switch
		{
			5126 => 4,
			5125 => 4,
			5123 => 2,
			5121 => 1,
			_ => 0
		};
		if (bytesPerComp == 0) return null;

		int elemSize = numComponents * bytesPerComp;
		int stride = bv["byteStride"]?.GetValue<int>() ?? elemSize;
		if (stride < elemSize) stride = elemSize;

		if ((long)byteOffset + ((long)count * stride) - (stride - elemSize) > binChunk.Length) return null;

		var result = new Vector4[count];
		for (int i = 0; i < count; i++)
		{
			int offset = byteOffset + (i * stride);
			result[i] = ParseVector4(binChunk, offset, numComponents, bytesPerComp, compType, isNormalized);
		}
		return result;
	}

	private static Vector4 ParseVector4(byte[] binChunk, int offset, int numComponents, int bytesPerComp, int compType, bool isNormalized)
	{
		float c0 = ParseComponent(binChunk, offset, compType, isNormalized);
		float c1 = numComponents > 1 ? ParseComponent(binChunk, offset + bytesPerComp, compType, isNormalized) : 0f;
		float c2 = numComponents > 2 ? ParseComponent(binChunk, offset + (2 * bytesPerComp), compType, isNormalized) : 0f;
		float c3 = numComponents > 3 ? ParseComponent(binChunk, offset + (3 * bytesPerComp), compType, isNormalized) : 1f;

		return new Vector4(c0, c1, c2, c3);
	}

	private static float ParseComponent(byte[] binChunk, int cOff, int compType, bool isNormalized)
	{
		return compType switch
		{
			5126 => BitConverter.ToSingle(binChunk, cOff),
			5125 => BitConverter.ToUInt32(binChunk, cOff),
			5123 => isNormalized ? (BitConverter.ToUInt16(binChunk, cOff) / 65535.0f) : BitConverter.ToUInt16(binChunk, cOff),
			5121 => isNormalized ? (binChunk[cOff] / 255.0f) : binChunk[cOff],
			_ => 0f
		};
	}

	private static uint[]? ExtractIndices(JsonObject primObj, JsonArray accessors, JsonArray bufferViews, byte[] binChunk, int vertexCount)
	{
		if (!primObj.ContainsKey("indices") || primObj["indices"] == null)
		{
			if (vertexCount < 3 || vertexCount % 3 != 0) return null;
			var sequentialIndices = new uint[vertexCount];
			for (uint i = 0; i < vertexCount; i++) sequentialIndices[i] = i;
			return sequentialIndices;
		}

		int accIdx = primObj["indices"]!.GetValue<int>();
		if (accIdx < 0 || accIdx >= accessors.Count) return null;
		if (accessors[accIdx] is not JsonObject acc) return null;
		if (acc.ContainsKey("sparse")) return null;

		int count = acc["count"]?.GetValue<int>() ?? 0;
		int compType = acc["componentType"]?.GetValue<int>() ?? 0;
		if (count < 3 || count % 3 != 0) return null;

		int bvIdx = acc["bufferView"]?.GetValue<int>() ?? -1;
		if (bvIdx < 0 || bvIdx >= bufferViews.Count) return null;
		if (bufferViews[bvIdx] is not JsonObject bv) return null;

		int byteOffset = (acc["byteOffset"]?.GetValue<int>() ?? 0) + (bv["byteOffset"]?.GetValue<int>() ?? 0);
		int elemSize = compType switch
		{
			5121 => 1,
			5123 => 2,
			5125 => 4,
			_ => 0
		};
		if (elemSize == 0 || (long)byteOffset + ((long)count * elemSize) > binChunk.Length) return null;

		var indices = new uint[count];
		for (int i = 0; i < count; i++)
		{
			indices[i] = compType switch
			{
				5121 => binChunk[byteOffset + i],
				5123 => BitConverter.ToUInt16(binChunk, byteOffset + (i * 2)),
				5125 => BitConverter.ToUInt32(binChunk, byteOffset + (i * 4)),
				_ => 0
			};
		}
		return indices;
	}
}
