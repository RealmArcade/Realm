using System.Text.Json.Nodes;

namespace Realm.Shared;

/// <summary>dropping the accessors and buffer views that nothing references any more, and rebuilding the binary chunk without them.</summary>
public static partial class GlbManifestUtils
{
	public static byte[] CompactUnusedAccessors(byte[] glbBytes)
	{
		var (jsonNode, binChunk, version) = ParseGlb(glbBytes);
		if (jsonNode is not JsonObject root) return glbBytes;
		if (root["accessors"] is not JsonArray accessors || accessors.Count == 0) return glbBytes;
		if (root["bufferViews"] is not JsonArray bufferViews) return glbBytes;

		bool[] referencedAccessors = CollectReferencedAccessors(root, accessors.Count);
		int referencedCount = 0;
		for (int i = 0; i < referencedAccessors.Length; i++)
		{
			if (referencedAccessors[i]) referencedCount++;
		}

		if (referencedCount == accessors.Count) return glbBytes;

		int[] accessorRemap = BuildAccessorRemap(referencedAccessors);
		bool[] retainedBufferViews = CollectReferencedBufferViews(root, accessors, bufferViews.Count, referencedAccessors);

		using var newBinStream = new MemoryStream();
		int[] bufferViewOffsets = CopyRetainedBufferViews(bufferViews, retainedBufferViews, binChunk ?? Array.Empty<byte>(), newBinStream);
		int[] bufferViewRemap = BuildBufferViewRemap(retainedBufferViews);

		JsonArray rebuiltAccessors = RebuildAccessors(accessors, referencedAccessors, accessorRemap, bufferViewRemap);
		JsonArray rebuiltBufferViews = RebuildBufferViews(bufferViews, retainedBufferViews, bufferViewRemap, bufferViewOffsets);

		root["accessors"] = rebuiltAccessors;
		root["bufferViews"] = rebuiltBufferViews;

		RemapAccessorReferences(root, accessorRemap);
		RemapBufferViewReferences(root, bufferViewRemap);

		byte[] finalBin = newBinStream.ToArray();
		if (root["buffers"] is JsonArray buffers && buffers.Count > 0 && buffers[0] is JsonObject buffer0)
		{
			buffer0["byteLength"] = finalBin.Length;
		}

		return BuildGlb(root, finalBin, version);
	}

	private static bool[] CollectReferencedAccessors(JsonObject root, int accessorCount)
	{
		var referenced = new bool[accessorCount];

		CollectMeshAccessors(root, referenced);
		CollectAnimationAccessors(root, referenced);
		CollectSkinAccessors(root, referenced);

		return referenced;
	}

	private static void CollectMeshAccessors(JsonObject root, bool[] referenced)
	{
		if (root["meshes"] is not JsonArray meshes) return;

		foreach (var meshNode in meshes)
		{
			if (meshNode is not JsonObject meshObj || meshObj["primitives"] is not JsonArray primitives) continue;
			foreach (var primitiveNode in primitives)
			{
				if (primitiveNode is JsonObject primitiveObj) CollectPrimitiveAccessors(primitiveObj, referenced);
			}
		}
	}

	private static void CollectPrimitiveAccessors(JsonObject primitiveObj, bool[] referenced)
	{
		if (primitiveObj["attributes"] is JsonObject attributes)
		{
			foreach (var attribute in attributes)
			{
				MarkAccessor(attribute.Value, referenced);
			}
		}

		MarkAccessor(primitiveObj["indices"], referenced);

		if (primitiveObj["targets"] is not JsonArray targets) return;
		foreach (var targetNode in targets)
		{
			if (targetNode is not JsonObject targetObj) continue;
			foreach (var attribute in targetObj)
			{
				MarkAccessor(attribute.Value, referenced);
			}
		}
	}

	private static void CollectAnimationAccessors(JsonObject root, bool[] referenced)
	{
		if (root["animations"] is not JsonArray animations) return;

		foreach (var animationNode in animations)
		{
			if (animationNode is not JsonObject animationObj || animationObj["samplers"] is not JsonArray samplers) continue;
			foreach (var samplerNode in samplers)
			{
				if (samplerNode is not JsonObject samplerObj) continue;
				MarkAccessor(samplerObj["input"], referenced);
				MarkAccessor(samplerObj["output"], referenced);
			}
		}
	}

	private static void CollectSkinAccessors(JsonObject root, bool[] referenced)
	{
		if (root["skins"] is not JsonArray skins) return;

		foreach (var skinNode in skins)
		{
			if (skinNode is JsonObject skinObj) MarkAccessor(skinObj["inverseBindMatrices"], referenced);
		}
	}

