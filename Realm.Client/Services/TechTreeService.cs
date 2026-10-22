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
	public bool BuyWeaponsUpgrade(Entity playerEntity)
	{
		if (!GameContext.World.IsAlive(playerEntity)) return false;
		ref var upgrades = ref GameContext.World.Get<PlayerUpgrades>(playerEntity);
		if (upgrades.WeaponsUpgrade) return false;

		upgrades.WeaponsUpgrade = true;

		var query = QueryCache.AllAttackAndOwnerNoneDeadAndBuildingQuery;
		GameContext.World.Query(in query, (Entity entity, ref Attack atk, ref Owner owner) =>
		{
			if (owner.PlayerEntity.Value == playerEntity)
			{
				atk.Damage += 3f;
			}
		});
		return true;
	}

	public bool BuyShieldsUpgrade(Entity playerEntity)
	{
		if (!GameContext.World.IsAlive(playerEntity)) return false;
		ref var upgrades = ref GameContext.World.Get<PlayerUpgrades>(playerEntity);
		if (upgrades.ShieldsUpgrade) return false;

		upgrades.ShieldsUpgrade = true;

		var query = QueryCache.AllArmorAndOwnerNoneDeadQuery;
		GameContext.World.Query(in query, (Entity entity, ref Armor arm, ref Owner owner) =>
		{
			if (owner.PlayerEntity.Value == playerEntity)
			{
				arm.FlatArmor += 2f;
			}
		});
		return true;
	}

	public bool BuyHarvestingUpgrade(Entity playerEntity)
	{
		if (!GameContext.World.IsAlive(playerEntity)) return false;
		ref var upgrades = ref GameContext.World.Get<PlayerUpgrades>(playerEntity);
		if (upgrades.HarvestingUpgrade) return false;

		upgrades.HarvestingUpgrade = true;
		return true;
	}

	public Dictionary<(int PlayerIndex, string TechId), int> PlayerTechLevels { get; } = new();

	public int GetPlayerTechLevel(int playerIndex, string techId)
	{
		if (string.IsNullOrEmpty(techId)) return 0;
		return PlayerTechLevels.TryGetValue((playerIndex, techId), out int level) ? level : 0;
	}

	public void SetPlayerTechLevel(int playerIndex, string techId, int level)
	{
		if (string.IsNullOrEmpty(techId)) return;
		PlayerTechLevels[(playerIndex, techId)] = level;
		ApplyTechUpgradesToPlayerUnits(playerIndex, techId, level);
	}

	public void AddPlayerTechLevel(int playerIndex, string techId, int delta)
	{
		if (string.IsNullOrEmpty(techId)) return;
		int current = GetPlayerTechLevel(playerIndex, techId);
		int newLevel = current + delta;
		PlayerTechLevels[(playerIndex, techId)] = newLevel;
		ApplyTechUpgradesToPlayerUnits(playerIndex, techId, newLevel);
	}

	public void ApplyTechUpgradesToPlayerUnits(int playerIndex, string techId, int level)
	{
		if (GameContext.World == null) return;

		var upgradeDef = GameContext.RegistryService?.UpgradeRegistry?.TryGetValue(techId, out var meta) == true ? meta : null;
		var affectedUnits = upgradeDef?.AffectedUnitIds;

		var query = QueryCache.AllOwnerNoneDeadQuery;
		GameContext.World.Query(in query, (Entity entity, ref Owner owner) =>
		{
			if (owner.PlayerEntity.Value != Entity.Null && GameContext.World.Has<UnitOwnerPlayer>(owner.PlayerEntity.Value))
			{
				var pIndex = GameContext.World.Get<UnitOwnerPlayer>(owner.PlayerEntity.Value).PlayerIndex;
				if (pIndex != playerIndex) return;
			}

			if (affectedUnits != null && affectedUnits.Length > 0)
			{
				if (!GameContext.World.Has<DefinitionId>(entity)) return;
				string unitDefId = GameContext.World.Get<DefinitionId>(entity).Value;
				bool isAffected = false;
				for (int i = 0; i < affectedUnits.Length; i++)
				{
					if (string.Equals(affectedUnits[i], unitDefId, System.StringComparison.OrdinalIgnoreCase))
					{
						isAffected = true;
						break;
					}
				}
				if (!isAffected) return;
			}

			ApplyUpgradeModifiersToEntity(entity, techId, level, upgradeDef);
			Realm.Ecs.Services.AttributeStatCalculator.RecalculateEntityStats(GameContext.World, entity);
		});
	}

	public static void ApplyUpgradeModifiersToEntity(Entity entity, string techId, int level, Realm.Shared.Metadata.UpgradeMetadata? upgradeDef)
	{
		if (upgradeDef == null || upgradeDef.Modifiers == null || upgradeDef.Modifiers.Count == 0) return;

		if (!GameContext.World.Has<ModifierState>(entity))
		{
			GameContext.World.Add(entity, new ModifierState(new List<StatModifier>()));
		}
		ref var modState = ref GameContext.World.Get<ModifierState>(entity);
		var list = modState.Value;

		for (int i = list.Count - 1; i >= 0; i--)
		{
			if (string.Equals(list[i].SourceUpgradeId, techId, System.StringComparison.OrdinalIgnoreCase))
			{
				list.RemoveAt(i);
			}
		}

		if (level <= 0) return;

		for (int i = 0; i < upgradeDef.Modifiers.Count; i++)
		{
			var modDef = upgradeDef.Modifiers[i];
			if (string.IsNullOrWhiteSpace(modDef.StatKey)) continue;

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

			list.Add(statMod);
		}
	}
}