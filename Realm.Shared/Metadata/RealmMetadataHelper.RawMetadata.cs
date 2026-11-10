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

/// <summary>adding and removing the metadata block itself.</summary>
public static partial class RealmMetadataHelper
	{
	public static List<string> ExtractTags(string filePath)
	{
		var result = new List<string>();
		string? metaJson = ExtractMetadata(filePath);
		if (string.IsNullOrEmpty(metaJson)) return result;
		try
		{
			var node = JsonNode.Parse(metaJson);
			if (node is JsonObject obj && obj[AssetMetadataKeys.Tags] is JsonArray tagsArray)
			{
				foreach (var tagNode in tagsArray)
				{
					string? tag = tagNode?.ToString()?.Trim();
					if (!string.IsNullOrEmpty(tag) && !result.Contains(tag, StringComparer.OrdinalIgnoreCase))
					{
						result.Add(tag);
					}
				}
			}
		}
		catch
		{
		}

		return result;
	}

	public static bool SetTags(string filePath, IEnumerable<string> tags)
	{
		string ext = Path.GetExtension(filePath).ToLowerInvariant();
		if (ext is not (".rtex" or ".ranim" or ".rmesh" or ".raud")) return false;

		return UpdateMetadataField(filePath, metaObj =>
		{
			var cleanTags = (tags ?? Array.Empty<string>())
				.Where(t => !string.IsNullOrWhiteSpace(t))
				.Select(t => t.Trim().ToLowerInvariant())
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.Select(t => (JsonNode)JsonValue.Create(t)!)
				.ToArray();

			metaObj[AssetMetadataKeys.Tags] = new JsonArray(cleanTags);
		});
	}

	public static bool AddMetadata(string filePath, string realmMetadataJson)
	{
		if (!File.Exists(filePath)) return false;
		string ext = Path.GetExtension(filePath).ToLowerInvariant();
		switch (ext)
		{
			case ".rmesh":
				AddMetadataToRmesh(filePath, realmMetadataJson);
				return true;
			case ".rtex":
				AddMetadataToRtex(filePath, realmMetadataJson);
				return true;
			case ".ranim":
				AddMetadataToRanim(filePath, realmMetadataJson);
				return true;
			case ".raud":
				AddMetadataToRaud(filePath, realmMetadataJson);
				return true;
			case ".rkey":
				AddMetadataToRkey(filePath, realmMetadataJson);
				return true;
			default:
				throw new NotSupportedException($"Unsupported file format '{ext}' for metadata. Supported formats: .rmesh, .rtex, .raud, .ranim, .rkey");
		}
	}

	public static bool RemoveMetadata(string filePath)
	{
		if (!File.Exists(filePath)) return false;
		string ext = Path.GetExtension(filePath).ToLowerInvariant();
		switch (ext)
		{
			case ".rmesh":
				RemoveMetadataFromRmesh(filePath);
				return true;
			case ".rtex":
				RemoveMetadataFromRtex(filePath);
				return true;
			case ".ranim":
				RemoveMetadataFromRanim(filePath);
				return true;
			case ".raud":
				RemoveMetadataFromRaud(filePath);
				return true;
			case ".rkey":
				RemoveMetadataFromRkey(filePath);
				return true;
			default:
				throw new NotSupportedException($"Unsupported file format '{ext}' for metadata. Supported formats: .rmesh, .rtex, .raud, .ranim, .rkey");
		}
	}
}
