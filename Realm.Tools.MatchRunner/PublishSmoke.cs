using System.Text;
using Realm.Ecs.AI.Policy;
using Realm.ListenServer;

namespace Realm.Tools.MatchRunner;

public class PublishSmokeResult
{
	public int Matches { get; init; }
	public int Failures { get; init; }
	public bool WarningFound { get; init; }
	public int RegisteredFeatures { get; init; }
	public int ProfileMapWeights { get; init; }
	public IReadOnlyList<string> Details { get; init; } = Array.Empty<string>();
	public string LogPath { get; init; } = string.Empty;
}

public static class PublishSmoke
{
	public static PublishSmokeResult Run(MatchRunOptions options, string profilePath, BotProfile profile, string workspaceDirectory, string combinedLogPath)
	{
		options.DataProfilePath = profilePath;
		options.Policy = "none";

		int matches = 0;
		int failures = 0;
		int registeredFeatures = 0;
		var details = new List<string>();
		var logLines = new List<string>();
		bool warningFound = false;

		for (int offset = 0; offset < options.PublishSmokeSeeds; offset++)
		{
			int seed = options.PublishSeedStart + offset;
			string logPath = Path.Combine(workspaceDirectory, $"smoke-{seed}.log");

			try
			{
				MatchPlayer.Run(
					options,
					seed,
					null,
					logPath,
					bots =>
					{
						for (int seat = 1; seat <= options.Seats; seat++)
						{
							int count = bots.MapCandidateFeatureCount(seat);
							if (count > registeredFeatures) registeredFeatures = count;
						}
					});
				matches++;
			}
			catch (Exception exception)
			{
				failures++;
				details.Add($"seed {seed}: {exception.Message}");
			}

			if (!File.Exists(logPath)) continue;
			logLines.Add($"# seed {seed}");
			foreach (string line in File.ReadLines(logPath))
			{
				logLines.Add(line);
				if (!line.Contains(ServerBots.MapCandidateWeightsMismatchMarker, StringComparison.Ordinal)) continue;
				details.Add(line);
				warningFound = true;
			}
		}

		string writtenLogPath = string.Empty;
		if (logLines.Count > 0)
		{
			writtenLogPath = Path.GetFullPath(combinedLogPath);
			Directory.CreateDirectory(Path.GetDirectoryName(writtenLogPath) ?? Directory.GetCurrentDirectory());
			File.WriteAllLines(writtenLogPath, logLines, new UTF8Encoding(false));
		}

		return new PublishSmokeResult
		{
			Matches = matches,
			Failures = failures,
			WarningFound = warningFound,
			RegisteredFeatures = registeredFeatures,
			ProfileMapWeights = profile.MapCandidateWeights.Length,
			Details = details,
			LogPath = writtenLogPath
		};
	}
}
