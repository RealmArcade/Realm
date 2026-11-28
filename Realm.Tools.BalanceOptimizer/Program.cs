using System.Globalization;
using System.Text;
using Realm.Tools.BalanceOptimizer;

BalanceOptimizerOptions options;
try
{
	options = BalanceOptimizerOptions.Parse(args);
}
catch (ArgumentException exception)
{
	Console.Error.WriteLine(exception.Message);
	Console.Error.WriteLine();
	Console.Error.WriteLine(BalanceOptimizerOptions.Usage);
	return 2;
}

if (options.ShowHelp)
{
	Console.WriteLine(BalanceOptimizerOptions.Usage);
	return 0;
}

BalanceCatalog catalog = BalanceCatalog.Build(options.MapPath);
Console.WriteLine($"map:    {catalog.MapName}");
Console.WriteLine($"fields: {catalog.Count} ({catalog.TunableCount} tunable, {catalog.FrozenCount} frozen)");
Console.WriteLine($"heroes: {catalog.HeroCount}");

if (options.DescribeOnly)
{
	catalog.DescribeTargets(Console.Out);
	return 0;
}

string mapFolder = Path.GetFullPath(options.MapPath);
if (options.ProfilePath.Length == 0) options.ProfilePath = Path.Combine(mapFolder, $"{catalog.MapName}_bot.json");
if (options.OutputPath.Length == 0) options.OutputPath = Path.Combine(mapFolder, "metadata.balance.genome.json");
if (options.ReportPath.Length == 0) options.ReportPath = Path.Combine(mapFolder, "metadata.balance.optimizer.json");

if (!File.Exists(options.ProfilePath))
{
	Console.Error.WriteLine($"the skill guard needs a trained profile and '{options.ProfilePath}' does not exist");
	return 3;
}

var patcher = new BalanceMapPatcher(mapFolder);
if (patcher.HasPendingBackup)
{
	Console.WriteLine($"restoring the metadata.json an interrupted run left patched");
	patcher.RestorePendingPatch();
}

var evaluator = new BalanceEvaluator(mapFolder, options.MaximumSeconds, options.Parallel);
var optimizer = new BalanceOptimizer();

BalanceOptimizerReport report;
try
{
	report = optimizer.Run(options, catalog, evaluator, patcher, Console.Out);
}
finally
{
	patcher.Restore();
}

File.WriteAllText(options.OutputPath, report.BestGenome?.ToJson() ?? string.Empty, new UTF8Encoding(false));
File.WriteAllText(options.ReportPath, report.ToJson(catalog, options), new UTF8Encoding(false));

Console.WriteLine($"genome: {options.OutputPath}");
Console.WriteLine($"report: {options.ReportPath}");

if (report.BestEvaluation != null)
{
	Console.WriteLine(
		$"best fitness {report.BestEvaluation.Fitness.ToString("0.000", CultureInfo.InvariantCulture)}, "
		+ $"worst hero {report.BestEvaluation.WorstHeroId} at "
		+ $"{report.BestEvaluation.MaximumDeviation.ToString("0.000", CultureInfo.InvariantCulture)}, "
		+ $"skill guard {report.BestEvaluation.SkillGuardWinRate.ToString("0.000", CultureInfo.InvariantCulture)}");
}

string difference = report.BestGenome?.DescribeDiff(catalog) ?? string.Empty;
if (difference.Length > 0)
{
	Console.WriteLine();
	Console.WriteLine("changed fields:");
	Console.Write(difference);
}

return 0;
