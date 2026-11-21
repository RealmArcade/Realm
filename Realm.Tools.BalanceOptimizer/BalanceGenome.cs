using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Realm.Tools.BalanceOptimizer;

public class BalanceGenome
{
	public string SchemaVersion { get; init; } = "1.0.0";
	public string MapName { get; init; } = string.Empty;
	public double[] Multipliers { get; set; } = Array.Empty<double>();

	public static BalanceGenome CreateNeutral(BalanceCatalog catalog, string mapName)
	{
		var multipliers = new double[catalog.Count];
		Array.Fill(multipliers, 1.0);
		return new BalanceGenome { MapName = mapName, Multipliers = multipliers };
	}

	public BalanceGenome Clone() => new()
	{
		SchemaVersion = SchemaVersion,
		MapName = MapName,
		Multipliers = (double[])Multipliers.Clone()
	};

	public string ToJson()
	{
		var payload = new
		{
			schemaVersion = SchemaVersion,
			mapName = MapName,
			multipliers = Multipliers
		};

		return JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
	}

	public static BalanceGenome FromJson(string json, int expectedCount)
	{
		JsonNode root = JsonNode.Parse(json) ?? throw new InvalidOperationException("the genome file is empty");
		JsonArray multipliers = root["multipliers"] as JsonArray
			?? throw new InvalidOperationException("the genome file carries no multipliers");

		if (multipliers.Count != expectedCount)
		{
			throw new InvalidOperationException(
				$"the genome carries {multipliers.Count.ToString(CultureInfo.InvariantCulture)} multipliers "
				+ $"and the map catalog has {expectedCount.ToString(CultureInfo.InvariantCulture)} fields");
		}

		var values = new double[expectedCount];
		for (int index = 0; index < expectedCount; index++)
		{
			values[index] = multipliers[index]!.GetValue<double>();
		}

		return new BalanceGenome
		{
			SchemaVersion = root["schemaVersion"]?.GetValue<string>() ?? "1.0.0",
			MapName = root["mapName"]?.GetValue<string>() ?? string.Empty,
			Multipliers = values
		};
	}

	public string DescribeDiff(BalanceCatalog catalog)
	{
		var builder = new System.Text.StringBuilder();
		for (int index = 0; index < catalog.Count && index < Multipliers.Length; index++)
		{
			BalanceTuningTarget target = catalog.Targets[index];
			double multiplier = Multipliers[index];
			if (Math.Abs(multiplier - 1.0) < 0.005) continue;

			double resolved = catalog.ResolveValue(target, multiplier);
			builder.AppendLine(
				$"{target.Describe(),-52} {target.BaseValue.ToString("0.###", CultureInfo.InvariantCulture),10} -> "
				+ $"{resolved.ToString("0.###", CultureInfo.InvariantCulture),10} "
				+ $"({multiplier.ToString("0.000", CultureInfo.InvariantCulture)}x)");
		}

		return builder.ToString();
	}
}
