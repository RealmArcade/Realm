using System.Globalization;
using System.Text.Json;

namespace Realm.Tools.BalanceOptimizer;

public class BalanceOptimizerReport
{
	public List<double> GenerationBestFitness { get; } = new();
	public List<double> GenerationBaselineFitness { get; } = new();
	public List<string> GenerationWorstHero { get; } = new();
	public BalanceGenome? BestGenome { get; set; }
	public BalanceEvaluation? BestEvaluation { get; set; }

	public string ToJson(BalanceCatalog catalog, BalanceOptimizerOptions options)
	{
		var heroes = new List<object>();
		if (BestEvaluation != null)
		{
			foreach (BalanceHeroResult hero in BestEvaluation.Heroes)
			{
				heroes.Add(new
				{
					heroId = hero.HeroId,
					matches = hero.Matches,
					wins = hero.Wins,
					winRate = Math.Round(hero.WinRate, 4),
					deviation = Math.Round(hero.Deviation, 4)
				});
			}
		}

		var changedFields = new List<object>();
		if (BestGenome != null)
		{
			for (int index = 0; index < catalog.Count && index < BestGenome.Multipliers.Length; index++)
			{
				BalanceTuningTarget target = catalog.Targets[index];
				double multiplier = BestGenome.Multipliers[index];
				if (Math.Abs(multiplier - 1.0) < 0.005) continue;

				changedFields.Add(new
				{
					source = target.Source.ToString(),
					owner = target.Owner,
					field = target.Field,
					from = target.BaseValue,
					to = catalog.ResolveValue(target, multiplier),
					multiplier = Math.Round(multiplier, 4)
				});
			}
		}

		var payload = new
		{
			schemaVersion = "1.0.0",
			mapName = catalog.MapName,
			generations = options.Generations,
			population = options.Population,
			seedsPerHero = options.SeedsPerHero,
			tunableFields = catalog.TunableCount,
			frozenFields = catalog.FrozenCount,
			baseSeed = options.Seed,
			skillGuard = new
			{
				seeds = options.SkillGuardSeeds,
				tolerance = options.SkillGuardTolerance,
				winRate = BestEvaluation == null ? 0.0 : Math.Round(BestEvaluation.SkillGuardWinRate, 4)
			},
			generationBestFitness = GenerationBestFitness,
			generationBaselineFitness = GenerationBaselineFitness,
			generationWorstHero = GenerationWorstHero,
			bestFitness = BestEvaluation == null ? 0.0 : Math.Round(BestEvaluation.Fitness, 4),
			maximumDeviation = BestEvaluation == null ? 0.0 : Math.Round(BestEvaluation.MaximumDeviation, 4),
			worstHeroId = BestEvaluation?.WorstHeroId ?? string.Empty,
			heroes,
			changedFields,
			createdUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)
		};

		return JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
	}
}

