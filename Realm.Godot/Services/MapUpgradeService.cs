using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Godot;
using Realm.Ecs.Services;
using Realm.Godot.Utils;
using Realm.Shared;
using Realm.Shared.Audio;
using Realm.Shared.Distribution;
using Realm.Shared.Metadata;
using Realm.Shared.ModelOptimization;
using Realm.Shared.Textures;

namespace Realm.Godot.Services;

public record MigrationProgressUpdate(
	string CurrentMigration,
	int StepIndex,
	int TotalSteps,
	string Message
);

public class MigrationResult
{
	public bool Success { get; set; }
	public string? ErrorMessage { get; set; }
	public string FromVersion { get; set; } = string.Empty;
	public string ToVersion { get; set; } = string.Empty;
}

public class UpgradeResult
{
	public bool Success { get; set; }
	public string? ErrorMessage { get; set; }
	public string InitialVersion { get; set; } = string.Empty;
	public string FinalVersion { get; set; } = string.Empty;
	public List<MigrationResult> StepResults { get; set; } = new();
}

public interface IMapMigration
{
	string FromVersion { get; }
	string ToVersion { get; }
	string Description { get; }
	MigrationResult Up(string mapDirectory, IProgress<MigrationProgressUpdate>? progress = null);
	Task<MigrationResult> UpAsync(string mapDirectory, IProgress<MigrationProgressUpdate>? progress = null);
}

public class Migration_0_0_1_InitialCanonicalFormat : IMapMigration
{
	public string FromVersion => "v0.0.0";
	public string ToVersion => "v0.0.1";
	public string Description => "Migrate legacy map fields and asset dictionaries to canonical zero-fallback v0.0.1 format";

	private static readonly HashSet<string> ModelExtensions = new(StringComparer.OrdinalIgnoreCase)
	{
		".glb", ".gltf", ".fbx", ".obj", ".dae"
	};

	private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
	{
		".ogg", ".wav", ".mp3", ".flac", ".aac", ".aiff", ".aif", ".wma"
	};

	private static readonly HashSet<string> TextureExtensions = new(StringComparer.OrdinalIgnoreCase)
	{
		".png", ".jpg", ".jpeg", ".tga", ".bmp"
	};

	public MigrationResult Up(string mapDirectory, IProgress<MigrationProgressUpdate>? progress = null)
	{
		try
		{
			string metadataPath = Path.Combine(mapDirectory, "metadata.json");
			JsonObject metadataRoot = LoadMetadata(metadataPath);

			const int totalSteps = 6;

			progress?.Report(new MigrationProgressUpdate(Description, 1, totalSteps, "Normalizing metadata properties..."));
			NormalizeMapProperties(metadataRoot);
			MigrateLegacyEntityArrays(metadataRoot);

			progress?.Report(new MigrationProgressUpdate(Description, 2, totalSteps, "Migrating asset dictionaries to manifest..."));
			var unionedAssets = MapAssetHelper.LoadAssets(mapDirectory);
			MigrateAssetDictionaries(metadataRoot, unionedAssets);

			progress?.Report(new MigrationProgressUpdate(Description, 3, totalSteps, "Converting 3D model assets to .rmesh..."));
			ConvertModelAssets(mapDirectory, metadataRoot, unionedAssets, progress, Description, 3, totalSteps);

			progress?.Report(new MigrationProgressUpdate(Description, 4, totalSteps, "Converting audio assets to .raud..."));
			ConvertAudioAssets(mapDirectory, metadataRoot, unionedAssets, progress, Description, 4, totalSteps);

			progress?.Report(new MigrationProgressUpdate(Description, 5, totalSteps, "Converting textures to .rtex..."));
			ConvertTextureAssets(mapDirectory, metadataRoot, unionedAssets, progress, Description, 5, totalSteps);

			progress?.Report(new MigrationProgressUpdate(Description, 6, totalSteps, "Normalizing swatch indices and manifest..."));
			MapWorkspaceService.NormalizeTextureEntries(unionedAssets, mapDirectory);
			MapAssetHelper.SaveAssetsToManifest(mapDirectory, unionedAssets, removeFromMetadata: true);

			CleanupMetadata(metadataRoot);
			metadataRoot["GameBuildNumber"] = ToVersion;

			progress?.Report(new MigrationProgressUpdate(Description, 6, totalSteps, "Saving migrated metadata.json..."));
			MapJsonFormatter.SaveFormattedJson(metadataPath, metadataRoot);

			return new MigrationResult { Success = true, FromVersion = FromVersion, ToVersion = ToVersion };
		}
		catch (Exception ex)
		{
			return new MigrationResult { Success = false, ErrorMessage = $"Migration 0.0.1 failed: {ex.Message}", FromVersion = FromVersion, ToVersion = ToVersion };
		}
	}

	private static JsonObject LoadMetadata(string metadataPath)
	{
		if (!File.Exists(metadataPath)) return new JsonObject();
		string text = File.ReadAllText(metadataPath);
		return JsonNode.Parse(text)?.AsObject() ?? new JsonObject();
	}

	private static void NormalizeMapProperties(JsonObject metadataRoot)
	{
		if (!metadataRoot.TryGetPropertyValue("MapProperties", out var propsNode) || propsNode is not JsonObject mapProperties) return;

		EnsureMapName(mapProperties);
		EnsureMapDescription(mapProperties);
	}

	private static void EnsureMapName(JsonObject mapProperties)
	{
		if (!mapProperties.ContainsKey("MapName"))
		{
			string nameVal = mapProperties["Name"]?.ToString() ?? mapProperties["Title"]?.ToString() ?? "";
			if (!string.IsNullOrEmpty(nameVal)) mapProperties["MapName"] = nameVal;
		}
		mapProperties.Remove("Name");
		mapProperties.Remove("Title");
	}

	private static void EnsureMapDescription(JsonObject mapProperties)
	{
		if (!mapProperties.ContainsKey("MapDescription"))
		{
			string descVal = mapProperties["Description"]?.ToString() ?? "";
			if (!string.IsNullOrEmpty(descVal)) mapProperties["MapDescription"] = descVal;
		}
		mapProperties.Remove("Description");
	}

	private static void MigrateLegacyEntityArrays(JsonObject metadataRoot)
	{
		var legacyEntityArrays = new (string LegacyKey, string CanonicalKey)[]
		{
			("Units", "CustomUnits"), ("Buildings", "CustomBuildings"), ("Resources", "CustomResources"),
			("Props", "CustomProps"), ("Abilities", "CustomAbilities"), ("Weapons", "CustomWeapons"),
			("Upgrades", "CustomUpgrades"), ("Items", "CustomItems"), ("Attachments", "CustomAttachments"), ("Vfx", "CustomVfx")
		};

		foreach (var (legacyKey, canonicalKey) in legacyEntityArrays)
		{
			if (!metadataRoot.TryGetPropertyValue(legacyKey, out var legacyArrayNode) || legacyArrayNode is not JsonArray legacyArray) continue;
			
			if (!metadataRoot.ContainsKey(canonicalKey) || metadataRoot[canonicalKey] is not JsonArray)
			{
				metadataRoot[canonicalKey] = new JsonArray();
			}
			var canonicalArray = metadataRoot[canonicalKey]!.AsArray();
			foreach (var item in legacyArray)
			{
				if (item != null) canonicalArray.Add(item.DeepClone());
			}
			metadataRoot.Remove(legacyKey);
		}
	}

