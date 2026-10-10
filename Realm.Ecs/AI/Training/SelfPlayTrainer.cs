using Realm.Ecs.AI.Genres;
using Realm.Ecs.AI.Policy;
using Realm.Ecs.AI.Simulation;

namespace Realm.Ecs.AI.Training;

public class SelfPlayTrainer
{
	private static readonly Random Random = new Random();

	public BotProfile TrainSelfPlay(
		string mapName,
		int generations = 5,
		int populationSize = 8,
		int matchesPerEvaluation = 4,
		WinConditionEvaluator? winConditionEvaluator = null,
		MapSimulationInitializer? customInitializer = null,
		int maxTicks = 400,
		IAiGenreProvider? genreProvider = null)
	{
		var provider = genreProvider ?? new StandardRtsGenreProvider();
		var population = InitializePopulation(populationSize, provider.FeatureCount);

		var historicalPool = new List<float[]> { population[0] };

		float[] bestGenome = population[0];
		float maxFitness = float.MinValue;

		for (int gen = 0; gen < generations; gen++)
		{
			var fitnessScores = EvaluatePopulation(
				population,
				historicalPool,
				matchesPerEvaluation,
				provider.GenreName,
				maxTicks,
				winConditionEvaluator,
				customInitializer
			);

			UpdateBestGenome(population, fitnessScores, ref bestGenome, ref maxFitness);
			UpdateHistoricalPool(historicalPool, bestGenome);
			population = CreateNextGeneration(populationSize, population, bestGenome);
		}

		return CreateBotProfile(mapName, provider.GenreName, bestGenome, maxFitness, generations);
	}

	private static List<float[]> InitializePopulation(int populationSize, int featureCount)
	{
		var population = new List<float[]>();
		for (int i = 0; i < populationSize; i++)
		{
			var weights = new float[featureCount];
			for (int f = 0; f < featureCount; f++)
			{
				weights[f] = (float)(Random.NextDouble() * 2.0 - 1.0);
			}
			population.Add(weights);
		}
		return population;
	}

	private static float[] EvaluatePopulation(
		List<float[]> population,
		List<float[]> historicalPool,
		int matchesPerEvaluation,
		string genreName,
		int maxTicks,
		WinConditionEvaluator? winConditionEvaluator,
		MapSimulationInitializer? customInitializer)
	{
		var fitnessScores = new float[population.Count];
		Parallel.For(0, population.Count, popIdx =>
		{
			fitnessScores[popIdx] = EvaluateCandidate(
				population[popIdx],
				historicalPool,
				matchesPerEvaluation,
				genreName,
				maxTicks,
				winConditionEvaluator,
				customInitializer
			);
		});
		return fitnessScores;
	}

	private static float EvaluateCandidate(
		float[] candidate,
		List<float[]> historicalPool,
		int matchesPerEvaluation,
		string genreName,
		int maxTicks,
		WinConditionEvaluator? winConditionEvaluator,
		MapSimulationInitializer? customInitializer)
	{
		float totalScore = 0.0f;
		for (int m = 0; m < matchesPerEvaluation; m++)
		{
			float[] opponent = GetRandomOpponent(historicalPool);
			totalScore += RunMatchAndGetScore(
				candidate, opponent, genreName, maxTicks, winConditionEvaluator, customInitializer);
		}
		return totalScore / matchesPerEvaluation;
	}

	private static float[] GetRandomOpponent(List<float[]> historicalPool)
	{
		lock (historicalPool)
		{
			return historicalPool[Random.Next(historicalPool.Count)];
		}
	}

	private static float RunMatchAndGetScore(
		float[] candidate,
		float[] opponent,
		string genreName,
		int maxTicks,
		WinConditionEvaluator? winConditionEvaluator,
		MapSimulationInitializer? customInitializer)
	{
		var matchProvider = AiGenreRegistry.Get(genreName);
		var runner = new HeadlessSimulationRunner(matchProvider);
		var result = runner.RunMatch(
			candidate,
			opponent,
			maxTicks: maxTicks,
			winConditionEvaluator: winConditionEvaluator,
			customInitializer: customInitializer,
			p0Temperature: 0.05f,
			p1Temperature: 0.05f,
			genreProvider: matchProvider
		);

		if (result.WinnerPlayerIndex == 0) return 1.0f;
		if (result.WinnerPlayerIndex == -1) return 0.5f;
		return 0.0f;
	}

	private static void UpdateBestGenome(List<float[]> population, float[] fitnessScores, ref float[] bestGenome, ref float maxFitness)
	{
		for (int i = 0; i < population.Count; i++)
		{
			if (fitnessScores[i] > maxFitness)
			{
				maxFitness = fitnessScores[i];
				bestGenome = (float[])population[i].Clone();
			}
		}
	}

	private static void UpdateHistoricalPool(List<float[]> historicalPool, float[] bestGenome)
	{
		lock (historicalPool)
		{
			historicalPool.Add((float[])bestGenome.Clone());
			if (historicalPool.Count > 10)
			{
				historicalPool.RemoveAt(0);
			}
		}
	}

	private static List<float[]> CreateNextGeneration(int populationSize, List<float[]> currentPopulation, float[] bestGenome)
	{
		var newPopulation = new List<float[]>
		{
			(float[])bestGenome.Clone()
		};

		while (newPopulation.Count < populationSize)
		{
			float[] parent = currentPopulation[Random.Next(populationSize)];
			float[] child = MutateGenome(parent);
			newPopulation.Add(child);
		}

		return newPopulation;
	}

	private static BotProfile CreateBotProfile(string mapName, string genreName, float[] bestGenome, float maxFitness, int trainedEpochs)
	{
		return new BotProfile
		{
			SchemaVersion = "1.0.0",
			MapName = mapName,
			Genre = genreName,
			GameBuildNumber = "0.0.1",
			ProfileId = $"{mapName}_AutoTrained",
			Author = "SelfPlayTrainer",
			DecisionIntervalSeconds = 1.0f,
			AggressionMultiplier = 1.0f,
			ActionTemperature = 0.05f,
			Weights = bestGenome,
			FitnessScore = maxFitness,
			TrainedEpochs = trainedEpochs
		};
	}

	private static float[] MutateGenome(float[] parent)
	{
		var child = new float[parent.Length];
		for (int i = 0; i < parent.Length; i++)
		{
			float mutation = (float)(Random.NextDouble() * 0.4 - 0.2);
			child[i] = Math.Clamp(parent[i] + mutation, -2.0f, 2.0f);
		}
		return child;
	}
}
