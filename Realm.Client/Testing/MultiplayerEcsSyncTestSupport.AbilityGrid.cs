using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace Realm.Client.Testing;

public static partial class MultiplayerEcsSyncTestSupport
{
	/// <summary>
	///     How far apart two screens' records of the same unit may sit before they are held against each other. Each
	///     screen dumps its command grid once a second from its own game time, so the two clocks are compared at the
	///     nearest record rather than at an exact instant; a record further apart is one the other screen never took.
	/// </summary>
	private const float AbilityGridMatchWindowSeconds = 2.5f;

	/// <summary>
	///     How long before the end of the run a divergence has to hold before it is a failure. What a map declared about
	///     a unit is restated over a few descriptions after that unit's ability list changes, so a screen caught inside
	///     that window offers everything for a fraction of a second and then heals on its own. A grid that is still
	///     wrong for this long is one nothing will heal, which is the failure this check exists for.
	/// </summary>
	private const float AbilityGridPersistenceSeconds = 2.0f;

	private readonly record struct AbilityGridRecord(float Time, int ServerEntityId, string UnitType, string Visible, string BuildVisible, int BuildOptionCount);

	private readonly record struct AbilityGridPair(AbilityGridRecord Client, AbilityGridRecord Host);

	/// <summary>
	///     Holds the two screens' command grids against each other. A map that withholds an option from a unit speaks to
	///     every screen about it, by name rather than by the unit's ability list, and those statements are the only thing
	///     that keeps the withheld option off the grid: a screen that loses them offers everything the unit's template
	///     declares, which reads as a screen playing a different game from the one beside it. The grid is dumped by each
	///     screen rather than computed here, so this compares exactly what the panel would draw.
	/// </summary>
	private static void CompareAbilityGridRecords(string hostDirectory, string clientDirectory, MultiplayerEcsSyncReport report)
	{
		Dictionary<string, List<AbilityGridRecord>> hostRecords = ReadAbilityGridRecords(Path.Combine(hostDirectory, "host.log"));
		Dictionary<string, List<AbilityGridRecord>> clientRecords = ReadAbilityGridRecords(Path.Combine(clientDirectory, "client.log"));
		if (hostRecords.Count == 0 || clientRecords.Count == 0)
		{
			report.AddSummaryLine("Ability grid parity: not compared, because one screen dumped no command grid.");
			return;
		}

		int compared = 0;
		int withheldSomewhere = 0;
		int healedMismatchCount = 0;
		List<string> divergences = new();
		foreach (var entry in clientRecords)
		{
			if (!hostRecords.TryGetValue(entry.Key, out List<AbilityGridRecord>? hostUnitRecords)) continue;

			List<AbilityGridPair> pairs = PairAbilityGridRecords(entry.Value, hostUnitRecords);
			if (pairs.Count == 0) continue;

			AbilityGridRecord last = pairs[pairs.Count - 1].Client;
			compared++;
			if (last.BuildVisible.Split('|', StringSplitOptions.RemoveEmptyEntries).Length < last.BuildOptionCount) withheldSomewhere++;

			float steadyFrom = last.Time - AbilityGridPersistenceSeconds;
			bool differedAtAll = false;
			bool differedThroughoutTheTail = true;
			bool hasTailPair = false;
			for (int index = 0; index < pairs.Count; index++)
			{
				bool agrees = Agrees(pairs[index]);
				if (!agrees) differedAtAll = true;
				if (pairs[index].Client.Time < steadyFrom) continue;

				hasTailPair = true;
				if (agrees) differedThroughoutTheTail = false;
			}

			if (!differedAtAll) continue;

			// The failure is a grid nothing is going to heal: it is wrong now, and it was already wrong a persistence
			// window earlier. A single late sample of a restatement in flight is not that.
			if (!hasTailPair || !differedThroughoutTheTail)
			{
				healedMismatchCount++;
				continue;
			}

			divergences.Add(
				$"'{last.UnitType}' (server {last.ServerEntityId}) at {last.Time:F1}s: the client screen offered {DescribeCount(last.BuildVisible)} build option(s) and {DescribeCount(last.Visible)} listed abilit(ies), the host screen offered {DescribeCount(pairs[pairs.Count - 1].Host.BuildVisible)} and {DescribeCount(pairs[pairs.Count - 1].Host.Visible)}");
		}

		report.AddSummaryLine($"Ability grid parity: compared {compared} unit grid(s) at matching times, {withheldSomewhere} of them with at least one option the map withheld.");
		if (withheldSomewhere == 0 && compared > 0)
		{
			report.AddSummaryLine("Ability grid parity: no unit had an option withheld, so this run could not have caught a lost withholding (the map declares none).");
		}

		if (healedMismatchCount > 0)
		{
			report.AddSummaryLine($"Ability grid parity: {healedMismatchCount} unit grid(s) differed on a sample but not for {AbilityGridPersistenceSeconds:F0}s at the end of the run, which is what a restatement of a changed ability list looks like from a sample taken while it is in flight.");
		}

		if (divergences.Count == 0) return;

		report.AddFailure($"{divergences.Count} command grid(s) still differed at the end of the run, so at least one screen offered options the map withheld from the other and nothing was going to heal it.");
		for (int index = 0; index < Math.Min(divergences.Count, 8); index++) report.AddSummaryLine($"  {divergences[index]}");
	}

