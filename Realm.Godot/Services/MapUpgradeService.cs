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

			string assetsDir = Path.Combine(mapDirectory, "Assets");
			if (Directory.Exists(assetsDir))
			{
				string legacyVfxDir = Path.Combine(assetsDir, "vfx");
				string newVfxDir = Path.Combine(assetsDir, "vfx_spritesheets");
				if (Directory.Exists(legacyVfxDir))
				{
					Directory.CreateDirectory(newVfxDir);
					foreach (var file in Directory.GetFiles(legacyVfxDir, "*.*", SearchOption.AllDirectories))
					{
						string rel = Path.GetRelativePath(legacyVfxDir, file);
						string target = Path.Combine(newVfxDir, rel);
						string? targetDir = Path.GetDirectoryName(target);
						if (!string.IsNullOrEmpty(targetDir)) Directory.CreateDirectory(targetDir);
						if (!File.Exists(target))
						{
							File.Move(file, target);
						}
						else
						{
							File.Delete(file);
						}
					}
					try
					{
						Directory.Delete(legacyVfxDir, true);
					}
					catch { }
				}

				var prefixSubfolderChecks = new (string Folder, string Prefix)[]
				{
					("icons", "icon"),
					("skyboxes", "skybox"),
					("textures", "terrain"),
					("decals", "decal"),
					("ribbons", "ribbon"),
					("vfx_spritesheets", "spritesheet"),
					("vfx_spritesheets", "vfx")
				};

				foreach (var (folder, prefix) in prefixSubfolderChecks)
				{
					string nestedDir = Path.Combine(assetsDir, folder, prefix);
					if (Directory.Exists(nestedDir))
					{
						string parentDir = Path.Combine(assetsDir, folder);
						foreach (var file in Directory.GetFiles(nestedDir, "*.*", SearchOption.AllDirectories))
						{
							string rel = Path.GetRelativePath(nestedDir, file);
							string target = Path.Combine(parentDir, rel);
							string? targetDir = Path.GetDirectoryName(target);
							if (!string.IsNullOrEmpty(targetDir)) Directory.CreateDirectory(targetDir);
							if (!File.Exists(target))
							{
								File.Move(file, target);
							}
							else
							{
								File.Delete(file);
							}
						}
						try
						{
							Directory.Delete(nestedDir, true);
						}
						catch { }
					}
				}
			}

			const int totalSteps = 4;

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

			if (metadataRoot.TryGetPropertyValue("decals", out var decalsNode) && decalsNode is JsonObject decalsObj)
			{
				var normalizedDecals = new JsonObject();
				foreach (var kvp in decalsObj)
				{
					string rawKey = kvp.Key;
					string slug = TemplateIDHelper.GenerateSlug(rawKey);
					string normalizedId = TemplateIDHelper.NormalizeTemplateID("decal", slug);
					var itemObj = kvp.Value as JsonObject ?? new JsonObject();
					itemObj.Remove("Hash");
					itemObj.Remove("hash");
					itemObj.Remove("AssetType");
					itemObj.Remove("asset_type");
					if (string.IsNullOrWhiteSpace(itemObj["TexturePath"]?.ToString()))
					{
						itemObj["TexturePath"] = rawKey.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase) ? rawKey : $"{slug}.rtex";
					}
					if (!itemObj.ContainsKey("Opacity")) itemObj["Opacity"] = 1.0f;
					if (!itemObj.ContainsKey("Brightness")) itemObj["Brightness"] = 1.0f;
					if (!itemObj.ContainsKey("Contrast")) itemObj["Contrast"] = 1.0f;
					if (!itemObj.ContainsKey("Saturation")) itemObj["Saturation"] = 1.0f;

					if (!normalizedDecals.ContainsKey(normalizedId))
					{
						normalizedDecals[normalizedId] = itemObj.DeepClone();
					}
					else if (normalizedDecals[normalizedId] is JsonObject existingObj)
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
				metadataRoot["decals"] = normalizedDecals;
			}

			if (!metadataRoot.ContainsKey("SpawnShader") || metadataRoot["SpawnShader"] is not JsonObject)
			{
				metadataRoot["SpawnShader"] = new JsonObject();
			}
			var spawnShadersObj = metadataRoot["SpawnShader"]!.AsObject();
			if (metadataRoot.TryGetPropertyValue("shaders", out var legacyShadersNode) && legacyShadersNode is JsonObject legacyShadersObj)
			{
				foreach (var kvp in legacyShadersObj)
				{
					if (!spawnShadersObj.ContainsKey(kvp.Key) && kvp.Value != null)
					{
						spawnShadersObj[kvp.Key] = kvp.Value.DeepClone();
					}
				}
				metadataRoot.Remove("shaders");
			}

			var normalizedShaders = new JsonObject();
			foreach (var kvp in spawnShadersObj)
			{
				string rawKey = kvp.Key;
				string slug = TemplateIDHelper.GenerateSlug(rawKey);
				string normalizedId = TemplateIDHelper.NormalizeTemplateID("SpawnShader", slug);
				var itemObj = kvp.Value as JsonObject ?? new JsonObject();

				if (itemObj.TryGetPropertyValue("ConfigJson", out var configJsonNode) && configJsonNode != null)
				{
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
									string propName = cfgProp.Key switch
									{
										"name" => "Name",
										"transition_mode" => "TransitionMode",
										"direction" => "Direction",
										"edge_color" => "EdgeColor",
										"edge_width" => "EdgeWidth",
										"edge_emission" => "EdgeEmission",
										"noise_scale" => "NoiseScale",
										"noise_roughness" => "NoiseRoughness",
										"fresnel_power" => "FresnelPower",
										"vertex_displacement" => "VertexDisplacement",
										"alpha_fade" => "AlphaFade",
										"duration" => "Duration",
										"asset_type" => "AssetType",
										_ => cfgProp.Key
									};
									itemObj[propName] = cfgProp.Value?.DeepClone();
								}
							}
						}
						catch { }
					}
					itemObj.Remove("ConfigJson");
				}

				if (!normalizedShaders.ContainsKey(normalizedId))
				{
					normalizedShaders[normalizedId] = itemObj.DeepClone();
				}
			}

			if (metadataRoot.TryGetPropertyValue("TerrainProfiles", out var legacyProfilesNode) && legacyProfilesNode is JsonArray legacyProfilesArr)
			{
				if (!metadataRoot.ContainsKey("textures") || metadataRoot["textures"] is not JsonObject)
				{
					metadataRoot["textures"] = new JsonObject();
				}
				var texturesObj = metadataRoot["textures"]!.AsObject();

				foreach (var profItem in legacyProfilesArr)
				{
					if (profItem is JsonObject profObj)
					{
						string rawSwatch = profObj["SwatchName"]?.ToString() ?? string.Empty;
						if (string.IsNullOrWhiteSpace(rawSwatch)) continue;

						string slug = TemplateIDHelper.GenerateSlug(rawSwatch);
						string normalizedId = TemplateIDHelper.NormalizeTemplateID("terrain", slug);

						JsonObject? targetTexObj = null;
						if (texturesObj.TryGetPropertyValue(normalizedId, out var existingNode) && existingNode is JsonObject existingObj)
						{
							targetTexObj = existingObj;
						}
						else if (texturesObj.TryGetPropertyValue(rawSwatch, out var directNode) && directNode is JsonObject directObj)
						{
							targetTexObj = directObj;
						}
						else if (texturesObj.TryGetPropertyValue(slug, out var slugNode) && slugNode is JsonObject slugObj)
						{
							targetTexObj = slugObj;
						}

						if (targetTexObj == null)
						{
							targetTexObj = new JsonObject
							{
								["AssetType"] = rawSwatch.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase) ? rawSwatch : $"{slug}.rtex",
								["TileMode"] = "Stochastic",
								["UvScale"] = 1.0f,
								["Brightness"] = 1.0f
							};
							texturesObj[normalizedId] = targetTexObj;
						}

						if (profObj.TryGetPropertyValue("DefaultPathingCode", out var dpcVal) && dpcVal != null)
						{
							targetTexObj["DefaultPathingCode"] = dpcVal.DeepClone();
						}
						if (profObj.TryGetPropertyValue("DecalBombingRules", out var dbrVal) && dbrVal != null)
						{
							targetTexObj["DecalBombingRules"] = dbrVal.DeepClone();
						}
						if (profObj.TryGetPropertyValue("VfxBombingRules", out var vbrVal) && vbrVal != null)
						{
							targetTexObj["VfxBombingRules"] = vbrVal.DeepClone();
						}
					}
				}
				metadataRoot.Remove("TerrainProfiles");
			}

			if (metadataRoot.TryGetPropertyValue("textures", out var texturesNodeFinal) && texturesNodeFinal is JsonObject texturesObjFinal)
			{
				var normalizedTextures = new JsonObject();
				foreach (var kvp in texturesObjFinal)
				{
					string rawKey = kvp.Key;
					string slug = TemplateIDHelper.GenerateSlug(rawKey);
					string normalizedId = TemplateIDHelper.NormalizeTemplateID("terrain", slug);
					var itemObj = kvp.Value as JsonObject ?? new JsonObject();

					string rtexName = itemObj["TexturePath"]?.ToString()
						?? itemObj["texturePath"]?.ToString()
						?? itemObj["AssetType"]?.ToString()
						?? itemObj["assetType"]?.ToString()
						?? itemObj["rtex"]?.ToString()
						?? (rawKey.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase) ? rawKey : $"{slug}.rtex");

					itemObj.Remove("AssetType");
					itemObj.Remove("assetType");
					itemObj.Remove("asset_type");
					itemObj.Remove("rtex");
					itemObj.Remove("texturePath");
					itemObj.Remove("Hash");
					itemObj.Remove("hash");

					itemObj["TexturePath"] = rtexName;

					if (itemObj.TryGetPropertyValue("Scale_Factor", out var sfVal) || itemObj.TryGetPropertyValue("scale_factor", out sfVal))
					{
						itemObj["ScaleFactor"] = sfVal?.DeepClone();
						itemObj.Remove("Scale_Factor");
						itemObj.Remove("scale_factor");
					}
					if (itemObj.TryGetPropertyValue("swatchIndex", out var swVal) || itemObj.TryGetPropertyValue("swatch_index", out swVal))
					{
						itemObj["SwatchIndex"] = swVal?.DeepClone();
						itemObj.Remove("swatchIndex");
						itemObj.Remove("swatch_index");
					}
					if (itemObj.TryGetPropertyValue("Roughness_Scale", out var rsVal) || itemObj.TryGetPropertyValue("roughness_scale", out rsVal) || itemObj.TryGetPropertyValue("roughnessScale", out rsVal))
					{
						itemObj["RoughnessScale"] = rsVal?.DeepClone();
						itemObj.Remove("Roughness_Scale");
						itemObj.Remove("roughness_scale");
						itemObj.Remove("roughnessScale");
					}
					if (itemObj.TryGetPropertyValue("Normal_Scale", out var nsVal) || itemObj.TryGetPropertyValue("normal_scale", out nsVal) || itemObj.TryGetPropertyValue("normalScale", out nsVal))
					{
						itemObj["NormalScale"] = nsVal?.DeepClone();
						itemObj.Remove("Normal_Scale");
						itemObj.Remove("normal_scale");
						itemObj.Remove("normalScale");
					}
					if (itemObj.TryGetPropertyValue("Height_Scale", out var hsVal) || itemObj.TryGetPropertyValue("height_scale", out hsVal) || itemObj.TryGetPropertyValue("heightScale", out hsVal))
					{
						itemObj["HeightScale"] = hsVal?.DeepClone();
						itemObj.Remove("Height_Scale");
						itemObj.Remove("height_scale");
						itemObj.Remove("heightScale");
					}
					if (itemObj.TryGetPropertyValue("Height_Offset", out var hoVal) || itemObj.TryGetPropertyValue("height_offset", out hoVal) || itemObj.TryGetPropertyValue("heightOffset", out hoVal))
					{
						itemObj["HeightOffset"] = hoVal?.DeepClone();
						itemObj.Remove("Height_Offset");
						itemObj.Remove("height_offset");
						itemObj.Remove("heightOffset");
					}
					if (itemObj.TryGetPropertyValue("Crevice_Power", out var cpVal) || itemObj.TryGetPropertyValue("crevice_power", out cpVal) || itemObj.TryGetPropertyValue("crevicePower", out cpVal))
					{
						itemObj["CrevicePower"] = cpVal?.DeepClone();
						itemObj.Remove("Crevice_Power");
						itemObj.Remove("crevice_power");
						itemObj.Remove("crevicePower");
					}
					if (itemObj.TryGetPropertyValue("Tile_Mode", out var tmVal) || itemObj.TryGetPropertyValue("tile_mode", out tmVal) || itemObj.TryGetPropertyValue("tileMode", out tmVal))
					{
						itemObj["TileMode"] = tmVal?.DeepClone();
						itemObj.Remove("Tile_Mode");
						itemObj.Remove("tile_mode");
						itemObj.Remove("tileMode");
					}
					if (itemObj.TryGetPropertyValue("UV_Scale", out var uvVal) || itemObj.TryGetPropertyValue("uv_scale", out uvVal) || itemObj.TryGetPropertyValue("uvScale", out uvVal))
					{
						itemObj["UvScale"] = uvVal?.DeepClone();
						itemObj.Remove("UV_Scale");
						itemObj.Remove("uv_scale");
						itemObj.Remove("uvScale");
					}
					if (itemObj.TryGetPropertyValue("Stochastic_Tile_Size", out var stVal) || itemObj.TryGetPropertyValue("stochastic_tile_size", out stVal) || itemObj.TryGetPropertyValue("stochasticTileSize", out stVal))
					{
						itemObj["StochasticTileSize"] = stVal?.DeepClone();
						itemObj.Remove("Stochastic_Tile_Size");
						itemObj.Remove("stochastic_tile_size");
						itemObj.Remove("stochasticTileSize");
					}
					if (itemObj.TryGetPropertyValue("Cross_Fade", out var cfVal) || itemObj.TryGetPropertyValue("cross_fade", out cfVal) || itemObj.TryGetPropertyValue("Grid_Cross_Fade", out cfVal) || itemObj.TryGetPropertyValue("grid_cross_fade", out cfVal) || itemObj.TryGetPropertyValue("crossFade", out cfVal))
					{
						itemObj["CrossFade"] = cfVal?.DeepClone();
						itemObj.Remove("Cross_Fade");
						itemObj.Remove("cross_fade");
						itemObj.Remove("Grid_Cross_Fade");
						itemObj.Remove("grid_cross_fade");
						itemObj.Remove("crossFade");
					}

					if (!itemObj.ContainsKey("DefaultPathingCode") || itemObj["DefaultPathingCode"] == null)
					{
						itemObj["DefaultPathingCode"] = 8 | 32 | 4;
					}
					if (!itemObj.ContainsKey("DecalBombingRules") || itemObj["DecalBombingRules"] == null)
					{
						itemObj["DecalBombingRules"] = new JsonArray();
					}
					if (!itemObj.ContainsKey("VfxBombingRules") || itemObj["VfxBombingRules"] == null)
					{
						itemObj["VfxBombingRules"] = new JsonArray();
					}

					if (!normalizedTextures.ContainsKey(normalizedId))
					{
						normalizedTextures[normalizedId] = itemObj.DeepClone();
					}
					else if (normalizedTextures[normalizedId] is JsonObject existingObj)
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
				metadataRoot["textures"] = normalizedTextures;
			}

			if (metadataRoot.TryGetPropertyValue("icons", out var iconsNode) && iconsNode is JsonObject iconsObj)
			{
				var normalizedIcons = new JsonObject();
				foreach (var kvp in iconsObj)
				{
					string rawKey = kvp.Key;
					string slug = TemplateIDHelper.GenerateSlug(rawKey);
					string normalizedId = TemplateIDHelper.NormalizeTemplateID("icon", slug);
					var itemObj = kvp.Value as JsonObject ?? new JsonObject();
					itemObj.Remove("Hash");
					itemObj.Remove("hash");
					string texPath = itemObj["TexturePath"]?.ToString() ?? (rawKey.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase) ? rawKey : $"{slug}.rtex");
					itemObj["TexturePath"] = texPath;
					normalizedIcons[normalizedId] = itemObj;
				}
				metadataRoot["icons"] = normalizedIcons;
			}

			if (metadataRoot.TryGetPropertyValue("skyboxes", out var skyboxesNode) && skyboxesNode is JsonObject skyboxesObj)
			{
				var normalizedSkyboxes = new JsonObject();
				foreach (var kvp in skyboxesObj)
				{
					string rawKey = kvp.Key;
					string slug = TemplateIDHelper.GenerateSlug(rawKey);
					string normalizedId = TemplateIDHelper.NormalizeTemplateID("skybox", slug);
					var itemObj = kvp.Value as JsonObject ?? new JsonObject();
					itemObj.Remove("Hash");
					itemObj.Remove("hash");
					string texPath = itemObj["TexturePath"]?.ToString() ?? (rawKey.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase) ? rawKey : $"{slug}.rtex");
					itemObj["TexturePath"] = texPath;
					normalizedSkyboxes[normalizedId] = itemObj;
				}
				metadataRoot["skyboxes"] = normalizedSkyboxes;
			}

			if (metadataRoot.TryGetPropertyValue("ribbons", out var ribbonsNode) && ribbonsNode is JsonObject ribbonsObj)
			{
				var normalizedRibbons = new JsonObject();
				foreach (var kvp in ribbonsObj)
				{
					string rawKey = kvp.Key;
					string slug = TemplateIDHelper.GenerateSlug(rawKey);
					string normalizedId = TemplateIDHelper.NormalizeTemplateID("ribbon", slug);
					var itemObj = kvp.Value as JsonObject ?? new JsonObject();
					itemObj.Remove("Hash");
					itemObj.Remove("hash");
					string texPath = itemObj["TexturePath"]?.ToString() ?? (rawKey.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase) ? rawKey : $"{slug}.rtex");
					itemObj["TexturePath"] = texPath;
					normalizedRibbons[normalizedId] = itemObj;
				}
				metadataRoot["ribbons"] = normalizedRibbons;
			}

			if (metadataRoot.TryGetPropertyValue("vfx_spritesheets", out var vfxSpritesheetsNode) && vfxSpritesheetsNode is JsonObject vfxSpritesheetsObj)
			{
				var normalizedSpritesheets = new JsonObject();
				foreach (var kvp in vfxSpritesheetsObj)
				{
					string rawKey = kvp.Key;
					string slug = TemplateIDHelper.GenerateSlug(rawKey);
					string normalizedId = TemplateIDHelper.NormalizeTemplateID("spritesheet", slug);
					var itemObj = kvp.Value as JsonObject ?? new JsonObject();
					itemObj.Remove("Hash");
					itemObj.Remove("hash");
					string texPath = itemObj["TexturePath"]?.ToString() ?? itemObj["AssetType"]?.ToString() ?? (rawKey.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase) ? rawKey : $"{slug}.rtex");
					itemObj.Remove("AssetType");
					itemObj.Remove("asset_type");
					itemObj["TexturePath"] = texPath;

					if (itemObj.TryGetPropertyValue("columns", out var colVal))
					{
						itemObj["Columns"] = colVal?.DeepClone();
						itemObj.Remove("columns");
					}
					if (itemObj.TryGetPropertyValue("rows", out var rowVal))
					{
						itemObj["Rows"] = rowVal?.DeepClone();
						itemObj.Remove("rows");
					}
					if (itemObj.TryGetPropertyValue("fps", out var fpsVal))
					{
						itemObj["Fps"] = fpsVal?.DeepClone();
						itemObj.Remove("fps");
					}
					if (itemObj.TryGetPropertyValue("subframe_blend", out var sbVal) || itemObj.TryGetPropertyValue("subframeBlend", out sbVal))
					{
						itemObj["SubframeBlend"] = sbVal?.DeepClone();
						itemObj.Remove("subframe_blend");
						itemObj.Remove("subframeBlend");
					}

					if (!normalizedSpritesheets.ContainsKey(normalizedId))
					{
						normalizedSpritesheets[normalizedId] = itemObj.DeepClone();
					}
					else if (normalizedSpritesheets[normalizedId] is JsonObject existingObj)
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
				metadataRoot["vfx_spritesheets"] = normalizedSpritesheets;
			}

			if (metadataRoot.TryGetPropertyValue("Models", out var modelsNode) && modelsNode is JsonObject modelsObj)
			{
				var modelToTemplateId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
				var categoryNames = new[] { "Units", "Buildings", "Resources", "Props", "Items", "Weapons" };
				foreach (var cat in categoryNames)
				{
					if (templatesObj.TryGetPropertyValue(cat, out var catNode) && catNode is JsonArray arr)
					{
						foreach (var item in arr.OfType<JsonObject>())
						{
							string tId = item["TemplateID"]?.ToString() ?? "";
							string mPath = item["ModelPath"]?.ToString() ?? "";
							if (!string.IsNullOrEmpty(tId))
							{
								string slug = TemplateIDHelper.GenerateSlug(tId);
								if (!modelToTemplateId.ContainsKey(slug)) modelToTemplateId[slug] = tId;
								if (!string.IsNullOrEmpty(mPath))
								{
									string mFile = Path.GetFileName(mPath);
									string mSlug = TemplateIDHelper.GenerateSlug(mPath);
									if (!modelToTemplateId.ContainsKey(mFile)) modelToTemplateId[mFile] = tId;
									if (!modelToTemplateId.ContainsKey(mSlug)) modelToTemplateId[mSlug] = tId;
								}
							}
						}
					}
				}

				var normalizedModels = new JsonObject();
				foreach (var kvp in modelsObj)
				{
					string rawKey = kvp.Key;
					string targetKey = rawKey;
					if (modelToTemplateId.TryGetValue(rawKey, out var matchedTid))
					{
						targetKey = matchedTid;
					}
					else
					{
						string slug = TemplateIDHelper.GenerateSlug(rawKey);
						if (modelToTemplateId.TryGetValue(slug, out matchedTid))
						{
							targetKey = matchedTid;
						}
					}

					var itemObj = kvp.Value as JsonObject ?? new JsonObject();
					itemObj.Remove("Hash");
					itemObj.Remove("hash");
					if (!normalizedModels.ContainsKey(targetKey))
					{
						normalizedModels[targetKey] = itemObj.DeepClone();
					}
					else if (normalizedModels[targetKey] is JsonObject existingObj)
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
				metadataRoot["Models"] = normalizedModels;
			}

			if (normalizedShaders.Count == 0)
			{
				normalizedShaders["SpawnShader/magic_blueprint"] = CreateDefaultShaderConfig("Magic Blueprint", 0, 0, "#00e5ffff", 0.06f, 6.0f, 12.0f, 0.4f, 3.0f, 0.0f, 0.9f, 1.2f);
				normalizedShaders["SpawnShader/fire_demolish"] = CreateDefaultShaderConfig("Fire Ember Dissolve", 1, 1, "#ff590cff", 0.08f, 7.0f, 16.0f, 0.7f, 1.5f, 0.15f, 1.0f, 1.5f);
				normalizedShaders["SpawnShader/hologram_warp"] = CreateDefaultShaderConfig("Hologram Scanlines", 2, 0, "#66ff33ff", 0.04f, 4.0f, 20.0f, 0.2f, 4.0f, 0.02f, 0.75f, 1.0f);
				normalizedShaders["SpawnShader/earth_crumble"] = CreateDefaultShaderConfig("Earth Ground Crumble", 3, 1, "#99734cff", 0.05f, 2.0f, 8.0f, 0.8f, 1.0f, 0.25f, 1.0f, 1.1f);
				normalizedShaders["SpawnShader/frost_crystallize"] = CreateDefaultShaderConfig("Frost Crystallize", 4, 2, "#b2e5ffff", 0.05f, 5.0f, 25.0f, 0.6f, 3.5f, 0.03f, 0.95f, 1.3f);
				normalizedShaders["SpawnShader/shadow_void"] = CreateDefaultShaderConfig("Shadow Void Collapse", 5, 3, "#b219ffff", 0.07f, 8.0f, 14.0f, 0.9f, 2.0f, 0.18f, 1.0f, 1.4f);
			}
			foreach (var kvp in normalizedShaders)
			{
				if (kvp.Value is JsonObject sObj)
				{
					sObj.Remove("Hash");
					sObj.Remove("hash");
				}
			}
			metadataRoot["SpawnShader"] = normalizedShaders;

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
						("textures", "Terrain"),
						("vfx", "Spritesheet"),
						("vfx_spritesheets", "Spritesheet"),
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
							var renamedKeys = new List<(string OldKey, string NewKey, JsonNode Value)>();
							foreach (var assetPair in categoryObj)
							{
								string rawKey = assetPair.Key;
								var (pType, pSlug) = TemplateIDHelper.ParseTemplateID(rawKey);
								string cleanKey = (!string.IsNullOrEmpty(pType) && !string.IsNullOrEmpty(pSlug)) ? pSlug : rawKey;
								if (!Path.HasExtension(cleanKey))
								{
									string defaultExt = categoryName switch
									{
										"Animation" => ".ranim",
										"SoundEffect" or "Music" => ".raud",
										"Character" or "Building" or "Prop" or "Item" => ".rmesh",
										_ => ".rtex"
									};
									cleanKey += defaultExt;
								}
								if (!string.Equals(rawKey, cleanKey, StringComparison.OrdinalIgnoreCase) && assetPair.Value != null)
								{
									renamedKeys.Add((rawKey, cleanKey, assetPair.Value.DeepClone()));
								}
							}
							foreach (var (oldK, newK, v) in renamedKeys)
							{
								categoryObj.Remove(oldK);
								if (!categoryObj.ContainsKey(newK))
								{
									categoryObj[newK] = v;
								}
							}

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

			progress?.Report(new MigrationProgressUpdate(Description, 3, totalSteps, "Normalizing terrain.json placed entities to TemplateId format..."));

			string terrainPath = Path.Combine(mapDirectory, "terrain.json");
			if (File.Exists(terrainPath))
			{
				try
				{
					string terrainText = File.ReadAllText(terrainPath);
					var terrainRoot = JsonNode.Parse(terrainText)?.AsObject();
					if (terrainRoot != null)
					{
						var unitOrBuildingLookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
						var propOrResourceLookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
						var decalLookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
						var allTemplatesLookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

						void RegisterInLookup(Dictionary<string, string> categoryLookup, string templateId, string? name, string? modelPath, string? rawLegacyId)
						{
							if (string.IsNullOrWhiteSpace(templateId)) return;
							categoryLookup[templateId] = templateId;
							allTemplatesLookup[templateId] = templateId;

							var (_, pSlug) = TemplateIDHelper.ParseTemplateID(templateId);
							if (!string.IsNullOrEmpty(pSlug))
							{
								if (!categoryLookup.ContainsKey(pSlug)) categoryLookup[pSlug] = templateId;
								if (!allTemplatesLookup.ContainsKey(pSlug)) allTemplatesLookup[pSlug] = templateId;
							}

							if (!string.IsNullOrWhiteSpace(rawLegacyId))
							{
								categoryLookup[rawLegacyId] = templateId;
								allTemplatesLookup[rawLegacyId] = templateId;
								string legSlug = TemplateIDHelper.GenerateSlug(rawLegacyId);
								if (!categoryLookup.ContainsKey(legSlug)) categoryLookup[legSlug] = templateId;
								if (!allTemplatesLookup.ContainsKey(legSlug)) allTemplatesLookup[legSlug] = templateId;
							}

							if (!string.IsNullOrWhiteSpace(name))
							{
								categoryLookup[name] = templateId;
								allTemplatesLookup[name] = templateId;
								string nameSlug = TemplateIDHelper.GenerateSlug(name);
								if (!categoryLookup.ContainsKey(nameSlug)) categoryLookup[nameSlug] = templateId;
								if (!allTemplatesLookup.ContainsKey(nameSlug)) allTemplatesLookup[nameSlug] = templateId;
							}

							if (!string.IsNullOrWhiteSpace(modelPath))
							{
								string fileName = Path.GetFileName(modelPath);
								string fileNoExt = Path.GetFileNameWithoutExtension(modelPath);
								string modelSlug = TemplateIDHelper.GenerateSlug(modelPath);

								if (!categoryLookup.ContainsKey(modelPath)) categoryLookup[modelPath] = templateId;
								if (!categoryLookup.ContainsKey(fileName)) categoryLookup[fileName] = templateId;
								if (!categoryLookup.ContainsKey(fileNoExt)) categoryLookup[fileNoExt] = templateId;
								if (!categoryLookup.ContainsKey(modelSlug)) categoryLookup[modelSlug] = templateId;

								if (!allTemplatesLookup.ContainsKey(modelPath)) allTemplatesLookup[modelPath] = templateId;
								if (!allTemplatesLookup.ContainsKey(fileName)) allTemplatesLookup[fileName] = templateId;
								if (!allTemplatesLookup.ContainsKey(fileNoExt)) allTemplatesLookup[fileNoExt] = templateId;
								if (!allTemplatesLookup.ContainsKey(modelSlug)) allTemplatesLookup[modelSlug] = templateId;
							}
						}

						void PopulateCategoryLookup(Dictionary<string, string> targetLookup, string categoryName)
						{
							if (templatesObj.TryGetPropertyValue(categoryName, out var catNode) && catNode is JsonArray catArr)
							{
								foreach (var item in catArr.OfType<JsonObject>())
								{
									string tId = item["TemplateID"]?.ToString() ?? string.Empty;
									string? name = item["Name"]?.ToString();
									string? model = item["ModelPath"]?.ToString();
									string? rawId = item["UnitId"]?.ToString() ?? item["ObjectID"]?.ToString();
									RegisterInLookup(targetLookup, tId, name, model, rawId);
								}
							}
						}

						PopulateCategoryLookup(unitOrBuildingLookup, "Units");
						PopulateCategoryLookup(unitOrBuildingLookup, "Buildings");
						PopulateCategoryLookup(propOrResourceLookup, "Props");
						PopulateCategoryLookup(propOrResourceLookup, "Resources");

						if (metadataRoot.TryGetPropertyValue("decals", out var decalsNodeFinal) && decalsNodeFinal is JsonObject decalsObjFinal)
						{
							foreach (var kvp in decalsObjFinal)
							{
								string decId = kvp.Key;
								decalLookup[decId] = decId;
								var (_, dSlug) = TemplateIDHelper.ParseTemplateID(decId);
								if (!string.IsNullOrEmpty(dSlug)) decalLookup[dSlug] = decId;
							}
						}

						if (terrainRoot.TryGetPropertyValue("Units", out var terrUnitsNode) && terrUnitsNode is JsonArray terrUnitsArr)
						{
							foreach (var uNode in terrUnitsArr.OfType<JsonObject>())
							{
								string rawId = uNode["TemplateId"]?.ToString()
									?? uNode["TemplateID"]?.ToString()
									?? uNode["UnitId"]?.ToString()
									?? uNode["ObjectID"]?.ToString()
									?? uNode["Id"]?.ToString()
									?? string.Empty;

								uNode.Remove("UnitId");
								uNode.Remove("TemplateID");
								uNode.Remove("ObjectID");
								uNode.Remove("Id");

								string targetTemplateId = string.Empty;
								if (!string.IsNullOrWhiteSpace(rawId))
								{
									if (rawId.Contains('/'))
									{
										targetTemplateId = rawId;
									}
									else if (unitOrBuildingLookup.TryGetValue(rawId, out var matched))
									{
										targetTemplateId = matched;
									}
									else if (propOrResourceLookup.TryGetValue(rawId, out var matchedPropRes))
									{
										targetTemplateId = matchedPropRes;
									}
									else if (allTemplatesLookup.TryGetValue(rawId, out var matchedAll))
									{
										targetTemplateId = matchedAll;
									}
									else
									{
										string rawSlug = TemplateIDHelper.GenerateSlug(rawId);
										if (unitOrBuildingLookup.TryGetValue(rawSlug, out var matchedSlug))
										{
											targetTemplateId = matchedSlug;
										}
										else if (allTemplatesLookup.TryGetValue(rawSlug, out var matchedAllSlug))
										{
											targetTemplateId = matchedAllSlug;
										}
										else
										{
											targetTemplateId = TemplateIDHelper.NormalizeTemplateID("unit", rawId);
										}
									}
								}

								uNode["TemplateId"] = targetTemplateId;
							}
						}

						if (terrainRoot.TryGetPropertyValue("Props", out var terrPropsNode) && terrPropsNode is JsonArray terrPropsArr)
						{
							foreach (var pNode in terrPropsArr.OfType<JsonObject>())
							{
								string rawId = pNode["TemplateId"]?.ToString()
									?? pNode["TemplateID"]?.ToString()
									?? pNode["PropId"]?.ToString()
									?? pNode["UnitId"]?.ToString()
									?? pNode["ObjectID"]?.ToString()
									?? pNode["Id"]?.ToString()
									?? string.Empty;

								pNode.Remove("PropId");
								pNode.Remove("UnitId");
								pNode.Remove("TemplateID");
								pNode.Remove("ObjectID");
								pNode.Remove("Id");

								string targetTemplateId = string.Empty;
								if (!string.IsNullOrWhiteSpace(rawId))
								{
									if (rawId.Contains('/'))
									{
										targetTemplateId = rawId;
									}
									else if (propOrResourceLookup.TryGetValue(rawId, out var matched))
									{
										targetTemplateId = matched;
									}
									else if (unitOrBuildingLookup.TryGetValue(rawId, out var matchedUnitBld))
									{
										targetTemplateId = matchedUnitBld;
									}
									else if (allTemplatesLookup.TryGetValue(rawId, out var matchedAll))
									{
										targetTemplateId = matchedAll;
									}
									else
									{
										string rawSlug = TemplateIDHelper.GenerateSlug(rawId);
										if (propOrResourceLookup.TryGetValue(rawSlug, out var matchedSlug))
										{
											targetTemplateId = matchedSlug;
										}
										else if (allTemplatesLookup.TryGetValue(rawSlug, out var matchedAllSlug))
										{
											targetTemplateId = matchedAllSlug;
										}
										else
										{
											targetTemplateId = TemplateIDHelper.NormalizeTemplateID("prop", rawId);
										}
									}
								}

								pNode["TemplateId"] = targetTemplateId;
							}
						}

						if (terrainRoot.TryGetPropertyValue("Decals", out var terrDecalsNode) && terrDecalsNode is JsonArray terrDecalsArr)
						{
							foreach (var dNode in terrDecalsArr.OfType<JsonObject>())
							{
								string rawId = dNode["TemplateId"]?.ToString()
									?? dNode["TemplateID"]?.ToString()
									?? dNode["DecalId"]?.ToString()
									?? dNode["Id"]?.ToString()
									?? string.Empty;

								dNode.Remove("DecalId");
								dNode.Remove("TemplateID");
								dNode.Remove("Id");

								string targetTemplateId = string.Empty;
								if (!string.IsNullOrWhiteSpace(rawId))
								{
									if (rawId.Contains('/'))
									{
										targetTemplateId = rawId;
									}
									else if (decalLookup.TryGetValue(rawId, out var matched))
									{
										targetTemplateId = matched;
									}
									else
									{
										string rawSlug = TemplateIDHelper.GenerateSlug(rawId);
										if (decalLookup.TryGetValue(rawSlug, out var matchedSlug))
										{
											targetTemplateId = matchedSlug;
										}
										else
										{
											targetTemplateId = TemplateIDHelper.NormalizeTemplateID("decal", rawId);
										}
									}
								}

								dNode["TemplateId"] = targetTemplateId;
							}
						}

						MapJsonFormatter.SaveFormattedJson(terrainPath, terrainRoot);
					}
				}
				catch (Exception ex)
				{
					GD.PrintErr($"[Migration_0_0_4] Failed to migrate terrain.json: {ex.Message}");
				}
			}

			progress?.Report(new MigrationProgressUpdate(Description, 4, totalSteps, "Updating map build number and saving metadata.json..."));

			metadataRoot["GameBuildNumber"] = ToVersion;

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
		var categoryNames = new[] { "Units", "Buildings", "Resources", "Props" };
		foreach (var cat in categoryNames)
		{
			if (templatesObj.TryGetPropertyValue(cat, out var catNode) && catNode is JsonArray arr)
			{
				bool found = arr.OfType<JsonObject>().Any(item =>
					string.Equals(item["ModelPath"]?.ToString(), fileName, StringComparison.OrdinalIgnoreCase) ||
					string.Equals(TemplateIDHelper.GenerateSlug(item["ModelPath"]?.ToString() ?? ""), slug, StringComparison.OrdinalIgnoreCase) ||
					string.Equals(TemplateIDHelper.GenerateSlug(item["TemplateID"]?.ToString() ?? ""), slug, StringComparison.OrdinalIgnoreCase));
				if (found) return true;
			}
		}
		return false;
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
							["Scale"] = 2.75f,
							["PathingType"] = 255,
							["DespillPlayerColor"] = false,
							["NormalizeLuminance"] = true,
							["IgnorePlayerColor"] = true
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
					break;
				}

			case "terrain" or "textures":
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
					break;
				}

			case "spritesheet" or "spritesheets" or "vfx":
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

			case "shader" or "shaders" or "gdshader":
				{
					string gdshaderTemplateId = TemplateIDHelper.NormalizeTemplateID("gdshader", slug);
					if (!metadataRoot.ContainsKey("gdshader") || metadataRoot["gdshader"] is not JsonObject)
					{
						metadataRoot["gdshader"] = new JsonObject();
					}
					var gdshadersObj = metadataRoot["gdshader"]!.AsObject();
					bool exists = gdshadersObj.Any(kvp => string.Equals(kvp.Key, gdshaderTemplateId, StringComparison.OrdinalIgnoreCase));
					if (!exists)
					{
						gdshadersObj[gdshaderTemplateId] = new JsonObject
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
