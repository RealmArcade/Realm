using System.Globalization;

namespace Realm.Tools.MatchRunner;

public class MatchRunOptions
{
	public const string DefaultPolicy = "uniform";

	public string MapPath { get; private set; } = string.Empty;
	public string? WasmPath { get; private set; }
	public string? DataProfilePath { get; set; }
	public string? AuthorityLogPath { get; private set; }
	public int SeedStart { get; private set; } = 1;
	public int SeedEnd { get; private set; } = 1;
	public int Seats { get; private set; } = 2;
	public float MaxSeconds { get; private set; } = 1800f;
	public string Policy { get; set; } = DefaultPolicy;
	public Dictionary<int, string> SeatPolicies { get; } = new();
	public string OutputPath { get; private set; } = "match-outcomes.jsonl";
	public int Parallel { get; private set; } = 1;
	public bool IsBlockChild { get; private set; }
	public bool ShowHelp { get; private set; }
	public bool Train { get; private set; }
	public int TrainGenerations { get; private set; } = 3;
	public int TrainPopulation { get; private set; } = 6;
	public int TrainEvaluations { get; private set; } = 4;
	public string TrainGenre { get; private set; } = "custom";
	public int TrainSeed { get; private set; }
	public string? TrainOutputPath { get; private set; }
	public float[]? TrainInitialWeights { get; private set; }
	public int TrainMapCandidateFeatures { get; private set; }
	public int TrainParallel { get; private set; } = Math.Max(1, Environment.ProcessorCount - 2);
	public bool TrainPublish { get; private set; }
	public int PublishSeedStart { get; private set; }
	public int PublishSeedEnd { get; private set; }
	public int PublishSmokeSeeds { get; private set; } = 4;
	public bool Balance { get; private set; }
	public string? BalanceReportPath { get; private set; }
	public string? BalanceInputPath { get; private set; }

	public int SeedCount => SeedEnd - SeedStart + 1;

	public int PublishSeedCount => PublishSeedEnd - PublishSeedStart + 1;

	public static string Usage =>
		"""
		Realm match runner — plays real maps on the authority without a client.

		  --map <path>            map folder to play (required)
		  --wasm <path>           exact map script to run instead of the one found in the map folder
		  --data-profile <path>   seed a saved bot profile into the match data directory
		  --authority-log <path>  keep the authority log of a single-match run instead of discarding it
		  --seed-range <a-b>      seeds to play, inclusive (default 1-1)
		  --seats <n>             computer seats to seat on slots 1..n (default 2)
		  --max-seconds <value>   stop a match after this many simulated seconds (default 1800)
		  --policy <spec>         policy for every seat: uniform, none, fixed:<payloadId>, random-payload:<prefix>, profile:<path> (default uniform)
		  --seat-policy <s>=<spec> override the policy of one seat (repeatable)
		  --output <path>         jsonl file that receives one line per match (default match-outcomes.jsonl)
		  --parallel <n>          matches played at the same time, one process each (default 1)
		  --train                 evolve a bot profile by playing real matches instead of a batch
		  --train-generations <n> generations of the evolution (default 3)
		  --train-population <n>  candidate weight vectors per generation (default 6)
		  --train-evaluations <n> matches played by each candidate per generation (default 4)
		  --train-genre <name>    genre whose feature count the profile carries (default custom)
		  --train-seed <n>        seed of the evolution; zero selects the fixed default
		  --train-output <path>   profile file to write (default <map>/<mapName>_bot.json)
		  --train-initial-weights <a,b,...> start the population around this weight vector instead of pure noise
		  --train-map-features <n> trailing genome slots the map's own candidates own (default 0)
		  --train-parallel <n>    candidate evaluations played at the same time (default cores minus two)
		  --train-publish         train, duel the champion on new seeds, write the profile and gate publication on that duel
		  --publish-seeds <a-b>   seeds of the out-of-sample duel, never played by the training (required by --train-publish)
		  --publish-smoke-seeds <n> leading publish seeds replayed with the written profile to catch weight mismatches (default 4)
		  --balance               write the balance report of the played batch beside the outcomes
		  --balance-report <path> markdown report to write (default <output>.balance.md)
		  --balance-input <path>  analyse an already played jsonl (or a directory of chunk jsonl) instead of playing
		  --help                  show this text
		""";

