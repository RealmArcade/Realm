using Realm.Ecs;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Stats;
using Realm.Ecs.Services;
using Realm.Shared.Maps;
using Realm.Shared.Metadata;

namespace Realm.Shared.Simulation;

/// <summary>
///     Puts items on units and takes them off again, applying everything an item hands over: the abilities it grants,
///     the upgrades it carries and the weapons it swings. It reads the map's own catalog rather than a process registry,
///     so the authority and any other process that equips an item arrive at the same stats for it.
/// </summary>
public class ItemGrantService
{
	/// <summary>How many distinct items a unit can carry at once.</summary>
	public const int CarriedItemLimit = 6;

	private readonly SimulationContext _context;
	private readonly DefinitionCatalog _catalog;

	public ItemGrantService(SimulationContext context, DefinitionCatalog catalog)
	{
		_context = context;
		_catalog = catalog;
	}

	public EcsWorld World => _context.World;

	/// <summary>Whether a unit has room for an item, which is what decides if it can pick one up.</summary>
	public bool CanCarry(Entity entity, string itemId)
	{
		if (!World.IsAlive(entity) || string.IsNullOrWhiteSpace(itemId)) return false;
		if (!World.Has<Inventory>(entity)) return true;

		ref Inventory inventory = ref World.Get<Inventory>(entity);
		return inventory.Count < CarriedItemLimit || inventory.HasItem(itemId);
	}

	/// <summary>
	///     Gives a unit an item and everything the item grants, stacking onto a copy it already carries. The inventory
	///     refuses an item it has no slot for, so the attempt is read back rather than assumed.
	/// </summary>
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

	/// <summary>Takes an item off a unit and withdraws everything the item was handing over.</summary>
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

	/// <summary>The items a unit carries, in the order it picked them up.</summary>
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

	/// <summary>
	///     Changes how many charges of an item a unit carries, which is what a consumable being spent does. Dropping to
	///     nothing takes the item off the unit and withdraws what it was granting.
	/// </summary>
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

	/// <summary>How many charges of one item a unit carries, read without touching the world's storage.</summary>
	private int CarriedCharges(Entity entity, string itemId)
	{
		if (!World.IsAlive(entity) || !World.Has<Inventory>(entity)) return 0;

		return World.Get<Inventory>(entity).GetItemCount(itemId);
	}

	/// <summary>
	///     Writes a charge count into a unit's inventory and reports whether it stuck: an inventory with no slot left
	///     silently refuses an item it does not already carry, so the write has to be read back to be trusted.
	/// </summary>
	private bool WriteCharges(Entity entity, string itemId, int charges)
	{
		ref Inventory inventory = ref World.Get<Inventory>(entity);
		inventory.SetItemCount(itemId, charges);

		return inventory.GetItemCount(itemId) == charges;
	}

	/// <summary>
	///     Applies what an item grants without writing it into an inventory, which is what a caller that already recorded
	///     the item does to hand over its effects. An upgrade an item carries is applied once per copy the unit holds.
	/// </summary>
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

	/// <summary>Drops the stat changes an item's upgrades were applying, leaving every other modifier in place.</summary>
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

	/// <summary>
	///     Adds one upgrade's modifiers to a unit from a named source, replacing whatever that same source applied before.
	///     Level one is the item's own grant; a higher level is a research level the match has reached.
	/// </summary>
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

	/// <summary>
	///     Re-derives everything a unit is holding at once: the abilities its template plus its items make available, the
	///     weapons those items swing, and the stats that follow from both.
	/// </summary>
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

	/// <summary>Rebuilds the weapon slots a unit fights with from the items it carries, and points its attack at them.</summary>
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
