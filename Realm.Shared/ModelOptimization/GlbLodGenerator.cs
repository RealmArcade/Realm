using System.Buffers.Binary;
using System.Text.Json.Nodes;

namespace Realm.Shared.ModelOptimization;

public static unsafe class GlbLodGenerator
{
	public static readonly float[] DefaultLodRatios = new float[] { 1.0f, 0.50f, 0.25f, 0.10f };
	public static readonly float[] DefaultVisibilityBegins = new float[] { 0f, 45f, 90f, 150f };
	public static readonly float[] DefaultVisibilityEnds = new float[] { 45f, 90f, 150f, 0f };

	public static (bool Success, byte[] OutputGlbBytes, string ErrorMessage) GenerateLods(
		byte[] inputGlbBytes,
		float[]? lodRatios = null,
		float[]? visBegins = null,
		float[]? visEnds = null)
	{
		if (inputGlbBytes == null || inputGlbBytes.Length < 20)
			return (false, inputGlbBytes ?? Array.Empty<byte>(), "Invalid GLB buffer");

		if (!AreLodParametersValid(lodRatios, visBegins, visEnds))
		{
			lodRatios = DefaultLodRatios;
			visBegins = DefaultVisibilityBegins;
			visEnds = DefaultVisibilityEnds;
		}

		try
		{
			return ProcessGlb(inputGlbBytes, lodRatios!, visBegins!, visEnds!);
		}
		catch (Exception ex)
		{
			return (false, inputGlbBytes, $"Failed to generate LODs: {ex.Message}");
		}
	}

	private static bool AreLodParametersValid(float[]? lodRatios, float[]? visBegins, float[]? visEnds)
	{
		if (lodRatios == null || visBegins == null || visEnds == null) return false;
		if (lodRatios.Length < 2 || visBegins.Length != lodRatios.Length || visEnds.Length != lodRatios.Length) return false;
		return IsDecreasingRatios(lodRatios);
	}

	private static (bool, byte[], string) ProcessGlb(byte[] input, float[] ratios, float[] visBegins, float[] visEnds)
	{
		var (jsonNode, binBytes, glbVer) = GlbManifestUtils.ParseGlb(input);
		if (jsonNode is not JsonObject root)
			return (false, input, "Failed to parse glTF JSON root");

		CleanEarlyExtensions(root);
		CleanEarlyNodes(root);

		if (!HasRequiredGlbArrays(root))
			return (true, input, string.Empty);

		binBytes ??= Array.Empty<byte>();
		using var newBinStream = new MemoryStream();
		newBinStream.Write(binBytes, 0, binBytes.Length);

		var meshLodMap = ProcessMeshes(root, ratios, binBytes, newBinStream);

		AlignStream(newBinStream);
		byte[] finalBinBytes = newBinStream.ToArray();

		UpdateBuffersLength(root, finalBinBytes.Length);
		UpdateSceneNodesWithLods(root, meshLodMap, visBegins, visEnds);
		SanityCheckFinalScenes(root);

		byte[] outputGlb = GlbManifestUtils.BuildGlb(root, finalBinBytes, glbVer);
		return (true, outputGlb, string.Empty);
	}

	private static void CleanEarlyExtensions(JsonObject root)
	{
		RemoveMsftLodFrom(root["extensionsUsed"] as JsonArray);
		RemoveMsftLodFrom(root["extensionsRequired"] as JsonArray);
	}

	private static void RemoveMsftLodFrom(JsonArray? extArray)
	{
		if (extArray == null) return;
		for (int i = extArray.Count - 1; i >= 0; i--)
		{
			RemoveIfMsftLod(extArray, i);
		}
	}

	private static void RemoveIfMsftLod(JsonArray extArray, int i)
	{
		if (extArray[i]?.GetValue<string>() == "MSFT_lod")
			extArray.RemoveAt(i);
	}

	private static void CleanEarlyNodes(JsonObject root)
	{
		GlbManifestUtils.RemoveAndRemapNodes(root, nodeObj =>
		{
			string nodeName = nodeObj["name"]?.GetValue<string>() ?? string.Empty;
			return nodeName.EndsWith("_LOD1", StringComparison.OrdinalIgnoreCase) ||
			       nodeName.EndsWith("_LOD2", StringComparison.OrdinalIgnoreCase) ||
			       nodeName.EndsWith("_LOD3", StringComparison.OrdinalIgnoreCase);
		});

		if (root["nodes"] is JsonArray remainingNodes)
		{
			foreach (var n in remainingNodes)
			{
				if (n is not JsonObject nodeObj) continue;
				string nodeName = nodeObj["name"]?.GetValue<string>() ?? string.Empty;

				if (nodeName.EndsWith("_LOD0", StringComparison.OrdinalIgnoreCase))
					nodeObj["name"] = nodeName.Substring(0, nodeName.Length - 5);

				if (nodeObj.TryGetPropertyValue("extensions", out var extNode) && extNode is JsonObject nodeExts)
					nodeExts.Remove("MSFT_lod");
			}
		}
	}

	private static bool HasRequiredGlbArrays(JsonObject root)
	{
		if (root["meshes"] is not JsonArray meshes || meshes.Count == 0) return false;
		if (root["accessors"] is not JsonArray) return false;
		if (root["bufferViews"] is not JsonArray) return false;
		if (root["buffers"] is not JsonArray) return false;
		return true;
	}

