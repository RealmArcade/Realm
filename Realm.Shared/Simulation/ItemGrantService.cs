using Realm.Ecs;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Stats;
using Realm.Ecs.Services;
using Realm.Shared.Maps;
using Realm.Shared.Metadata;

namespace Realm.Shared.Simulation;

public class ItemGrantService
{
	public const int CarriedItemLimit = 6;

	private readonly SimulationContext _context;
	private readonly DefinitionCatalog _catalog;

	public ItemGrantService(SimulationContext context, DefinitionCatalog catalog)
	{
		_context = context;
		_catalog = catalog;
	}

	public EcsWorld World => _context.World;

	public bool CanCarry(Entity entity, string itemId)
	{
		if (!World.IsAlive(entity) || string.IsNullOrWhiteSpace(itemId)) return false;
		if (!World.Has<Inventory>(entity)) return true;

		ref Inventory inventory = ref World.Get<Inventory>(entity);
		return inventory.Count < CarriedItemLimit || inventory.HasItem(itemId);
	}

	public bool Equip(Entity entity, string itemId, int charges = 1)
	{
		if (!World.IsAlive(entity) || string.IsNullOrWhiteSpace(itemId)) return false;
		if (!CanCarry(entity, itemId)) return false;

		if (!World.Has<Inventory>(entity)) World.Add(entity, default(Inventory));

		int carried = CarriedCharges(entity, itemId);
		int added = Math.Max(1, charges);
		if (!WriteCharges(entity, itemId, carried > 0 ? carried + added : added)) return false;

		ApplyGrants(entity, itemId);
		return true;
	}

	public bool Unequip(Entity entity, string itemId)
	{
		if (!World.IsAlive(entity) || string.IsNullOrWhiteSpace(itemId)) return false;
		if (!World.Has<Inventory>(entity)) return false;
		if (!Carries(entity, itemId)) return false;
		if (!WriteCharges(entity, itemId, 0)) return false;

		WithdrawGrants(entity, itemId);
		return true;
	}

	public bool Carries(Entity entity, string itemId)
	{
		if (!World.IsAlive(entity) || string.IsNullOrWhiteSpace(itemId)) return false;
		if (!World.Has<Inventory>(entity)) return false;

		return World.Get<Inventory>(entity).HasItem(itemId);
	}

	public List<string> List(Entity entity)
	{
		if (!World.IsAlive(entity) || !World.Has<Inventory>(entity)) return new List<string>();

		return World.Get<Inventory>(entity).ToList();
	}

	public int Charges(Entity entity, string itemId)
	{
		if (!World.IsAlive(entity) || string.IsNullOrWhiteSpace(itemId)) return 0;

		return CarriedCharges(entity, itemId);
	}

	public bool SetCharges(Entity entity, string itemId, int charges)
	{
		if (!World.IsAlive(entity) || string.IsNullOrWhiteSpace(itemId)) return false;
		if (!World.Has<Inventory>(entity)) return false;
		if (!Carries(entity, itemId)) return false;

		int wanted = Math.Max(0, charges);
		if (!WriteCharges(entity, itemId, wanted)) return false;

		if (wanted == 0) WithdrawGrants(entity, itemId);
		else ApplyGrants(entity, itemId);

		return true;
	}

	private int CarriedCharges(Entity entity, string itemId)
	{
		if (!World.IsAlive(entity) || !World.Has<Inventory>(entity)) return 0;

		return World.Get<Inventory>(entity).GetItemCount(itemId);
	}

	private bool WriteCharges(Entity entity, string itemId, int charges)
	{
		ref Inventory inventory = ref World.Get<Inventory>(entity);
		inventory.SetItemCount(itemId, charges);

		return inventory.GetItemCount(itemId) == charges;
	}

	public void ApplyGrants(Entity entity, string itemId)
	{
		if (!World.IsAlive(entity)) return;

		ItemMetadata? item = _catalog.FindItem(itemId);
		if (item == null) return;

		GrantAbilities(entity, item);
		GrantUpgrades(entity, itemId, item);
		ApplyGrantedWeapons(entity);
	}

