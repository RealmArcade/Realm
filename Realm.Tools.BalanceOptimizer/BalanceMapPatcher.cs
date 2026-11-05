using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Realm.Tools.BalanceOptimizer;

public class BalanceMapPatcher
{
	private readonly string _metadataPath;
	private readonly string _backupPath;

	public BalanceMapPatcher(string mapPath)
	{
		_metadataPath = Path.Combine(Path.GetFullPath(mapPath), "metadata.json");
		_backupPath = _metadataPath + ".balance-backup";
	}

	public string MetadataPath => _metadataPath;

	public string BackupPath => _backupPath;

	public bool HasPendingBackup => File.Exists(_backupPath);

	public void RestorePendingPatch()
	{
		if (!HasPendingBackup) return;
		File.Copy(_backupPath, _metadataPath, overwrite: true);
		File.Delete(_backupPath);
	}

	public void Apply(BalanceCatalog catalog, BalanceGenome genome)
	{
		if (!HasPendingBackup) File.Copy(_metadataPath, _backupPath);

		JsonNode root = JsonNode.Parse(File.ReadAllText(_backupPath))
			?? throw new InvalidOperationException($"'{_backupPath}' is empty");

		JsonArray units = root["Templates"]?["Units"] as JsonArray
			?? throw new InvalidOperationException($"'{_backupPath}' carries no Templates.Units");

		var unitsByTemplate = new Dictionary<string, JsonNode>(StringComparer.OrdinalIgnoreCase);
		foreach (JsonNode? unit in units)
		{
			if (unit == null) continue;
			string templateId = unit["TemplateID"]?.GetValue<string>() ?? string.Empty;
			if (templateId.Length == 0) continue;
			unitsByTemplate[templateId] = unit;
		}

		for (int index = 0; index < catalog.Count; index++)
		{
			BalanceTuningTarget target = catalog.Targets[index];
			if (target.Source != BalanceSource.Metadata) continue;
			if (!unitsByTemplate.TryGetValue(target.Owner, out JsonNode? unit)) continue;

			double resolved = catalog.ResolveValue(target, genome.Multipliers[index]);
			unit[target.Field] = target.IsInteger
				? JsonValue.Create((long)Math.Round(resolved, MidpointRounding.AwayFromZero))
				: JsonValue.Create(resolved);
		}

		string patched = root.ToJsonString(new JsonSerializerOptions
		{
			WriteIndented = true,
			Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
		});

		File.WriteAllText(_metadataPath, patched);
	}

	public void Restore()
	{
		if (!HasPendingBackup) return;
		File.Copy(_backupPath, _metadataPath, overwrite: true);
		File.Delete(_backupPath);
	}
}
