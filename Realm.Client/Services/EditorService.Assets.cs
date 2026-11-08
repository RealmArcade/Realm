using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Realm.Client.Core;
using Realm.Client.Utils;
using Realm.EditorAPI;
using Realm.Shared.Metadata;
using Realm.Shared.Services;

namespace Realm.Client.Services;

public partial class EditorService
{
	public List<Asset> SearchAssets(AssetType[] assetTypes, string query, int limit = 5) =>
		EditorAssetOperations.SearchAssets(assetTypes, query, limit);

	public string ExtractSearchQuery(string templateSlug) => EditorAssetOperations.ExtractSearchQuery(templateSlug);

	public void ImportAssets(List<Asset> assets) => EditorAssetOperations.ImportAssets(assets);

	public static string? SnapshotTemplateJson(string wsPath, string templateSlug) =>
		EditorAssetOperations.SnapshotTemplateJson(wsPath, templateSlug);

	public static void RevertTemplateJson(string wsPath, string templateSlug, string? snapshotJson) =>
		EditorAssetOperations.RevertTemplateJson(wsPath, templateSlug, snapshotJson);

	public static string? GetTemplateSlugFromJsonObject(JsonObject obj) => TemplateJsonSearch.GetTemplateSlugFromJsonObject(obj);

	public void AssignRandomAssetsToTemplate(string templateSlug, bool overwriteExisting = false)
	{
		if (string.IsNullOrWhiteSpace(templateSlug))
		{
			return;
		}

		string searchQuery = EditorAssetOperations.ExtractSearchQuery(templateSlug);
		string wsPath = GetWorkspacePath();
		string metaPath = MetadataService.ResolveMetadataPath(wsPath);
		if (!File.Exists(metaPath))
		{
			return;
		}

		string jsonContent = File.ReadAllText(metaPath);
		var rootNode = JsonNode.Parse(jsonContent);
		if (rootNode is not JsonObject rootObj)
		{
			return;
		}

		JsonObject? foundTemplateObj = TemplateJsonSearch.FindTemplateOrEntryObject(rootObj, templateSlug);
		if (foundTemplateObj == null)
		{
			return;
		}

		var candidateProperties = new List<AssetPropertyTarget>();
		EditorAssetOperations.CollectAssetProperties(foundTemplateObj, candidateProperties, overwriteExisting, templateSlug);

		if (candidateProperties.Count == 0)
		{
			return;
		}

		var chosenAssets = new List<Asset>();
		var rng = new Random();

		foreach (var propTarget in candidateProperties)
		{
			var types = EditorAssetOperations.GetAssetTypesForProperty(propTarget, templateSlug);
			var (prefix, _) = TemplateIDHelper.ParseTemplateID(templateSlug);
			if (AssetTypeExtensions.TryParse(prefix, out AssetType defaultType) && types.Contains(defaultType))
			{
				types = [defaultType];
			}

			var searchResults = EditorAssetOperations.SearchAssets(types, searchQuery, 100);
			if (searchResults.Count == 0)
			{
				searchResults = EditorAssetOperations.SearchAssets(types, string.Empty, 100);
			}

			if (searchResults.Count > 0)
			{
				var chosen = searchResults[rng.Next(searchResults.Count)];
				propTarget.Parent[propTarget.PropertyName] = chosen.FileName;
				chosenAssets.Add(chosen);
			}
		}

		if (chosenAssets.Count > 0)
		{
			EditorAssetOperations.ImportAssets(chosenAssets);

			var options = new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
			File.WriteAllText(metaPath, rootObj.ToJsonString(options));
			this.LastMetadataSyncTime = File.GetLastWriteTimeUtc(metaPath).Ticks;
		}
	}

	public string? SnapshotTemplateJson(string templateSlug)
	{
		return EditorAssetOperations.SnapshotTemplateJson(GetWorkspacePath(), templateSlug);
	}

	public void RevertTemplateJson(string templateSlug, string? snapshotJson)
	{
		EditorAssetOperations.RevertTemplateJson(GetWorkspacePath(), templateSlug, snapshotJson);
		string metaPath = MetadataService.ResolveMetadataPath(GetWorkspacePath());
		if (File.Exists(metaPath))
		{
			this.LastMetadataSyncTime = File.GetLastWriteTimeUtc(metaPath).Ticks;
		}
	}

	public void AssignRandomAssetsToTemplates(TemplateType[] templateTypes, bool overwriteExisting = false, Action<int, int, string>? progress = null)
	{
		if (templateTypes == null || templateTypes.Length == 0)
		{
			return;
		}

		string wsPath = GetWorkspacePath();
		string metaPath = MetadataService.ResolveMetadataPath(wsPath);
		if (!File.Exists(metaPath))
		{
			return;
		}

		string jsonContent = File.ReadAllText(metaPath);
		var rootNode = JsonNode.Parse(jsonContent);
		if (rootNode is not JsonObject rootObj || !rootObj.TryGetPropertyValue("Templates", out var templatesNode) || templatesNode is not JsonObject templatesObj)
		{
			return;
		}

		var targetTypeNames = new HashSet<string>(templateTypes.Select(t => t.ToString()), StringComparer.OrdinalIgnoreCase);
		var slugsToProcess = new List<string>();

		foreach (var kvp in templatesObj)
		{
			if (!targetTypeNames.Contains(kvp.Key) || kvp.Value is not JsonArray arr)
			{
				continue;
			}

			foreach (var item in arr)
			{
				if (item is JsonObject obj)
				{
					string? slug = GetTemplateSlugFromJsonObject(obj);
					if (!string.IsNullOrWhiteSpace(slug))
					{
						slugsToProcess.Add(slug);
					}
				}
			}
		}

		for (int i = 0; i < slugsToProcess.Count; i++)
		{
			string slug = slugsToProcess[i];
			progress?.Invoke(i, slugsToProcess.Count, slug);
			AssignRandomAssetsToTemplate(slug, overwriteExisting);
		}

		progress?.Invoke(slugsToProcess.Count, slugsToProcess.Count, string.Empty);
	}

	public void AssignRandomAssetsToTemplates(TemplateType[] templateTypes, bool overwriteExisting = false)
	{
		AssignRandomAssetsToTemplates(templateTypes, overwriteExisting, null);
	}

	public void AssignRandomAssetsToTemplates(bool overwriteExisting = false)
	{
		AssignRandomAssetsToTemplates(overwriteExisting, null);
	}

	public void AssignRandomAssetsToTemplates(bool overwriteExisting, Action<int, int, string>? progress)
	{
		var allTemplateTypes = (TemplateType[])Enum.GetValues(typeof(TemplateType));
		AssignRandomAssetsToTemplates(allTemplateTypes, overwriteExisting, progress);
	}
}
