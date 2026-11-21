using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Realm.Tools.MatchRunner;

public class PublishDuelResult
{
	public int SeedFirst { get; init; }
	public int SeedLast { get; init; }
	public int Failures { get; init; }
	public IReadOnlyList<string> FailureDetails { get; init; } = Array.Empty<string>();
	public string SeatOnePath { get; init; } = string.Empty;
	public string SeatTwoPath { get; init; } = string.Empty;
	public int[] SeatMatches { get; init; } = new int[2];
	public int[] SeatWins { get; init; } = new int[2];
	public int[] SeatLosses { get; init; } = new int[2];
	public int[] SeatDraws { get; init; } = new int[2];

	public int Matches => SeatMatches[0] + SeatMatches[1];
	public int Wins => SeatWins[0] + SeatWins[1];
	public int Losses => SeatLosses[0] + SeatLosses[1];
	public int Draws => SeatDraws[0] + SeatDraws[1];

	public double WinRate => Matches > 0 ? (Wins + 0.5 * Draws) / Matches : 0.0;
}

public static class PublishDuel
{
	public static PublishDuelResult Run(MatchRunOptions options, string profilePath, string outputDirectory, string baseName, string workspaceDirectory)
	{
		string partsDirectory = Path.Combine(workspaceDirectory, "duel");
		Directory.CreateDirectory(partsDirectory);

		var jobs = new List<(int Seed, int Seat)>();
		for (int seed = options.PublishSeedStart; seed <= options.PublishSeedEnd; seed++)
		{
			jobs.Add((seed, 1));
			jobs.Add((seed, 2));
		}

		var failures = new List<string>();
		Parallel.ForEach(
			jobs,
			new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, options.TrainParallel) },
			job =>
			{
				string outcomePath = PartPath(partsDirectory, job.Seed, job.Seat);
				try
				{
					MatchProcessRunner.ClearOutcomeFiles(outcomePath);
					MatchProcessRunner.Run(options, profilePath, job.Seed, job.Seat, outcomePath);
				}
				catch (Exception exception)
				{
					lock (failures) failures.Add($"seed {job.Seed} seat {job.Seat}: {exception.Message}");
				}
			});

		Directory.CreateDirectory(outputDirectory);
		string seatOnePath = Path.ChangeExtension(Path.Combine(outputDirectory, baseName), ".publish-seat1.jsonl");
		string seatTwoPath = Path.ChangeExtension(Path.Combine(outputDirectory, baseName), ".publish-seat2.jsonl");

		var seatMatches = new int[2];
		var seatWins = new int[2];
		var seatLosses = new int[2];
		var seatDraws = new int[2];

		for (int seat = 1; seat <= 2; seat++)
		{
			var lines = new List<string>(options.PublishSeedCount);
			for (int seed = options.PublishSeedStart; seed <= options.PublishSeedEnd; seed++)
			{
				string partPath = PartPath(partsDirectory, seed, seat);
				if (!File.Exists(partPath)) continue;

				string line;
				int winnerSeat;
				try
				{
					line = MatchProcessRunner.ReadOutcomeLine(partPath);
					using JsonDocument document = JsonDocument.Parse(line);
					winnerSeat = document.RootElement.GetProperty("winnerSeat").GetInt32();
				}
				catch (Exception exception)
				{
					lock (failures) failures.Add($"seed {seed} seat {seat}: {exception.Message}");
					continue;
				}

				if (winnerSeat == seat) seatWins[seat - 1]++;
				else if (winnerSeat <= 0) seatDraws[seat - 1]++;
				else seatLosses[seat - 1]++;

				seatMatches[seat - 1]++;
				lines.Add(line);
			}

			File.WriteAllLines(seat == 1 ? seatOnePath : seatTwoPath, lines, new UTF8Encoding(false));
		}

		return new PublishDuelResult
		{
			SeedFirst = options.PublishSeedStart,
			SeedLast = options.PublishSeedEnd,
			Failures = failures.Count,
			FailureDetails = failures,
			SeatOnePath = seatOnePath,
			SeatTwoPath = seatTwoPath,
			SeatMatches = seatMatches,
			SeatWins = seatWins,
			SeatLosses = seatLosses,
			SeatDraws = seatDraws
		};
	}

	private static string PartPath(string partsDirectory, int seed, int seat)
	{
		return Path.Combine(partsDirectory, string.Create(CultureInfo.InvariantCulture, $"{seed}-{seat}.jsonl"));
	}
}