	private void GrantAbilities(Entity entity, ItemMetadata item)
	{
		if (item.GrantedAbilities == null || item.GrantedAbilities.Length == 0) return;

		if (!World.Has<AbilityState>(entity)) World.Add(entity, new AbilityState(null));

		ref AbilityState abilities = ref World.Get<AbilityState>(entity);
		for (int index = 0; index < item.GrantedAbilities.Length; index++)
		{
			string abilityId = item.GrantedAbilities[index];
			if (!string.IsNullOrWhiteSpace(abilityId)) abilities.Add(abilityId);
		}
	}

	private void GrantUpgrades(Entity entity, string itemId, ItemMetadata item)
	{
		if (item.GrantedUpgrades == null || item.GrantedUpgrades.Length == 0) return;

		int copies = World.Has<Inventory>(entity) ? Math.Max(1, World.Get<Inventory>(entity).GetItemCount(itemId)) : 1;
		WithdrawUpgrades(entity, itemId);

		for (int index = 0; index < item.GrantedUpgrades.Length; index++)
		{
			string upgradeId = item.GrantedUpgrades[index];
			if (string.IsNullOrWhiteSpace(upgradeId)) continue;

			UpgradeMetadata? upgrade = _catalog.FindUpgrade(upgradeId);
			if (upgrade == null) continue;

			for (int copy = 0; copy < copies; copy++)
			{
				ApplyUpgradeModifiers(entity, $"{ItemSourcePrefix}{itemId}:{upgradeId}:{copy}", 1, upgrade);
			}
		}

		AttributeStatCalculator.RecalculateEntityStats(World, entity, 0f, _context.ActiveCombatTuning);
	}

	public void WithdrawGrants(Entity entity, string itemId)
	{
		if (World.Has<ModifierState>(entity)) WithdrawUpgrades(entity, itemId);
		RebuildCapabilities(entity);
	}

