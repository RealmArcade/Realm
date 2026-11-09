using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Realm.Ecs.AI.Genres;
using Realm.Ecs.AI.Policy;
using Realm.Ecs.AI.Simulation;
using Realm.Ecs.AI.Training;
using Realm.Shared;

namespace Realm.Tools.MatchRunner;

public class TrainingRunResult
{
	public BotProfile Profile { get; init; } = new();
	public IReadOnlyList<float> GenerationBestFitness { get; init; } = Array.Empty<float>();
	public float ValidationFitness { get; init; }
	public int ChampionGeneration { get; init; }
	public int PlayedEvaluations { get; init; }
	public int FailedEvaluations { get; init; }
}

public static class MatchTrainer
{
	public const float WinWeight = 0.9f;
	public const float DamageShareWeight = 0.1f;

	public static TrainingRunResult Train(MatchRunOptions options)
	{
		IAiGenreProvider provider = AiGenreRegistry.Get(options.TrainGenre);
		string mapName = ReadMapName(options.MapPath);
		string workspaceDirectory = Path.Combine(Path.GetTempPath(), "realm-match-runner", $"training-{Environment.ProcessId}");
		Directory.CreateDirectory(workspaceDirectory);

		try
		{
			var evaluator = new TrainingMatchEvaluator(options, workspaceDirectory);
			var trainer = new SelfPlayTrainer();
			BotProfile profile = trainer.TrainAgainstEvaluator(
				mapName,
				options.TrainGenerations,
				options.TrainPopulation,
				options.TrainEvaluations,
				provider.FeatureCount,
				options.TrainMapCandidateFeatures,
				provider.GenreName,
				evaluator.Evaluate,
				options.TrainSeed,
				RealmVersion.GameBuildNumber,
				options.TrainInitialWeights,
				ReportGeneration,
				options.TrainParallel);

			return new TrainingRunResult
			{
				Profile = profile,
				GenerationBestFitness = trainer.GenerationBestFitness.ToArray(),
				ValidationFitness = trainer.ValidationFitness,
				ChampionGeneration = trainer.ChampionGeneration,
				PlayedEvaluations = evaluator.PlayedMatches,
				FailedEvaluations = evaluator.FailedMatches
			};
		}
		finally
		{
			TryDeleteDirectory(workspaceDirectory);
		}
	}

	public static string BuildManifest(MatchRunOptions options, TrainingRunResult result, string profilePath)
	{
		IAiGenreProvider provider = AiGenreRegistry.Get(options.TrainGenre);
		var manifest = new
		{
			schemaVersion = "1.0.0",
			mapName = result.Profile.MapName,
			mapPath = Path.GetFullPath(options.MapPath),
			gameBuildNumber = result.Profile.GameBuildNumber,
			genre = provider.GenreName,
			featureCount = provider.FeatureCount,
			mapCandidateFeatures = options.TrainMapCandidateFeatures,
			mapCandidateWeights = result.Profile.MapCandidateWeights,
			generations = options.TrainGenerations,
			populationSize = options.TrainPopulation,
			evaluationsPerCandidate = options.TrainEvaluations,
			trainingMatches = result.PlayedEvaluations,
			failedMatches = result.FailedEvaluations,
			baseSeed = options.TrainSeed,
			initialWeights = options.TrainInitialWeights,
			pairedEvaluationSeeds = true,
			matchedSeatOrientationsPerSeed = true,
			fitnessShaping = new { win = WinWeight, damageShare = DamageShareWeight },
			maximumSecondsPerMatch = options.MaxSeconds,
			seats = options.Seats,
			opponentPolicy = options.Policy,
			validation = new
			{
				seedSalt = SelfPlayTrainer.ValidationSeedSalt,
				candidates = options.TrainEvaluations,
				championSelection = "highest held-out fitness across every generation champion",
				championGeneration = result.ChampionGeneration,
				fitness = result.ValidationFitness
			},
			evaluationsInParallel = options.TrainParallel,
			profilePath = Path.GetFullPath(profilePath),
			fitnessScore = result.Profile.FitnessScore,
			generationBestFitness = result.GenerationBestFitness,
			createdUtc = DateTime.UtcNow.ToString("O")
		};

		return JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
	}