	private static Dictionary<int, List<int>> ProcessMeshes(JsonObject root, float[] ratios, byte[] binBytes, MemoryStream newBinStream)
	{
		var meshLodMap = new Dictionary<int, List<int>>();
		var meshes = root["meshes"] as JsonArray;
		if (meshes == null) return meshLodMap;

		for (int m = 0; m < meshes.Count; m++)
		{
			if (meshes[m] is not JsonObject meshObj) continue;
			ProcessSingleMesh(meshObj, m, root, ratios, binBytes, newBinStream, meshes, meshLodMap);
		}

		return meshLodMap;
	}

	private static void ProcessSingleMesh(JsonObject meshObj, int m, JsonObject root, float[] ratios, byte[] binBytes, MemoryStream newBinStream, JsonArray meshes, Dictionary<int, List<int>> meshLodMap)
	{
		if (meshObj["primitives"] is not JsonArray primitives || primitives.Count == 0) return;

		string meshName = meshObj["name"]?.GetValue<string>() ?? $"Mesh_{m}";
		if (meshName.EndsWith("_LOD0", StringComparison.OrdinalIgnoreCase))
			meshName = meshName.Substring(0, meshName.Length - 5);
		else if (meshName.EndsWith("LOD0", StringComparison.OrdinalIgnoreCase))
			meshName = meshName.Substring(0, meshName.Length - 4);
		else if (IsExcludedMeshName(meshName))
			return;

		var lodMeshIndices = new List<int>();
		for (int t = 1; t < ratios.Length; t++)
		{
			ProcessLodTier(t, ratios[t], meshObj, root, binBytes, newBinStream, meshes, meshName, primitives, lodMeshIndices);
		}

		if (lodMeshIndices.Count > 0)
		{
			meshLodMap[m] = lodMeshIndices;
			meshObj["name"] = $"{meshName}_LOD0";
		}
	}

	private static bool IsExcludedMeshName(string meshName)
	{
		return meshName.Contains("_LOD", StringComparison.OrdinalIgnoreCase) ||
		       meshName.Contains("LOD1", StringComparison.OrdinalIgnoreCase) ||
		       meshName.Contains("LOD2", StringComparison.OrdinalIgnoreCase) ||
		       meshName.Contains("LOD3", StringComparison.OrdinalIgnoreCase);
	}

	private static void ProcessLodTier(int t, float ratio, JsonObject meshObj, JsonObject root, byte[] binBytes, MemoryStream newBinStream, JsonArray meshes, string meshName, JsonArray primitives, List<int> lodMeshIndices)
	{
		var newPrimitives = new JsonArray();
		for (int p = 0; p < primitives.Count; p++)
		{
			if (primitives[p] is not JsonObject primObj) continue;
			ProcessPrimitive(primObj, t, ratio, root, binBytes, newBinStream, newPrimitives);
		}

		if (newPrimitives.Count > 0)
		{
			int newMeshIdx = meshes.Count;
			var newMesh = new JsonObject();
			newMesh["name"] = $"{meshName}_LOD{t}";
			newMesh["primitives"] = newPrimitives;
			meshes.Add(newMesh);
			lodMeshIndices.Add(newMeshIdx);
		}
	}

	private static void ProcessPrimitive(JsonObject primObj, int t, float ratio, JsonObject root, byte[] binBytes, MemoryStream newBinStream, JsonArray newPrimitives)
	{
		if (ShouldSkipPrimitive(primObj))
		{
			newPrimitives.Add(primObj.DeepClone());
			return;
		}

		var geometry = GetGeometry(primObj, root, binBytes);
		if (geometry == null) return;

		uint[]? originalIndices = GetOriginalIndices(primObj, root, binBytes, geometry.Value.PositionCount);
		if (!IsOriginalIndicesValid(originalIndices, geometry.Value.PositionCount))
		{
			newPrimitives.Add(primObj.DeepClone());
			return;
		}

		int targetIndexCount = (int)(originalIndices!.Length * ratio) / 3 * 3;
		if (!IsTargetIndexCountValid(targetIndexCount, originalIndices.Length))
		{
			newPrimitives.Add(primObj.DeepClone());
			return;
		}

		var simplifiedIndices = SimplifyIndices(originalIndices, geometry.Value, targetIndexCount, t, binBytes);
		if (simplifiedIndices == null)
		{
			newPrimitives.Add(primObj.DeepClone());
			return;
		}

		AppendSimplifiedPrimitive(primObj, simplifiedIndices, root, newBinStream, newPrimitives);
	}

	private static bool ShouldSkipPrimitive(JsonObject primObj)
	{
		if (primObj.TryGetPropertyValue("mode", out var modeVal) && modeVal != null && modeVal.GetValue<int>() != 4)
			return true;
		if (primObj.ContainsKey("extensions") && primObj["extensions"] != null)
			return true;
		return false;
	}

	private static bool IsOriginalIndicesValid(uint[]? originalIndices, int posCount)
	{
		if (originalIndices == null || originalIndices.Length < 12) return false;
		return !HasInvalidIndex(originalIndices, posCount);
	}

	private static bool IsTargetIndexCountValid(int targetIndexCount, int originalLength)
	{
		return targetIndexCount >= 3 && targetIndexCount < originalLength;
	}

	private const float WeldEpsilon = 0.0001f;

