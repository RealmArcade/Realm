using System.Text;
using Realm.Ecs.AI.Policy;
using Realm.Ecs.AI.Training;

namespace Realm.Tools.MatchRunner;

public static class PublishPipeline
{
	public const int NoPublishExitCode = 3;

	public static int Run(MatchRunOptions options)
	{
		string workspace = Path.Combine(Path.GetTempPath(), "realm-match-runner", $"publish-{Environment.ProcessId}");
		Directory.CreateDirectory(workspace);

		try
		{
			TrainingRunResult training = MatchTrainer.Train(options);
			BotProfile profile = training.Profile;
			string profilePath = Path.GetFullPath(options.TrainOutputPath ?? Path.Combine(options.MapPath, $"{profile.MapName}_bot.json"));
			string profileDirectory = Path.GetDirectoryName(profilePath) ?? Directory.GetCurrentDirectory();
			string baseName = Path.GetFileNameWithoutExtension(profilePath);
			string stagingPath = Path.Combine(workspace, "candidate_bot.json");
			File.WriteAllText(stagingPath, profile.ToJson(), new UTF8Encoding(false));

			Directory.CreateDirectory(profileDirectory);
			string manifestPath = Path.ChangeExtension(profilePath, ".run.json");
			File.WriteAllText(manifestPath, MatchTrainer.BuildManifest(options, training, profilePath), new UTF8Encoding(false));

			PublishDuelResult duel = PublishDuel.Run(options, stagingPath, profileDirectory, baseName, workspace);
			string smokeLogPath = Path.ChangeExtension(profilePath, ".publish-smoke.log");
			PublishSmokeResult smoke = PublishSmoke.Run(options, stagingPath, profile, workspace, smokeLogPath);
			PublishVerdict verdict = PublishGate.Judge(duel, smoke, options.TrainMapCandidateFeatures);

			string reportPath = Path.ChangeExtension(profilePath, ".publish.md");
			int validationCandidates = Math.Max(options.TrainEvaluations, SelfPlayTrainer.MinimumValidationCandidates);
			PublishReport.Write(reportPath, options, training, duel, smoke, verdict, profilePath, manifestPath, validationCandidates);

			Console.WriteLine($"out-of-sample duel: {PublishGate.FormatPercent(verdict.WinRate)} [{PublishGate.FormatPercent(verdict.IntervalLow)}-{PublishGate.FormatPercent(verdict.IntervalHigh)}] p {PublishGate.FormatPValue(verdict.PValue)} on {duel.Matches} match(es), seeds {duel.SeedFirst}-{duel.SeedLast}");
			for (int seat = 1; seat <= 2; seat++)
			{
				int matches = duel.SeatMatches[seat - 1];
				double rate = matches > 0 ? (duel.SeatWins[seat - 1] + 0.5 * duel.SeatDraws[seat - 1]) / matches : 0.0;
				Console.WriteLine($"  seat {seat}: {PublishGate.FormatPercent(rate)} on {matches} match(es), {duel.SeatDraws[seat - 1]} draw(s)");
			}

			Console.WriteLine($"internal selection (not the published figure): {PublishGate.FormatPercent(training.ValidationFitness)} over {validationCandidates} validation seeds per generation champion");
			Console.WriteLine($"smoke: {smoke.Matches} match(es), {smoke.RegisteredFeatures} map candidate feature(s), weight warning: {(smoke.WarningFound ? "yes" : "no")}");

			if (verdict.Publish)
			{
				File.WriteAllText(profilePath, profile.ToJson(), new UTF8Encoding(false));
				Console.WriteLine("verdict: PUBLISH");
				Console.WriteLine($"profile: {profilePath}");
			}
			else
			{
				Console.WriteLine("verdict: DO NOT PUBLISH");
				foreach (string blocker in verdict.Blockers) Console.WriteLine($"  - {blocker}");
				Console.WriteLine($"profile: not written to {profilePath}");
			}

			Console.WriteLine($"report:  {reportPath}");
			return verdict.Publish ? 0 : NoPublishExitCode;
		}
		finally
		{
			TryDeleteDirectory(workspace);
		}
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
}
