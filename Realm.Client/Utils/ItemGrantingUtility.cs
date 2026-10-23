using System;
using System.Collections.Generic;
using System.Linq;
using Realm.Ecs;
using Realm.Client.Core;
using Realm.Client.Services;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Stats;
using Realm.Ecs.Services;
using Realm.Shared.Metadata;

namespace Realm.Client.Utils;

public static class ItemGrantingUtility
{
	public static bool EquipItem(EcsWorld world, Entity entity, string itemId, int charges = 1)
	{
		if (world == null || !world.IsAlive(entity) || string.IsNullOrWhiteSpace(itemId))
			return false;

		if (!world.Has<Inventory>(entity))
		{
			world.Add(entity, new Inventory());
		}

		ref var inv = ref world.Get<Inventory>(entity);
		int current = inv.GetItemCount(itemId);
		inv.SetItemCount(itemId, current > 0 ? current + Math.Max(1, charges) : Math.Max(1, charges));

		ApplyItemGrants(world, entity, itemId);
		return true;
	}

	public static bool UnequipItem(EcsWorld world, Entity entity, string itemId)
	{
		if (world == null || !world.IsAlive(entity) || string.IsNullOrWhiteSpace(itemId))
			return false;

		if (!world.Has<Inventory>(entity))
			return false;

		ref var inv = ref world.Get<Inventory>(entity);
		if (!inv.HasItem(itemId))
			return false;

		inv.SetItemCount(itemId, 0);
		RemoveItemGrants(world, entity, itemId);
		RebuildEntityCapabilities(world, entity);
		return true;
	}

	public static bool IsItemEquipped(EcsWorld world, Entity entity, string itemId)
	{
		if (world == null || !world.IsAlive(entity) || string.IsNullOrWhiteSpace(itemId))
			return false;

		if (!world.Has<Inventory>(entity))
			return false;

		var inv = world.Get<Inventory>(entity);
		return inv.HasItem(itemId);
	}

	public static IEnumerable<string> GetEquippedItems(EcsWorld world, Entity entity)
	{
		if (world == null || !world.IsAlive(entity) || !world.Has<Inventory>(entity))
			return Array.Empty<string>();

		var inv = world.Get<Inventory>(entity);
		return inv.Items != null ? inv.Items.Keys.ToArray() : Array.Empty<string>();
	}

	public static void ApplyItemGrants(EcsWorld world, Entity entity, string itemId)
	{
		if (GameContext.RegistryService?.ItemRegistry == null || !GameContext.RegistryService.ItemRegistry.TryGetValue(itemId, out var itemMeta) || itemMeta == null)
			return;

		if (itemMeta.GrantedAbilities != null && itemMeta.GrantedAbilities.Length > 0)
		{
			if (!world.Has<AbilityState>(entity))
			{
				world.Add(entity, new AbilityState(null));
			}

			ref var abiState = ref world.Get<AbilityState>(entity);
			foreach (var abiId in itemMeta.GrantedAbilities)
			{
				if (!string.IsNullOrWhiteSpace(abiId)) abiState.Add(abiId);
			}
		}

		if (itemMeta.GrantedUpgrades != null && itemMeta.GrantedUpgrades.Length > 0)
		{
			int copies = world.Has<Inventory>(entity)
				? Math.Max(1, world.Get<Inventory>(entity).GetItemCount(itemId))
				: 1;

			RemoveItemModifiers(world, entity, itemId);

			foreach (var upgId in itemMeta.GrantedUpgrades)
			{
				if (string.IsNullOrWhiteSpace(upgId)) continue;
				if (GameContext.RegistryService.UpgradeRegistry.TryGetValue(upgId, out var upgMeta) && upgMeta != null)
				{
					for (int copy = 0; copy < copies; copy++)
					{
						string sourceId = $"item:{itemId}:{upgId}:{copy}";
						Services.TechTreeService.ApplyUpgradeModifiersToEntity(entity, sourceId, level: 1, upgMeta);
					}
				}
			}

			AttributeStatCalculator.RecalculateEntityStats(world, entity);
		}

		if (itemMeta.GrantedWeapons != null && itemMeta.GrantedWeapons.Length > 0)
		{
			ApplyGrantedWeapons(world, entity);
		}
	}

	private static void RemoveItemModifiers(EcsWorld world, Entity entity, string itemId)
	{
		if (!world.Has<ModifierState>(entity)) return;

		ref var modState = ref world.Get<ModifierState>(entity);
		if (modState.Value == null) return;

		string itemPrefix = $"item:{itemId}:";
		for (int i = modState.Value.Count - 1; i >= 0; i--)
		{
			var mod = modState.Value[i];
			if (mod.SourceUpgradeId != null && mod.SourceUpgradeId.StartsWith(itemPrefix, StringComparison.OrdinalIgnoreCase))
			{
				modState.Value.RemoveAt(i);
			}
		}
	}

