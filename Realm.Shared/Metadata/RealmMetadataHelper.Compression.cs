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

/// <summary>the compression flag.</summary>
public static partial class RealmMetadataHelper
	{
	public static bool ExtractIsCompressed(string? metadataJson)
	{
		return TryExtractIsCompressed(metadataJson, out bool isCompressed) && isCompressed;
	}

	public static bool TryExtractIsCompressed(string? metadataJson, out bool isCompressed)
	{
		isCompressed = false;
		if (string.IsNullOrWhiteSpace(metadataJson)) return false;
		try
		{
			if (JsonNode.Parse(metadataJson) is not JsonObject obj) return false;

			var keys = new[] { AssetMetadataKeys.IsCompressed, "IsCompressed" };
			return ParseIsCompressedValue(obj, keys, out isCompressed);
		}
		catch
		{
		}

		return false;
	}

	private static bool ParseIsCompressedValue(JsonObject obj, string[] keys, out bool isCompressed)
	{
		isCompressed = false;
		foreach (var key in keys)
		{
			if (obj.TryGetPropertyValue(key, out var val) && val != null)
			{
				if (val.GetValueKind() == JsonValueKind.True)
				{
					isCompressed = true;
					return true;
				}

				if (val.GetValueKind() == JsonValueKind.False)
				{
					isCompressed = false;
					return true;
				}

				if (bool.TryParse(val.ToString(), out bool b))
				{
					isCompressed = b;
					return true;
				}
			}
		}

		return false;
	}
}
