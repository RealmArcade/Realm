using System.Globalization;

namespace Realm.Tools.MatchRunner;

public class PublishVerdict
{
	public bool Publish { get; init; }
	public IReadOnlyList<string> Blockers { get; init; } = Array.Empty<string>();
	public double WinRate { get; init; }
	public double IntervalLow { get; init; }
	public double IntervalHigh { get; init; }
	public double PValue { get; init; }
	public int OverlappingTrainingSeeds { get; init; }
}

public static class PublishGate
{
	public const double MinimumWinRate = 0.55;
	public const double MaximumPValue = 0.01;

	private const double Z95 = 1.959963985;

	public static PublishVerdict Judge(PublishDuelResult duel, PublishSmokeResult smoke, int mapCandidateFeatures, IReadOnlyCollection<int> trainingSeeds)
	{
		var blockers = new List<string>();

		IReadOnlyList<int> overlappingSeeds = OverlappingSeeds(duel.SeedFirst, duel.SeedLast, trainingSeeds);
		if (overlappingSeeds.Count > 0)
		{
			blockers.Add($"la ventana del duelo {duel.SeedFirst}-{duel.SeedLast} repite {overlappingSeeds.Count} semilla(s) que el entrenamiento ya jugó (la primera, {overlappingSeeds[0]}), y la compuerta sólo puede medir un duelo sobre una ventana disjunta de este entrenamiento");
		}

		if (duel.Failures > 0) blockers.Add($"{duel.Failures} partida(s) del duelo no se pudieron jugar");
		if (duel.Matches == 0) blockers.Add("el duelo no jugó ninguna partida");

		int decisive = duel.Wins + duel.Losses;
		double pValue = UpperTail(duel.Wins, decisive);
		double winRate = duel.WinRate;
		(double low, double high) = Wilson(duel.Wins + 0.5 * duel.Draws, duel.Matches);

		if (duel.Matches > 0 && winRate < MinimumWinRate)
		{
			blockers.Add($"la tasa del duelo {FormatPercent(winRate)} no alcanza el {FormatPercent(MinimumWinRate)}");
		}

		if (decisive > 0 && pValue >= MaximumPValue)
		{
			blockers.Add($"el p unilateral del duelo {FormatPValue(pValue)} no queda por debajo de {MaximumPValue.ToString("0.00", CultureInfo.InvariantCulture)}");
		}

		if (smoke.Failures > 0) blockers.Add($"{smoke.Failures} partida(s) del humo no se pudieron jugar");
		if (smoke.Matches == 0) blockers.Add("el humo no jugó ninguna partida");
		if (smoke.WarningFound) blockers.Add("el humo emitió el aviso de desajuste de pesos de candidato");

		if (smoke.Matches > 0)
		{
			if (mapCandidateFeatures > 0 && smoke.RegisteredFeatures != mapCandidateFeatures)
			{
				blockers.Add($"el mapa registró {smoke.RegisteredFeatures} rasgo(s) de candidato y el perfil lleva {mapCandidateFeatures}");
			}

			if (smoke.ProfileMapWeights != mapCandidateFeatures)
			{
				blockers.Add($"el perfil lleva {smoke.ProfileMapWeights} peso(s) de candidato en vez de {mapCandidateFeatures}");
			}
		}

		return new PublishVerdict
		{
			Publish = blockers.Count == 0,
			Blockers = blockers,
			WinRate = winRate,
			IntervalLow = low,
			IntervalHigh = high,
			PValue = pValue,
			OverlappingTrainingSeeds = overlappingSeeds.Count
		};
	}

	public static IReadOnlyList<int> OverlappingSeeds(int firstSeed, int lastSeed, IReadOnlyCollection<int> trainingSeeds)
	{
		var overlapping = new List<int>();
		if (trainingSeeds.Count == 0 || lastSeed < firstSeed) return overlapping;

		foreach (int seed in trainingSeeds)
		{
			if (seed >= firstSeed && seed <= lastSeed) overlapping.Add(seed);
		}

		overlapping.Sort();
		return overlapping;
	}

	public static (double Low, double High) Wilson(double successes, int trials)
	{
		if (trials <= 0) return (0.0, 1.0);

		double proportion = successes / trials;
		double denominator = 1.0 + Z95 * Z95 / trials;
		double center = (proportion + Z95 * Z95 / (2.0 * trials)) / denominator;
		double margin = Z95 * Math.Sqrt(proportion * (1.0 - proportion) / trials + Z95 * Z95 / (4.0 * trials * trials)) / denominator;
		return (Math.Max(0.0, center - margin), Math.Min(1.0, center + margin));
	}

	public static double UpperTail(int successes, int trials)
	{
		if (trials <= 0 || successes <= 0) return 1.0;
		if (successes > trials) return 0.0;

		var logFactorial = new double[trials + 1];
		for (int value = 1; value <= trials; value++) logFactorial[value] = logFactorial[value - 1] + Math.Log(value);

		double logHalf = -Math.Log(2.0);
		var logs = new double[trials - successes + 1];
		double maximum = double.NegativeInfinity;
		for (int wins = successes; wins <= trials; wins++)
		{
			double log = logFactorial[trials] - logFactorial[wins] - logFactorial[trials - wins] + trials * logHalf;
			logs[wins - successes] = log;
			if (log > maximum) maximum = log;
		}

		double sum = 0.0;
		foreach (double log in logs) sum += Math.Exp(log - maximum);
		return Math.Min(1.0, Math.Exp(maximum) * sum);
	}

	public static string FormatPercent(double value)
	{
		return (value * 100.0).ToString("F2", CultureInfo.InvariantCulture) + "%";
	}

	public static string FormatPValue(double value)
	{
		return value < 0.0001 ? "< 0.0001" : value.ToString("F4", CultureInfo.InvariantCulture);
	}
}
