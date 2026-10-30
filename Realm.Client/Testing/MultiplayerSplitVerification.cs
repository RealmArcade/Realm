using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Realm.Client.Testing;

public static class MultiplayerSplitVerification
{
	private const float PositionToleranceMeters = 15f;
	private const float HealthToleranceFraction = 0.25f;

	public static SplitWorldReport Verify(string outputDirectory, IReadOnlyList<string> clientDirectories)
	{
		var report = new SplitWorldReport();
		string hostPath = Path.Combine(outputDirectory, "world_summary_host.json");
		if (!File.Exists(hostPath))
		{
			report.AddFailure("the authority never wrote its world summary, so nothing can be compared");
			return report;
		}

		WorldSummary? host = ReadSummary(hostPath, report);
		if (host == null) return report;

		report.HostUnitCount = host.Units.Count;
		for (int index = 0; index < clientDirectories.Count; index++)
		{
			int harnessPeerIndex = index + 1;
			int peerIndex = harnessPeerIndex + 1;
			string clientPath = Path.Combine(clientDirectories[index], $"world_summary_peer{harnessPeerIndex}.json");
			if (!File.Exists(clientPath))
			{
				report.AddFailure($"peer {peerIndex} never wrote its world summary");
				continue;
			}

			WorldSummary? client = ReadSummary(clientPath, report);
			if (client == null) continue;

			report.ClientUnitCounts.Add(client.Units.Count);
			Compare(host, client, peerIndex, report);
		}

		return report;
	}

	private static void Compare(WorldSummary host, WorldSummary client, int peerIndex, SplitWorldReport report)
	{
		var hostUnits = new Dictionary<int, SummaryUnit>();
		for (int index = 0; index < host.Units.Count; index++)
		{
			hostUnits[host.Units[index].ServerId] = host.Units[index];
		}

		var clientUnits = new Dictionary<int, SummaryUnit>();
		for (int index = 0; index < client.Units.Count; index++)
		{
			clientUnits[client.Units[index].ServerId] = client.Units[index];
		}

		var missing = new List<int>();
		foreach (var pair in hostUnits)
		{
			if (!clientUnits.ContainsKey(pair.Key)) missing.Add(pair.Key);
		}

		var extra = new List<int>();
		foreach (var pair in clientUnits)
		{
			if (!hostUnits.ContainsKey(pair.Key)) extra.Add(pair.Key);
		}

		if (missing.Count > 0) report.AddFailure($"peer {peerIndex} never received {missing.Count} unit(s) the authority holds: {Describe(missing)}");
		if (extra.Count > 0) report.AddFailure($"peer {peerIndex} holds {extra.Count} unit(s) the authority does not have: {Describe(extra)}");

		int identityFailures = 0;
		int playerFailures = 0;
		float worstPositionDelta = 0f;
		int healthFailures = 0;

		foreach (var pair in hostUnits)
		{
			if (!clientUnits.TryGetValue(pair.Key, out SummaryUnit? clientUnit)) continue;

			SummaryUnit hostUnit = pair.Value;
			if (!string.Equals(hostUnit.UnitId, clientUnit.UnitId, StringComparison.Ordinal))
			{
				identityFailures++;
				if (identityFailures <= 5) report.AddFailure($"unit {pair.Key} is '{hostUnit.UnitId}' on the authority but '{clientUnit.UnitId}' on peer {peerIndex}");
			}

			if (hostUnit.Player != clientUnit.Player || hostUnit.Building != clientUnit.Building)
			{
				playerFailures++;
				if (playerFailures <= 5)
				{
					report.AddFailure($"unit {pair.Key} belongs to player {hostUnit.Player} (building {hostUnit.Building}) on the authority but player {clientUnit.Player} (building {clientUnit.Building}) on peer {peerIndex}");
				}
			}

			float deltaX = hostUnit.X - clientUnit.X;
			float deltaY = hostUnit.Y - clientUnit.Y;
			float deltaZ = hostUnit.Z - clientUnit.Z;
			float distance = MathF.Sqrt(deltaX * deltaX + deltaY * deltaY + deltaZ * deltaZ);
			if (distance > worstPositionDelta) worstPositionDelta = distance;
			if (distance > PositionToleranceMeters)
			{
				report.AddFailure($"unit {pair.Key} is {distance:F1} m away from where peer {peerIndex} holds it");
			}

			float allowance = MathF.Max(1f, hostUnit.MaxHp * HealthToleranceFraction);
			if (MathF.Abs(hostUnit.Hp - clientUnit.Hp) > allowance && hostUnit.Hp > 0f && clientUnit.Hp > 0f)
			{
				healthFailures++;
				if (healthFailures <= 5) report.AddFailure($"unit {pair.Key} has {hostUnit.Hp:F1} health on the authority but {clientUnit.Hp:F1} on peer {peerIndex}");
			}
		}

		report.WorstPositionDeltaMeters = MathF.Max(report.WorstPositionDeltaMeters, worstPositionDelta);

		var hostPlayers = new Dictionary<int, SummaryPlayer>();
		for (int index = 0; index < host.Players.Count; index++) hostPlayers[host.Players[index].Slot] = host.Players[index];

		int playerStateFailures = 0;
		foreach (var pair in hostPlayers)
		{
			if (!client.Players.Exists(player => player.Slot == pair.Key)) continue;

			SummaryPlayer clientPlayer = client.Players.Find(player => player.Slot == pair.Key);
			SummaryPlayer hostPlayer = pair.Value;
			if (hostPlayer.Active != clientPlayer.Active || hostPlayer.Computer != clientPlayer.Computer || hostPlayer.Team != clientPlayer.Team)
			{
				playerStateFailures++;
				if (playerStateFailures <= 5)
				{
					report.AddFailure($"player {pair.Key} is active={hostPlayer.Active} computer={hostPlayer.Computer} team={hostPlayer.Team} on the authority but active={clientPlayer.Active} computer={clientPlayer.Computer} team={clientPlayer.Team} on peer {peerIndex}");
				}
			}

			if (MathF.Abs(hostPlayer.Gold - clientPlayer.Gold) > 1f
				|| MathF.Abs(hostPlayer.Wood - clientPlayer.Wood) > 1f
				|| MathF.Abs(hostPlayer.Stone - clientPlayer.Stone) > 1f)
			{
				report.AddFailure($"player {pair.Key} holds {hostPlayer.Gold:F0}/{hostPlayer.Wood:F0}/{hostPlayer.Stone:F0} on the authority but {clientPlayer.Gold:F0}/{clientPlayer.Wood:F0}/{clientPlayer.Stone:F0} on peer {peerIndex}");
			}
		}
	}

