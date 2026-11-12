using System.Collections.Concurrent;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using Realm.Shared.Textures;
using SkiaSharp;

namespace Realm.Shared;

/// <summary>rebuilding the glb around the new albedo image.</summary>
public static partial class GlbInMemoryColorPreprocessor
	{
	private static byte[] RebuildGlbWithUpdatedAlbedoTexture(
		JsonObject root,
		byte[] binChunk,
		int albedoImageIndex,
		byte[] newAlbedoBytes,
		uint glbVersion)
	{
		var accessors = root["accessors"] as JsonArray ?? new JsonArray();
		var bufferViews = root["bufferViews"] as JsonArray ?? new JsonArray();
		var images = root["images"] as JsonArray ?? new JsonArray();
		var textures = root["textures"] as JsonArray ?? new JsonArray();

		var retainedBvIndices = CollectRetainedBufferViews(accessors, images, bufferViews.Count, albedoImageIndex);

		using var newBinStream = new MemoryStream();
		var oldBvToNewBv = new Dictionary<int, int>();
		var newBufferViewsList = new JsonArray();

		for (int oldBvIdx = 0; oldBvIdx < bufferViews.Count; oldBvIdx++)
		{
			ProcessRetainedBufferView(oldBvIdx, bufferViews, retainedBvIndices, binChunk, newBinStream, newBufferViewsList, oldBvToNewBv);
		}

		int newAlbedoBvIdx = AppendNewAlbedoBufferView(newBinStream, newAlbedoBytes, newBufferViewsList);

		UpdateAccessorBufferViews(accessors, oldBvToNewBv);
		UpdateImageBufferViews(images, albedoImageIndex, oldBvToNewBv);
		SetAlbedoImageProperties(images, albedoImageIndex, newAlbedoBvIdx);
		EnsureTexturesUseWebp(textures, images.Count);
		EnsureExtensionsUsedContainsWebp(root);

		root["bufferViews"] = newBufferViewsList;

		if (root["buffers"] is JsonArray buffers && buffers.Count > 0 && buffers[0] is JsonObject buf0)
		{
			buf0["byteLength"] = (int)newBinStream.Position;
		}

		byte[] newBin = newBinStream.ToArray();
		return GlbManifestUtils.BuildGlb(root, newBin, glbVersion);
	}

	private static HashSet<int> CollectRetainedBufferViews(JsonArray accessors, JsonArray images, int bufferViewsCount, int albedoImageIndex)
	{
		var retained = new HashSet<int>();
		CollectAccessorBufferViews(accessors, bufferViewsCount, retained);
		CollectImageBufferViews(images, bufferViewsCount, albedoImageIndex, retained);
		return retained;
	}

	private static void CollectAccessorBufferViews(JsonArray accessors, int bufferViewsCount, HashSet<int> retained)
	{
		foreach (var acc in accessors)
		{
			if (acc is JsonObject accObj && accObj.TryGetPropertyValue("bufferView", out var bvVal) && bvVal != null)
			{
				int bvIdx = bvVal.GetValue<int>();
				if (bvIdx >= 0 && bvIdx < bufferViewsCount)
				{
					retained.Add(bvIdx);
				}
			}
		}
	}

	private static void CollectImageBufferViews(JsonArray images, int bufferViewsCount, int albedoImageIndex, HashSet<int> retained)
	{
		for (int i = 0; i < images.Count; i++)
		{
			if (i == albedoImageIndex) continue;
			if (images[i] is JsonObject imgObj)
			{
				int imgBv = GlbTextureResolver.GetImageBufferViewIndex(imgObj);
				if (imgBv >= 0 && imgBv < bufferViewsCount)
				{
					retained.Add(imgBv);
				}
			}
		}
	}

	private static void ProcessRetainedBufferView(
		int oldBvIdx, JsonArray bufferViews, HashSet<int> retainedBvIndices,
		byte[] binChunk, MemoryStream newBinStream, JsonArray newBufferViewsList,
		Dictionary<int, int> oldBvToNewBv)
	{
		if (!retainedBvIndices.Contains(oldBvIdx) || bufferViews[oldBvIdx] is not JsonObject oldBv)
			return;

		int origOffset = GetIntValueOrDefault(oldBv, "byteOffset");
		int origLength = GetIntValueOrDefault(oldBv, "byteLength");

		AlignStreamTo4Bytes(newBinStream);
		int newOffset = (int)newBinStream.Position;

		CopyBufferViewData(binChunk, origOffset, origLength, newBinStream);

		var clonedBv = (JsonObject)oldBv.DeepClone();
		clonedBv["byteOffset"] = newOffset;
		clonedBv["buffer"] = 0;

		newBufferViewsList.Add(clonedBv);
		oldBvToNewBv[oldBvIdx] = newBufferViewsList.Count - 1;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static int GetIntValueOrDefault(JsonObject obj, string propertyName)
	{
		return obj[propertyName]?.GetValue<int>() ?? 0;
	}
}