	private readonly record struct AttributeStream(int ByteOffset, int ByteStride, int ElementSize);

	private readonly record struct TexCoordStream(int ByteOffset, int ByteStride, int ComponentType, bool Normalized);

	private readonly record struct VertexWeldKey(int PositionX, int PositionY, int PositionZ, int TexCoordU, int TexCoordV, ulong OtherAttributesHash);

	private readonly record struct PrimitiveGeometry(
		int PositionByteOffset,
		int PositionCount,
		int PositionByteStride,
		bool HasTexCoords,
		TexCoordStream TexCoords,
		AttributeStream[] OtherAttributes);

	private static PrimitiveGeometry? GetGeometry(JsonObject primObj, JsonObject root, byte[] binBytes)
	{
		var accessors = root["accessors"] as JsonArray;
		var bufferViews = root["bufferViews"] as JsonArray;
		if (accessors == null || bufferViews == null) return null;

		var posAcc = GetValidAccessor(primObj, accessors, "POSITION", "VEC3", 5126);
		if (posAcc == null) return null;

		var posBv = GetValidBufferView(posAcc, bufferViews);
		if (posBv == null) return null;

		var positions = ValidateAndCreatePositionData(posAcc, posBv, binBytes);
		if (positions == null) return null;

		if (!TryGetTexCoordStream(primObj, accessors, bufferViews, positions.Value.posCount, binBytes, out TexCoordStream texCoords, out bool hasTexCoords)) return null;

		var otherAttributes = CollectOtherAttributes(primObj, accessors, bufferViews, positions.Value.posCount, binBytes);
		if (otherAttributes == null) return null;

		return new PrimitiveGeometry(
			positions.Value.posByteOffset,
			positions.Value.posCount,
			positions.Value.posStride,
			hasTexCoords,
			texCoords,
			otherAttributes);
	}

	private static bool TryGetTexCoordStream(JsonObject primObj, JsonArray accessors, JsonArray bufferViews, int vertexCount, byte[] binBytes, out TexCoordStream texCoords, out bool hasTexCoords)
	{
		texCoords = default;
		hasTexCoords = false;

		int accessorIndex = GetAttributeIndex(primObj, "TEXCOORD_0");
		if (accessorIndex < 0) return true;
		if (accessorIndex >= accessors.Count) return false;
		if (accessors[accessorIndex] is not JsonObject accessor) return false;
		if (accessor.ContainsKey("sparse")) return false;
		if ((accessor["type"]?.GetValue<string>() ?? string.Empty) != "VEC2") return false;

		int componentType = accessor["componentType"]?.GetValue<int>() ?? 0;
		if (componentType != 5121 && componentType != 5122 && componentType != 5123 && componentType != 5126) return false;

		int accessorCount = accessor["count"]?.GetValue<int>() ?? 0;
		if (accessorCount != vertexCount) return false;

		if (GetValidBufferView(accessor, bufferViews) is not JsonObject bufferView) return false;

		int byteOffset = (accessor["byteOffset"]?.GetValue<int>() ?? 0) + (bufferView["byteOffset"]?.GetValue<int>() ?? 0);
		int byteStride = bufferView["byteStride"]?.GetValue<int>() ?? GetElementByteSize(accessor);
		if (!IsValidAttributeStream(byteOffset, byteStride, vertexCount, binBytes.Length)) return false;

		texCoords = new TexCoordStream(byteOffset, byteStride, componentType, accessor["normalized"]?.GetValue<bool>() ?? false);
		hasTexCoords = true;
		return true;
	}

	private static AttributeStream[]? CollectOtherAttributes(JsonObject primObj, JsonArray accessors, JsonArray bufferViews, int vertexCount, byte[] binBytes)
	{
		if (primObj["attributes"] is not JsonObject attributes) return null;

		var streams = new List<AttributeStream>(attributes.Count);
		foreach (var attribute in attributes)
		{
			if (attribute.Key is "POSITION" or "NORMAL" or "TANGENT" or "TEXCOORD_0") continue;

			if (!TryCreateAttributeStream(accessors, bufferViews, attribute.Value, vertexCount, binBytes, out AttributeStream stream, out bool isConstant)) return null;
			if (!isConstant) streams.Add(stream);
		}

		return streams.ToArray();
	}

	private static bool TryCreateAttributeStream(JsonArray accessors, JsonArray bufferViews, JsonNode? attributeValue, int vertexCount, byte[] binBytes, out AttributeStream stream, out bool isConstant)
	{
		stream = default;
		isConstant = false;

		int accessorIndex = attributeValue?.GetValue<int>() ?? -1;
		if (accessorIndex < 0 || accessorIndex >= accessors.Count) return false;
		if (accessors[accessorIndex] is not JsonObject accessor) return false;
		if (accessor.ContainsKey("sparse")) return false;

		if (!accessor.ContainsKey("bufferView"))
		{
			isConstant = true;
			return true;
		}

		if ((accessor["count"]?.GetValue<int>() ?? 0) != vertexCount) return false;

		if (GetValidBufferView(accessor, bufferViews) is not JsonObject bufferView) return false;

		int elementSize = GetElementByteSize(accessor);
		if (elementSize <= 0) return false;

		int byteOffset = (accessor["byteOffset"]?.GetValue<int>() ?? 0) + (bufferView["byteOffset"]?.GetValue<int>() ?? 0);
		int byteStride = bufferView["byteStride"]?.GetValue<int>() ?? elementSize;
		if (!IsValidAttributeStream(byteOffset, byteStride, vertexCount, binBytes.Length)) return false;

		stream = new AttributeStream(byteOffset, byteStride, elementSize);
		return true;
	}