	public static MatchRunOptions Parse(string[] args)
	{
		var options = new MatchRunOptions();

		for (int index = 0; index < args.Length; index++)
		{
			string argument = args[index];
			switch (argument)
			{
				case "--help":
				case "-h":
					options.ShowHelp = true;
					return options;
				case "--map":
					options.MapPath = ReadValue(args, ref index, argument);
					break;
				case "--wasm":
					options.WasmPath = ReadValue(args, ref index, argument);
					break;
				case "--data-profile":
					options.DataProfilePath = ReadValue(args, ref index, argument);
					break;
				case "--authority-log":
					options.AuthorityLogPath = ReadValue(args, ref index, argument);
					break;
				case "--seed-range":
					(options.SeedStart, options.SeedEnd) = ReadSeedRange(args, ref index, argument);
					break;
				case "--seats":
					options.Seats = ReadInt(args, ref index, argument, minimum: 1);
					break;
				case "--max-seconds":
					options.MaxSeconds = ReadFloat(args, ref index, argument);
					break;
				case "--policy":
					options.Policy = ReadValue(args, ref index, argument);
					break;
				case "--seat-policy":
					ReadSeatPolicy(options, ReadValue(args, ref index, argument));
					break;
				case "--output":
					options.OutputPath = ReadValue(args, ref index, argument);
					break;
				case "--parallel":
					options.Parallel = ReadInt(args, ref index, argument, minimum: 1);
					break;
				case "--block-child":
					options.IsBlockChild = true;
					break;
				case "--train":
					options.Train = true;
					break;
				case "--train-generations":
					options.TrainGenerations = ReadInt(args, ref index, argument, minimum: 1);
					break;
				case "--train-population":
					options.TrainPopulation = ReadInt(args, ref index, argument, minimum: 2);
					break;
				case "--train-evaluations":
					options.TrainEvaluations = ReadInt(args, ref index, argument, minimum: 1);
					break;
				case "--train-genre":
					options.TrainGenre = ReadValue(args, ref index, argument);
					break;
				case "--train-seed":
					options.TrainSeed = ReadNonNegativeInt(args, ref index, argument);
					break;
				case "--train-output":
					options.TrainOutputPath = ReadValue(args, ref index, argument);
					break;
				case "--train-initial-weights":
					options.TrainInitialWeights = ReadFloatList(args, ref index, argument);
					break;
				case "--train-parallel":
					options.TrainParallel = ReadInt(args, ref index, argument, minimum: 1);
					break;
				case "--train-map-features":
					options.TrainMapCandidateFeatures = ReadNonNegativeInt(args, ref index, argument);
					break;
				case "--train-publish":
					options.TrainPublish = true;
					break;
				case "--publish-seeds":
					(options.PublishSeedStart, options.PublishSeedEnd) = ReadSeedRange(args, ref index, argument);
					break;
				case "--publish-smoke-seeds":
					options.PublishSmokeSeeds = ReadInt(args, ref index, argument, minimum: 1);
					break;
				case "--balance":
					options.Balance = true;
					break;
				case "--balance-report":
					options.BalanceReportPath = ReadValue(args, ref index, argument);
					break;
				case "--balance-input":
					options.BalanceInputPath = ReadValue(args, ref index, argument);
					options.Balance = true;
					break;
				default:
					throw new ArgumentException($"unknown argument '{argument}'");
			}
		}

		if (!options.ShowHelp && string.IsNullOrWhiteSpace(options.MapPath))
		{
			throw new ArgumentException("--map is required");
		}
		if (!options.ShowHelp && options.Seats > 24)
		{
			throw new ArgumentException("--seats cannot exceed 24, because slot zero is the neutral seat");
		}

		if (!options.ShowHelp && options.TrainPublish)
		{
			options.Train = true;
			if (options.PublishSeedStart < 1 || options.PublishSeedEnd < options.PublishSeedStart)
			{
				throw new ArgumentException("--train-publish needs --publish-seeds <first>-<last> with seeds the training never played");
			}

			if (options.Seats != 2)
			{
				throw new ArgumentException("--train-publish duels the champion against the factory profile, so it needs exactly 2 seats");
			}
		}

		return options;
	}

