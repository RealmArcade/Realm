using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Realm.Shared.Simulation;

public abstract record CheatKind
{
	public abstract CheatResult Result { get; }

	public sealed record GrantResources : CheatKind
	{
		public override CheatResult Result => CheatResult.Stonks;
	}

	public sealed record EmpowerOwnedUnits : CheatKind
	{
		public override CheatResult Result => CheatResult.Gigachad;
	}

	public sealed record BoostSelectedMovementSpeed : CheatKind
	{
		public override CheatResult Result => CheatResult.AbsoluteUnit;
	}

	public sealed record KillEnemyAiUnits : CheatKind
	{
		public override CheatResult Result => CheatResult.ThanosSnap;
	}

	public sealed record EndMatch : CheatKind
	{
		public override CheatResult Result => CheatResult.EzClap;
	}

	public sealed record DisableShroud : CheatKind
	{
		public override CheatResult Result => CheatResult.NoCap;
	}

	public sealed record ToggleFastBuild : CheatKind
	{
		public override CheatResult Result => CheatResult.WarpSpeed;
	}

	public sealed record ToggleUnlimitedPower : CheatKind
	{
		public override CheatResult Result => CheatResult.UnlimitedPower;
	}

	public sealed record KillSelectedUnits : CheatKind
	{
		public override CheatResult Result => CheatResult.Sacrifice;
	}
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

	public static bool TryParse(string word, [NotNullWhen(true)] out CheatKind? kind)
	{
		kind = null;
		if (string.IsNullOrWhiteSpace(word)) return false;

		switch (word.Trim().ToLowerInvariant())
		{
			case "securethebag":
				kind = new CheatKind.GrantResources();
				return true;
			case "gigachad":
				kind = new CheatKind.EmpowerOwnedUnits();
				return true;
			case "needforspeed":
				kind = new CheatKind.BoostSelectedMovementSpeed();
				return true;
			case "thanossnap":
				kind = new CheatKind.KillEnemyAiUnits();
				return true;
			case "ezclap":
				kind = new CheatKind.EndMatch();
				return true;
			case "aura":
				kind = new CheatKind.DisableShroud();
				return true;
			case "speedrun":
				kind = new CheatKind.ToggleFastBuild();
				return true;
			case "skibidi":
				kind = new CheatKind.ToggleUnlimitedPower();
				return true;
			case "cooked":
				kind = new CheatKind.KillSelectedUnits();
				return true;
			default:
				return false;
		}
	}
}
