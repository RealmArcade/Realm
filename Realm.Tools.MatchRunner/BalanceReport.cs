using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Realm.Tools.MatchRunner;

public class BalanceReport
{
	public const double TolerancePoints = 2.0;
	public const int MinimumMatches = 2000;

	private const double Z95 = 1.959963985;
	private const double HeroViolationZ = 2.0;

	private readonly record struct SeatRecord(int Seat, string[] HeroPickIds);

	private readonly record struct MatchRecord(int Seed, int WinnerSeat, bool Completed, SeatRecord[] Seats);

	private readonly record struct HeroEffect(string HeroId, int Picks, int Paired, double EffectPoints, double ZScore)
	{
		public bool Violates => Math.Abs(ZScore) > HeroViolationZ;
	}

	private readonly record struct DraftResult(string Label, string Scale, bool Circular, int Decided, int BetterWins, double StandardDeviation, double PointsPerStandardDeviation, double PointIntervalLow, double PointIntervalHigh, double WorstDecileRate, double BestDecileRate, int DecileSize)
	{
		public double Rate => Decided > 0 ? (double)BetterWins / Decided * 100.0 : 0.0;

		public bool Violates => !Circular && Decided > 0 && Math.Abs(Rate - 50.0) > TolerancePoints;
	}

	private readonly record struct BotResult(string Label, bool Measured, int Matches, int Wins, string Reason)
	{
		public double Rate => Matches > 0 ? (double)Wins / Matches * 100.0 : 0.0;

		public bool Violates => Measured && Math.Abs(Rate - 50.0) > TolerancePoints;
	}

	public static string Write(IReadOnlyList<string> outcomeLines, MatchRunOptions options, string markdownPath)
	{
		List<MatchRecord> matches = ParseOutcomes(outcomeLines);
		Dictionary<string, double> powerByHero = ReadHeroPower(options.MapPath);
		string markdown = BuildMarkdown(matches, options, powerByHero);
		string json = BuildJson(matches, options, powerByHero);

		string directory = Path.GetDirectoryName(Path.GetFullPath(markdownPath)) ?? Directory.GetCurrentDirectory();
		Directory.CreateDirectory(directory);
		File.WriteAllText(markdownPath, markdown, new UTF8Encoding(false));
		File.WriteAllText(Path.ChangeExtension(markdownPath, ".json"), json, new UTF8Encoding(false));

		return BuildDigest(matches, options, powerByHero);
	}

	private static List<MatchRecord> ParseOutcomes(IReadOnlyList<string> outcomeLines)
	{
		var matches = new List<MatchRecord>(outcomeLines.Count);
		foreach (string line in outcomeLines)
		{
			if (string.IsNullOrWhiteSpace(line)) continue;

			using JsonDocument document = JsonDocument.Parse(line);
			JsonElement root = document.RootElement;
			if (root.TryGetProperty("error", out _)) continue;
			if (!root.TryGetProperty("perSeat", out JsonElement perSeat) || perSeat.ValueKind != JsonValueKind.Array) continue;

			int seed = root.TryGetProperty("seed", out JsonElement seedValue) ? seedValue.GetInt32() : -1;
			int winnerSeat = root.TryGetProperty("winnerSeat", out JsonElement winnerValue) ? winnerValue.GetInt32() : -1;
			bool completed = root.TryGetProperty("completed", out JsonElement completedValue) && completedValue.GetBoolean();

			var seats = new List<SeatRecord>(perSeat.GetArrayLength());
			foreach (JsonElement seat in perSeat.EnumerateArray())
			{
				if (!seat.TryGetProperty("seat", out JsonElement seatValue)) continue;

				string[] heroPickIds = Array.Empty<string>();
				if (seat.TryGetProperty("heroPickIds", out JsonElement picks) && picks.ValueKind == JsonValueKind.Array)
				{
					var heroes = new List<string>(picks.GetArrayLength());
					foreach (JsonElement pick in picks.EnumerateArray())
					{
						string heroId = pick.GetString() ?? string.Empty;
						if (heroId.Length > 0) heroes.Add(heroId);
					}

					heroPickIds = heroes.ToArray();
				}

				seats.Add(new SeatRecord(seatValue.GetInt32(), heroPickIds));
			}

			matches.Add(new MatchRecord(seed, winnerSeat, completed, seats.ToArray()));
		}

		return matches;
	}

