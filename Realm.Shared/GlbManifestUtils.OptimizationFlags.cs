using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Realm.Shared.Textures;
using SkiaSharp;

namespace Realm.Shared;

/// <summary>reading the optimization extras the pipeline writes.</summary>
public static partial class GlbManifestUtils
	{
	private static bool TryGetTrue(JsonObject? container, string propertyName)
	{
		if (container == null || !container.TryGetPropertyValue(propertyName, out var node) || node == null)
			return false;
		try
		{
			if (node.GetValueKind() == JsonValueKind.True) return true;
			if (node is JsonValue jv) return EvaluateJsonValue(jv);
			return false;
		}
		catch
		{
			return false;
		}
	}

	private static bool EvaluateJsonValue(JsonValue jv)
	{
		if (jv.TryGetValue<bool>(out var b)) return b;
		if (jv.TryGetValue<int>(out var num)) return num != 0;
		if (jv.TryGetValue<string>(out var str) && bool.TryParse(str, out var parsedB)) return parsedB;
		return false;
	}

	public static bool HasOptimizationFlag(byte[] glbBytes)
	{
		var (json, _, _) = ParseGlb(glbBytes);
		if (json is not JsonObject root) return false;

		if (HasCompletedExtras(root)) return true;
		if (HasCompletedAssetExtras(root)) return true;
		if (HasExtension(root, "extensionsUsed", "MSFT_lod")) return true;
		if (HasExtension(root, "extensionsRequired", "MSFT_lod")) return true;

		return false;
	}

	private static bool HasCompletedExtras(JsonObject root)
	{
		if (root.TryGetPropertyValue("extras", out var extrasNode) && extrasNode is JsonObject extras)
		{
			if (TryGetTrue(extras, "realm_optimize_completed")) return true;
			if (TryGetTrue(extras, "realm_decimate_completed")) return true;
		}

		return false;
	}

	private static bool HasCompletedAssetExtras(JsonObject root)
	{
		if (root.TryGetPropertyValue("asset", out var assetNode) && assetNode is JsonObject asset &&
		    asset.TryGetPropertyValue("extras", out var assetExtrasNode) && assetExtrasNode is JsonObject assetExtras)
		{
			if (TryGetTrue(assetExtras, "realm_optimize_completed")) return true;
			if (TryGetTrue(assetExtras, "realm_decimate_completed")) return true;
		}

		return false;
	}

	private static bool HasExtension(JsonObject root, string listName, string extensionName)
	{
		if (root.TryGetPropertyValue(listName, out var extNode) && extNode is JsonArray extArray)
		{
			foreach (var ext in extArray)
			{
				if (ext?.GetValue<string>() == extensionName) return true;
			}
		}

		return false;
	}
}