	public static void RemoveItemGrants(EcsWorld world, Entity entity, string itemId)
	{
		if (world.Has<ModifierState>(entity))
		{
			RemoveItemModifiers(world, entity, itemId);
			AttributeStatCalculator.RecalculateEntityStats(world, entity);
		}
	}

	public static void RebuildEntityCapabilities(EcsWorld world, Entity entity)
	{
		if (world == null || !world.IsAlive(entity)) return;

		string unitDefId = world.Has<DefinitionId>(entity) ? world.Get<DefinitionId>(entity).Value : "";
		var activeAbilities = new List<string>();

		if (!string.IsNullOrEmpty(unitDefId) && GameHost.TryGetUnitOrBuildingMetadata(unitDefId, out var regMeta) && regMeta.Abilities != null)
		{
			activeAbilities.AddRange(regMeta.Abilities);
		}

		var equippedItems = GetEquippedItems(world, entity);
		foreach (var eqItemId in equippedItems)
		{
			if (GameContext.RegistryService?.ItemRegistry != null && GameContext.RegistryService.ItemRegistry.TryGetValue(eqItemId, out var itemMeta) && itemMeta?.GrantedAbilities != null)
			{
				foreach (var abiId in itemMeta.GrantedAbilities)
				{
					if (!string.IsNullOrWhiteSpace(abiId) && !activeAbilities.Contains(abiId, StringComparer.OrdinalIgnoreCase))
					{
						activeAbilities.Add(abiId);
					}
				}
			}
		}

		if (activeAbilities.Count > 0 || world.Has<AbilityState>(entity))
		{
			world.SetOrAdd(entity, new AbilityState(activeAbilities));
		}

		ApplyGrantedWeapons(world, entity);

		AttributeStatCalculator.RecalculateEntityStats(world, entity);
	}

	private static void ApplyGrantedWeapons(EcsWorld world, Entity entity)
	{
		var weaponSlots = new List<Weapon>();
		var equippedItems = GetEquippedItems(world, entity);

		foreach (var eqItemId in equippedItems)
		{
			if (GameContext.RegistryService?.ItemRegistry != null && GameContext.RegistryService.ItemRegistry.TryGetValue(eqItemId, out var itemMeta) && itemMeta?.GrantedWeapons != null)
			{
				foreach (var wId in itemMeta.GrantedWeapons)
				{
					if (string.IsNullOrWhiteSpace(wId)) continue;
					if (GameContext.RegistryService.WeaponRegistry.TryGetValue(wId, out var wMeta) && wMeta != null)
					{
						weaponSlots.Add(new Weapon(
							baseDamage: wMeta.Damage,
							maxRange: wMeta.Range,
							attackInterval: wMeta.AttackCooldown > 0 ? wMeta.AttackCooldown : 1.5f,
							projectileSpeed: wMeta.ProjectileSpeed > 0 ? wMeta.ProjectileSpeed : 20f,
							damageType: !string.IsNullOrEmpty(wMeta.AttackType) ? wMeta.AttackType : "normal"
						));

						if (world.Has<Attack>(entity))
						{
							ref var atk = ref world.Get<Attack>(entity);
							world.Set(entity, new Attack(
								damage: wMeta.Damage > 0 ? wMeta.Damage : atk.Damage,
								range: wMeta.Range > 0 ? wMeta.Range : atk.Range,
								cooldown: wMeta.AttackCooldown > 0 ? wMeta.AttackCooldown : atk.Cooldown,
								currentCooldown: atk.CurrentCooldown,
								damageVariance: atk.DamageVariance,
								damageType: !string.IsNullOrEmpty(wMeta.AttackType) ? wMeta.AttackType : atk.DamageType,
								flatArmorPenetration: atk.FlatArmorPenetration,
								percentArmorPenetration: atk.PercentArmorPenetration,
								critChance: atk.CritChance,
								critMultiplier: atk.CritMultiplier,
								splashType: atk.SplashType,
								splashInnerRadius: atk.SplashInnerRadius,
								splashMediumRadius: atk.SplashMediumRadius,
								splashOuterRadius: atk.SplashOuterRadius,
								splashInnerRatio: atk.SplashInnerRatio,
								splashMediumRatio: atk.SplashMediumRatio,
								splashOuterRatio: atk.SplashOuterRatio,
								friendlyFire: atk.FriendlyFire
							));
						}
					}
				}
			}
		}

		if (weaponSlots.Count > 0)
		{
			world.SetOrAdd(entity, new Weapons(weaponSlots));
		}
	}
}