	private static string ReadValue(string[] args, ref int index, string argument)
	{
		if (index + 1 >= args.Length) throw new ArgumentException($"'{argument}' needs a value");
		index++;
		return args[index];
	}

	private static int ReadInt(string[] args, ref int index, string argument, int minimum)
	{
		string text = ReadValue(args, ref index, argument);
		if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) || value < minimum)
		{
			throw new ArgumentException($"'{argument}' needs a whole number of at least {minimum}, not '{text}'");
		}

		return value;
	}

	private static float ReadFloat(string[] args, ref int index, string argument)
	{
		string text = ReadValue(args, ref index, argument);
		if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) || !float.IsFinite(value) || value <= 0f)
		{
			throw new ArgumentException($"'{argument}' needs a positive number, not '{text}'");
		}

		return value;
	}

	private static float[] ReadFloatList(string[] args, ref int index, string argument)
	{
		string text = ReadValue(args, ref index, argument);
		string[] parts = text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		if (parts.Length == 0)
		{
			throw new ArgumentException($"'{argument}' needs at least one number");
		}

		var values = new float[parts.Length];
		for (int position = 0; position < parts.Length; position++)
		{
			if (!float.TryParse(parts[position], NumberStyles.Float, CultureInfo.InvariantCulture, out values[position]) || !float.IsFinite(values[position]))
			{
				throw new ArgumentException($"'{argument}' expects numbers separated by commas, not '{parts[position]}'");
			}
		}

		return values;
	}

	private static int ReadNonNegativeInt(string[] args, ref int index, string argument)
	{
		string text = ReadValue(args, ref index, argument);
		if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) || value < 0)
		{
			throw new ArgumentException($"'{argument}' needs a whole number of at least 0, not '{text}'");
		}

		return value;
	}

	private static (int Start, int End) ReadSeedRange(string[] args, ref int index, string argument)
	{
		string text = ReadValue(args, ref index, argument);
		int separator = text.IndexOf('-');
		if (separator <= 0 || separator == text.Length - 1)
		{
			throw new ArgumentException($"{argument} expects '<first>-<last>', not '{text}'");
		}

		if (!int.TryParse(text.AsSpan(0, separator), NumberStyles.Integer, CultureInfo.InvariantCulture, out int start)
			|| !int.TryParse(text.AsSpan(separator + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int end))
		{
			throw new ArgumentException($"{argument} expects two whole numbers, not '{text}'");
		}

		if (start < 1 || end < start)
		{
			throw new ArgumentException($"{argument} must start at one or more and end after it starts, not '{text}'");
		}

		return (start, end);
	}

	private static void ReadSeatPolicy(MatchRunOptions options, string text)
	{
		int separator = text.IndexOf('=');
		if (separator <= 0 || separator == text.Length - 1)
		{
			throw new ArgumentException($"--seat-policy expects '<slot>=<spec>', not '{text}'");
		}

		if (!int.TryParse(text.AsSpan(0, separator), NumberStyles.Integer, CultureInfo.InvariantCulture, out int slot) || slot < 1)
		{
			throw new ArgumentException($"--seat-policy needs a slot of one or more, not '{text}'");
		}

		options.SeatPolicies[slot] = text[(separator + 1)..];
	}
}
