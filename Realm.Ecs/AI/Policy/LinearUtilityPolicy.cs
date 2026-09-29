using Realm.Ecs.AI.Affordances;

namespace Realm.Ecs.AI.Policy;

public class LinearUtilityPolicy
{
	private static readonly Random Random = new Random();

	public GenericAffordance? SelectAction(IReadOnlyList<GenericAffordance> affordances, float[] weights, float epsilon = 0.0f, float temperature = 0.0f, float aggressionMultiplier = 1.0f)
	{
		if (affordances == null || affordances.Count == 0 || weights == null || weights.Length == 0)
		{
			return null;
		}

		int count = affordances.Count;
		if (epsilon > 0.0f && Random.NextDouble() < epsilon)
		{
			return affordances[Random.Next(count)];
		}

		if (temperature <= 0.0001f)
		{
			int bestIdx = 0;
			float highestScore = ComputeUtility(affordances[0].FeatureVector, weights, aggressionMultiplier);

			for (int i = 1; i < count; i++)
			{
				float score = ComputeUtility(affordances[i].FeatureVector, weights, aggressionMultiplier);
				if (score > highestScore)
				{
					highestScore = score;
					bestIdx = i;
				}
			}

			return affordances[bestIdx];
		}

		Span<float> scores = count <= 128 ? stackalloc float[count] : new float[count];
		float maxScore = float.MinValue;

		for (int i = 0; i < count; i++)
		{
			float score = ComputeUtility(affordances[i].FeatureVector, weights, aggressionMultiplier);
			scores[i] = score;
			if (score > maxScore)
			{
				maxScore = score;
			}
		}

		float sumExp = 0.0f;
		Span<float> expScores = count <= 128 ? stackalloc float[count] : new float[count];
		float invTemperature = 1.0f / MathF.Max(0.01f, temperature);

		for (int i = 0; i < count; i++)
		{
			expScores[i] = MathF.Exp((scores[i] - maxScore) * invTemperature);
			sumExp += expScores[i];
		}

		if (sumExp <= 0.0001f)
		{
			return affordances[0];
		}

		float randVal = (float)Random.NextDouble() * sumExp;
		float cumulative = 0.0f;

		for (int i = 0; i < count; i++)
		{
			cumulative += expScores[i];
			if (randVal <= cumulative)
			{
				return affordances[i];
			}
		}

		return affordances[count - 1];
	}

	public float ComputeUtility(float[] featureVector, float[] weights, float aggressionMultiplier = 1.0f)
	{
		if (featureVector == null || weights == null)
		{
			return 0.0f;
		}

		float dotProduct = 0.0f;
		int len = Math.Min(featureVector.Length, weights.Length);

		for (int i = 0; i < len; i++)
		{
			float weight = weights[i];
			if (i == 1 && aggressionMultiplier != 1.0f)
			{
				weight *= aggressionMultiplier;
			}
			dotProduct += featureVector[i] * weight;
		}

		return dotProduct;
	}
}
