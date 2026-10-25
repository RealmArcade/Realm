using System;
using System.Collections.Generic;

namespace Realm.Ecs.Common;

/// <summary>
///     The complete set of damage types the engine mitigates differently. Three, deliberately:
///     physical is resisted by armor, magic by magic resistance, and pure by nothing at all.
///     Flavours such as fire or frost are presentation, not a fourth mitigation path — a map that
///     genuinely wants a type chart authors matrix rows instead (see <c>CombatConfig</c>).
/// </summary>
public static class DamageTypes
{
	public const string Physical = "physical";
	public const string Magic = "magic";
	public const string Pure = "pure";

	private static readonly HashSet<string> Known = new(StringComparer.OrdinalIgnoreCase)
	{
		Physical, Magic, Pure
	};

	private static readonly HashSet<string> Warned = new(StringComparer.OrdinalIgnoreCase);

	/// <summary>True when the type is not one of the three engine types.</summary>
	public static bool IsKnown(string? damageType) => !string.IsNullOrWhiteSpace(damageType) && Known.Contains(damageType);

	/// <summary>Pure damage skips resistance, penetration and the type matrix entirely.</summary>
	public static bool IsPure(string? damageType) => string.Equals(damageType?.Trim(), Pure, StringComparison.OrdinalIgnoreCase);

	/// <summary>Magic damage is resisted by magic resistance; everything else falls to armor.</summary>
	public static bool IsMagic(string? damageType) => string.Equals(damageType?.Trim(), Magic, StringComparison.OrdinalIgnoreCase);

	/// <summary>
	///     Resolves an authored damage type to an engine type. An unknown string resolves to
	///     physical and is reported once per value, so a typo costs a log line rather than silently
	///     changing what a unit's damage does.
	/// </summary>
	public static string Normalize(string? damageType)
	{
		if (string.IsNullOrWhiteSpace(damageType)) return Physical;
		if (Known.Contains(damageType)) return damageType.Trim().ToLowerInvariant();

		string unknown = damageType.Trim();
		if (Warned.Add(unknown))
		{
			Console.WriteLine($"[Damage] Unknown damage type '{unknown}'. It is treated as {Physical}. Known types: {string.Join(", ", Known)}");
		}

		return Physical;
	}
}
