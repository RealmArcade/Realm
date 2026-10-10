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
		var removedIndices = CleanEarlyNodes(root);
		CleanEarlyScenes(root, removedIndices);

		if (!HasRequiredGlbArrays(root))
			return (true, input, string.Empty);

		binBytes ??= Array.Empty<byte>();
		using var newBinStream = new MemoryStream();
		newBinStream.Write(binBytes, 0, binBytes.Length);

		var meshLodMap = ProcessMeshes(root, ratios, binBytes, newBinStream);

		AlignStream(newBinStream);
		byte[] finalBinBytes = newBinStream.ToArray();
		
		UpdateBuffersLength(root, finalBinBytes.Length);
		EnsureMsftLodExtension(root);
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

	private static HashSet<int> CleanEarlyNodes(JsonObject root)
	{
		var removedIndices = new HashSet<int>();
		if (root["nodes"] is not JsonArray nodes) return removedIndices;

		for (int i = nodes.Count - 1; i >= 0; i--)
		{
			if (nodes[i] is not JsonObject nodeObj) continue;
			ProcessAndRemoveNodeIfEarly(nodes, i, nodeObj, removedIndices);
		}
		return removedIndices;
	}

	private static void ProcessAndRemoveNodeIfEarly(JsonArray nodes, int i, JsonObject nodeObj, HashSet<int> removedIndices)
	{
		if (ProcessEarlyNode(nodeObj, i, removedIndices))
		{
			nodes.RemoveAt(i);
		}
	}

	private static bool ProcessEarlyNode(JsonObject nodeObj, int index, HashSet<int> removedIndices)
	{
		string nodeName = nodeObj["name"]?.GetValue<string>() ?? string.Empty;
		if (nodeName.EndsWith("_LOD1", StringComparison.OrdinalIgnoreCase) ||
			nodeName.EndsWith("_LOD2", StringComparison.OrdinalIgnoreCase) ||
			nodeName.EndsWith("_LOD3", StringComparison.OrdinalIgnoreCase))
		{
			removedIndices.Add(index);
			return true; // remove this node
		}
		
		if (nodeName.EndsWith("_LOD0", StringComparison.OrdinalIgnoreCase))
			nodeObj["name"] = nodeName.Substring(0, nodeName.Length - 5);
			
		if (nodeObj.TryGetPropertyValue("extensions", out var extNode) && extNode is JsonObject nodeExts)
			nodeExts.Remove("MSFT_lod");

		return false;
	}

	private static void CleanEarlyScenes(JsonObject root, HashSet<int> removedIndices)
	{
		if (root["scenes"] is not JsonArray scenes) return;
		int nodeCount = (root["nodes"] as JsonArray)?.Count ?? 0;

		foreach (var sc in scenes)
		{
			CleanScene(sc, removedIndices, nodeCount);
		}
	}

	private static void CleanScene(JsonNode? sc, HashSet<int> removedIndices, int nodeCount)
	{
		if (sc is not JsonObject scObj || scObj["nodes"] is not JsonArray scNodes) return;
		scObj["nodes"] = GetCleanSceneNodes(scNodes, removedIndices, nodeCount);
	}

	private static JsonArray GetCleanSceneNodes(JsonArray scNodes, HashSet<int> removedIndices, int nodeCount)
	{
		var cleanNodes = new JsonArray();
		var seen = new HashSet<int>();
		foreach (var item in scNodes)
		{
			TryAddCleanNode(item, cleanNodes, removedIndices, nodeCount, seen);
		}
		return cleanNodes;
	}

	private static void TryAddCleanNode(JsonNode? item, JsonArray cleanNodes, HashSet<int> removedIndices, int nodeCount, HashSet<int> seen)
	{
		if (item is not JsonValue jv || !jv.TryGetValue(out int nodeIdx)) return;
		if (!removedIndices.Contains(nodeIdx) && nodeIdx >= 0 && nodeIdx < nodeCount && seen.Add(nodeIdx))
			cleanNodes.Add(nodeIdx);
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

		var posData = GetPositionData(primObj, root, binBytes);
		if (posData == null) return;

		uint[]? originalIndices = GetOriginalIndices(primObj, root, binBytes, posData.Value.posCount);
		if (!IsOriginalIndicesValid(originalIndices, posData.Value.posCount))
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

		var simplifiedIndices = SimplifyIndices(originalIndices, posData.Value, targetIndexCount, t, binBytes);
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

	private static (int posByteOffset, int posCount, int posStride)? GetPositionData(JsonObject primObj, JsonObject root, byte[] binBytes)
	{
		var accessors = root["accessors"] as JsonArray;
		var bufferViews = root["bufferViews"] as JsonArray;
		if (accessors == null || bufferViews == null) return null;

		var posAcc = GetValidAccessor(primObj, accessors, "POSITION", "VEC3", 5126);
		if (posAcc == null) return null;

		var posBv = GetValidBufferView(posAcc, bufferViews);
		if (posBv == null) return null;

		return ValidateAndCreatePositionData(posAcc, posBv, binBytes);
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

	private static uint[]? SimplifyIndices(uint[] originalIndices, (int posByteOffset, int posCount, int posStride) posData, int targetIndexCount, int t, byte[] binBytes)
	{
		uint[] simplifiedIndices = new uint[originalIndices.Length];
		nuint simplifiedCount = 0;

		fixed (uint* pOrig = originalIndices)
		fixed (uint* pDest = simplifiedIndices)
		fixed (byte* pBin = binBytes)
		{
			float* pPos = (float*)(pBin + posData.posByteOffset);
			simplifiedCount = DoSimplifyIndices(pOrig, pDest, pPos, posData, targetIndexCount, t, (nuint)originalIndices.Length);
		}

		if (simplifiedCount < 3 || (simplifiedCount % 3) != 0 || simplifiedCount >= (nuint)originalIndices.Length) return null;

		uint[] finalIndices = new uint[(int)simplifiedCount];
		Array.Copy(simplifiedIndices, finalIndices, (int)simplifiedCount);
		return finalIndices;
	}

	private static nuint DoSimplifyIndices(uint* pOrig, uint* pDest, float* pPos, (int posByteOffset, int posCount, int posStride) posData, int targetIndexCount, int t, nuint origLength)
	{
		float resultError = 0f;
		nuint simplifiedCount = MeshOptimizerNative.meshopt_simplify(pDest, pOrig, origLength, pPos, (nuint)posData.posCount, (nuint)posData.posStride, (nuint)targetIndexCount, 0.02f * t, 0, &resultError);

		if (simplifiedCount >= origLength || simplifiedCount < 3)
		{
			simplifiedCount = MeshOptimizerNative.meshopt_simplifySloppy(pDest, pOrig, origLength, pPos, (nuint)posData.posCount, (nuint)posData.posStride, null, (nuint)targetIndexCount, 0.05f * t, &resultError);
		}

		if (simplifiedCount >= 3 && (simplifiedCount % 3) == 0 && simplifiedCount < origLength)
		{
			MeshOptimizerNative.meshopt_optimizeVertexCache(pDest, pDest, simplifiedCount, (nuint)posData.posCount);
			MeshOptimizerNative.meshopt_optimizeOverdraw(pDest, pDest, simplifiedCount, pPos, (nuint)posData.posCount, (nuint)posData.posStride, 1.05f);
		}

		return simplifiedCount;
	}

	private static void AppendSimplifiedPrimitive(JsonObject primObj, uint[] simplifiedIndices, JsonObject root, MemoryStream newBinStream, JsonArray newPrimitives)
	{
		AlignStream(newBinStream);

		uint maxIdx = 0;
		for (int i = 0; i < simplifiedIndices.Length; i++)
			if (simplifiedIndices[i] > maxIdx) maxIdx = simplifiedIndices[i];

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

	private static void EnsureMsftLodExtension(JsonObject root)
	{
		if (root["extensionsUsed"] is not JsonArray extUsed)
		{
			extUsed = new JsonArray();
			root["extensionsUsed"] = extUsed;
		}
		
		bool hasMsftLod = false;
		foreach (var item in extUsed)
		{
			if (item?.ToString() == "MSFT_lod") { hasMsftLod = true; break; }
		}
		if (!hasMsftLod) extUsed.Add("MSFT_lod");
	}

	private static void UpdateSceneNodesWithLods(JsonObject root, Dictionary<int, List<int>> meshLodMap, float[] visBegins, float[] visEnds)
	{
		if (root["nodes"] is not JsonArray nodes || nodes.Count == 0) return;

		int initialNodeCount = nodes.Count;
		for (int n = 0; n < initialNodeCount; n++)
		{
			if (nodes[n] is not JsonObject nodeObj || !nodeObj.ContainsKey("mesh")) continue;

			int meshIdx = nodeObj["mesh"]!.GetValue<int>();
			if (!meshLodMap.TryGetValue(meshIdx, out var lodMeshesList)) continue;

			ProcessNodeLod(nodeObj, n, lodMeshesList, nodes, visBegins, visEnds);
		}
	}

	private static void ProcessNodeLod(JsonObject nodeObj, int n, List<int> lodMeshesList, JsonArray nodes, float[] visBegins, float[] visEnds)
	{
		string nodeName = nodeObj["name"]?.GetValue<string>() ?? $"Node_{n}";
		if (nodeName.EndsWith("_LOD0", StringComparison.OrdinalIgnoreCase))
			nodeName = nodeName.Substring(0, nodeName.Length - 5);
		nodeObj["name"] = $"{nodeName}_LOD0";

		var lodNodeIndices = new JsonArray();
		for (int t = 0; t < lodMeshesList.Count; t++)
		{
			int lodTier = t + 1;
			var lodNode = CreateLodNode(nodeObj, nodeName, lodMeshesList[t], lodTier, visBegins, visEnds);
			
			int newLodNodeIdx = nodes.Count;
			nodes.Add(lodNode);
			lodNodeIndices.Add(newLodNodeIdx);
		}

		UpdateNodeExtensionsWithLod(nodeObj, lodNodeIndices, visBegins[0], visEnds[0]);
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

	private static void UpdateNodeExtensionsWithLod(JsonObject nodeObj, JsonArray lodNodeIndices, float visBegin, float visEnd)
	{
		if (nodeObj["extensions"] is not JsonObject nodeExts)
		{
			nodeExts = new JsonObject();
			nodeObj["extensions"] = nodeExts;
		}
		var msftLod = new JsonObject();
		msftLod["ids"] = lodNodeIndices;
		nodeExts["MSFT_lod"] = msftLod;

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