	private static int GetElementByteSize(JsonObject accessor)
	{
		int componentSize = (accessor["componentType"]?.GetValue<int>() ?? 0) switch
		{
			5120 or 5121 => 1,
			5122 or 5123 => 2,
			5125 or 5126 => 4,
			_ => 0
		};

		int componentCount = (accessor["type"]?.GetValue<string>() ?? string.Empty) switch
		{
			"SCALAR" => 1,
			"VEC2" => 2,
			"VEC3" => 3,
			"VEC4" => 4,
			"MAT2" => 4,
			"MAT3" => 9,
			"MAT4" => 16,
			_ => 0
		};

		return componentSize * componentCount;
	}

	private static bool IsValidAttributeStream(int byteOffset, int byteStride, int vertexCount, int binBytesLength)
	{
		if (byteOffset < 0) return false;
		if (byteStride <= 0 || byteStride > 256) return false;

		long endOffset = (long)byteOffset + ((long)vertexCount * byteStride);
		return endOffset <= binBytesLength;
	}

	private static JsonObject? GetValidAccessor(JsonObject primObj, JsonArray accessors, string attributeKey, string expectedType, int expectedCompType)
	{
		int accIdx = GetAttributeIndex(primObj, attributeKey);
		if (accIdx < 0 || accIdx >= accessors.Count) return null;

		if (accessors[accIdx] is not JsonObject acc) return null;
		if (acc.ContainsKey("sparse")) return null;

		if (!IsValidAccessorFormat(acc, expectedType, expectedCompType)) return null;

		return acc;
	}

	private static int GetAttributeIndex(JsonObject primObj, string attributeKey)
	{
		if (primObj["attributes"] is not JsonObject attributes) return -1;
		if (!attributes.ContainsKey(attributeKey)) return -1;
		return attributes[attributeKey]!.GetValue<int>();
	}

	private static bool IsValidAccessorFormat(JsonObject acc, string expectedType, int expectedCompType)
	{
		string typeStr = acc["type"]?.GetValue<string>() ?? string.Empty;
		if (typeStr != expectedType) return false;

		int compType = acc["componentType"]?.GetValue<int>() ?? 0;
		if (compType != expectedCompType) return false;

		return true;
	}

	private static JsonObject? GetValidBufferView(JsonObject accessor, JsonArray bufferViews)
	{
		int bvIdx = accessor["bufferView"]?.GetValue<int>() ?? -1;
		if (bvIdx < 0 || bvIdx >= bufferViews.Count) return null;
		return bufferViews[bvIdx] as JsonObject;
	}

	private static (int posByteOffset, int posCount, int posStride)? ValidateAndCreatePositionData(JsonObject posAcc, JsonObject posBv, byte[] binBytes)
	{
		int posByteOffset = (posAcc["byteOffset"]?.GetValue<int>() ?? 0) + (posBv["byteOffset"]?.GetValue<int>() ?? 0);
		int posCount = posAcc["count"]?.GetValue<int>() ?? 0;
		int posStride = posBv["byteStride"]?.GetValue<int>() ?? 12;

		if (!IsValidPositionMetadata(posByteOffset, posCount, posStride, binBytes.Length)) return null;

		return (posByteOffset, posCount, posStride);
	}

	private static bool IsValidPositionMetadata(int posByteOffset, int posCount, int posStride, int binBytesLength)
	{
		if (posByteOffset < 0) return false;
		if (posCount < 3) return false;
		if (posStride < 12) return false;
		if ((posStride % 4) != 0) return false;
		if (posStride > 256) return false;

		long endOffset = (long)posByteOffset + ((long)posCount * posStride);
		if (endOffset > binBytesLength) return false;

		return true;
	}

	private static uint[]? GetOriginalIndices(JsonObject primObj, JsonObject root, byte[] binBytes, int posCount)
	{
		if (!primObj.ContainsKey("indices"))
			return GenerateSequentialIndices(posCount);

		var accessors = root["accessors"] as JsonArray;
		var bufferViews = root["bufferViews"] as JsonArray;
		if (accessors == null || bufferViews == null) return null;

		var indAcc = GetIndicesAccessor(primObj, accessors);
		if (indAcc == null) return null;

		var indBv = GetValidBufferView(indAcc, bufferViews);
		if (indBv == null) return null;

		return ValidateAndExtractIndices(indAcc, indBv, binBytes);
	}

	private static uint[]? GenerateSequentialIndices(int posCount)
	{
		if (posCount % 3 != 0) return null;
		uint[] indices = new uint[posCount];
		for (uint i = 0; i < posCount; i++) indices[i] = i;
		return indices;
	}

	private static JsonObject? GetIndicesAccessor(JsonObject primObj, JsonArray accessors)
	{
		int indAccIdx = primObj["indices"]!.GetValue<int>();
		if (indAccIdx < 0 || indAccIdx >= accessors.Count) return null;

		if (accessors[indAccIdx] is not JsonObject indAcc || indAcc.ContainsKey("sparse")) return null;
		if ((indAcc["type"]?.GetValue<string>() ?? string.Empty) != "SCALAR") return null;

		return indAcc;
	}