	private static void ReportGeneration(int generation, float bestFitness)
	{
		Console.WriteLine($"  generation {generation + 1} best fitness {bestFitness:F3}");
		Console.Out.Flush();
	}

	private static void TryDeleteDirectory(string directory)
	{
		try
		{
			if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
		}
		catch (IOException)
		{
		}
		catch (UnauthorizedAccessException)
		{
		}
	}

	private static string ReadMapName(string mapPath)
	{
		string trimmedPath = mapPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		string metadataPath = Path.Combine(trimmedPath, "metadata.json");
		if (!File.Exists(metadataPath))
		{
			return Path.GetFileName(trimmedPath);
		}

		using JsonDocument document = JsonDocument.Parse(File.ReadAllText(metadataPath));
		if (document.RootElement.TryGetProperty("MapProperties", out JsonElement properties)
			&& properties.TryGetProperty("MapName", out JsonElement mapName)
			&& mapName.ValueKind == JsonValueKind.String)
		{
			return mapName.GetString() ?? "Map";
		}

		return "Map";
	}
}

public class TrainingMatchEvaluator
{
	private const float DrawFitness = 0.5f;

	private readonly MatchRunOptions _options;
	private readonly string _workspaceDirectory;
	private int _playedMatches;
	private int _failedMatches;
	private int _evaluationCounter;

	public TrainingMatchEvaluator(MatchRunOptions options, string workspaceDirectory)
	{
		_options = options;
		_workspaceDirectory = workspaceDirectory;
	}

	public int PlayedMatches => Volatile.Read(ref _playedMatches);

	public int FailedMatches => Volatile.Read(ref _failedMatches);

	public float Evaluate(float[] weights, int seed)
	{
		float candidateAsSeatOne = PlayOrientation(weights, seed, candidateSeat: 1);
		float candidateAsSeatTwo = PlayOrientation(weights, seed, candidateSeat: 2);
		return 0.5f * (candidateAsSeatOne + candidateAsSeatTwo);
	}

	private float PlayOrientation(float[] weights, int seed, int candidateSeat)
	{
		string profilePath = WriteCandidateProfile(weights);
		string outcomePath = Path.Combine(
			_workspaceDirectory,
			$"outcome-{seed}-{candidateSeat}-{Interlocked.Increment(ref _evaluationCounter)}.jsonl");
		ClearOutcomeFiles(outcomePath);
		Interlocked.Increment(ref _playedMatches);

		try
		{
			RunMatchProcess(profilePath, seed, candidateSeat, outcomePath);
			return ReadFitness(outcomePath, candidateSeat);
		}
		catch (InvalidOperationException exception)
		{
			int failedMatches = Interlocked.Increment(ref _failedMatches);
			if (failedMatches * 2 > Volatile.Read(ref _playedMatches))
			{
				throw new InvalidOperationException(
					$"the training stopped after {failedMatches} of {Volatile.Read(ref _playedMatches)} evaluation matches failed to run: {exception.Message}",
					exception);
			}

			return DrawFitness;
		}
		finally
		{
			ClearOutcomeFiles(outcomePath);
		}
	}