	private static Dictionary<string, double> ReadHeroPower(string mapPath)
	{
		var powerByHero = new Dictionary<string, double>(StringComparer.Ordinal);
		string metadataPath = Path.Combine(mapPath, "metadata.json");
		if (!File.Exists(metadataPath)) return powerByHero;

		using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(metadataPath));
		if (!document.RootElement.TryGetProperty("Templates", out JsonElement templates)) return powerByHero;
		if (!templates.TryGetProperty("Units", out JsonElement units)) return powerByHero;

		IEnumerable<JsonElement> unitValues = units.ValueKind switch
		{
			JsonValueKind.Array => units.EnumerateArray(),
			JsonValueKind.Object => units.EnumerateObject().Select(property => property.Value),
			_ => Array.Empty<JsonElement>()
		};

		foreach (JsonElement value in unitValues)
		{
			if (value.ValueKind != JsonValueKind.Object) continue;
			if (!value.TryGetProperty("TemplateID", out JsonElement templateId) || templateId.ValueKind != JsonValueKind.String) continue;
			if (!value.TryGetProperty("IsHero", out JsonElement isHero) || !isHero.GetBoolean()) continue;

			double maxHealth = ReadNumber(value, "MaxHp");
			double armor = ReadNumber(value, "Armor");
			double damage = ReadNumber(value, "Damage");
			double attackCooldown = ReadNumber(value, "AttackCooldown");
			if (attackCooldown <= 0.0 || damage <= 0.0 || maxHealth <= 0.0) continue;

			double effectiveHealth = maxHealth * (1.0 + armor / 10.0);
			powerByHero[templateId.GetString() ?? string.Empty] = effectiveHealth * (damage / attackCooldown);
		}

