using System.Text.Json;
using Realm.Ecs.AI.Genres;
using Realm.Ecs.AI.Policy;
using Realm.Ecs.AI.Simulation;
using Realm.Ecs.AI.Training;
using Realm.Shared;

namespace Realm.Tools.MatchRunner;

public class TrainingRunResult
{
	public BotProfile Profile { get; init; } = new();
	public IReadOnlyList<float> GenerationBestFitness { get; init; } = Array.Empty<float>();
}

public static class MatchTrainer
{
	private const float WinWeight = 0.9f;
	private const float DamageShareWeight = 0.1f;

	public static TrainingRunResult Train(MatchRunOptions options)
	{
		IAiGenreProvider provider = AiGenreRegistry.Get(options.TrainGenre);
		string mapName = ReadMapName(options.MapPath);

		var trainer = new SelfPlayTrainer();
		BotProfile profile = trainer.TrainAgainstEvaluator(
			mapName,
			options.TrainGenerations,
			options.TrainPopulation,
			options.TrainEvaluations,
			provider.FeatureCount,
			provider.GenreName,
			(weights, seed) => Evaluate(weights, options, seed),
			options.TrainSeed,
			RealmVersion.GameBuildNumber,
			options.TrainInitialWeights);

		return new TrainingRunResult
		{
			Profile = profile,
			GenerationBestFitness = trainer.GenerationBestFitness.ToArray()
		};
	}

	public static string BuildManifest(MatchRunOptions options, TrainingRunResult result, string profilePath)
	{
		IAiGenreProvider provider = AiGenreRegistry.Get(options.TrainGenre);
		var manifest = new
		{
			schemaVersion = "1.0.0",
			mapName = result.Profile.MapName,
			mapPath = Path.GetFullPath(options.MapPath),
			gameBuildNumber = result.Profile.GameBuildNumber,
			genre = provider.GenreName,
			featureCount = provider.FeatureCount,
			generations = options.TrainGenerations,
			populationSize = options.TrainPopulation,
			evaluationsPerCandidate = options.TrainEvaluations,
			trainingMatches = options.TrainGenerations * options.TrainPopulation * options.TrainEvaluations,
			baseSeed = options.TrainSeed,
			initialWeights = options.TrainInitialWeights,
			pairedEvaluationSeeds = true,
			fitnessShaping = new { win = WinWeight, damageShare = DamageShareWeight },
			maximumSecondsPerMatch = options.MaxSeconds,
			seats = options.Seats,
			opponentPolicy = options.Policy,
			profilePath = Path.GetFullPath(profilePath),
			fitnessScore = result.Profile.FitnessScore,
			generationBestFitness = result.GenerationBestFitness,
			createdUtc = DateTime.UtcNow.ToString("O")
		};

		return JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
	}

	private static float Evaluate(float[] weights, MatchRunOptions options, int seed)
	{
		MatchOutcome outcome = MatchPlayer.Run(options, seed, weights);

		float winScore = outcome.WinnerSeat == 1 ? 1.0f : outcome.WinnerSeat < 0 ? 0.5f : 0.0f;
		MatchSeatOutcome? trainedSeat = FindSeat(outcome, 1);
		MatchSeatOutcome? opponentSeat = FindSeat(outcome, 2);
		if (trainedSeat == null || opponentSeat == null) return winScore;

		float damageTotal = trainedSeat.DamageDealt + opponentSeat.DamageDealt;
		if (damageTotal <= 0f) return winScore;

		float damageShare = trainedSeat.DamageDealt / damageTotal;
		return WinWeight * winScore + DamageShareWeight * damageShare;
	}

	private static MatchSeatOutcome? FindSeat(MatchOutcome outcome, int seat)
	{
		for (int index = 0; index < outcome.PerSeat.Length; index++)
		{
			if (outcome.PerSeat[index].Seat == seat) return outcome.PerSeat[index];
		}

		return null;
	}

	private static string ReadMapName(string mapPath)
	{
		string trimmedPath = mapPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		string metadataPath = Path.Combine(trimmedPath, "metadata.json");
		if (!File.Exists(metadataPath))
		{
			return Path.GetFileName(trimmedPath);
		}

		using JsonDocument document = JsonDocument.Parse(File.ReadAllText(metadataPath));
		if (document.RootElement.TryGetProperty("MapProperties", out JsonElement properties)
			&& properties.TryGetProperty("MapName", out JsonElement mapName)
			&& mapName.ValueKind == JsonValueKind.String)
		{
			return mapName.GetString() ?? "Map";
		}

		return "Map";
	}
}
