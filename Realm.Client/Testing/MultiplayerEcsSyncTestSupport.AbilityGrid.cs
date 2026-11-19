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

	private readonly record struct AbilityGridRecord(float Time, int ServerEntityId, string UnitType, string Visible, string BuildVisible, int BuildOptionCount);

	/// <summary>
	///     Holds the two screens' command grids against each other. A map that withholds an option from a unit speaks to
	///     every screen about it, by name rather than by the unit's ability list, and those statements are the only thing
	///     that keeps the withheld option off the grid: a screen that loses them offers everything the unit's template
	///     declares, which reads as a screen playing a different game from the one beside it. The grid is dumped by each
	///     screen rather than computed here, so this compares exactly what the panel would draw.
	/// </summary>
	private static void CompareAbilityGridRecords(string hostDirectory, string clientDirectory, MultiplayerEcsSyncReport report)
	{
		Dictionary<string, AbilityGridRecord> hostRecords = ReadAbilityGridRecords(Path.Combine(hostDirectory, "host.log"));
		Dictionary<string, AbilityGridRecord> clientRecords = ReadAbilityGridRecords(Path.Combine(clientDirectory, "client.log"));
		if (hostRecords.Count == 0 || clientRecords.Count == 0)
		{
			report.AddSummaryLine("Ability grid parity: not compared, because one screen dumped no command grid.");
			return;
		}

		int compared = 0;
		int withheldSomewhere = 0;
		List<string> divergences = new();
		foreach (var entry in clientRecords)
		{
			if (!hostRecords.TryGetValue(entry.Key, out AbilityGridRecord hostRecord)) continue;
			if (Math.Abs(hostRecord.Time - entry.Value.Time) > AbilityGridMatchWindowSeconds) continue;

			compared++;
			if (entry.Value.BuildVisible.Split('|', StringSplitOptions.RemoveEmptyEntries).Length < entry.Value.BuildOptionCount) withheldSomewhere++;
			if (entry.Value.Visible != hostRecord.Visible || entry.Value.BuildVisible != hostRecord.BuildVisible)
			{
				divergences.Add(
					$"'{entry.Value.UnitType}' (server {entry.Value.ServerEntityId}) at {entry.Value.Time:F1}s: the client screen offered {DescribeCount(entry.Value.BuildVisible)} build option(s) and {DescribeCount(entry.Value.Visible)} listed abilit(ies), the host screen offered {DescribeCount(hostRecord.BuildVisible)} and {DescribeCount(hostRecord.Visible)}");
			}
		}

		report.AddSummaryLine($"Ability grid parity: compared {compared} unit grid(s) at matching times, {withheldSomewhere} of them with at least one option the map withheld.");
		if (withheldSomewhere == 0 && compared > 0)
		{
			report.AddSummaryLine("Ability grid parity: no unit had an option withheld, so this run could not have caught a lost withholding (the map declares none).");
		}

		if (divergences.Count == 0) return;

		report.AddFailure($"{divergences.Count} command grid(s) differed between the two screens, so at least one screen offered options the map withheld from the other.");
		for (int index = 0; index < Math.Min(divergences.Count, 8); index++) report.AddSummaryLine($"  {divergences[index]}");
	}

	private static string DescribeCount(string ids) => ids.Split('|', StringSplitOptions.RemoveEmptyEntries).Length.ToString(CultureInfo.InvariantCulture);

	/// <summary>
	///     Reads the last command grid each screen dumped for every unit. The key is the unit's template and the server
	///     identifier it holds, because one screen draws both seats' builders and those are two units of one type.
	/// </summary>
	private static Dictionary<string, AbilityGridRecord> ReadAbilityGridRecords(string logPath)
	{
		var records = new Dictionary<string, AbilityGridRecord>(StringComparer.Ordinal);
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

			records[$"{unitType}#{serverEntityId.ToString(CultureInfo.InvariantCulture)}"] = new AbilityGridRecord(time, serverEntityId, unitType, visible, buildVisible, buildOptionCount);
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