	private static void MigrateAssetDictionaries(JsonObject metadataRoot, MapManifestAssets unionedAssets)
	{
		if (metadataRoot.TryGetPropertyValue("vfx", out var vfxNode) && vfxNode is JsonObject vfxObj)
		{
			MapUpgradeService.MergeCategoryInto(unionedAssets, "Spritesheet", vfxObj);
			metadataRoot.Remove("vfx");
		}
		if (metadataRoot.TryGetPropertyValue("noise", out var noiseNode) && noiseNode is JsonObject noiseObj)
		{
			MapUpgradeService.MergeCategoryInto(unionedAssets, "Noise", noiseObj);
			metadataRoot.Remove("noise");
		}
		if (metadataRoot.TryGetPropertyValue("ribbon_textures", out var ribbonNode) && ribbonNode is JsonObject ribbonObj)
		{
			MapUpgradeService.MergeCategoryInto(unionedAssets, "Ribbon", ribbonObj);
			metadataRoot.Remove("ribbon_textures");
		}
	}

	private static void CleanupMetadata(JsonObject metadataRoot)
	{
		metadataRoot.Remove("Assets");
		if (metadataRoot["MapProperties"] is JsonObject mapPropsObj)
		{
			mapPropsObj.Remove("Assets");
		}
	}

	public Task<MigrationResult> UpAsync(string mapDirectory, IProgress<MigrationProgressUpdate>? progress = null)
	{
		return Task.Run(() => Up(mapDirectory, progress));
	}

	private static void ConvertModelAssets(
		string mapDirectory,
		JsonObject metadataRoot,
		MapManifestAssets unionedAssets,
		IProgress<MigrationProgressUpdate>? progress,
		string description,
		int stepIndex,
		int totalSteps)
	{
		ProcessModelFiles(mapDirectory, progress, description, stepIndex, totalSteps);
		UpdateEntityModelPaths(metadataRoot);
		UpdateModelDictionaries(metadataRoot);
		UpdateModelNormalModes(metadataRoot);
		UpdateManifestAssetCategories(unionedAssets, new[] { "Character", "Building", "Prop", "Item" }, ModelExtensions, ".rmesh");
	}

	private static void ProcessModelFiles(string mapDirectory, IProgress<MigrationProgressUpdate>? progress, string description, int stepIndex, int totalSteps)
	{
		string assetsDir = Path.Combine(mapDirectory, "Assets");
		if (!Directory.Exists(assetsDir)) return;

		string[] allFiles = Directory.GetFiles(assetsDir, "*.*", SearchOption.AllDirectories);
		var modelFiles = allFiles.Where(f => ModelExtensions.Contains(Path.GetExtension(f))).ToList();

		for (int i = 0; i < modelFiles.Count; i++)
		{
			ConvertSingleModel(modelFiles[i], i, modelFiles.Count, progress, description, stepIndex, totalSteps);
		}
	}

	private static void ConvertSingleModel(string modelFile, int index, int count, IProgress<MigrationProgressUpdate>? progress, string description, int stepIndex, int totalSteps)
	{
		string normalized = modelFile.Replace("\\", "/");
		if (normalized.Contains("/bin/") || normalized.Contains("/obj/") || normalized.Contains("/.git/") || normalized.Contains("/.godot/") || normalized.Contains("/vscode_embedded/") || normalized.Contains("/wasi_sdk_embedded/"))
		{
			return;
		}

		string fileName = Path.GetFileName(modelFile);
		progress?.Report(new MigrationProgressUpdate(description, stepIndex, totalSteps, $"Converting model {fileName} -> .rmesh ({index + 1}/{count})..."));

		string assetType = DetermineModelAssetType(normalized);
		string targetRmesh = Path.ChangeExtension(modelFile, ".rmesh");
		var result = ModelConverter.ConvertToRmesh(modelFile, targetRmesh, assetType, force: true);
		
		if (result.Success && File.Exists(targetRmesh))
		{
			DeleteOriginalFile(modelFile);
		}
	}

	private static string DetermineModelAssetType(string normalizedPath)
	{
		string lowerPath = normalizedPath.ToLowerInvariant();
		if (lowerPath.Contains("/units/") || lowerPath.Contains("/characters/")) return "Character";
		if (lowerPath.Contains("/buildings/") || lowerPath.Contains("/structures/")) return "Building";
		if (lowerPath.Contains("/items/") || lowerPath.Contains("/attachments/") || lowerPath.Contains("/projectiles/") || lowerPath.Contains("/weapons/")) return "Item";
		return "Prop";
	}

	private static void DeleteOriginalFile(string file)
	{
		try
		{
			var attrs = File.GetAttributes(file);
			if ((attrs & FileAttributes.ReadOnly) != 0) File.SetAttributes(file, attrs & ~FileAttributes.ReadOnly);
			File.Delete(file);
		}
		catch { }
	}

	private static void UpdateEntityModelPaths(JsonObject metadataRoot)
	{
		var entityArrayNames = new[] { "CustomUnits", "CustomBuildings", "CustomResources", "CustomProps", "CustomItems", "CustomAttachments", "CustomWeapons" };
		foreach (var arrayName in entityArrayNames)
		{
			if (!metadataRoot.TryGetPropertyValue(arrayName, out var arrNode) || arrNode is not JsonArray arr) continue;
			foreach (var item in arr)
			{
				if (item is JsonObject itemObj)
				{
					UpdateSingleEntityModelPath(itemObj);
				}
			}
		}
	}

	private static void UpdateSingleEntityModelPath(JsonObject itemObj)
	{
		if (itemObj.TryGetPropertyValue("ModelPath", out var mpVal) && mpVal != null)
		{
			string mp = mpVal.ToString();
			if (ModelExtensions.Contains(Path.GetExtension(mp))) itemObj["ModelPath"] = Path.ChangeExtension(mp, ".rmesh");
		}
		if (itemObj.TryGetPropertyValue("PortraitModelPath", out var pmpVal) && pmpVal != null)
		{
			string pmp = pmpVal.ToString();
			if (ModelExtensions.Contains(Path.GetExtension(pmp))) itemObj["PortraitModelPath"] = Path.ChangeExtension(pmp, ".rmesh");
		}
		if (itemObj.ContainsKey("NormalMode"))
		{
			itemObj["DespillPlayerColor"] = false;
			itemObj.Remove("NormalMode");
		}
		if (itemObj.ContainsKey("RecalculateNormals"))
		{
			itemObj["DespillPlayerColor"] = false;
			itemObj.Remove("RecalculateNormals");
		}
	}

