using System.Text.Json;
using Realm.Ecs.AI.Policy;
using Realm.Ecs.AI.Simulation;
using Realm.ListenServer;
using Realm.Shared.Maps;
using Realm.Shared.Network;

namespace Realm.Tools.MatchRunner;

public static class MatchPlayer
{
	public static MatchOutcome Run(MatchRunOptions options, int seed)
	{
		MatchRosterSeat[] roster = BuildRoster(options.Seats);
		string workDirectory = Path.Combine(Path.GetTempPath(), "realm-match-runner", $"{Environment.ProcessId}-{seed}");
		Directory.CreateDirectory(workDirectory);

		LoadedMap map = MapWorldLoader.Load(options.MapPath, roster);
		using var log = new ServerLog(Path.Combine(workDirectory, "authority.log"));
		var paths = ServerMatchPaths.ForMap(map, options.MapPath, Path.Combine(workDirectory, "data"), workDirectory);

		using var simulation = new ServerSimulation(
			map,
			paths,
			log,
			seed,
			cheatsEnabled: false,
			wasmDebug: false,
			mapWorkspacePath: null,
			roster: roster,
			hostPlayerSlot: 0,
			preparedScriptHost: null,
			scriptPathOverride: options.WasmPath);

		ApplyPolicies(simulation, options);

		long maximumTicks = (long)Math.Ceiling(options.MaxSeconds * MatchTiming.SimulationTickRateHz);
		long ticks = 0;
		while (!simulation.IsGameOver && ticks < maximumTicks)
		{
			simulation.Step();
			ticks++;
		}

		string endReason = simulation.IsGameOver ? "victory" : "max-seconds";
		return simulation.Metrics.BuildOutcome(
			paths.MapName,
			seed,
			options.MaxSeconds,
			simulation.IsGameOver,
			endReason,
			(float)simulation.MatchSeconds,
			options.Seats);
	}

	private static MatchRosterSeat[] BuildRoster(int seats)
	{
		var roster = new MatchRosterSeat[seats];
		for (int seat = 1; seat <= seats; seat++)
		{
			roster[seat - 1] = new MatchRosterSeat(seat, $"Bot {seat}", seat, $"Team {seat}", IsComputer: true);
		}

		return roster;
	}

	private static void ApplyPolicies(ServerSimulation simulation, MatchRunOptions options)
	{
		ServerBots bots = simulation.ScriptApi.Bots;
		for (int seat = 1; seat <= options.Seats; seat++)
		{
			string policy = options.SeatPolicies.TryGetValue(seat, out string? seatPolicy) ? seatPolicy : options.Policy;
			ApplyPolicy(bots, seat, policy);
		}
	}

	private static void ApplyPolicy(ServerBots bots, int seat, string policy)
	{
		if (string.Equals(policy, "uniform", StringComparison.OrdinalIgnoreCase))
		{
			bots.SetDecisionPolicy(seat, new UniformRandomDecisionPolicy());
			return;
		}

		const string fixedPrefix = "fixed:";
		if (policy.StartsWith(fixedPrefix, StringComparison.OrdinalIgnoreCase))
		{
			string payloadId = policy[fixedPrefix.Length..];
			if (payloadId.Length == 0) throw new ArgumentException($"seat {seat} was given 'fixed:' without the payload to prefer");
			bots.SetDecisionPolicy(seat, new FixedFirstPickDecisionPolicy(payloadId));
			return;
		}

		const string profilePrefix = "profile:";
		if (policy.StartsWith(profilePrefix, StringComparison.OrdinalIgnoreCase))
		{
			string profilePath = policy[profilePrefix.Length..];
			if (profilePath.Length == 0) throw new ArgumentException($"seat {seat} was given 'profile:' without the file to read");
			if (!File.Exists(profilePath)) throw new ArgumentException($"seat {seat} was given the profile '{profilePath}' and there is no such file");

			string json = File.ReadAllText(profilePath);
			BotProfile profile;
			try
			{
				profile = BotProfile.FromJson(json);
			}
			catch (JsonException exception)
			{
				throw new ArgumentException($"seat {seat} was given the profile '{profilePath}' and it cannot be read: {exception.Message}");
			}

			if (profile.Weights == null || profile.Weights.Length != 8)
			{
				throw new ArgumentException($"seat {seat} was given the profile '{profilePath}' and it does not carry the eight weights a custom genre scores");
			}

			bots.SetProfile(seat, json);
			bots.SetDecisionPolicy(seat, new LinearUtilityDecisionPolicy());
			return;
		}

		throw new ArgumentException($"seat {seat} was given the policy '{policy}', which is not uniform, fixed:<payload> or profile:<path>");
	}
}
