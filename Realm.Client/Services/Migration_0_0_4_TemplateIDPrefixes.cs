using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace Realm.Client.Services;

public class Migration_0_0_4_TemplateIDPrefixes : IMapMigration
{
	public string FromVersion => "v0.0.3";
	public string ToVersion => "v0.0.4";
	public string Description => "Migrate custom template keys to Templates container and normalize entity IDs to TemplateID";

	public MigrationResult Up(string mapDirectory, IProgress<MigrationProgressUpdate>? progress = null)
	{
		try
		{
			string metadataPath = Path.Combine(mapDirectory, "metadata.json");
			JsonObject metadataRoot = LoadMetadata(metadataPath);

			MigrateAssetsDirectories(mapDirectory);

			const int totalSteps = 4;
			progress?.Report(new MigrationProgressUpdate(Description, 1, totalSteps, "Normalizing entity template definitions into Templates container..."));
			
			var templatesObj = GetOrCreateObject(metadataRoot, "Templates");
			MigrateEntityCategories(metadataRoot, templatesObj);
			MigrateDecals(metadataRoot);
			MigrateSpawnShaders(metadataRoot);
			MigrateTerrainProfiles(metadataRoot);
			MigrateTextures(metadataRoot);
			MigrateSimpleCategories(metadataRoot, "icons", "icon");
			MigrateSimpleCategories(metadataRoot, "skyboxes", "skybox");
			MigrateSimpleCategories(metadataRoot, "ribbons", "ribbon");
			MigrateSpritesheets(metadataRoot);
			MigrateModels(metadataRoot, templatesObj);

			progress?.Report(new MigrationProgressUpdate(Description, 2, totalSteps, "Migrating manifest.json asset keys to canonical categories..."));
			MigrateManifestJson(mapDirectory, metadataRoot, templatesObj);

			progress?.Report(new MigrationProgressUpdate(Description, 3, totalSteps, "Normalizing terrain.json placed entities to TemplateId format..."));
			MigrateTerrainJson(mapDirectory, metadataRoot, templatesObj);

			progress?.Report(new MigrationProgressUpdate(Description, 4, totalSteps, "Updating map build number and saving metadata.json..."));
			metadataRoot["GameBuildNumber"] = ToVersion;
			MapJsonFormatter.SaveFormattedJson(metadataPath, metadataRoot);

			return new MigrationResult { Success = true, FromVersion = FromVersion, ToVersion = ToVersion };
		}
		catch (Exception ex)
		{
			return new MigrationResult { Success = false, ErrorMessage = $"Migration 0.0.4 failed: {ex.Message}", FromVersion = FromVersion, ToVersion = ToVersion };
		}
	}

	private static JsonObject LoadMetadata(string metadataPath)
	{
		if (!File.Exists(metadataPath)) return new JsonObject();
		return JsonNode.Parse(File.ReadAllText(metadataPath))?.AsObject() ?? new JsonObject();
	}

	private static JsonObject GetOrCreateObject(JsonObject parent, string key)
	{
		if (!parent.ContainsKey(key) || parent[key] is not JsonObject)
		{
			parent[key] = new JsonObject();
		}
		return parent[key]!.AsObject();
	}

	private static void MigrateAssetsDirectories(string mapDirectory)
	{
		string assetsDir = Path.Combine(mapDirectory, "Assets");
		if (!Directory.Exists(assetsDir)) return;

		MoveLegacyVfxDir(assetsDir);
		MovePrefixSubfolders(assetsDir);
	}

	private static void MoveLegacyVfxDir(string assetsDir)
	{
		string legacyVfxDir = Path.Combine(assetsDir, "vfx");
		string newVfxDir = Path.Combine(assetsDir, "vfx_spritesheets");
		if (Directory.Exists(legacyVfxDir))
		{
			Directory.CreateDirectory(newVfxDir);
			MoveDirectoryContents(legacyVfxDir, newVfxDir);
			try { Directory.Delete(legacyVfxDir, true); } catch { }
		}
	}

	private static void MovePrefixSubfolders(string assetsDir)
	{
		var prefixSubfolderChecks = new (string Folder, string Prefix)[]
		{
			("icons", "icon"), ("skyboxes", "skybox"), ("textures", "terrain"),
			("decals", "decal"), ("ribbons", "ribbon"),
			("vfx_spritesheets", "spritesheet"), ("vfx_spritesheets", "vfx")
		};

		foreach (var (folder, prefix) in prefixSubfolderChecks)
		{
			string nestedDir = Path.Combine(assetsDir, folder, prefix);
			if (Directory.Exists(nestedDir))
			{
				string parentDir = Path.Combine(assetsDir, folder);
				MoveDirectoryContents(nestedDir, parentDir);
				try { Directory.Delete(nestedDir, true); } catch { }
			}
		}
	}

	private static void MoveDirectoryContents(string sourceDir, string targetDir)
	{
		foreach (var file in Directory.GetFiles(sourceDir, "*.*", SearchOption.AllDirectories))
		{
			string rel = Path.GetRelativePath(sourceDir, file);
			string target = Path.Combine(targetDir, rel);
			string? targetFileDir = Path.GetDirectoryName(target);
			if (!string.IsNullOrEmpty(targetFileDir)) Directory.CreateDirectory(targetFileDir);
			
			if (!File.Exists(target)) File.Move(file, target);
			else File.Delete(file);
		}
	}

	private static void MigrateEntityCategories(JsonObject metadataRoot, JsonObject templatesObj)
	{
		var entityCategories = new (string LegacyCustomKey, string CanonicalKey, string ObjectType, string LegacyIdKey)[]
		{
			("CustomUnits", "Units", "unit", "UnitId"), ("CustomBuildings", "Buildings", "building", "UnitId"),
			("CustomResources", "Resources", "resource", "UnitId"), ("CustomProps", "Props", "prop", "UnitId"),
			("CustomAbilities", "Abilities", "ability", "AbilityId"), ("CustomWeapons", "Weapons", "weapon", "WeaponId"),
			("CustomUpgrades", "Upgrades", "upgrade", "UpgradeId"), ("CustomItems", "Items", "item", "ItemId"),
			("CustomAttachments", "Attachments", "", ""), ("CustomVfx", "Vfx", "", "")
		};

		foreach (var (legacyKey, canonicalKey, objectType, legacyIdKey) in entityCategories)
		{
			MigrateEntityCategoryList(metadataRoot, templatesObj, legacyKey, canonicalKey);
			NormalizeEntityIdsAndModels(templatesObj, canonicalKey, objectType, legacyIdKey);
		}
	}

	private static void MigrateEntityCategoryList(JsonObject metadataRoot, JsonObject templatesObj, string legacyKey, string canonicalKey)
	{
		if (metadataRoot.TryGetPropertyValue(legacyKey, out var legacyArrNode) && legacyArrNode is JsonArray legacyArray)
		{
			if (!templatesObj.ContainsKey(canonicalKey) || templatesObj[canonicalKey] is not JsonArray)
			{
				templatesObj[canonicalKey] = new JsonArray();
			}
			var canonicalArray = templatesObj[canonicalKey]!.AsArray();
			foreach (var item in legacyArray)
			{
				if (item != null) canonicalArray.Add(item.DeepClone());
			}
			metadataRoot.Remove(legacyKey);
		}
	}