		return powerByHero;
	}

	private static double ReadNumber(JsonElement element, string propertyName)
	{
		if (!element.TryGetProperty(propertyName, out JsonElement value)) return 0.0;
		return value.ValueKind == JsonValueKind.Number ? value.GetDouble() : 0.0;
	}

	private static List<(int Seat, int Wins)> SeatTotals(List<MatchRecord> matches)
	{
		var wins = new Dictionary<int, int>();
		var order = new List<int>();
		foreach (MatchRecord match in matches)
		{
			if (!match.Completed) continue;
			foreach (SeatRecord seat in match.Seats)
			{
				if (wins.ContainsKey(seat.Seat)) continue;
				wins[seat.Seat] = 0;
				order.Add(seat.Seat);
			}

			if (wins.ContainsKey(match.WinnerSeat)) wins[match.WinnerSeat]++;
		}

		order.Sort();
		var totals = new List<(int Seat, int Wins)>(order.Count);
		foreach (int seat in order) totals.Add((seat, wins[seat]));
		return totals;
	}

	private static int CountCompleted(List<MatchRecord> matches)
	{
		int completed = 0;
		foreach (MatchRecord match in matches) if (match.Completed && match.Seats.Length >= 2) completed++;
		return completed;
	}

	private static List<HeroEffect> HeroEffects(List<MatchRecord> matches)
	{
		var picksByHero = new Dictionary<string, int>(StringComparer.Ordinal);
		var pairedByHero = new Dictionary<string, int>(StringComparer.Ordinal);
		var scoreByHero = new Dictionary<string, int>(StringComparer.Ordinal);

		foreach (MatchRecord match in matches)
		{
			if (!match.Completed || match.Seats.Length < 2) continue;

			for (int seatIndex = 0; seatIndex < match.Seats.Length; seatIndex++)
			{
				foreach (string heroId in match.Seats[seatIndex].HeroPickIds)
				{
					picksByHero[heroId] = picksByHero.TryGetValue(heroId, out int picks) ? picks + 1 : 1;
				}
			}

			var seenHeroes = new HashSet<string>(StringComparer.Ordinal);
			for (int seatIndex = 0; seatIndex < match.Seats.Length; seatIndex++)
			{
				foreach (string heroId in match.Seats[seatIndex].HeroPickIds) seenHeroes.Add(heroId);
			}

			foreach (string heroId in seenHeroes)
			{
				int holders = 0;
				bool holderWon = false;
				for (int seatIndex = 0; seatIndex < match.Seats.Length; seatIndex++)
				{
					if (Array.IndexOf(match.Seats[seatIndex].HeroPickIds, heroId) < 0) continue;
					holders++;
					holderWon = match.Seats[seatIndex].Seat == match.WinnerSeat;
				}

				if (holders != 1) continue;

				pairedByHero[heroId] = pairedByHero.TryGetValue(heroId, out int pairs) ? pairs + 1 : 1;
				int score = holderWon ? 1 : -1;
				scoreByHero[heroId] = scoreByHero.TryGetValue(heroId, out int total) ? total + score : score;
			}
		}

		var effects = new List<HeroEffect>(picksByHero.Count);
		foreach (KeyValuePair<string, int> entry in picksByHero)
		{
			int paired = pairedByHero.TryGetValue(entry.Key, out int pairs) ? pairs : 0;
			int score = scoreByHero.TryGetValue(entry.Key, out int total) ? total : 0;
			double effectPoints = paired > 0 ? (double)score / paired * 100.0 : 0.0;
			double standardError = paired > 0 ? 100.0 / Math.Sqrt(paired) : double.PositiveInfinity;
			double zScore = paired > 0 ? effectPoints / standardError : 0.0;
			effects.Add(new HeroEffect(entry.Key, entry.Value, paired, effectPoints, zScore));
		}

		effects.Sort((left, right) => left.EffectPoints.CompareTo(right.EffectPoints));
		return effects;
	}

	private static DraftResult AnalyseDraft(List<MatchRecord> matches, IReadOnlyDictionary<string, double> weightByHero, string label, string scale, bool circular, bool useLogWeights)
	{
		var deltas = new List<double>(matches.Count);
		var outcomes = new List<int>(matches.Count);

		foreach (MatchRecord match in matches)
		{
			if (!match.Completed || match.Seats.Length < 2) continue;

			double left = TeamWeight(match.Seats[0], weightByHero, useLogWeights);
			double right = TeamWeight(match.Seats[1], weightByHero, useLogWeights);

			deltas.Add(left - right);
			outcomes.Add(match.Seats[0].Seat == match.WinnerSeat ? 1 : 0);
		}

		int decided = 0;
		int betterWins = 0;
		for (int index = 0; index < deltas.Count; index++)
		{
			if (Math.Abs(deltas[index]) < 1e-9) continue;
			decided++;
			bool betterIsLeft = deltas[index] > 0.0;
			bool winnerIsLeft = outcomes[index] == 1;
			if (betterIsLeft == winnerIsLeft) betterWins++;
		}

		double meanDelta = Mean(deltas);
		double varianceDelta = Variance(deltas, meanDelta);
		double meanOutcome = Mean(outcomes.ConvertAll(value => (double)value));
		double covariance = 0.0;
		for (int index = 0; index < deltas.Count; index++)
		{
			covariance += (deltas[index] - meanDelta) * (outcomes[index] - meanOutcome);
		}

		double slope = varianceDelta > 0.0 ? covariance / (deltas.Count * varianceDelta) : 0.0;
		double residual = 0.0;
		for (int index = 0; index < deltas.Count; index++)
		{
			double predicted = meanOutcome + slope * (deltas[index] - meanDelta);
			residual += (outcomes[index] - predicted) * (outcomes[index] - predicted);
		}

		double slopeStandardError = deltas.Count > 2 && varianceDelta > 0.0
			? Math.Sqrt(residual / (deltas.Count - 2) / (deltas.Count * varianceDelta))
			: 0.0;

		double standardDeviation = Math.Sqrt(varianceDelta);

		var ordered = new List<(double Delta, int Outcome)>(deltas.Count);
		for (int index = 0; index < deltas.Count; index++) ordered.Add((deltas[index], outcomes[index]));
		ordered.Sort((left, right) => left.Delta.CompareTo(right.Delta));

		int decileSize = ordered.Count / 10;
		double worstDecileRate = decileSize > 0 ? LeftRate(ordered.GetRange(0, decileSize)) : 0.0;
		double bestDecileRate = decileSize > 0 ? LeftRate(ordered.GetRange(ordered.Count - decileSize, decileSize)) : 0.0;

		return new DraftResult(
			label,
			scale,
			circular,
			decided,
			betterWins,
			standardDeviation,
			slope * standardDeviation * 100.0,
			(slope - Z95 * slopeStandardError) * standardDeviation * 100.0,
			(slope + Z95 * slopeStandardError) * standardDeviation * 100.0,
			worstDecileRate,
			bestDecileRate,
			decileSize);
	}

	private static double TeamWeight(SeatRecord seat, IReadOnlyDictionary<string, double> weightByHero, bool useLogWeights)
	{
		double total = 0.0;
		foreach (string heroId in seat.HeroPickIds)
		{
			if (!weightByHero.TryGetValue(heroId, out double weight)) continue;
			total += useLogWeights ? Math.Log(weight) : weight;
		}

		return total;
	}

	private static double LeftRate(List<(double Delta, int Outcome)> subset)
	{
		if (subset.Count == 0) return 0.0;
		int wins = 0;
		foreach ((double _, int outcome) in subset) wins += outcome;
		return (double)wins / subset.Count * 100.0;
	}

	private static Dictionary<string, double> MeasuredWeights(List<HeroEffect> effects)
	{
		var weights = new Dictionary<string, double>(StringComparer.Ordinal);
		foreach (HeroEffect effect in effects) weights[effect.HeroId] = effect.EffectPoints;
		return weights;
	}

	private static List<BotResult> BotResults(List<MatchRecord> matches, MatchRunOptions options)
	{
		var results = new List<BotResult>();

		var policies = new SortedDictionary<int, string>();
		for (int seat = 1; seat <= options.Seats; seat++)
		{
			policies[seat] = options.SeatPolicies.TryGetValue(seat, out string? seatPolicy) ? seatPolicy : options.Policy;
		}

		var distinct = new HashSet<string>(policies.Values, StringComparer.OrdinalIgnoreCase);
		if (distinct.Count < 2)
		{
			results.Add(new BotResult($"asientos con la misma política '{options.Policy}'", false, 0, 0, "todos los asientos jugaron la misma política, así que no hay nada que comparar"));
			return results;
		}

		foreach (KeyValuePair<int, string> policy in policies)
		{
			var rivals = new List<string>();
			foreach (KeyValuePair<int, string> other in policies)
			{
				if (other.Key == policy.Key) continue;
				rivals.Add(other.Value);
			}

			if (rivals.Count == 0 || !rivals.Contains(policy.Value, StringComparer.OrdinalIgnoreCase)) continue;
			if (policies.Count > 2) continue;

			int played = 0;
			int wins = 0;
			foreach (MatchRecord match in matches)
			{
				if (!match.Completed) continue;
				bool present = false;
				foreach (SeatRecord seat in match.Seats) if (seat.Seat == policy.Key) present = true;
				if (!present) continue;
				played++;
				if (match.WinnerSeat == policy.Key) wins++;
			}

			results.Add(new BotResult($"asiento {policy.Key} '{policy.Value}' contra '{rivals[0]}'", played > 0, played, wins, string.Empty));
		}

		if (results.Count == 0)
		{
			results.Add(new BotResult("políticas por asiento", false, 0, 0, "las políticas de este lote no describen un solo duelo"));
		}

		return results;
	}

	private static double Mean(List<double> values) => values.Count > 0 ? Sum(values) / values.Count : 0.0;

	private static double Sum(List<double> values)
	{
		double total = 0.0;
		foreach (double value in values) total += value;
		return total;
	}

	private static double Variance(List<double> values, double mean)
	{
		if (values.Count == 0) return 0.0;
		double total = 0.0;
		foreach (double value in values) total += (value - mean) * (value - mean);
		return total / values.Count;
	}

	private static (double Low, double High) Wilson(int successes, int trials)
	{
		if (trials <= 0) return (0.0, 0.0);

		double proportion = (double)successes / trials;
		double denominator = 1.0 + Z95 * Z95 / trials;
		double center = (proportion + Z95 * Z95 / (2.0 * trials)) / denominator;
		double margin = Z95 * Math.Sqrt(proportion * (1.0 - proportion) / trials + Z95 * Z95 / (4.0 * trials * trials)) / denominator;
		return ((center - margin) * 100.0, (center + margin) * 100.0);
	}

	private static string Points(double value) => value.ToString("F1", CultureInfo.InvariantCulture);

	private static string Rate(double value) => value.ToString("F1", CultureInfo.InvariantCulture) + "%";

	private static string SeedSpan(List<MatchRecord> matches, MatchRunOptions options)
	{
		int first = int.MaxValue;
		int last = int.MinValue;
		foreach (MatchRecord match in matches)
		{
			if (match.Seed < 0) continue;
			if (match.Seed < first) first = match.Seed;
			if (match.Seed > last) last = match.Seed;
		}

		if (first == int.MaxValue) return options.SeedStart + "-" + options.SeedEnd;
		string firstText = first.ToString(CultureInfo.InvariantCulture);
		return first == last ? firstText : firstText + "-" + last.ToString(CultureInfo.InvariantCulture);
	}

	private static string BuildMarkdown(List<MatchRecord> matches, MatchRunOptions options, Dictionary<string, double> powerByHero)
	{
		List<HeroEffect> effects = HeroEffects(matches);
		var measuredWeights = MeasuredWeights(effects);
		DraftResult measured = AnalyseDraft(matches, measuredWeights, "efecto medido por héroe (circular, no decide)", "puntos", circular: true, useLogWeights: false);
		DraftResult stated = AnalyseDraft(matches, powerByHero, "poder declarado (ehp·dps)", "ln", circular: false, useLogWeights: true);
		List<(int Seat, int Wins)> seats = SeatTotals(matches);
		List<BotResult> bots = BotResults(matches, options);

		int completed = CountCompleted(matches);
		var builder = new StringBuilder();
		builder.Append("# Balance report — ").AppendLine(options.MapPath);
		builder.AppendLine();
		builder.Append("seeds ").Append(SeedSpan(matches, options))
			.Append(" · ").Append(completed).Append(" partidas completas · ").Append(options.Seats).Append(" asientos · política ").Append(options.Policy)
			.Append(" · criterio 50 ± ").Append(Points(TolerancePoints)).Append(" puntos a n ≥ ").Append(MinimumMatches).AppendLine();
		builder.AppendLine();
		if (completed < MinimumMatches)
		{
			builder.Append("> muestra insuficiente: ").Append(completed).Append(" de ").Append(MinimumMatches).AppendLine(" partidas. Los números no alcanzan para cerrar T1.");
			builder.AppendLine();
		}

		builder.AppendLine("## Asiento");
		builder.AppendLine();
		builder.AppendLine("| asiento | victorias | tasa | IC95 | veredicto |");
		builder.AppendLine("|---|---|---|---|---|");
		bool seatsOk = true;
		foreach ((int seatNumber, int wins) in seats)
		{
			double rate = completed > 0 ? (double)wins / completed * 100.0 : 0.0;
			(double low, double high) = Wilson(wins, completed);
			bool ok = Math.Abs(rate - 50.0) <= TolerancePoints;
			seatsOk &= ok;
			builder.Append("| ").Append(seatNumber).Append(" | ").Append(wins).Append(" | ").Append(Rate(rate))
				.Append(" | ").Append(Points(low)).Append("–").Append(Rate(high))
				.Append(" | ").Append(ok ? "cumple" : "no cumple").AppendLine(" |");
		}

		builder.AppendLine();
		builder.AppendLine("## Héroe (sorteo)");
		builder.AppendLine();
		builder.AppendLine("Efecto pareado: solo cuentan las partidas en que el héroe está en un asiento y no en el otro.");
		builder.AppendLine();
		builder.AppendLine("| héroe | picks | pareadas | efecto | z | veredicto |");
		builder.AppendLine("|---|---|---|---|---|---|");
		int violations = 0;
		foreach (HeroEffect effect in effects)
		{
			if (effect.Violates) violations++;
			builder.Append("| ").Append(effect.HeroId).Append(" | ").Append(effect.Picks).Append(" | ").Append(effect.Paired)
				.Append(" | ").Append(Points(effect.EffectPoints)).Append(" | ").Append(Points(effect.ZScore))
				.Append(" | ").Append(effect.Violates ? "no cumple" : "cumple").AppendLine(" |");
		}

		builder.AppendLine();
		builder.AppendLine("## Draft completo");
		builder.AppendLine();
		AppendDraftRow(builder, measured);
		AppendDraftRow(builder, stated);
		builder.AppendLine();
		builder.AppendLine("## Bot");
		builder.AppendLine();
		foreach (BotResult bot in bots)
		{
			if (!bot.Measured)
			{
				builder.Append("- ").Append(bot.Label).Append(": no medido — ").AppendLine(bot.Reason);
				continue;
			}

			(double low, double high) = Wilson(bot.Wins, bot.Matches);
			builder.Append("- ").Append(bot.Label).Append(": ").Append(Rate(bot.Rate))
				.Append(" [").Append(Points(low)).Append("–").Append(Rate(high)).Append("] sobre ").Append(bot.Matches)
				.Append(bot.Violates ? " — no supera el 50" : " — cumple").AppendLine();
		}

		builder.AppendLine();
		builder.AppendLine("## Veredicto");
		builder.AppendLine();
		builder.Append("- asiento: ").AppendLine(seatsOk ? "cumple" : "no cumple");
		builder.Append("- héroe: ").Append(violations == 0 ? "cumple" : $"no cumple ({violations} fuera de |z| > 2)").AppendLine();
		builder.Append("- draft: ").AppendLine(stated.Violates ? "no cumple" : "cumple");
		bool anyBotMeasured = false;
		bool anyBotViolated = false;
		foreach (BotResult bot in bots)
		{
			if (!bot.Measured) continue;
			anyBotMeasured = true;
			anyBotViolated |= bot.Violates;
		}

		builder.Append("- bot: ").AppendLine(!anyBotMeasured ? "no medido" : anyBotViolated ? "no supera el azar" : "decide mejor que el azar");

		return builder.ToString();
	}

	private static void AppendDraftRow(StringBuilder builder, DraftResult result)
	{
		(double low, double high) = Wilson(result.BetterWins, result.Decided);
		builder.Append("- ").Append(result.Label).Append(": el asiento con mejor draft gana ").Append(Rate(result.Rate))
			.Append(" [").Append(Points(low)).Append("–").Append(Rate(high)).Append("] (n=").Append(result.Decided).Append(") · ")
			.Append("sd ").Append(Points(result.StandardDeviation)).Append(' ').Append(result.Scale).Append(" · 1 sd vale ").Append(Points(result.PointsPerStandardDeviation))
			.Append(" puntos [").Append(Points(result.PointIntervalLow)).Append("–").Append(Points(result.PointIntervalHigh)).Append("] · ")
			.Append("decil peor ").Append(Rate(result.WorstDecileRate)).Append(" / decil mejor ").Append(Rate(result.BestDecileRate))
			.Append(" · ").AppendLine(result.Circular ? "circular, no decide" : result.Violates ? "no cumple" : "cumple");
	}

	private static string BuildJson(List<MatchRecord> matches, MatchRunOptions options, Dictionary<string, double> powerByHero)
	{
		List<HeroEffect> effects = HeroEffects(matches);
		DraftResult measured = AnalyseDraft(matches, MeasuredWeights(effects), "measured", "points", circular: true, useLogWeights: false);
		DraftResult stated = AnalyseDraft(matches, powerByHero, "stated", "log", circular: false, useLogWeights: true);
		List<(int Seat, int Wins)> seats = SeatTotals(matches);
		List<BotResult> bots = BotResults(matches, options);
		int completed = CountCompleted(matches);

		using var stream = new MemoryStream();
		using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
		{
			writer.WriteStartObject();
			writer.WriteString("map", options.MapPath);
			writer.WriteNumber("seedFirst", options.SeedStart);
			writer.WriteNumber("seedLast", options.SeedEnd);
			writer.WriteNumber("matches", completed);
			writer.WriteNumber("minimumMatches", MinimumMatches);
			writer.WriteNumber("tolerancePoints", TolerancePoints);
			writer.WriteString("policy", options.Policy);

			writer.WriteStartArray("seats");
			foreach ((int seatNumber, int wins) in seats)
			{
				(double low, double high) = Wilson(wins, completed);
				writer.WriteStartObject();
				writer.WriteNumber("seat", seatNumber);
				writer.WriteNumber("wins", wins);
				writer.WriteNumber("rate", Math.Round(completed > 0 ? (double)wins / completed * 100.0 : 0.0, 2));
				writer.WriteNumber("intervalLow", Math.Round(low, 2));
				writer.WriteNumber("intervalHigh", Math.Round(high, 2));
				writer.WriteEndObject();
			}

			writer.WriteEndArray();

			writer.WriteStartArray("heroes");
			foreach (HeroEffect effect in effects)
			{
				writer.WriteStartObject();
				writer.WriteString("heroId", effect.HeroId);
				writer.WriteNumber("picks", effect.Picks);
				writer.WriteNumber("paired", effect.Paired);
				writer.WriteNumber("effectPoints", Math.Round(effect.EffectPoints, 2));
				writer.WriteNumber("zScore", Math.Round(effect.ZScore, 2));
				writer.WriteBoolean("violates", effect.Violates);
				writer.WriteEndObject();
			}

			writer.WriteEndArray();

			WriteDraft(writer, "draftMeasured", measured);
			WriteDraft(writer, "draftStated", stated);

			writer.WriteStartArray("bots");
			foreach (BotResult bot in bots)
			{
				(double low, double high) = Wilson(bot.Wins, bot.Matches);
				writer.WriteStartObject();
				writer.WriteString("label", bot.Label);
				writer.WriteBoolean("measured", bot.Measured);
				writer.WriteNumber("matches", bot.Matches);
				writer.WriteNumber("wins", bot.Wins);
				writer.WriteNumber("rate", Math.Round(bot.Rate, 2));
				writer.WriteNumber("intervalLow", Math.Round(low, 2));
				writer.WriteNumber("intervalHigh", Math.Round(high, 2));
				writer.WriteBoolean("beatsChance", bot.Measured && bot.Rate > 50.0 + TolerancePoints);
				writer.WriteString("reason", bot.Reason);
				writer.WriteEndObject();
			}

			writer.WriteEndArray();
			writer.WriteEndObject();
		}

		return Encoding.UTF8.GetString(stream.ToArray());
	}

	private static void WriteDraft(Utf8JsonWriter writer, string name, DraftResult result)
	{
		(double low, double high) = Wilson(result.BetterWins, result.Decided);
		writer.WriteStartObject(name);
		writer.WriteNumber("decided", result.Decided);
		writer.WriteNumber("betterDraftWins", result.BetterWins);
		writer.WriteNumber("rate", Math.Round(result.Rate, 2));
		writer.WriteNumber("intervalLow", Math.Round(low, 2));
		writer.WriteNumber("intervalHigh", Math.Round(high, 2));
		writer.WriteNumber("standardDeviation", Math.Round(result.StandardDeviation, 4));
		writer.WriteNumber("pointsPerStandardDeviation", Math.Round(result.PointsPerStandardDeviation, 2));
		writer.WriteNumber("worstDecileRate", Math.Round(result.WorstDecileRate, 2));
		writer.WriteNumber("bestDecileRate", Math.Round(result.BestDecileRate, 2));
		writer.WriteString("scale", result.Scale);
		writer.WriteBoolean("circular", result.Circular);
		writer.WriteBoolean("violates", result.Violates);
		writer.WriteEndObject();
	}

	private static string BuildDigest(List<MatchRecord> matches, MatchRunOptions options, Dictionary<string, double> powerByHero)
	{
		List<HeroEffect> effects = HeroEffects(matches);
		DraftResult measured = AnalyseDraft(matches, MeasuredWeights(effects), "medido", "puntos", circular: true, useLogWeights: false);
		DraftResult stated = AnalyseDraft(matches, powerByHero, "declarado", "ln", circular: false, useLogWeights: true);
		int completed = CountCompleted(matches);
		int violations = 0;
		foreach (HeroEffect effect in effects) if (effect.Violates) violations++;

		var builder = new StringBuilder();
		builder.Append("balance: ").Append(completed).Append(" partidas · draft ").Append(Rate(stated.Rate))
			.Append(" [").Append(Points(Wilson(stated.BetterWins, stated.Decided).Low)).Append('–')
			.Append(Points(Wilson(stated.BetterWins, stated.Decided).High)).Append("] · ")
			.Append(violations).Append(" héroe(s) fuera de |z| > 2");

		List<(int Seat, int Wins)> seats = SeatTotals(matches);
		foreach ((int seatNumber, int wins) in seats)
		{
			builder.Append(" · asiento ").Append(seatNumber).Append(' ').Append(Rate(completed > 0 ? (double)wins / completed * 100.0 : 0.0));
		}

		if (completed < MinimumMatches)
		{
			builder.Append(" · muestra insuficiente (").Append(MinimumMatches).Append(" mínimas)");
		}

		return builder.ToString();
	}
}
