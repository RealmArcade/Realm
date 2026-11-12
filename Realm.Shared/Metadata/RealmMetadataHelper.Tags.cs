using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using Blake3;
using Realm.EditorAPI;
using Realm.Shared.Animation;
using Realm.Shared.Audio;
using Realm.Shared.ModelOptimization;
using Realm.Shared.Textures;

namespace Realm.Shared.Metadata;

/// <summary>the tag list field.</summary>
public static partial class RealmMetadataHelper
	{
	public static bool SetSupportsTeamColor(string filePath, bool supportsTeamColor)
	{
		return UpdateMetadataField(filePath, meta => meta[AssetMetadataKeys.TeamColor] = supportsTeamColor);
	}

	public static string? ExtractChromaKey(string filePath)
	{
		string? metaJson = ExtractMetadata(filePath);
		return ExtractChromaKeyFromMetadataJson(metaJson);
	}

	public static string? ExtractChromaKeyFromMetadataJson(string? metaJson)
	{
		if (string.IsNullOrEmpty(metaJson)) return null;
		try
		{
			var node = JsonNode.Parse(metaJson);
			if (node is not JsonObject obj) return null;

			var keys = new[] { AssetMetadataKeys.ChromaKey, "chromaKey", AssetMetadataKeys.TargetHex, "targetHex" };
			foreach (var key in keys)
			{
				if (obj.TryGetPropertyValue(key, out var val) && val != null)
				{
					return val.ToString();
				}
			}
		}
		catch
		{
		}

		return null;
	}

	public static bool SetChromaKey(string filePath, string chromaKey)
	{
		return UpdateMetadataField(filePath, meta => meta[AssetMetadataKeys.ChromaKey] = chromaKey);
	}
}