	private static uint[]? ValidateAndExtractIndices(JsonObject indAcc, JsonObject indBv, byte[] binBytes)
	{
		int indByteOffset = (indAcc["byteOffset"]?.GetValue<int>() ?? 0) + (indBv["byteOffset"]?.GetValue<int>() ?? 0);
		int indCount = indAcc["count"]?.GetValue<int>() ?? 0;
		int compType = indAcc["componentType"]?.GetValue<int>() ?? 0;

		if (!IsValidIndicesMetadata(indByteOffset, indCount, compType, binBytes.Length)) return null;

		return ExtractIndicesFromBytes(binBytes, indByteOffset, indCount, compType);
	}

	private static bool IsValidIndicesMetadata(int indByteOffset, int indCount, int compType, int binBytesLength)
	{
		if (indByteOffset < 0) return false;
		if (indCount < 3) return false;
		if ((indCount % 3) != 0) return false;

		int elemSize = compType switch { 5121 => 1, 5123 => 2, 5125 => 4, _ => 0 };
		if (elemSize == 0) return false;

		long endOffset = (long)indByteOffset + ((long)indCount * elemSize);
		if (endOffset > binBytesLength) return false;

		return true;
	}

	private static uint[]? ExtractIndicesFromBytes(byte[] binBytes, int offset, int count, int compType)
	{
		uint[] indices = new uint[count];
		if (compType == 5123)
		{
			for (int i = 0; i < count; i++) indices[i] = BitConverter.ToUInt16(binBytes, offset + (i * 2));
		}
		else if (compType == 5125)
		{
			for (int i = 0; i < count; i++) indices[i] = BitConverter.ToUInt32(binBytes, offset + (i * 4));
		}
		else if (compType == 5121)
		{
			for (int i = 0; i < count; i++) indices[i] = binBytes[offset + i];
		}
		else return null;

		return indices;
	}

	private static bool HasInvalidIndex(uint[] indices, int posCount)
	{
		for (int i = 0; i < indices.Length; i++)
		{
			if (indices[i] >= (uint)posCount) return true;
		}

		return false;
	}

	private static uint[]? SimplifyIndices(uint[] originalIndices, PrimitiveGeometry geometry, int targetIndexCount, int t, byte[] binBytes)
	{
		float[] weldedPositions = new float[geometry.PositionCount * 3];
		float[] weldedTexCoords = new float[geometry.PositionCount * 2];
		uint[] weldedIndices = new uint[originalIndices.Length];
		uint[] representatives = new uint[geometry.PositionCount];
		int weldedVertexCount = BuildPositionTexCoordWeldedMesh(weldedIndices, weldedPositions, weldedTexCoords, representatives, originalIndices, geometry, binBytes);

		uint[] simplifiedIndices = new uint[originalIndices.Length];
		nuint simplifiedCount = 0;

		fixed (uint* pWelded = weldedIndices)
		fixed (float* pWeldedPositions = weldedPositions)
		fixed (float* pWeldedTexCoords = weldedTexCoords)
		fixed (uint* pRepresentatives = representatives)
		fixed (uint* pDest = simplifiedIndices)
		fixed (byte* pBin = binBytes)
		{
			float* pAttributeWeights = stackalloc float[2] { 1.0f, 1.0f };

			simplifiedCount = DoSimplifyIndices(pWelded, pDest, pWeldedPositions, (nuint)weldedVertexCount, pWeldedTexCoords, pAttributeWeights, geometry.HasTexCoords, targetIndexCount, t, (nuint)originalIndices.Length);

			if (simplifiedCount < 3 || (simplifiedCount % 3) != 0 || simplifiedCount >= (nuint)originalIndices.Length) return null;

			for (nuint i = 0; i < simplifiedCount; i++)
			{
				pDest[i] = pRepresentatives[pDest[i]];
			}

			float* pPositions = (float*)(pBin + geometry.PositionByteOffset);
			MeshOptimizerNative.meshopt_optimizeVertexCache(pDest, pDest, simplifiedCount, (nuint)geometry.PositionCount);
			MeshOptimizerNative.meshopt_optimizeOverdraw(pDest, pDest, simplifiedCount, pPositions, (nuint)geometry.PositionCount, (nuint)geometry.PositionByteStride, 1.05f);
		}

		uint[] finalIndices = new uint[(int)simplifiedCount];
		Array.Copy(simplifiedIndices, finalIndices, (int)simplifiedCount);
		return finalIndices;
	}

