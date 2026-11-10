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

/// <summary>the author and preferred file name fields.</summary>
public static partial class RealmMetadataHelper
	{
	public static bool SetAssetType(string filePath, string assetType)
	{
		if (!IsValidAssetTypeForExtension(filePath, assetType, out string canonical, out _))
		{
			return false;
		}

		return UpdateMetadataField(filePath, meta => meta[AssetMetadataKeys.AssetType] = canonical);
	}

	public static string? ExtractAuthor(string filePath)
	{
		string? metaJson = ExtractMetadata(filePath);
		if (string.IsNullOrEmpty(metaJson)) return null;
		try
		{
			var node = JsonNode.Parse(metaJson);
			return node?[AssetMetadataKeys.Author]?.ToString() ?? node?["Author"]?.ToString();
		}
		catch
		{
		}

		return null;
	}

	private static bool UpdateMetadataField(string filePath, Action<JsonObject> updateAction)
	{
		if (!File.Exists(filePath)) return false;
		string? existingMeta = ExtractMetadata(filePath);
		JsonObject metaObj;
		if (!string.IsNullOrEmpty(existingMeta))
		{
			try
			{
				metaObj = JsonNode.Parse(existingMeta)?.AsObject() ?? new JsonObject();
			}
			catch
			{
				metaObj = new JsonObject();
			}
		}
		else
		{
			metaObj = new JsonObject();
			string ext = Path.GetExtension(filePath).ToLowerInvariant();
			metaObj[AssetMetadataKeys.CreatedUtc] = DateTime.UtcNow.ToString("O");
			metaObj[AssetMetadataKeys.Format] = ext.TrimStart('.');
		}

		updateAction(metaObj);
		metaObj[AssetMetadataKeys.Blake3Hash] = ComputeBlake3(filePath);
		return AddMetadata(filePath, metaObj.ToJsonString());
	}

	public static bool SetAuthor(string filePath, string author)
	{
		return UpdateMetadataField(filePath, meta => meta[AssetMetadataKeys.Author] = author);
	}

	public static string? ExtractPreferredFileName(string filePath)
	{
		string? metaJson = ExtractMetadata(filePath);
		if (string.IsNullOrEmpty(metaJson)) return null;
		try
		{
			var node = JsonNode.Parse(metaJson);
			return node?[AssetMetadataKeys.PreferredFileName]?.ToString() ?? node?["preferredFileName"]?.ToString();
		}
		catch
		{
		}

		return null;
	}

	public static bool SetPreferredFileName(string filePath, string preferredFileName)
	{
		return UpdateMetadataField(filePath, meta => meta[AssetMetadataKeys.PreferredFileName] = preferredFileName);
	}
}
