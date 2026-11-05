using Realm.Ecs.AI.Genres;
using Realm.Ecs.AI.Policy;
using Realm.Ecs.AI.Simulation;

namespace Realm.Ecs.AI.Training;

public class SelfPlayTrainer
{
	public List<float> GenerationBestFitness { get; } = new();

	public BotProfile TrainSelfPlay(
		string mapName,
		int generations = 5,
		int populationSize = 8,
		int matchesPerEvaluation = 4,
		WinConditionEvaluator? winConditionEvaluator = null,
		MapSimulationInitializer? customInitializer = null,
		int maxTicks = 400,
		IAiGenreProvider? genreProvider = null,
		int seed = 0,
		string gameBuildNumber = "")
	{
		var provider = genreProvider ?? new StandardRtsGenreProvider();
		GenerationBestFitness.Clear();
		int baseSeed = RandomStreams.CreateBaseSeed(seed);
		var population = InitializePopulation(populationSize, provider.FeatureCount, RandomStreams.Derive(baseSeed, 0));

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
				customInitializer,
				baseSeed,
				gen
			);

			UpdateBestGenome(population, fitnessScores, ref bestGenome, ref maxFitness);
			GenerationBestFitness.Add(maxFitness);
			UpdateHistoricalPool(historicalPool, bestGenome);
			population = CreateNextGeneration(populationSize, population, bestGenome, RandomStreams.Derive(baseSeed, gen + 100000));
		}

		return CreateBotProfile(mapName, provider.GenreName, bestGenome, maxFitness, generations, gameBuildNumber);
	}

	public BotProfile TrainAgainstEvaluator(
		string mapName,
		int generations,
		int populationSize,
		int matchesPerEvaluation,
		int featureCount,
		string genreName,
		Func<float[], int, float> evaluateCandidate,
		int seed = 0,
		string gameBuildNumber = "",
		float[]? initialWeights = null)
	{
		int baseSeed = RandomStreams.CreateBaseSeed(seed);
		GenerationBestFitness.Clear();
		var population = InitializePopulation(populationSize, featureCount, RandomStreams.Derive(baseSeed, 0), initialWeights);

		float[] bestGenome = population[0];
		float maxFitness = float.MinValue;

		for (int generation = 0; generation < generations; generation++)
		{
			var fitnessScores = new float[population.Count];
			int[] evaluationSeeds = DeriveEvaluationSeeds(baseSeed, generation, matchesPerEvaluation);
			for (int candidateIndex = 0; candidateIndex < population.Count; candidateIndex++)
			{
				float totalScore = 0.0f;
				for (int matchIndex = 0; matchIndex < evaluationSeeds.Length; matchIndex++)
				{
					totalScore += evaluateCandidate(population[candidateIndex], evaluationSeeds[matchIndex]);
				}

				fitnessScores[candidateIndex] = totalScore / evaluationSeeds.Length;
			}

			UpdateBestGenome(population, fitnessScores, ref bestGenome, ref maxFitness);
			GenerationBestFitness.Add(maxFitness);
			population = CreateNextGeneration(populationSize, population, bestGenome, RandomStreams.Derive(baseSeed, generation + 100000));
		}

		return CreateBotProfile(mapName, genreName, bestGenome, maxFitness, generations, gameBuildNumber);
	}

	private static List<float[]> InitializePopulation(int populationSize, int featureCount, SeededRandom random, float[]? initialWeights = null)
	{
		bool startFromPrior = initialWeights != null && initialWeights.Length == featureCount;
		var population = new List<float[]>();
		for (int i = 0; i < populationSize; i++)
		{
			var weights = new float[featureCount];
			for (int f = 0; f < featureCount; f++)
			{
				if (!startFromPrior)
				{
					weights[f] = (float)(random.NextDouble() * 2.0 - 1.0);
					continue;
				}

				float jitter = i == 0 ? 0.0f : (float)(random.NextDouble() * 0.5 - 0.25);
				weights[f] = Math.Clamp(initialWeights![f] + jitter, -2.0f, 2.0f);
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
		MapSimulationInitializer? customInitializer,
		int baseSeed,
		int generation)
	{
		var fitnessScores = new float[population.Count];
		Parallel.For(0, population.Count, popIdx =>
		{
			SeededRandom random = RandomStreams.Derive(baseSeed, generation * 1000 + popIdx + 1);
			fitnessScores[popIdx] = EvaluateCandidate(
				population[popIdx],
				historicalPool,
				matchesPerEvaluation,
				genreName,
				maxTicks,
				winConditionEvaluator,
				customInitializer,
				random
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
		MapSimulationInitializer? customInitializer,
		SeededRandom random)
	{
		float totalScore = 0.0f;
		for (int m = 0; m < matchesPerEvaluation; m++)
		{
			float[] opponent = GetRandomOpponent(historicalPool, random);
			totalScore += RunMatchAndGetScore(
				candidate, opponent, genreName, maxTicks, winConditionEvaluator, customInitializer, random);
		}

		return totalScore / matchesPerEvaluation;
	}

	private static float[] GetRandomOpponent(List<float[]> historicalPool, SeededRandom random)
	{
		lock (historicalPool)
		{
			return historicalPool[random.Next(historicalPool.Count)];
		}
	}

	private static float RunMatchAndGetScore(
		float[] candidate,
		float[] opponent,
		string genreName,
		int maxTicks,
		WinConditionEvaluator? winConditionEvaluator,
		MapSimulationInitializer? customInitializer,
		SeededRandom random)
	{
		var matchProvider = AiGenreRegistry.Get(genreName);
		var runner = new HeadlessSimulationRunner(matchProvider, random);
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

	private static int[] DeriveEvaluationSeeds(int baseSeed, int generation, int matchesPerEvaluation)
	{
		var seeds = new int[matchesPerEvaluation];
		SeededRandom random = RandomStreams.Derive(baseSeed, generation * 100000 + 7);
		for (int matchIndex = 0; matchIndex < seeds.Length; matchIndex++)
		{
			seeds[matchIndex] = random.Next(1, int.MaxValue);
		}

		return seeds;
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

	private static List<float[]> CreateNextGeneration(int populationSize, List<float[]> currentPopulation, float[] bestGenome, SeededRandom random)
	{
		var newPopulation = new List<float[]>
		{
			(float[])bestGenome.Clone()
		};

		while (newPopulation.Count < populationSize)
		{
			float[] parent = currentPopulation[random.Next(populationSize)];
			float[] child = MutateGenome(parent, random);
			newPopulation.Add(child);
		}

		return newPopulation;
	}

	private static BotProfile CreateBotProfile(string mapName, string genreName, float[] bestGenome, float maxFitness, int trainedEpochs, string gameBuildNumber)
	{
		var profile = new BotProfile
		{
			SchemaVersion = "1.0.0",
			MapName = mapName,
			Genre = genreName,
			ProfileId = $"{mapName}_AutoTrained",
			Author = "SelfPlayTrainer",
			DecisionIntervalSeconds = 1.0f,
			AggressionMultiplier = 1.0f,
			ActionTemperature = 0.05f,
			Weights = bestGenome,
			FitnessScore = maxFitness,
			TrainedEpochs = trainedEpochs
		};

		if (gameBuildNumber.Length > 0) profile.GameBuildNumber = gameBuildNumber;
		return profile;
	}

	private static float[] MutateGenome(float[] parent, SeededRandom random)
	{
		var child = new float[parent.Length];
		for (int i = 0; i < parent.Length; i++)
		{
			float mutation = (float)(random.NextDouble() * 0.4 - 0.2);
			child[i] = Math.Clamp(parent[i] + mutation, -2.0f, 2.0f);
		}

		return child;
	}
}