	private static void NormalizeEntityIdsAndModels(JsonObject templatesObj, string canonicalKey, string objectType, string legacyIdKey)
	{
		if (string.IsNullOrEmpty(objectType)) return;
		if (!templatesObj.TryGetPropertyValue(canonicalKey, out var arrNode) || arrNode is not JsonArray entityArray) return;

		foreach (var item in entityArray.OfType<JsonObject>())
		{
			string rawId = ExtractRawId(item, legacyIdKey);
			if (!string.IsNullOrWhiteSpace(rawId))
			{
				item["TemplateID"] = TemplateIDHelper.NormalizeTemplateID(objectType, rawId);
			}

			string modelPath = item["ModelPath"]?.ToString() ?? string.Empty;
			if (modelPath.EndsWith(".rmesh", StringComparison.OrdinalIgnoreCase))
			{
				item["VisualMode"] = "Mesh";
			}
		}
	}

	private static string ExtractRawId(JsonObject entityObj, string legacyIdKey)
	{
		string rawId = string.Empty;
		if (entityObj.TryGetPropertyValue("TemplateID", out var tid) && tid != null) rawId = tid.ToString();
		else if (entityObj.TryGetPropertyValue("ObjectID", out var oid) && oid != null) { rawId = oid.ToString(); entityObj.Remove("ObjectID"); }
		else if (!string.IsNullOrEmpty(legacyIdKey) && entityObj.TryGetPropertyValue(legacyIdKey, out var lid) && lid != null) { rawId = lid.ToString(); entityObj.Remove(legacyIdKey); }
		else if (entityObj.TryGetPropertyValue("UnitId", out var uid) && uid != null) { rawId = uid.ToString(); entityObj.Remove("UnitId"); }
		return rawId;
	}

