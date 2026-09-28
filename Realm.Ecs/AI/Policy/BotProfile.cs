using System.Text.Json;

namespace Realm.Ecs.AI.Policy;

public class BotProfile
{
	public string SchemaVersion { get; set; } = "1.0.0";
	public string MapName { get; set; } = "GenericMap";
	public string GameBuildNumber { get; set; } = "0.0.1";
	public string ProfileId { get; set; } = "Default";
	public string Author { get; set; } = "AutoTrainer";
	public float DecisionIntervalSeconds { get; set; } = 1.0f;
	public float AggressionMultiplier { get; set; } = 1.0f;
	public float ActionTemperature { get; set; } = 0.0f;
	public float[] Weights { get; set; } = Array.Empty<float>();
	public float FitnessScore { get; set; } = 0.0f;
	public int TrainedEpochs { get; set; } = 0;

	public string ToJson()
	{
		return JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
	}

	public static BotProfile FromJson(string json)
	{
		return JsonSerializer.Deserialize<BotProfile>(json) ?? new BotProfile();
	}

	public static BotProfile CreateDefault(string mapName = "GenericMap")
	{
		return new BotProfile
		{
			MapName = mapName,
			Weights = new float[]
			{
				-0.5f,
				0.8f,
				0.2f,
				0.5f,
				0.7f,
				0.9f,
				0.4f,
				0.6f
			}
		};
	}
}