	private static string Describe(List<int> ids)
	{
		var builder = new StringBuilder();
		for (int index = 0; index < ids.Count && index < 8; index++)
		{
			if (index > 0) builder.Append(", ");
			builder.Append(ids[index].ToString(CultureInfo.InvariantCulture));
		}

		if (ids.Count > 8) builder.Append(", ...");
		return builder.ToString();
	}

	private static WorldSummary? ReadSummary(string path, SplitWorldReport report)
	{
		try
		{
			using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
			JsonElement root = document.RootElement;

			var summary = new WorldSummary();
			if (root.TryGetProperty("units", out JsonElement units))
			{
				foreach (JsonElement unit in units.EnumerateArray())
				{
					summary.Units.Add(new SummaryUnit
					{
						ServerId = unit.GetProperty("serverId").GetInt32(),
						UnitId = unit.GetProperty("unitId").GetString() ?? string.Empty,
						Player = unit.GetProperty("player").GetInt32(),
						Building = unit.GetProperty("building").GetBoolean(),
						X = unit.GetProperty("x").GetSingle(),
						Y = unit.GetProperty("y").GetSingle(),
						Z = unit.GetProperty("z").GetSingle(),
						Hp = unit.GetProperty("hp").GetSingle(),
						MaxHp = unit.GetProperty("maxHp").GetSingle()
					});
				}
			}

			if (root.TryGetProperty("players", out JsonElement players))
			{
				foreach (JsonElement player in players.EnumerateArray())
				{
					summary.Players.Add(new SummaryPlayer
					{
						Slot = player.GetProperty("slot").GetInt32(),
						Active = player.GetProperty("active").GetBoolean(),
						Computer = player.GetProperty("computer").GetBoolean(),
						Team = player.GetProperty("team").GetInt32(),
						Gold = player.GetProperty("gold").GetSingle(),
						Wood = player.GetProperty("wood").GetSingle(),
						Stone = player.GetProperty("stone").GetSingle()
					});
				}
			}

			return summary;
		}
		catch (Exception exception) when (exception is JsonException or IOException or KeyNotFoundException or InvalidOperationException)
		{
			report.AddFailure($"the world summary '{Path.GetFileName(path)}' could not be read: {exception.Message}");
			return null;
		}
	}

	private sealed class WorldSummary
	{
		public List<SummaryUnit> Units { get; } = new();
		public List<SummaryPlayer> Players { get; } = new();
	}

	private sealed class SummaryUnit
	{
		public int ServerId;
		public string UnitId = string.Empty;
		public int Player;
		public bool Building;
		public float X;
		public float Y;
		public float Z;
		public float Hp;
		public float MaxHp;
	}

	private sealed class SummaryPlayer
	{
		public int Slot;
		public bool Active;
		public bool Computer;
		public int Team;
		public float Gold;
		public float Wood;
		public float Stone;
	}
}

public class SplitWorldReport
{
	private readonly List<string> _failures = new();

	public IReadOnlyList<string> Failures => _failures;
	public int HostUnitCount { get; set; }
	public List<int> ClientUnitCounts { get; } = new();
	public float WorstPositionDeltaMeters { get; set; }

	public void AddFailure(string failure) => _failures.Add(failure);

	public string Render()
	{
		var builder = new StringBuilder();
		builder.AppendLine("=== Match world comparison across processes ===");
		builder.AppendLine($"Authority units: {HostUnitCount}");
		for (int index = 0; index < ClientUnitCounts.Count; index++)
		{
			builder.AppendLine($"Peer {index + 2} units: {ClientUnitCounts[index]}");
		}

		builder.AppendLine($"Worst reported position difference between the authority and a screen: {WorstPositionDeltaMeters:F2} m");
		builder.AppendLine($"Failures: {_failures.Count}");
		for (int index = 0; index < _failures.Count; index++) builder.AppendLine($"  - {_failures[index]}");
		builder.AppendLine($"Verdict: {(_failures.Count == 0 ? "PASS" : "FAIL")}");
		return builder.ToString();
	}
}
