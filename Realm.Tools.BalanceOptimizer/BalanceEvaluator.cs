using System.Globalization;
using Realm.Ecs.AI.Simulation;
using Realm.Tools.MatchRunner;

namespace Realm.Tools.BalanceOptimizer;

public class BalanceHeroResult
{
	public string HeroId { get; init; } = string.Empty;
	public int Matches { get; init; }
	public double Wins { get; init; }

	public double WinRate => Matches == 0 ? 0.0 : Wins / Matches;

	public double Deviation => Math.Abs(WinRate - 0.5);
}

public class BalanceEvaluation
{
	public double Fitness { get; init; } = -1.0;
	public double MaximumDeviation { get; init; } = 0.5;
	public string WorstHeroId { get; init; } = string.Empty;
	public double SkillGuardWinRate { get; init; }
	public int SkillGuardMatches { get; init; }
	public List<BalanceHeroResult> Heroes { get; init; } = new();
}

public class BalanceEvaluator
{
	private readonly string _mapPath;
	private readonly float _maximumSeconds;
	private readonly int _parallel;
	private readonly object _guardLock = new();

	public BalanceEvaluator(string mapPath, float maximumSeconds, int parallel)
	{
		_mapPath = Path.GetFullPath(mapPath);
		_maximumSeconds = maximumSeconds;
		_parallel = Math.Max(1, parallel);
	}

	public BalanceEvaluation Evaluate(
		IReadOnlyList<string> heroIds,
		int seedsPerHero,
		int baseSeed,
		string profilePath,
		int skillGuardSeeds)
	{
		var heroes = new BalanceHeroResult[heroIds.Count];

		Parallel.For(0, heroIds.Count, new ParallelOptions { MaxDegreeOfParallelism = _parallel }, heroIndex =>
		{
			string heroId = heroIds[heroIndex];
			double wins = 0.0;
			for (int seedIndex = 0; seedIndex < seedsPerHero; seedIndex++)
			{
				int seed = DeriveSeed(baseSeed, heroIndex * 4096 + seedIndex + 1);
				MatchRunOptions options = BuildOptions($"fixed-once:{heroId}", "uniform");
				MatchOutcome outcome = MatchPlayer.Run(options, seed);
				wins += SeatScore(outcome, 1);
			}

			heroes[heroIndex] = new BalanceHeroResult { HeroId = heroId, Matches = seedsPerHero, Wins = wins };
		});

		double guardWins = 0.0;
		if (skillGuardSeeds > 0)
		{
			Parallel.For(0, skillGuardSeeds, new ParallelOptions { MaxDegreeOfParallelism = _parallel }, seedIndex =>
			{
				int seed = DeriveSeed(baseSeed, 1_000_000 + seedIndex);
				MatchRunOptions options = BuildOptions($"profile:{profilePath}", "uniform");
				MatchOutcome outcome = MatchPlayer.Run(options, seed);
				double score = SeatScore(outcome, 1);
				lock (_guardLock) guardWins += score;
			});
		}

		double maximumDeviation = 0.0;
		string worstHeroId = string.Empty;
		foreach (BalanceHeroResult hero in heroes)
		{
			if (hero.Deviation <= maximumDeviation) continue;
			maximumDeviation = hero.Deviation;
			worstHeroId = hero.HeroId;
		}

		double guardRate = skillGuardSeeds == 0 ? 0.0 : guardWins / skillGuardSeeds;

		return new BalanceEvaluation
		{
			Fitness = 1.0 - maximumDeviation / 0.5,
			MaximumDeviation = maximumDeviation,
			WorstHeroId = worstHeroId,
			SkillGuardWinRate = guardRate,
			SkillGuardMatches = skillGuardSeeds,
			Heroes = new List<BalanceHeroResult>(heroes)
		};
	}

	private MatchRunOptions BuildOptions(string seatOnePolicy, string seatTwoPolicy)
	{
		string[] arguments =
		[
			"--map", _mapPath,
			"--seats", "2",
			"--max-seconds", _maximumSeconds.ToString("0.###", CultureInfo.InvariantCulture),
			"--seat-policy", $"1={seatOnePolicy}",
			"--seat-policy", $"2={seatTwoPolicy}"
		];

		return MatchRunOptions.Parse(arguments);
	}

	private static double SeatScore(MatchOutcome outcome, int seat)
	{
		if (outcome.WinnerSeat == seat) return 1.0;
		if (outcome.WinnerSeat < 0) return 0.5;
		return 0.0;
	}

	public static int DeriveSeed(int baseSeed, int stream)
	{
		unchecked
		{
			uint mixed = (uint)baseSeed * 2654435761u + (uint)stream * 40503u;
			mixed ^= mixed >> 15;
			mixed *= 2246822519u;
			mixed ^= mixed >> 13;
			return (int)(mixed & 0x7FFFFFFF) + 1;
		}
	}
}
