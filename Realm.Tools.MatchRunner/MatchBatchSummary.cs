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
		var heroWins = new Dictionary<string, int>();
		var firstPickPicks = new Dictionary<string, int>();
		var firstPickWins = new Dictionary<string, int>();
		var firstPickTilePicks = new Dictionary<(string HeroId, float TileX, float TileZ), int>();
		var firstPickTileWins = new Dictionary<(string HeroId, float TileX, float TileZ), int>();
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

				bool isWinner = seatIndex == winner;
				if (seat.TryGetProperty("firstDraftHeroId", out JsonElement firstDraftValue))
				{
					string firstDraftHeroId = firstDraftValue.GetString() ?? string.Empty;
					if (firstDraftHeroId.Length > 0)
					{
						firstPickPicks[firstDraftHeroId] = firstPickPicks.TryGetValue(firstDraftHeroId, out int firstPicksOfHero) ? firstPicksOfHero + 1 : 1;
						if (isWinner)
						{
							firstPickWins[firstDraftHeroId] = firstPickWins.TryGetValue(firstDraftHeroId, out int firstWinsOfHero) ? firstWinsOfHero + 1 : 1;
						}

						float tileX = seat.TryGetProperty("firstDraftTileX", out JsonElement tileXValue) ? tileXValue.GetSingle() : 0f;
						float tileZ = seat.TryGetProperty("firstDraftTileZ", out JsonElement tileZValue) ? tileZValue.GetSingle() : 0f;
						var tileKey = (firstDraftHeroId, MathF.Round(tileX, 1), MathF.Round(tileZ, 1));
						firstPickTilePicks[tileKey] = firstPickTilePicks.TryGetValue(tileKey, out int tilePicks) ? tilePicks + 1 : 1;
						if (isWinner)
						{
							firstPickTileWins[tileKey] = firstPickTileWins.TryGetValue(tileKey, out int tileWins) ? tileWins + 1 : 1;
						}
					}
				}

				if (!seat.TryGetProperty("heroPickIds", out JsonElement picks)) continue;
				foreach (JsonElement pick in picks.EnumerateArray())
				{
					string heroId = pick.GetString() ?? string.Empty;
					if (heroId.Length == 0) continue;
					heroPicks[heroId] = heroPicks.TryGetValue(heroId, out int picksOfHero) ? picksOfHero + 1 : 1;
					if (isWinner)
					{
						heroWins[heroId] = heroWins.TryGetValue(heroId, out int winsOfHero) ? winsOfHero + 1 : 1;
					}
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

			writer.WriteStartArray("heroStats");
			foreach (KeyValuePair<string, int> entry in heroPicks.OrderByDescending(entry => entry.Value))
			{
				string heroId = entry.Key;
				int picks = entry.Value;
				int wins = heroWins.TryGetValue(heroId, out int w) ? w : 0;
				double rate = picks > 0 ? (double)wins / picks : 0.0;
				writer.WriteStartObject();
				writer.WriteString("heroId", heroId);
				writer.WriteNumber("picks", picks);
				writer.WriteNumber("wins", wins);
				writer.WriteNumber("winRate", Math.Round(rate, 4));
				writer.WriteNumber("winRatePercent", Math.Round(rate * 100.0, 2));
				writer.WriteEndObject();
			}

			writer.WriteEndArray();

			writer.WriteStartArray("firstPickStats");
			foreach (KeyValuePair<string, int> entry in firstPickPicks.OrderByDescending(entry => entry.Value))
			{
				string heroId = entry.Key;
				int picks = entry.Value;
				int wins = firstPickWins.TryGetValue(heroId, out int firstWins) ? firstWins : 0;
				double rate = picks > 0 ? (double)wins / picks : 0.0;
				double ci95 = picks > 0 ? 1.96 * Math.Sqrt(rate * (1.0 - rate) / picks) : 0.0;
				writer.WriteStartObject();
				writer.WriteString("heroId", heroId);
				writer.WriteNumber("picks", picks);
				writer.WriteNumber("wins", wins);
				writer.WriteNumber("winRate", Math.Round(rate, 4));
				writer.WriteNumber("winRatePercent", Math.Round(rate * 100.0, 2));
				writer.WriteNumber("ci95Percent", Math.Round(ci95 * 100.0, 2));
				writer.WriteEndObject();
			}

			writer.WriteEndArray();

			writer.WriteStartArray("firstPickTiles");
			foreach (KeyValuePair<(string HeroId, float TileX, float TileZ), int> entry in firstPickTilePicks.OrderByDescending(entry => entry.Value))
			{
				int picks = entry.Value;
				int wins = firstPickTileWins.TryGetValue(entry.Key, out int tileWins) ? tileWins : 0;
				double rate = picks > 0 ? (double)wins / picks : 0.0;
				writer.WriteStartObject();
				writer.WriteString("heroId", entry.Key.HeroId);
				writer.WriteNumber("tileX", Math.Round(entry.Key.TileX, 1));
				writer.WriteNumber("tileZ", Math.Round(entry.Key.TileZ, 1));
				writer.WriteNumber("picks", picks);
				writer.WriteNumber("wins", wins);
				writer.WriteNumber("winRate", Math.Round(rate, 4));
				writer.WriteNumber("winRatePercent", Math.Round(rate * 100.0, 2));
				writer.WriteEndObject();
			}

			writer.WriteEndArray();
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