	private static void UpdateModelDictionaries(JsonObject metadataRoot)
	{
		var modelDictNames = new[]
		{
			"ModelOffsets", "ModelScales", "ModelCollisionCircleRatios", "ModelObstacleRadii",
			"ModelBrightness", "ModelColorTint", "ModelDespillPlayerColor", "ModelNormalizeLuminance",
			"ModelIgnorePlayerColor", "ModelSpawnShaders", "ModelDeathShaders"
		};

		foreach (var dictName in modelDictNames)
		{
			if (!metadataRoot.TryGetPropertyValue(dictName, out var dictNode) || dictNode is not JsonObject dictObj) continue;
			
			var keysToMigrate = new List<(string OldKey, string NewKey, JsonNode? Value)>();
			foreach (var prop in dictObj)
			{
				string ext = Path.GetExtension(prop.Key);
				if (ModelExtensions.Contains(ext))
				{
					keysToMigrate.Add((prop.Key, Path.ChangeExtension(prop.Key, ".rmesh"), prop.Value?.DeepClone()));
				}
			}
			foreach (var (oldKey, newKey, val) in keysToMigrate)
			{
				dictObj.Remove(oldKey);
				dictObj[newKey] = val;
			}
		}
	}

	private static void UpdateModelNormalModes(JsonObject metadataRoot)
	{
		if (!metadataRoot.TryGetPropertyValue("ModelNormalModes", out var nrmNode) || nrmNode is not JsonObject nrmObj) return;

		if (!metadataRoot.ContainsKey("ModelDespillPlayerColor") || metadataRoot["ModelDespillPlayerColor"] is not JsonObject)
		{
			metadataRoot["ModelDespillPlayerColor"] = new JsonObject();
		}
		var despillObj = metadataRoot["ModelDespillPlayerColor"]!.AsObject();
		foreach (var prop in nrmObj)
		{
			despillObj[Path.ChangeExtension(prop.Key, ".rmesh")] = true;
		}
		metadataRoot.Remove("ModelNormalModes");
	}

	private static void UpdateManifestAssetCategories(MapManifestAssets unionedAssets, string[] categories, HashSet<string> extensions, string targetExtension)
	{
		foreach (var catName in categories)
		{
			var dict = unionedAssets.GetCategory(catName);
			if (dict == null) continue;

			var keysToMigrate = new List<(string OldKey, string NewKey, string Value)>();
			foreach (var item in dict)
			{
				if (extensions.Contains(Path.GetExtension(item.Key)))
				{
					keysToMigrate.Add((item.Key, Path.ChangeExtension(item.Key, targetExtension), item.Value));
				}
			}
			foreach (var (oldKey, newKey, val) in keysToMigrate)
			{
				dict.Remove(oldKey);
				dict[newKey] = val;
			}
		}
	}

	private static void ConvertAudioAssets(
		string mapDirectory,
		JsonObject metadataRoot,
		MapManifestAssets unionedAssets,
		IProgress<MigrationProgressUpdate>? progress,
		string description,
		int stepIndex,
		int totalSteps)
	{
		ProcessAudioFiles(mapDirectory, progress, description, stepIndex, totalSteps);
		UpdateManifestAssetCategories(unionedAssets, new[] { "SoundEffect", "Music" }, AudioExtensions, ".raud");
	}

	private static void ProcessAudioFiles(string mapDirectory, IProgress<MigrationProgressUpdate>? progress, string description, int stepIndex, int totalSteps)
	{
		string audioDir = Path.Combine(mapDirectory, "Assets", "audio");
		if (!Directory.Exists(audioDir)) return;

		string[] allFiles = Directory.GetFiles(audioDir, "*.*", SearchOption.AllDirectories);
		var audioFiles = allFiles.Where(f => AudioExtensions.Contains(Path.GetExtension(f))).ToList();

		for (int i = 0; i < audioFiles.Count; i++)
		{
			ConvertSingleAudio(audioFiles[i], i, audioFiles.Count, progress, description, stepIndex, totalSteps);
		}
	}

	private static void ConvertSingleAudio(string audioFile, int index, int count, IProgress<MigrationProgressUpdate>? progress, string description, int stepIndex, int totalSteps)
	{
		string fileName = Path.GetFileName(audioFile);
		progress?.Report(new MigrationProgressUpdate(description, stepIndex, totalSteps, $"Converting audio {fileName} -> .raud ({index + 1}/{count})..."));

		string targetRaud = Path.ChangeExtension(audioFile, ".raud");
		var result = AudioConverter.ConvertToRaud(audioFile, targetRaud);
		if (result.Success && File.Exists(targetRaud))
		{
			DeleteOriginalFile(audioFile);
		}
	}

	private static void ConvertTextureAssets(
		string mapDirectory,
		JsonObject metadataRoot,
		MapManifestAssets unionedAssets,
		IProgress<MigrationProgressUpdate>? progress,
		string description,
		int stepIndex,
		int totalSteps)
	{
		string assetsDir = Path.Combine(mapDirectory, "Assets");
		if (!Directory.Exists(assetsDir)) return;

		string[] allFiles = Directory.GetFiles(assetsDir, "*.*", SearchOption.AllDirectories);
		var textureFiles = allFiles.Where(f => TextureExtensions.Contains(Path.GetExtension(f))).ToList();

		for (int i = 0; i < textureFiles.Count; i++)
		{
			ConvertSingleTexture(textureFiles[i], i, textureFiles.Count, progress, description, stepIndex, totalSteps);
		}
	}

	private static void ConvertSingleTexture(string texFile, int index, int count, IProgress<MigrationProgressUpdate>? progress, string description, int stepIndex, int totalSteps)
	{
		string normalized = texFile.Replace("\\", "/");
		if (normalized.Contains("/bin/") || normalized.Contains("/obj/") || normalized.Contains("/.git/") || normalized.Contains("/.godot/") || normalized.Contains("/vscode_embedded/") || normalized.Contains("/wasi_sdk_embedded/"))
		{
			return;
		}

		string fileName = Path.GetFileName(texFile);
		progress?.Report(new MigrationProgressUpdate(description, stepIndex, totalSteps, $"Converting texture {fileName} -> .rtex ({index + 1}/{count})..."));

		DetermineTextureAssetProperties(normalized, out string assetType, out int columns, out int rows);

		string targetRtex = Path.ChangeExtension(texFile, ".rtex");
		var result = TextureConverter.ConvertTextureFile(texFile, targetRtex, assetType, columns, rows);
		if (result.Success && File.Exists(targetRtex))
		{
			DeleteOriginalFile(texFile);
		}
	}

