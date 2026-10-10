using Realm.Shared.Distribution;
using System;
using System.IO;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace Realm.Client.Services;

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