	private static void MigrateDecals(JsonObject metadataRoot)
	{
		if (!metadataRoot.TryGetPropertyValue("decals", out var decalsNode) || decalsNode is not JsonObject decalsObj) return;

		var normalizedDecals = new JsonObject();
		foreach (var kvp in decalsObj)
		{
			string rawKey = kvp.Key;
			string slug = TemplateIDHelper.GenerateSlug(rawKey);
			string normalizedId = TemplateIDHelper.NormalizeTemplateID("decal", slug);
			var itemObj = kvp.Value as JsonObject ?? new JsonObject();
			
			CleanHashAndAssetType(itemObj);
			if (string.IsNullOrWhiteSpace(itemObj["TexturePath"]?.ToString()))
			{
				itemObj["TexturePath"] = rawKey.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase) ? rawKey : $"{slug}.rtex";
			}
			EnsureDecalDefaults(itemObj);
			MergeOrAddNormalizedItem(normalizedDecals, normalizedId, itemObj);
		}
		metadataRoot["decals"] = normalizedDecals;
	}

	private static void CleanHashAndAssetType(JsonObject itemObj)
	{
		itemObj.Remove("Hash"); itemObj.Remove("hash");
		itemObj.Remove("AssetType"); itemObj.Remove("assetType"); itemObj.Remove("asset_type");
	}

	private static void EnsureDecalDefaults(JsonObject itemObj)
	{
		if (!itemObj.ContainsKey("Opacity")) itemObj["Opacity"] = 1.0f;
		if (!itemObj.ContainsKey("Brightness")) itemObj["Brightness"] = 1.0f;
		if (!itemObj.ContainsKey("Contrast")) itemObj["Contrast"] = 1.0f;
		if (!itemObj.ContainsKey("Saturation")) itemObj["Saturation"] = 1.0f;
	}

	private static void MergeOrAddNormalizedItem(JsonObject normalizedCollection, string normalizedId, JsonObject itemObj)
	{
		if (!normalizedCollection.ContainsKey(normalizedId))
		{
			normalizedCollection[normalizedId] = itemObj.DeepClone();
		}
		else if (normalizedCollection[normalizedId] is JsonObject existingObj)
		{
			foreach (var prop in itemObj)
			{
				if (prop.Value != null && (!existingObj.ContainsKey(prop.Key) || existingObj[prop.Key] == null))
				{
					existingObj[prop.Key] = prop.Value.DeepClone();
				}
			}
		}
	}

	private static void MigrateSpawnShaders(JsonObject metadataRoot)
	{
		var spawnShadersObj = GetOrCreateObject(metadataRoot, "SpawnShader");
		MigrateLegacyShaders(metadataRoot, spawnShadersObj);

		var normalizedShaders = new JsonObject();
		foreach (var kvp in spawnShadersObj)
		{
			string normalizedId = TemplateIDHelper.NormalizeTemplateID("SpawnShader", TemplateIDHelper.GenerateSlug(kvp.Key));
			var itemObj = kvp.Value as JsonObject ?? new JsonObject();
			
			MigrateShaderConfigJson(itemObj);
			if (!normalizedShaders.ContainsKey(normalizedId))
			{
				normalizedShaders[normalizedId] = itemObj.DeepClone();
			}
		}

		EnsureDefaultSpawnShaders(normalizedShaders);
		foreach (var kvp in normalizedShaders)
		{
			if (kvp.Value is JsonObject sObj) { sObj.Remove("Hash"); sObj.Remove("hash"); }
		}
		metadataRoot["SpawnShader"] = normalizedShaders;
	}

	private static void MigrateLegacyShaders(JsonObject metadataRoot, JsonObject spawnShadersObj)
	{
		if (metadataRoot.TryGetPropertyValue("shaders", out var legacyShadersNode) && legacyShadersNode is JsonObject legacyShadersObj)
		{
			foreach (var kvp in legacyShadersObj)
			{
				if (!spawnShadersObj.ContainsKey(kvp.Key) && kvp.Value != null) spawnShadersObj[kvp.Key] = kvp.Value.DeepClone();
			}
			metadataRoot.Remove("shaders");
		}
	}

	private static void MigrateShaderConfigJson(JsonObject itemObj)
	{
		if (!itemObj.TryGetPropertyValue("ConfigJson", out var configJsonNode) || configJsonNode == null) return;
		
		string configJsonStr = configJsonNode.ToString();
		if (!string.IsNullOrWhiteSpace(configJsonStr))
		{
			try
			{
				var parsedCfg = JsonNode.Parse(configJsonStr)?.AsObject();
				if (parsedCfg != null)
				{
					foreach (var cfgProp in parsedCfg)
					{
						string propName = MapShaderConfigKey(cfgProp.Key);
						itemObj[propName] = cfgProp.Value?.DeepClone();
					}
				}
			}
			catch { }
		}
		itemObj.Remove("ConfigJson");
	}

	private static readonly Dictionary<string, string> ShaderConfigKeyMap = new(StringComparer.OrdinalIgnoreCase)
	{
		{ "name", "Name" }, { "transition_mode", "TransitionMode" }, { "direction", "Direction" },
		{ "edge_color", "EdgeColor" }, { "edge_width", "EdgeWidth" }, { "edge_emission", "EdgeEmission" },
		{ "noise_scale", "NoiseScale" }, { "noise_roughness", "NoiseRoughness" }, { "fresnel_power", "FresnelPower" },
		{ "vertex_displacement", "VertexDisplacement" }, { "alpha_fade", "AlphaFade" }, { "duration", "Duration" },
		{ "asset_type", "AssetType" }
	};

	private static string MapShaderConfigKey(string key)
	{
		return ShaderConfigKeyMap.TryGetValue(key, out var newKey) ? newKey : key;
	}

	private static void EnsureDefaultSpawnShaders(JsonObject normalizedShaders)
	{
		if (normalizedShaders.Count == 0)
		{
			normalizedShaders["SpawnShader/magic_blueprint"] = CreateDefaultShaderConfig("Magic Blueprint", 0, 0, "#00e5ffff", 0.06f, 6.0f, 12.0f, 0.4f, 3.0f, 0.0f, 0.9f, 1.2f);
			normalizedShaders["SpawnShader/fire_demolish"] = CreateDefaultShaderConfig("Fire Ember Dissolve", 1, 1, "#ff590cff", 0.08f, 7.0f, 16.0f, 0.7f, 1.5f, 0.15f, 1.0f, 1.5f);
			normalizedShaders["SpawnShader/hologram_warp"] = CreateDefaultShaderConfig("Hologram Scanlines", 2, 0, "#66ff33ff", 0.04f, 4.0f, 20.0f, 0.2f, 4.0f, 0.02f, 0.75f, 1.0f);
			normalizedShaders["SpawnShader/earth_crumble"] = CreateDefaultShaderConfig("Earth Ground Crumble", 3, 1, "#99734cff", 0.05f, 2.0f, 8.0f, 0.8f, 1.0f, 0.25f, 1.0f, 1.1f);
			normalizedShaders["SpawnShader/frost_crystallize"] = CreateDefaultShaderConfig("Frost Crystallize", 4, 2, "#b2e5ffff", 0.05f, 5.0f, 25.0f, 0.6f, 3.5f, 0.03f, 0.95f, 1.3f);
			normalizedShaders["SpawnShader/shadow_void"] = CreateDefaultShaderConfig("Shadow Void Collapse", 5, 3, "#b219ffff", 0.07f, 8.0f, 14.0f, 0.9f, 2.0f, 0.18f, 1.0f, 1.4f);
		}
	}

	private static void MigrateTerrainProfiles(JsonObject metadataRoot)
	{
		if (!metadataRoot.TryGetPropertyValue("TerrainProfiles", out var legacyProfilesNode) || legacyProfilesNode is not JsonArray legacyProfilesArr) return;

		var texturesObj = GetOrCreateObject(metadataRoot, "textures");
		foreach (var profItem in legacyProfilesArr.OfType<JsonObject>())
		{
			string rawSwatch = profItem["SwatchName"]?.ToString() ?? string.Empty;
			if (string.IsNullOrWhiteSpace(rawSwatch)) continue;

			string slug = TemplateIDHelper.GenerateSlug(rawSwatch);
			string normalizedId = TemplateIDHelper.NormalizeTemplateID("terrain", slug);

			var targetTexObj = FindTerrainTextureTarget(texturesObj, normalizedId, rawSwatch, slug);
			if (targetTexObj == null)
			{
				targetTexObj = new JsonObject
				{
					["AssetType"] = rawSwatch.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase) ? rawSwatch : $"{slug}.rtex",
					["TileMode"] = "Stochastic", ["UvScale"] = 1.0f, ["Brightness"] = 1.0f
				};
				texturesObj[normalizedId] = targetTexObj;
			}

			CopyTerrainProfileProperties(profItem, targetTexObj);
		}
		metadataRoot.Remove("TerrainProfiles");
	}

	private static JsonObject? FindTerrainTextureTarget(JsonObject texturesObj, string normalizedId, string rawSwatch, string slug)
	{
		if (texturesObj.TryGetPropertyValue(normalizedId, out var existingNode) && existingNode is JsonObject existingObj) return existingObj;
		if (texturesObj.TryGetPropertyValue(rawSwatch, out var directNode) && directNode is JsonObject directObj) return directObj;
		if (texturesObj.TryGetPropertyValue(slug, out var slugNode) && slugNode is JsonObject slugObj) return slugObj;
		return null;
	}

	private static void CopyTerrainProfileProperties(JsonObject profItem, JsonObject targetTexObj)
	{
		if (profItem.TryGetPropertyValue("DefaultPathingCode", out var dpcVal) && dpcVal != null) targetTexObj["DefaultPathingCode"] = dpcVal.DeepClone();
		if (profItem.TryGetPropertyValue("DecalBombingRules", out var dbrVal) && dbrVal != null) targetTexObj["DecalBombingRules"] = dbrVal.DeepClone();
		if (profItem.TryGetPropertyValue("VfxBombingRules", out var vbrVal) && vbrVal != null) targetTexObj["VfxBombingRules"] = vbrVal.DeepClone();
	}

	private static void MigrateTextures(JsonObject metadataRoot)
	{
		if (!metadataRoot.TryGetPropertyValue("textures", out var texturesNodeFinal) || texturesNodeFinal is not JsonObject texturesObjFinal) return;

		var normalizedTextures = new JsonObject();
		foreach (var kvp in texturesObjFinal)
		{
			string slug = TemplateIDHelper.GenerateSlug(kvp.Key);
			string normalizedId = TemplateIDHelper.NormalizeTemplateID("terrain", slug);
			var itemObj = kvp.Value as JsonObject ?? new JsonObject();

			NormalizeTexturePath(itemObj, kvp.Key, slug);
			CleanHashAndAssetType(itemObj);
			itemObj.Remove("rtex"); itemObj.Remove("texturePath");

			RenameTextureProperties(itemObj);
			EnsureTextureDefaults(itemObj);
			MergeOrAddNormalizedItem(normalizedTextures, normalizedId, itemObj);
		}
		metadataRoot["textures"] = normalizedTextures;
	}

	private static void NormalizeTexturePath(JsonObject itemObj, string rawKey, string slug)
	{
		string rtexName = GetTexturePath(itemObj, rawKey, slug);
		itemObj["TexturePath"] = rtexName;
	}

	private static string GetTexturePath(JsonObject itemObj, string rawKey, string slug)
	{
		string[] keys = ["TexturePath", "texturePath", "AssetType", "assetType", "rtex"];
		foreach (var k in keys)
		{
			if (itemObj[k]?.ToString() is string p && !string.IsNullOrEmpty(p)) return p;
		}
		
		return rawKey.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase) ? rawKey : $"{slug}.rtex";
	}

	private static void RenameTextureProperties(JsonObject itemObj)
	{
		RenameProperty(itemObj, "ScaleFactor", "Scale_Factor", "scale_factor");
		RenameProperty(itemObj, "SwatchIndex", "swatchIndex", "swatch_index");
		RenameProperty(itemObj, "RoughnessScale", "Roughness_Scale", "roughness_scale", "roughnessScale");
		RenameProperty(itemObj, "NormalScale", "Normal_Scale", "normal_scale", "normalScale");
		RenameProperty(itemObj, "HeightScale", "Height_Scale", "height_scale", "heightScale");
		RenameProperty(itemObj, "HeightOffset", "Height_Offset", "height_offset", "heightOffset");
		RenameProperty(itemObj, "CrevicePower", "Crevice_Power", "crevice_power", "crevicePower");
		RenameProperty(itemObj, "TileMode", "Tile_Mode", "tile_mode", "tileMode");
		RenameProperty(itemObj, "UvScale", "UV_Scale", "uv_scale", "uvScale");
		RenameProperty(itemObj, "StochasticTileSize", "Stochastic_Tile_Size", "stochastic_tile_size", "stochasticTileSize");
		RenameProperty(itemObj, "CrossFade", "Cross_Fade", "cross_fade", "Grid_Cross_Fade", "grid_cross_fade", "crossFade");
	}

	private static void RenameProperty(JsonObject obj, string newName, params string[] oldNames)
	{
		foreach (var old in oldNames)
		{
			if (obj.TryGetPropertyValue(old, out var val))
			{
				obj[newName] = val?.DeepClone();
				foreach (var o in oldNames) obj.Remove(o);
				break;
			}
		}
	}

	private static void EnsureTextureDefaults(JsonObject itemObj)
	{
		if (!itemObj.ContainsKey("DefaultPathingCode") || itemObj["DefaultPathingCode"] == null) itemObj["DefaultPathingCode"] = 8 | 32 | 4;
		if (!itemObj.ContainsKey("DecalBombingRules") || itemObj["DecalBombingRules"] == null) itemObj["DecalBombingRules"] = new JsonArray();
		if (!itemObj.ContainsKey("VfxBombingRules") || itemObj["VfxBombingRules"] == null) itemObj["VfxBombingRules"] = new JsonArray();
	}

	private static void MigrateSimpleCategories(JsonObject metadataRoot, string containerKey, string objectType)
	{
		if (!metadataRoot.TryGetPropertyValue(containerKey, out var node) || node is not JsonObject obj) return;

		var normalized = new JsonObject();
		foreach (var kvp in obj)
		{
			string rawKey = kvp.Key;
			string slug = TemplateIDHelper.GenerateSlug(rawKey);
			string normalizedId = TemplateIDHelper.NormalizeTemplateID(objectType, slug);
			var itemObj = kvp.Value as JsonObject ?? new JsonObject();
			CleanHashAndAssetType(itemObj);
			itemObj["TexturePath"] = itemObj["TexturePath"]?.ToString() ?? (rawKey.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase) ? rawKey : $"{slug}.rtex");
			normalized[normalizedId] = itemObj;
		}
		metadataRoot[containerKey] = normalized;
	}

	private static void MigrateSpritesheets(JsonObject metadataRoot)
	{
		if (!metadataRoot.TryGetPropertyValue("vfx_spritesheets", out var vfxSpritesheetsNode) || vfxSpritesheetsNode is not JsonObject vfxSpritesheetsObj) return;

		var normalizedSpritesheets = new JsonObject();
		foreach (var kvp in vfxSpritesheetsObj)
		{
			string slug = TemplateIDHelper.GenerateSlug(kvp.Key);
			string normalizedId = TemplateIDHelper.NormalizeTemplateID("spritesheet", slug);
			var itemObj = kvp.Value as JsonObject ?? new JsonObject();
			
			itemObj["TexturePath"] = itemObj["TexturePath"]?.ToString() ?? itemObj["AssetType"]?.ToString() ?? (kvp.Key.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase) ? kvp.Key : $"{slug}.rtex");
			CleanHashAndAssetType(itemObj);
			
			RenameProperty(itemObj, "Columns", "columns");
			RenameProperty(itemObj, "Rows", "rows");
			RenameProperty(itemObj, "Fps", "fps");
			RenameProperty(itemObj, "SubframeBlend", "subframe_blend", "subframeBlend");

			MergeOrAddNormalizedItem(normalizedSpritesheets, normalizedId, itemObj);
		}
		metadataRoot["vfx_spritesheets"] = normalizedSpritesheets;
	}

	private static void MigrateModels(JsonObject metadataRoot, JsonObject templatesObj)
	{
		if (!metadataRoot.TryGetPropertyValue("Models", out var modelsNode) || modelsNode is not JsonObject modelsObj) return;

		var modelToTemplateId = BuildModelToTemplateIdMap(templatesObj);
		var normalizedModels = new JsonObject();

		foreach (var kvp in modelsObj)
		{
			string targetKey = DetermineModelTargetKey(kvp.Key, modelToTemplateId);
			var itemObj = kvp.Value as JsonObject ?? new JsonObject();
			CleanHashAndAssetType(itemObj);
			MergeOrAddNormalizedItem(normalizedModels, targetKey, itemObj);
		}
		metadataRoot["Models"] = normalizedModels;
	}

	private static Dictionary<string, string> BuildModelToTemplateIdMap(JsonObject templatesObj)
	{
		var modelToTemplateId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		var categoryNames = new[] { "Units", "Buildings", "Resources", "Props", "Items", "Weapons" };
		foreach (var cat in categoryNames)
		{
			if (templatesObj.TryGetPropertyValue(cat, out var catNode) && catNode is JsonArray arr)
			{
				ProcessTemplateArray(arr, modelToTemplateId);
			}
		}
		return modelToTemplateId;
	}

	private static void ProcessTemplateArray(JsonArray arr, Dictionary<string, string> modelToTemplateId)
	{
		foreach (var item in arr.OfType<JsonObject>())
		{
			string tId = item["TemplateID"]?.ToString() ?? "";
			if (string.IsNullOrEmpty(tId)) continue;
			
			modelToTemplateId[TemplateIDHelper.GenerateSlug(tId)] = tId;
			string mPath = item["ModelPath"]?.ToString() ?? "";
			if (!string.IsNullOrEmpty(mPath))
			{
				modelToTemplateId[Path.GetFileName(mPath)] = tId;
				modelToTemplateId[TemplateIDHelper.GenerateSlug(mPath)] = tId;
			}
		}
	}

	private static string DetermineModelTargetKey(string rawKey, Dictionary<string, string> modelToTemplateId)
	{
		if (modelToTemplateId.TryGetValue(rawKey, out var matchedTid)) return matchedTid;
		if (modelToTemplateId.TryGetValue(TemplateIDHelper.GenerateSlug(rawKey), out matchedTid)) return matchedTid;
		return rawKey;
	}

	private static void MigrateManifestJson(string mapDirectory, JsonObject metadataRoot, JsonObject templatesObj)
	{
		string manifestPath = Path.Combine(mapDirectory, "manifest.json");
		if (!File.Exists(manifestPath)) return;

		JsonObject? manifestRoot = JsonNode.Parse(File.ReadAllText(manifestPath))?.AsObject();
		if (manifestRoot == null || !manifestRoot.TryGetPropertyValue("Assets", out var assetsNode) || assetsNode is not JsonObject assetsObj) return;

		RenameManifestCategories(assetsObj);
		MigrateGlbCategory(assetsObj);
		NormalizeAssetKeysInManifest(assetsObj, metadataRoot, templatesObj);

		MapJsonFormatter.SaveFormattedJson(manifestPath, manifestRoot);
	}

	private static void RenameManifestCategories(JsonObject assetsObj)
	{
		var topLevelRenames = new (string OldKey, string NewKey)[]
		{
			("animations", "Animation"), ("decals", "Decal"), ("icons", "Icon"), ("music", "Music"),
			("noise_textures", "Noise"), ("ribbons", "Ribbon"), ("sfx", "SoundEffect"), ("shaders", "Shader"),
			("skyboxes", "Skybox"), ("textures", "Terrain"), ("vfx", "Spritesheet"), ("vfx_spritesheets", "Spritesheet"),
			("spritesheets", "Spritesheet")
		};

		foreach (var (oldKey, newKey) in topLevelRenames)
		{
			if (assetsObj.TryGetPropertyValue(oldKey, out var sourceNode) && sourceNode is JsonObject sourceObj)
			{
				UnionCategoryInto(assetsObj, newKey, sourceObj);
				assetsObj.Remove(oldKey);
			}
		}
	}

	private static void MigrateGlbCategory(JsonObject assetsObj)
	{
		if (!assetsObj.TryGetPropertyValue("glb", out var glbNode) || glbNode is not JsonObject glbObj) return;

		var glbRenames = new (string OldKey, string NewKey)[]
		{
			("units", "Character"), ("resources", "Prop"), ("props", "Prop"), ("projectiles", "Item"), ("buildings", "Building")
		};

		foreach (var (oldKey, newKey) in glbRenames)
		{
			if (glbObj.TryGetPropertyValue(oldKey, out var sourceNode) && sourceNode is JsonObject sourceObj)
			{
				UnionCategoryInto(assetsObj, newKey, sourceObj);
				glbObj.Remove(oldKey);
			}
		}
		if (glbObj.Count == 0) assetsObj.Remove("glb");
	}

	private static void NormalizeAssetKeysInManifest(JsonObject assetsObj, JsonObject metadataRoot, JsonObject templatesObj)
	{
		foreach (var categoryPair in assetsObj)
		{
			if (categoryPair.Value is JsonObject categoryObj)
			{
				NormalizeCategoryAssets(categoryPair.Key, categoryObj, metadataRoot, templatesObj);
			}
		}
	}

	private static void NormalizeCategoryAssets(string categoryName, JsonObject categoryObj, JsonObject metadataRoot, JsonObject templatesObj)
	{
		RenameMismatchedKeys(categoryName, categoryObj);

		foreach (var assetPair in categoryObj)
		{
			string hash = GetAssetHash(assetPair.Value);
			EnsureTemplateForManifestAsset(metadataRoot, templatesObj, categoryName, assetPair.Key, hash);
		}
	}

	private static void RenameMismatchedKeys(string categoryName, JsonObject categoryObj)
	{
		var renamedKeys = new List<(string OldKey, string NewKey, JsonNode Value)>();
		foreach (var assetPair in categoryObj)
		{
			string cleanKey = DetermineCleanAssetKey(assetPair.Key, categoryName);
			if (string.Equals(assetPair.Key, cleanKey, StringComparison.OrdinalIgnoreCase) || assetPair.Value == null) continue;
			
			renamedKeys.Add((assetPair.Key, cleanKey, assetPair.Value.DeepClone()));
		}
		
		foreach (var (oldK, newK, v) in renamedKeys)
		{
			categoryObj.Remove(oldK);
			if (!categoryObj.ContainsKey(newK)) categoryObj[newK] = v;
		}
	}

	private static string GetAssetHash(JsonNode? assetValue)
	{
		if (assetValue is not JsonObject obj) return assetValue?.ToString() ?? string.Empty;
		return obj["hash"]?.ToString() ?? obj["blake3"]?.ToString() ?? string.Empty;
	}

	private static string DetermineCleanAssetKey(string rawKey, string categoryName)
	{
		var (pType, pSlug) = TemplateIDHelper.ParseTemplateID(rawKey);
		string cleanKey = (!string.IsNullOrEmpty(pType) && !string.IsNullOrEmpty(pSlug)) ? pSlug : rawKey;
		if (!Path.HasExtension(cleanKey))
		{
			string defaultExt = categoryName switch
			{
				"Animation" => ".ranim", "SoundEffect" or "Music" => ".raud",
				"Character" or "Building" or "Prop" or "Item" => ".rmesh", _ => ".rtex"
			};
			cleanKey += defaultExt;
		}
		return cleanKey;
	}

	private static void MigrateTerrainJson(string mapDirectory, JsonObject metadataRoot, JsonObject templatesObj)
	{
		string terrainPath = Path.Combine(mapDirectory, "terrain.json");
		if (!File.Exists(terrainPath)) return;

		try
		{
			var terrainRoot = JsonNode.Parse(File.ReadAllText(terrainPath))?.AsObject();
			if (terrainRoot == null) return;

			var unitOrBuildingLookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			var propOrResourceLookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			var decalLookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			var allTemplatesLookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

			PopulateCategoryLookup(templatesObj, unitOrBuildingLookup, allTemplatesLookup, "Units");
			PopulateCategoryLookup(templatesObj, unitOrBuildingLookup, allTemplatesLookup, "Buildings");
			PopulateCategoryLookup(templatesObj, propOrResourceLookup, allTemplatesLookup, "Props");
			PopulateCategoryLookup(templatesObj, propOrResourceLookup, allTemplatesLookup, "Resources");
			PopulateDecalLookup(metadataRoot, decalLookup);

			MigrateTerrainCategory(terrainRoot, "Units", "unit", unitOrBuildingLookup, propOrResourceLookup, allTemplatesLookup, "TemplateID", "UnitId", "ObjectID", "Id");
			MigrateTerrainCategory(terrainRoot, "Props", "prop", propOrResourceLookup, unitOrBuildingLookup, allTemplatesLookup, "PropId", "UnitId", "TemplateID", "ObjectID", "Id");
			MigrateTerrainCategory(terrainRoot, "Decals", "decal", decalLookup, null, null, "DecalId", "TemplateID", "Id");

			MapJsonFormatter.SaveFormattedJson(terrainPath, terrainRoot);
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[Migration_0_0_4] Failed to migrate terrain.json: {ex.Message}");
		}
	}

	private static void PopulateCategoryLookup(JsonObject templatesObj, Dictionary<string, string> targetLookup, Dictionary<string, string> allTemplatesLookup, string categoryName)
	{
		if (!templatesObj.TryGetPropertyValue(categoryName, out var catNode) || catNode is not JsonArray catArr)
		{
			return;
		}

		foreach (var item in catArr.OfType<JsonObject>())
		{
			ProcessCategoryItem(item, targetLookup, allTemplatesLookup);
		}
	}

	private static void ProcessCategoryItem(JsonObject item, Dictionary<string, string> targetLookup, Dictionary<string, string> allTemplatesLookup)
	{
		string tId = GetStringValue(item, "TemplateID") ?? string.Empty;
		string? objectId = GetStringValue(item, "UnitId") ?? GetStringValue(item, "ObjectID");
		string? name = GetStringValue(item, "Name");
		string? modelPath = GetStringValue(item, "ModelPath");
		RegisterInLookup(targetLookup, allTemplatesLookup, tId, name, modelPath, objectId);
	}

	private static string? GetStringValue(JsonObject obj, string key)
	{
		return obj.TryGetPropertyValue(key, out var node) ? node?.ToString() : null;
	}

	private static void PopulateDecalLookup(JsonObject metadataRoot, Dictionary<string, string> decalLookup)
	{
		if (metadataRoot.TryGetPropertyValue("decals", out var decalsNodeFinal) && decalsNodeFinal is JsonObject decalsObjFinal)
		{
			foreach (var kvp in decalsObjFinal)
			{
				decalLookup[kvp.Key] = kvp.Key;
				var (_, dSlug) = TemplateIDHelper.ParseTemplateID(kvp.Key);
				if (!string.IsNullOrEmpty(dSlug)) decalLookup[dSlug] = kvp.Key;
			}
		}
	}

	private static void RegisterInLookup(Dictionary<string, string> lookup, Dictionary<string, string> allLookup, string templateId, string? name, string? modelPath, string? rawLegacyId)
	{
		if (string.IsNullOrWhiteSpace(templateId)) return;
		lookup[templateId] = templateId; allLookup[templateId] = templateId;

		var (_, pSlug) = TemplateIDHelper.ParseTemplateID(templateId);
		if (!string.IsNullOrEmpty(pSlug)) { lookup[pSlug] = templateId; allLookup[pSlug] = templateId; }
		if (!string.IsNullOrWhiteSpace(rawLegacyId)) { lookup[rawLegacyId] = templateId; allLookup[rawLegacyId] = templateId; string ls = TemplateIDHelper.GenerateSlug(rawLegacyId); lookup[ls] = templateId; allLookup[ls] = templateId; }
		if (!string.IsNullOrWhiteSpace(name)) { lookup[name] = templateId; allLookup[name] = templateId; string ns = TemplateIDHelper.GenerateSlug(name); lookup[ns] = templateId; allLookup[ns] = templateId; }
		if (!string.IsNullOrWhiteSpace(modelPath))
		{
			string[] keys = { modelPath, Path.GetFileName(modelPath), Path.GetFileNameWithoutExtension(modelPath), TemplateIDHelper.GenerateSlug(modelPath) };
			foreach(var k in keys) { lookup[k] = templateId; allLookup[k] = templateId; }
		}
	}

	private static void MigrateTerrainCategory(JsonObject terrainRoot, string categoryKey, string objectType, Dictionary<string, string> primaryLookup, Dictionary<string, string>? secondaryLookup, Dictionary<string, string>? allLookup, params string[] oldKeys)
	{
		if (!terrainRoot.TryGetPropertyValue(categoryKey, out var arrNode) || arrNode is not JsonArray arr) return;
		
		foreach (var node in arr.OfType<JsonObject>())
		{
			string rawId = ExtractTerrainId(node, oldKeys);
			string targetTemplateId = ResolveTerrainTemplateId(rawId, objectType, primaryLookup, secondaryLookup, allLookup);
			node["TemplateId"] = targetTemplateId;
		}
	}

	private static string ExtractTerrainId(JsonObject node, string[] oldKeys)
	{
		string rawId = node["TemplateId"]?.ToString() ?? "";
		
		rawId = ExtractFromOldKeys(node, oldKeys, rawId);
		
		if (string.IsNullOrEmpty(rawId)) rawId = node["TemplateID"]?.ToString() ?? "";
		node.Remove("TemplateID");
		return rawId;
	}
	
	private static string ExtractFromOldKeys(JsonObject node, string[] oldKeys, string rawId)
	{
		foreach (var k in oldKeys)
		{
			if (string.IsNullOrEmpty(rawId) && node.TryGetPropertyValue(k, out var val)) rawId = val?.ToString() ?? "";
			node.Remove(k);
		}
		return rawId;
	}

	private static string ResolveTerrainTemplateId(string rawId, string objectType, Dictionary<string, string> primary, Dictionary<string, string>? secondary, Dictionary<string, string>? all)
	{
		if (string.IsNullOrWhiteSpace(rawId)) return string.Empty;
		if (rawId.Contains('/')) return rawId;

		string matched = CheckDictionaries(rawId, primary, secondary, all);
		if (!string.IsNullOrEmpty(matched)) return matched;

		string rawSlug = TemplateIDHelper.GenerateSlug(rawId);
		string matchedSlug = CheckPrimaryOrAllDictionaries(rawSlug, primary, all);
		if (!string.IsNullOrEmpty(matchedSlug)) return matchedSlug;
		
		return TemplateIDHelper.NormalizeTemplateID(objectType, rawId);
	}

	private static string CheckDictionaries(string key, params Dictionary<string, string>?[] dicts)
	{
		foreach (var d in dicts)
		{
			if (d != null && d.TryGetValue(key, out var matched)) return matched;
		}
		return string.Empty;
	}

	private static string CheckPrimaryOrAllDictionaries(string key, Dictionary<string, string> primary, Dictionary<string, string>? all)
	{
		if (primary.TryGetValue(key, out var matchedPrimary)) return matchedPrimary;
		if (all != null && all.TryGetValue(key, out var matchedAll)) return matchedAll;
		return string.Empty;
	}
	public Task<MigrationResult> UpAsync(string mapDirectory, IProgress<MigrationProgressUpdate>? progress = null)
	{
		return Task.Run(() => Up(mapDirectory, progress));
	}

	private static JsonObject CreateDefaultShaderConfig(
		string name,
		int transitionMode,
		int direction,
		string edgeColorHex,
		float edgeWidth,
		float edgeEmission,
		float noiseScale,
		float noiseRoughness,
		float fresnelPower,
		float vertexDisplacement,
		float alphaFade,
		float duration)
	{
		return new JsonObject
		{
			["Name"] = name,
			["TransitionMode"] = transitionMode,
			["Direction"] = direction,
			["EdgeColor"] = edgeColorHex,
			["EdgeWidth"] = edgeWidth,
			["EdgeEmission"] = edgeEmission,
			["NoiseScale"] = noiseScale,
			["NoiseRoughness"] = noiseRoughness,
			["FresnelPower"] = fresnelPower,
			["VertexDisplacement"] = vertexDisplacement,
			["AlphaFade"] = alphaFade,
			["Duration"] = duration,
			["AssetType"] = "SpawnShader"
		};
	}

	private static bool ModelExistsInOtherCategories(JsonObject templatesObj, string fileName, string slug)
	{
		return new[] { "Units", "Buildings", "Resources", "Props" }
			.Where(cat => templatesObj.ContainsKey(cat) && templatesObj[cat] is JsonArray)
			.SelectMany(cat => templatesObj[cat]!.AsArray().OfType<JsonObject>())
			.Any(item => 
				string.Equals(item["ModelPath"]?.ToString(), fileName, StringComparison.OrdinalIgnoreCase) ||
				string.Equals(TemplateIDHelper.GenerateSlug(item["ModelPath"]?.ToString() ?? ""), slug, StringComparison.OrdinalIgnoreCase) ||
				string.Equals(TemplateIDHelper.GenerateSlug(item["TemplateID"]?.ToString() ?? ""), slug, StringComparison.OrdinalIgnoreCase));
	}

	private static void EnsureTemplateForManifestAsset(JsonObject metadataRoot, JsonObject templatesObj, string category, string fileName, string hash)
	{
		if (string.IsNullOrWhiteSpace(fileName)) return;

		string slug = TemplateIDHelper.GenerateSlug(fileName);
		string normalizedCategory = MapAssetHelper.NormalizeCategoryKey(category);

		switch (normalizedCategory.ToLowerInvariant())
		{
			case "character" or "unit" or "characters" or "units":
				EnsureUnitTemplate(templatesObj, fileName, slug);
				break;
			case "building" or "buildings":
				EnsureBuildingTemplate(templatesObj, fileName, slug);
				break;
			case "prop" or "props":
				EnsurePropTemplate(templatesObj, fileName, slug);
				break;
			case "resource" or "resources":
				EnsureResourceTemplate(templatesObj, fileName, slug);
				break;
			case "decal" or "decals":
				EnsureDecalTemplate(metadataRoot, fileName, slug, hash);
				break;
			case "terrain" or "textures":
				EnsureTerrainTemplate(metadataRoot, fileName, slug, hash);
				break;
			case "spritesheet" or "spritesheets" or "vfx":
				EnsureSpritesheetTemplate(metadataRoot, fileName, slug, hash);
				break;
			case "icon" or "icons":
				EnsureIconTemplate(metadataRoot, slug, hash);
				break;
			case "shader" or "shaders" or "gdshader":
				EnsureShaderTemplate(metadataRoot, slug, hash);
				break;
		}
	}

	private static void EnsureUnitTemplate(JsonObject templatesObj, string fileName, string slug)
	{
		string unitTemplateId = TemplateIDHelper.NormalizeTemplateID("unit", slug);
		var unitsArr = GetOrAddArray(templatesObj, "Units");

		if (CheckIfTemplateExists(unitsArr, unitTemplateId, fileName, slug) || ModelExistsInOtherCategories(templatesObj, fileName, slug)) return;

		unitsArr.Add(new JsonObject
		{
			["TemplateID"] = unitTemplateId,
			["Name"] = slug,
			["Description"] = "",
			["ModelPath"] = fileName,
			["VisualMode"] = GetVisualMode(fileName),
			["Scale"] = 1.0f,
			["PathingType"] = 9,
			["DespillPlayerColor"] = false,
			["NormalizeLuminance"] = true
		});
	}

	private static JsonArray GetOrAddArray(JsonObject templatesObj, string key)
	{
		if (!templatesObj.ContainsKey(key) || templatesObj[key] is not JsonArray)
		{
			templatesObj[key] = new JsonArray();
		}
		return (JsonArray)templatesObj[key]!;
	}

	private static bool CheckIfTemplateExists(JsonArray array, string templateId, string fileName, string slug)
	{
		return array.OfType<JsonObject>().Any(t =>
			string.Equals(t["TemplateID"]?.ToString(), templateId, StringComparison.OrdinalIgnoreCase) ||
			string.Equals(TemplateIDHelper.GenerateSlug(t["TemplateID"]?.ToString() ?? ""), slug, StringComparison.OrdinalIgnoreCase) ||
			string.Equals(t["ModelPath"]?.ToString(), fileName, StringComparison.OrdinalIgnoreCase) ||
			string.Equals(TemplateIDHelper.GenerateSlug(t["ModelPath"]?.ToString() ?? ""), slug, StringComparison.OrdinalIgnoreCase));
	}

	private static string GetVisualMode(string fileName)
	{
		return fileName.EndsWith(".rmesh", StringComparison.OrdinalIgnoreCase) ? "3D Mesh (.rmesh)" : "GroundPlane";
	}

	private static void EnsureBuildingTemplate(JsonObject templatesObj, string fileName, string slug)
	{
		string buildingTemplateId = TemplateIDHelper.NormalizeTemplateID("building", slug);
		var buildingsArr = GetOrAddArray(templatesObj, "Buildings");

		if (CheckIfTemplateExists(buildingsArr, buildingTemplateId, fileName, slug) || ModelExistsInOtherCategories(templatesObj, fileName, slug)) return;

		buildingsArr.Add(new JsonObject
		{
			["TemplateID"] = buildingTemplateId,
			["Name"] = slug,
			["Description"] = "",
			["ModelPath"] = fileName,
			["VisualMode"] = GetVisualMode(fileName),
			["Scale"] = 1.5f,
			["PathingType"] = 32,
			["DespillPlayerColor"] = false,
			["NormalizeLuminance"] = true
		});
	}

	private static void EnsurePropTemplate(JsonObject templatesObj, string fileName, string slug)
	{
		string propTemplateId = TemplateIDHelper.NormalizeTemplateID("prop", slug);
		var propsArr = GetOrAddArray(templatesObj, "Props");

		if (CheckIfTemplateExists(propsArr, propTemplateId, fileName, slug) || ModelExistsInOtherCategories(templatesObj, fileName, slug)) return;

		propsArr.Add(new JsonObject
		{
			["TemplateID"] = propTemplateId,
			["Name"] = slug,
			["Description"] = "",
			["ModelPath"] = fileName,
			["VisualMode"] = GetVisualMode(fileName),
			["Scale"] = 1.25f,
			["PathingType"] = 255,
			["DespillPlayerColor"] = false,
			["NormalizeLuminance"] = true,
			["IgnorePlayerColor"] = true
		});
	}

	private static void EnsureResourceTemplate(JsonObject templatesObj, string fileName, string slug)
	{
		string resourceTemplateId = TemplateIDHelper.NormalizeTemplateID("resource", slug);
		var resourcesArr = GetOrAddArray(templatesObj, "Resources");

		if (CheckIfTemplateExists(resourcesArr, resourceTemplateId, fileName, slug) || ModelExistsInOtherCategories(templatesObj, fileName, slug)) return;

		resourcesArr.Add(new JsonObject
		{
			["TemplateID"] = resourceTemplateId,
			["Name"] = slug,
			["Description"] = "",
			["ModelPath"] = fileName,
			["VisualMode"] = GetVisualMode(fileName),
			["Scale"] = 2.75f,
			["PathingType"] = 255,
			["DespillPlayerColor"] = false,
			["NormalizeLuminance"] = true,
			["IgnorePlayerColor"] = true
		});
	}

	private static void EnsureDecalTemplate(JsonObject metadataRoot, string fileName, string slug, string hash)
	{
		string decalTemplateId = TemplateIDHelper.NormalizeTemplateID("decal", slug);
		var decalsObj = GetOrAddObject(metadataRoot, "decals");

		if (CheckIfDecalExists(decalsObj, decalTemplateId, fileName, slug)) return;

		decalsObj[decalTemplateId] = new JsonObject
		{
			["Hash"] = hash ?? string.Empty,
			["TexturePath"] = fileName,
			["Opacity"] = 1.0f,
			["Brightness"] = 1.0f,
			["Contrast"] = 1.0f,
			["Saturation"] = 1.0f
		};
	}

	private static JsonObject GetOrAddObject(JsonObject parent, string key)
	{
		if (!parent.ContainsKey(key) || parent[key] is not JsonObject)
		{
			parent[key] = new JsonObject();
		}
		return (JsonObject)parent[key]!;
	}

	private static bool CheckIfDecalExists(JsonObject decalsObj, string templateId, string fileName, string slug)
	{
		foreach (var kvp in decalsObj)
		{
			if (IsDecalMatch(kvp.Key, kvp.Value as JsonObject, templateId, fileName, slug))
			{
				return true;
			}
		}
		return false;
	}

	private static bool IsDecalMatch(string key, JsonObject? dObj, string templateId, string fileName, string slug)
	{
		if (string.Equals(key, templateId, StringComparison.OrdinalIgnoreCase) ||
		    string.Equals(key, slug, StringComparison.OrdinalIgnoreCase) ||
		    string.Equals(key, fileName, StringComparison.OrdinalIgnoreCase) ||
		    string.Equals(TemplateIDHelper.GenerateSlug(key), slug, StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}

		if (dObj == null) return false;

		string? texturePath = GetStringValue(dObj, "TexturePath");
		if (texturePath == null) return false;

		return string.Equals(texturePath, fileName, StringComparison.OrdinalIgnoreCase) ||
		       string.Equals(texturePath, slug, StringComparison.OrdinalIgnoreCase) ||
		       string.Equals(TemplateIDHelper.GenerateSlug(texturePath), slug, StringComparison.OrdinalIgnoreCase);
	}

	private static void EnsureTerrainTemplate(JsonObject metadataRoot, string fileName, string slug, string hash)
	{
		string terrainTemplateId = TemplateIDHelper.NormalizeTemplateID("terrain", slug);
		if (!metadataRoot.ContainsKey("textures") || metadataRoot["textures"] is not JsonObject)
		{
			metadataRoot["textures"] = new JsonObject();
		}
		var texturesObj = metadataRoot["textures"]!.AsObject();
		bool exists = texturesObj.Any(kvp =>
			string.Equals(kvp.Key, terrainTemplateId, StringComparison.OrdinalIgnoreCase) ||
			string.Equals(kvp.Key, slug, StringComparison.OrdinalIgnoreCase) ||
			string.Equals(kvp.Key, fileName, StringComparison.OrdinalIgnoreCase) ||
			(kvp.Value is JsonObject tObj && string.Equals(tObj["AssetType"]?.ToString(), fileName, StringComparison.OrdinalIgnoreCase)));
		if (!exists)
		{
			texturesObj[terrainTemplateId] = new JsonObject
			{
				["Hash"] = hash ?? string.Empty,
				["AssetType"] = fileName,
				["ScaleFactor"] = 1.0f,
				["Brightness"] = 1.0f,
				["Contrast"] = 1.0f,
				["Saturation"] = 1.0f,
				["TileMode"] = "Stochastic",
				["DefaultPathingCode"] = 8 | 32 | 4,
				["DecalBombingRules"] = new JsonArray(),
				["VfxBombingRules"] = new JsonArray()
			};
		}
	}

	private static void EnsureSpritesheetTemplate(JsonObject metadataRoot, string fileName, string slug, string hash)
	{
		string spritesheetTemplateId = TemplateIDHelper.NormalizeTemplateID("spritesheet", slug);
		if (!metadataRoot.ContainsKey("vfx_spritesheets") || metadataRoot["vfx_spritesheets"] is not JsonObject)
		{
			metadataRoot["vfx_spritesheets"] = new JsonObject();
		}
		var vfxObj = metadataRoot["vfx_spritesheets"]!.AsObject();
		bool exists = vfxObj.Any(kvp =>
			string.Equals(kvp.Key, spritesheetTemplateId, StringComparison.OrdinalIgnoreCase) ||
			string.Equals(kvp.Key, slug, StringComparison.OrdinalIgnoreCase) ||
			string.Equals(kvp.Key, fileName, StringComparison.OrdinalIgnoreCase) ||
			(kvp.Value is JsonObject sObj && string.Equals(sObj["AssetType"]?.ToString(), fileName, StringComparison.OrdinalIgnoreCase)));
		if (!exists)
		{
			vfxObj[spritesheetTemplateId] = new JsonObject
			{
				["Hash"] = hash ?? string.Empty,
				["AssetType"] = fileName,
				["Columns"] = 4,
				["Rows"] = 4,
				["Fps"] = 20.0f
			};
		}
	}

	private static void EnsureIconTemplate(JsonObject metadataRoot, string slug, string hash)
	{
		string iconTemplateId = TemplateIDHelper.NormalizeTemplateID("icon", slug);
		if (!metadataRoot.ContainsKey("icons") || metadataRoot["icons"] is not JsonObject)
		{
			metadataRoot["icons"] = new JsonObject();
		}
		var iconsObj = metadataRoot["icons"]!.AsObject();
		bool exists = iconsObj.Any(kvp => string.Equals(kvp.Key, iconTemplateId, StringComparison.OrdinalIgnoreCase));
		if (!exists)
		{
			iconsObj[iconTemplateId] = new JsonObject
			{
				["Hash"] = hash ?? string.Empty
			};
		}
	}

	private static void EnsureShaderTemplate(JsonObject metadataRoot, string slug, string hash)
	{
		string shaderTemplateId = TemplateIDHelper.NormalizeTemplateID("gdshader", slug);
		if (!metadataRoot.ContainsKey("gdshader") || metadataRoot["gdshader"] is not JsonObject)
		{
			metadataRoot["gdshader"] = new JsonObject();
		}
		var shaderObj = metadataRoot["gdshader"]!.AsObject();
		bool exists = shaderObj.Any(kvp => string.Equals(kvp.Key, shaderTemplateId, StringComparison.OrdinalIgnoreCase));
		if (!exists)
		{
			shaderObj[shaderTemplateId] = new JsonObject
			{
				["Hash"] = hash ?? string.Empty
			};
		}
	}

	private static void UnionCategoryInto(JsonObject targetContainer, string targetCategory, JsonObject sourceObject)
	{
		if (!targetContainer.ContainsKey(targetCategory) || targetContainer[targetCategory] is not JsonObject)
		{
			targetContainer[targetCategory] = new JsonObject();
		}
		var categoryTarget = targetContainer[targetCategory]!.AsObject();

		foreach (var pair in sourceObject)
		{
			if (categoryTarget.ContainsKey(pair.Key) && categoryTarget[pair.Key] is JsonObject existingObj && pair.Value is JsonObject sourceObj)
			{
				foreach (var prop in sourceObj)
				{
					existingObj[prop.Key] = prop.Value?.DeepClone();
				}
			}
			else if (pair.Value != null)
			{
				categoryTarget[pair.Key] = pair.Value.DeepClone();
			}
		}
	}
}