	private static void DetermineTextureAssetProperties(string normalizedPath, out string assetType, out int columns, out int rows)
	{
		string lowerNorm = normalizedPath.ToLowerInvariant();
		columns = 1;
		rows = 1;

		if (lowerNorm.Contains("/decals/")) { assetType = "decal"; return; }
		if (lowerNorm.Contains("/icons/")) { assetType = "icon"; return; }
		if (lowerNorm.Contains("/skyboxes/")) { assetType = "skybox"; return; }
		if (lowerNorm.Contains("/ribbons/")) { assetType = "ribbon"; return; }
		if (lowerNorm.Contains("/noise/")) { assetType = "noise"; return; }
		
		if (lowerNorm.Contains("/vfx/"))
		{
			assetType = "vfx";
			columns = 4;
			rows = 4;
			return;
		}

		assetType = "texture";
	}

	public static void MergeCategoryInto(MapManifestAssets targetAssets, string category, JsonObject sourceObject)
	{
		var categoryTarget = targetAssets.GetCategory(category);
		if (categoryTarget == null)
		{
			categoryTarget = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			targetAssets.SetCategory(category, categoryTarget);
		}

		foreach (var pair in sourceObject)
		{
			if (pair.Value is JsonObject sourceObj)
			{
				string hash = sourceObj["hash"]?.ToString() ?? sourceObj["blake3"]?.ToString() ?? string.Empty;
				if (!string.IsNullOrEmpty(hash))
				{
					categoryTarget[pair.Key] = hash;
				}
			}
			else if (pair.Value != null)
			{
				categoryTarget[pair.Key] = pair.Value.ToString();
			}
		}
	}
}

public class Migration_0_0_2_NormalizeModelProperties : IMapMigration
{
	public string FromVersion => "v0.0.1";
	public string ToVersion => "v0.0.2";
	public string Description => "Normalize legacy top-level Model dictionaries into canonical Models dictionary";

	public MigrationResult Up(string mapDirectory, IProgress<MigrationProgressUpdate>? progress = null)
	{
		try
		{
			string metadataPath = Path.Combine(mapDirectory, "metadata.json");
			JsonObject metadataRoot = LoadMetadata(metadataPath);

			const int totalSteps = 2;
			progress?.Report(new MigrationProgressUpdate(Description, 1, totalSteps, "Normalizing model properties into Models dictionary..."));

			var modelsObj = GetOrCreateModelsObject(metadataRoot);
			MigrateModelProperties(metadataRoot, modelsObj);
			MigrateModelNormalModes(metadataRoot, modelsObj);

			metadataRoot["GameBuildNumber"] = ToVersion;

			progress?.Report(new MigrationProgressUpdate(Description, 2, totalSteps, "Saving migrated metadata.json..."));
			MapJsonFormatter.SaveFormattedJson(metadataPath, metadataRoot);

			return new MigrationResult { Success = true, FromVersion = FromVersion, ToVersion = ToVersion };
		}
		catch (Exception ex)
		{
			return new MigrationResult { Success = false, ErrorMessage = $"Migration 0.0.2 failed: {ex.Message}", FromVersion = FromVersion, ToVersion = ToVersion };
		}
	}

	private static JsonObject LoadMetadata(string metadataPath)
	{
		if (!File.Exists(metadataPath)) return new JsonObject();
		string text = File.ReadAllText(metadataPath);
		return JsonNode.Parse(text)?.AsObject() ?? new JsonObject();
	}

	private static JsonObject GetOrCreateModelsObject(JsonObject metadataRoot)
	{
		if (!metadataRoot.ContainsKey("Models") || metadataRoot["Models"] is not JsonObject)
		{
			metadataRoot["Models"] = new JsonObject();
		}
		return metadataRoot["Models"]!.AsObject();
	}

	private static void MigrateModelProperties(JsonObject metadataRoot, JsonObject modelsObj)
	{
		var dictMappings = new (string TopLevelKey, string ModelPropKey)[]
		{
			("ModelBrightness", "Brightness"), ("ModelCollisionCircleRatios", "CollisionCircleRatios"),
			("ModelColorTint", "ColorTint"), ("ModelDespillPlayerColor", "DespillPlayerColor"),
			("ModelIgnorePlayerColor", "IgnorePlayerColor"), ("ModelNormalizeLuminance", "NormalizeLuminance"),
			("ModelObstacleRadii", "ObstacleRadii"), ("ModelOffsets", "Offsets"),
			("ModelScales", "Scales"), ("ModelSpawnShaders", "SpawnShaders"), ("ModelDeathShaders", "DeathShaders")
		};

		foreach (var (topKey, propKey) in dictMappings)
		{
			if (!metadataRoot.TryGetPropertyValue(topKey, out var dictNode) || dictNode is not JsonObject dictObj) continue;

			foreach (var kvp in dictObj)
			{
				string modelKey = kvp.Key;
				if (string.IsNullOrWhiteSpace(modelKey) || kvp.Value == null) continue;

				if (!modelsObj.ContainsKey(modelKey) || modelsObj[modelKey] is not JsonObject)
				{
					modelsObj[modelKey] = new JsonObject();
				}
				modelsObj[modelKey]!.AsObject()[propKey] = kvp.Value.DeepClone();
			}
			metadataRoot.Remove(topKey);
		}
	}

	private static void MigrateModelNormalModes(JsonObject metadataRoot, JsonObject modelsObj)
	{
		if (!metadataRoot.TryGetPropertyValue("ModelNormalModes", out var nrmNode) || nrmNode is not JsonObject nrmObj) return;

		foreach (var kvp in nrmObj)
		{
			string modelKey = kvp.Key;
			if (string.IsNullOrWhiteSpace(modelKey)) continue;

			if (!modelsObj.ContainsKey(modelKey) || modelsObj[modelKey] is not JsonObject)
			{
				modelsObj[modelKey] = new JsonObject();
			}
			modelsObj[modelKey]!.AsObject()["DespillPlayerColor"] = true;
		}
		metadataRoot.Remove("ModelNormalModes");
	}

	public Task<MigrationResult> UpAsync(string mapDirectory, IProgress<MigrationProgressUpdate>? progress = null)
	{
		return Task.Run(() => Up(mapDirectory, progress));
	}
}

public class Migration_0_0_3_WaterProfilesAndShaders : IMapMigration
{
	public string FromVersion => "v0.0.2";
	public string ToVersion => "v0.0.3";
	public string Description => "Migrate water profiles schema, normalize custom shaders in metadata and manifest, and update build number to v0.0.3";

