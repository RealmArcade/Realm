using System;
using System.Collections.Generic;
using Realm.Ecs;
using Realm.Client.Core;
using Realm.Shared.Simulation;

namespace Realm.Client.Services;

public class CheatService
{
	public bool UnlimitedPowerEnabled { get; set; }

	public bool GigachadEnabled { get; set; }

	public (CheatResult Result, int AffectedCount) TryTriggerCheat(string text, bool isMultiplayer, IEnumerable<Entity> selectedEntities)
	{
		if (isMultiplayer) return (CheatResult.None, 0);
		if (!(GameContext.Host?.CheatsEnabled ?? false)) return (CheatResult.None, 0);
		if (!CheatCatalog.TryParse(text, out CheatKind kind)) return (CheatResult.None, 0);

		CheatResult result = kind switch
		{
			CheatKind.GrantResources => CheatResult.Stonks,
			CheatKind.EmpowerOwnedUnits => CheatResult.Gigachad,
			CheatKind.BoostSelectedMovementSpeed => CheatResult.AbsoluteUnit,
			CheatKind.KillEnemyAiUnits => CheatResult.ThanosSnap,
			CheatKind.EndMatch => CheatResult.EzClap,
			CheatKind.DisableShroud => CheatResult.NoCap,
			CheatKind.ToggleFastBuild => CheatResult.WarpSpeed,
			CheatKind.ToggleUnlimitedPower => CheatResult.UnlimitedPower,
			CheatKind.KillSelectedUnits => CheatResult.Sacrifice,
			_ => CheatResult.None
		};

		if (result == CheatResult.None) return (CheatResult.None, 0);

		if (result == CheatResult.UnlimitedPower) UnlimitedPowerEnabled = !UnlimitedPowerEnabled;
		else if (result == CheatResult.Gigachad) GigachadEnabled = true;

		GameContext.Host.SubmitCheatOrder(text.Trim(), selectedEntities);
		return (result, 0);
	}
}
