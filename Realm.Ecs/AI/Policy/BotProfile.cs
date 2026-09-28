using Realm.Ecs.AI.Genres;
using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Realm.Ecs.AI.Policy;

public class BotProfile
{
	public string SchemaVersion { get; set; } = "1.0.0";
	public string MapName { get; set; } = "GenericMap";
	public string Genre { get; set; } = "rts";
	public string GameBuildNumber { get; set; } = "0.0.1";
	public string ProfileId { get; set; } = "Default";
	public string Author { get; set; } = "AutoTrainer";
	public float DecisionIntervalSeconds { get; set; } = 1.0f;
	public float AggressionMultiplier { get; set; } = 1.0f;
	public float ActionTemperature { get; set; } = 0.0f;
	public float[] Weights { get; set; } = Array.Empty<float>();
	public float FitnessScore { get; set; } = 0.0f;
	public int TrainedEpochs { get; set; } = 0;
	public Dictionary<string, string> CustomParameters { get; set; } = new();

	public string ToJson()
	{
		return JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
	}

	public static BotProfile FromJson(string json)
	{
		return JsonSerializer.Deserialize<BotProfile>(json) ?? new BotProfile();
	}

	public static BotProfile CreateDefault(string mapName = "GenericMap", string genre = "rts")
	{
		var provider = AiGenreRegistry.Get(genre);
		var profile = provider.CreateDefaultProfile(mapName);
		profile.Genre = genre;
		return profile;
	}
}
