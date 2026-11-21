using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Realm.Tools.BalanceOptimizer;

public partial class BalanceCatalog
{
	public List<BalanceTuningTarget> Targets { get; } = new();
	public string MapName { get; private set; } = string.Empty;
	public string MetadataPath { get; private set; } = string.Empty;
	public string ElixirSourcePath { get; private set; } = string.Empty;
	public int HeroCount { get; private set; }

	public int Count => Targets.Count;

	public int TunableCount => CountOf(tunable: true);

	public int FrozenCount => CountOf(tunable: false);

	public IReadOnlyList<string> HeroIds
	{
		get
		{
			var identifiers = new List<string>();
			var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (BalanceTuningTarget target in Targets)
			{
				if (target.Source != BalanceSource.Metadata) continue;
				if (!seen.Add(target.Owner)) continue;
				identifiers.Add(target.Owner);
			}

			return identifiers;
		}
	}

	public static BalanceCatalog Build(string mapPath)
	{
		string folder = Path.GetFullPath(mapPath);
		string metadataPath = Path.Combine(folder, "metadata.json");
		if (!File.Exists(metadataPath))
		{
			throw new FileNotFoundException($"the map folder '{folder}' carries no metadata.json", metadataPath);
		}

		var catalog = new BalanceCatalog
		{
			MetadataPath = metadataPath,
			ElixirSourcePath = Path.Combine(folder, "MapScript.Elixirs.cs")
		};

		JsonNode root = JsonNode.Parse(File.ReadAllText(metadataPath))
			?? throw new InvalidOperationException($"'{metadataPath}' is empty");

		catalog.MapName = ReadMapName(root, folder);

		JsonArray units = root["Templates"]?["Units"] as JsonArray
			?? throw new InvalidOperationException($"'{metadataPath}' carries no Templates.Units");

		foreach (JsonNode? unit in units)
		{
			if (unit == null) continue;
			if (!ReadBoolean(unit["IsHero"])) continue;
			if (!ReadNumber(unit["Damage"], out double damage) || damage <= 0.0) continue;
			if (!ReadNumber(unit["AttackCooldown"], out double attackCooldown) || attackCooldown <= 0.0) continue;

			string templateId = unit["TemplateID"]?.GetValue<string>() ?? string.Empty;
			if (templateId.Length == 0) continue;

			bool added = false;
			added |= catalog.AddUnitField(unit, templateId, "MaxHp", 0.70, 1.40);
			added |= catalog.AddUnitField(unit, templateId, "Damage", 0.70, 1.40);
			added |= catalog.AddUnitField(unit, templateId, "Armor", 0.50, 2.00);
			added |= catalog.AddUnitField(unit, templateId, "Range", 0.85, 1.15);
			added |= catalog.AddUnitField(unit, templateId, "AttackCooldown", 0.85, 1.15);
			added |= catalog.AddUnitField(unit, templateId, "Speed", 0.90, 1.10);

			if (added) catalog.HeroCount++;
		}

		if (catalog.HeroCount == 0)
		{
			throw new InvalidOperationException($"'{metadataPath}' marks no unit as a hero");
		}

		catalog.ReadElixirTable();
		return catalog;
	}

	public void DescribeTargets(TextWriter writer)
	{
		foreach (BalanceTuningTarget target in Targets)
		{
			writer.WriteLine(
				$"{target.Describe(),-52} {target.BaseValue.ToString("0.###", CultureInfo.InvariantCulture),10} "
				+ $"[{target.MinimumMultiplier:0.00}..{target.MaximumMultiplier:0.00}] "
				+ (target.IsTunable ? "tunable" : "frozen"));
		}
	}

	public double Clamp(BalanceTuningTarget target, double multiplier) =>
		Math.Clamp(multiplier, target.MinimumMultiplier, target.MaximumMultiplier);

	public double ResolveValue(BalanceTuningTarget target, double multiplier)
	{
		double scaled = target.BaseValue * Clamp(target, multiplier);
		if (target.IsInteger) return Math.Round(scaled, MidpointRounding.AwayFromZero);
		return Math.Round(scaled, 4, MidpointRounding.AwayFromZero);
	}

	private int CountOf(bool tunable)
	{
		int count = 0;
		foreach (BalanceTuningTarget target in Targets)
		{
			if (target.IsTunable == tunable) count++;
		}

		return count;
	}

	private bool AddUnitField(JsonNode unit, string templateId, string fieldName, double minimumMultiplier, double maximumMultiplier)
	{
		JsonNode? node = unit[fieldName];
		if (node == null) return false;
		if (node is not JsonValue value) return false;
		if (!value.TryGetValue(out double baseValue)) return false;

		Targets.Add(new BalanceTuningTarget
		{
			Source = BalanceSource.Metadata,
			Owner = templateId,
			Field = fieldName,
			BaseValue = baseValue,
			MinimumMultiplier = minimumMultiplier,
			MaximumMultiplier = maximumMultiplier,
			IsInteger = IsIntegerLiteral(value, baseValue),
			IsTunable = true
		});

		return true;
	}

	private void ReadElixirTable()
	{
		if (!File.Exists(ElixirSourcePath)) return;

		foreach (string line in File.ReadAllLines(ElixirSourcePath))
		{
			Match match = ElixirRow().Match(line);
			if (!match.Success) continue;

			string identifier = match.Groups["id"].Value;
			AddElixirField(identifier, "Amount", match.Groups["amount"].Value, 0.60, 1.60);
			AddElixirField(identifier, "CostGold", match.Groups["cost"].Value, 0.70, 1.30);
		}
	}

	private void AddElixirField(string identifier, string fieldName, string literal, double minimumMultiplier, double maximumMultiplier)
	{
		if (!double.TryParse(literal, NumberStyles.Float, CultureInfo.InvariantCulture, out double baseValue)) return;

		Targets.Add(new BalanceTuningTarget
		{
			Source = BalanceSource.ElixirTable,
			Owner = identifier,
			Field = fieldName,
			BaseValue = baseValue,
			MinimumMultiplier = minimumMultiplier,
			MaximumMultiplier = maximumMultiplier,
			IsInteger = false,
			IsTunable = false
		});
	}

	private static string ReadMapName(JsonNode root, string folder)
	{
		string? declared = root["MapProperties"]?["MapName"]?.GetValue<string>();
		if (!string.IsNullOrWhiteSpace(declared)) return declared;
		return Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
	}

	private static bool ReadBoolean(JsonNode? node)
	{
		if (node is not JsonValue value) return false;
		return value.TryGetValue(out bool parsed) && parsed;
	}

	private static bool ReadNumber(JsonNode? node, out double parsed)
	{
		parsed = 0.0;
		if (node is not JsonValue value) return false;
		return value.TryGetValue(out parsed);
	}

	private static bool IsIntegerLiteral(JsonValue value, double baseValue)
	{
		if (Math.Abs(baseValue - Math.Round(baseValue)) > 0.0000001) return false;
		string text = value.ToJsonString();
		return !text.Contains('.', StringComparison.Ordinal) && !text.Contains('e', StringComparison.OrdinalIgnoreCase);
	}

	[GeneratedRegex(@"new\(""(?<id>[a-z0-9_]+)"",\s*ElixirHook\.(?<hook>\w+),\s*(?<amount>[-\d.]+)f,\s*(?<stacks>\d+),\s*(?<cost>[-\d.]+)f\)")]
	private static partial Regex ElixirRow();
}
