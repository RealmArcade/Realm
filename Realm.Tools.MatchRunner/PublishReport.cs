using System.Globalization;
using System.Text;
using Realm.Ecs.AI.Training;

namespace Realm.Tools.MatchRunner;

public static class PublishReport
{
	public static string Write(
		string reportPath,
		MatchRunOptions options,
		TrainingRunResult training,
		PublishDuelResult duel,
		PublishSmokeResult smoke,
		PublishVerdict verdict,
		string profilePath,
		string manifestPath,
		int validationCandidates)
	{
		var builder = new StringBuilder();
		builder.Append("# Publish gate — ").Append(training.Profile.MapName).AppendLine();
		builder.AppendLine();
		builder.Append("Veredicto: **").Append(verdict.Publish ? "PUBLICAR" : "NO PUBLICAR").AppendLine("**");
		builder.AppendLine();
		builder.Append("Criterio: duelo fuera de muestra ≥ ").Append(PublishGate.FormatPercent(PublishGate.MinimumWinRate))
			.Append(" con p < ").Append(PublishGate.MaximumPValue.ToString("0.00", CultureInfo.InvariantCulture))
			.Append(", 0 partidas fallidas y sin aviso de desajuste de pesos.").AppendLine();

		if (!verdict.Publish)
		{
			builder.AppendLine();
			builder.AppendLine("## Bloqueos");
			builder.AppendLine();
			foreach (string blocker in verdict.Blockers) builder.Append("- ").AppendLine(blocker);
		}

		builder.AppendLine();
		builder.AppendLine("## Titular — duelo fuera de muestra");
		builder.AppendLine();
		builder.Append("- semillas ").Append(duel.SeedFirst).Append('-').Append(duel.SeedLast)
			.Append(" · 2 asientos espejados · n = ").Append(duel.Matches).Append(" partidas (")
			.Append(duel.SeatMatches[0]).Append(" + ").Append(duel.SeatMatches[1]).AppendLine(")");
		builder.Append("- campeón: ").Append(duel.Wins).Append(" victoria(s) · ").Append(duel.Losses).Append(" derrota(s) · ")
			.Append(duel.Draws).Append(" tabla(s) → ").Append(PublishGate.FormatPercent(verdict.WinRate))
			.Append(" IC95 [").Append(PublishGate.FormatPercent(verdict.IntervalLow)).Append("–").Append(PublishGate.FormatPercent(verdict.IntervalHigh))
			.Append("] · p = ").Append(PublishGate.FormatPValue(verdict.PValue)).AppendLine(" (una cola, H0 = 50%)");

		for (int seat = 1; seat <= 2; seat++)
		{
			int matches = duel.SeatMatches[seat - 1];
			double rate = matches > 0 ? (duel.SeatWins[seat - 1] + 0.5 * duel.SeatDraws[seat - 1]) / matches : 0.0;
			(double low, double high) = PublishGate.Wilson(duel.SeatWins[seat - 1] + 0.5 * duel.SeatDraws[seat - 1], matches);
			builder.Append("- asiento ").Append(seat).Append(": ").Append(PublishGate.FormatPercent(rate))
				.Append(" IC95 [").Append(PublishGate.FormatPercent(low)).Append("–").Append(PublishGate.FormatPercent(high))
				.Append("] · n = ").Append(matches).AppendLine();
		}

		builder.Append("- datos: `").Append(duel.SeatOnePath).Append("` (n=").Append(duel.SeatMatches[0]).Append(") · `")
			.Append(duel.SeatTwoPath).Append("` (n=").Append(duel.SeatMatches[1]).AppendLine(")");
		if (duel.Failures > 0)
		{
			builder.Append("- partidas fallidas: ").AppendLine(duel.Failures.ToString(CultureInfo.InvariantCulture));
			foreach (string failure in duel.FailureDetails) builder.Append("  - ").AppendLine(failure);
		}
		else
		{
			builder.AppendLine("- partidas fallidas: 0");
		}

		builder.AppendLine();
		builder.AppendLine("## Selección interna (no publicable)");
		builder.AppendLine();
		builder.Append("- validación: ").Append(PublishGate.FormatPercent(training.ValidationFitness))
			.Append(" sobre ").Append(validationCandidates)
			.Append(" semillas de validación por campeón de generación, campeón elegido como el máximo entre ")
			.Append(options.TrainGenerations).Append(options.TrainGenerations == 1 ? " generación" : " generaciones").AppendLine();
		builder.Append("- este número es un máximo sobre la muestra de selección y está inflado por construcción; no se publica").AppendLine();
		builder.Append("- fitness mejor por generación: ");
		for (int generation = 0; generation < training.GenerationBestFitness.Count; generation++)
		{
			if (generation > 0) builder.Append(" · ");
			builder.Append(PublishGate.FormatPercent(training.GenerationBestFitness[generation]));
		}

		builder.AppendLine();
		builder.AppendLine();
		builder.AppendLine("## Humo del perfil candidato");
		builder.AppendLine();
		builder.Append("- ").Append(smoke.Matches).Append(smoke.Matches == 1 ? " partida" : " partidas")
			.Append(" con el perfil cargado desde el directorio de datos del mapa, ").Append(smoke.Failures)
			.AppendLine(smoke.Failures == 1 ? " fallida" : " fallidas");
		builder.Append("- rasgos de candidato registrados por el mapa: ").Append(smoke.RegisteredFeatures)
			.Append(" · pesos de mapa del perfil: ").AppendLine(smoke.ProfileMapWeights.ToString(CultureInfo.InvariantCulture));
		builder.Append("- aviso de desajuste de pesos: ").AppendLine(smoke.WarningFound ? "sí" : "ninguno");
		foreach (string detail in smoke.Details) builder.Append("  - ").AppendLine(detail);
		if (smoke.LogPath.Length > 0) builder.Append("- log: `").Append(smoke.LogPath).AppendLine("`");

		builder.AppendLine();
		builder.AppendLine("## Perfil");
		builder.AppendLine();
		builder.Append("- perfil: `").Append(profilePath).Append("` (").Append(verdict.Publish ? "escrito" : "no escrito").AppendLine(")");
		builder.Append("- manifiesto de entrenamiento: `").Append(manifestPath).AppendLine("`");
		builder.Append("- pesos de mapa: ");
		for (int index = 0; index < training.Profile.MapCandidateWeights.Length; index++)
		{
			if (index > 0) builder.Append(", ");
			builder.Append(training.Profile.MapCandidateWeights[index].ToString("0.#######", CultureInfo.InvariantCulture));
		}

		builder.AppendLine();
		builder.Append("- rasgos de género: ").Append(options.TrainGenre).Append(" · ")
			.Append(training.Profile.Weights.Length.ToString(CultureInfo.InvariantCulture)).AppendLine(" peso(s)");

		string directory = Path.GetDirectoryName(Path.GetFullPath(reportPath)) ?? Directory.GetCurrentDirectory();
		Directory.CreateDirectory(directory);
		File.WriteAllText(reportPath, builder.ToString(), new UTF8Encoding(false));
		return reportPath;
	}
}