	private static int BuildPositionTexCoordWeldedMesh(uint[] weldedIndices, float[] weldedPositions, float[] weldedTexCoords, uint[] representatives, uint[] originalIndices, PrimitiveGeometry geometry, byte[] binBytes)
	{
		var vertexLookup = new Dictionary<VertexWeldKey, int>(geometry.PositionCount);
		var vertexRemap = new uint[geometry.PositionCount];
		int weldedVertexCount = 0;

		for (int vertexIndex = 0; vertexIndex < geometry.PositionCount; vertexIndex++)
		{
			float x = ReadSingle(binBytes, geometry.PositionByteOffset + (vertexIndex * geometry.PositionByteStride));
			float y = ReadSingle(binBytes, geometry.PositionByteOffset + (vertexIndex * geometry.PositionByteStride) + 4);
			float z = ReadSingle(binBytes, geometry.PositionByteOffset + (vertexIndex * geometry.PositionByteStride) + 8);
			float u = 0f;
			float v = 0f;

			if (geometry.HasTexCoords)
			{
				int texCoordOffset = geometry.TexCoords.ByteOffset + (vertexIndex * geometry.TexCoords.ByteStride);
				int componentSize = GetComponentByteSize(geometry.TexCoords.ComponentType);
				u = ReadTexCoordComponent(binBytes, texCoordOffset, geometry.TexCoords);
				v = ReadTexCoordComponent(binBytes, texCoordOffset + componentSize, geometry.TexCoords);
			}

			var weldKey = new VertexWeldKey(
				QuantizeWeldValue(x),
				QuantizeWeldValue(y),
				QuantizeWeldValue(z),
				QuantizeWeldValue(u),
				QuantizeWeldValue(v),
				ComputeOtherAttributesHash(geometry.OtherAttributes, vertexIndex, binBytes));

			if (!vertexLookup.TryGetValue(weldKey, out int weldedIndex))
			{
				weldedIndex = weldedVertexCount++;
				vertexLookup[weldKey] = weldedIndex;
				weldedPositions[(weldedIndex * 3)] = x;
				weldedPositions[(weldedIndex * 3) + 1] = y;
				weldedPositions[(weldedIndex * 3) + 2] = z;
				weldedTexCoords[(weldedIndex * 2)] = u;
				weldedTexCoords[(weldedIndex * 2) + 1] = v;
				representatives[weldedIndex] = (uint)vertexIndex;
			}

			vertexRemap[vertexIndex] = (uint)weldedIndex;
		}

		for (int i = 0; i < originalIndices.Length; i++)
		{
			weldedIndices[i] = vertexRemap[originalIndices[i]];
		}

		return weldedVertexCount;
	}

	private static int QuantizeWeldValue(float value)
	{
		return (int)MathF.Round(value / WeldEpsilon);
	}

	private static ulong ComputeOtherAttributesHash(AttributeStream[] streams, int vertexIndex, byte[] binBytes)
	{
		ulong hash = 14695981039346656037UL;

		foreach (var stream in streams)
		{
			int offset = stream.ByteOffset + (vertexIndex * stream.ByteStride);
			for (int b = 0; b < stream.ElementSize; b++)
			{
				hash ^= binBytes[offset + b];
				hash *= 1099511628211UL;
			}
		}

		return hash;
	}

	private static float ReadSingle(byte[] binBytes, int offset)
	{
		return BinaryPrimitives.ReadSingleLittleEndian(binBytes.AsSpan(offset, 4));
	}

	private static float ReadTexCoordComponent(byte[] binBytes, int offset, TexCoordStream texCoords)
	{
		switch (texCoords.ComponentType)
		{
			case 5126:
				return ReadSingle(binBytes, offset);
			case 5123:
				ushort unsignedShortValue = BinaryPrimitives.ReadUInt16LittleEndian(binBytes.AsSpan(offset, 2));
				return texCoords.Normalized ? unsignedShortValue / 65535f : unsignedShortValue;
			case 5122:
				short shortValue = BinaryPrimitives.ReadInt16LittleEndian(binBytes.AsSpan(offset, 2));
				return texCoords.Normalized ? MathF.Max(shortValue / 32767f, -1f) : shortValue;
			default:
				byte byteValue = binBytes[offset];
				return texCoords.Normalized ? byteValue / 255f : byteValue;
		}
	}

	private static int GetComponentByteSize(int componentType)
	{
		return componentType switch
		{
			5120 or 5121 => 1,
			5122 or 5123 => 2,
			_ => 4
		};
	}

	private static nuint DoSimplifyIndices(uint* pWelded, uint* pDest, float* pWeldedPositions, nuint weldedVertexCount, float* pWeldedTexCoords, float* pAttributeWeights, bool hasTexCoords, int targetIndexCount, int t, nuint origLength)
	{
		float resultError = 0f;
		float targetError = GetLodTargetError(t);

		if (!hasTexCoords)
		{
			return MeshOptimizerNative.meshopt_simplify(pDest, pWelded, origLength, pWeldedPositions, weldedVertexCount, 3 * sizeof(float), (nuint)targetIndexCount, targetError, MeshOptimizerNative.SimplifyPrune, &resultError);
		}

		return MeshOptimizerNative.meshopt_simplifyWithAttributes(
			pDest, pWelded, origLength,
			pWeldedPositions, weldedVertexCount, 3 * sizeof(float),
			pWeldedTexCoords, 2 * sizeof(float), pAttributeWeights, 2,
			null, (nuint)targetIndexCount, targetError, MeshOptimizerNative.SimplifyPrune, &resultError);
	}

	private static float GetLodTargetError(int lodTier)
	{
		return lodTier switch
		{
			1 => 0.05f,
			2 => 0.15f,
			_ => 0.5f
		};
	}