	public MigrationResult Up(string mapDirectory, IProgress<MigrationProgressUpdate>? progress = null)
	{
		try
		{
			string metadataPath = Path.Combine(mapDirectory, "metadata.json");
			JsonObject metadataRoot = LoadMetadata(metadataPath);

			const int totalSteps = 3;

			progress?.Report(new MigrationProgressUpdate(Description, 1, totalSteps, "Normalizing custom water profiles..."));
			NormalizeWaterProfiles(metadataRoot);

			progress?.Report(new MigrationProgressUpdate(Description, 2, totalSteps, "Normalizing shaders in manifest and metadata..."));
			var unionedAssets = MapAssetHelper.LoadAssets(mapDirectory);
			NormalizeShaders(metadataRoot, unionedAssets);
			MapAssetHelper.SaveAssetsToManifest(mapDirectory, unionedAssets, removeFromMetadata: true);

			CleanupMetadata(metadataRoot);

			progress?.Report(new MigrationProgressUpdate(Description, 3, totalSteps, "Updating map build number and saving metadata.json..."));
			metadataRoot["GameBuildNumber"] = ToVersion;
			MapJsonFormatter.SaveFormattedJson(metadataPath, metadataRoot);

			return new MigrationResult { Success = true, FromVersion = FromVersion, ToVersion = ToVersion };
		}
		catch (Exception ex)
		{
			return new MigrationResult { Success = false, ErrorMessage = $"Migration 0.0.3 failed: {ex.Message}", FromVersion = FromVersion, ToVersion = ToVersion };
		}
	}

	private static JsonObject LoadMetadata(string metadataPath)
	{
		if (!File.Exists(metadataPath)) return new JsonObject();
		string text = File.ReadAllText(metadataPath);
		return JsonNode.Parse(text)?.AsObject() ?? new JsonObject();
	}

	private static void NormalizeWaterProfiles(JsonObject metadataRoot)
	{
		if (!metadataRoot.TryGetPropertyValue("CustomWaterProfiles", out var node) || node is not JsonArray waterProfilesArray) return;

		foreach (var item in waterProfilesArray)
		{
			if (item is JsonObject profileObject)
			{
				NormalizeSingleWaterProfile(profileObject);
			}
		}
	}

	private static void NormalizeSingleWaterProfile(JsonObject profileObject)
	{
		profileObject.Remove("WaterType");
		SetDefaultValue(profileObject, "DetailTileMode", "Stochastic");
		SetDefaultValue(profileObject, "DetailStochasticTileSize", 1.0f);
		SetDefaultValue(profileObject, "DetailCrossFade", 0.0f);
		SetDefaultValue(profileObject, "DetailTexturePath", string.Empty);
		SetDefaultValue(profileObject, "UseDetailTexture", false);
		SetDefaultValue(profileObject, "DetailUvScaleX", 1.0f);
		SetDefaultValue(profileObject, "DetailUvScaleY", 1.0f);
		SetDefaultValue(profileObject, "DetailUvScrollX", 0.0f);
		SetDefaultValue(profileObject, "DetailUvScrollY", 0.0f);
		SetDefaultValue(profileObject, "DetailAlpha", 0.5f);
		SetDefaultValue(profileObject, "DetailBlendMode", 0);
	}

	private static void SetDefaultValue(JsonObject obj, string key, JsonNode? defaultValue)
	{
		if (!obj.ContainsKey(key) || obj[key] == null)
		{
			obj[key] = defaultValue;
		}
	}

	private static void NormalizeShaders(JsonObject metadataRoot, MapManifestAssets unionedAssets)
	{
		if (metadataRoot.TryGetPropertyValue("shaders", out var shadersNode) && shadersNode is JsonObject shadersObject)
		{
			MapUpgradeService.MergeCategoryInto(unionedAssets, "Shader", shadersObject);
		}
	}

	private static void CleanupMetadata(JsonObject metadataRoot)
	{
		metadataRoot.Remove("Assets");
		if (metadataRoot.TryGetPropertyValue("MapProperties", out var propsNode) && propsNode is JsonObject mapPropertiesObject)
		{
			mapPropertiesObject.Remove("Assets");
		}
	}