	private void RunMatchProcess(string profilePath, int seed, int candidateSeat, string outcomePath)
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
		AddArgument(start, "--map", _options.MapPath);
		if (!string.IsNullOrWhiteSpace(_options.WasmPath)) AddArgument(start, "--wasm", _options.WasmPath);
		if (!string.IsNullOrWhiteSpace(_options.DataProfilePath)) AddArgument(start, "--data-profile", _options.DataProfilePath);
		AddArgument(start, "--seed-range", string.Create(CultureInfo.InvariantCulture, $"{seed}-{seed}"));
		AddArgument(start, "--seats", _options.Seats.ToString(CultureInfo.InvariantCulture));
		AddArgument(start, "--max-seconds", _options.MaxSeconds.ToString("0.###", CultureInfo.InvariantCulture));
		AddArgument(start, "--policy", _options.Policy);
		AddArgument(start, "--seat-policy", $"{candidateSeat}=profile:{profilePath}");
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
			throw new InvalidOperationException($"the match for seed {seed} did not finish within {EvaluationTimeoutMilliseconds} ms");
		}

		if (process.ExitCode != 0 || File.Exists(outcomePath) == false)
		{
			throw new InvalidOperationException(
				$"the match for seed {seed} with the candidate at seat {candidateSeat} ended with exit code {process.ExitCode}: {DescribeText(errorText)}");
		}
	}

	private int EvaluationTimeoutMilliseconds
	{
		get
		{
			double milliseconds = (_options.MaxSeconds + 600.0) * 1000.0;
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

	private string WriteCandidateProfile(float[] weights)
	{
		string profilePath = Path.Combine(_workspaceDirectory, $"candidate-{HashWeights(weights)}.json");
		if (File.Exists(profilePath)) return profilePath;

		int mapCandidateFeatures = Math.Clamp(_options.TrainMapCandidateFeatures, 0, weights.Length);
		int genreFeatureCount = weights.Length - mapCandidateFeatures;

		var profile = new BotProfile
		{
			Genre = _options.TrainGenre,
			MapName = "Training",
			ProfileId = "TrainingCandidate",
			Weights = weights[..genreFeatureCount],
			MapCandidateWeights = mapCandidateFeatures > 0 ? weights[genreFeatureCount..] : Array.Empty<float>()
		};

		string temporaryPath = $"{profilePath}.{Environment.ProcessId}.{Interlocked.Increment(ref _evaluationCounter)}.tmp";
		File.WriteAllText(temporaryPath, profile.ToJson(), new UTF8Encoding(false));
		File.Move(temporaryPath, profilePath, overwrite: true);
		return profilePath;
	}

	private static string HashWeights(float[] weights)
	{
		uint hash = 2166136261u;
		for (int index = 0; index < weights.Length; index++)
		{
			byte[] weightBytes = BitConverter.GetBytes(weights[index]);
			for (int byteIndex = 0; byteIndex < weightBytes.Length; byteIndex++)
			{
				hash ^= weightBytes[byteIndex];
				hash *= 16777619u;
			}
		}

		return hash.ToString("x8", CultureInfo.InvariantCulture);
	}

	private static float ReadFitness(string outcomePath, int candidateSeat)
	{
		if (!File.Exists(outcomePath))
		{
			throw new InvalidOperationException($"the match with the candidate at seat {candidateSeat} wrote no outcome");
		}

		foreach (string line in File.ReadLines(outcomePath))
		{
			if (line.Length == 0) continue;

			using JsonDocument document = JsonDocument.Parse(line);
			JsonElement root = document.RootElement;
			if (root.TryGetProperty("error", out JsonElement error))
			{
				throw new InvalidOperationException(error.GetString() ?? "the match failed without a message");
			}

			if (!root.TryGetProperty("winnerSeat", out JsonElement winner))
			{
				throw new InvalidOperationException("the match outcome carries no winner seat");
			}

			int winnerSeat = winner.GetInt32();
			float winScore = winnerSeat == candidateSeat ? 1.0f : winnerSeat <= 0 ? 0.5f : 0.0f;
			return MatchTrainer.WinWeight * winScore + MatchTrainer.DamageShareWeight * ReadDamageShare(root, candidateSeat);
		}

		throw new InvalidOperationException("the match outcome file is empty");
	}

	private static float ReadDamageShare(JsonElement root, int candidateSeat)
	{
		if (!root.TryGetProperty("perSeat", out JsonElement perSeat)) return DrawFitness;

		float candidateDamage = 0.0f;
		float totalDamage = 0.0f;
		foreach (JsonElement seat in perSeat.EnumerateArray())
		{
			float damage = seat.TryGetProperty("damageDealt", out JsonElement damageValue) ? damageValue.GetSingle() : 0.0f;
			totalDamage += damage;
			if (seat.TryGetProperty("seat", out JsonElement seatValue) && seatValue.GetInt32() == candidateSeat)
			{
				candidateDamage = damage;
			}
		}

		return totalDamage > 0.0f ? candidateDamage / totalDamage : DrawFitness;
	}

	private static void ClearOutcomeFiles(string outcomePath)
	{
		if (File.Exists(outcomePath)) File.Delete(outcomePath);
		string summaryPath = outcomePath + ".summary.json";
		if (File.Exists(summaryPath)) File.Delete(summaryPath);
	}
}