	private static void AppendSimplifiedPrimitive(JsonObject primObj, uint[] simplifiedIndices, JsonObject root, MemoryStream newBinStream, JsonArray newPrimitives)
	{
		AlignStream(newBinStream);

		uint maxIdx = 0;
		for (int i = 0; i < simplifiedIndices.Length; i++)
			if (simplifiedIndices[i] > maxIdx)
				maxIdx = simplifiedIndices[i];

		int newIndByteOffset = (int)newBinStream.Position;
		bool useShort = maxIdx <= 65535;
		int newIndByteLength = simplifiedIndices.Length * (useShort ? 2 : 4);

		WriteIndicesToStream(simplifiedIndices, useShort, newBinStream);

		var bufferViews = root["bufferViews"] as JsonArray;
		var accessors = root["accessors"] as JsonArray;
		if (bufferViews == null || accessors == null) return;

		int newBvIdx = bufferViews.Count;
		var newBv = new JsonObject();
		newBv["buffer"] = 0;
		newBv["byteOffset"] = newIndByteOffset;
		newBv["byteLength"] = newIndByteLength;
		newBv["target"] = 34963;
		bufferViews.Add(newBv);

		int newAccIdx = accessors.Count;
		var newAcc = new JsonObject();
		newAcc["bufferView"] = newBvIdx;
		newAcc["byteOffset"] = 0;
		newAcc["componentType"] = useShort ? 5123 : 5125;
		newAcc["count"] = simplifiedIndices.Length;
		newAcc["type"] = "SCALAR";
		accessors.Add(newAcc);

		var newPrim = new JsonObject
		{
			["attributes"] = primObj["attributes"]!.DeepClone(),
			["indices"] = newAccIdx
		};
		if (primObj.ContainsKey("material"))
			newPrim["material"] = primObj["material"]!.GetValue<int>();

		newPrimitives.Add(newPrim);
	}

	private static void WriteIndicesToStream(uint[] indices, bool useShort, MemoryStream stream)
	{
		for (int i = 0; i < indices.Length; i++)
		{
			WriteIndexToStream(indices[i], useShort, stream);
		}
	}

	private static void WriteIndexToStream(uint index, bool useShort, MemoryStream stream)
	{
		if (useShort)
			stream.Write(BitConverter.GetBytes((ushort)index), 0, 2);
		else
			stream.Write(BitConverter.GetBytes(index), 0, 4);
	}

	private static void AlignStream(MemoryStream stream)
	{
		while ((stream.Position % 4) != 0) stream.WriteByte(0);
	}

	private static void UpdateBuffersLength(JsonObject root, int length)
	{
		if (root["buffers"] is JsonArray buffers && buffers.Count > 0 && buffers[0] is JsonObject buf0)
			buf0["byteLength"] = length;
	}

	private static void UpdateSceneNodesWithLods(JsonObject root, Dictionary<int, List<int>> meshLodMap, float[] visBegins, float[] visEnds)
	{
		if (root["nodes"] is not JsonArray nodes || nodes.Count == 0) return;

		var nodeParentMap = BuildNodeParentMap(nodes);
		var sceneRootLists = GetSceneRootLists(root);

		int initialNodeCount = nodes.Count;
		for (int n = 0; n < initialNodeCount; n++)
		{
			if (nodes[n] is not JsonObject nodeObj || !nodeObj.ContainsKey("mesh")) continue;

			int meshIdx = nodeObj["mesh"]!.GetValue<int>();
			if (!meshLodMap.TryGetValue(meshIdx, out var lodMeshesList)) continue;

			ProcessNodeLod(nodeObj, n, lodMeshesList, nodes, nodeParentMap, sceneRootLists, visBegins, visEnds);
		}
	}

	private static Dictionary<int, int> BuildNodeParentMap(JsonArray nodes)
	{
		var nodeParentMap = new Dictionary<int, int>(nodes.Count);
		for (int i = 0; i < nodes.Count; i++)
		{
			if (nodes[i] is not JsonObject nodeObj || nodeObj["children"] is not JsonArray children) continue;

			foreach (var childItem in children)
			{
				if (childItem is JsonValue childValue && childValue.TryGetValue(out int childIndex))
				{
					nodeParentMap[childIndex] = i;
				}
			}
		}

		return nodeParentMap;
	}

	private static List<JsonArray> GetSceneRootLists(JsonObject root)
	{
		if (root["scenes"] is not JsonArray scenes)
		{
			scenes = new JsonArray();
			root["scenes"] = scenes;
		}

		if (scenes.Count == 0)
		{
			scenes.Add(new JsonObject { ["nodes"] = new JsonArray() });
		}

		var sceneRootLists = new List<JsonArray>(scenes.Count);
		foreach (var sceneItem in scenes)
		{
			if (sceneItem is not JsonObject sceneObj) continue;

			if (sceneObj["nodes"] is not JsonArray sceneRoots)
			{
				sceneRoots = new JsonArray();
				sceneObj["nodes"] = sceneRoots;
			}

			sceneRootLists.Add(sceneRoots);
		}

		return sceneRootLists;
	}

	private static void ProcessNodeLod(JsonObject nodeObj, int nodeIndex, List<int> lodMeshesList, JsonArray nodes, Dictionary<int, int> nodeParentMap, List<JsonArray> sceneRootLists, float[] visBegins, float[] visEnds)
	{
		string nodeName = nodeObj["name"]?.GetValue<string>() ?? $"Node_{nodeIndex}";
		if (nodeName.EndsWith("_LOD0", StringComparison.OrdinalIgnoreCase))
			nodeName = nodeName.Substring(0, nodeName.Length - 5);
		nodeObj["name"] = $"{nodeName}_LOD0";

		JsonArray siblingList = GetNodeSiblingList(nodes, nodeParentMap, sceneRootLists, nodeIndex);
		for (int t = 0; t < lodMeshesList.Count; t++)
		{
			int lodTier = t + 1;
			var lodNode = CreateLodNode(nodeObj, nodeName, lodMeshesList[t], lodTier, visBegins, visEnds);

			int newLodNodeIdx = nodes.Count;
			nodes.Add(lodNode);
			AppendUniqueChildReference(siblingList, newLodNodeIdx);
		}

		UpdateNodeExtensionsWithLod(nodeObj, visBegins[0], visEnds[0]);
	}