public class BalanceOptimizer
{
	public BalanceOptimizerReport Run(
		BalanceOptimizerOptions options,
		BalanceCatalog catalog,
		BalanceEvaluator evaluator,
		BalanceMapPatcher patcher,
		TextWriter log)
	{
		var report = new BalanceOptimizerReport();
		var population = new List<BalanceGenome>();

		BalanceGenome neutral = BalanceGenome.CreateNeutral(catalog, catalog.MapName);
		population.Add(neutral);

		var random = new Random(BalanceEvaluator.DeriveSeed(options.Seed, 17));
		for (int candidateIndex = 1; candidateIndex < options.Population; candidateIndex++)
		{
			BalanceGenome candidate = BalanceGenome.CreateNeutral(catalog, catalog.MapName);
			for (int gene = 0; gene < catalog.Count; gene++)
			{
				BalanceTuningTarget target = catalog.Targets[gene];
				if (!target.IsTunable) continue;
				candidate.Multipliers[gene] = catalog.Clamp(target, 1.0 + (random.NextDouble() * 0.16 - 0.08));
			}

			population.Add(candidate);
		}

		BalanceGenome best = neutral.Clone();
		BalanceEvaluation bestEvaluation = new() { Fitness = double.NegativeInfinity, MaximumDeviation = 1.0 };

		for (int generation = 0; generation < options.Generations; generation++)
		{
			int generationSeed = BalanceEvaluator.DeriveSeed(options.Seed, generation + 1);
			double generationBestFitness = double.NegativeInfinity;
			double baselineFitness = 0.0;
			double baselineGuardRate = 0.0;
			string generationWorstHero = string.Empty;

			for (int candidateIndex = 0; candidateIndex < population.Count; candidateIndex++)
			{
				BalanceGenome candidate = population[candidateIndex];
				patcher.Apply(catalog, candidate);

				BalanceEvaluation evaluation;
				try
				{
					evaluation = evaluator.Evaluate(
						catalog.HeroIds,
						options.SeedsPerHero,
						generationSeed,
						options.ProfilePath,
						options.SkillGuardSeeds);
				}
				finally
				{
					patcher.Restore();
				}

				if (candidateIndex == 0)
				{
					baselineFitness = evaluation.Fitness;
					baselineGuardRate = evaluation.SkillGuardWinRate;
				}

				bool rejected = options.SkillGuardSeeds > 0
					&& candidateIndex > 0
					&& evaluation.SkillGuardWinRate < baselineGuardRate - options.SkillGuardTolerance;
				double fitness = rejected ? -1.0 : evaluation.Fitness;

				log.WriteLine(
					$"generation {generation + 1} candidate {candidateIndex + 1}/{population.Count}: "
					+ $"fitness {fitness.ToString("0.000", CultureInfo.InvariantCulture)} "
					+ $"worst {evaluation.WorstHeroId} at {evaluation.MaximumDeviation.ToString("0.000", CultureInfo.InvariantCulture)} "
					+ $"guard {evaluation.SkillGuardWinRate.ToString("0.000", CultureInfo.InvariantCulture)} "
					+ $"against baseline {baselineGuardRate.ToString("0.000", CultureInfo.InvariantCulture)}"
					+ (rejected ? " (rejected)" : string.Empty));

				if (fitness > generationBestFitness)
				{
					generationBestFitness = fitness;
					generationWorstHero = evaluation.WorstHeroId;
				}

				if (fitness <= bestEvaluation.Fitness) continue;
				bestEvaluation = evaluation;
				best = candidate.Clone();
			}

			report.GenerationBestFitness.Add(Math.Round(generationBestFitness, 4));
			report.GenerationBaselineFitness.Add(Math.Round(baselineFitness, 4));
			report.GenerationWorstHero.Add(generationWorstHero);

			log.WriteLine(
				$"generation {generation + 1}: best fitness {generationBestFitness.ToString("0.000", CultureInfo.InvariantCulture)}, "
				+ $"baseline {baselineFitness.ToString("0.000", CultureInfo.InvariantCulture)}");

			population = Breed(catalog, best, options.Population, BalanceEvaluator.DeriveSeed(options.Seed, generation + 1000));
		}

		report.BestGenome = best;
		report.BestEvaluation = bestEvaluation;
		return report;
	}

	private static List<BalanceGenome> Breed(BalanceCatalog catalog, BalanceGenome best, int populationSize, int seed)
	{
		var random = new Random(seed);
		var population = new List<BalanceGenome> { best.Clone() };

		while (population.Count < populationSize)
		{
			BalanceGenome child = best.Clone();
			for (int gene = 0; gene < catalog.Count; gene++)
			{
				BalanceTuningTarget target = catalog.Targets[gene];
				if (!target.IsTunable) continue;
				double mutation = random.NextDouble() * 0.10 - 0.05;
				child.Multipliers[gene] = catalog.Clamp(target, child.Multipliers[gene] + mutation);
			}

			population.Add(child);
		}

		return population;
	}
}
