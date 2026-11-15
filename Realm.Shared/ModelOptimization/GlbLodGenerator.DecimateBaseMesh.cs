using System.Text.Json.Nodes;

namespace Realm.Shared.ModelOptimization;

/// <summary>bringing the master tier down to a budget that a screen can actually draw, and compacting the vertices it no longer uses.</summary>
public static unsafe partial class GlbLodGenerator
{
	public const int BaseTierReferenceViewportHeight = 1080;

	private const int MinimumTargetIndexCount = 3;
	private const float FailSafeTargetError = 0.5f;

	private readonly record struct AttributeSource(int ByteOffset, int ByteStride, int ElementSize);

	public static (bool Success, byte[] OutputGlbBytes, string ErrorMessage, int TriangleCount) DecimateBaseMesh(
		byte[] inputGlbBytes,
		BaseTierDecimationOptions options,
		LodDecimationProfile? decimationProfile = null,
		float uvSeamMergeTolerance = 0f)
	{
		if (inputGlbBytes == null || inputGlbBytes.Length < 20)
			return (false, inputGlbBytes ?? Array.Empty<byte>(), "Invalid GLB buffer", 0);

		try
		{
			return ProcessBaseMeshDecimation(inputGlbBytes, options, decimationProfile, uvSeamMergeTolerance);
		}
		catch (Exception ex)
		{
			return (false, inputGlbBytes, $"Failed to decimate the master tier: {ex.Message}", 0);
		}
	}

	private static (bool Success, byte[] OutputGlbBytes, string ErrorMessage, int TriangleCount) ProcessBaseMeshDecimation(
		byte[] inputGlbBytes,
		BaseTierDecimationOptions options,
		LodDecimationProfile? decimationProfile,
		float uvSeamMergeTolerance)
	{
		var (jsonNode, binBytes, version) = GlbManifestUtils.ParseGlb(inputGlbBytes);
		if (jsonNode is not JsonObject root)
			return (false, inputGlbBytes, "Failed to parse glTF JSON root", 0);
		if (!HasRequiredGlbArrays(root))
			return (true, inputGlbBytes, string.Empty, 0);

		binBytes ??= Array.Empty<byte>();
		var bufferViews = (JsonArray)root["bufferViews"]!;
		var accessors = (JsonArray)root["accessors"]!;
		var meshes = (JsonArray)root["meshes"]!;

		int totalTriangleCount = CountTotalTriangleCount(meshes, accessors);
		using var newBinStream = new MemoryStream();
		newBinStream.Write(binBytes, 0, binBytes.Length);

		int finalTriangleCount = 0;
		bool anyDecimated = false;

		for (int meshIndex = 0; meshIndex < meshes.Count; meshIndex++)
		{
			if (meshes[meshIndex] is not JsonObject meshObj || meshObj["primitives"] is not JsonArray primitives) continue;
			if (meshObj["name"]?.GetValue<string>() is string meshName && IsExcludedMeshName(meshName)) continue;

			foreach (var primitiveNode in primitives)
			{
				if (primitiveNode is not JsonObject primitiveObj) continue;

				bool decimated = TryDecimatePrimitive(primitiveObj, root, binBytes, newBinStream, bufferViews, accessors, options, totalTriangleCount, decimationProfile, uvSeamMergeTolerance, out int triangleCount);
				finalTriangleCount += triangleCount;
				anyDecimated |= decimated;
			}
		}

		if (!anyDecimated)
			return (true, inputGlbBytes, string.Empty, totalTriangleCount);

		AlignStream(newBinStream);
		byte[] rebuiltBinBytes = newBinStream.ToArray();
		UpdateBuffersLength(root, rebuiltBinBytes.Length);

		byte[] rebuiltGlbBytes = GlbManifestUtils.BuildGlb(root, rebuiltBinBytes, version);
		byte[] compactedGlbBytes = GlbManifestUtils.CompactUnusedAccessors(rebuiltGlbBytes);

		return (true, compactedGlbBytes, string.Empty, finalTriangleCount);
	}

	private static int CountTotalTriangleCount(JsonArray meshes, JsonArray accessors)
	{
		int totalTriangleCount = 0;

		foreach (var meshNode in meshes)
		{
			if (meshNode is not JsonObject meshObj || meshObj["primitives"] is not JsonArray primitives) continue;
			if (meshObj["name"]?.GetValue<string>() is string meshName && IsExcludedMeshName(meshName)) continue;

			foreach (var primitiveNode in primitives)
			{
				if (primitiveNode is not JsonObject primitiveObj) continue;

				totalTriangleCount += GetPrimitiveTriangleCount(primitiveObj, accessors);
			}
		}

		return totalTriangleCount;
	}

