using Realm.Shared.Audio;
using Realm.Shared.Distribution;
using Realm.Shared.ModelOptimization;
using Realm.Shared.Textures;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace Realm.Client.Services;

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
		var modelFiles = allFiles.Where(f => ModelExtensions.Contains(Path.GetExtension((string?)f))).ToList();

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