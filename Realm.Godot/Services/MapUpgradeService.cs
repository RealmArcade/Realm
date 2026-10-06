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
			string manifestPath = Path.Combine(mapDirectory, "manifest.json");

			JsonObject? metadataRoot = null;
			if (File.Exists(metadataPath))
			{
				string text = File.ReadAllText(metadataPath);
				metadataRoot = JsonNode.Parse(text)?.AsObject();
			}

			metadataRoot ??= new JsonObject();

			const int totalSteps = 6;

			progress?.Report(new MigrationProgressUpdate(Description, 1, totalSteps, "Normalizing metadata properties..."));

			if (metadataRoot.TryGetPropertyValue("MapProperties", out var propsNode) && propsNode is JsonObject mapProperties)
			{
				if (!mapProperties.ContainsKey("MapName"))
				{
					string nameVal = mapProperties["Name"]?.ToString() ?? mapProperties["Title"]?.ToString() ?? "";
					if (!string.IsNullOrEmpty(nameVal))
					{
						mapProperties["MapName"] = nameVal;
					}
				}
				mapProperties.Remove("Name");
				mapProperties.Remove("Title");

				if (!mapProperties.ContainsKey("MapDescription"))
				{
					string descVal = mapProperties["Description"]?.ToString() ?? "";
					if (!string.IsNullOrEmpty(descVal))
					{
						mapProperties["MapDescription"] = descVal;
					}
				}
				mapProperties.Remove("Description");
			}

			var legacyEntityArrays = new (string LegacyKey, string CanonicalKey)[]
			{
				("Units", "CustomUnits"),
				("Buildings", "CustomBuildings"),
				("Resources", "CustomResources"),
				("Props", "CustomProps"),
				("Abilities", "CustomAbilities"),
				("Weapons", "CustomWeapons"),
				("Upgrades", "CustomUpgrades"),
				("Items", "CustomItems"),
				("Attachments", "CustomAttachments"),
				("Vfx", "CustomVfx")
			};

			foreach (var (legacyKey, canonicalKey) in legacyEntityArrays)
			{
				if (metadataRoot.TryGetPropertyValue(legacyKey, out var legacyArrayNode) && legacyArrayNode is JsonArray legacyArray)
				{
					if (!metadataRoot.ContainsKey(canonicalKey) || metadataRoot[canonicalKey] is not JsonArray)
					{
						metadataRoot[canonicalKey] = new JsonArray();
					}
					var canonicalArray = metadataRoot[canonicalKey]!.AsArray();
					foreach (var item in legacyArray)
					{
						if (item != null)
						{
							canonicalArray.Add(item.DeepClone());
						}
					}
					metadataRoot.Remove(legacyKey);
				}
			}

			progress?.Report(new MigrationProgressUpdate(Description, 2, totalSteps, "Migrating asset dictionaries to manifest..."));

			var unionedAssets = MapAssetHelper.LoadAssets(mapDirectory);

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

			progress?.Report(new MigrationProgressUpdate(Description, 3, totalSteps, "Converting 3D model assets to .rmesh..."));
			ConvertModelAssets(mapDirectory, metadataRoot, unionedAssets, progress, Description, 3, totalSteps);

			progress?.Report(new MigrationProgressUpdate(Description, 4, totalSteps, "Converting audio assets to .raud..."));
			ConvertAudioAssets(mapDirectory, metadataRoot, unionedAssets, progress, Description, 4, totalSteps);

			progress?.Report(new MigrationProgressUpdate(Description, 5, totalSteps, "Converting textures to .rtex..."));
			ConvertTextureAssets(mapDirectory, metadataRoot, unionedAssets, progress, Description, 5, totalSteps);

			progress?.Report(new MigrationProgressUpdate(Description, 6, totalSteps, "Normalizing swatch indices and manifest..."));

			MapWorkspaceService.NormalizeTextureEntries(unionedAssets, mapDirectory);
			MapAssetHelper.SaveAssetsToManifest(mapDirectory, unionedAssets, removeFromMetadata: true);

			metadataRoot.Remove("Assets");
			if (metadataRoot["MapProperties"] is JsonObject mapPropsObj)
			{
				mapPropsObj.Remove("Assets");
			}

			metadataRoot["GameBuildNumber"] = ToVersion;

			SaveLoadService.CleanMetadataJsonSchema(metadataRoot);

			progress?.Report(new MigrationProgressUpdate(Description, 6, totalSteps, "Saving migrated metadata.json..."));
			MapJsonFormatter.SaveFormattedJson(metadataPath, metadataRoot);

			return new MigrationResult
			{
				Success = true,
				FromVersion = FromVersion,
				ToVersion = ToVersion
			};
		}
		catch (Exception ex)
		{
			return new MigrationResult
			{
				Success = false,
				ErrorMessage = $"Migration 0.0.1 failed: {ex.Message}",
				FromVersion = FromVersion,
				ToVersion = ToVersion
			};
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
		string assetsDir = Path.Combine(mapDirectory, "Assets");
		if (Directory.Exists(assetsDir))
		{
			string[] allFiles = Directory.GetFiles(assetsDir, "*.*", SearchOption.AllDirectories);
			var modelFiles = allFiles
				.Where(f => ModelExtensions.Contains(Path.GetExtension(f)))
				.ToList();

			for (int i = 0; i < modelFiles.Count; i++)
			{
				string modelFile = modelFiles[i];
				string normalized = modelFile.Replace("\\", "/");
				if (normalized.Contains("/bin/") || normalized.Contains("/obj/") || normalized.Contains("/.git/") || normalized.Contains("/.godot/") || normalized.Contains("/vscode_embedded/") || normalized.Contains("/wasi_sdk_embedded/"))
				{
					continue;
				}

				string fileName = Path.GetFileName(modelFile);
				progress?.Report(new MigrationProgressUpdate(description, stepIndex, totalSteps, $"Converting model {fileName} -> .rmesh ({i + 1}/{modelFiles.Count})..."));

				string assetType = "Prop";
				string lowerPath = normalized.ToLowerInvariant();
				if (lowerPath.Contains("/units/") || lowerPath.Contains("/characters/"))
				{
					assetType = "Character";
				}
				else if (lowerPath.Contains("/buildings/") || lowerPath.Contains("/structures/"))
				{
					assetType = "Building";
				}
				else if (lowerPath.Contains("/items/") || lowerPath.Contains("/attachments/") || lowerPath.Contains("/projectiles/") || lowerPath.Contains("/weapons/"))
				{
					assetType = "Item";
				}

				string targetRmesh = Path.ChangeExtension(modelFile, ".rmesh");
				var result = ModelConverter.ConvertToRmesh(modelFile, targetRmesh, assetType, force: true);
				if (result.Success && File.Exists(targetRmesh))
				{
					try
					{
						var attrs = File.GetAttributes(modelFile);
						if ((attrs & FileAttributes.ReadOnly) != 0)
						{
							File.SetAttributes(modelFile, attrs & ~FileAttributes.ReadOnly);
						}
						File.Delete(modelFile);
					}
					catch { }
				}
			}
		}

		var entityArrayNames = new[] { "CustomUnits", "CustomBuildings", "CustomResources", "CustomProps", "CustomItems", "CustomAttachments", "CustomWeapons" };
		foreach (var arrayName in entityArrayNames)
		{
			if (metadataRoot.TryGetPropertyValue(arrayName, out var arrNode) && arrNode is JsonArray arr)
			{
				foreach (var item in arr)
				{
					if (item is JsonObject itemObj)
					{
						if (itemObj.TryGetPropertyValue("ModelPath", out var mpVal) && mpVal != null)
						{
							string mp = mpVal.ToString();
							if (ModelExtensions.Contains(Path.GetExtension(mp)))
							{
								itemObj["ModelPath"] = Path.ChangeExtension(mp, ".rmesh");
							}
						}
						if (itemObj.TryGetPropertyValue("PortraitModelPath", out var pmpVal) && pmpVal != null)
						{
							string pmp = pmpVal.ToString();
							if (ModelExtensions.Contains(Path.GetExtension(pmp)))
							{
								itemObj["PortraitModelPath"] = Path.ChangeExtension(pmp, ".rmesh");
							}
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
				}
			}
		}

		var modelDictNames = new[]
		{
			"ModelOffsets", "ModelScales", "ModelCollisionCircleRatios", "ModelObstacleRadii",
			"ModelBrightness", "ModelColorTint", "ModelDespillPlayerColor", "ModelNormalizeLuminance",
			"ModelIgnorePlayerColor", "ModelSpawnShaders", "ModelDeathShaders"
		};

		foreach (var dictName in modelDictNames)
		{
			if (metadataRoot.TryGetPropertyValue(dictName, out var dictNode) && dictNode is JsonObject dictObj)
			{
				var keysToMigrate = new List<(string OldKey, string NewKey, JsonNode? Value)>();
				foreach (var prop in dictObj)
				{
					string ext = Path.GetExtension(prop.Key);
					if (ModelExtensions.Contains(ext))
					{
						string newKey = Path.ChangeExtension(prop.Key, ".rmesh");
						keysToMigrate.Add((prop.Key, newKey, prop.Value?.DeepClone()));
					}
				}
				foreach (var (oldKey, newKey, val) in keysToMigrate)
				{
					dictObj.Remove(oldKey);
					dictObj[newKey] = val;
				}
			}
		}

		if (metadataRoot.TryGetPropertyValue("ModelNormalModes", out var nrmNode) && nrmNode is JsonObject nrmObj)
		{
			if (!metadataRoot.ContainsKey("ModelDespillPlayerColor") || metadataRoot["ModelDespillPlayerColor"] is not JsonObject)
			{
				metadataRoot["ModelDespillPlayerColor"] = new JsonObject();
			}
			var despillObj = metadataRoot["ModelDespillPlayerColor"]!.AsObject();
			foreach (var prop in nrmObj)
			{
				string newKey = Path.ChangeExtension(prop.Key, ".rmesh");
				despillObj[newKey] = true;
			}
			metadataRoot.Remove("ModelNormalModes");
		}

		foreach (var catName in new[] { "Character", "Building", "Prop", "Item" })
		{
			var dict = unionedAssets.GetCategory(catName);
			if (dict != null)
			{
				var keysToMigrate = new List<(string OldKey, string NewKey, string Value)>();
				foreach (var item in dict)
				{
					string ext = Path.GetExtension(item.Key);
					if (ModelExtensions.Contains(ext))
					{
						string newKey = Path.ChangeExtension(item.Key, ".rmesh");
						keysToMigrate.Add((item.Key, newKey, item.Value));
					}
				}
				foreach (var (oldKey, newKey, val) in keysToMigrate)
				{
					dict.Remove(oldKey);
					dict[newKey] = val;
				}
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
		string audioDir = Path.Combine(mapDirectory, "Assets", "audio");
		if (Directory.Exists(audioDir))
		{
			string[] allFiles = Directory.GetFiles(audioDir, "*.*", SearchOption.AllDirectories);
			var audioFiles = allFiles
				.Where(f => AudioExtensions.Contains(Path.GetExtension(f)))
				.ToList();

			for (int i = 0; i < audioFiles.Count; i++)
			{
				string audioFile = audioFiles[i];
				string fileName = Path.GetFileName(audioFile);
				progress?.Report(new MigrationProgressUpdate(description, stepIndex, totalSteps, $"Converting audio {fileName} -> .raud ({i + 1}/{audioFiles.Count})..."));

				string targetRaud = Path.ChangeExtension(audioFile, ".raud");
				var result = AudioConverter.ConvertToRaud(audioFile, targetRaud);
				if (result.Success && File.Exists(targetRaud))
				{
					try
					{
						var attrs = File.GetAttributes(audioFile);
						if ((attrs & FileAttributes.ReadOnly) != 0)
						{
							File.SetAttributes(audioFile, attrs & ~FileAttributes.ReadOnly);
						}
						File.Delete(audioFile);
					}
					catch { }
				}
			}
		}

		foreach (var catName in new[] { "SoundEffect", "Music" })
		{
			var dict = unionedAssets.GetCategory(catName);
			if (dict != null)
			{
				var keysToMigrate = new List<(string OldKey, string NewKey, string Value)>();
				foreach (var item in dict)
				{
					string ext = Path.GetExtension(item.Key);
					if (AudioExtensions.Contains(ext))
					{
						string newKey = Path.ChangeExtension(item.Key, ".raud");
						keysToMigrate.Add((item.Key, newKey, item.Value));
					}
				}
				foreach (var (oldKey, newKey, val) in keysToMigrate)
				{
					dict.Remove(oldKey);
					dict[newKey] = val;
				}
			}
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
		if (Directory.Exists(assetsDir))
		{
			string[] allFiles = Directory.GetFiles(assetsDir, "*.*", SearchOption.AllDirectories);
			var textureFiles = allFiles
				.Where(f => TextureExtensions.Contains(Path.GetExtension(f)))
				.ToList();

			for (int i = 0; i < textureFiles.Count; i++)
			{
				string texFile = textureFiles[i];
				string normalized = texFile.Replace("\\", "/");
				if (normalized.Contains("/bin/") || normalized.Contains("/obj/") || normalized.Contains("/.git/") || normalized.Contains("/.godot/") || normalized.Contains("/vscode_embedded/") || normalized.Contains("/wasi_sdk_embedded/"))
				{
					continue;
				}

				string fileName = Path.GetFileName(texFile);
				progress?.Report(new MigrationProgressUpdate(description, stepIndex, totalSteps, $"Converting texture {fileName} -> .rtex ({i + 1}/{textureFiles.Count})..."));

				string targetRtex = Path.ChangeExtension(texFile, ".rtex");
				string cleanName = Path.GetFileNameWithoutExtension(texFile);
				string lowerNorm = normalized.ToLowerInvariant();

				string assetType = "texture";
				int columns = 1;
				int rows = 1;

				if (lowerNorm.Contains("/decals/"))
				{
					assetType = "decal";
				}
				else if (lowerNorm.Contains("/icons/"))
				{
					assetType = "icon";
				}
				else if (lowerNorm.Contains("/skyboxes/"))
				{
					assetType = "skybox";
				}
				else if (lowerNorm.Contains("/ribbons/"))
				{
					assetType = "ribbon";
				}
				else if (lowerNorm.Contains("/noise/"))
				{
					assetType = "noise";
				}
				else if (lowerNorm.Contains("/vfx/"))
				{
					assetType = "vfx";
					columns = 4;
					rows = 4;
				}

				var result = TextureConverter.ConvertTextureFile(texFile, targetRtex, assetType, columns, rows);
				if (result.Success && File.Exists(targetRtex))
				{
					try
					{
						var attrs = File.GetAttributes(texFile);
						if ((attrs & FileAttributes.ReadOnly) != 0)
						{
							File.SetAttributes(texFile, attrs & ~FileAttributes.ReadOnly);
						}
						File.Delete(texFile);
					}
					catch { }
				}
			}
		}
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
			JsonObject? metadataRoot = null;
			if (File.Exists(metadataPath))
			{
				string text = File.ReadAllText(metadataPath);
				metadataRoot = JsonNode.Parse(text)?.AsObject();
			}

			metadataRoot ??= new JsonObject();

			const int totalSteps = 2;
			progress?.Report(new MigrationProgressUpdate(Description, 1, totalSteps, "Normalizing model properties into Models dictionary..."));

			if (!metadataRoot.ContainsKey("Models") || metadataRoot["Models"] is not JsonObject)
			{
				metadataRoot["Models"] = new JsonObject();
			}
			var modelsObj = metadataRoot["Models"]!.AsObject();

			var dictMappings = new (string TopLevelKey, string ModelPropKey)[]
			{
				("ModelBrightness", "Brightness"),
				("ModelCollisionCircleRatios", "CollisionCircleRatios"),
				("ModelColorTint", "ColorTint"),
				("ModelDespillPlayerColor", "DespillPlayerColor"),
				("ModelIgnorePlayerColor", "IgnorePlayerColor"),
				("ModelNormalizeLuminance", "NormalizeLuminance"),
				("ModelObstacleRadii", "ObstacleRadii"),
				("ModelOffsets", "Offsets"),
				("ModelScales", "Scales"),
				("ModelSpawnShaders", "SpawnShaders"),
				("ModelDeathShaders", "DeathShaders")
			};

			foreach (var (topKey, propKey) in dictMappings)
			{
				if (metadataRoot.TryGetPropertyValue(topKey, out var dictNode) && dictNode is JsonObject dictObj)
				{
					foreach (var kvp in dictObj)
					{
						string modelKey = kvp.Key;
						if (string.IsNullOrWhiteSpace(modelKey)) continue;

						if (!modelsObj.ContainsKey(modelKey) || modelsObj[modelKey] is not JsonObject)
						{
							modelsObj[modelKey] = new JsonObject();
						}
						var modelEntry = modelsObj[modelKey]!.AsObject();
						if (kvp.Value != null)
						{
							modelEntry[propKey] = kvp.Value.DeepClone();
						}
					}
					metadataRoot.Remove(topKey);
				}
			}

			if (metadataRoot.TryGetPropertyValue("ModelNormalModes", out var nrmNode) && nrmNode is JsonObject nrmObj)
			{
				foreach (var kvp in nrmObj)
				{
					string modelKey = kvp.Key;
					if (string.IsNullOrWhiteSpace(modelKey)) continue;

					if (!modelsObj.ContainsKey(modelKey) || modelsObj[modelKey] is not JsonObject)
					{
						modelsObj[modelKey] = new JsonObject();
					}
					var modelEntry = modelsObj[modelKey]!.AsObject();
					modelEntry["DespillPlayerColor"] = true;
				}
				metadataRoot.Remove("ModelNormalModes");
			}

			metadataRoot["GameBuildNumber"] = ToVersion;
			SaveLoadService.CleanMetadataJsonSchema(metadataRoot);

			progress?.Report(new MigrationProgressUpdate(Description, 2, totalSteps, "Saving migrated metadata.json..."));
			MapJsonFormatter.SaveFormattedJson(metadataPath, metadataRoot);

			return new MigrationResult
			{
				Success = true,
				FromVersion = FromVersion,
				ToVersion = ToVersion
			};
		}
		catch (Exception ex)
		{
			return new MigrationResult
			{
				Success = false,
				ErrorMessage = $"Migration 0.0.2 failed: {ex.Message}",
				FromVersion = FromVersion,
				ToVersion = ToVersion
			};
		}
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
			JsonObject? metadataRoot = null;
			if (File.Exists(metadataPath))
			{
				string text = File.ReadAllText(metadataPath);
				metadataRoot = JsonNode.Parse(text)?.AsObject();
			}

			metadataRoot ??= new JsonObject();

			const int totalSteps = 3;

			progress?.Report(new MigrationProgressUpdate(Description, 1, totalSteps, "Normalizing custom water profiles..."));

			if (metadataRoot.TryGetPropertyValue("CustomWaterProfiles", out var waterProfilesNode) && waterProfilesNode is JsonArray waterProfilesArray)
			{
				foreach (var item in waterProfilesArray)
				{
					if (item is JsonObject profileObject)
					{
						profileObject.Remove("WaterType");

						if (!profileObject.ContainsKey("DetailTileMode") || profileObject["DetailTileMode"] == null)
						{
							profileObject["DetailTileMode"] = "Stochastic";
						}
						if (!profileObject.ContainsKey("DetailStochasticTileSize") || profileObject["DetailStochasticTileSize"] == null)
						{
							profileObject["DetailStochasticTileSize"] = 1.0f;
						}
						if (!profileObject.ContainsKey("DetailCrossFade") || profileObject["DetailCrossFade"] == null)
						{
							profileObject["DetailCrossFade"] = 0.0f;
						}
						if (!profileObject.ContainsKey("DetailTexturePath") || profileObject["DetailTexturePath"] == null)
						{
							profileObject["DetailTexturePath"] = string.Empty;
						}
						if (!profileObject.ContainsKey("UseDetailTexture") || profileObject["UseDetailTexture"] == null)
						{
							profileObject["UseDetailTexture"] = false;
						}
						if (!profileObject.ContainsKey("DetailUvScaleX") || profileObject["DetailUvScaleX"] == null)
						{
							profileObject["DetailUvScaleX"] = 1.0f;
						}
						if (!profileObject.ContainsKey("DetailUvScaleY") || profileObject["DetailUvScaleY"] == null)
						{
							profileObject["DetailUvScaleY"] = 1.0f;
						}
						if (!profileObject.ContainsKey("DetailUvScrollX") || profileObject["DetailUvScrollX"] == null)
						{
							profileObject["DetailUvScrollX"] = 0.0f;
						}
						if (!profileObject.ContainsKey("DetailUvScrollY") || profileObject["DetailUvScrollY"] == null)
						{
							profileObject["DetailUvScrollY"] = 0.0f;
						}
						if (!profileObject.ContainsKey("DetailAlpha") || profileObject["DetailAlpha"] == null)
						{
							profileObject["DetailAlpha"] = 0.5f;
						}
						if (!profileObject.ContainsKey("DetailBlendMode") || profileObject["DetailBlendMode"] == null)
						{
							profileObject["DetailBlendMode"] = 0;
						}
					}
				}
			}

			progress?.Report(new MigrationProgressUpdate(Description, 2, totalSteps, "Normalizing shaders in manifest and metadata..."));

			var unionedAssets = MapAssetHelper.LoadAssets(mapDirectory);

			if (metadataRoot.TryGetPropertyValue("shaders", out var shadersNode) && shadersNode is JsonObject shadersObject)
			{
				MapUpgradeService.MergeCategoryInto(unionedAssets, "Shader", shadersObject);
			}

			MapAssetHelper.SaveAssetsToManifest(mapDirectory, unionedAssets, removeFromMetadata: true);

			metadataRoot.Remove("Assets");
			if (metadataRoot["MapProperties"] is JsonObject mapPropertiesObject)
			{
				mapPropertiesObject.Remove("Assets");
			}

			progress?.Report(new MigrationProgressUpdate(Description, 3, totalSteps, "Updating map build number and saving metadata.json..."));

			metadataRoot["GameBuildNumber"] = ToVersion;
			SaveLoadService.CleanMetadataJsonSchema(metadataRoot);

			MapJsonFormatter.SaveFormattedJson(metadataPath, metadataRoot);

			return new MigrationResult
			{
				Success = true,
				FromVersion = FromVersion,
				ToVersion = ToVersion
			};
		}
		catch (Exception ex)
		{
			return new MigrationResult
			{
				Success = false,
				ErrorMessage = $"Migration 0.0.3 failed: {ex.Message}",
				FromVersion = FromVersion,
				ToVersion = ToVersion
			};
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
			JsonObject? metadataRoot = null;
			if (File.Exists(metadataPath))
			{
				string text = File.ReadAllText(metadataPath);
				metadataRoot = JsonNode.Parse(text)?.AsObject();
			}

			metadataRoot ??= new JsonObject();

			const int totalSteps = 3;

			progress?.Report(new MigrationProgressUpdate(Description, 1, totalSteps, "Normalizing entity template definitions into Templates container..."));

			if (!metadataRoot.ContainsKey("Templates") || metadataRoot["Templates"] is not JsonObject)
			{
				metadataRoot["Templates"] = new JsonObject();
			}
			var templatesObj = metadataRoot["Templates"]!.AsObject();

			var entityCategories = new (string LegacyCustomKey, string CanonicalKey, string ObjectType, string LegacyIdKey)[]
			{
				("CustomUnits", "Units", "unit", "UnitId"),
				("CustomBuildings", "Buildings", "building", "UnitId"),
				("CustomResources", "Resources", "resource", "UnitId"),
				("CustomProps", "Props", "prop", "UnitId"),
				("CustomAbilities", "Abilities", "ability", "AbilityId"),
				("CustomWeapons", "Weapons", "weapon", "WeaponId"),
				("CustomUpgrades", "Upgrades", "upgrade", "UpgradeId"),
				("CustomItems", "Items", "item", "ItemId"),
				("CustomAttachments", "Attachments", "", ""),
				("CustomVfx", "Vfx", "", "")
			};

			foreach (var (legacyCustomKey, canonicalKey, objectType, legacyIdKey) in entityCategories)
			{
				if (metadataRoot.TryGetPropertyValue(legacyCustomKey, out var legacyArrNode) && legacyArrNode is JsonArray legacyArray)
				{
					if (!templatesObj.ContainsKey(canonicalKey) || templatesObj[canonicalKey] is not JsonArray)
					{
						templatesObj[canonicalKey] = new JsonArray();
					}
					var canonicalArray = templatesObj[canonicalKey]!.AsArray();
					foreach (var item in legacyArray)
					{
						if (item != null)
						{
							canonicalArray.Add(item.DeepClone());
						}
					}
					metadataRoot.Remove(legacyCustomKey);
				}

				if (templatesObj.TryGetPropertyValue(canonicalKey, out var arrNode) && arrNode is JsonArray entityArray && !string.IsNullOrEmpty(objectType))
				{
					foreach (var item in entityArray)
					{
						if (item is JsonObject entityObj)
						{
							string rawId = string.Empty;
							if (entityObj.TryGetPropertyValue("TemplateID", out var templateIdNode) && templateIdNode != null)
							{
								rawId = templateIdNode.ToString();
							}
							else if (entityObj.TryGetPropertyValue("ObjectID", out var objectIdNode) && objectIdNode != null)
							{
								rawId = objectIdNode.ToString();
								entityObj.Remove("ObjectID");
							}
							else if (!string.IsNullOrEmpty(legacyIdKey) && entityObj.TryGetPropertyValue(legacyIdKey, out var legacyIdNode) && legacyIdNode != null)
							{
								rawId = legacyIdNode.ToString();
								entityObj.Remove(legacyIdKey);
							}
							else if (entityObj.TryGetPropertyValue("UnitId", out var fallbackUnitIdNode) && fallbackUnitIdNode != null)
							{
								rawId = fallbackUnitIdNode.ToString();
								entityObj.Remove("UnitId");
							}

							if (!string.IsNullOrWhiteSpace(rawId))
							{
								entityObj["TemplateID"] = TemplateIDHelper.NormalizeTemplateID(objectType, rawId);
							}
						}
					}
				}
			}

			progress?.Report(new MigrationProgressUpdate(Description, 2, totalSteps, "Migrating manifest.json asset keys to canonical categories..."));

			string manifestPath = Path.Combine(mapDirectory, "manifest.json");
			if (File.Exists(manifestPath))
			{
				string manifestText = File.ReadAllText(manifestPath);
				JsonObject? manifestRoot = JsonNode.Parse(manifestText)?.AsObject();
				if (manifestRoot != null && manifestRoot.TryGetPropertyValue("Assets", out var assetsNode) && assetsNode is JsonObject assetsObj)
				{
					var topLevelRenames = new (string OldKey, string NewKey)[]
					{
						("animations", "Animation"),
						("decals", "Decal"),
						("icons", "Icon"),
						("music", "Music"),
						("noise_textures", "Noise"),
						("ribbons", "Ribbon"),
						("sfx", "SoundEffect"),
						("shaders", "Shader"),
						("skyboxes", "Skybox"),
						("textures", "Terrain")
					};

					foreach (var (oldKey, newKey) in topLevelRenames)
					{
						if (assetsObj.TryGetPropertyValue(oldKey, out var sourceNode) && sourceNode is JsonObject sourceObj)
						{
							UnionCategoryInto(assetsObj, newKey, sourceObj);
							assetsObj.Remove(oldKey);
						}
					}

					if (assetsObj.TryGetPropertyValue("glb", out var glbNode) && glbNode is JsonObject glbObj)
					{
						var glbRenames = new (string OldKey, string NewKey)[]
						{
							("units", "Character"),
							("resources", "Prop"),
							("props", "Prop"),
							("projectiles", "Item"),
							("buildings", "Building")
						};

						foreach (var (oldKey, newKey) in glbRenames)
						{
							if (glbObj.TryGetPropertyValue(oldKey, out var sourceNode) && sourceNode is JsonObject sourceObj)
							{
								UnionCategoryInto(assetsObj, newKey, sourceObj);
								glbObj.Remove(oldKey);
							}
						}

						if (glbObj.Count == 0)
						{
							assetsObj.Remove("glb");
						}
					}

					foreach (var categoryPair in assetsObj)
					{
						string categoryName = categoryPair.Key;
						if (categoryPair.Value is JsonObject categoryObj)
						{
							foreach (var assetPair in categoryObj)
							{
								string fileName = assetPair.Key;
								string hash = assetPair.Value is JsonObject obj
									? (obj["hash"]?.ToString() ?? obj["blake3"]?.ToString() ?? string.Empty)
									: (assetPair.Value?.ToString() ?? string.Empty);

								EnsureTemplateForManifestAsset(metadataRoot, templatesObj, categoryName, fileName, hash);
							}
						}
					}

					MapJsonFormatter.SaveFormattedJson(manifestPath, manifestRoot);
				}
			}

			progress?.Report(new MigrationProgressUpdate(Description, 3, totalSteps, "Updating map build number and saving metadata.json..."));

			metadataRoot["GameBuildNumber"] = ToVersion;
			SaveLoadService.CleanMetadataJsonSchema(metadataRoot);

			MapJsonFormatter.SaveFormattedJson(metadataPath, metadataRoot);

			return new MigrationResult
			{
				Success = true,
				FromVersion = FromVersion,
				ToVersion = ToVersion
			};
		}
		catch (Exception ex)
		{
			return new MigrationResult
			{
				Success = false,
				ErrorMessage = $"Migration 0.0.4 failed: {ex.Message}",
				FromVersion = FromVersion,
				ToVersion = ToVersion
			};
		}
	}

	public Task<MigrationResult> UpAsync(string mapDirectory, IProgress<MigrationProgressUpdate>? progress = null)
	{
		return Task.Run(() => Up(mapDirectory, progress));
	}

	private static void EnsureTemplateForManifestAsset(JsonObject metadataRoot, JsonObject templatesObj, string category, string fileName, string hash)
	{
		if (string.IsNullOrWhiteSpace(fileName)) return;

		string slug = TemplateIDHelper.GenerateSlug(fileName);
		string normalizedCategory = MapAssetHelper.NormalizeCategoryKey(category);

		switch (normalizedCategory.ToLowerInvariant())
		{
			case "character" or "unit" or "characters" or "units":
				{
					string unitTemplateId = TemplateIDHelper.NormalizeTemplateID("unit", slug);
					if (!templatesObj.ContainsKey("Units") || templatesObj["Units"] is not JsonArray)
					{
						templatesObj["Units"] = new JsonArray();
					}
					var unitsArr = templatesObj["Units"]!.AsArray();
					bool exists = unitsArr.OfType<JsonObject>().Any(u =>
						string.Equals(u["TemplateID"]?.ToString(), unitTemplateId, StringComparison.OrdinalIgnoreCase) ||
						string.Equals(u["ModelPath"]?.ToString(), fileName, StringComparison.OrdinalIgnoreCase));
					if (!exists)
					{
						unitsArr.Add(new JsonObject
						{
							["TemplateID"] = unitTemplateId,
							["Name"] = slug,
							["Description"] = "",
							["ModelPath"] = fileName,
							["Scale"] = 1.0f,
							["PathingType"] = 9,
							["DespillPlayerColor"] = false,
							["NormalizeLuminance"] = true
						});
					}
					break;
				}

			case "building" or "buildings":
				{
					string buildingTemplateId = TemplateIDHelper.NormalizeTemplateID("building", slug);
					if (!templatesObj.ContainsKey("Buildings") || templatesObj["Buildings"] is not JsonArray)
					{
						templatesObj["Buildings"] = new JsonArray();
					}
					var buildingsArr = templatesObj["Buildings"]!.AsArray();
					bool exists = buildingsArr.OfType<JsonObject>().Any(b =>
						string.Equals(b["TemplateID"]?.ToString(), buildingTemplateId, StringComparison.OrdinalIgnoreCase) ||
						string.Equals(b["ModelPath"]?.ToString(), fileName, StringComparison.OrdinalIgnoreCase));
					if (!exists)
					{
						buildingsArr.Add(new JsonObject
						{
							["TemplateID"] = buildingTemplateId,
							["Name"] = slug,
							["Description"] = "",
							["ModelPath"] = fileName,
							["Scale"] = 1.5f,
							["PathingType"] = 32,
							["DespillPlayerColor"] = false,
							["NormalizeLuminance"] = true
						});
					}
					break;
				}

			case "prop" or "props":
				{
					string propTemplateId = TemplateIDHelper.NormalizeTemplateID("prop", slug);
					if (!templatesObj.ContainsKey("Props") || templatesObj["Props"] is not JsonArray)
					{
						templatesObj["Props"] = new JsonArray();
					}
					var propsArr = templatesObj["Props"]!.AsArray();
					bool exists = propsArr.OfType<JsonObject>().Any(p =>
						string.Equals(p["TemplateID"]?.ToString(), propTemplateId, StringComparison.OrdinalIgnoreCase) ||
						string.Equals(p["ModelPath"]?.ToString(), fileName, StringComparison.OrdinalIgnoreCase));
					if (!exists)
					{
						propsArr.Add(new JsonObject
						{
							["TemplateID"] = propTemplateId,
							["Name"] = slug,
							["Description"] = "",
							["ModelPath"] = fileName,
							["Scale"] = 1.25f,
							["PathingType"] = 255,
							["DespillPlayerColor"] = false,
							["NormalizeLuminance"] = true,
							["IgnorePlayerColor"] = true
						});
					}
					break;
				}

			case "resource" or "resources":
				{
					string resourceTemplateId = TemplateIDHelper.NormalizeTemplateID("resource", slug);
					if (!templatesObj.ContainsKey("Resources") || templatesObj["Resources"] is not JsonArray)
					{
						templatesObj["Resources"] = new JsonArray();
					}
					var resourcesArr = templatesObj["Resources"]!.AsArray();
					bool exists = resourcesArr.OfType<JsonObject>().Any(r =>
						string.Equals(r["TemplateID"]?.ToString(), resourceTemplateId, StringComparison.OrdinalIgnoreCase) ||
						string.Equals(r["ModelPath"]?.ToString(), fileName, StringComparison.OrdinalIgnoreCase));
					if (!exists)
					{
						resourcesArr.Add(new JsonObject
						{
							["TemplateID"] = resourceTemplateId,
							["Name"] = slug,
							["Description"] = "",
							["ModelPath"] = fileName,
							["Scale"] = 2.75f,
							["PathingType"] = 255,
							["DespillPlayerColor"] = false,
							["NormalizeLuminance"] = true,
							["IgnorePlayerColor"] = true
						});
					}
					break;
				}

			case "item" or "items":
				{
					string itemTemplateId = TemplateIDHelper.NormalizeTemplateID("item", slug);
					if (!templatesObj.ContainsKey("Items") || templatesObj["Items"] is not JsonArray)
					{
						templatesObj["Items"] = new JsonArray();
					}
					var itemsArr = templatesObj["Items"]!.AsArray();
					bool exists = itemsArr.OfType<JsonObject>().Any(i =>
						string.Equals(i["TemplateID"]?.ToString(), itemTemplateId, StringComparison.OrdinalIgnoreCase) ||
						string.Equals(i["IconPath"]?.ToString(), fileName, StringComparison.OrdinalIgnoreCase));
					if (!exists)
					{
						itemsArr.Add(new JsonObject
						{
							["TemplateID"] = itemTemplateId,
							["Name"] = slug,
							["Description"] = "",
							["ItemClass"] = "consumable",
							["IconPath"] = fileName.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase) ? fileName : null,
							["CanDrop"] = true
						});
					}
					break;
				}

			case "decal" or "decals":
				{
					string decalTemplateId = TemplateIDHelper.NormalizeTemplateID("decal", slug);
					if (!metadataRoot.ContainsKey("decals") || metadataRoot["decals"] is not JsonObject)
					{
						metadataRoot["decals"] = new JsonObject();
					}
					var decalsObj = metadataRoot["decals"]!.AsObject();
					bool exists = decalsObj.Any(kvp =>
						string.Equals(kvp.Key, decalTemplateId, StringComparison.OrdinalIgnoreCase) ||
						(kvp.Value is JsonObject dObj && string.Equals(dObj["TexturePath"]?.ToString(), fileName, StringComparison.OrdinalIgnoreCase)));
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
					break;
				}

			case "terrain" or "textures":
				{
					if (!metadataRoot.ContainsKey("textures") || metadataRoot["textures"] is not JsonObject)
					{
						metadataRoot["textures"] = new JsonObject();
					}
					var texturesObj = metadataRoot["textures"]!.AsObject();
					bool exists = texturesObj.Any(kvp =>
						string.Equals(kvp.Key, fileName, StringComparison.OrdinalIgnoreCase) ||
						(kvp.Value is JsonObject tObj && string.Equals(tObj["AssetType"]?.ToString(), fileName, StringComparison.OrdinalIgnoreCase)));
					if (!exists)
					{
						texturesObj[fileName] = new JsonObject
						{
							["Hash"] = hash ?? string.Empty,
							["AssetType"] = fileName,
							["ScaleFactor"] = 1.0f,
							["Brightness"] = 1.0f,
							["Contrast"] = 1.0f,
							["Saturation"] = 1.0f,
							["TileMode"] = "Stochastic"
						};
					}

					if (!metadataRoot.ContainsKey("TerrainProfiles") || metadataRoot["TerrainProfiles"] is not JsonArray)
					{
						metadataRoot["TerrainProfiles"] = new JsonArray();
					}
					var terrainProfilesArr = metadataRoot["TerrainProfiles"]!.AsArray();
					bool profileExists = terrainProfilesArr.OfType<JsonObject>().Any(tp =>
						string.Equals(tp["SwatchName"]?.ToString(), fileName, StringComparison.OrdinalIgnoreCase));
					if (!profileExists)
					{
						terrainProfilesArr.Add(new JsonObject
						{
							["SwatchName"] = fileName,
							["DefaultPathingCode"] = 0,
							["DecalBombingRules"] = new JsonArray(),
							["VfxBombingRules"] = new JsonArray()
						});
					}
					break;
				}

			case "spritesheet" or "spritesheets" or "vfx" or "vfx_radial" or "vfx_vertical":
				{
					if (!metadataRoot.ContainsKey("vfx_spritesheets") || metadataRoot["vfx_spritesheets"] is not JsonObject)
					{
						metadataRoot["vfx_spritesheets"] = new JsonObject();
					}
					var vfxObj = metadataRoot["vfx_spritesheets"]!.AsObject();
					bool exists = vfxObj.Any(kvp =>
						string.Equals(kvp.Key, fileName, StringComparison.OrdinalIgnoreCase) ||
						(kvp.Value is JsonObject sObj && string.Equals(sObj["AssetType"]?.ToString(), fileName, StringComparison.OrdinalIgnoreCase)));
					if (!exists)
					{
						vfxObj[fileName] = new JsonObject
						{
							["Hash"] = hash ?? string.Empty,
							["AssetType"] = fileName,
							["Columns"] = 4,
							["Rows"] = 4,
							["Fps"] = 20.0f
						};
					}
					break;
				}

			case "icon" or "icons":
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
					break;
				}

			case "ribbon" or "ribbons":
				{
					string ribbonTemplateId = TemplateIDHelper.NormalizeTemplateID("ribbon", slug);
					if (!metadataRoot.ContainsKey("ribbons") || metadataRoot["ribbons"] is not JsonObject)
					{
						metadataRoot["ribbons"] = new JsonObject();
					}
					var ribbonsObj = metadataRoot["ribbons"]!.AsObject();
					bool exists = ribbonsObj.Any(kvp => string.Equals(kvp.Key, ribbonTemplateId, StringComparison.OrdinalIgnoreCase));
					if (!exists)
					{
						ribbonsObj[ribbonTemplateId] = new JsonObject
						{
							["Hash"] = hash ?? string.Empty
						};
					}
					break;
				}

			case "noise" or "noise_textures":
				{
					if (!metadataRoot.ContainsKey("noise_textures") || metadataRoot["noise_textures"] is not JsonObject)
					{
						metadataRoot["noise_textures"] = new JsonObject();
					}
					var noiseObj = metadataRoot["noise_textures"]!.AsObject();
					bool exists = noiseObj.Any(kvp =>
						string.Equals(kvp.Key, fileName, StringComparison.OrdinalIgnoreCase) ||
						(kvp.Value is JsonObject nObj && string.Equals(nObj["AssetType"]?.ToString(), fileName, StringComparison.OrdinalIgnoreCase)));
					if (!exists)
					{
						noiseObj[fileName] = new JsonObject
						{
							["Hash"] = hash ?? string.Empty,
							["AssetType"] = fileName,
							["ScaleFactor"] = 1.0f
						};
					}
					break;
				}

			case "skybox" or "skyboxes":
				{
					string skyboxTemplateId = TemplateIDHelper.NormalizeTemplateID("skybox", slug);
					if (!metadataRoot.ContainsKey("skyboxes") || metadataRoot["skyboxes"] is not JsonObject)
					{
						metadataRoot["skyboxes"] = new JsonObject();
					}
					var skyboxesObj = metadataRoot["skyboxes"]!.AsObject();
					bool exists = skyboxesObj.Any(kvp => string.Equals(kvp.Key, skyboxTemplateId, StringComparison.OrdinalIgnoreCase));
					if (!exists)
					{
						skyboxesObj[skyboxTemplateId] = new JsonObject
						{
							["Hash"] = hash ?? string.Empty
						};
					}
					break;
				}

			case "shader" or "shaders":
				{
					string shaderTemplateId = TemplateIDHelper.NormalizeTemplateID("shader", slug);
					if (!metadataRoot.ContainsKey("shaders") || metadataRoot["shaders"] is not JsonObject)
					{
						metadataRoot["shaders"] = new JsonObject();
					}
					var shadersObj = metadataRoot["shaders"]!.AsObject();
					bool exists = shadersObj.Any(kvp => string.Equals(kvp.Key, shaderTemplateId, StringComparison.OrdinalIgnoreCase));
					if (!exists)
					{
						shadersObj[shaderTemplateId] = new JsonObject
						{
							["Hash"] = hash ?? string.Empty
						};
					}
					break;
				}
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
		if (string.IsNullOrWhiteSpace(versionString))
		{
			return new Version(0, 0, 0, 0);
		}

		string cleaned = versionString.Trim().TrimStart('v', 'V').Trim();
		int separatorIndex = cleaned.IndexOfAny(new[] { '-', '_', '+', ' ', '(' });
		if (separatorIndex >= 0)
		{
			cleaned = cleaned.Substring(0, separatorIndex).Trim();
		}

		if (Version.TryParse(cleaned, out var parsedVersion))
		{
			int major = Math.Max(0, parsedVersion.Major);
			int minor = Math.Max(0, parsedVersion.Minor);
			int build = Math.Max(0, parsedVersion.Build);
			int revision = parsedVersion.Revision >= 0 ? parsedVersion.Revision : 0;
			return new Version(major, minor, build, revision);
		}

		var parts = cleaned.Split('.');
		if (parts.Length == 1 && int.TryParse(parts[0], out int p0))
		{
			return new Version(p0, 0, 0, 0);
		}
		if (parts.Length == 2 && int.TryParse(parts[0], out int maj) && int.TryParse(parts[1], out int min))
		{
			return new Version(maj, min, 0, 0);
		}
		if (parts.Length == 3 && int.TryParse(parts[0], out int b0) && int.TryParse(parts[1], out int b1) && int.TryParse(parts[2], out int b2))
		{
			return new Version(b0, b1, b2, 0);
		}
		if (parts.Length >= 4 && int.TryParse(parts[0], out int r0) && int.TryParse(parts[1], out int r1) && int.TryParse(parts[2], out int r2) && int.TryParse(parts[3], out int r3))
		{
			return new Version(r0, r1, r2, r3);
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

		var excludedFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
		{
			".git", "bin", "obj", ".godot", ".vs", ".vscode", ".idea", "map_upgrades", "map_backups", "backups", ".backups", ".dotnet", ".wasi", ".sidecarcache", ".cache"
		};

		Directory.CreateDirectory(targetDir);

		foreach (var file in source.GetFiles())
		{
			if (file.Extension.Equals(".tmp", StringComparison.OrdinalIgnoreCase)) continue;
			string destFile = Path.Combine(targetDir, file.Name);
			file.CopyTo(destFile, true);
		}

		foreach (var dir in source.GetDirectories())
		{
			if (excludedFolders.Contains(dir.Name)) continue;
			if (dir.Name.StartsWith("backup_", StringComparison.OrdinalIgnoreCase)) continue;

			string subDirFull = Path.GetFullPath(dir.FullName).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

			if (subDirFull.IndexOf("map_backups", StringComparison.OrdinalIgnoreCase) >= 0 ||
				subDirFull.IndexOf("map_upgrades", StringComparison.OrdinalIgnoreCase) >= 0 ||
				subDirFull.IndexOf(".backups", StringComparison.OrdinalIgnoreCase) >= 0)
			{
				continue;
			}

			if (!string.IsNullOrEmpty(normalizedBackupsRoot) &&
				(string.Equals(subDirFull, normalizedBackupsRoot, StringComparison.OrdinalIgnoreCase) ||
				 subDirFull.StartsWith(normalizedBackupsRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
			{
				continue;
			}

			if (string.Equals(subDirFull, normalizedTarget, StringComparison.OrdinalIgnoreCase) ||
				subDirFull.StartsWith(normalizedTarget + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
				normalizedTarget.StartsWith(subDirFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			string destSubDir = Path.Combine(targetDir, dir.Name);
			CopyDirectoryContentsSafe(dir.FullName, destSubDir, backupsRoot);
		}
	}
}
