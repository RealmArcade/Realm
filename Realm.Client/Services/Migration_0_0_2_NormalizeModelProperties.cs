using System;
using System.IO;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace Realm.Client.Services;

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