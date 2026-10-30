using System;
using System.Collections.Generic;
using Realm.Ecs;
using Realm.Client.Core;

namespace Realm.Client.Services;

public class CheatService
{
	public bool UnlimitedPowerEnabled { get; set; }

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
