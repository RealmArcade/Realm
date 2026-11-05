using System.Globalization;

namespace Realm.Tools.BalanceOptimizer;

public class BalanceOptimizerOptions
{
	public string MapPath { get; private set; } = string.Empty;
	public int Generations { get; private set; } = 3;
	public int Population { get; private set; } = 4;
	public int SeedsPerHero { get; private set; } = 4;
	public int SkillGuardSeeds { get; private set; } = 20;
	public double SkillGuardTolerance { get; private set; } = 0.05;
	public float MaximumSeconds { get; private set; } = 1800f;
	public int Parallel { get; private set; } = 4;
	public int Seed { get; private set; }
	public string ProfilePath { get; set; } = string.Empty;
	public string OutputPath { get; set; } = string.Empty;
	public string ReportPath { get; set; } = string.Empty;
	public bool ShowHelp { get; private set; }
	public bool DescribeOnly { get; private set; }

	public static string Usage =>
		"""
		Realm balance optimizer — tunes the map's numeric content against real matches.

		  --map <path>              map folder to tune (required)
		  --generations <n>         generations of the evolution (default 3)
		  --population <n>          candidates evaluated per generation (default 4)
		  --seeds-per-hero <n>      matches every hero plays in one candidate (default 4)
		  --skill-guard-seeds <n>   matches the trained policy plays against uniform (default 20)
		  --skill-guard-tolerance <x> drop from the untuned content's guard that rejects a candidate (default 0.05)
		  --max-seconds <value>     stop a match after this many simulated seconds (default 1800)
		  --parallel <n>            matches played at the same time (default 4)
		  --seed <n>                base seed of the evolution; zero selects the fixed default
		  --profile <path>          trained profile the skill guard plays (default <map>/<mapName>_bot.json)
		  --output <path>           genome file to write (default <map>/metadata.balance.genome.json)
		  --report <path>           run report to write (default <map>/metadata.balance.optimizer.json)
		  --describe                list the tunable fields and exit
		  --help                    show this text
		""";

	public static BalanceOptimizerOptions Parse(string[] args)
	{
		var options = new BalanceOptimizerOptions();

		for (int index = 0; index < args.Length; index++)
		{
			string argument = args[index];
			switch (argument)
			{
				case "--help":
				case "-h":
					options.ShowHelp = true;
					return options;
				case "--describe":
					options.DescribeOnly = true;
					break;
				case "--map":
					options.MapPath = ReadValue(args, ref index, argument);
					break;
				case "--generations":
					options.Generations = ReadInt(args, ref index, argument, minimum: 1);
					break;
				case "--population":
					options.Population = ReadInt(args, ref index, argument, minimum: 1);
					break;
				case "--seeds-per-hero":
					options.SeedsPerHero = ReadInt(args, ref index, argument, minimum: 1);
					break;
				case "--skill-guard-seeds":
					options.SkillGuardSeeds = ReadInt(args, ref index, argument, minimum: 0);
					break;
				case "--skill-guard-tolerance":
					options.SkillGuardTolerance = ReadFloat(args, ref index, argument);
					break;
				case "--max-seconds":
					options.MaximumSeconds = (float)ReadFloat(args, ref index, argument);
					break;
				case "--parallel":
					options.Parallel = ReadInt(args, ref index, argument, minimum: 1);
					break;
				case "--seed":
					options.Seed = ReadInt(args, ref index, argument, minimum: 0);
					break;
				case "--profile":
					options.ProfilePath = ReadValue(args, ref index, argument);
					break;
				case "--output":
					options.OutputPath = ReadValue(args, ref index, argument);
					break;
				case "--report":
					options.ReportPath = ReadValue(args, ref index, argument);
					break;
				default:
					throw new ArgumentException($"unknown argument '{argument}'");
			}
		}

		if (!options.ShowHelp && string.IsNullOrWhiteSpace(options.MapPath))
		{
			throw new ArgumentException("--map is required");
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

	private static double ReadFloat(string[] args, ref int index, string argument)
	{
		string text = ReadValue(args, ref index, argument);
		if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) || !double.IsFinite(value) || value < 0.0)
		{
			throw new ArgumentException($"'{argument}' needs a non-negative number, not '{text}'");
		}

		return value;
	}
}
