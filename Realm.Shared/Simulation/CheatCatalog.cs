using System;
using System.Collections.Generic;

namespace Realm.Shared.Simulation;

public enum CheatKind
{
	None,
	GrantResources,
	EmpowerOwnedUnits,
	BoostSelectedMovementSpeed,
	KillEnemyAiUnits,
	EndMatch,
	DisableShroud,
	ToggleFastBuild,
	ToggleUnlimitedPower,
	KillSelectedUnits
}

public static class CheatCatalog
{
	private static readonly string[] _words =
	{
		"securethebag",
		"gigachad",
		"needforspeed",
		"thanossnap",
		"ezclap",
		"aura",
		"speedrun",
		"skibidi",
		"cooked"
	};

	public static IReadOnlyList<string> Words => _words;

	public static bool TryParse(string word, out CheatKind kind)
	{
		kind = CheatKind.None;
		if (string.IsNullOrWhiteSpace(word)) return false;

		string key = word.Trim().ToLowerInvariant();
		switch (key)
		{
			case "securethebag":
				kind = CheatKind.GrantResources;
				return true;
			case "gigachad":
				kind = CheatKind.EmpowerOwnedUnits;
				return true;
			case "needforspeed":
				kind = CheatKind.BoostSelectedMovementSpeed;
				return true;
			case "thanossnap":
				kind = CheatKind.KillEnemyAiUnits;
				return true;
			case "ezclap":
				kind = CheatKind.EndMatch;
				return true;
			case "aura":
				kind = CheatKind.DisableShroud;
				return true;
			case "speedrun":
				kind = CheatKind.ToggleFastBuild;
				return true;
			case "skibidi":
				kind = CheatKind.ToggleUnlimitedPower;
				return true;
			case "cooked":
				kind = CheatKind.KillSelectedUnits;
				return true;
			default:
				return false;
		}
	}
}