	/// <summary>
	///     Holds each of one screen's records against the other screen's nearest record in time. A screen dumps a grid
	///     every second, so pairing them this way compares the two grids at the same moments instead of at the two
	///     screens' first and last dumps, which can be seconds apart.
	/// </summary>
	private static List<AbilityGridPair> PairAbilityGridRecords(List<AbilityGridRecord> clientUnitRecords, List<AbilityGridRecord> hostUnitRecords)
	{
		List<AbilityGridPair> pairs = new();
		for (int index = 0; index < clientUnitRecords.Count; index++)
		{
			AbilityGridRecord clientRecord = clientUnitRecords[index];
			AbilityGridRecord nearest = default;
			float nearestDistance = float.MaxValue;
			for (int hostIndex = 0; hostIndex < hostUnitRecords.Count; hostIndex++)
			{
				float distance = Math.Abs(hostUnitRecords[hostIndex].Time - clientRecord.Time);
				if (distance >= nearestDistance) continue;

				nearestDistance = distance;
				nearest = hostUnitRecords[hostIndex];
			}

			if (nearestDistance > AbilityGridMatchWindowSeconds) continue;

			pairs.Add(new AbilityGridPair(clientRecord, nearest));
		}

		return pairs;
	}

	private static bool Agrees(AbilityGridPair pair) => pair.Client.Visible == pair.Host.Visible && pair.Client.BuildVisible == pair.Host.BuildVisible;

	private static string DescribeCount(string ids) => ids.Split('|', StringSplitOptions.RemoveEmptyEntries).Length.ToString(CultureInfo.InvariantCulture);

	/// <summary>
	///     Reads every command grid each screen dumped for every unit, in the order it dumped them. The key is the unit's
	///     template and the server identifier it holds, because one screen draws both seats' builders and those are two
	///     units of one type.
	/// </summary>
	private static Dictionary<string, List<AbilityGridRecord>> ReadAbilityGridRecords(string logPath)
	{
		var records = new Dictionary<string, List<AbilityGridRecord>>(StringComparer.Ordinal);
		if (!File.Exists(logPath)) return records;

		foreach (string line in File.ReadLines(logPath))
		{
			if (!line.Contains("[ABILITY_DUMP]", StringComparison.Ordinal)) continue;

			float time = ReadFloatField(line, "time=");
			int serverEntityId = (int)ReadFloatField(line, "server=");
			string unitType = ReadTextField(line, "type=");
			string visible = ReadTextField(line, "visible=");
			string buildVisible = ReadTextField(line, "buildVisibleIds=");
			int buildOptionCount = (int)ReadFloatField(line, "buildOptions=");
			if (unitType.Length == 0) continue;

			string key = $"{unitType}#{serverEntityId.ToString(CultureInfo.InvariantCulture)}";
			if (!records.TryGetValue(key, out List<AbilityGridRecord>? unitRecords))
			{
				unitRecords = new List<AbilityGridRecord>();
				records[key] = unitRecords;
			}

			unitRecords.Add(new AbilityGridRecord(time, serverEntityId, unitType, visible, buildVisible, buildOptionCount));
		}

		return records;
	}

	private static float ReadFloatField(string line, string field)
	{
		int start = line.IndexOf(field, StringComparison.Ordinal);
		if (start < 0) return 0f;

		start += field.Length;
		int end = line.IndexOf(' ', start);
		if (end < 0) end = line.Length;

		return float.TryParse(line.AsSpan(start, end - start), NumberStyles.Float, CultureInfo.InvariantCulture, out float value) ? value : 0f;
	}

	private static string ReadTextField(string line, string field)
	{
		int start = line.IndexOf(field, StringComparison.Ordinal);
		if (start < 0) return string.Empty;

		start += field.Length;
		int end = line.IndexOf(' ', start);
		if (end < 0) end = line.Length;

		return line.Substring(start, end - start);
	}
}
