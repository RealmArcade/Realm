using System;
using System.Collections.Generic;
using Realm.Ecs;
using Realm.Client.Core;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Stats;

namespace Realm.Client.Services;

public class TechTreeService
{
	private readonly Dictionary<(int PlayerIndex, string TechId), int> _levels = new();

	public int GetPlayerTechLevel(int playerIndex, string techId)
	{
		if (string.IsNullOrEmpty(techId)) return 0;
		return _levels.TryGetValue((playerIndex, techId), out int level) ? level : 0;
	}

	public void ApplyReplicatedLevel(int playerIndex, string techId, int level)
	{
		if (string.IsNullOrEmpty(techId)) return;
		_levels[(playerIndex, techId)] = level;
	}

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