	private static void MarkAccessor(JsonNode? accessorNode, bool[] referenced)
	{
		if (accessorNode == null) return;

		int accessorIndex = accessorNode.GetValue<int>();
		if (accessorIndex < 0 || accessorIndex >= referenced.Length) return;

		referenced[accessorIndex] = true;
	}

	private static int[] BuildAccessorRemap(bool[] referenced)
	{
		var remap = new int[referenced.Length];
		int nextIndex = 0;

		for (int i = 0; i < referenced.Length; i++)
		{
			remap[i] = referenced[i] ? nextIndex++ : -1;
		}

		return remap;
	}

	private static bool[] CollectReferencedBufferViews(JsonObject root, JsonArray accessors, int bufferViewCount, bool[] referencedAccessors)
	{
		var retained = new bool[bufferViewCount];

		for (int i = 0; i < accessors.Count; i++)
		{
			if (!referencedAccessors[i]) continue;
			if (accessors[i] is not JsonObject accessor) continue;
			if (!TryGetBufferViewIndex(accessor, out int bufferViewIndex)) continue;
			if (bufferViewIndex < 0 || bufferViewIndex >= bufferViewCount) continue;

			retained[bufferViewIndex] = true;
		}

		if (root["images"] is JsonArray images)
		{
			foreach (var imageNode in images)
			{
				if (imageNode is not JsonObject imageObj || !TryGetBufferViewIndex(imageObj, out int bufferViewIndex)) continue;
				if (bufferViewIndex < 0 || bufferViewIndex >= bufferViewCount) continue;

				retained[bufferViewIndex] = true;
			}
		}

		return retained;
	}

	private static bool TryGetBufferViewIndex(JsonObject owner, out int bufferViewIndex)
	{
		bufferViewIndex = -1;
		if (owner["bufferView"] is not JsonNode bufferViewNode) return false;

		bufferViewIndex = bufferViewNode.GetValue<int>();
		return true;
	}

	private static int[] CopyRetainedBufferViews(JsonArray bufferViews, bool[] retainedBufferViews, byte[] binChunk, MemoryStream newBinStream)
	{
		var offsets = new int[bufferViews.Count];

		for (int bufferViewIndex = 0; bufferViewIndex < bufferViews.Count; bufferViewIndex++)
		{
			if (!retainedBufferViews[bufferViewIndex]) continue;
			offsets[bufferViewIndex] = CopySingleBufferView(bufferViews[bufferViewIndex] as JsonObject, binChunk, newBinStream);
		}

		return offsets;
	}

	private static int CopySingleBufferView(JsonObject? bufferView, byte[] binChunk, MemoryStream newBinStream)
	{
		AlignStreamToFourBytes(newBinStream);
		int targetOffset = (int)newBinStream.Position;
		if (bufferView == null) return targetOffset;

		int sourceOffset = bufferView["byteOffset"]?.GetValue<int>() ?? 0;
		int sourceLength = bufferView["byteLength"]?.GetValue<int>() ?? 0;
		if (sourceOffset < 0 || sourceLength <= 0) return targetOffset;
		if ((long)sourceOffset + sourceLength > binChunk.Length) return targetOffset;

		newBinStream.Write(binChunk, sourceOffset, sourceLength);
		AlignStreamToFourBytes(newBinStream);

		return targetOffset;
	}

	private static void AlignStreamToFourBytes(MemoryStream stream)
	{
		while ((stream.Position % 4) != 0) stream.WriteByte(0);
	}

	private static int[] BuildBufferViewRemap(bool[] retainedBufferViews)
	{
		var remap = new int[retainedBufferViews.Length];
		int nextIndex = 0;

		for (int i = 0; i < retainedBufferViews.Length; i++)
		{
			remap[i] = retainedBufferViews[i] ? nextIndex++ : -1;
		}

		return remap;
	}

	private static JsonArray RebuildAccessors(JsonArray accessors, bool[] referencedAccessors, int[] accessorRemap, int[] bufferViewRemap)
	{
		var retainedAccessors = new JsonArray();

		for (int i = 0; i < accessors.Count; i++)
		{
			if (!referencedAccessors[i]) continue;
			if (accessors[i] is not JsonObject accessor) continue;

			var rebuiltAccessor = (JsonObject)accessor.DeepClone();
			if (TryGetBufferViewIndex(rebuiltAccessor, out int oldBufferViewIndex))
			{
				rebuiltAccessor["bufferView"] = bufferViewRemap[oldBufferViewIndex];
			}

			retainedAccessors.Add(rebuiltAccessor);
		}

		return retainedAccessors;
	}

