using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Realm.Client.Utils;

namespace Realm.Client.Services;

/// <summary>
///     Converts authored combat data from the rated-armor and armor-class model to the two
///     resistance model. Armor classes are gone, so a unit keeps only its armor points; every
///     elemental damage label collapses onto the damage type it was always mitigated as, because the
///     engine now mitigates physical, magic and pure and nothing else. The damage/armor matrix that
///     existed only to differentiate those labels goes with them.
/// </summary>
public class Migration_0_0_6_Resistances : IMapMigration
{
	public string FromVersion => "v0.0.5";
	public string ToVersion => "v0.0.6";
	public string Description => "Replace rated armor and armor classes with armor and magic resistance points";

	private static readonly Dictionary<string, string> DamageTypeMap = new(StringComparer.OrdinalIgnoreCase)
	{
		["physical"] = "physical",
		["normal"] = "physical",
		["pierce"] = "physical",
		["melee"] = "physical",
		["ranged"] = "physical",
		["magic"] = "magic",
		["fire"] = "magic",
		["frost"] = "magic",
		["nature"] = "magic",
		["holy"] = "magic",
		["shadow"] = "magic",
		["pure"] = "pure"
	};

	public MigrationResult Up(string mapDirectory, IProgress<MigrationProgressUpdate>? progress = null)
	{
		try
		{
			string metadataPath = Path.Combine(mapDirectory, "metadata.json");
			if (!File.Exists(metadataPath))
			{
				return new MigrationResult { Success = true, FromVersion = FromVersion, ToVersion = ToVersion };
			}

			JsonObject metadataRoot = JsonNode.Parse(File.ReadAllText(metadataPath))?.AsObject() ?? new JsonObject();

			int converted = 0;
			int total = 0;

			if (metadataRoot["Templates"] is JsonObject templates && templates["Units"] is JsonArray units)
			{
				total = units.Count;
				for (int index = 0; index < units.Count; index++)
				{
					if (units[index] is not JsonObject unit) continue;

					progress?.Report(new MigrationProgressUpdate(Description, index + 1, Math.Max(1, total), $"Converting unit {index + 1} of {total}..."));
					if (ConvertUnit(unit)) converted++;
				}
			}

			ConvertTuning(metadataRoot["CombatTuning"] as JsonObject);

			metadataRoot["GameBuildNumber"] = ToVersion;
			MapJsonFormatter.SaveFormattedJson(metadataPath, metadataRoot);

			Console.WriteLine($"[Migration_0_0_6] Converted {converted} of {total} units to the two-resistance model.");
			return new MigrationResult { Success = true, FromVersion = FromVersion, ToVersion = ToVersion };
		}
		catch (Exception exception)
		{
			return new MigrationResult
			{
				Success = false,
				ErrorMessage = $"Migration 0.0.6 failed: {exception.Message}",
				FromVersion = FromVersion,
				ToVersion = ToVersion
			};
		}
	}

	public async Task<MigrationResult> UpAsync(string mapDirectory, IProgress<MigrationProgressUpdate>? progress = null)
	{
		return await Task.Run(() => Up(mapDirectory, progress));
	}

	private static bool ConvertUnit(JsonObject unit)
	{
		bool changed = false;

		changed |= unit.Remove("RatedArmor");
		changed |= unit.Remove("ArmorType");
		changed |= unit.Remove("AttackType");

		if (!unit.ContainsKey("MagicResistance"))
		{
			unit["MagicResistance"] = 0f;
			changed = true;
		}

		if (unit["DamageType"] is JsonValue damageTypeValue &&
		    damageTypeValue.TryGetValue(out string? damageType) &&
		    damageType != null &&
		    DamageTypeMap.TryGetValue(damageType, out string? normalized) &&
		    !string.Equals(damageType, normalized, StringComparison.Ordinal))
		{
			unit["DamageType"] = normalized;
			changed = true;
		}

		return changed;
	}

	private static void ConvertTuning(JsonObject? tuning)
	{
		if (tuning == null) return;

		tuning.Remove("DamageMatrix");
		tuning.Remove("UndefinedPairMultiplier");
		tuning.Remove("IntelligenceMagicResistK");
		tuning.Remove("MaxMagicResist");

		if (tuning["ArmorTuningConstantK"] is JsonNode k)
		{
			tuning.Remove("ArmorTuningConstantK");
			tuning["ResistTuningConstantK"] = k.DeepClone();
		}

		if (tuning["MaxMagicPenetration"] is JsonNode penetration)
		{
			tuning.Remove("MaxMagicPenetration");
			tuning["MaxPercentMagicPenetration"] = penetration.DeepClone();
		}
	}
}
