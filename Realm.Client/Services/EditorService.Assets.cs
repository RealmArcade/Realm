using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using LiteDB;
using Realm.Client.Core;
using Realm.Client.Network;
using Realm.Client.Utils;
using Realm.EditorAPI;
using Realm.Shared.Distribution;
using Realm.Shared.Metadata;
using Realm.Shared.Services;

namespace Realm.Client.Services;

public partial class EditorService
{
	[GeneratedRegex(@"[^a-zA-Z]+")]
	private static partial Regex NonAlphaRegex();

	private static HashSet<string>? GetAllowedTypeStrings(HashSet<AssetType>? allowedTypes)
	{
		if (allowedTypes == null || allowedTypes.Count == 0)
		{
			return null;
		}

		var typeStrings = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (var t in allowedTypes)
		{
			typeStrings.Add(t.ToString());
			typeStrings.Add(t.ToCanonicalString());
			string norm = RealmMetadataHelper.NormalizeAssetType(t.ToString());
			if (!string.IsNullOrEmpty(norm))
			{
				typeStrings.Add(norm);
			}
		}

		return typeStrings;
	}

	public List<Asset> SearchAssets(AssetType[] assetTypes, string query, int limit = 5)
	{
		string cacheFilePath = AssetIndexService.GetCacheFilePath();
		var connectionString = new ConnectionString
		{
			Filename = cacheFilePath,
			Connection = ConnectionType.Shared
		};

		var allowedTypes = assetTypes != null && assetTypes.Length > 0
			? new HashSet<AssetType>(assetTypes)
			: null;
		var allowedTypeStrings = GetAllowedTypeStrings(allowedTypes);

		if (string.IsNullOrWhiteSpace(query))
		{
			var allAssets = new List<IndexedAsset>();

			if (File.Exists(cacheFilePath))
			{
				using (var db = new LiteDatabase(connectionString))
				{
					var col = db.GetCollection<IndexedAsset>("assets");
					IEnumerable<IndexedAsset> candidates;

					if (allowedTypeStrings != null && allowedTypeStrings.Count > 0)
					{
						var bsonTypes = allowedTypeStrings.Select(t => new BsonValue(t)).ToArray();
						candidates = col.Find(Query.In("AssetType", bsonTypes));
					}
					else
					{
						candidates = col.FindAll();
					}

					foreach (var candidate in candidates)
					{
						if (candidate == null) continue;

						var mappedType = MapStringToAssetType(candidate.AssetType, candidate.Extension, candidate.FilePath ?? candidate.DirectoryPath);
						if (allowedTypes != null && !allowedTypes.Contains(mappedType))
						{
							continue;
						}

						allAssets.Add(candidate);
					}
				}
			}

			if (allAssets.Count == 0)
			{
				allAssets = FallbackScanAssets(allowedTypes);
			}

			var rng = new Random();
			rng.Shuffle(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(allAssets));

			return allAssets
				.Take(limit)
				.Select(ToEditorAsset)
				.ToList();
		}

		if (!File.Exists(cacheFilePath))
		{
			var fallbackResults = FallbackScanAssets(allowedTypes);
			var rng = new Random();
			rng.Shuffle(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(fallbackResults));
			return fallbackResults
				.Take(limit)
				.Select(ToEditorAsset)
				.ToList();
		}

		string[] terms = NonAlphaRegex().Split(query)
			.Where(t => !string.IsNullOrWhiteSpace(t))
			.Select(t => t.Trim().ToLowerInvariant())
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToArray();

		if (terms.Length == 0)
		{
			return new List<Asset>();
		}

		var indexedAssetMap = new Dictionary<string, IndexedAsset>(StringComparer.OrdinalIgnoreCase);

		using (var db = new LiteDatabase(connectionString))
		{
			var col = db.GetCollection<IndexedAsset>("assets");
			BsonValue[]? bsonTypes = allowedTypeStrings != null && allowedTypeStrings.Count > 0
				? allowedTypeStrings.Select(t => new BsonValue(t)).ToArray()
				: null;

			foreach (string term in terms)
			{
				var termQuery = Query.Or(
					Query.Contains("Tags", term),
					Query.Contains("FileName", term)
				);

				var finalQuery = bsonTypes != null
					? Query.And(termQuery, Query.In("AssetType", bsonTypes))
					: termQuery;

				var subCandidates = col.Find(finalQuery);

				foreach (var candidate in subCandidates)
				{
					if (candidate == null) continue;
					string key = !string.IsNullOrEmpty(candidate.Blake3) ? candidate.Blake3 : candidate.FilePath;
					if (string.IsNullOrEmpty(key)) continue;

					if (!indexedAssetMap.ContainsKey(key))
					{
						indexedAssetMap[key] = candidate;
					}
				}
			}
		}

		var filteredAssets = new List<IndexedAsset>();

		foreach (var candidate in indexedAssetMap.Values)
		{
			var mappedType = MapStringToAssetType(candidate.AssetType, candidate.Extension, candidate.FilePath ?? candidate.DirectoryPath);
			if (allowedTypes != null && !allowedTypes.Contains(mappedType))
			{
				continue;
			}

			filteredAssets.Add(candidate);
		}

		var rankedAssets = filteredAssets
			.Select(asset => new
			{
				Asset = asset,
				MatchCount = CountMatchingTerms(asset, terms)
			})
			.Where(item => item.MatchCount > 0)
			.OrderByDescending(item => item.MatchCount)
			.ThenBy(item => item.Asset.FileName, StringComparer.OrdinalIgnoreCase)
			.Take(limit)
			.Select(item => ToEditorAsset(item.Asset))
			.ToList();

		return rankedAssets;
	}

