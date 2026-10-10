using Realm.Ecs.AI.Simulation;

namespace Realm.Ecs.AI.Balancer;

public class StrategicAutoBalancer
{
	private static readonly Random Random = new Random();

	public BalanceGenome OptimizeBalance(BalanceGenome initialGenome, float[] agentWeights, int generations = 5, int matchesPerGeneration = 10)
	{
		BalanceGenome currentBest = initialGenome;
		float bestFitness = EvaluateFitness(currentBest, agentWeights, matchesPerGeneration);

		for (int gen = 0; gen < generations; gen++)
		{
			BalanceGenome candidate = MutateGenome(currentBest);
			float candidateFitness = EvaluateFitness(candidate, agentWeights, matchesPerGeneration);

			if (candidateFitness > bestFitness)
			{
				bestFitness = candidateFitness;
				currentBest = candidate;
			}
		}

		return currentBest;
	}

	public float EvaluateFitness(BalanceGenome genome, float[] agentWeights, int matchCount)
	{
		int p0Wins = 0;
		int p1Wins = 0;
		int draws = 0;
		float totalDuration = 0.0f;

		Parallel.For(0, matchCount, i =>
		{
			var runner = new HeadlessSimulationRunner();
			var result = runner.RunMatch(agentWeights, agentWeights, maxTicks: 500);

			lock (runner)
			{
				if (result.WinnerPlayerIndex == 0) p0Wins++;
				else if (result.WinnerPlayerIndex == 1) p1Wins++;
				else draws++;

				totalDuration += result.MatchDurationSeconds;
			}
		});

		float winRate0 = (float)p0Wins / matchCount;
		float winRateParityPenalty = MathF.Abs(winRate0 - 0.5f) * 2.0f; // 0 is ideal (50%), 1 is worst

		float avgDuration = totalDuration / matchCount;
		float targetDuration = 30.0f; // ideal match pacing
		float pacingPenalty = MathF.Min(1.0f, MathF.Abs(avgDuration - targetDuration) / targetDuration);

		float winRateParityScore = 1.0f - winRateParityPenalty;
		float pacingScore = 1.0f - pacingPenalty;

		float fitness = (winRateParityScore * 0.6f) + (pacingScore * 0.4f);
		return fitness;
	}

	private BalanceGenome MutateGenome(BalanceGenome parent)
	{
		var child = new BalanceGenome();
		float[] vec = parent.ToVector();
		for (int i = 0; i < vec.Length; i++)
		{
			float mutation = (float)(Random.NextDouble() * 0.2 - 0.1) * vec[i];
			vec[i] += mutation;
		}
		child.FromVector(vec);
		return child;
	}
}