	private static int GetPrimitiveTriangleCount(JsonObject primitiveObj, JsonArray accessors)
	{
		if (primitiveObj.TryGetPropertyValue("mode", out var modeNode) && modeNode != null && modeNode.GetValue<int>() != 4) return 0;
		if (primitiveObj.ContainsKey("targets")) return 0;

		int accessorIndex = primitiveObj["indices"]?.GetValue<int>() ?? -1;
		if (accessorIndex < 0 || accessorIndex >= accessors.Count) return 0;
		if (accessors[accessorIndex] is not JsonObject indexAccessor) return 0;

		return (indexAccessor["count"]?.GetValue<int>() ?? 0) / 3;
	}

	private static bool TryDecimatePrimitive(
		JsonObject primitiveObj,
		JsonObject root,
		byte[] binBytes,
		MemoryStream newBinStream,
		JsonArray bufferViews,
		JsonArray accessors,
		BaseTierDecimationOptions options,
		int totalTriangleCount,
		LodDecimationProfile? decimationProfile,
		float uvSeamMergeTolerance,
		out int triangleCount)
	{
		triangleCount = 0;

		if (ShouldSkipPrimitive(primitiveObj)) return false;
		if (primitiveObj.ContainsKey("targets")) return false;

		int originalTriangleCount = GetPrimitiveTriangleCount(primitiveObj, accessors);
		triangleCount = originalTriangleCount;
		if (originalTriangleCount <= 0) return false;

		var geometry = GetGeometry(primitiveObj, root, binBytes);
		if (geometry == null) return false;

		uint[]? originalIndices = GetOriginalIndices(primitiveObj, root, binBytes, geometry.Value.PositionCount);
		if (!IsOriginalIndicesValid(originalIndices, geometry.Value.PositionCount)) return false;

		int clampTriangleCount = ResolvePrimitiveTriangleBudget(originalTriangleCount, totalTriangleCount, options.MaximumTriangleCount);
		bool hasQualityBudget = options.TargetError > 0f;
		if (!hasQualityBudget && clampTriangleCount >= originalTriangleCount) return false;

		var simplifiedIndices = SimplifyBaseTierIndices(originalIndices!, geometry.Value, clampTriangleCount, hasQualityBudget, options.TargetError, binBytes, decimationProfile, uvSeamMergeTolerance);
		if (simplifiedIndices == null) return false;

		if (!TryCompactPrimitive(primitiveObj, simplifiedIndices, geometry.Value, root, binBytes, newBinStream, bufferViews, accessors)) return false;

		triangleCount = simplifiedIndices.Length / 3;
		return true;
	}

	private static uint[]? SimplifyBaseTierIndices(
		uint[] originalIndices,
		PrimitiveGeometry geometry,
		int clampTriangleCount,
		bool hasQualityBudget,
		float targetError,
		byte[] binBytes,
		LodDecimationProfile? decimationProfile,
		float uvSeamMergeTolerance)
	{
		if (!hasQualityBudget)
		{
			return SimplifyIndicesToBudget(originalIndices, geometry, clampTriangleCount * 3, FailSafeTargetError, binBytes, decimationProfile, uvSeamMergeTolerance);
		}

		uint[]? qualityLimitedIndices = SimplifyIndicesToBudget(originalIndices, geometry, MinimumTargetIndexCount, targetError, binBytes, decimationProfile, uvSeamMergeTolerance);
		if (qualityLimitedIndices == null) return null;
		if (qualityLimitedIndices.Length <= clampTriangleCount * 3) return qualityLimitedIndices;

		uint[]? clampedIndices = SimplifyIndicesToBudget(originalIndices, geometry, clampTriangleCount * 3, FailSafeTargetError, binBytes, decimationProfile, uvSeamMergeTolerance);

		return clampedIndices ?? qualityLimitedIndices;
	}

	private static int ResolvePrimitiveTriangleBudget(int originalTriangleCount, int totalTriangleCount, int maximumTriangleCount)
	{
		if (maximumTriangleCount <= 0) return originalTriangleCount;
		if (totalTriangleCount <= maximumTriangleCount) return originalTriangleCount;

		long scaledBudget = (long)maximumTriangleCount * originalTriangleCount / totalTriangleCount;
		int triangleBudget = (int)Math.Clamp(scaledBudget, 3, originalTriangleCount);

		return triangleBudget;
	}