	private void WithdrawUpgrades(Entity entity, string itemId)
	{
		if (!World.Has<ModifierState>(entity)) return;

		ref ModifierState modifiers = ref World.Get<ModifierState>(entity);
		string prefix = $"{ItemSourcePrefix}{itemId}:";
		for (int index = modifiers.Count - 1; index >= 0; index--)
		{
			string sourceId = modifiers.Get(index).SourceUpgradeId;
			if (sourceId == null || !sourceId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;

			modifiers.RemoveAt(index);
		}
	}

	public void ApplyUpgradeModifiers(Entity entity, string sourceId, int level, UpgradeMetadata upgrade)
	{
		if (upgrade.Modifiers == null || upgrade.Modifiers.Count == 0) return;

		if (!World.Has<ModifierState>(entity)) World.Add(entity, new ModifierState(null));

		ref ModifierState modifiers = ref World.Get<ModifierState>(entity);
		for (int index = modifiers.Count - 1; index >= 0; index--)
		{
			if (string.Equals(modifiers.Get(index).SourceUpgradeId, sourceId, StringComparison.OrdinalIgnoreCase)) modifiers.RemoveAt(index);
		}

		if (level <= 0) return;

		for (int index = 0; index < upgrade.Modifiers.Count; index++)
		{
			StatModifierDefinition definition = upgrade.Modifiers[index];
			if (string.IsNullOrWhiteSpace(definition.StatKey)) continue;

			StatKeys.WarnIfUnknown($"upgrade '{sourceId}'", definition.StatKey);
			ModifierType type = Enum.TryParse(definition.Type, out ModifierType parsed) ? parsed : ModifierType.Flat;
			float value = definition.Value + (level - 1) * definition.PerLevelScaling;

			modifiers.Add(new StatModifier(
				new StatId(definition.StatKey),
				type,
				value,
				-1f,
				definition.WASMFunctionName,
				level,
				sourceId));
		}
	}

	public void RebuildCapabilities(Entity entity)
	{
		if (!World.IsAlive(entity)) return;

		string templateId = World.Has<DefinitionId>(entity) ? World.Get<DefinitionId>(entity).Value : string.Empty;
		var abilities = new List<string>();

		if (!string.IsNullOrEmpty(templateId))
		{
			UnitMetadata? template = _catalog.FindUnitOrBuilding(templateId);
			if (template?.Abilities != null) abilities.AddRange(template.Abilities);
		}

		List<string> carried = List(entity);
		for (int index = 0; index < carried.Count; index++)
		{
			ItemMetadata? item = _catalog.FindItem(carried[index]);
			if (item?.GrantedAbilities == null) continue;

			for (int grantedIndex = 0; grantedIndex < item.GrantedAbilities.Length; grantedIndex++)
			{
				string abilityId = item.GrantedAbilities[grantedIndex];
				if (string.IsNullOrWhiteSpace(abilityId)) continue;
				if (abilities.Contains(abilityId, StringComparer.OrdinalIgnoreCase)) continue;

				abilities.Add(abilityId);
			}
		}

		if (abilities.Count > 0 || World.Has<AbilityState>(entity)) World.SetOrAdd(entity, new AbilityState(abilities));

		ApplyGrantedWeapons(entity);
		AttributeStatCalculator.RecalculateEntityStats(World, entity, 0f, _context.ActiveCombatTuning);
	}

	private void ApplyGrantedWeapons(Entity entity)
	{
		var slots = new List<Weapon>();
		List<string> carried = List(entity);

		for (int index = 0; index < carried.Count; index++)
		{
			ItemMetadata? item = _catalog.FindItem(carried[index]);
			if (item?.GrantedWeapons == null) continue;

			for (int grantedIndex = 0; grantedIndex < item.GrantedWeapons.Length; grantedIndex++)
			{
				string weaponId = item.GrantedWeapons[grantedIndex];
				if (string.IsNullOrWhiteSpace(weaponId)) continue;

				WeaponMetadata? weapon = _catalog.FindWeapon(weaponId);
				if (weapon == null) continue;

				slots.Add(new Weapon(
					baseDamage: weapon.Damage,
					maxRange: weapon.Range,
					attackInterval: weapon.AttackCooldown > 0 ? weapon.AttackCooldown : 1.5f,
					projectileSpeed: weapon.ProjectileSpeed > 0 ? weapon.ProjectileSpeed : 20f,
					damageType: string.IsNullOrEmpty(weapon.DamageType) ? "physical" : weapon.DamageType));

				if (!World.Has<Attack>(entity)) continue;

				ref Attack attack = ref World.Get<Attack>(entity);
				World.Set(entity, new Attack(
					damage: weapon.Damage > 0 ? weapon.Damage : attack.Damage,
					range: weapon.Range > 0 ? weapon.Range : attack.Range,
					cooldown: weapon.AttackCooldown > 0 ? weapon.AttackCooldown : attack.Cooldown,
					currentCooldown: attack.CurrentCooldown,
					damageVariance: attack.DamageVariance,
					damageType: string.IsNullOrEmpty(weapon.DamageType) ? attack.DamageType : weapon.DamageType,
					flatArmorPenetration: attack.FlatArmorPenetration,
					percentArmorPenetration: attack.PercentArmorPenetration,
					critChance: attack.CritChance,
					critMultiplier: attack.CritMultiplier,
					splashType: attack.SplashType,
					splashInnerRadius: attack.SplashInnerRadius,
					splashMediumRadius: attack.SplashMediumRadius,
					splashOuterRadius: attack.SplashOuterRadius,
					splashInnerRatio: attack.SplashInnerRatio,
					splashMediumRatio: attack.SplashMediumRatio,
					splashOuterRatio: attack.SplashOuterRatio,
					friendlyFire: attack.FriendlyFire));
			}
		}

		if (slots.Count > 0) World.SetOrAdd(entity, new Weapons(slots));
	}

	private const string ItemSourcePrefix = "item:";
}
