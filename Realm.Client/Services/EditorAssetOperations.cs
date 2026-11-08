using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
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

/// <summary>
///     Finding and importing the assets a template points at: the index query, the search query derived from a template
///     id, template JSON traversal, asset-type mapping and the property-to-type rules. None of it reads editor state, so
///     it can run without an editor session; EditorService forwards the public methods because they are the IEditorAPI
///     surface. The random-assignment orchestration stays on EditorService because it owns the metadata-sync timestamp.
/// </summary>
internal static partial class EditorAssetOperations
{
	[GeneratedRegex(@"[^a-zA-Z]+")]
	internal static partial Regex NonAlphaRegex();

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
			var norm = RealmMetadataHelper.NormalizeAssetType(t);
			if (norm.HasValue)
			{
				typeStrings.Add(norm.Value.ToCanonicalString());
			}
		}

		return typeStrings;
	}

	internal static List<Asset> SearchAssets(AssetType[] assetTypes, string query, int limit = 5)
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

					if (allowedTypes != null && allowedTypes.Count > 0)
					{
						var bsonTypes = allowedTypes.Select(t => new BsonValue((int)t)).Concat(allowedTypeStrings?.Select(t => new BsonValue(t)) ?? Array.Empty<BsonValue>()).ToArray();
						candidates = col.Find(Query.In("AssetType", bsonTypes));
					}
					else
					{
						candidates = col.FindAll();
					}

					foreach (var candidate in candidates)
					{
						if (candidate == null) continue;

						var mappedType = AssetTypeMapping.MapStringToAssetType(candidate.AssetType, candidate.Extension, candidate.FilePath ?? candidate.DirectoryPath);
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
			BsonValue[]? bsonTypes = allowedTypes != null && allowedTypes.Count > 0
				? allowedTypes.Select(t => new BsonValue((int)t)).Concat(allowedTypeStrings?.Select(t => new BsonValue(t)) ?? Array.Empty<BsonValue>()).ToArray()
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
			var mappedType = AssetTypeMapping.MapStringToAssetType(candidate.AssetType, candidate.Extension, candidate.FilePath ?? candidate.DirectoryPath);
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

	internal static string ExtractSearchQuery(string templateSlug)
	{
		if (string.IsNullOrWhiteSpace(templateSlug))
		{
			return string.Empty;
		}

		string wsPath = MapWorkspaceService.GetActiveWorkspacePath();
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

		JsonObject? foundTemplateObj = TemplateJsonSearch.FindTemplateOrEntryObject(rootObj, templateSlug);
		if (foundTemplateObj == null)
		{
			return string.Empty;
		}

		var op1Terms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		var assetReferences = new List<string>();
		TemplateJsonSearch.TraverseJsonForTermsAndAssetReferences(foundTemplateObj, op1Terms, assetReferences);

		var op2Terms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (string assetRef in assetReferences)
		{
			TemplateJsonSearch.ExtractTermsFromAssetReference(wsPath, assetRef, op2Terms);
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

	internal static void ImportAssets(List<Asset> assets)
	{
		if (assets == null || assets.Count == 0)
		{
			return;
		}

		string wsPath = MapWorkspaceService.GetActiveWorkspacePath();
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

			string category = AssetTypeMapping.MapAssetTypeToCategoryString(asset.AssetType);
			string targetSubFolder = AssetTypeMapping.MapCategoryToDestinationSubFolder(category);
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

	internal static string? SnapshotTemplateJson(string wsPath, string templateSlug)
	{
		if (string.IsNullOrWhiteSpace(templateSlug) || string.IsNullOrEmpty(wsPath))
		{
			return null;
		}

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

		JsonObject? found = TemplateJsonSearch.FindTemplateOrEntryObject(rootObj, templateSlug);
		return found?.ToJsonString();
	}

	internal static void RevertTemplateJson(string wsPath, string templateSlug, string? snapshotJson)
	{
		if (string.IsNullOrWhiteSpace(templateSlug) || string.IsNullOrEmpty(wsPath))
		{
			return;
		}

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
						if (arr[i] is JsonObject obj && TemplateJsonSearch.IsTemplateMatch(obj, templateSlug))
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
			var options = new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
			File.WriteAllText(metaPath, rootObj.ToJsonString(options));
		}
	}

	internal static int CountMatchingTerms(IndexedAsset asset, string[] terms)
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

	internal static Asset ToEditorAsset(IndexedAsset item)
	{
		return new Asset
		{
			FilePath = item.FilePath,
			FileName = item.FileName,
			Extension = item.Extension,
			Blake3 = item.Blake3 ?? string.Empty,
			AssetType = AssetTypeMapping.MapStringToAssetType(item.AssetType, item.Extension, item.FilePath ?? item.DirectoryPath),
			Tags = item.Tags != null ? new List<string>(item.Tags) : new List<string>()
		};
	}











	internal static List<IndexedAsset> FallbackScanAssets(HashSet<AssetType>? allowedTypes)
	{
		var results = new List<IndexedAsset>();
		var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		string wsPath = MapWorkspaceService.GetActiveWorkspacePath();
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
					var mappedType = AssetTypeMapping.MapStringToAssetType((string?)null, ext, relativePath);
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
						AssetType = mappedType,
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

	internal static void CollectAssetProperties(JsonObject obj, List<AssetPropertyTarget> properties, bool overwriteExisting, string? templateSlug = null)
	{
		EnsureKnownTemplateAssetProperties(obj, templateSlug);

		foreach (var kvp in obj)
		{
			string? strVal = null;

			if (kvp.Value is JsonObject childObj)
			{
				CollectAssetProperties(childObj, properties, overwriteExisting, templateSlug);
				continue;
			}

			(kvp.Value as JsonValue)?.TryGetValue<string>(out strVal);
			if (!overwriteExisting && !string.IsNullOrWhiteSpace(strVal))
			{
				continue;
			}

			if (TemplateJsonSearch.IsAssetExtension(strVal) || IsKnownAssetProperty(kvp.Key))
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

	internal static void EnsureKnownTemplateAssetProperties(JsonObject obj, string? templateSlug = null)
	{
		string? slug = (templateSlug ?? TemplateJsonSearch.GetTemplateSlugFromJsonObject(obj))?.ToLowerInvariant();

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

	internal static bool IsKnownAssetProperty(string propertyName)
	{
		string p = propertyName.ToLowerInvariant();
		return p is "modelpath" or "portraitmodelpath" or "projectilemodelpath" or "iconpath" or "texturepath" or "basetexture" or "noisetexture" or "ribbontexture" or "visualeffect" or "attacksound" or "castsound" or "impactsound" or "impactvisualeffect" or "texture_path";
	}

	internal static AssetType[] GetAssetTypesForProperty(AssetPropertyTarget propTarget, string? templateSlug = null)
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

			string? slug = TemplateJsonSearch.GetTemplateSlugFromJsonObject(propTarget.Parent)?.ToLowerInvariant();
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
}

/// <summary>
///     One JSON property that points at an asset file, with the parent object and the value currently written there, so a
///     caller can swap in a different file name without walking the tree again.
/// </summary>
public class AssetPropertyTarget
{
	public JsonObject Parent { get; set; } = null!;
	public string PropertyName { get; set; } = string.Empty;
	public string CurrentValue { get; set; } = string.Empty;
}