	private static bool TryCompactPrimitive(
		JsonObject primitiveObj,
		uint[] simplifiedIndices,
		PrimitiveGeometry geometry,
		JsonObject root,
		byte[] binBytes,
		MemoryStream newBinStream,
		JsonArray bufferViews,
		JsonArray accessors)
	{
		if (primitiveObj["attributes"] is not JsonObject attributes) return false;
		if (simplifiedIndices.Length < 3) return false;

		int[] vertexRemap = BuildUsedVertexRemap(simplifiedIndices, geometry.PositionCount, out int compactVertexCount);
		if (compactVertexCount <= 0) return false;

		uint[] compactIndices = RemapIndices(simplifiedIndices, vertexRemap);

		var compactAttributes = new JsonObject();
		foreach (var attribute in attributes)
		{
			if (!TryWriteCompactedAttribute(attribute.Key, attribute.Value, vertexRemap, compactVertexCount, bufferViews.Count, bufferViews, accessors, binBytes, newBinStream, compactAttributes))
				return false;
		}

		primitiveObj["attributes"] = compactAttributes;
		primitiveObj["indices"] = AppendCompactedIndices(compactIndices, newBinStream, bufferViews, accessors);

		return true;
	}

	private static int[] BuildUsedVertexRemap(uint[] simplifiedIndices, int vertexCount, out int compactVertexCount)
	{
		var vertexRemap = new int[vertexCount];
		Array.Fill(vertexRemap, -1);

		compactVertexCount = 0;
		for (int i = 0; i < simplifiedIndices.Length; i++)
		{
			uint vertexIndex = simplifiedIndices[i];
			if (vertexIndex >= vertexRemap.Length) continue;
			if (vertexRemap[vertexIndex] >= 0) continue;

			vertexRemap[vertexIndex] = compactVertexCount++;
		}

		return vertexRemap;
	}

	private static uint[] RemapIndices(uint[] simplifiedIndices, int[] vertexRemap)
	{
		var compactIndices = new uint[simplifiedIndices.Length];

		for (int i = 0; i < simplifiedIndices.Length; i++)
		{
			uint vertexIndex = simplifiedIndices[i];
			int remappedIndex = vertexIndex < vertexRemap.Length ? vertexRemap[vertexIndex] : -1;
			compactIndices[i] = remappedIndex >= 0 ? (uint)remappedIndex : 0;
		}

		return compactIndices;
	}

	private static bool TryWriteCompactedAttribute(
		string attributeName,
		JsonNode? attributeNode,
		int[] vertexRemap,
		int compactVertexCount,
		int bufferViewCount,
		JsonArray bufferViews,
		JsonArray accessors,
		byte[] binBytes,
		MemoryStream newBinStream,
		JsonObject compactAttributes)
	{
		int accessorIndex = attributeNode?.GetValue<int>() ?? -1;
		if (accessorIndex < 0 || accessorIndex >= accessors.Count) return false;
		if (accessors[accessorIndex] is not JsonObject accessor) return false;
		if (accessor.ContainsKey("sparse")) return false;

		if (accessor["bufferView"] is not JsonNode bufferViewNode)
		{
			compactAttributes[attributeName] = accessorIndex;
			return true;
		}

		int bufferViewIndex = bufferViewNode.GetValue<int>();
		if (bufferViewIndex < 0 || bufferViewIndex >= bufferViewCount) return false;
		if (bufferViews[bufferViewIndex] is not JsonObject bufferView) return false;

		int elementSize = GetElementByteSize(accessor);
		if (elementSize <= 0) return false;

		int sourceOffset = (accessor["byteOffset"]?.GetValue<int>() ?? 0) + (bufferView["byteOffset"]?.GetValue<int>() ?? 0);
		int sourceStride = bufferView["byteStride"]?.GetValue<int>() ?? elementSize;
		if (sourceOffset < 0 || sourceStride < elementSize) return false;

		var attributeSource = new AttributeSource(sourceOffset, sourceStride, elementSize);
		byte[] compactBytes = CompactAttributeBytes(attributeSource, vertexRemap, compactVertexCount, binBytes);
		if (compactBytes.Length == 0) return false;

		AlignStream(newBinStream);
		int newOffset = (int)newBinStream.Position;
		newBinStream.Write(compactBytes, 0, compactBytes.Length);

		int newBufferViewIndex = bufferViews.Count;
		var newBufferView = new JsonObject
		{
			["buffer"] = 0,
			["byteOffset"] = newOffset,
			["byteLength"] = compactBytes.Length
		};
		if (bufferView["target"] is JsonNode targetNode) newBufferView["target"] = targetNode.DeepClone();
		bufferViews.Add(newBufferView);

		compactAttributes[attributeName] = AppendCompactedAccessor(attributeName, accessor, newBufferViewIndex, compactVertexCount, compactBytes, accessors);

		return true;
	}

