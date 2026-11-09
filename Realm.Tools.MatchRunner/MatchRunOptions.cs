using System.Globalization;

namespace Realm.Tools.MatchRunner;

public class MatchRunOptions
{
	public const string DefaultPolicy = "uniform";

	public string MapPath { get; private set; } = string.Empty;
	public string? WasmPath { get; private set; }
	public string? DataProfilePath { get; private set; }
	public int SeedStart { get; private set; } = 1;
	public int SeedEnd { get; private set; } = 1;
	public int Seats { get; private set; } = 2;
	public float MaxSeconds { get; private set; } = 1800f;
	public string Policy { get; private set; } = DefaultPolicy;
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
	public bool Balance { get; private set; }
	public string? BalanceReportPath { get; private set; }
	public string? BalanceInputPath { get; private set; }

	public int SeedCount => SeedEnd - SeedStart + 1;

	public static string Usage =>
		"""
		Realm match runner — plays real maps on the authority without a client.

		  --map <path>            map folder to play (required)
		  --wasm <path>           exact map script to run instead of the one found in the map folder
		  --data-profile <path>   seed a saved bot profile into the match data directory
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
				case "--seed-range":
					ReadSeedRange(options, ReadValue(args, ref index, argument));
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

	private static void ReadSeedRange(MatchRunOptions options, string text)
	{
		int separator = text.IndexOf('-');
		if (separator <= 0 || separator == text.Length - 1)
		{
			throw new ArgumentException($"--seed-range expects '<first>-<last>', not '{text}'");
		}

		if (!int.TryParse(text.AsSpan(0, separator), NumberStyles.Integer, CultureInfo.InvariantCulture, out int start)
			|| !int.TryParse(text.AsSpan(separator + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int end))
		{
			throw new ArgumentException($"--seed-range expects two whole numbers, not '{text}'");
		}

		if (start < 1 || end < start)
		{
			throw new ArgumentException($"--seed-range must start at one or more and end after it starts, not '{text}'");
		}

		options.SeedStart = start;
		options.SeedEnd = end;
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
