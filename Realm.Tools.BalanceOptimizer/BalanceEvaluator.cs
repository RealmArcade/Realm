using System.Globalization;
using System.Diagnostics;
using System.Text.Json;
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
	private readonly string _workspaceDirectory;
	private readonly object _guardLock = new();
	private int _evaluationCounter;

	public BalanceEvaluator(string mapPath, float maximumSeconds, int parallel)
	{
		_mapPath = Path.GetFullPath(mapPath);
		_maximumSeconds = maximumSeconds;
		_parallel = Math.Max(1, parallel);
		_workspaceDirectory = Path.Combine(
			Path.GetTempPath(),
			"realm-balance-evaluator",
			Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
		Directory.CreateDirectory(_workspaceDirectory);
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
				wins += PlayMatch($"fixed-once:{heroId}", "uniform", seed);
			}

			heroes[heroIndex] = new BalanceHeroResult { HeroId = heroId, Matches = seedsPerHero, Wins = wins };
		});

		double guardWins = 0.0;
		if (skillGuardSeeds > 0)
		{
			Parallel.For(0, skillGuardSeeds, new ParallelOptions { MaxDegreeOfParallelism = _parallel }, seedIndex =>
			{
				int seed = DeriveSeed(baseSeed, 1_000_000 + seedIndex);
				double score = PlayMatch($"profile:{profilePath}", "uniform", seed);
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

	private double PlayMatch(string seatOnePolicy, string seatTwoPolicy, int seed)
	{
		string outcomePath = Path.Combine(
			_workspaceDirectory,
			string.Create(CultureInfo.InvariantCulture, $"outcome-{seed}-{Interlocked.Increment(ref _evaluationCounter)}.jsonl"));

		try
		{
			RunMatchProcess(seatOnePolicy, seatTwoPolicy, seed, outcomePath);
			return SeatScore(ReadWinnerSeat(outcomePath), 1);
		}
		finally
		{
			DeleteOutcomeFiles(outcomePath);
		}
	}

	private void RunMatchProcess(string seatOnePolicy, string seatTwoPolicy, int seed, string outcomePath)
	{
		string processPath = Environment.ProcessPath ?? "dotnet";
		bool runsThroughDotnet = string.Equals(Path.GetFileNameWithoutExtension(processPath), "dotnet", StringComparison.OrdinalIgnoreCase);
		string assemblyPath = typeof(MatchRunOptions).Assembly.Location;

		var start = new ProcessStartInfo(processPath)
		{
			UseShellExecute = false,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			Environment = { ["REALM_MATCH_RUNNER_CHILD"] = "1" }
		};

		if (runsThroughDotnet) start.ArgumentList.Add(assemblyPath);
		AddArgument(start, "--map", _mapPath);
		AddArgument(start, "--seed-range", string.Create(CultureInfo.InvariantCulture, $"{seed}-{seed}"));
		AddArgument(start, "--seats", "2");
		AddArgument(start, "--max-seconds", _maximumSeconds.ToString("0.###", CultureInfo.InvariantCulture));
		AddArgument(start, "--seat-policy", $"1={seatOnePolicy}");
		AddArgument(start, "--seat-policy", $"2={seatTwoPolicy}");
		AddArgument(start, "--output", outcomePath);

		using Process process = Process.Start(start)
			?? throw new InvalidOperationException($"could not start the runner for seed {seed}");

		Task<string> standardOutput = process.StandardOutput.ReadToEndAsync();
		Task<string> standardError = process.StandardError.ReadToEndAsync();
		bool finished = process.WaitForExit(EvaluationTimeoutMilliseconds);
		string errorText = standardError.GetAwaiter().GetResult();
		standardOutput.GetAwaiter().GetResult();

		if (!finished)
		{
			process.Kill(entireProcessTree: true);
			throw new InvalidOperationException($"the balance match for seed {seed} did not finish within {EvaluationTimeoutMilliseconds} ms");
		}

		if (process.ExitCode != 0 || File.Exists(outcomePath) == false)
		{
			throw new InvalidOperationException(
				$"the balance match for seed {seed} ended with exit code {process.ExitCode}: {DescribeText(errorText)}");
		}
	}

	private int EvaluationTimeoutMilliseconds
	{
		get
		{
			double milliseconds = (_maximumSeconds + 600.0) * 1000.0;
			return (int)Math.Min(int.MaxValue, milliseconds);
		}
	}

	private static void AddArgument(ProcessStartInfo start, string name, string? value)
	{
		if (string.IsNullOrWhiteSpace(value)) return;
		start.ArgumentList.Add(name);
		start.ArgumentList.Add(value);
	}

	private static string DescribeText(string text)
	{
		string trimmedText = text.Trim();
		if (trimmedText.Length == 0) return "no message";
		const int maximumReportedLength = 400;
		return trimmedText.Length <= maximumReportedLength ? trimmedText : trimmedText[^maximumReportedLength..];
	}

	private static int ReadWinnerSeat(string outcomePath)
	{
		if (!File.Exists(outcomePath))
		{
			throw new InvalidOperationException("the balance match wrote no outcome");
		}

		foreach (string line in File.ReadLines(outcomePath))
		{
			if (line.Length == 0) continue;

			using JsonDocument document = JsonDocument.Parse(line);
			JsonElement root = document.RootElement;
			if (root.TryGetProperty("error", out JsonElement error))
			{
				throw new InvalidOperationException(error.GetString() ?? "the balance match failed without a message");
			}

			if (!root.TryGetProperty("winnerSeat", out JsonElement winner))
			{
				throw new InvalidOperationException("the balance match outcome carries no winner seat");
			}

			return winner.GetInt32();
		}

		throw new InvalidOperationException("the balance match outcome file is empty");
	}

	private static void DeleteOutcomeFiles(string outcomePath)
	{
		if (File.Exists(outcomePath)) File.Delete(outcomePath);
		string summaryPath = outcomePath + ".summary.json";
		if (File.Exists(summaryPath)) File.Delete(summaryPath);
	}

	private static double SeatScore(int winnerSeat, int seat)
	{
		if (winnerSeat == seat) return 1.0;
		if (winnerSeat <= 0) return 0.5;
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
