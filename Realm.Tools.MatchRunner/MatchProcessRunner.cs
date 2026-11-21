using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace Realm.Tools.MatchRunner;

public static class MatchProcessRunner
{
	public static void Run(MatchRunOptions options, string profilePath, int seed, int candidateSeat, string outcomePath)
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
		AddArgument(start, "--map", options.MapPath);
		if (!string.IsNullOrWhiteSpace(options.WasmPath)) AddArgument(start, "--wasm", options.WasmPath);
		if (!string.IsNullOrWhiteSpace(options.DataProfilePath)) AddArgument(start, "--data-profile", options.DataProfilePath);
		AddArgument(start, "--seed-range", string.Create(CultureInfo.InvariantCulture, $"{seed}-{seed}"));
		AddArgument(start, "--seats", options.Seats.ToString(CultureInfo.InvariantCulture));
		AddArgument(start, "--max-seconds", options.MaxSeconds.ToString("0.###", CultureInfo.InvariantCulture));
		AddArgument(start, "--policy", options.Policy);
		AddArgument(start, "--seat-policy", $"{candidateSeat}=profile:{profilePath}");
		AddArgument(start, "--output", outcomePath);

		using Process process = Process.Start(start)
			?? throw new InvalidOperationException($"could not start the runner for seed {seed}");

		Task<string> standardOutput = process.StandardOutput.ReadToEndAsync();
		Task<string> standardError = process.StandardError.ReadToEndAsync();
		int timeoutMilliseconds = EvaluationTimeoutMilliseconds(options);
		bool finished = process.WaitForExit(timeoutMilliseconds);
		string errorText = standardError.GetAwaiter().GetResult();
		standardOutput.GetAwaiter().GetResult();

		if (!finished)
		{
			process.Kill(entireProcessTree: true);
			throw new InvalidOperationException($"the match for seed {seed} did not finish within {timeoutMilliseconds} ms");
		}

		if (process.ExitCode != 0 || File.Exists(outcomePath) == false)
		{
			throw new InvalidOperationException(
				$"the match for seed {seed} with the candidate at seat {candidateSeat} ended with exit code {process.ExitCode}: {DescribeText(errorText)}");
		}
	}

	public static float ReadScore(string outcomePath, int candidateSeat)
	{
		int winnerSeat = ReadWinnerSeat(outcomePath);
		return winnerSeat == candidateSeat ? 1.0f : winnerSeat <= 0 ? 0.5f : 0.0f;
	}

	public static string ReadOutcomeLine(string outcomePath)
	{
		if (!File.Exists(outcomePath))
		{
			throw new InvalidOperationException("the match outcome file is missing");
		}

		foreach (string line in File.ReadLines(outcomePath))
		{
			if (line.Length == 0) continue;

			using JsonDocument document = JsonDocument.Parse(line);
			if (document.RootElement.TryGetProperty("error", out JsonElement error))
			{
				throw new InvalidOperationException(error.GetString() ?? "the match failed without a message");
			}

			return line;
		}

		throw new InvalidOperationException("the match outcome file is empty");
	}

	public static int ReadWinnerSeat(string outcomePath)
	{
		string line = ReadOutcomeLine(outcomePath);
		using JsonDocument document = JsonDocument.Parse(line);
		if (!document.RootElement.TryGetProperty("winnerSeat", out JsonElement winner))
		{
			throw new InvalidOperationException("the match outcome carries no winner seat");
		}

		return winner.GetInt32();
	}

	public static void ClearOutcomeFiles(string outcomePath)
	{
		if (File.Exists(outcomePath)) File.Delete(outcomePath);
		string summaryPath = outcomePath + ".summary.json";
		if (File.Exists(summaryPath)) File.Delete(summaryPath);
	}

	private static int EvaluationTimeoutMilliseconds(MatchRunOptions options)
	{
		double milliseconds = (options.MaxSeconds + 600.0) * 1000.0;
		return (int)Math.Min(int.MaxValue, milliseconds);
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
}
