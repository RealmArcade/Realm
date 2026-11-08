using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Realm.Shared.Textures;
using SkiaSharp;

namespace Realm.Shared;

/// <summary>injecting the optimization metadata.</summary>
public static partial class GlbManifestUtils
	{
	public static byte[] InjectOptimizationMetadata(
		byte[] glbBytes,
		Dictionary<string, object>? extraStats = null)
	{
		var (json, bin, glbVer) = ParseGlb(glbBytes);
		if (json is not JsonObject root) return glbBytes;

		JsonObject extras;
		if (root.TryGetPropertyValue("extras", out var extrasNode) && extrasNode is JsonObject existingExtras)
		{
			extras = existingExtras;
		}
		else
		{
			extras = new JsonObject();
			root["extras"] = extras;
		}

		extras["realm_optimize_completed"] = true;
		extras["realm_version"] = RealmVersion.GameBinaryVersion;
		extras["optimization_timestamp"] = DateTime.UtcNow.ToString("o");

		InjectAssetOptimizationMetadata(root);
		ApplyExtraStats(extraStats, extras);

		return BuildGlb(root, bin, glbVer);
	}

	private static void InjectAssetOptimizationMetadata(JsonObject root)
	{
		if (!root.ContainsKey("asset") || root["asset"] == null)
			root["asset"] = new JsonObject();

		if (root["asset"] is not JsonObject assetObj) return;

		if (!assetObj.ContainsKey("extras") || assetObj["extras"] == null)
			assetObj["extras"] = new JsonObject();

		if (assetObj["extras"] is JsonObject assetExtras)
			assetExtras["realm_optimize_completed"] = true;
	}

	private static void ApplyExtraStats(Dictionary<string, object>? extraStats, JsonObject extras)
	{
		if (extraStats == null) return;

		foreach (var kvp in extraStats)
		{
			if (kvp.Value is int i) extras[kvp.Key] = i;
			else if (kvp.Value is float f) extras[kvp.Key] = f;
			else if (kvp.Value is bool b) extras[kvp.Key] = b;
			else if (kvp.Value is string s) extras[kvp.Key] = s;
		}
	}
}