	public string ExtractSearchQuery(string templateSlug)
	{
		if (string.IsNullOrWhiteSpace(templateSlug))
		{
			return string.Empty;
		}

		string wsPath = GetWorkspacePath();
		string metaPath = MetadataService.ResolveMetadataPath(wsPath);
		if (!File.Exists(metaPath))
		{
			return string.Empty;
		}

		string jsonContent = File.ReadAllText(metaPath);
		var rootNode = JsonNode.Parse(jsonContent);
		if (rootNode is not JsonObject rootObj)
		{
			return string.Empty;
		}

		JsonObject? foundTemplateObj = FindTemplateOrEntryObject(rootObj, templateSlug);
		if (foundTemplateObj == null)
		{
			return string.Empty;
		}

		var op1Terms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		var assetReferences = new List<string>();
		TraverseJsonForTermsAndAssetReferences(foundTemplateObj, op1Terms, assetReferences);

		var op2Terms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (string assetRef in assetReferences)
		{
			ExtractTermsFromAssetReference(wsPath, assetRef, op2Terms);
		}

		var unionedTerms = op1Terms.Union(op2Terms, StringComparer.OrdinalIgnoreCase).ToList();
		var validTerms = new List<string>();

		foreach (string term in unionedTerms)
		{
			if (string.IsNullOrWhiteSpace(term)) continue;

			var results = SearchAssets(Array.Empty<AssetType>(), term, 1);
			if (results.Count > 0)
			{
				validTerms.Add(term);
			}
		}

		return string.Join(" ", validTerms);
	}

	public void ImportAssets(List<Asset> assets)
	{
		if (assets == null || assets.Count == 0)
		{
			return;
		}

		string wsPath = GetWorkspacePath();
		var manifest = MapFileService.LoadManifest(wsPath);
		manifest.Assets ??= new MapManifestAssets();

		var existingAssetsDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		MapManifest.FlattenAssetsInto(existingAssetsDict, manifest.Assets);

		var seenHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (var val in existingAssetsDict.Values)
		{
			if (!string.IsNullOrEmpty(val))
			{
				seenHashes.Add(ContentAddressableStorage.NormalizeBlake3Hash(val));
			}
		}

		foreach (var asset in assets)
		{
			if (asset == null || string.IsNullOrEmpty(asset.Blake3))
			{
				continue;
			}

			string normalizedHash = ContentAddressableStorage.NormalizeBlake3Hash(asset.Blake3);
			if (seenHashes.Contains(normalizedHash) || existingAssetsDict.ContainsKey(asset.FileName))
			{
				continue;
			}

			string? casPath = MapAssetManager.Storage.FindAssetFilePath(normalizedHash);
			if (string.IsNullOrEmpty(casPath) || !File.Exists(casPath))
			{
				if (!string.IsNullOrEmpty(asset.FilePath) && File.Exists(asset.FilePath))
				{
					casPath = asset.FilePath;
				}
				else
				{
					continue;
				}
			}

			string category = MapAssetTypeToCategoryString(asset.AssetType);
			string targetSubFolder = MapCategoryToDestinationSubFolder(category);
			string targetDir = Path.Combine(wsPath, "Assets", targetSubFolder);
			Directory.CreateDirectory(targetDir);

			string targetFilePath = Path.Combine(targetDir, asset.FileName);
			if (!File.Exists(targetFilePath))
			{
				File.Copy(casPath, targetFilePath, true);
			}

			MapAssetHelper.UpdateManifestAsset(wsPath, category, asset.FileName, normalizedHash);
			seenHashes.Add(normalizedHash);
			existingAssetsDict[asset.FileName] = normalizedHash;
		}
	}

