using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Realm.Shared.Textures;
using SkiaSharp;

namespace Realm.Shared;

/// <summary>stripping the optimization metadata back out.</summary>
public static partial class GlbManifestUtils
	{
	public static (byte[] UnoptimizedBytes, bool WasModified) StripOptimizationMetadata(byte[] glbBytes)
	{
		var (json, bin, glbVer) = ParseGlb(glbBytes);
		if (json is not JsonObject root) return (glbBytes, false);

		bool modified = false;

		modified |= StripExtras(root);
		modified |= StripAssetExtras(root);
		modified |= StripMsftLodExtensions(root);
		modified |= StripLodNodes(root);

		if (!modified) return (glbBytes, false);

		return (BuildGlb(root, bin, glbVer), true);
	}

	private static bool StripExtras(JsonObject root)
	{
		bool modified = false;
		if (root.TryGetPropertyValue("extras", out var extrasNode) && extrasNode is JsonObject extras)
		{
			if (extras.Remove("realm_optimize_completed")) modified = true;
			if (extras.Remove("realm_version")) modified = true;
			if (extras.Remove("optimization_timestamp")) modified = true;
			if (extras.Remove("msft_lod_embedded")) modified = true;
		}

		return modified;
	}

	private static bool StripAssetExtras(JsonObject root)
	{
		if (root.TryGetPropertyValue("asset", out var assetNode) && assetNode is JsonObject asset &&
		    asset.TryGetPropertyValue("extras", out var assetExtrasNode) && assetExtrasNode is JsonObject assetExtras)
		{
			if (assetExtras.Remove("realm_optimize_completed")) return true;
		}

		return false;
	}

	private static bool StripMsftLodExtensions(JsonObject root)
	{
		bool modified = false;
		modified |= RemoveExtension(root, "extensionsUsed", "MSFT_lod");
		modified |= RemoveExtension(root, "extensionsRequired", "MSFT_lod");
		modified |= RemoveNodeMsftLodExtensions(root);
		return modified;
	}
}
