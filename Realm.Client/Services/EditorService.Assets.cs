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

	public List<Asset> SearchAssets(AssetType[] assetTypes, string query, int limit = 5)
	{
		string cacheFilePath = AssetIndexService.GetCacheFilePath();
		if (!File.Exists(cacheFilePath))
		{
			return new List<Asset>();
		}

		var connectionString = new ConnectionString
		{
			Filename = cacheFilePath,
			Connection = ConnectionType.Shared
		};

		var allowedTypes = assetTypes != null && assetTypes.Length > 0
			? new HashSet<AssetType>(assetTypes)
			: null;

		if (string.IsNullOrWhiteSpace(query))
		{
			var allAssets = new List<IndexedAsset>();

			using (var db = new LiteDatabase(connectionString))
			{
				var col = db.GetCollection<IndexedAsset>("assets");
				foreach (var candidate in col.FindAll())
				{
					if (candidate == null) continue;

					var mappedType = MapStringToAssetType(candidate.AssetType, candidate.Extension);
					if (allowedTypes != null && !allowedTypes.Contains(mappedType))
					{
						continue;
					}

					allAssets.Add(candidate);
				}
			}

			var rng = new Random();
			rng.Shuffle(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(allAssets));

			return allAssets
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

			foreach (string term in terms)
			{
				var subCandidates = col.Find(Query.Or(
					Query.Contains("Tags", term),
					Query.Contains("FileName", term)
				));

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
			var mappedType = MapStringToAssetType(candidate.AssetType, candidate.Extension);
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
		CollectAssetProperties(foundTemplateObj, candidateProperties, overwriteExisting);

		if (candidateProperties.Count == 0)
		{
			return;
		}

		var chosenAssets = new List<Asset>();
		var rng = new Random();

		foreach (var propTarget in candidateProperties)
		{
			var types = GetAssetTypesForProperty(propTarget);
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
			AssetType = MapStringToAssetType(item.AssetType, item.Extension),
			Tags = item.Tags != null ? new List<string>(item.Tags) : new List<string>()
		};
	}

	private AssetType MapStringToAssetType(string? rawType, string extension)
	{
		string normalized = RealmMetadataHelper.NormalizeAssetType(rawType);
		switch (normalized)
		{
			case "character": return AssetType.Character;
			case "building": return AssetType.Building;
			case "prop": return AssetType.Prop;
			case "item": return AssetType.Item;
			case "decal": return AssetType.Decal;
			case "icon": return AssetType.Icon;
			case "noise": return AssetType.Noise;
			case "ribbon": return AssetType.Ribbon;
			case "skybox": return AssetType.Skybox;
			case "spritesheet": return AssetType.Spritesheet;
			case "terrain": return AssetType.Terrain;
			case "vfxradial": return AssetType.VfxRadial;
			case "vfxvertical": return AssetType.VfxVertical;
			case "animation": return AssetType.Animation;
			case "music": return AssetType.Music;
			case "soundeffect": return AssetType.SoundEffect;
			case "shader": return AssetType.Shader;
		}

		string ext = (extension ?? string.Empty).ToLowerInvariant();
		return ext switch
		{
			".rmesh" => AssetType.Character,
			".ranim" => AssetType.Animation,
			".raud" => AssetType.SoundEffect,
			".gdshader" => AssetType.Shader,
			_ => AssetType.Decal
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

	private void CollectAssetProperties(JsonObject obj, List<AssetPropertyTarget> properties, bool overwriteExisting)
	{
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
				CollectAssetProperties(childObj, properties, overwriteExisting);
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

	private AssetType[] GetAssetTypesForProperty(AssetPropertyTarget propTarget)
	{
		string p = propTarget.PropertyName.ToLowerInvariant();
		string curr = (propTarget.CurrentValue ?? string.Empty).ToLowerInvariant();

		if (curr.EndsWith(".rmesh") || p.Contains("model"))
		{
			return new[] { AssetType.Character, AssetType.Building, AssetType.Prop, AssetType.Item };
		}

		if (curr.EndsWith(".ranim") || p.Contains("anim"))
		{
			return new[] { AssetType.Animation };
		}

		if (curr.EndsWith(".raud") || p.Contains("sound") || p.Contains("audio") || p.Contains("music"))
		{
			return new[] { AssetType.SoundEffect };
		}

		if (curr.EndsWith(".gdshader") || p.Contains("shader"))
		{
			return new[] { AssetType.Shader };
		}

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

		if (p.Contains("effect") || p.Contains("vfx"))
		{
			return new[] { AssetType.Spritesheet, AssetType.VfxRadial, AssetType.VfxVertical };
		}

		if (p.Contains("decal"))
		{
			return new[] { AssetType.Decal };
		}

		if (propTarget.Parent != null)
		{
			string? rawAssetType = propTarget.Parent["asset_type"]?.ToString() ?? propTarget.Parent["AssetType"]?.ToString();
			if (!string.IsNullOrWhiteSpace(rawAssetType))
			{
				string normalizedType = RealmMetadataHelper.NormalizeAssetType(rawAssetType);
				switch (normalizedType)
				{
					case "decal": return new[] { AssetType.Decal };
					case "terrain": return new[] { AssetType.Terrain };
					case "icon": return new[] { AssetType.Icon };
					case "noise": return new[] { AssetType.Noise };
					case "ribbon": return new[] { AssetType.Ribbon };
					case "skybox": return new[] { AssetType.Skybox };
					case "spritesheet": return new[] { AssetType.Spritesheet };
					case "vfxradial": return new[] { AssetType.VfxRadial };
					case "vfxvertical": return new[] { AssetType.VfxVertical };
				}
			}

			string? slug = GetTemplateSlugFromJsonObject(propTarget.Parent)?.ToLowerInvariant();
			if (!string.IsNullOrEmpty(slug))
			{
				if (slug.StartsWith("decal/") || slug.Contains("decal"))
				{
					return new[] { AssetType.Decal };
				}
				if (slug.StartsWith("terrain/") || slug.StartsWith("texture/") || slug.Contains("terrain"))
				{
					return new[] { AssetType.Terrain };
				}
				if (slug.StartsWith("icon/") || slug.Contains("icon"))
				{
					return new[] { AssetType.Icon };
				}
				if (slug.StartsWith("noise/") || slug.Contains("noise"))
				{
					return new[] { AssetType.Noise };
				}
				if (slug.StartsWith("ribbon/") || slug.Contains("ribbon"))
				{
					return new[] { AssetType.Ribbon };
				}
				if (slug.StartsWith("skybox/") || slug.Contains("skybox"))
				{
					return new[] { AssetType.Skybox };
				}
				if (slug.StartsWith("vfx/") || slug.Contains("spritesheet"))
				{
					return new[] { AssetType.Spritesheet, AssetType.VfxRadial, AssetType.VfxVertical };
				}
			}

			if (propTarget.Parent.ContainsKey("DecalId") || propTarget.Parent.ContainsKey("decal_id"))
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
