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

/// <summary>the team colour flags and the chroma key that goes with them.</summary>
public static partial class RealmMetadataHelper
	{
	public static bool ExtractSupportsTeamColorFromMetadataJson(string? metaJson)
	{
		if (string.IsNullOrEmpty(metaJson)) return false;
		try
		{
			if (JsonNode.Parse(metaJson) is not JsonObject obj) return false;

			var booleanKeys = new[]
			{
				AssetMetadataKeys.TeamColor, "teamColor",
				"supports_team_color", "supportsTeamColor",
				"player_color", "playerColor",
				"has_player_color_mask", "hasPlayerColorMask",
				"has_player_color", "hasPlayerColor"
			};

			if (TryExtractBooleanTeamColor(obj, booleanKeys, out bool supportsColor))
			{
				return supportsColor;
			}

			var stringKeys = new[]
			{
				AssetMetadataKeys.ChromaKey, "chromaKey",
				AssetMetadataKeys.TargetHex, "targetHex"
			};

			return HasValidChromaKey(obj, stringKeys);
		}
		catch
		{
		}

		return false;
	}

	private static bool TryExtractBooleanTeamColor(JsonObject obj, string[] keys, out bool result)
	{
		result = false;
		foreach (var key in keys)
		{
			if (obj.TryGetPropertyValue(key, out var val) && val != null)
			{
				if (val.GetValueKind() == JsonValueKind.True)
				{
					result = true;
					return true;
				}

				if (val.GetValueKind() == JsonValueKind.False)
				{
					result = false;
					return true;
				}

				if (bool.TryParse(val.ToString(), out bool b))
				{
					result = b;
					return true;
				}
			}
		}

		return false;
	}

	private static bool HasValidChromaKey(JsonObject obj, string[] keys)
	{
		foreach (var key in keys)
		{
			if (obj.TryGetPropertyValue(key, out var val) && val != null)
			{
				if (val.GetValueKind() == JsonValueKind.True) return true;
				if (val.GetValueKind() == JsonValueKind.False) return false;

				string s = val.ToString().Trim();
				if (!string.IsNullOrEmpty(s) && !string.Equals(s, "none", StringComparison.OrdinalIgnoreCase))
				{
					return true;
				}
			}
		}

		return false;
	}

	public static bool? ExtractSupportsTeamColorWithMetadata(string? metaJson, string filePath)
	{
		if (!string.IsNullOrEmpty(metaJson))
		{
			if (ExtractSupportsTeamColorFromMetadataJson(metaJson))
			{
				return true;
			}

			try
			{
				var node = JsonNode.Parse(metaJson);
				if (node is JsonObject obj)
				{
					if (obj.TryGetPropertyValue(AssetMetadataKeys.TeamColor, out var tcVal) && tcVal != null && tcVal.GetValueKind() == JsonValueKind.False)
					{
						return false;
					}
				}
			}
			catch
			{
			}
		}

		string ext = Path.GetExtension(filePath).ToLowerInvariant();
		if (ext is ".rmesh" or ".glb" or ".gltf")
		{
			if (File.Exists(filePath))
			{
				return GlbTeamColorProbe.DetectSupportsTeamColor(filePath);
			}
		}

		return null;
	}

	public static bool? ExtractSupportsTeamColor(string filePath)
	{
		if (!File.Exists(filePath)) return null;
		string? metaJson = ExtractMetadata(filePath);
		return ExtractSupportsTeamColorWithMetadata(metaJson, filePath);
	}
}