	private static JsonArray GetNodeSiblingList(JsonArray nodes, Dictionary<int, int> nodeParentMap, List<JsonArray> sceneRootLists, int nodeIndex)
	{
		if (nodeParentMap.TryGetValue(nodeIndex, out int parentIndex) && nodes[parentIndex] is JsonObject parentObj && parentObj["children"] is JsonArray siblings)
		{
			return siblings;
		}

		foreach (var sceneRoots in sceneRootLists)
		{
			if (ContainsNodeIndex(sceneRoots, nodeIndex)) return sceneRoots;
		}

		if (sceneRootLists.Count == 0) return new JsonArray();

		JsonArray defaultRoots = sceneRootLists[0];
		AppendUniqueChildReference(defaultRoots, nodeIndex);
		return defaultRoots;
	}

	private static bool ContainsNodeIndex(JsonArray nodeIndices, int nodeIndex)
	{
		foreach (var item in nodeIndices)
		{
			if (item is JsonValue value && value.TryGetValue(out int existingIndex) && existingIndex == nodeIndex) return true;
		}

		return false;
	}

	private static void AppendUniqueChildReference(JsonArray childrenArray, int childIndex)
	{
		foreach (var childItem in childrenArray)
		{
			if (childItem is JsonValue childValue && childValue.TryGetValue(out int existingIndex) && existingIndex == childIndex)
			{
				return;
			}
		}

		childrenArray.Add(childIndex);
	}

	private static JsonObject CreateLodNode(JsonObject nodeObj, string nodeName, int lodMeshIdx, int lodTier, float[] visBegins, float[] visEnds)
	{
		var lodNode = new JsonObject
		{
			["name"] = $"{nodeName}_LOD{lodTier}",
			["mesh"] = lodMeshIdx
		};

		if (nodeObj.ContainsKey("skin")) lodNode["skin"] = nodeObj["skin"]!.GetValue<int>();
		if (nodeObj.ContainsKey("matrix")) lodNode["matrix"] = nodeObj["matrix"]!.DeepClone();
		if (nodeObj.ContainsKey("translation")) lodNode["translation"] = nodeObj["translation"]!.DeepClone();
		if (nodeObj.ContainsKey("rotation")) lodNode["rotation"] = nodeObj["rotation"]!.DeepClone();
		if (nodeObj.ContainsKey("scale")) lodNode["scale"] = nodeObj["scale"]!.DeepClone();

		var extras = new JsonObject();
		extras["visibility_range_begin"] = visBegins[lodTier];
		extras["visibility_range_end"] = visEnds[lodTier];
		lodNode["extras"] = extras;

		return lodNode;
	}

	private static void UpdateNodeExtensionsWithLod(JsonObject nodeObj, float visBegin, float visEnd)
	{
		if (nodeObj["extensions"] is JsonObject nodeExts)
		{
			nodeExts.Remove("MSFT_lod");
			if (nodeExts.Count == 0)
			{
				nodeObj.Remove("extensions");
			}
		}

		if (nodeObj["extras"] is not JsonObject nExtras)
		{
			nExtras = new JsonObject();
			nodeObj["extras"] = nExtras;
		}

		nExtras["visibility_range_begin"] = visBegin;
		nExtras["visibility_range_end"] = visEnd;
	}

	private static void SanityCheckFinalScenes(JsonObject root)
	{
		if (root["scenes"] is not JsonArray finalScenes || root["nodes"] is not JsonArray finalNodes) return;

		foreach (var sc in finalScenes)
		{
			SanityCheckScene(sc, finalNodes);
		}
	}

	private static void SanityCheckScene(JsonNode? sc, JsonArray finalNodes)
	{
		if (sc is not JsonObject scObj || scObj["nodes"] is not JsonArray scNodes) return;

		var distinctNodes = new JsonArray();
		var seen = new HashSet<int>();
		foreach (var item in scNodes)
		{
			TryAddDistinctNode(item, finalNodes, seen, distinctNodes);
		}

		scObj["nodes"] = distinctNodes;
	}

	private static void TryAddDistinctNode(JsonNode? item, JsonArray finalNodes, HashSet<int> seen, JsonArray distinctNodes)
	{
		if (item is not JsonValue jv || !jv.TryGetValue(out int nodeIdx)) return;
		if (nodeIdx >= 0 && nodeIdx < finalNodes.Count && seen.Add(nodeIdx))
			distinctNodes.Add(nodeIdx);
	}

	private static bool IsDecreasingRatios(float[] ratios)
	{
		for (int i = 0; i < ratios.Length; i++)
		{
			if (IsInvalidRatio(ratios[i])) return false;
			if (i > 0 && ratios[i] >= ratios[i - 1]) return false;
		}

		return true;
	}

	private static bool IsInvalidRatio(float ratio)
	{
		return float.IsNaN(ratio) || float.IsInfinity(ratio) || ratio <= 0f || ratio > 1.0f;
	}
}