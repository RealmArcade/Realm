using System;
using System.Collections.Generic;
using Realm.Ecs;
using Realm.Client.Core;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Stats;

namespace Realm.Client.Services;

/// <summary>
///     What each player is up to on the map's tech tracks, as the authority reported it. The authority owns the levels
///     and grants what a level does to the units it simulates, so a screen keeps the numbers only to offer the next
///     level and to show the one a player is on: applying a level here as well would double what the world already says.
/// </summary>
public class TechTreeService
{
	private readonly Dictionary<(int PlayerIndex, string TechId), int> _levels = new();

	public int GetPlayerTechLevel(int playerIndex, string techId)
	{
		if (string.IsNullOrEmpty(techId)) return 0;
		return _levels.TryGetValue((playerIndex, techId), out int level) ? level : 0;
	}

	/// <summary>Records how far the authority says a player is up one of the map's tech tracks.</summary>
	public void ApplyReplicatedLevel(int playerIndex, string techId, int level)
	{
		if (string.IsNullOrEmpty(techId)) return;
		_levels[(playerIndex, techId)] = level;
	}

	/// <summary>
	///     Grants one of a map's authored modifiers to an entity a screen made for itself, which is what an item that
	///     confers an upgrade does. A level the authority granted is not applied here: it arrives as replicated state on
	///     the units the authority changed.
	/// </summary>
	public static void ApplyUpgradeModifiersToEntity(Entity entity, string techId, int level, Realm.Shared.Metadata.UpgradeMetadata? upgradeDef)
	{
		if (upgradeDef == null || upgradeDef.Modifiers == null || upgradeDef.Modifiers.Count == 0) return;

		if (!GameContext.World.Has<ModifierState>(entity))
		{
			GameContext.World.Add(entity, new ModifierState(null));
		}
		ref var modState = ref GameContext.World.Get<ModifierState>(entity);

		for (int i = modState.Count - 1; i >= 0; i--)
		{
			if (string.Equals(modState.Get(i).SourceUpgradeId, techId, System.StringComparison.OrdinalIgnoreCase))
			{
				modState.RemoveAt(i);
			}
		}

		if (level <= 0) return;

		for (int i = 0; i < upgradeDef.Modifiers.Count; i++)
		{
			var modDef = upgradeDef.Modifiers[i];
			if (string.IsNullOrWhiteSpace(modDef.StatKey)) continue;

			Realm.Ecs.Common.StatKeys.WarnIfUnknown($"upgrade '{techId}'", modDef.StatKey);
			var statId = new StatId(modDef.StatKey);
			var modType = Enum.TryParse<ModifierType>(modDef.Type, out var parsedType) ? parsedType : ModifierType.Flat;

			float totalValue = modDef.Value + (level - 1) * modDef.PerLevelScaling;
			var statMod = new StatModifier(
				statTypeId: statId,
				type: modType,
				value: totalValue,
				duration: -1f,
				wasmFunctionName: modDef.WASMFunctionName,
				sourceLevel: level,
				sourceUpgradeId: techId
			);

			modState.Add(statMod);
		}
	}
}