	public Task<MigrationResult> UpAsync(string mapDirectory, IProgress<MigrationProgressUpdate>? progress = null)
	{
		return Task.Run(() => Up(mapDirectory, progress));
	}
}

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
		if (itemObj["TexturePath"]?.ToString() is string p1 && !string.IsNullOrEmpty(p1)) return p1;
		if (itemObj["texturePath"]?.ToString() is string p2 && !string.IsNullOrEmpty(p2)) return p2;
		if (itemObj["AssetType"]?.ToString() is string p3 && !string.IsNullOrEmpty(p3)) return p3;
		if (itemObj["assetType"]?.ToString() is string p4 && !string.IsNullOrEmpty(p4)) return p4;
		if (itemObj["rtex"]?.ToString() is string p5 && !string.IsNullOrEmpty(p5)) return p5;
		
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
		var renamedKeys = new List<(string OldKey, string NewKey, JsonNode Value)>();
		foreach (var assetPair in categoryObj)
		{
			string cleanKey = DetermineCleanAssetKey(assetPair.Key, categoryName);
			if (!string.Equals(assetPair.Key, cleanKey, StringComparison.OrdinalIgnoreCase) && assetPair.Value != null)
			{
				renamedKeys.Add((assetPair.Key, cleanKey, assetPair.Value.DeepClone()));
			}
		}
		
		foreach (var (oldK, newK, v) in renamedKeys)
		{
			categoryObj.Remove(oldK);
			if (!categoryObj.ContainsKey(newK)) categoryObj[newK] = v;
		}

		foreach (var assetPair in categoryObj)
		{
			string hash = assetPair.Value is JsonObject obj ? (obj["hash"]?.ToString() ?? obj["blake3"]?.ToString() ?? string.Empty) : (assetPair.Value?.ToString() ?? string.Empty);
			EnsureTemplateForManifestAsset(metadataRoot, templatesObj, categoryName, assetPair.Key, hash);
		}
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
		if (!templatesObj.TryGetPropertyValue(categoryName, out var catNode) || catNode is not JsonArray catArr) return;

		foreach (var item in catArr.OfType<JsonObject>())
		{
			string tId = item["TemplateID"]?.ToString() ?? string.Empty;
			string objectId = item["UnitId"]?.ToString() ?? item["ObjectID"]?.ToString();
			RegisterInLookup(targetLookup, allTemplatesLookup, tId, item["Name"]?.ToString(), item["ModelPath"]?.ToString(), objectId);
		}
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
		foreach (var k in oldKeys)
		{
			if (string.IsNullOrEmpty(rawId) && node.TryGetPropertyValue(k, out var val)) rawId = val?.ToString() ?? "";
			node.Remove(k);
		}
		if (string.IsNullOrEmpty(rawId)) rawId = node["TemplateID"]?.ToString() ?? "";
		node.Remove("TemplateID");
		return rawId;
	}

	private static string ResolveTerrainTemplateId(string rawId, string objectType, Dictionary<string, string> primary, Dictionary<string, string>? secondary, Dictionary<string, string>? all)
	{
		if (string.IsNullOrWhiteSpace(rawId)) return string.Empty;
		if (rawId.Contains('/')) return rawId;

		var dicts = new[] { primary, secondary, all };
		foreach (var d in dicts)
		{
			if (d != null && d.TryGetValue(rawId, out var matched)) return matched;
		}

		string rawSlug = TemplateIDHelper.GenerateSlug(rawId);
		foreach (var d in dicts)
		{
			if (d != null && (d == primary || d == all) && d.TryGetValue(rawSlug, out var matchedSlug)) return matchedSlug;
		}
		
		return TemplateIDHelper.NormalizeTemplateID(objectType, rawId);
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
		if (!templatesObj.ContainsKey("Units") || templatesObj["Units"] is not JsonArray)
		{
			templatesObj["Units"] = new JsonArray();
		}
		var unitsArr = templatesObj["Units"]!.AsArray();
		bool exists = unitsArr.OfType<JsonObject>().Any(u =>
			string.Equals(u["TemplateID"]?.ToString(), unitTemplateId, StringComparison.OrdinalIgnoreCase) ||
			string.Equals(TemplateIDHelper.GenerateSlug(u["TemplateID"]?.ToString() ?? ""), slug, StringComparison.OrdinalIgnoreCase) ||
			string.Equals(u["ModelPath"]?.ToString(), fileName, StringComparison.OrdinalIgnoreCase) ||
			string.Equals(TemplateIDHelper.GenerateSlug(u["ModelPath"]?.ToString() ?? ""), slug, StringComparison.OrdinalIgnoreCase))
			|| ModelExistsInOtherCategories(templatesObj, fileName, slug);
		if (!exists)
		{
			unitsArr.Add(new JsonObject
			{
				["TemplateID"] = unitTemplateId,
				["Name"] = slug,
				["Description"] = "",
				["ModelPath"] = fileName,
				["VisualMode"] = fileName.EndsWith(".rmesh", StringComparison.OrdinalIgnoreCase) ? "3D Mesh (.rmesh)" : "GroundPlane",
				["Scale"] = 1.0f,
				["PathingType"] = 9,
				["DespillPlayerColor"] = false,
				["NormalizeLuminance"] = true
			});
		}
	}

	private static void EnsureBuildingTemplate(JsonObject templatesObj, string fileName, string slug)
	{
		string buildingTemplateId = TemplateIDHelper.NormalizeTemplateID("building", slug);
		if (!templatesObj.ContainsKey("Buildings") || templatesObj["Buildings"] is not JsonArray)
		{
			templatesObj["Buildings"] = new JsonArray();
		}
		var buildingsArr = templatesObj["Buildings"]!.AsArray();
		bool exists = buildingsArr.OfType<JsonObject>().Any(b =>
			string.Equals(b["TemplateID"]?.ToString(), buildingTemplateId, StringComparison.OrdinalIgnoreCase) ||
			string.Equals(TemplateIDHelper.GenerateSlug(b["TemplateID"]?.ToString() ?? ""), slug, StringComparison.OrdinalIgnoreCase) ||
			string.Equals(b["ModelPath"]?.ToString(), fileName, StringComparison.OrdinalIgnoreCase) ||
			string.Equals(TemplateIDHelper.GenerateSlug(b["ModelPath"]?.ToString() ?? ""), slug, StringComparison.OrdinalIgnoreCase))
			|| ModelExistsInOtherCategories(templatesObj, fileName, slug);
		if (!exists)
		{
			buildingsArr.Add(new JsonObject
			{
				["TemplateID"] = buildingTemplateId,
				["Name"] = slug,
				["Description"] = "",
				["ModelPath"] = fileName,
				["VisualMode"] = fileName.EndsWith(".rmesh", StringComparison.OrdinalIgnoreCase) ? "3D Mesh (.rmesh)" : "GroundPlane",
				["Scale"] = 1.5f,
				["PathingType"] = 32,
				["DespillPlayerColor"] = false,
				["NormalizeLuminance"] = true
			});
		}
	}

	private static void EnsurePropTemplate(JsonObject templatesObj, string fileName, string slug)
	{
		string propTemplateId = TemplateIDHelper.NormalizeTemplateID("prop", slug);
		if (!templatesObj.ContainsKey("Props") || templatesObj["Props"] is not JsonArray)
		{
			templatesObj["Props"] = new JsonArray();
		}
		var propsArr = templatesObj["Props"]!.AsArray();
		bool exists = propsArr.OfType<JsonObject>().Any(p =>
			string.Equals(p["TemplateID"]?.ToString(), propTemplateId, StringComparison.OrdinalIgnoreCase) ||
			string.Equals(TemplateIDHelper.GenerateSlug(p["TemplateID"]?.ToString() ?? ""), slug, StringComparison.OrdinalIgnoreCase) ||
			string.Equals(p["ModelPath"]?.ToString(), fileName, StringComparison.OrdinalIgnoreCase) ||
			string.Equals(TemplateIDHelper.GenerateSlug(p["ModelPath"]?.ToString() ?? ""), slug, StringComparison.OrdinalIgnoreCase))
			|| ModelExistsInOtherCategories(templatesObj, fileName, slug);
		if (!exists)
		{
			propsArr.Add(new JsonObject
			{
				["TemplateID"] = propTemplateId,
				["Name"] = slug,
				["Description"] = "",
				["ModelPath"] = fileName,
				["VisualMode"] = fileName.EndsWith(".rmesh", StringComparison.OrdinalIgnoreCase) ? "3D Mesh (.rmesh)" : "GroundPlane",
				["Scale"] = 1.25f,
				["PathingType"] = 255,
				["DespillPlayerColor"] = false,
				["NormalizeLuminance"] = true,
				["IgnorePlayerColor"] = true
			});
		}
	}

	private static void EnsureResourceTemplate(JsonObject templatesObj, string fileName, string slug)
	{
		string resourceTemplateId = TemplateIDHelper.NormalizeTemplateID("resource", slug);
		if (!templatesObj.ContainsKey("Resources") || templatesObj["Resources"] is not JsonArray)
		{
			templatesObj["Resources"] = new JsonArray();
		}
		var resourcesArr = templatesObj["Resources"]!.AsArray();
		bool exists = resourcesArr.OfType<JsonObject>().Any(r =>
			string.Equals(r["TemplateID"]?.ToString(), resourceTemplateId, StringComparison.OrdinalIgnoreCase) ||
			string.Equals(TemplateIDHelper.GenerateSlug(r["TemplateID"]?.ToString() ?? ""), slug, StringComparison.OrdinalIgnoreCase) ||
			string.Equals(r["ModelPath"]?.ToString(), fileName, StringComparison.OrdinalIgnoreCase) ||
			string.Equals(TemplateIDHelper.GenerateSlug(r["ModelPath"]?.ToString() ?? ""), slug, StringComparison.OrdinalIgnoreCase))
			|| ModelExistsInOtherCategories(templatesObj, fileName, slug);
		if (!exists)
		{
			resourcesArr.Add(new JsonObject
			{
				["TemplateID"] = resourceTemplateId,
				["Name"] = slug,
				["Description"] = "",
				["ModelPath"] = fileName,
				["VisualMode"] = fileName.EndsWith(".rmesh", StringComparison.OrdinalIgnoreCase) ? "3D Mesh (.rmesh)" : "GroundPlane",
				["Scale"] = 2.75f,
				["PathingType"] = 255,
				["DespillPlayerColor"] = false,
				["NormalizeLuminance"] = true,
				["IgnorePlayerColor"] = true
			});
		}
	}

	private static void EnsureDecalTemplate(JsonObject metadataRoot, string fileName, string slug, string hash)
	{
		string decalTemplateId = TemplateIDHelper.NormalizeTemplateID("decal", slug);
		if (!metadataRoot.ContainsKey("decals") || metadataRoot["decals"] is not JsonObject)
		{
			metadataRoot["decals"] = new JsonObject();
		}
		var decalsObj = metadataRoot["decals"]!.AsObject();
		bool exists = decalsObj.Any(kvp =>
			string.Equals(kvp.Key, decalTemplateId, StringComparison.OrdinalIgnoreCase) ||
			string.Equals(kvp.Key, slug, StringComparison.OrdinalIgnoreCase) ||
			string.Equals(kvp.Key, fileName, StringComparison.OrdinalIgnoreCase) ||
			string.Equals(TemplateIDHelper.GenerateSlug(kvp.Key), slug, StringComparison.OrdinalIgnoreCase) ||
			(kvp.Value is JsonObject dObj && (
				string.Equals(dObj["TexturePath"]?.ToString(), fileName, StringComparison.OrdinalIgnoreCase) ||
				string.Equals(dObj["TexturePath"]?.ToString(), slug, StringComparison.OrdinalIgnoreCase) ||
				string.Equals(TemplateIDHelper.GenerateSlug(dObj["TexturePath"]?.ToString() ?? ""), slug, StringComparison.OrdinalIgnoreCase)
			)));
		if (!exists)
		{
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

public class MapUpgradeService
{
	public static void MergeCategoryInto(MapManifestAssets targetAssets, string category, JsonObject sourceObject)
	{
		var categoryTarget = targetAssets.GetCategory(category);
		if (categoryTarget == null)
		{
			categoryTarget = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			targetAssets.SetCategory(category, categoryTarget);
		}

		foreach (var pair in sourceObject)
		{
			if (pair.Value is JsonObject sourceObj)
			{
				string hash = sourceObj["hash"]?.ToString() ?? sourceObj["blake3"]?.ToString() ?? string.Empty;
				if (!string.IsNullOrEmpty(hash))
				{
					categoryTarget[pair.Key] = hash;
				}
			}
			else if (pair.Value != null)
			{
				categoryTarget[pair.Key] = pair.Value.ToString();
			}
		}
	}
	private readonly WorldAccessor _worldAccessor;
	private readonly List<IMapMigration> _migrations = new();

	public static MapUpgradeService Instance => ServiceLocator.Get<MapUpgradeService>();

	public MapUpgradeService(WorldAccessor worldAccessor)
	{
		_worldAccessor = worldAccessor;
		RegisterMigrations();
	}

	private void RegisterMigrations()
	{
		_migrations.Add(new Migration_0_0_1_InitialCanonicalFormat());
		_migrations.Add(new Migration_0_0_2_NormalizeModelProperties());
		_migrations.Add(new Migration_0_0_3_WaterProfilesAndShaders());
		_migrations.Add(new Migration_0_0_4_TemplateIDPrefixes());
	}

	public string GetMapBuildNumber(string mapDirectory)
	{
		if (string.IsNullOrEmpty(mapDirectory))
		{
			return "v0.0.0";
		}

		string metadataPath = File.Exists(mapDirectory) && Path.GetFileName(mapDirectory).Equals("metadata.json", StringComparison.OrdinalIgnoreCase)
			? mapDirectory
			: Path.Combine(mapDirectory, "metadata.json");

		if (!File.Exists(metadataPath))
		{
			return "v0.0.0";
		}

		try
		{
			string json = File.ReadAllText(metadataPath);
			using var doc = JsonDocument.Parse(json);
			if (doc.RootElement.TryGetProperty("GameBuildNumber", out var prop) && prop.ValueKind == JsonValueKind.String)
			{
				string? val = prop.GetString();
				if (!string.IsNullOrWhiteSpace(val))
				{
					return val.Trim();
				}
			}
		}
		catch
		{
		}

		return "v0.0.0";
	}

	public bool NeedsUpgrade(string mapDirectory, out string currentVersion, out string targetVersion)
	{
		currentVersion = GetMapBuildNumber(mapDirectory);
		targetVersion = RealmVersion.GameBuildNumber;
		return !string.Equals(currentVersion, targetVersion, StringComparison.OrdinalIgnoreCase);
	}

	public bool IsMapNewerThanGame(string mapVersionOrDirectory, string? gameVersion = null)
	{
		string mapBuildNumber = mapVersionOrDirectory;
		if (!string.IsNullOrEmpty(mapVersionOrDirectory) && (Directory.Exists(mapVersionOrDirectory) || File.Exists(mapVersionOrDirectory)))
		{
			mapBuildNumber = GetMapBuildNumber(mapVersionOrDirectory);
		}

		string targetGameVersion = !string.IsNullOrWhiteSpace(gameVersion) ? gameVersion : RealmVersion.GameBuildNumber;
		var parsedMapVersion = ParseBuildVersion(mapBuildNumber);
		var parsedGameVersion = ParseBuildVersion(targetGameVersion);

		return parsedMapVersion.CompareTo(parsedGameVersion) > 0;
	}

	public static Version ParseBuildVersion(string? versionString)
	{
		if (string.IsNullOrWhiteSpace(versionString)) return new Version(0, 0, 0, 0);

		string cleaned = CleanVersionString(versionString);

		if (Version.TryParse(cleaned, out var parsedVersion))
		{
			return new Version(
				Math.Max(0, parsedVersion.Major),
				Math.Max(0, parsedVersion.Minor),
				Math.Max(0, parsedVersion.Build),
				parsedVersion.Revision >= 0 ? parsedVersion.Revision : 0
			);
		}

		return ParseVersionFallback(cleaned);
	}

	private static string CleanVersionString(string versionString)
	{
		string cleaned = versionString.Trim().TrimStart('v', 'V').Trim();
		int separatorIndex = cleaned.IndexOfAny(new[] { '-', '_', '+', ' ', '(' });
		if (separatorIndex >= 0)
		{
			cleaned = cleaned.Substring(0, separatorIndex).Trim();
		}
		return cleaned;
	}

	private static Version ParseVersionFallback(string cleaned)
	{
		var parts = cleaned.Split('.');
		int[] parsed = new int[4];

		for (int i = 0; i < parts.Length && i < 4; i++)
		{
			if (int.TryParse(parts[i], out int val))
			{
				parsed[i] = val;
			}
		}

		if (parts.Length > 0 && int.TryParse(parts[0], out _))
		{
			return new Version(parsed[0], parsed[1], parsed[2], parsed[3]);
		}
		
		return new Version(0, 0, 0, 0);
	}

	public List<IMapMigration> GetPendingMigrations(string currentVersion, string targetVersion)
	{
		var pending = new List<IMapMigration>();
		string current = string.IsNullOrWhiteSpace(currentVersion) ? "v0.0.0" : currentVersion.Trim();
		string target = targetVersion.Trim();

		while (!string.Equals(current, target, StringComparison.OrdinalIgnoreCase))
		{
			var nextMigration = _migrations.FirstOrDefault(m => string.Equals(m.FromVersion, current, StringComparison.OrdinalIgnoreCase));
			if (nextMigration == null)
			{
				break;
			}
			pending.Add(nextMigration);
			current = nextMigration.ToVersion;
		}

		return pending;
	}

	public UpgradeResult UpgradeMap(string mapDirectory, IProgress<MigrationProgressUpdate>? progress = null)
	{
		string initialVersion = GetMapBuildNumber(mapDirectory);
		string targetVersion = RealmVersion.GameBuildNumber;

		if (IsMapNewerThanGame(initialVersion, targetVersion))
		{
			return new UpgradeResult
			{
				Success = false,
				ErrorMessage = $"Map build {initialVersion} is newer than current game build {targetVersion}. Downgrading is not supported.",
				InitialVersion = initialVersion,
				FinalVersion = initialVersion
			};
		}

		var pendingMigrations = GetPendingMigrations(initialVersion, targetVersion);
		if (pendingMigrations.Count == 0)
		{
			return new UpgradeResult
			{
				Success = true,
				InitialVersion = initialVersion,
				FinalVersion = initialVersion,
				StepResults = new List<MigrationResult>()
			};
		}

		CreateMapBackup(mapDirectory, initialVersion);

		var result = new UpgradeResult
		{
			InitialVersion = initialVersion,
			FinalVersion = initialVersion
		};

		for (int i = 0; i < pendingMigrations.Count; i++)
		{
			var migration = pendingMigrations[i];
			progress?.Report(new MigrationProgressUpdate(
				migration.Description,
				i + 1,
				pendingMigrations.Count,
				$"Applying migration {migration.FromVersion} -> {migration.ToVersion}..."
			));

			var stepResult = migration.Up(mapDirectory, progress);
			result.StepResults.Add(stepResult);

			if (!stepResult.Success)
			{
				result.Success = false;
				result.ErrorMessage = stepResult.ErrorMessage ?? $"Failed at migration {migration.FromVersion} -> {migration.ToVersion}";
				return result;
			}

			result.FinalVersion = migration.ToVersion;
		}

		result.Success = string.Equals(result.FinalVersion, targetVersion, StringComparison.OrdinalIgnoreCase);
		return result;
	}

	public Task<UpgradeResult> UpgradeMapAsync(string mapDirectory, IProgress<MigrationProgressUpdate>? progress = null)
	{
		return Task.Run(() => UpgradeMap(mapDirectory, progress));
	}

	private static void CreateMapBackup(string mapDirectory, string currentVersion)
	{
		try
		{
			string loadedFolderName = Path.GetFileName(mapDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
			if (string.IsNullOrEmpty(loadedFolderName))
			{
				loadedFolderName = "map";
			}

			string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
			string userDataDir;
			try
			{
				userDataDir = OS.GetUserDataDir();
				if (string.IsNullOrEmpty(userDataDir))
				{
					userDataDir = ProjectSettings.GlobalizePath("user://");
				}
			}
			catch
			{
				string appData = System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData);
				userDataDir = Path.Combine(appData, "Godot", "app_userdata", "Realm");
			}

			string fullUserDataDir = Path.GetFullPath(userDataDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
			string upgradeBackupsRoot = Path.GetFullPath(Path.Combine(fullUserDataDir, "map_upgrades")).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
			string targetBackupDir = Path.GetFullPath(Path.Combine(upgradeBackupsRoot, $"{loadedFolderName}_{timestamp}")).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

			CopyDirectoryContentsSafe(mapDirectory, targetBackupDir, upgradeBackupsRoot);
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[MapUpgradeService] Warning: Failed to create map backup: {ex.Message}");
		}
	}

	private static void CopyDirectoryContentsSafe(string sourceDir, string targetDir, string? backupsRoot = null)
	{
		var source = new DirectoryInfo(sourceDir);
		if (!source.Exists) return;

		string normalizedTarget = Path.GetFullPath(targetDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		string? normalizedBackupsRoot = !string.IsNullOrEmpty(backupsRoot) ? Path.GetFullPath(backupsRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) : null;

		Directory.CreateDirectory(targetDir);
		CopyFilesSafe(source, targetDir);
		CopySubDirectoriesSafe(source, targetDir, normalizedTarget, normalizedBackupsRoot, backupsRoot);
	}

	private static void CopyFilesSafe(DirectoryInfo source, string targetDir)
	{
		foreach (var file in source.GetFiles())
		{
			if (file.Extension.Equals(".tmp", StringComparison.OrdinalIgnoreCase)) continue;
			file.CopyTo(Path.Combine(targetDir, file.Name), true);
		}
	}

	private static void CopySubDirectoriesSafe(DirectoryInfo source, string targetDir, string normalizedTarget, string? normalizedBackupsRoot, string? backupsRoot)
	{
		var excludedFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
		{
			".git", "bin", "obj", ".godot", ".vs", ".vscode", ".idea", "map_upgrades", "map_backups", "backups", ".backups", ".dotnet", ".wasi", ".sidecarcache", ".cache"
		};

		foreach (var dir in source.GetDirectories())
		{
			if (excludedFolders.Contains(dir.Name) || dir.Name.StartsWith("backup_", StringComparison.OrdinalIgnoreCase)) continue;

			string subDirFull = Path.GetFullPath(dir.FullName).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

			if (ShouldIgnoreDirectory(subDirFull, normalizedTarget, normalizedBackupsRoot)) continue;

			CopyDirectoryContentsSafe(dir.FullName, Path.Combine(targetDir, dir.Name), backupsRoot);
		}
	}

	private static bool ShouldIgnoreDirectory(string subDirFull, string normalizedTarget, string? normalizedBackupsRoot)
	{
		if (subDirFull.IndexOf("map_backups", StringComparison.OrdinalIgnoreCase) >= 0 ||
			subDirFull.IndexOf("map_upgrades", StringComparison.OrdinalIgnoreCase) >= 0 ||
			subDirFull.IndexOf(".backups", StringComparison.OrdinalIgnoreCase) >= 0)
		{
			return true;
		}

		if (!string.IsNullOrEmpty(normalizedBackupsRoot) &&
			(string.Equals(subDirFull, normalizedBackupsRoot, StringComparison.OrdinalIgnoreCase) ||
			 subDirFull.StartsWith(normalizedBackupsRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
		{
			return true;
		}

		if (string.Equals(subDirFull, normalizedTarget, StringComparison.OrdinalIgnoreCase) ||
			subDirFull.StartsWith(normalizedTarget + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
			normalizedTarget.StartsWith(subDirFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}

		return false;
	}
}