	public void AssignRandomAssetsToTemplate(string templateSlug, bool overwriteExisting = false)
	{
		if (string.IsNullOrWhiteSpace(templateSlug))
		{
			return;
		}

		string searchQuery = ExtractSearchQuery(templateSlug);
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

		JsonObject? foundTemplateObj = FindTemplateOrEntryObject(rootObj, templateSlug);
		if (foundTemplateObj == null)
		{
			return;
		}

		var candidateProperties = new List<AssetPropertyTarget>();
		CollectAssetProperties(foundTemplateObj, candidateProperties, overwriteExisting, templateSlug);

		if (candidateProperties.Count == 0)
		{
			return;
		}

		var chosenAssets = new List<Asset>();
		var rng = new Random();

		foreach (var propTarget in candidateProperties)
		{
			var types = GetAssetTypesForProperty(propTarget, templateSlug);
			var searchResults = SearchAssets(types, searchQuery, 100);
			if (searchResults.Count == 0)
			{
				searchResults = SearchAssets(types, string.Empty, 100);
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
			ImportAssets(chosenAssets);

			var options = new JsonSerializerOptions { WriteIndented = true };
			File.WriteAllText(metaPath, rootObj.ToJsonString(options));
			this.LastMetadataSyncTime = File.GetLastWriteTimeUtc(metaPath).Ticks;
		}
	}

	public string? SnapshotTemplateJson(string templateSlug)
	{
		if (string.IsNullOrWhiteSpace(templateSlug))
		{
			return null;
		}

		string wsPath = GetWorkspacePath();
		string metaPath = MetadataService.ResolveMetadataPath(wsPath);
		if (!File.Exists(metaPath))
		{
			return null;
		}

		string jsonContent = File.ReadAllText(metaPath);
		var rootNode = JsonNode.Parse(jsonContent);
		if (rootNode is not JsonObject rootObj)
		{
			return null;
		}

		JsonObject? found = FindTemplateOrEntryObject(rootObj, templateSlug);
		return found?.ToJsonString();
	}

	public void RevertTemplateJson(string templateSlug, string? snapshotJson)
	{
		if (string.IsNullOrWhiteSpace(templateSlug))
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
		if (rootNode is not JsonObject rootObj)
		{
			return;
		}

		bool modified = false;

		if (rootObj.TryGetPropertyValue("Templates", out var templatesNode) && templatesNode is JsonObject templatesObj)
		{
			foreach (var kvp in templatesObj)
			{
				if (kvp.Value is JsonArray arr)
				{
					for (int i = 0; i < arr.Count; i++)
					{
						if (arr[i] is JsonObject obj && IsTemplateMatch(obj, templateSlug))
						{
							if (snapshotJson != null)
							{
								arr[i] = JsonNode.Parse(snapshotJson);
							}
							else
							{
								arr.RemoveAt(i);
							}
							modified = true;
							break;
						}
					}
				}
				if (modified) break;
			}
		}

		if (!modified)
		{
			string[] dictionaryKeys = { "textures", "vfx_spritesheets", "decals", "SpawnShader", "vfx" };
			var (_, slug) = TemplateIDHelper.ParseTemplateID(templateSlug);

			foreach (string dictKey in dictionaryKeys)
			{
				if (rootObj.TryGetPropertyValue(dictKey, out var dictNode) && dictNode is JsonObject dictObj)
				{
					string? matchedKey = null;
					foreach (var kvp in dictObj)
					{
						if (string.Equals(kvp.Key, templateSlug, StringComparison.OrdinalIgnoreCase) ||
						    (!string.IsNullOrEmpty(slug) && string.Equals(kvp.Key, slug, StringComparison.OrdinalIgnoreCase)) ||
						    (!string.IsNullOrEmpty(slug) && string.Equals(Path.GetFileNameWithoutExtension(kvp.Key), slug, StringComparison.OrdinalIgnoreCase)))
						{
							matchedKey = kvp.Key;
							break;
						}
					}

					if (matchedKey != null)
					{
						if (snapshotJson != null)
						{
							dictObj[matchedKey] = JsonNode.Parse(snapshotJson);
						}
						else
						{
							dictObj.Remove(matchedKey);
						}
						modified = true;
						break;
					}
				}
			}
		}

		if (modified)
		{
			var options = new JsonSerializerOptions { WriteIndented = true };
			File.WriteAllText(metaPath, rootObj.ToJsonString(options));
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

	private int CountMatchingTerms(IndexedAsset asset, string[] terms)
	{
		int count = 0;
		foreach (string term in terms)
		{
			bool matches = (asset.Tags != null && asset.Tags.Any(t => t.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0)) ||
			               asset.FileName.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0 ||
			               asset.FilePath.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0 ||
			               (!string.IsNullOrEmpty(asset.MapName) && asset.MapName.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0);
			if (matches)
			{
				count++;
			}
		}

		return count;
	}

	private Asset ToEditorAsset(IndexedAsset item)
	{
		return new Asset
		{
			FilePath = item.FilePath,
			FileName = item.FileName,
			Extension = item.Extension,
			Blake3 = item.Blake3 ?? string.Empty,
			AssetType = MapStringToAssetType(item.AssetType, item.Extension, item.FilePath ?? item.DirectoryPath),
			Tags = item.Tags != null ? new List<string>(item.Tags) : new List<string>()
		};
	}

	private static readonly Dictionary<string, AssetType> PathSegmentToAssetType = new(StringComparer.OrdinalIgnoreCase)
	{
		["decals"] = AssetType.Decal,
		["textures"] = AssetType.Terrain,
		["terrain"] = AssetType.Terrain,
		["icons"] = AssetType.Icon,
		["noise"] = AssetType.Noise,
		["ribbons"] = AssetType.Ribbon,
		["skyboxes"] = AssetType.Skybox,
		["vfx_spritesheets"] = AssetType.Spritesheet,
		["vfx_radial"] = AssetType.VfxRadial,
		["vfx_vertical"] = AssetType.VfxVertical,
		["units"] = AssetType.Character,
		["characters"] = AssetType.Character,
		["buildings"] = AssetType.Building,
		["props"] = AssetType.Prop,
		["items"] = AssetType.Item,
		["attachments"] = AssetType.Item,
		["projectiles"] = AssetType.Item,
		["animations"] = AssetType.Animation,
		["music"] = AssetType.Music,
		["sfx"] = AssetType.SoundEffect,
		["shaders"] = AssetType.Shader
	};

	private AssetType MapStringToAssetType(string? rawType, string extension, string? pathHint = null)
	{
		if (AssetTypeExtensions.TryParse(rawType, out var parsed))
		{
			return parsed;
		}

		if (!string.IsNullOrEmpty(pathHint))
		{
			string normalizedHint = pathHint.Replace('\\', '/');
			string[] segments = normalizedHint.Split('/', StringSplitOptions.RemoveEmptyEntries);
			foreach (var segment in segments.Reverse())
			{
				if (PathSegmentToAssetType.TryGetValue(segment, out var matchedType))
				{
					return matchedType;
				}
				if (AssetTypeExtensions.TryParse(segment, out var segmentParsed))
				{
					return segmentParsed;
				}
			}
		}

		string ext = (extension ?? string.Empty).ToLowerInvariant();
		return ext switch
		{
			".rmesh" => AssetType.Character,
			".ranim" => AssetType.Animation,
			".raud" => AssetType.SoundEffect,
			".gdshader" => AssetType.Shader,
			_ => AssetType.Terrain
		};
	}

	private string MapAssetTypeToCategoryString(AssetType assetType)
	{
		return assetType switch
		{
			AssetType.Character => "Character",
			AssetType.Building => "Building",
			AssetType.Prop => "Prop",
			AssetType.Item => "Item",
			AssetType.Decal => "Decal",
			AssetType.Icon => "Icon",
			AssetType.Noise => "Noise",
			AssetType.Ribbon => "Ribbon",
			AssetType.Skybox => "Skybox",
			AssetType.Spritesheet => "Spritesheet",
			AssetType.Terrain => "Terrain",
			AssetType.VfxRadial => "vfx_radial",
			AssetType.VfxVertical => "vfx_vertical",
			AssetType.Animation => "Animation",
			AssetType.Music => "Music",
			AssetType.SoundEffect => "SoundEffect",
			AssetType.Shader => "Shader",
			_ => "Other"
		};
	}

	private string MapCategoryToDestinationSubFolder(string category)
	{
		return category switch
		{
			"Character" => "models/units",
			"Building" => "models/buildings",
			"Prop" => "models/props",
			"Item" => "models/items",
			"Spritesheet" or "vfx_spritesheets" => "vfx_spritesheets",
			"vfx_radial" => "vfx_radial",
			"vfx_vertical" => "vfx_vertical",
			"Animation" => "animations",
			"SoundEffect" => "audio/sfx",
			"Music" => "audio/music",
			"Icon" => "icons",
			"Decal" => "decals",
			"Ribbon" => "ribbons",
			"Noise" => "noise",
			"Skybox" => "skyboxes",
			"Terrain" => "textures",
			"Shader" => "shaders",
			_ => category.ToLowerInvariant()
		};
	}

	private JsonObject? FindTemplateOrEntryObject(JsonObject rootObj, string templateSlug)
	{
		if (rootObj.TryGetPropertyValue("Templates", out var templatesNode) && templatesNode is JsonObject templatesObj)
		{
			foreach (var kvp in templatesObj)
			{
				if (kvp.Value is JsonArray arr)
				{
					foreach (var item in arr)
					{
						if (item is JsonObject obj && IsTemplateMatch(obj, templateSlug))
						{
							return obj;
						}
					}
				}
			}
		}

		string[] dictionaryKeys = { "textures", "vfx_spritesheets", "decals", "SpawnShader" };
		var (_, slug) = TemplateIDHelper.ParseTemplateID(templateSlug);

		foreach (string dictKey in dictionaryKeys)
		{
			if (rootObj.TryGetPropertyValue(dictKey, out var dictNode) && dictNode is JsonObject dictObj)
			{
				foreach (var kvp in dictObj)
				{
					if (string.Equals(kvp.Key, templateSlug, StringComparison.OrdinalIgnoreCase) ||
					    (!string.IsNullOrEmpty(slug) && string.Equals(kvp.Key, slug, StringComparison.OrdinalIgnoreCase)) ||
					    (!string.IsNullOrEmpty(slug) && string.Equals(Path.GetFileNameWithoutExtension(kvp.Key), slug, StringComparison.OrdinalIgnoreCase)))
					{
						if (kvp.Value is JsonObject itemObj)
						{
							return itemObj;
						}
					}
				}
			}
		}

		return null;
	}

	private bool IsTemplateMatch(JsonObject obj, string templateSlug)
	{
		string? slug = GetTemplateSlugFromJsonObject(obj);
		if (string.IsNullOrEmpty(slug)) return false;

		if (string.Equals(slug, templateSlug, StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}

		if (slug.Contains('/'))
		{
			var parts = slug.Split('/');
			if (parts.Length > 1 && string.Equals(parts[1], templateSlug, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}

		var (_, parsedSlug) = TemplateIDHelper.ParseTemplateID(templateSlug);
		if (!string.IsNullOrEmpty(parsedSlug))
		{
			if (string.Equals(slug, parsedSlug, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}

			if (slug.Contains('/'))
			{
				var parts = slug.Split('/');
				if (parts.Length > 1 && string.Equals(parts[1], parsedSlug, StringComparison.OrdinalIgnoreCase))
				{
					return true;
				}
			}
		}

		return false;
	}

	private string? GetTemplateSlugFromJsonObject(JsonObject obj)
	{
		if (obj.TryGetPropertyValue("TemplateID", out var tidNode) && tidNode != null)
		{
			return tidNode.ToString();
		}
		if (obj.TryGetPropertyValue("template_id", out var tidNodeLower) && tidNodeLower != null)
		{
			return tidNodeLower.ToString();
		}
		if (obj.TryGetPropertyValue("AttachmentId", out var aidNode) && aidNode != null)
		{
			return aidNode.ToString();
		}
		if (obj.TryGetPropertyValue("VfxId", out var vidNode) && vidNode != null)
		{
			return vidNode.ToString();
		}
		if (obj.TryGetPropertyValue("DecalId", out var didNode) && didNode != null)
		{
			return didNode.ToString();
		}
		if (obj.TryGetPropertyValue("decal_id", out var didNodeLower) && didNodeLower != null)
		{
			return didNodeLower.ToString();
		}

		return null;
	}

	private void TraverseJsonForTermsAndAssetReferences(JsonNode node, HashSet<string> op1Terms, List<string> assetReferences)
	{
		if (node is JsonObject obj)
		{
			foreach (var kvp in obj)
			{
				if (kvp.Value == null) continue;

				if (kvp.Value is JsonValue val && val.TryGetValue<string>(out string? strVal) && !string.IsNullOrWhiteSpace(strVal))
				{
					if (IsAssetExtension(strVal))
					{
						assetReferences.Add(strVal);
					}
					else
					{
						SplitAndAddTerms(strVal, op1Terms);
					}
				}
				else
				{
					TraverseJsonForTermsAndAssetReferences(kvp.Value, op1Terms, assetReferences);
				}
			}
		}
		else if (node is JsonArray arr)
		{
			foreach (var item in arr)
			{
				if (item == null) continue;

				if (item is JsonValue val && val.TryGetValue<string>(out string? strVal) && !string.IsNullOrWhiteSpace(strVal))
				{
					if (IsAssetExtension(strVal))
					{
						assetReferences.Add(strVal);
					}
					else
					{
						SplitAndAddTerms(strVal, op1Terms);
					}
				}
				else
				{
					TraverseJsonForTermsAndAssetReferences(item, op1Terms, assetReferences);
				}
			}
		}
	}

	private bool IsAssetExtension(string value)
	{
		string lower = value.ToLowerInvariant();
		return lower.EndsWith(".rtex") || lower.EndsWith(".raud") || lower.EndsWith(".rmesh") || lower.EndsWith(".ranim");
	}

	private void SplitAndAddTerms(string text, HashSet<string> termsSet)
	{
		string[] words = NonAlphaRegex().Split(text);
		foreach (string word in words)
		{
			if (!string.IsNullOrWhiteSpace(word) && word.Length > 1)
			{
				termsSet.Add(word.Trim().ToLowerInvariant());
			}
		}
	}

	private void ExtractTermsFromAssetReference(string wsPath, string assetRef, HashSet<string> termsSet)
	{
		string fileName = Path.GetFileName(assetRef);
		string fileNameWithoutExt = Path.GetFileNameWithoutExtension(assetRef);
		SplitAndAddTerms(fileNameWithoutExt, termsSet);

		string? fullDiskPath = null;
		if (File.Exists(assetRef))
		{
			fullDiskPath = assetRef;
		}
		else
		{
			string directWs = Path.Combine(wsPath, assetRef.TrimStart('/', '\\'));
			if (File.Exists(directWs))
			{
				fullDiskPath = directWs;
			}
			else
			{
				string assetsWs = Path.Combine(wsPath, "Assets", assetRef.TrimStart('/', '\\'));
				if (File.Exists(assetsWs))
				{
					fullDiskPath = assetsWs;
				}
				else
				{
					var manifest = MapFileService.LoadManifest(wsPath);
					if (manifest.Assets != null)
					{
						var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
						MapManifest.FlattenAssetsInto(dict, manifest.Assets);
						if (dict.TryGetValue(fileName, out string? hash) && !string.IsNullOrEmpty(hash))
						{
							fullDiskPath = MapAssetManager.Storage.FindAssetFilePath(hash);
						}
					}
				}
			}
		}

		if (fullDiskPath != null && File.Exists(fullDiskPath))
		{
			string? metaJson = RealmMetadataHelper.ExtractMetadata(fullDiskPath);
			if (!string.IsNullOrEmpty(metaJson))
			{
				try
				{
					var metaObj = JsonNode.Parse(metaJson) as JsonObject;
					if (metaObj != null && metaObj.TryGetPropertyValue("tags", out var tagsNode) && tagsNode is JsonArray tagArr)
					{
						foreach (var t in tagArr)
						{
							if (t != null)
							{
								SplitAndAddTerms(t.ToString(), termsSet);
							}
						}
					}
				}
				catch
				{
				}
			}
		}
	}

	private List<IndexedAsset> FallbackScanAssets(HashSet<AssetType>? allowedTypes)
	{
		var results = new List<IndexedAsset>();
		var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		string wsPath = GetWorkspacePath();
		var searchDirs = new List<string>();

		if (Directory.Exists(wsPath))
		{
			string wsAssets = Path.Combine(wsPath, "Assets");
			if (Directory.Exists(wsAssets)) searchDirs.Add(wsAssets);
		}

		string templateDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "MapTemplate", "Assets"));
		if (Directory.Exists(templateDir))
		{
			searchDirs.Add(templateDir);
		}
		else
		{
			string altTemplateDir = @"D:\git\Realm\Realm\MapTemplate\Assets";
			if (Directory.Exists(altTemplateDir)) searchDirs.Add(altTemplateDir);
		}

		foreach (string baseDir in searchDirs)
		{
			try
			{
				foreach (string file in Directory.EnumerateFiles(baseDir, "*.*", SearchOption.AllDirectories))
				{
					string ext = Path.GetExtension(file).ToLowerInvariant();
					if (ext is not (".rtex" or ".raud" or ".rmesh" or ".ranim" or ".gdshader"))
					{
						continue;
					}

					string fileName = Path.GetFileName(file);
					if (seenPaths.Contains(fileName))
					{
						continue;
					}

					string relativePath = Path.GetRelativePath(baseDir, file).Replace('\\', '/');
					var mappedType = MapStringToAssetType(null, ext, relativePath);
					if (allowedTypes != null && !allowedTypes.Contains(mappedType))
					{
						continue;
					}

					seenPaths.Add(fileName);
					results.Add(new IndexedAsset
					{
						FileName = fileName,
						FilePath = file,
						Extension = ext,
						AssetType = MapAssetTypeToCategoryString(mappedType),
						DirectoryPath = Path.GetDirectoryName(file) ?? string.Empty,
						Blake3 = Path.GetFileNameWithoutExtension(fileName)
					});
				}
			}
			catch
			{
			}
		}

		return results;
	}

	private void CollectAssetProperties(JsonObject obj, List<AssetPropertyTarget> properties, bool overwriteExisting, string? templateSlug = null)
	{
		EnsureKnownTemplateAssetProperties(obj, templateSlug);

		foreach (var kvp in obj)
		{
			if (kvp.Value is JsonValue val && val.TryGetValue<string>(out string? strVal))
			{
				bool isAsset = IsKnownAssetPropertyOrExtension(kvp.Key, strVal);
				if (isAsset)
				{
					if (string.IsNullOrWhiteSpace(strVal) || overwriteExisting)
					{
						properties.Add(new AssetPropertyTarget
						{
							Parent = obj,
							PropertyName = kvp.Key,
							CurrentValue = strVal ?? string.Empty
						});
					}
				}
			}
			else if (kvp.Value is JsonObject childObj)
			{
				CollectAssetProperties(childObj, properties, overwriteExisting, templateSlug);
			}
		}
	}

	private void EnsureKnownTemplateAssetProperties(JsonObject obj, string? templateSlug = null)
	{
		string? slug = (templateSlug ?? GetTemplateSlugFromJsonObject(obj))?.ToLowerInvariant();

		if (obj.ContainsKey("Columns") || obj.ContainsKey("Rows") || obj.ContainsKey("Fps") ||
		    (slug != null && (slug.StartsWith("spritesheet/") || slug.Contains("spritesheet"))))
		{
			if (!obj.ContainsKey("TexturePath") && !obj.ContainsKey("texture_path"))
			{
				obj["TexturePath"] = string.Empty;
			}
		}

		if (obj.ContainsKey("AttackCooldown") || obj.ContainsKey("ProjectileSpeed") || obj.ContainsKey("TrajectoryType") ||
		    (slug != null && (slug.StartsWith("weapon/") || slug.Contains("weapon"))))
		{
			string[] weaponAssetProps = { "ProjectileModelPath", "RibbonTexture", "NoiseTexture", "AttackSound", "ImpactSound", "ImpactVisualEffect" };
			foreach (var prop in weaponAssetProps)
			{
				if (!obj.ContainsKey(prop))
				{
					obj[prop] = string.Empty;
				}
			}
		}

		if (obj.ContainsKey("ManaCost") || obj.ContainsKey("Cooldown") || obj.ContainsKey("AreaOfEffectRadius") ||
		    (slug != null && (slug.StartsWith("ability/") || slug.Contains("ability"))))
		{
			string[] abilityAssetProps = { "VisualEffect", "CastSound", "IconPath" };
			foreach (var prop in abilityAssetProps)
			{
				if (!obj.ContainsKey(prop))
				{
					obj[prop] = string.Empty;
				}
			}
		}

		if (obj.ContainsKey("ItemClass") || obj.ContainsKey("ItemLevel") || obj.ContainsKey("ResearchTime") || obj.ContainsKey("MaxLevel") ||
		    (slug != null && (slug.StartsWith("item/") || slug.StartsWith("upgrade/") || slug.Contains("item") || slug.Contains("upgrade"))))
		{
			if (!obj.ContainsKey("IconPath") && !obj.ContainsKey("icon_path"))
			{
				obj["IconPath"] = string.Empty;
			}
		}

		if (obj.ContainsKey("DecalId") || obj.ContainsKey("decal_id") || obj.ContainsKey("UpperFade") || obj.ContainsKey("upper_fade") ||
		    (slug != null && (slug.StartsWith("decal/") || slug.Contains("decal"))))
		{
			if (!obj.ContainsKey("TexturePath") && !obj.ContainsKey("texture_path"))
			{
				obj["TexturePath"] = string.Empty;
			}
		}
	}

	private bool IsKnownAssetPropertyOrExtension(string propertyName, string? value)
	{
		if (!string.IsNullOrWhiteSpace(value) && IsAssetExtension(value))
		{
			return true;
		}

		string p = propertyName.ToLowerInvariant();
		return p is "modelpath" or "portraitmodelpath" or "projectilemodelpath" or "iconpath" or "texturepath" or "basetexture" or "noisetexture" or "ribbontexture" or "visualeffect" or "attacksound" or "castsound" or "impactsound" or "impactvisualeffect" or "texture_path";
	}

	private AssetType[] GetAssetTypesForProperty(AssetPropertyTarget propTarget, string? templateSlug = null)
	{
		string p = propTarget.PropertyName.ToLowerInvariant();
		string curr = (propTarget.CurrentValue ?? string.Empty).ToLowerInvariant();

		if (p.Contains("ribbon"))
		{
			return new[] { AssetType.Ribbon };
		}

		if (p.Contains("noise"))
		{
			return new[] { AssetType.Noise };
		}

		if (p.Contains("icon"))
		{
			return new[] { AssetType.Icon };
		}

		if (curr.EndsWith(".raud") || p.Contains("sound") || p.Contains("audio") || p.Contains("music"))
		{
			return new[] { AssetType.SoundEffect };
		}

		if (p.Contains("effect") || p.Contains("vfx"))
		{
			return new[] { AssetType.Spritesheet, AssetType.VfxRadial, AssetType.VfxVertical };
		}

		if (curr.EndsWith(".rmesh") || p.Contains("model"))
		{
			return new[] { AssetType.Character, AssetType.Building, AssetType.Prop, AssetType.Item };
		}

		if (curr.EndsWith(".ranim") || p.Contains("anim"))
		{
			return new[] { AssetType.Animation };
		}

		if (curr.EndsWith(".gdshader") || p.Contains("shader"))
		{
			return new[] { AssetType.Shader };
		}

		if (p.Contains("decal"))
		{
			return new[] { AssetType.Decal };
		}

		if (!string.IsNullOrEmpty(templateSlug))
		{
			string slugLower = templateSlug.ToLowerInvariant();
			int slashIdx = slugLower.IndexOf('/');
			string prefix = slashIdx > 0 ? slugLower.Substring(0, slashIdx) : slugLower;

			if (AssetTypeExtensions.TryParse(prefix, out var slugType))
			{
				return new[] { slugType };
			}
		}

		if (propTarget.Parent != null)
		{
			string? rawAssetType = propTarget.Parent["asset_type"]?.ToString();
			if (AssetTypeExtensions.TryParse(rawAssetType, out var parentAssetType))
			{
				return new[] { parentAssetType };
			}

			string? slug = GetTemplateSlugFromJsonObject(propTarget.Parent)?.ToLowerInvariant();
			if (!string.IsNullOrEmpty(slug))
			{
				int slashIdx = slug.IndexOf('/');
				string prefix = slashIdx > 0 ? slug.Substring(0, slashIdx) : slug;

				if (prefix is "vfx")
				{
					return new[] { AssetType.Spritesheet, AssetType.VfxRadial, AssetType.VfxVertical };
				}

				if (AssetTypeExtensions.TryParse(prefix, out var slugType))
				{
					return new[] { slugType };
				}
			}

			if (propTarget.Parent.ContainsKey("DecalId") || propTarget.Parent.ContainsKey("decal_id") ||
			    propTarget.Parent.ContainsKey("UpperFade") || propTarget.Parent.ContainsKey("upper_fade") ||
			    propTarget.Parent.ContainsKey("LowerFade") || propTarget.Parent.ContainsKey("lower_fade") ||
			    propTarget.Parent.ContainsKey("AlbedoMix") || propTarget.Parent.ContainsKey("albedo_mix"))
			{
				return new[] { AssetType.Decal };
			}

			if (propTarget.Parent.ContainsKey("SwatchIndex") || propTarget.Parent.ContainsKey("TileMode") || propTarget.Parent.ContainsKey("UvScale"))
			{
				return new[] { AssetType.Terrain };
			}

			if (propTarget.Parent.ContainsKey("Columns") || propTarget.Parent.ContainsKey("Rows") || propTarget.Parent.ContainsKey("Fps"))
			{
				return new[] { AssetType.Spritesheet };
			}
		}

		return new[] { AssetType.Decal, AssetType.Terrain, AssetType.Icon, AssetType.Noise, AssetType.Ribbon, AssetType.Skybox, AssetType.Spritesheet };
	}

	private class AssetPropertyTarget
	{
		public JsonObject Parent { get; set; } = null!;
		public string PropertyName { get; set; } = string.Empty;
		public string CurrentValue { get; set; } = string.Empty;
	}
}
