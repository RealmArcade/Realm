using System.Diagnostics;
using System.Globalization;
using System.Text;
using Realm.Tools.MatchRunner;

MatchRunOptions options;
try
{
	options = MatchRunOptions.Parse(args);
}
catch (ArgumentException exception)
{
	Console.Error.WriteLine(exception.Message);
	Console.Error.WriteLine();
	Console.Error.WriteLine(MatchRunOptions.Usage);
	return 2;
}

if (options.ShowHelp)
{
	Console.WriteLine(MatchRunOptions.Usage);
	return 0;
}

bool isChild = string.Equals(Environment.GetEnvironmentVariable("REALM_MATCH_RUNNER_CHILD"), "1", StringComparison.Ordinal);
string outputPath = Path.GetFullPath(options.OutputPath);
string outputDirectory = Path.GetDirectoryName(outputPath) ?? Directory.GetCurrentDirectory();
Directory.CreateDirectory(outputDirectory);
string partsDirectory = Path.Combine(outputDirectory, $"{Path.GetFileName(outputPath)}.parts-{Environment.ProcessId}");
Directory.CreateDirectory(partsDirectory);

var clock = Stopwatch.StartNew();
List<string> failureSeeds = new();

if (options.SeedCount == 1)
{
	PlaySeeds([options.SeedStart], options, partsDirectory, failureSeeds);
}
else
{
	PlayInChildProcesses(options, partsDirectory, failureSeeds);
}

List<string> outcomeLines = MergeParts(options, partsDirectory);
File.WriteAllLines(outputPath, outcomeLines, new UTF8Encoding(false));

double wallClockSeconds = clock.Elapsed.TotalSeconds;
string summary = MatchBatchSummary.Build(outcomeLines, options, wallClockSeconds, options.SeedCount == 1 ? 1 : options.Parallel);
string summaryPath = outputPath + ".summary.json";
File.WriteAllText(summaryPath, summary, new UTF8Encoding(false));

if (!isChild)
{
	Console.WriteLine($"played {outcomeLines.Count - failureSeeds.Count} match(es) of seeds {options.SeedStart}-{options.SeedEnd} in {wallClockSeconds:F2}s");
	Console.WriteLine($"outcomes: {outputPath}");
	Console.WriteLine($"summary:  {summaryPath}");
}

Directory.Delete(partsDirectory, recursive: true);
return failureSeeds.Count > 0 ? 1 : 0;

static void PlaySeeds(IReadOnlyList<int> seeds, MatchRunOptions options, string partsDirectory, List<string> failureSeeds)
{
	foreach (int seed in seeds)
	{
		string partPath = Path.Combine(partsDirectory, $"{seed}.jsonl");
		try
		{
			var outcome = MatchPlayer.Run(options, seed);
			File.WriteAllText(partPath, outcome.ToJsonLine(), new UTF8Encoding(false));
			Console.WriteLine($"seed {seed}: finished with {outcome.EndReason}, winner {outcome.WinnerSeat}");
		}
		catch (Exception exception)
		{
			lock (failureSeeds) failureSeeds.Add(seed.ToString(CultureInfo.InvariantCulture));
			File.WriteAllText(partPath, MatchBatchSummary.BuildErrorLine(seed, $"{exception.GetType().Name}: {exception.Message}"), new UTF8Encoding(false));
			Console.Error.WriteLine($"seed {seed}: failed with {exception.GetType().Name}: {exception.Message}");
		}
	}
}

static void PlayInChildProcesses(MatchRunOptions options, string partsDirectory, List<string> failureSeeds)
{
	string processPath = Environment.ProcessPath ?? "dotnet";
	bool runsThroughDotnet = string.Equals(Path.GetFileNameWithoutExtension(processPath), "dotnet", StringComparison.OrdinalIgnoreCase);
	string assemblyPath = typeof(MatchRunOptions).Assembly.Location;

	using var throttle = new SemaphoreSlim(options.Parallel);
	var tasks = new List<Task>();

	for (int seed = options.SeedStart; seed <= options.SeedEnd; seed++)
	{
		int capturedSeed = seed;
		throttle.Wait();
		tasks.Add(Task.Run(() =>
		{
			try
			{
				var start = new ProcessStartInfo(processPath)
				{
					UseShellExecute = false,
					Environment = { ["REALM_MATCH_RUNNER_CHILD"] = "1" }
				};

				if (runsThroughDotnet) start.ArgumentList.Add(assemblyPath);
				start.ArgumentList.Add("--map");
				start.ArgumentList.Add(options.MapPath);
				if (!string.IsNullOrWhiteSpace(options.WasmPath))
				{
					start.ArgumentList.Add("--wasm");
					start.ArgumentList.Add(options.WasmPath);
				}

				start.ArgumentList.Add("--seed-range");
				start.ArgumentList.Add($"{capturedSeed}-{capturedSeed}");
				start.ArgumentList.Add("--seats");
				start.ArgumentList.Add(options.Seats.ToString(CultureInfo.InvariantCulture));
				start.ArgumentList.Add("--max-seconds");
				start.ArgumentList.Add(options.MaxSeconds.ToString("0.###", CultureInfo.InvariantCulture));
				start.ArgumentList.Add("--policy");
				start.ArgumentList.Add(options.Policy);
				foreach (KeyValuePair<int, string> seatPolicy in options.SeatPolicies)
				{
					start.ArgumentList.Add("--seat-policy");
					start.ArgumentList.Add($"{seatPolicy.Key}={seatPolicy.Value}");
				}

				start.ArgumentList.Add("--output");
				start.ArgumentList.Add(Path.Combine(partsDirectory, $"{capturedSeed}.jsonl"));

				using Process process = Process.Start(start) ?? throw new InvalidOperationException($"could not start the runner for seed {capturedSeed}");
				process.WaitForExit();
				if (process.ExitCode != 0)
				{
					lock (failureSeeds) failureSeeds.Add(capturedSeed.ToString(CultureInfo.InvariantCulture));
				}
			}
			finally
			{
				throttle.Release();
			}
		}));
	}

	Task.WaitAll(tasks.ToArray());
}

static List<string> MergeParts(MatchRunOptions options, string partsDirectory)
{
	var lines = new List<string>(options.SeedCount);
	for (int seed = options.SeedStart; seed <= options.SeedEnd; seed++)
	{
		string partPath = Path.Combine(partsDirectory, $"{seed}.jsonl");
		if (!File.Exists(partPath))
		{
			lines.Add(MatchBatchSummary.BuildErrorLine(seed, "the runner process for this seed ended without an outcome"));
			continue;
		}

		int lineCountBeforePart = lines.Count;
		foreach (string line in File.ReadAllLines(partPath))
		{
			if (line.Length > 0) lines.Add(line);
		}

		if (lines.Count == lineCountBeforePart)
		{
			lines.Add(MatchBatchSummary.BuildErrorLine(seed, "the runner process for this seed wrote no outcome"));
		}
	}

	return lines;
}
