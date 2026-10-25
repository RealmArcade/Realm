using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Realm.Client.Utils;

namespace Realm.Client.Services;

/// <summary>
///     Converts the single damage-or-heal ability shape into the composable effect list. Every
///     legacy scalar becomes the one effect that reproduces it — a damage number becomes a damage
///     effect on the same radius, a heal becomes a heal effect, a summon becomes a summon effect —
///     so an upgraded map plays exactly as it did before.
/// </summary>
public class Migration_0_0_5_AbilityEffects : IMapMigration
{
	public string FromVersion => "v0.0.4";
	public string ToVersion => "v0.0.5";
	public string Description => "Replace scalar ability damage, healing and summon fields with a composable Effects list";

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

			if (metadataRoot["Templates"] is JsonObject templates && templates["Abilities"] is JsonArray abilities)
			{
				total = abilities.Count;
				for (int index = 0; index < abilities.Count; index++)
				{
					if (abilities[index] is not JsonObject ability) continue;

					progress?.Report(new MigrationProgressUpdate(Description, index + 1, Math.Max(1, total), $"Converting ability {index + 1} of {total}..."));
					if (ConvertAbility(ability)) converted++;
				}
			}

			metadataRoot["GameBuildNumber"] = ToVersion;
			MapJsonFormatter.SaveFormattedJson(metadataPath, metadataRoot);

			Console.WriteLine($"[Migration_0_0_5] Converted {converted} of {total} abilities to composable effects.");
			return new MigrationResult { Success = true, FromVersion = FromVersion, ToVersion = ToVersion };
		}
		catch (Exception exception)
		{
			return new MigrationResult
			{
				Success = false,
				ErrorMessage = $"Migration 0.0.5 failed: {exception.Message}",
				FromVersion = FromVersion,
				ToVersion = ToVersion
			};
		}
	}

	private static bool ConvertAbility(JsonObject ability)
	{
		bool changed = false;

		float radius = ReadFloat(ability, "AreaOfEffectRadius");
		var effects = ability["Effects"] as JsonArray ?? new JsonArray();

		float damage = ReadFloat(ability, "Damage");
		if (damage > 0f)
		{
			effects.Add(BuildEffect("damage", "enemies", radius, amount: damage));
			changed = true;
		}

		float healing = ReadFloat(ability, "Healing");
		if (healing > 0f)
		{
			effects.Add(BuildEffect("heal", "allies", radius, amount: healing));
			changed = true;
		}

		string summonUnitId = ability["SummonedUnitId"]?.ToString() ?? string.Empty;
		if (!string.IsNullOrWhiteSpace(summonUnitId))
		{
			JsonObject summonEffect = BuildEffect("summon", "self", radius, amount: 0f);
			summonEffect["SummonUnitId"] = summonUnitId;
			summonEffect["SummonCount"] = Math.Max(1, (int)ReadFloat(ability, "SummonCount"));
			summonEffect["Duration"] = ReadFloat(ability, "SummonDuration");
			effects.Add(summonEffect);
			changed = true;
		}

		if (changed && ability["Effects"] is not JsonArray)
		{
			ability["Effects"] = effects;
		}

		// Applied status effects were authored as bare ids with no duration or magnitude, and no
		// shipped map ever set them; the effect list replaces them outright.
		changed |= ability.Remove("AppliedStatusEffects");
		changed |= ability.Remove("Damage");
		changed |= ability.Remove("Healing");
		changed |= ability.Remove("SummonedUnitId");
		changed |= ability.Remove("SummonCount");
		changed |= ability.Remove("SummonDuration");

		return changed;
	}

	private static JsonObject BuildEffect(string kind, string targets, float radius, float amount)
	{
		var effect = new JsonObject
		{
			["Kind"] = kind,
			["Targets"] = targets,
			["Delivery"] = radius > 0f ? "radius" : "single",
			["Radius"] = radius,
			["Amount"] = amount
		};

		return effect;
	}

	private static float ReadFloat(JsonObject obj, string key)
	{
		if (obj[key] is not JsonValue value) return 0f;
		if (value.TryGetValue(out float number)) return number;
		if (value.TryGetValue(out string? text) && float.TryParse(text, out float parsed)) return parsed;

		return 0f;
	}

	public Task<MigrationResult> UpAsync(string mapDirectory, IProgress<MigrationProgressUpdate>? progress = null)
	{
		return Task.Run(() => Up(mapDirectory, progress));
	}
}
