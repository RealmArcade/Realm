using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Realm.Tools.MatchRunner;

public class MatchBatchSummary
{
	public static string Build(IReadOnlyList<string> outcomeLines, MatchRunOptions options, double wallClockSeconds, int parallel)
	{
		var seatGold = new Dictionary<int, double>();
		var seatDamage = new Dictionary<int, double>();
		var seatDrafts = new Dictionary<int, double>();
		var seatHeroes = new Dictionary<int, double>();
		var seatUpgrades = new Dictionary<int, double>();
		var heroPicks = new Dictionary<string, int>();
		var winners = new Dictionary<int, int>();
		var failures = new List<(int Seed, string Error)>();

		string mapName = string.Empty;
		int completed = 0;

		foreach (string line in outcomeLines)
		{
			if (string.IsNullOrWhiteSpace(line)) continue;

			using JsonDocument document = JsonDocument.Parse(line);
			JsonElement root = document.RootElement;
			int seed = root.TryGetProperty("seed", out JsonElement seedValue) ? seedValue.GetInt32() : -1;

			if (root.TryGetProperty("error", out JsonElement errorValue))
			{
				failures.Add((seed, errorValue.GetString() ?? "unknown failure"));
				continue;
			}

			if (mapName.Length == 0 && root.TryGetProperty("mapName", out JsonElement nameValue))
			{
				mapName = nameValue.GetString() ?? string.Empty;
			}

			if (root.TryGetProperty("completed", out JsonElement completedValue) && completedValue.GetBoolean()) completed++;

			int winner = root.TryGetProperty("winnerSeat", out JsonElement winnerValue) ? winnerValue.GetInt32() : -1;
			winners[winner] = winners.TryGetValue(winner, out int winnerCount) ? winnerCount + 1 : 1;

			if (!root.TryGetProperty("perSeat", out JsonElement perSeat)) continue;
			foreach (JsonElement seat in perSeat.EnumerateArray())
			{
				int seatIndex = seat.GetProperty("seat").GetInt32();
				Accumulate(seatGold, seatIndex, seat, "goldSpent");
				Accumulate(seatDamage, seatIndex, seat, "damageDealt");
				Accumulate(seatDrafts, seatIndex, seat, "unitsDrafted");
				Accumulate(seatHeroes, seatIndex, seat, "heroesPicked");
				Accumulate(seatUpgrades, seatIndex, seat, "upgradesBought");

				if (!seat.TryGetProperty("heroPickIds", out JsonElement picks)) continue;
				foreach (JsonElement pick in picks.EnumerateArray())
				{
					string heroId = pick.GetString() ?? string.Empty;
					if (heroId.Length == 0) continue;
					heroPicks[heroId] = heroPicks.TryGetValue(heroId, out int picksOfHero) ? picksOfHero + 1 : 1;
				}
			}
		}

		int matches = outcomeLines.Count - failures.Count;
		double seatsPlayed = Math.Max(1, matches);
		double matchesPerSecond = wallClockSeconds > 0.0 ? matches / wallClockSeconds : 0.0;

		ArrayBufferWriter<byte> buffer = new(1024);
		using (Utf8JsonWriter writer = new(buffer, new JsonWriterOptions { Indented = true }))
		{
			writer.WriteStartObject();
			writer.WriteString("mapName", mapName);
			writer.WriteStartObject("seeds");
			writer.WriteNumber("first", options.SeedStart);
			writer.WriteNumber("last", options.SeedEnd);
			writer.WriteEndObject();
			writer.WriteNumber("seats", options.Seats);
			writer.WriteNumber("maxSeconds", options.MaxSeconds);
			writer.WriteNumber("parallel", parallel);
			writer.WriteNumber("matches", matches);
			writer.WriteNumber("completed", completed);
			writer.WriteNumber("failures", failures.Count);

			writer.WriteStartObject("winners");
			foreach (KeyValuePair<int, int> entry in winners.OrderBy(entry => entry.Key))
			{
				writer.WriteNumber(entry.Key.ToString(CultureInfo.InvariantCulture), entry.Value);
			}

			writer.WriteEndObject();

			writer.WriteStartArray("perSeat");
			for (int seat = 1; seat <= options.Seats; seat++)
			{
				writer.WriteStartObject();
				writer.WriteNumber("seat", seat);
				writer.WriteNumber("averageGoldSpent", Average(seatGold, seat, seatsPlayed));
				writer.WriteNumber("averageDamageDealt", Average(seatDamage, seat, seatsPlayed));
				writer.WriteNumber("averageUnitsDrafted", Average(seatDrafts, seat, seatsPlayed));
				writer.WriteNumber("averageHeroesPicked", Average(seatHeroes, seat, seatsPlayed));
				writer.WriteNumber("averageUpgradesBought", Average(seatUpgrades, seat, seatsPlayed));
				writer.WriteEndObject();
			}

			writer.WriteEndArray();

			writer.WriteStartObject("heroPicks");
			foreach (KeyValuePair<string, int> entry in heroPicks.OrderByDescending(entry => entry.Value))
			{
				writer.WriteNumber(entry.Key, entry.Value);
			}

			writer.WriteEndObject();

			writer.WriteNumber("wallClockSeconds", Math.Round(wallClockSeconds, 3));
			writer.WriteNumber("matchesPerSecond", Math.Round(matchesPerSecond, 3));

			writer.WriteStartArray("failureDetails");
			for (int index = 0; index < failures.Count; index++)
			{
				writer.WriteStartObject();
				writer.WriteNumber("seed", failures[index].Seed);
				writer.WriteString("error", failures[index].Error);
				writer.WriteEndObject();
			}

			writer.WriteEndArray();
			writer.WriteEndObject();
		}

		return Encoding.UTF8.GetString(buffer.WrittenSpan);
	}

	private static void Accumulate(Dictionary<int, double> totals, int seat, JsonElement element, string propertyName)
	{
		if (!element.TryGetProperty(propertyName, out JsonElement value)) return;
		double amount = value.GetDouble();
		totals[seat] = totals.TryGetValue(seat, out double current) ? current + amount : amount;
	}

	private static double Average(Dictionary<int, double> totals, int seat, double matches)
	{
		return totals.TryGetValue(seat, out double total) ? Math.Round(total / matches, 3) : 0.0;
	}

	public static string BuildErrorLine(int seed, string message)
	{
		ArrayBufferWriter<byte> buffer = new(256);
		using (Utf8JsonWriter writer = new(buffer, new JsonWriterOptions { Indented = false }))
		{
			writer.WriteStartObject();
			writer.WriteNumber("seed", seed);
			writer.WriteString("endReason", "error");
			writer.WriteString("error", message);
			writer.WriteEndObject();
		}

		return Encoding.UTF8.GetString(buffer.WrittenSpan);
	}
}