	private static byte[] CompactAttributeBytes(AttributeSource attributeSource, int[] vertexRemap, int compactVertexCount, byte[] binBytes)
	{
		byte[] compactBytes = new byte[compactVertexCount * attributeSource.ElementSize];

		for (int vertexIndex = 0; vertexIndex < vertexRemap.Length; vertexIndex++)
		{
			int remappedIndex = vertexRemap[vertexIndex];
			if (remappedIndex < 0) continue;

			int sourceOffset = attributeSource.ByteOffset + (vertexIndex * attributeSource.ByteStride);
			if (sourceOffset < 0 || (long)sourceOffset + attributeSource.ElementSize > binBytes.Length) return Array.Empty<byte>();

			Buffer.BlockCopy(binBytes, sourceOffset, compactBytes, remappedIndex * attributeSource.ElementSize, attributeSource.ElementSize);
		}

		return compactBytes;
	}

	private static int AppendCompactedAccessor(
		string attributeName,
		JsonObject sourceAccessor,
		int bufferViewIndex,
		int compactVertexCount,
		byte[] compactBytes,
		JsonArray accessors)
	{
		int accessorIndex = accessors.Count;
		var accessor = new JsonObject
		{
			["bufferView"] = bufferViewIndex,
			["byteOffset"] = 0,
			["componentType"] = sourceAccessor["componentType"]?.GetValue<int>() ?? 5126,
			["count"] = compactVertexCount,
			["type"] = sourceAccessor["type"]?.GetValue<string>() ?? "VEC3"
		};

		if (sourceAccessor["normalized"] is JsonNode normalizedNode) accessor["normalized"] = normalizedNode.DeepClone();
		if (attributeName == "POSITION") WritePositionBounds(compactBytes, compactVertexCount, accessor);

		accessors.Add(accessor);
		return accessorIndex;
	}

	private static void WritePositionBounds(byte[] positionBytes, int vertexCount, JsonObject accessor)
	{
		if (vertexCount <= 0 || positionBytes.Length < vertexCount * 12) return;

		var minimums = new float[3] { float.MaxValue, float.MaxValue, float.MaxValue };
		var maximums = new float[3] { float.MinValue, float.MinValue, float.MinValue };

		for (int vertexIndex = 0; vertexIndex < vertexCount; vertexIndex++)
		{
			int vertexOffset = vertexIndex * 12;
			for (int componentIndex = 0; componentIndex < 3; componentIndex++)
			{
				float value = ReadSingle(positionBytes, vertexOffset + (componentIndex * 4));
				if (value < minimums[componentIndex]) minimums[componentIndex] = value;
				if (value > maximums[componentIndex]) maximums[componentIndex] = value;
			}
		}

		accessor["min"] = new JsonArray(minimums[0], minimums[1], minimums[2]);
		accessor["max"] = new JsonArray(maximums[0], maximums[1], maximums[2]);
	}

	private static int AppendCompactedIndices(uint[] compactIndices, MemoryStream newBinStream, JsonArray bufferViews, JsonArray accessors)
	{
		AlignStream(newBinStream);

		uint maximumIndex = 0;
		for (int i = 0; i < compactIndices.Length; i++)
		{
			if (compactIndices[i] > maximumIndex) maximumIndex = compactIndices[i];
		}

		bool useShort = maximumIndex <= 65535;
		int byteOffset = (int)newBinStream.Position;
		WriteIndicesToStream(compactIndices, useShort, newBinStream);

		int bufferViewIndex = bufferViews.Count;
		bufferViews.Add(new JsonObject
		{
			["buffer"] = 0,
			["byteOffset"] = byteOffset,
			["byteLength"] = compactIndices.Length * (useShort ? 2 : 4),
			["target"] = 34963
		});

		int accessorIndex = accessors.Count;
		accessors.Add(new JsonObject
		{
			["bufferView"] = bufferViewIndex,
			["byteOffset"] = 0,
			["componentType"] = useShort ? 5123 : 5125,
			["count"] = compactIndices.Length,
			["type"] = "SCALAR"
		});

		return accessorIndex;
	}
}