	private static JsonArray RebuildBufferViews(JsonArray bufferViews, bool[] retainedBufferViews, int[] bufferViewRemap, int[] bufferViewOffsets)
	{
		var rebuiltBufferViews = new JsonArray();

		for (int i = 0; i < bufferViews.Count; i++)
		{
			if (!retainedBufferViews[i]) continue;
			if (bufferViews[i] is not JsonObject bufferView) continue;

			var rebuiltBufferView = (JsonObject)bufferView.DeepClone();
			rebuiltBufferView["byteOffset"] = bufferViewOffsets[i];

			rebuiltBufferViews.Add(rebuiltBufferView);
		}

		return rebuiltBufferViews;
	}

	private static void RemapAccessorReferences(JsonObject root, int[] accessorRemap)
	{
		if (root["meshes"] is not JsonArray meshes) return;

		foreach (var meshNode in meshes)
		{
			if (meshNode is not JsonObject meshObj || meshObj["primitives"] is not JsonArray primitives) continue;
			foreach (var primitiveNode in primitives)
			{
				if (primitiveNode is JsonObject primitiveObj) RemapPrimitiveAccessors(primitiveObj, accessorRemap);
			}
		}

		RemapAnimationAccessors(root, accessorRemap);
		RemapSkinAccessors(root, accessorRemap);
	}

	private static void RemapPrimitiveAccessors(JsonObject primitiveObj, int[] accessorRemap)
	{
		if (primitiveObj["attributes"] is JsonObject attributes)
		{
			var remappedAttributes = new JsonObject();
			foreach (var attribute in attributes)
			{
				remappedAttributes[attribute.Key] = RemapAccessorIndex(attribute.Value, accessorRemap);
			}

			primitiveObj["attributes"] = remappedAttributes;
		}

		primitiveObj["indices"] = RemapAccessorIndex(primitiveObj["indices"], accessorRemap);
		RemapPrimitiveTargets(primitiveObj, accessorRemap);
	}

	private static void RemapPrimitiveTargets(JsonObject primitiveObj, int[] accessorRemap)
	{
		if (primitiveObj["targets"] is not JsonArray targets) return;

		foreach (var targetNode in targets)
		{
			if (targetNode is not JsonObject targetObj) continue;

			var remappedTarget = new JsonObject();
			foreach (var attribute in targetObj)
			{
				remappedTarget[attribute.Key] = RemapAccessorIndex(attribute.Value, accessorRemap);
			}

			targetObj.Clear();
			foreach (var attribute in remappedTarget)
			{
				targetObj[attribute.Key] = attribute.Value?.DeepClone();
			}
		}
	}

	private static void RemapAnimationAccessors(JsonObject root, int[] accessorRemap)
	{
		if (root["animations"] is not JsonArray animations) return;

		foreach (var animationNode in animations)
		{
			if (animationNode is not JsonObject animationObj || animationObj["samplers"] is not JsonArray samplers) continue;
			foreach (var samplerNode in samplers)
			{
				if (samplerNode is not JsonObject samplerObj) continue;
				samplerObj["input"] = RemapAccessorIndex(samplerObj["input"], accessorRemap);
				samplerObj["output"] = RemapAccessorIndex(samplerObj["output"], accessorRemap);
			}
		}
	}

	private static void RemapSkinAccessors(JsonObject root, int[] accessorRemap)
	{
		if (root["skins"] is not JsonArray skins) return;

		foreach (var skinNode in skins)
		{
			if (skinNode is not JsonObject skinObj) continue;
			skinObj["inverseBindMatrices"] = RemapAccessorIndex(skinObj["inverseBindMatrices"], accessorRemap);
		}
	}

	private static JsonNode? RemapAccessorIndex(JsonNode? accessorNode, int[] accessorRemap)
	{
		if (accessorNode == null) return null;

		int accessorIndex = accessorNode.GetValue<int>();
		if (accessorIndex < 0 || accessorIndex >= accessorRemap.Length) return accessorNode;

		return JsonValue.Create(accessorRemap[accessorIndex]);
	}

	private static void RemapBufferViewReferences(JsonObject root, int[] bufferViewRemap)
	{
		if (root["images"] is not JsonArray images) return;

		foreach (var imageNode in images)
		{
			if (imageNode is not JsonObject imageObj) continue;
			if (!TryGetBufferViewIndex(imageObj, out int bufferViewIndex)) continue;
			if (bufferViewIndex < 0 || bufferViewIndex >= bufferViewRemap.Length) continue;

			imageObj["bufferView"] = bufferViewRemap[bufferViewIndex];
		}
	}
}
