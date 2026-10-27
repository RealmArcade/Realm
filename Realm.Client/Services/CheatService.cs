using System;
using System.Collections.Generic;
using Realm.Ecs;
using Realm.Client.Core;

namespace Realm.Client.Services;

/// <summary>
///     What a player can type to bend a development match. Which words a screen answers is its own business, but what a
///     cheat does to the world is not: it travels to the authority as an order, because the authority owns the world
///     every player is looking at. A match played against other people refuses them outright.
/// </summary>
public class CheatService
{
	/// <summary>Whether the match was asked to waive what this player's casts cost, for the paths a screen still runs itself.</summary>
	public bool UnlimitedPowerEnabled { get; set; }

	/// <summary>Whether the match was asked to make this player's units close to unkillable, for the same paths.</summary>
	public bool GigachadEnabled { get; set; }

	private static readonly IReadOnlyDictionary<string, CheatResult> _cheats = new Dictionary<string, CheatResult>(StringComparer.OrdinalIgnoreCase)
	{
		{ "securethebag", CheatResult.Stonks },
		{ "gigachad", CheatResult.Gigachad },
		{ "needforspeed", CheatResult.AbsoluteUnit },
		{ "thanossnap", CheatResult.ThanosSnap },
		{ "ezclap", CheatResult.EzClap },
		{ "aura", CheatResult.NoCap },
		{ "speedrun", CheatResult.WarpSpeed },
		{ "skibidi", CheatResult.UnlimitedPower }
	};

	/// <summary>
	///     Recognizes a cheat a player typed and asks the authority for it. What this returns is what the screen should
	///     say, not what the world now is: only the authority decides whether the cheat was applied and what it did.
	/// </summary>
	internal (CheatResult Result, int AffectedCount) TryTriggerCheat(string text, bool isMultiplayer, IEnumerable<Entity> selectedEntities)
	{
		if (isMultiplayer) return (CheatResult.None, 0);
		if (!(GameContext.Host?.CheatsEnabled ?? false)) return (CheatResult.None, 0);

		string key = text.Trim();
		if (!_cheats.TryGetValue(key, out CheatResult result)) return (CheatResult.None, 0);

		if (result == CheatResult.UnlimitedPower) UnlimitedPowerEnabled = !UnlimitedPowerEnabled;
		else if (result == CheatResult.Gigachad) GigachadEnabled = true;

		GameContext.Host.SubmitCheatOrder(key, selectedEntities);
		return (result, 0);
	}
}
